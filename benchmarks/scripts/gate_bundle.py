"""Prepare content-bound offline bundles and launch only a previously sealed go."""
import copy
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
import shutil
import subprocess
import sys
import tarfile

import gate_session as session
import gate_time as timing
import gate_schedule as schedules
from gate_fly_guard import clean_run_folder

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
        if row['noise']:
            noise = row['noise']
            timing.require(noise.get('design') == 'long' and noise.get('machine') == 'performance-8x' and 0 <= noise['band'] < .05, 'Noise record provenance or band mismatch')
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
    for row in selected:
        row['corpusId'] = gate.corpus_identity(row, local)
        for side in row['sides'].values():
            path = Path('clients') / schedules.build_id(side)
            side.update(client=(path / 'ReplayClient.dll').as_posix(), harness=(path / 'Chrysalis.Benchmarks.dll').as_posix())
        row['job'] = dict(kind='time', replay=row['replay'], dotnet='dotnet/dotnet',
                          server='scripts/n2n_replay_server.py', capture='corpora/capture',
                          filter=row.get('microFilter', row['id']), microInvocations=MICRO_PROBE_INVOCATIONS, environment=dict(CHRYSALIS_CORPORA='corpora',
                          CHRYSALIS_BENCHMARK_DATA=fixture_relative.as_posix(), P1_RANGE='corpora/range.json', DOTNET_TieredPGO='1'))
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
    plans = timing.size_sessions(selected, config.get('_mode', 'change'), ratios=ratios,
                                 option=config['time'].get('option', 2), budget=budget)
    # A missing micro noise record schedules a one-time baseline-only session,
    # fully reserved before go, followed by the ordinary RC micro session.
    for plan in list(plans):
        if config.get('_mode') == 'release' and plan['design'] == 'micro' and any(not row.get('noise') for row in plan['rows']):
            band = copy.deepcopy(plan)
            band.update(design='micro-band', repeatAllowed=False, limitWithRepeatUsd=band['limitUsd'])
            for row in band['rows']:
                row['bPairs'] = 0
                row['sides']['candidate'] = copy.deepcopy(row['sides']['baseline'])
            band['rawSeconds'] = sum(2 * row['chargeSeconds'] * row['aPairs'] for row in band['rows'])
            band['destroyGuardSeconds'] = band['estimatedSeconds'] = math.ceil(1.15 * (720 + band['rawSeconds']))
            band['measurementCutoffSeconds'] = band['destroyGuardSeconds'] - 345
            band['limitUsd'] = band['limitWithRepeatUsd'] = timing.price(band['destroyGuardSeconds'])
            plans.insert(plans.index(plan), band)
    timing.require(sum(plan['limitWithRepeatUsd'] for plan in plans) <= budget, 'Micro band plus all repeats exceeds budget')
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


def archive_payload(note, plan=None, *, repeat=False):
    invalid = None
    for name in ('session-invalid.json', 'diagnostic-only.json', 'cleanup-failed.json'):
        if (note / name).exists():
            invalid = 'session invalid: ' + name
    proof = note / 'cleanup-proof.json'
    if not proof.exists() or not json.loads(proof.read_text()).get('ourAppAbsent'):
        invalid = 'session invalid: no own-app absence proof'
    pairs, startup = [], dict(available=False, reason='No session-start observation')
    archive = note / 'results.tar.gz'
    if archive.exists():
        with tarfile.open(archive) as stream:
            try:
                status = json.load(stream.extractfile('results/session-status.json'))
                if status.get('completedAllDeclaredRounds') is not True or status.get('exitCode') != 0:
                    invalid = 'session invalid: incomplete/budget-stopped session'
                if plan is not None and 'results/members.json' in stream.getnames():
                    pairs = schedules.restore_pairs(plan, json.load(stream.extractfile('results/members.json')), repeat=repeat)
                else:
                    pairs = json.load(stream.extractfile('results/pairs.json'))
                startup = json.load(stream.extractfile('results/session-start.json')) if 'results/session-start.json' in stream.getnames() else dict(available=False, reason='Legacy fixture')
            except (KeyError, TypeError, ValueError):
                invalid = 'session invalid: missing completion or pairs'
    else:
        invalid = 'session invalid: no results'
    context = {name: json.loads((note / name).read_text()) for name in
               ('neighbours-start.json', 'neighbours-before-fetch.json', 'duration-cost.json') if (note / name).exists()}
    context['session'] = note.name
    context['members'] = [dict(id=pair[side].get('id'), role=side, phase=pair['phase'], row=pair['id'], predecessorBuild=pair[side].get('predecessorBuild'), startedUtc=pair[side].get('startedUtc'), endedUtc=pair[side].get('endedUtc')) for pair in pairs for side in ('baseline', 'candidate')]
    return dict(pairs=pairs, invalid=invalid, context=context, startup=startup)


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
    reserved = sum(plan['limitWithRepeatUsd'] for plan in seal['timePlans'])
    timing.require(reserved <= min(gate.remaining_budget(seal), gate.remaining_budget(local)), 'Reserved sessions exceed remaining budget')
    return local


def checked_fly_go(directory, go):
    import gate
    timing.require(gate.digest(directory / 'seal.json') == go, 'Go differs from seal')
    seal = gate.read_seal(directory)
    timing.require(seal.get('timePlans'), 'Seal has no timing plans')
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
    host = directory / 'input/fly'
    index = []
    refusal_status = 0
    (directory / 'time').mkdir(exist_ok=True)
    for number, plan in enumerate(seal['timePlans']):
        band_invalid = False
        for phase in ('first', 'repeat'):
            selected = plan['rows']
            if phase == 'repeat':
                current, _ = timing.read_results(directory, seal)
                selected = [row for row in selected if current[row['id']]['status'] == 'FAIL' and row.get('rowClass') != 'recorded-only']
                if not selected or not plan['repeatAllowed'] or any(item.get('invalid') for item in [payload]):
                    break
            # A repeat is a deterministic subset of the sealed archive, not a new go.
            relative = f'input/session-{number}.tar.gz'
            if phase == 'repeat':
                # Same sealed original bundle; runner consumes an explicit fail list.
                # Its hash is verified on the VM; no source or count can change.
                relative = f'input/repeat-{number}.tar.gz'
                make_repeat(directory, seal, number, selected, relative)
            app = 'chrysalis-gate-' + dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dt%H%M%S') + '-' + secrets.token_hex(4)
            env = session.environment({})
            env.update(CHRYSALIS_GATE_LOCAL=os.environ['CHRYSALIS_GATE_LOCAL'], GATE_BUNDLE=relative)
            first_launch = number == 0 and phase == 'first'
            if first_launch:
                env['GATE_FIRST_GO'] = go
            with (directory / 'time' / f'{number}-{phase}-launcher.log').open('w') as log:
                status = owned_call(['bash', str(host / 'gate_fly.sh'), '--approved', app, str(directory / 'seal.json')],
                                         env=env, pass_fds=(9,), stdout=log, stderr=subprocess.STDOUT)
            if first_launch and not (directory / 'time/launch-started.json').is_file():
                return status or 2  # Local preflight did not consume the GO.
            note = directory / 'time/runs' / app
            if (note / 'cleanup-failed.json').exists():
                print('CLEANUP FAILED: ' + app + '; recovery retains the lock until absence proof; coordinator action required', file=sys.stderr)
            payload = archive_payload(note, plan, repeat=phase == 'repeat')
            if status == 4:
                refusal_status = status
            if status:
                payload['invalid'] = payload['invalid'] or 'session invalid: launcher failure'
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
    import gate
    import re
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
    current, _ = timing.read_results(directory, seal)
    selected = [row['id'] for row in original_plan['rows'] if current[row['id']]['status'] == 'FAIL' and row.get('rowClass') != 'recorded-only']
    timing.require(selected, 'No failed rows to repeat')
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
        good = timing.run(root, plan, root / 'results')
        verify(root)
        raise SystemExit(0 if good else 2)
