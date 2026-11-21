"""Opaque VM-only dry work and its independently rederived applied values."""
import copy
import math
from pathlib import Path
import tempfile

import gate_bundle as bundle
import gate_schedule as schedules
import gate_session as session
import gate_time as timing
from gate_time import require

IDENTITY = ('commit', 'treeId', 'runtime', 'dllSha256s')
CHARGE_EXCEEDED = 'Prelude process exceeds sealed charge'


def recipe(plan):
    rows = {}
    for row in plan['rows']:
        name = schedules.opaque(plan['scheduleSeed'], ['workload', row['id']])
        rows[name] = dict(builds={role: schedules.build_id(spec) for role, spec in row['sides'].items()},
                          chargeSeconds=row['chargeSeconds'])
    return dict(rows=rows, orderingProof=plan.get('orderingProof'))


def record_names(prelude):
    names = {f'{workload}-{side}.json' for workload, row in prelude['rows'].items()
             for side in ('baseline', 'candidate')}
    if prelude['orderingProof'] is not None:
        names.add('ordering.json')
    return names


def process_seconds(record):
    seconds = record['seconds']
    require(type(seconds) in (int, float) and math.isfinite(seconds) and seconds > 0, 'Invalid dry process charge')
    return seconds


def calibrated(record, charge, log=None, name=None):
    seconds = process_seconds(record)
    row, = record['data']['rows']
    invocations = bundle.calibrate_micro(row, bundle.MICRO_PROBE_INVOCATIONS)
    if log:
        log(f"{timing.STAGE['prelude']}{name} calibration seconds {seconds:.4f} invocations {invocations} allowance {charge}")
    require(seconds <= charge, 'Micro calibration exceeds sealed charge')
    return invocations


def observed(record, spec, job, counts):
    require(record['identity'] == {key: spec[key] for key in IDENTITY}, 'Prelude build identity changed')
    require(record.get('marks') == ['MEASURED_END', 'MEASURED_START'], 'Invalid prelude measurement marks')
    data = record['data']
    session.validate_observations(data, job['replay'], spec['runtime'], counts)
    if job['replay'] == 'micro':
        require(data['invocationsPerPass'] == job['microInvocations'] and data['filter'] == job['filter'],
                'Prelude micro composition changed')
    rows = data['warmupRows'] + data['rows']
    require(all(type(row.get('wireBytes')) is int and row['wireBytes'] >= 0 and type(row.get('blocks')) is int for row in rows), 'Invalid prelude wire byte count')
    return rows


def ordering(record, reference, spec, job):
    rows = observed(record, spec, job, (180, 5))
    data = record['data']
    require(data['stopwatchFrequency'] == 1_000_000_000, 'Ordering clock changed')
    require(all([row['blocks'], row['wireBytes']] == reference for row in rows),
            'Ordering proof differs from sealed fixed work')
    boundaries = [row for row in data['groupBoundaries'] if row['stage'] == 'measured']
    require(all(sum(row['delta'][counter] for row in boundaries[1:]) ==
                boundaries[-1]['cumulative'][counter] - boundaries[0]['cumulative'][counter]
                for counter in boundaries[0]['cumulative']), 'Ordering counters do not telescope')
    first = data['rows'][0]
    start = first['startTimestamp']
    boundary = start - first['elapsedSinceSeriesStartNs'] + boundaries[0]['elapsedNs']
    received, sent = (record['ordering'][key] for key in ('markReceived', 'ackSent'))
    require(received + 200_000_000 <= sent <= boundary <= start, 'Measured boundary 0 is not between delayed ACK and pass 1')


def derive(prelude, worker, raw, log=None):
    micros = {name for name in prelude['rows'] if worker['workloads'][name]['replay'] == 'micro'}
    require(set(raw) == record_names(prelude) | {name + '-calibration.json' for name in micros},
            'Missing or extra prelude evidence')
    applied = {}
    for name, row in prelude['rows'].items():
        job = worker['workloads'][name]
        specs = {role: worker['builds'][build] for role, build in row['builds'].items()}
        values = {}
        if name in micros:
            calibration = raw[name + '-calibration.json']
            observed(calibration, specs['baseline'], job, (1, 1))
            values['microInvocations'] = calibrated(calibration, row['chargeSeconds'])
            job = dict(job, **values)
        for role, spec in specs.items():
            record = raw[f'{name}-{role}.json']
            rows = observed(record, spec, job, (1, 1))
            seconds = process_seconds(record)
            projection = bundle.micro_process_seconds(seconds, rows) if name in micros else seconds
            if log:
                log(f"{timing.STAGE['prelude']}{name} {role} seconds {seconds:.4f} projection {projection:.4f} allowance {row['chargeSeconds']}")
            require(projection <= row['chargeSeconds'], CHARGE_EXCEEDED)
            fixed = {key: rows[0][key] for key in ('blocks', 'wireBytes')}
            require(all(all(entry[key] == value for key, value in fixed.items()) for entry in rows)
                    and values.setdefault('expectedWork', fixed) == fixed, 'Prelude fixed work differs across passes or sides')
        applied[name] = values
    if (reference := prelude['orderingProof']) is not None:
        probe = worker['probe']
        ordering(raw['ordering.json'], reference, worker['builds'][probe['build']], worker['workloads'][probe['workload']])
    return applied


def execute(prelude, worker, directory, member, log=None):
    raw = {}
    paths = directory / 'raw'
    paths.mkdir(parents=True, exist_ok=False)

    def record(name, build, workload, counts, **options):
        with tempfile.TemporaryDirectory(dir=directory.parent, prefix='prelude-scratch-') as scratch:
            item = member(build, workload, Path(scratch) / name, counts, **options)
        raw[name + '.json'] = item
        session.save(paths / (name + '.json'), item)
        return item

    for name, row in prelude['rows'].items():
        opts = {}
        if worker['workloads'][name]['replay'] == 'micro':
            calibration = record(name + '-calibration', row['builds']['baseline'], name, [1, 1])
            opts['invocations'] = calibrated(calibration, row['chargeSeconds'], log, name)
        for role, build in row['builds'].items():
            record(f'{name}-{role}', build, name, [1, 1], **opts)
    if prelude['orderingProof'] is not None:
        probe = worker['probe']
        record('ordering', probe['build'], probe['workload'], [180, 5], ordering_proof=True)
    applied = derive(prelude, worker, raw, log)
    session.save(directory / 'applied.json', applied)
    return applied


def read(archive, plan):
    from gate_prepare import checked_json
    prelude = recipe(plan)
    worker = schedules.worker_plan(plan)
    members = [item for item in archive if item.name.startswith('prelude/') and not item.isdir()]
    require(all(item.isfile() for item in members) and len({item.name for item in members}) == len(members),
            'Unexpected prelude result members')
    actual = {item.name: checked_json(archive.extractfile(item).read()) for item in members}
    declared = actual.pop('prelude/applied.json', None)
    raw = {Path(name).name: value for name, value in actual.items() if name == 'prelude/raw/' + Path(name).name}
    require(len(actual) == len(raw), 'Unexpected prelude raw result path')
    applied = derive(prelude, worker, raw)
    require(applied == declared, 'Applied prelude differs from raw results')
    return applied


def apply(worker, applied):
    effective = copy.deepcopy(worker)
    used = {block['workload'] for block in effective['blocks']}
    require(used <= set(applied), 'Missing applied worker values')
    for name in used:
        effective['workloads'][name].update(applied[name])
    return effective


def verify_population(plan, records, applied, *, repeat=False, selected=None):
    pairs = schedules.restore_pairs(plan, records, repeat=repeat)
    selected = {row['id'] for row in plan['rows']} if selected is None else set(selected)
    for row in plan['rows']:
        if row['id'] not in selected:
            continue
        key = schedules.opaque(plan['scheduleSeed'], ['workload', row['id']])
        chosen = [pair for pair in pairs if pair['id'] == row['id']]
        for phase in ('A', 'B'):
            if repeat and phase == 'A':
                continue
            timing.complete_population(row, chosen, phase, row['aPairs' if phase == 'A' else 'bPairs'], applied=applied[key])
