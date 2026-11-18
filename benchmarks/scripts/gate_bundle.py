"""Prepare content-bound offline bundles and launch only a previously sealed go."""
import copy
import contextlib
import datetime as dt
import fcntl
import hashlib
import json
import math
import os
from pathlib import Path
import re
import secrets
import signal
import stat
import shutil
import subprocess
import sys
import tarfile
import tempfile

import gate_session as session
import gate_time as timing
import gate_schedule as schedules
from gate_fly_guard import clean_run_folder, probe_entries

IMAGE_INDEX = 'mcr.microsoft.com/dotnet/sdk@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea'
IMAGE = 'mcr.microsoft.com/dotnet/sdk@sha256:7913685c883a29dd8ba79da96172f5c6b29ed9be706ebe95935e3ef45f19c9c1'
MICRO_PROBE_INVOCATIONS = 16
HOST_FILES = ('gate.py', 'gate_time.py', 'gate_bundle.py', 'gate_fly.sh', 'gate_fly_common.sh',
              'gate_fly_watchdog.sh', 'gate_fly_guard.py', 'gate_schedule.py', 'gate_session.py', 'gate_stats.py',
              'run_benchmarks.py', 'summarize.py', 'provenance.py', 'n2n_replay_server.py')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def copy_files(source, destination, *, files=None):
    source, destination = Path(source), Path(destination)
    selected = files if files is not None else [path.relative_to(source) for path in source.rglob('*') if path.is_file()]
    for relative in selected:
        path = source / relative
        timing.require(path.name.lower() not in {'config.yml', 'id_rsa', 'id_ed25519'} and path.suffix.lower() not in {'.key', '.pem', '.p12', '.pfx'}, 'Credential-shaped bundle path; contents not read')
        timing.require(path.resolve().is_relative_to(source.resolve()), 'Escaping bundle file')
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)


def verify(root):
    root = Path(root)
    manifest = json.loads((root / 'bundle-manifest.json').read_text())
    for name, expected in manifest.items():
        path = (root / name).resolve()
        timing.require(path.is_relative_to(root.resolve()) and path.is_file() and sha(path) == expected, 'Bundle input changed: ' + name)
    return manifest


def pack(root, destination):
    clean_run_folder(root)
    files = {path.relative_to(root).as_posix(): sha(path) for path in sorted(root.rglob('*')) if path.is_file() and path.name != 'bundle-manifest.json'}
    session.save(root / 'bundle-manifest.json', files)
    with tarfile.open(destination, 'w:gz') as archive:
        for name in sorted([*files, 'bundle-manifest.json']):
            archive.add(root / name, arcname=name, recursive=False)
    return sha(destination)


def time_rows(rows, config):
    result = []
    for original in rows:
        if original['kind'] != 'time':
            continue
        row = copy.deepcopy(original)
        name = row['id'].removeprefix('time/')
        if name.startswith('micro-'):
            row.update(replay='micro', microFilter=config['time']['micro'][name.removeprefix('micro-')])
        else:
            inferred = name.split('-')[-1]
            if name.startswith(('preprod-', 'mainnet-')):
                timing.require(row.get('replay', inferred) == inferred, 'Catalogue timing replay changed')
            row.setdefault('replay', inferred)
            timing.require(row['replay'] in ('tcp', 'offline'), 'Unknown timing replay')
        row['noise'] = copy.deepcopy(config['time']['noise'].get(row['id'], config['time']['noise'].get(row['replay'])))
        if row['noise'] is not None:
            timing.require(timing.noise_band(row['noise']) is not None, 'Noise record provenance or band mismatch')
        result.append(row)
    return result


def calibrate_micro(observation, invocations):
    # Non-counted baseline sizing only; never a separate candidate calibration.
    seconds = observation['nsPerBlock'] * observation['blocks'] / 1e9
    timing.require(math.isfinite(seconds) and seconds > 0, 'Invalid micro calibration')
    return max(1, math.ceil(invocations * .01 / seconds))


def prepare(output, rows, staged, config, local):
    """Called inside run's PC heavy slot. Dry-run every process before sealing."""
    import gate
    selected = time_rows(rows, config)
    if not selected:
        return dict(timePlans=[])
    rule = timing.validate_probe_rule(config['time'].get('probe'))
    budget = gate.remaining_budget(local)
    python = local.get('flyPythonRoot')
    timing.require(python and (Path(python) / 'bin/python3.12').is_file(), 'Missing local key: flyPythonRoot (relocatable Linux Python3.12)')
    base = output / 'build/fly-input'
    base.mkdir(parents=True)
    copy_files(python, base / 'python', files=[path.relative_to(python) for path in Path(python).rglob('*') if path.is_file() and '/test/' not in path.as_posix() and '__pycache__' not in path.parts])
    # Copy only public, already-installed runtimes. No SDK/NuGet/network bootstrap.
    dotnet = gate.runner.DOTNET_ROOT
    copy_files(dotnet, base / 'dotnet', files=['dotnet'])
    for family in ('host/fxr', 'shared/Microsoft.NETCore.App'):
        for runtime in sorted({side['runtime'] for side in staged.values()}):
            copy_files(dotnet / family / runtime, base / 'dotnet' / family / runtime)
    scripts = base / 'scripts'
    copy_files(gate.REPO / 'benchmarks/scripts', scripts, files=HOST_FILES)
    fixture_relative = Path('benchmarks/Chrysalis.Benchmarks')
    fixtures = gate.REPO / fixture_relative
    fixture_hashes = json.loads((fixtures / 'sha256.json').read_text())
    # The approved manifest names ../data siblings. Keep their original layout
    # inside one common root; copy_files still rejects any escape from that root.
    copy_files(fixtures.parent, base / fixture_relative.parent,
               files=[Path(fixtures.name) / name for name in ['sha256.json', *fixture_hashes]])
    if any(row['replay'] == 'tcp' for row in selected):
        corpus = Path(local['corporaRoot'])
        fingerprints = json.loads((corpus / 'capture/sha256.json').read_text())
        payload = ('capture.json', 'server-to-client.bin', 'client-to-server.bin')
        timing.require(all(sha(corpus / 'capture' / name) == fingerprints[name] for name in payload), 'Offline capture changed')
        copy_files(corpus, base / 'corpora', files=['range.json', *[Path('capture') / name for name in payload]])
        # Derive a payload-only manifest; never upload incidental host/node logs.
        session.save(base / 'corpora/capture/sha256.json', {name: fingerprints[name] for name in payload})
    installed = set()
    for side in staged.values():
        relative = Path('clients') / schedules.build_id(side)
        directory = Path(side['client']).parent
        # Each staged client closure contains the current shared harness and all DLLs.
        if relative not in installed:
            copy_files(directory, base / relative, files=[path.name for path in directory.iterdir() if path.is_file() and path.suffix in ('.dll', '.json', '.so')])
            installed.add(relative)
        timing.require(gate.closure_hashes(base / relative) == side['dllSha256s'], 'Staged every-DLL identity differs')
    def replay_job(replay, filter):
        return dict(kind='time', replay=replay, dotnet='dotnet/dotnet', server='scripts/n2n_replay_server.py',
                    capture='corpora/capture', filter=filter, microInvocations=MICRO_PROBE_INVOCATIONS,
                    environment=dict(CHRYSALIS_CORPORA='corpora', CHRYSALIS_BENCHMARK_DATA=fixture_relative.as_posix(),
                                     P1_RANGE='corpora/range.json', DOTNET_TieredPGO='1'))

    for row in selected:
        row['corpusId'] = gate.corpus_identity(row, local)
        for side in row['sides'].values():
            path = Path('clients') / schedules.build_id(side)
            side.update(client=(path / 'ReplayClient.dll').as_posix(), harness=(path / 'Chrysalis.Benchmarks.dll').as_posix())
        row['job'] = replay_job(row['replay'], row.get('microFilter', row['id']))
    # Same session runner, exactly1+1 passes per side, no fake timing claims.
    dry = dict(rows=[dict(row, aPairs=0, bPairs=1, chargeSeconds=0) for row in selected if row['replay'] != 'micro'], counts=[1, 1], measurementCutoffSeconds=6600)
    schedules.prepare(dry)
    session.save(base / 'plan.json', schedules.worker_plan(dry))
    pack(base, output / 'build/dry-input.tar.gz')
    verify(base)
    destination = output / 'build/fly-dry'
    timing.require(not dry['rows'] or timing.run(base, schedules.worker_plan(dry), destination, dry=True), 'PC bundle dry run failed; see build/fly-dry')
    verify(base)
    dry_pairs = schedules.restore_pairs(dry, json.loads((destination / 'members.json').read_text())) if dry['rows'] else []
    session.save(output / 'input/dry-schedule.json', dry)
    micros = [row for row in selected if row['replay'] == 'micro']
    if micros:
        for row in micros:
            spec = row['sides']['baseline']
            job = dict(row['job'], root=str(base), counts=[1,1], timeout=480)
            for key in ('dotnet','server','capture'): job[key] = str(base / job[key])
            job['environment'] = {key:str(base/value) if key in ('CHRYSALIS_CORPORA','CHRYSALIS_BENCHMARK_DATA','P1_RANGE') else value for key,value in job['environment'].items()}
            actual = dict(spec, client=str(base/spec['client']), harness=str(base/spec['harness']))
            process = session.member(actual, job, output/'build/micro-probe'/schedules.build_id(spec)/schedules.opaque(0,row['id']))
            verify(base)
            session.save(output/'input'/('micro-probe-'+schedules.opaque(0,row['id'])+'.json'),process)
            row['job']['microInvocations'] = calibrate_micro(process['data']['rows'][0], MICRO_PROBE_INVOCATIONS)
        # One baseline-only calibration fixes common work before the final dry
        # proof. Both sides then execute the identical sealed invocation count.
        micro_dry = dict(dry, rows=[dict(row, aPairs=0, bPairs=1, chargeSeconds=0) for row in micros])
        schedules.prepare(micro_dry)
        session.save(base / 'plan.json', schedules.worker_plan(micro_dry))
        pack(base, output / 'build/dry-micro-input.tar.gz')
        timing.require(timing.run(base, schedules.worker_plan(micro_dry), output / 'build/fly-dry-micro', dry=True), 'Final fixed-work micro dry run failed')
        verify(base)
        final_micro = schedules.restore_pairs(micro_dry, json.loads((output / 'build/fly-dry-micro/members.json').read_text()))
        session.save(output / 'input/dry-micro-schedule.json', micro_dry)
        dry_pairs = [pair for pair in dry_pairs if pair['id'] not in {row['id'] for row in micros}] + final_micro
    ratios = {}
    for row in selected:
        process = next(pair['baseline'] for pair in dry_pairs if pair['id'] == row['id'])
        row['expectedWork'] = {key: process['data']['rows'][0][key] for key in ('blocks', 'wireBytes') if key in process['data']['rows'][0]}
        if row['replay'] == 'micro':
            body = max(item['nsPerBlock'] * item['blocks'] / 1e9 for stage in ('rows', 'warmupRows') for item in process['data'][stage])
            ratios[row['id']] = max(2, process['seconds']) + 240 * body
    groups = [[row] for row in selected] if config.get('_sessionPerRow') else [selected]
    plans = [plan for group in groups for plan in timing.size_sessions(group, config.get('_mode', 'change'), ratios=ratios,
                                                                      option=config['time'].get('option', 2), budget=budget,
                                                                      probe=rule)]
    # A missing micro noise record schedules a one-time baseline-only session,
    # fully reserved before go, followed by the ordinary RC micro session.
    for plan in list(plans):
        if config.get('_mode') == 'release' and plan['design'] == 'micro' and any(not row.get('noise') for row in plan['rows']):
            band = copy.deepcopy(plan)
            band.update(design='micro-band', repeatAllowed=False)
            for row in band['rows']:
                row['bPairs'] = 0
                row['sides']['candidate'] = copy.deepcopy(row['sides']['baseline'])
            timing.finalize_plan(band, sum(2 * row['chargeSeconds'] * row['aPairs'] for row in band['rows']))
            plans.insert(plans.index(plan), band)
    timing.require(sum(plan['limitWithRepeatUsd'] for plan in plans) <= budget, 'Aggregate timing reservation exceeds budget')
    host = output / 'input/fly'
    copy_files(gate.REPO / 'benchmarks/scripts', host, files=HOST_FILES)
    # Remote script always archives even when bootstrap or session fails.
    (base / 'bootstrap.sh').write_text('''#!/usr/bin/env bash
set -euo pipefail
cd /opt/chrysalis
export PYTHONHOME="$PWD/python" PYTHONNOUSERSITE=1 PYTHONDONTWRITEBYTECODE=1
export LD_LIBRARY_PATH="$PWD/python/lib"
export DOTNET_ROOT="$PWD/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
"$PWD/python/bin/python3.12" scripts/gate_bundle.py verify "$PWD"
''')
    (base / 'archive-results.sh').write_text('''#!/usr/bin/env bash
set -euo pipefail
cd /opt/chrysalis
mkdir -p results
if [[ "${1:-}" == diagnostic-only ]]; then
  printf '{"diagnosticOnly":true}\\n' > results/recovery-diagnostic.json
fi
[[ ! -e session.log ]] || cp session.log results/session.log
tar -czf /tmp/chrysalis-results.tar.gz results
sha256sum /tmp/chrysalis-results.tar.gz > /tmp/chrysalis-results.sha256
''')
    (base / 'remote-session.sh').write_text('''#!/usr/bin/env bash
set +x
set -uo pipefail
bash -c 'set -euo pipefail; source /opt/chrysalis/bootstrap.sh; exec /opt/chrysalis/python/bin/python3.12 /opt/chrysalis/scripts/gate_bundle.py run /opt/chrysalis' > /opt/chrysalis/session.log 2>&1
status=$?
bash /opt/chrysalis/archive-results.sh || exit $?
exit "$status"
''')
    session.save(output / 'input/dry-pairs.json', dry_pairs)
    files = {path.relative_to(output).as_posix(): sha(path) for path in (output / 'input').glob('*.json')}
    for number, plan in enumerate(plans):
        plan['probeJob'] = replay_job('offline', '*')
        timing.validate_limits(plan)
        plan.update(mode=config.get('_mode', 'change'), ruleVersion=gate.stats.RULE_VERSION,
                    inputSha256={path.relative_to(base).as_posix(): sha(path) for path in sorted(base.rglob('*')) if path.is_file() and path.name not in ('plan.json', 'bundle-manifest.json')})
        schedules.prepare(plan)
        session.save(base / 'plan.json', schedules.worker_plan(plan))
        name = f'input/session-{number}.tar.gz'
        files[name] = pack(base, output / name)
    files.update({path.relative_to(output).as_posix(): sha(path) for path in host.iterdir() if path.is_file()})
    version = subprocess.check_output(['fly', 'version'], env=dict(session.environment({}), FLY_NO_UPDATE_CHECK='1', FLY_NO_ANALYTICS='1'),
                                      timeout=10, text=True).strip()
    return dict(timePlans=plans, flyBudgetLeftUsd=budget, flyctlVersion=version, image=IMAGE, imageIndex=IMAGE_INDEX, files=files,
                dryRun=dict(path='input/dry-pairs.json', counts=[1, 1], sha256=sha(output / 'input/dry-pairs.json')))


def note_json(note, name):
    try:
        data = json.loads((note / name).read_text())
        # Diagnostic metadata cannot poison a hashed payload with NaN or Infinity.
        json.dumps(data, allow_nan=False)
        return data if isinstance(data, dict) else None
    except (OSError, ValueError):
        return None


def archive_payload(note, plan=None, *, repeat=False, machine=None):
    invalid = None
    for name in ('session-invalid.json', 'diagnostic-only.json', 'cleanup-failed.json'):
        if (note / name).exists():
            invalid = 'session invalid: ' + name
    if (note_json(note, 'cleanup-proof.json') or {}).get('ourAppAbsent') is not True:
        invalid = 'session invalid: no own-app absence proof'
    pairs, startup, host = [], dict(available=False, reason='No session-start observation'), None
    archive = note / 'results.tar.gz'
    try:
        with tarfile.open(archive) as stream:
            names = stream.getnames()
            status = json.load(stream.extractfile('results/session-status.json'))
            host = status.get('host')
            if status.get('completedAllDeclaredRounds') is not True or status.get('exitCode') != 0:
                invalid = 'session invalid: incomplete/budget-stopped session'
            if plan is not None and 'results/members.json' in names:
                pairs = schedules.restore_pairs(plan, json.load(stream.extractfile('results/members.json')), repeat=repeat)
            elif status.get('probe') != 'rejected':
                pairs = json.load(stream.extractfile('results/pairs.json'))
            if 'results/session-start.json' in names:
                startup = json.load(stream.extractfile('results/session-start.json'))
            if host is None and 'results/host.json' in names and stream.getmember('results/host.json').isfile():
                host = json.load(stream.extractfile('results/host.json'))
            json.dumps([pairs, startup, host], allow_nan=False)
    except (AttributeError, OSError, KeyError, TypeError, ValueError, tarfile.TarError, EOFError):
        invalid = 'session invalid: missing or malformed completion or pairs' if archive.exists() else 'session invalid: no results'
        pairs, startup, host = [], dict(available=False, reason='Results could not be decoded'), None
    context = {name: note_json(note, name) for name in ('neighbours-start.json', 'neighbours-before-fetch.json', 'duration-cost.json')
               if (note / name).exists()}
    context['session'] = note.name
    context['members'] = [dict(id=pair[side].get('id'), role=side, phase=pair['phase'], row=pair['id'], predecessorBuild=pair[side].get('predecessorBuild'), startedUtc=pair[side].get('startedUtc'), endedUtc=pair[side].get('endedUtc')) for pair in pairs for side in ('baseline', 'candidate')]
    machine = machine or {}
    payload = dict(pairs=pairs, invalid=machine.get('invalid') or invalid, context=context, startup=startup)
    if isinstance(host, dict):
        payload['host'] = host
    payload.update({key: machine[key] for key in ('probe', 'probeRecords') if key in machine})
    return payload


def verified_machine(note, plan, spent, *, repeat=False, selected=None):
    """Check paid evidence before permitting any subsequent launch."""
    cost = note_json(note, 'duration-cost.json') or {}
    booked = cost.get('totalEstimateWithReserveUsd')
    booked = booked if type(booked) in (float, int) and booked >= 0 else None
    warnings = []
    if cost.get('cleanupOutstanding') is not False or booked is None:
        warnings.append('unknown booked cost or cleanup outstanding')
    if cost.get('withinSealedGuard') is False or (booked is not None and booked > timing.price(plan['destroyGuardSeconds'], timing.region_for(plan))):
        warnings.append('booked cost past machine guard')
    elif cost.get('withinSealedGuard') is not True and not warnings:
        warnings.append('unknown machine guard cost status')
    if booked is not None and spent + booked > plan['limitUsd']:
        warnings.append('booked cost exceeds session reservation')
    evidence = dict(bookedUsd=booked, costWarnings=warnings, invalid=None)
    try:
        archive = note / 'results.tar.gz'
        timing.require(archive.is_file() and (note / 'results.sha256').is_file(), 'missing results archive or hash')
        evidence['archiveSha256'] = sha(archive)
        timing.require((note / 'results.sha256').read_text().split()[:1] == [evidence['archiveSha256']], 'results archive hash differs')
        with tarfile.open(archive) as stream:
            names = stream.getnames()
            def load(name):
                path = 'results/' + name
                timing.require(names.count(path) == 1 and stream.getmember(path).isfile(), f'missing or malformed {name}')
                try:
                    return json.load(stream.extractfile(path))
                except ValueError as error:
                    raise ValueError(f'malformed {name}') from error
            status = load('session-status.json')
            timing.require(type(status.get('exitCode')) is int and status['exitCode'] == 0 and
                           'results/recovery-diagnostic.json' not in names, 'failed worker or recovery diagnostic')
            rule = plan['probe']
            ids = timing.probe_ids(rule)
            probe_entries(stream, ids)
            records = {identifier: load(f'probe/{identifier}.json') for identifier in ids}
            worker = schedules.worker_plan(plan, repeat=repeat, selected=selected)
            reference = worker['builds'][worker['probe']['build']]
            identity = {key: reference[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')}
            for record in records.values():
                timing.require(record.get('identity') == identity and record.get('platform') == 'fly', 'probe reference identity or platform differs')
                try:
                    observed = record['data']
                    timing.require(observed.get('replay') == 'offline', 'replay differs')
                    session.validate_observations(observed, 'offline', reference['runtime'], timing.PROBE_COUNTS)
                except (AttributeError, KeyError, TypeError, ValueError) as error:
                    raise ValueError(f'probe reference workload differs: {error}') from error
            probe = timing.probe_result(records, rule)
            kept = probe['decision'] == 'kept'
            members = 'results/members.json'
            reported = load('probe.json')
            matching = all(type(reported.get(key)) in (int, float) and math.isclose(reported[key], probe[key], rel_tol=1e-12, abs_tol=1e-15)
                           for key in ('cv', 'lag1'))
            timing.require(matching and dict(reported, cv=probe['cv'], lag1=probe['lag1']) == probe and status.get('probe') == probe['decision'] and
                           status.get('completedAllDeclaredRounds') is kept and
                           (names.count(members) == 1 and stream.getmember(members).isfile() if kept else members not in names),
                           'probe evidence contradicts sealed decision or members')
            if kept:
                measured = load('members.json')
                timing.require(isinstance(measured, list) and len(measured) == len(worker['members']) and
                               {record['id']: {'build': record['build']} for record in measured} == worker['members'],
                               'members differ from the full launched schedule')
            json.dumps(records, allow_nan=False)
            evidence.update(probe=probe, probeRecords=records)
            timing.require('results/host.json' in names, 'missing host fingerprint')
            host = load('host.json')
            timing.require(isinstance(host, dict) and type(host.get('processorCount')) is int and host['processorCount'] == 8 and
                           all(isinstance(host.get(key), str) and host[key].strip() for key in ('vendor_id', 'cpu family', 'model', 'stepping')) and
                           (host.get('microcode') is None or isinstance(host['microcode'], str) and host['microcode'].strip()) and
                           (host.get('flagsSha256') is None or isinstance(host['flagsSha256'], str) and re.fullmatch('[0-9a-f]{64}', host['flagsSha256'])),
                           'invalid host fingerprint')
            json.dumps(host, allow_nan=False)
            evidence['host'] = host
        timing.require(not any((note / filename).exists() for filename in ('session-invalid.json', 'diagnostic-only.json')),
                       'launcher marked results invalid or diagnostic-only')
        proof = note_json(note, 'cleanup-proof.json') or {}
        timing.require(proof.get('ourAppAbsent') is True and not (note / 'cleanup-failed.json').exists(), 'no own-app absence proof')
        timing.require(not warnings, '; '.join(warnings))
    except (AttributeError, EOFError, OSError, KeyError, TypeError, ValueError, RuntimeError, tarfile.TarError) as error:
        evidence['invalid'] = 'session invalid: ' + str(error)
    return evidence


def cleanup_notice(directory, seal):
    guards = {plan['destroyGuardSeconds'] for plan in seal['timePlans']}
    deadline = f"within {max(guards)}s of app creation (app timestamp not yet available)"
    try:
        note = sorted((Path(directory) / 'time/runs').glob('chrysalis-gate-*'))[-1]
        started = int((note / 'app-guard-start-unix.txt').read_text())
        guard = json.loads((note / 'verified-plan.json').read_text())['destroyGuardSeconds']
        if guard not in guards:
            raise ValueError('Unknown guard')
        deadline = dt.datetime.fromtimestamp(started + guard, dt.timezone.utc).isoformat()
    except (OSError, ValueError, KeyError, IndexError, TypeError):
        pass
    return 'Interrupted; cleanup is running; sealed guard deadline: ' + deadline


def owned_call(command, *, interrupt_notice=None, **kwargs):
    """Forward interrupts and retain ownership until the child cleans up."""
    child, interrupted = None, None
    def forward(number, frame):
        nonlocal interrupted
        first = interrupted is None
        interrupted = number
        if child is not None and child.poll() is None:
            try:child.send_signal(number)
            except ProcessLookupError:pass
        if first and interrupt_notice is not None:
            try:print(interrupt_notice(), file=sys.stderr, flush=True)
            except OSError:pass
    previous = {number:signal.signal(number, forward) for number in (signal.SIGINT,signal.SIGTERM,signal.SIGHUP)}
    try:
        child = subprocess.Popen(command, **kwargs)
        if interrupted is not None:
            forward(interrupted,None)
        result = child.wait()
        return 128 + interrupted if interrupted is not None else result
    finally:
        for number, handler in previous.items():signal.signal(number,handler)


def checked_go_directory(directory, out):
    directory = Path(directory).resolve()
    timing.require(directory.parent == Path(out).resolve() and re.fullmatch(r'gate-.+', directory.name),
                   'GO directory must be under benchmarks/out/gate-*')
    return directory


def retry_file(path):
    timing.require(path.resolve() == path and path.is_file(), f'Missing or non-canonical retry file: {path.name}')
    return path


def draw_record(machine, number, phase, draw, app, decision):
    return dict({key: value for key, value in machine.items() if key != 'probeRecords'},
                session=number, phase=phase, draw=draw, app=app, decision=decision)


def retry_phase(parent, item, plan, rows, phase, draws):
    import gate
    relative = f'time/0-{phase}.json'
    payload = retry_file(parent / relative).read_bytes()
    ids = [row['id'] for row in rows]
    entry = dict(session=0, phase=phase, ids=ids, path=relative,
                 sha256=hashlib.sha256(payload).hexdigest())
    timing.require(gate.encoded(item) == gate.encoded(entry), 'Retry index or timing payload changed')
    spent = 0
    reserve = timing.price(plan['destroyGuardSeconds'], timing.region_for(plan))
    timing.require(0 < len(draws) <= plan['probe']['maxDraws'], 'Missing or excessive retry draws')
    for draw, recorded in enumerate(draws, 1):
        timing.require(spent + reserve <= plan['limitUsd'], 'Retry draw exceeded session reservation')
        app = recorded['app']
        note = parent / 'time/runs' / app
        machine = verified_machine(note, plan, spent, repeat=phase == 'repeat', selected=ids)
        wanted = 'kept' if draw == len(draws) else 'rejected'
        decision = 'rejected' if machine.get('probe', {}).get('decision') == 'rejected' else 'kept'
        expected = draw_record(machine, 0, phase, draw, app, decision)
        proof = note_json(note, 'cleanup-proof.json') or {}
        timing.require(not machine['invalid'] and proof.get('ourApp') == app and
                       decision == wanted and gate.encoded(recorded) == gate.encoded(expected),
                       'Retry draw differs from verified machine evidence or lacks cleanup proof')
        spent += machine['bookedUsd']
    rebuilt = archive_payload(note, plan, repeat=phase == 'repeat', machine=machine)
    timing.require(not rebuilt['invalid'] and gate.encoded(rebuilt) == gate.encoded(json.loads(payload)),
                   'Retry payload differs from verified archive')
    return payload


def checked_retry_parent(parent, out, kind):
    import gate
    timing.require(kind in ('first', 'repeat') and not parent.is_symlink() and not out.is_symlink(),
                   'Invalid retry kind or parent path')
    parent = checked_go_directory(parent, out)
    out = parent.parent
    authority = retry_file(out / 'controls-authority.json')
    with os.fdopen(os.open(authority, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK), 'rb') as stream:
        timing.require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode), 'Retry authority must be a regular file')
        raw = stream.read()
    original = sha(retry_file(parent / 'seal.json'))
    retry_file(parent / 'seal.id')
    seal = gate.read_seal(parent)
    timing.require('retryOf' not in seal and json.loads(retry_file(parent / 'time/launch-started.json').read_bytes()) ==
                   dict(sealId=original), 'Retry needs a consumed original seal, not a retry child')
    claims = out / 'retry-claims'
    timing.require(not claims.is_symlink() and (not claims.exists() or claims.is_dir()) and
                   not os.path.lexists(claims / original), 'Retry lineage already claimed or invalid claims directory')
    for relative in seal['files']:
        path = parent / relative
        timing.require(path.is_relative_to(parent / 'input') and not Path(relative).is_absolute(), 'Retry file is not a sealed input')
        retry_file(path)
    timing.require(all(seal['files'].get(f'input/fly/{name}') == sha(Path(__file__).with_name(name))
                       for name in HOST_FILES), 'Original was not sealed with this retry-capable launcher')
    plans = seal.get('timePlans', [])
    rows = [row for row in seal['rows'] if row['kind'] == 'time' and row.get('rowClass') != 'recorded-only']
    scope = dict(originalSealId=original, mode='release', rows=[dict(id=row['id'], expected=row.get('expected')) for row in rows])
    timing.require(json.loads(raw) == scope and seal['mode'] == 'release' and len(plans) == 1 and
                   plans[0]['repeatAllowed'] and plans[0]['design'] != 'micro-band' and
                   rows and all(row['expected'] is not None for row in scope['rows']) and
                   scope['rows'] == [dict(id=row['id'], expected=row.get('expected')) for row in plans[0]['rows']
                                     if row.get('rowClass') != 'recorded-only'],
                   'Retry authority differs from original release controls')
    plan = plans[0]
    timing.validate_probe_rule(plan['probe'])
    phases = ['first'] if kind == 'first' else ['first', 'repeat']
    index = json.loads(retry_file(parent / 'time/index.json').read_bytes())
    draws = json.loads(retry_file(parent / 'time/draws.json').read_bytes())
    timing.require(isinstance(index, list) and len(index) == len(phases) and isinstance(draws, list) and
                   all(isinstance(draw, dict) and draw.get('phase') in phases and isinstance(draw.get('app'), str) for draw in draws),
                   'Invalid retry phase index or draw ledger')
    by_phase = {phase: [draw for draw in draws if draw['phase'] == phase] for phase in phases}
    apps = [draw['app'] for draw in draws]
    runs = parent / 'time/runs'
    timing.require(runs.is_dir() and runs.resolve() == runs and draws == [draw for phase in phases for draw in by_phase[phase]] and
                   len(apps) == len(set(apps)) and set(apps) == {note.name for note in runs.iterdir()} and
                   all(re.fullmatch(r'chrysalis-gate-[0-9]{8}t[0-9]{6}-[a-f0-9]{8}', app) and
                       (runs / app).is_dir() and not (runs / app).is_symlink() and
                       all(path.is_file() and not path.is_symlink() for path in (runs / app).iterdir()) for app in apps),
                   'Retry draw ledger must name every app in phase order')
    first = retry_phase(parent, index[0], plan, plan['rows'], 'first', by_phase['first'])
    classification, ids = timing.session_outcome(parent, seal, 0, first_only=True)
    binding = dict(originalSealId=original, parent=parent.name, kind=kind, authoritySha256=hashlib.sha256(raw).hexdigest())
    if kind == 'first':
        timing.require(classification == 'retry-eligible', 'First phase is not retry-eligible')
    else:
        timing.require(classification == 'repeat' and ids, 'First phase did not authorize a repeat')
        retry_phase(parent, index[1], plan, [row for row in plan['rows'] if row['id'] in ids], 'repeat', by_phase['repeat'])
        timing.require(timing.session_outcome(parent, seal, 0)[0] == 'retry-eligible', 'Repeat phase is not retry-eligible')
        binding.update(firstIndex=index[0], repeatIds=ids)
    return parent, seal, binding, first


def checked_fly_local(seal):
    import gate
    expected = seal['files'].get('input/fly-local.json')
    if expected is not None:
        path = os.environ.get('CHRYSALIS_GATE_LOCAL')
        timing.require(path and gate.digest(Path(path)) == expected, 'Fly local settings differ from seal')
    local = gate.local_config()
    timing.require(re.fullmatch(r'[a-z0-9-]+', local['flyOrg']), 'Invalid Fly organization slug')
    lock = Path(local['flyLock'])
    timing.require(lock.is_absolute() and str(lock.resolve()) == local['flyLock'], 'Fly lock path must be absolute and canonical')
    limit = 'limitUsd' if seal.get('retryOf', {}).get('kind') == 'repeat' else 'limitWithRepeatUsd'
    reserved = sum(plan[limit] for plan in seal['timePlans'])
    timing.require(reserved <= min(gate.remaining_budget(seal), gate.remaining_budget(local)), 'Reserved sessions exceed remaining budget')
    return local


def checked_fly_go(directory, go):
    import gate
    timing.require(gate.digest(directory / 'seal.json') == go, 'Go differs from seal')
    seal = gate.read_seal(directory)
    timing.require(seal.get('timePlans'), 'Seal has no timing plans')
    for number, plan in enumerate(seal['timePlans']):
        timing.validate_probe_rule(plan.get('probe'))
        priced = timing.finalize_plan(dict(plan), plan['rawSeconds'] - timing.probe_seconds(plan['probe']))
        timing.require(all(plan.get(key) == priced[key] for key in ('limitUsd', 'limitWithRepeatUsd', 'destroyGuardSeconds', 'measurementCutoffSeconds')),
                       'Invalid probe reservation or guard')
        relative = f'input/session-{number}.tar.gz'
        timing.require(relative in seal['files'], 'Missing probe session bundle')
        with tarfile.open(directory / relative) as stream:
            worker = json.load(stream.extractfile('plan.json'))
        timing.require(worker == schedules.worker_plan(plan), 'Sealed worker plan differs from host probe plan')
    return seal, checked_fly_local(seal)


def fly_command(go, directory=None):
    """Public command takes one go, holds one lock across sessions and repeats."""
    import gate
    out = gate.REPO / 'benchmarks/out'
    if directory is None:
        matches = [path.parent for path in out.glob('gate-*/seal.id') if path.read_text().strip() == go]
        timing.require(len(matches) == 1, 'Go must identify exactly one local seal')
        directory = matches[0]
    directory = checked_go_directory(directory, out)
    seal, local = checked_fly_go(directory, go)
    # Old sealed host copies also relied on this fast refusal. The held check is authoritative.
    timing.require(not (directory / 'time/launch-started.json').exists(), 'Go already consumed; never a second repeat')
    env = session.environment({})
    env.update(CHRYSALIS_GATE_LOCAL=os.environ['CHRYSALIS_GATE_LOCAL'], GATE_GO=go, GATE_GO_OUT=str(out))
    return owned_call(['bash', '-c', 'exec 9>"$1"; flock -n 9 || exit 75; exec "$2" "$3" _fly-held "$4"',
                            'gate-lock', local['flyLock'], sys.executable, str(directory / 'input/fly/gate.py'), str(directory)], env=env,
                            interrupt_notice=lambda: cleanup_notice(directory, seal))


def held_fly_go(directory):
    directory = checked_go_directory(directory, os.environ['GATE_GO_OUT'])
    go = os.environ['GATE_GO']
    seal, local = checked_fly_go(directory, go)
    timing.require(os.path.samestat(os.fstat(9), os.stat(local['flyLock'])), 'Wrong inherited Fly lock')
    fcntl.flock(9, fcntl.LOCK_EX | fcntl.LOCK_NB)
    timing.require(not (directory / 'time/launch-started.json').exists(), 'Go already consumed; never a second repeat')
    return directory, seal, go


def fly_held(directory):
    import gate
    directory, seal, go = held_fly_go(directory)
    index = []
    retry = seal.get('retryOf')
    phases = ('first', 'repeat')
    if retry is not None:
        out = directory.parent
        parent_name = retry.get('parent', '')
        timing.require(re.fullmatch(r'gate-[^/]+', parent_name), 'Invalid retry parent directory')
        _, original, binding, first = checked_retry_parent(out / parent_name, out, retry.get('kind'))
        timing.require(gate.encoded(seal) == gate.encoded(dict(original, retryOf=dict(binding, child=directory.name))),
                       'Retry authority, seal or first payload binding changed')
        if retry['kind'] == 'repeat':
            (directory / 'time').mkdir(exist_ok=True)
            (directory / 'time/0-first.json').write_bytes(first)
            index.append(binding['firstIndex'])
            session.save(directory / 'time/index.json', index)
            phases = ('repeat',)
    (directory / 'time').mkdir(exist_ok=True)
    if retry is None:
        return fly_sessions(directory, seal, go, phases, index)
    with retry_dispatch(directory, go) as dispatch:
        return fly_sessions(directory, seal, go, phases, index, dispatch=dispatch)


@contextlib.contextmanager
def retry_dispatch(directory, go):
    # The active dispatch, not a persistent GO marker, can authorize redraws.
    with tempfile.TemporaryFile(dir=directory / 'time') as dispatch:
        dispatch.write(go.encode('ascii') + secrets.token_bytes(32))
        dispatch.flush()
        yield dispatch


def fly_sessions(directory, seal, go, phases, index, *, dispatch=None):
    import gate
    host = directory / 'input/fly'
    draws = []
    first_launch = True
    refusal_status = 0
    for number, plan in enumerate(seal['timePlans']):
        band_invalid = False
        for phase in phases:
            selected = plan['rows']
            if phase == 'repeat':
                if not plan['repeatAllowed']:
                    break
                _, ids = timing.session_outcome(directory, seal, number)
                selected = [row for row in selected if row['id'] in ids]
                if not selected:
                    break
            # A repeat is a deterministic subset of the sealed archive, not a new go.
            relative = f'input/session-{number}.tar.gz'
            if phase == 'repeat':
                # Same sealed original bundle; runner consumes an explicit fail list.
                # Its hash is verified on the VM; no source or count can change.
                relative = f'input/repeat-{number}.tar.gz'
                make_repeat(directory, seal, number, selected, relative)
            rule = plan['probe']
            spent = 0
            for draw in range(1, rule['maxDraws'] + 1):
                app = 'chrysalis-gate-' + dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dt%H%M%S') + '-' + secrets.token_hex(4)
                env = session.environment({})
                env.update(CHRYSALIS_GATE_LOCAL=os.environ['CHRYSALIS_GATE_LOCAL'], GATE_BUNDLE=relative)
                if first_launch:
                    env['GATE_FIRST_GO'] = go
                suffix = f'-draw-{draw}'
                with (directory / 'time' / f'{number}-{phase}{suffix}-launcher.log').open('a') as log:
                    status = owned_call(['bash', str(host / 'gate_fly.sh'), '--approved', app, str(directory / 'seal.json')],
                                        env=env, pass_fds=(9,), stdin=dispatch, stdout=log, stderr=subprocess.STDOUT)
                if first_launch and not (directory / 'time/launch-started.json').is_file():
                    return status or 2  # Local preflight did not consume the GO.
                first_launch = False
                note = directory / 'time/runs' / app
                if (note / 'cleanup-failed.json').exists():
                    print('CLEANUP FAILED: ' + app + '; recovery retains the lock until absence proof; coordinator action required', file=sys.stderr)
                if status == 4:
                    refusal_status = status
                machine = verified_machine(note, plan, spent, repeat=phase == 'repeat', selected=[row['id'] for row in selected])
                rejected = machine.get('probe', {}).get('decision') == 'rejected'
                if status not in (0, 5):
                    detail = machine['invalid'] or 'no verified result'
                    machine['invalid'] = 'session invalid: launcher failure; ' + detail
                elif not machine['invalid'] and rejected != (status == 5):
                    machine['invalid'] = 'session invalid: launcher status contradicts probe'
                decision = 'invalid' if machine['invalid'] else 'rejected' if rejected else 'kept'
                draws.append(draw_record(machine, number, phase, draw, app, decision))
                session.save(directory / 'time/draws.json', draws)
                if decision == 'rejected':
                    spent += machine['bookedUsd']
                    if draw == rule['maxDraws']:
                        machine['invalid'] = f'session invalid: no machine kept after {draw} draws'
                    elif spent + timing.price(plan['destroyGuardSeconds'], timing.region_for(plan)) > plan['limitUsd']:
                        machine['invalid'] = 'session invalid: probe reservation exhausted'
                    else:
                        continue
                payload = archive_payload(note, plan, repeat=phase == 'repeat', machine=machine)
                break
            path = directory / 'time' / f'{number}-{phase}.json'
            session.save(path, payload)
            index.append(dict(session=number, phase=phase, ids=[row['id'] for row in selected],
                              path=path.relative_to(directory).as_posix(), sha256=sha(path)))
            session.save(directory / 'time/index.json', index)
            if payload['invalid']:
                break
            if plan['design'] == 'micro-band':
                records = {}
                for row in plan['rows']:
                    result = timing.baseline_band(row, [pair for pair in payload['pairs'] if pair['id'] == row['id']])
                    if result['status'] == 'PASS':
                        records[row['id']] = dict(band=result['band'], design='long', machine='performance-8x',
                            source=gate.digest(directory / 'seal.json'), baseline=row['sides']['baseline'], counts=plan['counts'], corpusId=row.get('corpusId'))
                session.save(directory / 'time/noise-records.json', records)
                band_invalid = len(records) != len(plan['rows'])
                break
        if payload['invalid'] or band_invalid:
            break
    report = gate.report(directory)
    session.save(directory / 'report.json', {key: value for key, value in report.items() if key != 'text'})
    (directory / 'report.txt').write_text(report['text'])
    print(report['text'])
    return refusal_status or (0 if report['outcome'] in ('RELEASE PASS', 'SUBSET') else 2)


def make_repeat(directory, seal, number, selected, relative):
    """Only the authorized failed subset changes; all other archive bytes persist."""
    import io
    source = directory / f'input/session-{number}.tar.gz'
    plan = seal['timePlans'][number]
    data = gate_encoded(schedules.worker_plan(plan, repeat=True, selected=[row['id'] for row in selected]))
    with tarfile.open(source) as original:
        manifest = json.load(original.extractfile('bundle-manifest.json'))
        manifest['plan.json'] = hashlib.sha256(data).hexdigest()
        with tarfile.open(directory / relative, 'w:gz') as repeated:
            for member in original:
                if member.name in ('plan.json', 'bundle-manifest.json'):
                    continue
                repeated.addfile(member, original.extractfile(member))
            for name, body in [('plan.json', data), ('bundle-manifest.json', gate_encoded(manifest))]:
                member = tarfile.TarInfo(name); member.size = len(body)
                repeated.addfile(member, io.BytesIO(body))


def verified_bundle(directory, seal, relative):
    """Repeat authority is the sealed rule plus the hashed first-run FAILs."""
    retry = seal.get('retryOf', {})
    repeat_only = retry.get('kind') == 'repeat'
    timing.require(not repeat_only or relative == 'input/repeat-0.tar.gz', 'Repeat retry cannot launch a first bundle')
    path = (directory / relative).resolve()
    timing.require(path.is_relative_to(directory.resolve()), 'Archive path escapes seal')
    data = path.read_bytes()
    if relative in seal['files']:
        timing.require(hashlib.sha256(data).hexdigest() == seal['files'][relative], 'Archive changed')
        return data
    match = re.fullmatch(r'input/repeat-([0-9]+)\.tar\.gz', relative)
    timing.require(match is not None and seal['mode'] == 'release', 'Unsealed archive')
    number = int(match[1])
    original_plan = seal['timePlans'][number]
    timing.require(original_plan['repeatAllowed'], 'Repeat not authorized')
    _, selected = timing.session_outcome(directory, seal, number, first_only=True)
    timing.require(selected, 'No failed rows to repeat')
    timing.require(not repeat_only or selected == retry['repeatIds'], 'Failed rows differ from sealed retry selection')
    expected = schedules.worker_plan(original_plan, repeat=True, selected=selected)
    import io
    original_path = f'input/session-{number}.tar.gz'
    timing.require(sha(directory / original_path) == seal['files'][original_path], 'Original archive changed')
    with tarfile.open(directory / original_path) as original, tarfile.open(fileobj=io.BytesIO(data)) as archive:
        original_files = {member.name: original.extractfile(member).read() for member in original if member.isfile()}
        actual = {member.name: archive.extractfile(member).read() for member in archive if member.isfile()}
        timing.require(len(actual) == len(archive.getmembers()) and set(actual) == set(original_files), 'Repeat archive members changed')
        timing.require(json.loads(actual['plan.json']) == expected, 'Repeat plan differs from failed subset')
        original_manifest = json.loads(original_files['bundle-manifest.json'])
        original_manifest['plan.json'] = hashlib.sha256(actual['plan.json']).hexdigest()
        timing.require(json.loads(actual['bundle-manifest.json']) == original_manifest, 'Repeat manifest changed')
        timing.require(all(value == original_files[name] for name, value in actual.items() if name not in ('plan.json', 'bundle-manifest.json')), 'Repeat input bytes changed')
    return data


def gate_encoded(value):
    return (json.dumps(value, indent=2, allow_nan=False) + '\n').encode()


if __name__ == '__main__':
    if sys.argv[1] == 'verify':
        verify(sys.argv[2])
    elif sys.argv[1] == 'run':
        root = Path(sys.argv[2])
        verify(root)
        plan = json.loads((root / 'plan.json').read_text())
        timing.validate_probe_rule(plan.get('probe'))
        good = timing.run(root, plan, root / 'results')
        verify(root)
        raise SystemExit(0 if good else 2)
