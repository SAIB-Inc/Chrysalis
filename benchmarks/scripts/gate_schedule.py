"""D68 sealed local ABBA schedule and host-only semantic index. No verdict rules."""
import copy
import hashlib
import json
import math
import statistics as st

import gate_session as session
from summarize import confidence95

VERSION = 'd68-local-abba-v1'
DERIVED = ('schedule', 'repeatSchedule', 'scheduleSeed', 'repeatSeed', 'scheduleCoreSha256', 'repeatCoreSha256',
           'scheduleCoreJson', 'repeatCoreJson')
DOMAIN = b'chrysalis-timing-shuffle-v1\0'


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def encoded(value):
    from gate import encoded as canonical
    return canonical(value)


def opaque(seed, label):
    return hashlib.sha256(encoded([seed, label])).hexdigest()[:16]


def build_id(spec):
    return hashlib.sha256(encoded({key: spec[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')})).hexdigest()[:16]


class Shuffle:
    """Portable D68 SHA256 stream, rejection sampling and Fisher–Yates."""
    def __init__(self, seed):
        self.seed = seed.to_bytes(8, 'big')
        self.counter = 0

    def below(self, bound):
        ceiling = 1 << 256
        limit = ceiling - ceiling % bound
        while True:
            value = int.from_bytes(hashlib.sha256(DOMAIN + self.seed + self.counter.to_bytes(8, 'big')).digest(), 'big')
            self.counter += 1
            if value < limit:
                return value % bound

    def shuffle(self, values):
        for index in range(len(values) - 1, 0, -1):
            other = self.below(index + 1)
            values[index], values[other] = values[other], values[index]


def phase_pairs(row, key):
    count, reserve = row[key], row.get('reservePairs', 0)
    require(type(count) is int and count >= 0 and type(reserve) is int and reserve in (0, 1), 'Invalid declared pair count')
    return count + (reserve if count else 0)


def make_schedule(plan, seed):
    """Spread row blocks, not individual pairs; an odd tail has its own slot."""
    rng = Shuffle(seed)
    result, ordinal = [], 0
    for phase, key in (('A', 'aPairs'), ('B', 'bPairs')):
        counts = [(row, total, (total + 1) // 2)
                  for row in sorted(plan['rows'], key=lambda item: item['id'].encode('utf-8'))
                  for total in (phase_pairs(row, key),)]
        largest = max((count for _, _, count in counts), default=0)
        rounds = [[] for _ in range(largest)]
        orientations = {}
        for row, total, count in counts:
            full = total // 2
            choices = [False, True] * (full // 2)
            if full % 2:
                choices.append(bool(rng.below(2)))  #0:ABBA/AB;1:BAAB/BA
            rng.shuffle(choices)
            if total % 2:
                choices.append(bool(rng.below(2)))  #0:ABBA/AB;1:BAAB/BA
            orientations[row['id']] = choices
            for block in range(1, count + 1):
                # 1+floor((j-.5)K/k), with exact integer arithmetic.
                index = ((2 * block - 1) * largest) // (2 * count)
                rounds[index].append((row, total, block))
        for round_number, blocks in enumerate(rounds, 1):
            rng.shuffle(blocks)
            for row, total, number in blocks:
                reverse = orientations[row['id']][number - 1]
                members, pairs = [], []
                for offset in range(2):
                    pair = 2 * number - 1 + offset
                    if pair > total:
                        break
                    names = ('candidate', 'baseline') if reverse ^ bool(offset) else ('baseline', 'candidate')
                    mapping = {}
                    for name in names:
                        ordinal += 1
                        identifier = opaque(seed, ['member', ordinal])
                        mapping[name] = identifier
                        members.append(identifier)
                    pairs.append(dict(pair=pair, members=mapping))
                result.append(dict(id=opaque(seed, ['block', phase, row['id'], number]), phase=phase,
                                   row=row['id'], round=round_number, members=members, pairs=pairs))
    identifiers = [value for block in result for value in block['members']]
    require(len(identifiers) == len(set(identifiers)), 'Opaque member ID collision')
    return result


def prepare(plan):
    """First64 SHA256 bits of the plan sans derived fields; repeat is presealed."""
    plan['scheduleVersion'] = VERSION
    source = {key: value for key, value in plan.items() if key not in DERIVED}
    plan['scheduleCoreJson'] = encoded(source).decode('utf-8')
    plan['scheduleCoreSha256'] = hashlib.sha256(plan['scheduleCoreJson'].encode('utf-8')).hexdigest()
    plan['scheduleSeed'] = int(plan['scheduleCoreSha256'][:16], 16)
    plan['schedule'] = make_schedule(source, plan['scheduleSeed'])
    # A separate repeat plan is declared before any observation. Failed-row
    # selection filters this schedule; it never reseeds/reorders the survivors.
    repeat = dict(source, sessionKind='repeat')
    plan['repeatCoreJson'] = encoded(repeat).decode('utf-8')
    plan['repeatCoreSha256'] = hashlib.sha256(plan['repeatCoreJson'].encode('utf-8')).hexdigest()
    plan['repeatSeed'] = int(plan['repeatCoreSha256'][:16], 16)
    plan['repeatSchedule'] = make_schedule(repeat, plan['repeatSeed']) if plan.get('repeatAllowed') else []
    return plan


def verify(plan):
    require(plan.get('scheduleVersion') == VERSION, 'Unknown sealed schedule version')
    source = {key: value for key, value in plan.items() if key not in DERIVED}
    for prefix, core, field in (('schedule', source, 'schedule'), ('repeat', dict(source, sessionKind='repeat'), 'repeatSchedule')):
        preserved = plan.get(prefix + 'CoreJson')
        require(isinstance(preserved, str), 'Missing preserved plan-core bytes')
        raw = preserved.encode('utf-8')
        digest = hashlib.sha256(raw).hexdigest()
        seed = int(digest[:16], 16)
        require(plan.get(prefix + 'CoreSha256') == digest and plan.get(prefix + 'Seed') == seed,
                'Sealed core hash or seed changed')
        require(raw == encoded(core), 'Plan differs from preserved canonical core')
        expected = make_schedule(json.loads(preserved), seed) if prefix == 'schedule' or plan.get('repeatAllowed') else []
        require(plan.get(field) == expected, 'Sealed schedule changed')


def worker_plan(plan, *, repeat=False, selected=None):
    """Derived neutral work only; logical roles/index remain in the host seal."""
    verify(plan)
    rows = {row['id']: row for row in plan['rows']}
    selected = set(rows) if selected is None else set(selected)
    require(selected <= set(rows), 'Unknown repeat row')
    blocks, members, builds, workloads = [], {}, {}, {}
    def add_build(spec):
        build = build_id(spec)
        execution = {key: copy.deepcopy(spec[key]) for key in ('commit', 'treeId', 'runtime', 'dllSha256s', 'client', 'harness') if key in spec}
        require(builds.setdefault(build, execution) == execution, 'One build has inconsistent installation metadata')
        return build

    probe = {}
    if 'probe' in plan:
        reference = next((row for row in plan['rows'] if row.get('rowClass', 'gated') == 'gated'), None)
        require(reference is not None and plan.get('probeJob', {}).get('replay') == 'offline', 'Missing gated baseline or offline workload for probe')
        workload = opaque(plan['scheduleSeed'], ['probe-workload'])
        workloads[workload] = copy.deepcopy(plan['probeJob'])
        rule = {key: plan['probe'][key] for key in ('statistic', 'limit', 'processes', 'maxDraws', 'gen0BudgetBytes')}
        probe['probe'] = dict(rule, build=add_build(reference['sides']['baseline']), workload=workload)
    for block in plan['repeatSchedule' if repeat else 'schedule']:
        if block['row'] not in selected:
            continue
        row = rows[block['row']]
        workload = opaque(plan['scheduleSeed'], ['workload', row['id']])
        job = copy.deepcopy(row.get('job', {}))
        job.pop('id', None)
        if row.get('replay') != 'micro':
            job['filter'] = '*'
        workloads[workload] = job
        for pair in block['pairs']:
            for role, identifier in pair['members'].items():
                spec = row['sides']['baseline' if block['phase'] == 'A' else role]
                members[identifier] = dict(build=add_build(spec))
        blocks.append(dict(id=block['id'], members=block['members'], workload=workload,
                           chargeSeconds=row.get('chargeSeconds', 0)))
    limits = {key: plan[key] for key in ('destroyGuardSeconds', 'measurementCutoffSeconds', 'limitUsd',
                                       'limitWithRepeatUsd', 'repeatAllowed', 'counts', 'region', 'provider') if key in plan}
    return dict(limits, scheduleVersion=VERSION, blocks=blocks, members=members, builds=builds, workloads=workloads, **probe)


def restore_pairs(plan, records, *, repeat=False):
    index = {record['id']: record for record in records}
    expected = {value for block in plan['repeatSchedule' if repeat else 'schedule'] for value in block['members']}
    require(len(index) == len(records) and set(index) <= expected, 'Duplicate or unsealed member record')
    rows = {row['id']: row for row in plan['rows']}
    result = []
    for block in plan['repeatSchedule' if repeat else 'schedule']:
        for pair in block['pairs']:
            mapping = pair['members']
            if not all(value in index for value in mapping.values()):
                continue  # incomplete pairs cannot enter inference
            value = dict(pair=pair['pair'], id=block['row'], phase=block['phase'], block=block['id'])
            order = [identifier for identifier in block['members'] if identifier in mapping.values()]
            value['orientation'] = 'AB' if order[0] == mapping['baseline'] else 'BA'
            for role, identifier in mapping.items():
                record = index[identifier]
                spec = rows[block['row']]['sides']['baseline' if block['phase'] == 'A' else role]
                require(record['build'] == build_id(spec), 'Measured build differs from sealed host index')
                value[role] = record
            result.append(value)
    return result


def block_diagnostics(pairs):
    """Geometric block contrast is information only; inference stays pair-level."""
    result = {}
    for phase in ('A', 'B'):
        grouped, slots, orientations = {}, [], {}
        for pair in pairs:
            if pair['phase'] != phase or 'block' not in pair:
                continue
            if any(pair[side]['timingRejectedReasons'] or pair[side].get('marks') != ['MEASURED_END', 'MEASURED_START'] for side in ('baseline', 'candidate')):
                continue
            means = [st.mean(item['nsPerBlock'] for item in pair[side]['data']['rows']) for side in ('baseline', 'candidate')]
            if any(not math.isfinite(value) or value <= 0 for value in means):
                continue
            ratio = means[1] / means[0]
            grouped.setdefault(pair['block'], []).append(math.log(ratio))
            orientations.setdefault(pair['orientation'], []).append(ratio)
            slots.append(ratio if pair['orientation'] == 'AB' else 1 / ratio)
        logs = [st.mean(values) for values in grouped.values() if len(values) == 2]
        entry = dict(informationOnly=True, blocks=len(logs), unmatchedPairs=sum(len(values) for values in grouped.values() if len(values) != 2),
                     orientation={key: dict(pairs=len(values), meanPairRatio=st.mean(values)) for key, values in orientations.items()},
                     meanSecondSlotRatio=st.mean(slots) if slots else None)
        if len(logs) >= 2:
            entry.update(ratio=math.exp(st.mean(logs)), confidence95=[math.exp(value) for value in confidence95(logs)], degreesOfFreedom=len(logs) - 1)
        else:
            entry['reason'] = 'Fewer than two complete admitted blocks; pair inference unchanged'
        result[phase] = entry
    return result


def startup_information(records):
    """DIAG rev2: whole-process fractions selected by start, no new probes."""
    usable = []
    for record in records:
        value = record.get('wholeProcess', {})
        if session.measuring_host(record.get('platform')) and isinstance(value.get('aggregateSteal'), (int, float)):
            fraction = value['aggregateSteal']
            if math.isfinite(fraction) and fraction >= 0:
                counts = record.get('measurement', {}).get('counts', [record['data'].get('warmupPasses'), record['data'].get('passes')])
                usable.append(dict(value, long=counts == [180, 60]))
    def summarize(population):
        if not population:
            return dict(available=False, n=0, reason='No whole-process CPU observations for this design')
        origin = min(value['startMonotonic'] for value in population)
        window = [value for value in population if origin <= value['startMonotonic'] < origin + 300]
        ordered = sorted(value['aggregateSteal'] for value in window)
        rank = .95 * (len(ordered) - 1)
        low = math.floor(rank)
        p95 = ordered[low] + (ordered[min(low + 1, len(ordered) - 1)] - ordered[low]) * (rank - low)
        return dict(available=True, n=len(window), median=st.median(ordered), p95=p95,
                    startMonotonic=origin, lastStartMonotonic=max(value['startMonotonic'] for value in window),
                    lastEndMonotonic=max(value['endMonotonic'] for value in window),
                    observationSpanSeconds=max(value['endMonotonic'] for value in window) - origin,
                    wholeSessionMedian=st.median(value['aggregateSteal'] for value in population),
                    warning='Whole-process observations selected by start; a process may end after the nominal300s window')
    return dict(informationOnly=True, requestedSeconds=300,
                firstLong=summarize([value for value in usable if value['long']]), firstAllClients=summarize(usable))
