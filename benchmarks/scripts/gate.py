#!/usr/bin/env python3
"""One sealed performance gate: PC bytes and explicitly approved Fly timings."""
import argparse
import datetime as dt
import fnmatch
import hashlib
import io
import json
import math
import os
import re
from pathlib import Path
import shutil
import statistics as st
import subprocess
import sys
import tarfile
import time

import gate_session as session
import gate_stats as stats
import run_benchmarks as runner

REPO = Path(__file__).resolve().parents[2]
GATE = REPO / 'benchmarks/gate'
PIN = '7c45d47c87c63520cea84212dbd01ecc8d372ac3'
LOCAL_KEYS = ('corporaRoot', 'pinnedBaseline', 'flyOrg', 'flyTokenFile', 'flyLock')


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def local_config():
    path = os.environ.get('CHRYSALIS_GATE_LOCAL')
    if not path or not Path(path).is_file():
        raise ValueError('Missing local keys: ' + ', '.join(LOCAL_KEYS))
    try:
        result = json.loads(Path(path).read_text())
    except (ValueError, OSError):
        raise ValueError('Invalid local configuration') from None
    missing = [key for key in LOCAL_KEYS if not isinstance(result.get(key), str) or not result[key]]
    if missing:
        raise ValueError('Missing local keys: ' + ', '.join(missing))
    result['flyBudgetLeftUsd'] = remaining_budget(result)
    return result


def remaining_budget(local):
    try:
        value = float(local['flyBudgetLeftUsd'])
        if isinstance(local['flyBudgetLeftUsd'], bool) or not math.isfinite(value) or value < 0:
            raise ValueError
        return value
    except (KeyError, TypeError, ValueError):
        raise ValueError('Missing or invalid local key: flyBudgetLeftUsd') from None


def git(*args, cwd=REPO):
    return subprocess.check_output(['git', '-C', str(cwd), *args],
                                   env=runner.git_environment(base=session.environment({})), text=True).strip()


def resolve_side(value, mode):
    path = Path(value).expanduser()
    if path.is_dir():
        if mode != 'change':
            raise ValueError('Working-tree sides require change mode')
        path = path.resolve()
        return dict(commit=git('rev-parse', 'HEAD', cwd=path), treeId=runner.working_tree_id(path, env=session.environment({})), working=path)
    commit = git('rev-parse', '--verify', value + '^{commit}')
    return dict(commit=commit, treeId=git('rev-parse', commit + '^{tree}'))


def export_side(info, output):
    path = output / 'src' / info['treeId']
    if not path.exists():
        path.mkdir(parents=True)
        source = info.get('working', REPO)
        data = subprocess.check_output(['git', '-C', str(source), 'archive', info['treeId']], env=runner.git_environment(base=session.environment({})))
        with tarfile.open(fileobj=io.BytesIO(data)) as archive:
            archive.extractall(path, filter='data')
    return path


def closure_hashes(path):
    return {file.name: digest(file) for file in sorted(Path(path).glob('*.dll'))}


def check_pinned(path):
    path = Path(path)
    manifest = json.loads((path / 'closure.json').read_text())
    if manifest.get('commit') != PIN:
        raise ValueError('Wrong pinned baseline commit')
    actual = closure_hashes(path)
    if actual != manifest['assemblySha256s']:
        raise ValueError('Pinned baseline DLL closure hash mismatch')
    return manifest


def heavy(command, log, *, cwd, env):
    """Exit 75 means no work started: wait, then retry the same declared work."""
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open('a') as stream:
        while True:
            code = subprocess.call(['heavy', *map(str, command)], cwd=cwd, env=env,
                                   stdout=stream, stderr=subprocess.STDOUT)
            if code != 75:
                break
            stream.write('Gate: scheduler unavailable; retrying in 120 seconds.\n')
            stream.flush()
            time.sleep(120)
    if code:
        raise RuntimeError(f'Command failed ({code}); see {log.name}')


def execute_logged(command, log, *, cwd, env):
    """A child of the one held PC slot, not another scheduler admission."""
    log.parent.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    record = dict(startUtc=dt.datetime.now(dt.timezone.utc).isoformat(), startLoad=os.getloadavg()[0])
    with log.open('w') as stream:
        code = subprocess.call(list(map(str, command)), cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT)
    record.update(exitCode=code, seconds=time.monotonic() - start,
                  endUtc=dt.datetime.now(dt.timezone.utc).isoformat(), endLoad=os.getloadavg()[0])
    session.save(Path(str(log) + '.run.json'), record)
    if code:
        raise RuntimeError(f'Command failed ({code}); see {log.name}')


def pinned_references(project, references):
    """Both executables use explicit pinned metadata, never transitive loose packages."""
    import xml.etree.ElementTree as xml
    root = xml.fromstring(project.read_text())
    for group in root.findall('ItemGroup'):
        for item in list(group.findall('ProjectReference')) + list(group.findall('PackageReference')):
            group.remove(item)
    group = xml.SubElement(root, 'ItemGroup')
    for name, path in references.items():
        item = xml.SubElement(group, 'Reference', Include=Path(name).stem)
        xml.SubElement(item, 'HintPath').text = str(path)
    project.write_text(xml.tostring(root, encoding='unicode'))


def measurement_inputs():
    harness = REPO / 'benchmarks/Chrysalis.Benchmarks'
    client = REPO / 'benchmarks/ReplayClient'
    return dict(harness={file.name: file.read_bytes() for file in harness.glob('*.cs')},
                harnessProject=(harness / 'Chrysalis.Benchmarks.csproj').read_text(),
                client={file.name: file.read_bytes() for file in client.glob('*.cs')},
                clientProject=(client / 'ReplayClient.csproj').read_text())


def stage_side(info, label, output, local, inputs=None):
    """Build one captured harness/client against either source or the pinned DLLs."""
    inputs = inputs or measurement_inputs()
    source = export_side(info, output)
    pinned = info['commit'] == PIN
    sdk = '10.0.301' if pinned else json.loads((source / 'global.json').read_text())['sdk']['version']
    tfm = 'net10.0' if pinned else 'net11.0'
    directory, harness, _ = runner.stage(label, source, output, inputs['harness'], sdk=sdk, tfm=tfm, project_snapshot=inputs['harnessProject'])
    if pinned:
        closure = check_pinned(local['pinnedBaseline'])
        pinned_references(directory / 'Chrysalis.Benchmarks.csproj',
                          {name: Path(local['pinnedBaseline']) / name for name in closure['assemblySha256s']
                           if name != 'Chrysalis.Benchmarks.dll'})
    client_dir = directory / 'client'
    client_dir.mkdir()
    for name, content in inputs['client'].items():
        (client_dir / name).write_bytes(content)
    project = inputs['clientProject'].replace('net11.0', tfm)
    project = project.replace('$(MSBuildThisFileDirectory)../Chrysalis.Benchmarks/Chrysalis.Benchmarks.csproj',
                              str(directory / 'Chrysalis.Benchmarks.csproj'))
    (client_dir / 'ReplayClient.csproj').write_text(project)
    if pinned:
        pinned_references(client_dir / 'ReplayClient.csproj',
                          {name: harness if name == 'Chrysalis.Benchmarks.dll' else Path(local['pinnedBaseline']) / name
                           for name in closure['assemblySha256s']})
    # The nested client must not enter the harness SDK's default Compile glob.
    project = directory / 'Chrysalis.Benchmarks.csproj'
    project.write_text(project.read_text().replace('</Project>', '<ItemGroup><Compile Remove="client/**/*.cs" /></ItemGroup></Project>'))
    env = session.environment(dict(NUGET_PACKAGES=str(REPO.parent / '.nuget/packages')))
    cli = [runner.DOTNET, runner.DOTNET_ROOT / 'sdk' / sdk / 'dotnet.dll']
    isolation = ['-nodeReuse:false', '--disable-build-servers', '-p:UseSharedCompilation=false']
    commands = []
    for name, project in [('harness', directory / 'Chrysalis.Benchmarks.csproj'), ('client', client_dir / 'ReplayClient.csproj')]:
        commands.append(dict(command=list(map(str, [*cli, 'restore', project, '--source', env['NUGET_PACKAGES'], *isolation])),
                             label=name + '-restore'))
        commands.append(dict(command=list(map(str, [*cli, 'build', project, '-c', 'Release', '-m:8', '-warnaserror', '--no-restore', *isolation,
                                                    *(['--no-dependencies'] if pinned or name == 'client' else [])])),
                             label=name + '-build',
                             copyClosureTo=str((directory if name == 'harness' else client_dir) / 'bin/Release' / tfm) if pinned else None))
    job = dict(commands=commands, cwd=str(directory), generated=dict(NUGET_PACKAGES=env['NUGET_PACKAGES']),
               pinnedBaseline=local['pinnedBaseline'] if pinned else None)
    job_file = output / 'build' / (label + '-staging.job.json')
    session.save(job_file, job)
    execute_logged([sys.executable, Path(__file__).resolve(), '_stage', job_file],
          output / 'build' / (label + '-staging.log'), cwd=directory, env=env)
    if 'working' in info and runner.working_tree_id(info['working'], env=session.environment({})) != info['treeId']:
        raise ValueError('Working tree changed during build')
    if pinned:
        check_pinned(local['pinnedBaseline'])
    return dict(commit=info['commit'], treeId=info['treeId'], sdk=sdk, tfm=tfm,
                runtime='10.0.9' if pinned else '11.0.0',
                harness=str(harness), client=str(client_dir / 'bin/Release' / tfm / 'ReplayClient.dll'),
                dllSha256s=closure_hashes(client_dir / 'bin/Release' / tfm))


def stage_worker(job_file):
    """Staging children inside the run's held slot, each with a timed record."""
    job_file = Path(job_file)
    job = json.loads(job_file.read_text())
    env = session.environment(job['generated'])
    closure = check_pinned(job['pinnedBaseline']) if job['pinnedBaseline'] else None
    for item in job['commands']:
        start = time.monotonic()
        metadata = dict(command=item['command'], startUtc=dt.datetime.now(dt.timezone.utc).isoformat(), startLoad=os.getloadavg()[0])
        with (job_file.parent / (job_file.stem + '-' + item['label'] + '.log')).open('w') as log:
            code = subprocess.call(item['command'], cwd=job['cwd'], env=env, stdout=log, stderr=subprocess.STDOUT)
        metadata.update(exitCode=code, seconds=time.monotonic() - start,
                        endUtc=dt.datetime.now(dt.timezone.utc).isoformat(), endLoad=os.getloadavg()[0])
        session.save(job_file.parent / (job_file.stem + '-' + item['label'] + '.run.json'), metadata)
        if code:
            raise RuntimeError('Staging child failed: ' + item['label'])
        if item.get('copyClosureTo'):
            for name, expected in closure['assemblySha256s'].items():
                if name != 'Chrysalis.Benchmarks.dll':
                    target = Path(item['copyClosureTo']) / name
                    shutil.copyfile(Path(job['pinnedBaseline']) / name, target)
                    if digest(target) != expected:
                        raise ValueError('Staged pinned DLL mismatch')
    if closure:
        check_pinned(job['pinnedBaseline'])


def public_side(side):
    return {key: value for key, value in side.items() if key not in ('harness', 'client')}


def cold_companion(side, candidate):
    return dict(side, runtime=candidate['runtime'])


def canonical(baseline, candidate):
    if candidate['runtime'] != '11.0.0' or candidate['tfm'] != 'net11.0':
        return False
    if baseline['commit'] == PIN:
        return baseline['runtime'] == '10.0.9'
    # Previous-alpha membership is resolved before sealing, not inferred from runtime alone.
    return baseline.get('previousAlpha', False) and baseline['runtime'] == '11.0.0'


def catalogue(config, audit):
    direct = {name: ('D39' if row['kind'] == 'exact' else 'range') for name, row in audit['rows'].items()}
    direct.update({name: declaration['rule'] if isinstance(declaration, dict) else declaration for name, declaration in config['direct'].items()})
    # These six rows were frozen together. A subset must still run that filter.
    ranges = ','.join(name for name, row in audit['rows'].items() if row['kind'] == 'range')
    rows = [dict(id=name, kind='direct', rule=rule,
                 **(dict(processFilter=ranges if name in audit['rows'] else name) if rule == 'range' else {}))
            for name, rule in direct.items()]
    result = (rows + [dict(id=f'replay/{case}/{window}', kind='replay', rule='replay', replay=case, window=window)
                    for case in config['replays'] for window in ('steady', 'cold')] +
            [dict(id=name, kind='time', rule='time') for name in config['time']['required']])
    for row in result:
        declaration = config['direct'].get(row['id'])
        if row['kind'] == 'direct' and isinstance(declaration, dict):
            row.update(declaration)
        if row['id'] in config.get('recorded', {}):
            row.update(rowClass='recorded-only', explanation=config['recorded'][row['id']])
    return result


def corpus_identity(row, local):
    paths = [REPO / 'benchmarks/Chrysalis.Benchmarks/sha256.json']
    if row.get('replay') == 'tcp':
        root = Path(local['corporaRoot'])
        paths += [root / 'range.json', root / 'capture/sha256.json']
    return hashlib.sha256(encoded([digest(path) for path in paths])).hexdigest()


def band_window(row):
    # Old cold records remain immutable history under their old keys.
    return 'cold-first-pass-v2' if row.get('window') == 'cold' else row.get('window', 'direct')


def band_key(row, runtime, design):
    return '|'.join((row.get('replay', row['id']), design, band_window(row), runtime, row.get('corpusId', 'audit-preprod')))


def load_band(row, runtime, design):
    key = band_key(row, runtime, design)
    matches = [(path, json.loads(path.read_text())) for path in sorted((GATE / 'bands').glob('*.json'))]
    matches = [(path, band) for path, band in matches if band['key'] == key]
    if len(matches) > 1:
        raise ValueError('Duplicate band key: ' + ', '.join(path.name for path, _ in matches))
    if not matches:
        return None
    path, band = matches[0]
    expected = '|'.join((band.get('replay') or band['row'], band['design'], band['window'], band['runtime'], band['corpusId']))
    if expected != key:
        raise ValueError('Band provenance does not match its key')
    if band.get('purpose') != 'baseline-noise' or band.get('pairs', 0) < 10:
        raise ValueError('Band lacks baseline-only ten-pair provenance')
    if band.get('processFilter') is not None and band['processFilter'] != row.get('processFilter', row['id']):
        raise ValueError('Band process composition differs from measurement')
    if row['kind'] == 'direct':
        # Immutable pre-field records used the protocol encoded in their window.
        legacy = 10000 if band['window'] in ('direct/warm10', 'readeveryfield/warm10') else 1000
        if band.get('warmupMilliseconds', legacy) != row.get('warmupMilliseconds', 1000):
            raise ValueError('Band warm-up differs from declared row')
    return dict(sha256=digest(path), record=band)


def new_output():
    output = REPO / 'benchmarks/out' / ('gate-' + dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ'))
    output.mkdir(parents=True)
    return output


def make_job(row, baseline, candidate, local, *, pairs, design, scripts=None):
    scripts = scripts or REPO / 'benchmarks/scripts'
    fixtures = REPO / 'benchmarks/Chrysalis.Benchmarks'
    generated = dict(CHRYSALIS_CORPORA=local['corporaRoot'], NUGET_PACKAGES=str(REPO.parent / '.nuget/packages'),
                     CHRYSALIS_BENCHMARK_DATA=str(fixtures), P1_RANGE=str(Path(local['corporaRoot']) / 'range.json'),
                     DOTNET_TieredPGO='1')
    if row.get('warmupMilliseconds') is not None:
        generated['CHRYSALIS_DIRECT_WARMUP_MS'] = str(row['warmupMilliseconds'])
    return dict(id=row['id'], kind=row['kind'], replay=row.get('replay'), pairs=pairs,
                counts=list(session.DESIGNS[design]), root=str(REPO), dotnet=str(runner.DOTNET),
                server=str(scripts / 'n2n_replay_server.py'), sessionScript=str(scripts / 'gate_session.py'),
                capture=str(Path(local['corporaRoot']) / 'capture'), environment=generated,
                sides=dict(baseline=baseline, candidate=candidate), filter=row.get('processFilter', row.get('filter', row['id'])))


def execute_job(job, output):
    job_file = Path(str(output) + '.job.json')
    session.save(job_file, job)
    def verify():
        for side in job['sides'].values():
            client, harness = Path(side['client']).parent, Path(side['harness']).parent
            if closure_hashes(client) != side['dllSha256s']:
                raise ValueError('Staged DLL closure changed')
            expected = {name: value for name, value in side['dllSha256s'].items() if name != Path(side['client']).name}
            if harness != client and closure_hashes(harness) != expected:
                raise ValueError('Staged harness DLL closure changed')
    verify()
    try:
        execute_logged([sys.executable, job['sessionScript'], job_file, output],
              Path(str(output) + '.log'), cwd=REPO, env=session.environment(job['environment']))
    finally:
        verify()
    return json.loads((output / 'pairs.json').read_text())


def freeze_band(row, pairs, baseline, *, design, seal_id, raw_hash):
    identity = {key: baseline[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')}
    if len(pairs) != 10 or any(pair[side].get('identity') != identity for pair in pairs for side in ('baseline', 'candidate')):
        raise ValueError('A band needs exactly ten baseline-only pairs; candidate data refused')
    left, right, _ = pair_values(row, pairs, design)
    result = stats.allocation_noise_band(left, right, purpose='baseline-noise')
    return dict(result, key=band_key(row, baseline['runtime'], design),
                baselineCommit=baseline['commit'], treeId=baseline['treeId'], runtime=baseline['runtime'],
                dllClosureSha256=hashlib.sha256(encoded(baseline['dllSha256s'])).hexdigest(),
                replay=row.get('replay'), row=row['id'], design=design, window=band_window(row),
                **(dict(processFilter=row.get('processFilter', row['id']), warmupMilliseconds=row.get('warmupMilliseconds', 1000)) if row['kind'] == 'direct' else {}),
                corpusId=row.get('corpusId', 'audit-preprod'), sealId=seal_id, rawSha256=raw_hash,
                createdUtc=dt.datetime.now(dt.timezone.utc).isoformat())


def pair_values(row, pairs, design):
    sides = [[], []]
    batches = [[], []]
    for pair in pairs:
        for index, side in enumerate(('baseline', 'candidate')):
            data = pair[side]['data']
            if row['kind'] == 'direct':
                if row.get('processFilter') and {item['name'] for item in data} != set(row['processFilter'].split(',')):
                    raise ValueError('Direct process composition differs from frozen band/seal')
                found = [item for item in data if item['name'] == row['id']]
                if len(found) != 1 or len(found[0]['samples']) != 5:
                    raise ValueError('Missing direct row or five batches')
                if row.get('warmupMilliseconds') is not None and found[0].get('warmupMilliseconds') != row['warmupMilliseconds']:
                    raise ValueError('Direct warm-up differs from sealed protocol')
                values = [sample['bytes'] for sample in found[0]['samples']]
                if any(not math.isfinite(value) or value < 0 for value in values):
                    raise ValueError('Invalid absolute byte measurement')
                batches[index].extend(values)
            else:
                rows = data['warmupRows'][:1] if row['window'] == 'cold' else data['rows'][15:25] if design == 'short' else data['rows']
                values = [item['bytesPerBlock'] for item in rows]
            sides[index].append(st.mean(values))
    return *sides, batches


def write_seal(output, seal):
    session.save(output / 'seal.json', seal)
    seal_id = digest(output / 'seal.json')
    (output / 'seal.id').write_text(seal_id + '\n')
    return seal_id


def read_seal(output):
    path = output / 'seal.json'
    if digest(path) != (output / 'seal.id').read_text().strip():
        raise ValueError('Seal changed')
    seal = json.loads(path.read_text())
    for relative, expected in seal.get('files', {}).items():
        source = (output / relative).resolve()
        if not source.is_relative_to(output.resolve()) or not source.is_file() or digest(source) != expected:
            raise ValueError('Sealed input file changed or missing')
    for plan in seal.get('timePlans', []):
        if plan.get('scheduleVersion'):
            import gate_schedule
            gate_schedule.verify(plan)
    for key, band in seal.get('bands', {}).items():
        if band and (band['record']['key'] != key or hashlib.sha256(encoded(band['record'])).hexdigest() != band['contentSha256']):
            raise ValueError('Sealed band changed or provenance mismatch')
    return seal


def snapshot_scripts(output):
    directory = output / 'input/scripts'
    directory.mkdir(parents=True)
    for name in ('gate_session.py', 'n2n_replay_server.py'):
        shutil.copyfile(REPO / 'benchmarks/scripts' / name, directory / name)
    return directory


def band_rows(args, local):
    config = json.loads((GATE / 'gated.json').read_text())
    audit = json.loads((REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
    rows = [row for row in catalogue(config, audit) if row['kind'] != 'time' and
            any(fnmatch.fnmatchcase(row['id'], pattern) for pattern in args.set.split(','))]
    if not rows:
        raise ValueError('No band rows selected')
    if any(row['kind'] == 'direct' and row['rule'] != 'range' for row in rows):
        raise ValueError('D39 rows have no band')
    for row in rows:
        row['corpusId'] = corpus_identity(row, local)
    return rows


def band_jobs(rows, baseline):
    for runtime in (('10.0.9', '11.0.0') if baseline['commit'] == PIN else ('11.0.0',)):
        groups = {}
        for row in rows:
            designs = ['direct'] if row['kind'] == 'direct' else (['long'] if row['window'] == 'steady' else []) + (['short'] if runtime == '11.0.0' else [])
            for design in designs:
                key = (row.get('processFilter'), row.get('replay'), design, row.get('warmupMilliseconds'))
                groups.setdefault(key, []).append(row)
        for (_, _, design, _), targets in groups.items():
            yield runtime, design, targets


def band_path(key):
    return GATE / 'bands' / (hashlib.sha256(key.encode()).hexdigest()[:16] + '.json')


def preflight_bands(rows, baseline):
    for runtime, design, targets in band_jobs(rows, baseline):
        for row in targets:
            if load_band(row, runtime, design) or band_path(band_key(row, runtime, design)).exists():
                raise ValueError('Refusing an existing frozen band: ' + row['id'])


def pc_command(args):
    """Public PC admission: the entire build/measurement run holds one slot."""
    local = local_config()
    if args.command == 'band':
        preflight_bands(band_rows(args, local), resolve_side(args.side, 'band'))
    output = new_output()
    payload = output / 'command.json'
    session.save(payload, dict(args={key: str(value) if isinstance(value, Path) else value for key, value in vars(args).items()},
                               output=str(output), local={key: value for key, value in local.items() if key in ('corporaRoot', 'pinnedBaseline', 'flyPythonRoot', 'flyBudgetLeftUsd')}))
    print('Run directory:', output, flush=True)
    heavy([sys.executable, Path(__file__).resolve(), '_held', payload], output / 'command.log', cwd=REPO, env=session.environment({}))
    print((output / 'report.txt').read_text() if args.command == 'run' else 'Bands frozen', end='\n')
    return output


def band_command(args, output=None, local=None):
    local = local if local is not None else local_config()
    rows = band_rows(args, local)
    info = resolve_side(args.side, 'band')
    preflight_bands(rows, info)
    output = output or new_output()
    baseline = stage_side(info, 'baseline', output, local)
    return band_sets(args, baseline, output, local)


def band_sets(args, baseline, output, local):
    rows = band_rows(args, local)
    preflight_bands(rows, baseline)
    scripts = snapshot_scripts(output)
    corpus = Path(local['corporaRoot'])
    corpus_hashes = {path.relative_to(corpus).as_posix(): digest(path)
                     for path in (corpus / 'range.json', corpus / 'capture/sha256.json') if path.is_file()}
    seal = dict(mode='band', ruleVersion=stats.RULE_VERSION, purpose='baseline-noise', side=public_side(baseline), rows=rows,
                pairs=10, corpusHashes=corpus_hashes,
                files={file.relative_to(output).as_posix(): digest(file) for file in scripts.iterdir() if file.is_file()})
    seal_id = write_seal(output, seal)
    print('Band seal', seal_id, output, flush=True)
    try:
        for runtime, design, targets in band_jobs(rows, baseline):
            side = dict(baseline, runtime=runtime)
            row = targets[0]
            job = make_job(row, side, side, local, pairs=10, design='short' if design == 'direct' else design, scripts=scripts)
            destination = output / 'data' / f'{row.get("replay", row["id"])}-{design}-{runtime}'
            pairs = execute_job(job, destination)
            for target in targets:
                record = freeze_band(target, pairs, side, design=design, seal_id=seal_id, raw_hash=digest(destination / 'pairs.json'))
                path = band_path(record['key'])
                if path.exists():
                    raise ValueError('Refusing to overwrite a frozen band')
                session.save(path, record)
            print('Frozen', len(targets), 'bands', design, runtime, flush=True)
    finally:
        if baseline['commit'] == PIN:
            check_pinned(local['pinnedBaseline'])
    return output


def select_rows(args, config, audit):
    available = catalogue(config, audit)
    defaults = config['defaults'][args.mode]
    session_per_row = False
    if args.plan:
        payload = json.loads(Path(args.plan).read_text())
        session_per_row = payload.get('sessionPerRow', False) if isinstance(payload, dict) else False
        if type(session_per_row) is not bool:
            raise ValueError('sessionPerRow must be a boolean')
        selected = payload if isinstance(payload, list) else payload.get('entries') if isinstance(payload, dict) else None
        if not isinstance(selected, list):
            raise ValueError('Measurement entries must be an array')
        rows = []
        known = {row['id']: row for row in available}
        allowed = {'id', 'kind', 'replay', 'manifest', 'pairs', 'aPairs', 'chargeSeconds', 'candidate', 'rowClass', 'explanation'}
        for entry in selected:
            if not isinstance(entry, dict) or set(entry) - allowed or not isinstance(entry.get('id'), str):
                raise ValueError('Invalid measurement entry fields')
            if entry.get('rowClass') == 'recorded-only' and (not entry.get('explanation') or not isinstance(entry['explanation'], str)):
                raise ValueError('Recorded-only row needs an explanation link')
            if entry.get('rowClass') not in (None, 'gated', 'recorded-only'):
                raise ValueError('Unknown declared row class')
            original = known.get(entry['id'])
            if original and any(key in entry and original.get(key) is not None and entry[key] != original.get(key) for key in ('kind', 'replay')):
                raise ValueError('A declared entry cannot change a catalogue row class or replay')
            if original and entry.get('rowClass') == 'recorded-only' and original.get('rowClass') != 'recorded-only':
                raise ValueError('Catalogue rows cannot be removed from gating by a plan')
            row = dict(original or {}, **entry)
            if row.get('kind') not in ('direct', 'replay', 'time'):
                raise ValueError('Unknown row kind')
            if row['kind'] == 'direct' and 'rule' not in row:
                # New per-message rows are strict by definition, never assigned a band.
                row['rule'] = 'D39'
            if row['kind'] == 'replay':
                if row.get('replay') not in config['replays']:
                    raise ValueError('Unknown replay')
                row.setdefault('window', 'steady')
                row['rule'] = 'replay'
            if row['kind'] == 'time':
                if 'pairs' in entry:
                    row['bPairs'] = entry['pairs']
                if 'aPairs' in row and (type(row['aPairs']) is not int or row['aPairs'] < 0):
                    raise ValueError('A pairs must be a nonnegative integer')
            elif 'aPairs' in entry or 'chargeSeconds' in entry:
                raise ValueError('A pairs and process charges apply only to time rows')
            rows.append(row)
    else:
        patterns = args.only.split(',') if args.only else ['*']
        rows = [row for row in available if any(fnmatch.fnmatchcase(row['id'], pattern) for pattern in patterns)]
    if not rows or len({row['id'] for row in rows}) != len(rows):
        raise ValueError('No rows selected or duplicate row ids')
    if session_per_row and (args.mode != 'change' or any(
            row['kind'] != 'time' or row.get('rowClass') != 'recorded-only' for row in rows)):
        raise ValueError('sessionPerRow requires only recorded-only time rows in change mode')
    config['_sessionPerRow'] = session_per_row
    # Only the current frozen window determines the declared direct cohort.
    banded = {(record['row'], record['window']) for path in (GATE / 'bands').glob('*.json')
              for record in [json.loads(path.read_text())]
              if record['design'] == 'direct' and record['absoluteNoiseBandBytes'] > 0}
    for row in rows:
        row.setdefault('pairs', config['defaults'].get('bandedDirectPairs', defaults['directPairs']) if row['kind'] == 'direct' and (row['id'], band_window(row)) in banded else
                       defaults['directPairs'] if row['kind'] == 'direct' else
                       defaults.get('coldPairs', defaults['replayPairs']) if row.get('window') == 'cold' else
                       defaults.get('timePairs', {}).get(row['id'], config['defaults']['timePairs']) if row['kind'] == 'time' else defaults['replayPairs'])
        if not isinstance(row['pairs'], int) or isinstance(row['pairs'], bool) or row['pairs'] <= 0:
            raise ValueError('Pairs must be a positive integer')
        row['design'] = 'direct' if row['kind'] == 'direct' else 'short' if row.get('window') == 'cold' else defaults['design']
        if row.get('manifest'):
            path = Path(row['manifest'])
            if path.is_absolute() or '..' in path.parts:
                raise ValueError('Manifest must be a repository-relative path')
            raise ValueError('Manifest replay requires P4 MR-B; the current client has no manifest argument')
    return rows


def subset_reasons(seal, mode=None):
    reasons = []
    if seal['mode'] != 'release':
        reasons.append(seal['mode'] + ' mode')
    if mode and mode != seal['mode']:
        reasons.append('re-derived under a different mode')
    if not seal['canonical']:
        reasons.append('non-canonical sides')
    if {row['id'] for row in seal['rows'] if row.get('rowClass') != 'recorded-only'} != set(seal['releaseSet']):
        reasons.append(f'{len(seal["rows"])} of {len(seal["releaseSet"])} release rows declared')
    if not seal.get('timeScopeDecided'):
        reasons.append('time scope undecided')
    return reasons


def outcome(seal, results, *, mode=None):
    statuses = [results.get(row['id'], {}).get('status', 'INCONCLUSIVE') for row in seal['rows'] if row.get('rowClass') != 'recorded-only']
    if 'FAIL' in statuses:
        return 'FAIL'
    if any(status not in ('PASS', 'CLEAR') for status in statuses):
        return 'INCONCLUSIVE'
    return 'SUBSET' if subset_reasons(seal, mode) else 'RELEASE PASS'


def seal_bands(rows):
    result = {}
    for row in rows:
        if row['kind'] == 'time' or row.get('rule') == 'D39':
            continue
        runtime = row['sides']['baseline']['runtime']
        key = band_key(row, runtime, row['design'])
        band = load_band(row, runtime, row['design'])
        if band:
            band['contentSha256'] = hashlib.sha256(encoded(band['record'])).hexdigest()
        result[key] = band
        row['bandKey'] = key
    return result


def row_result(row, pairs, mode, sealed_band, design, control=None):
    if row['kind'] == 'time':
        return dict(status='INCONCLUSIVE', reason='No Fly time measurement; PC time never gates')
    if any(pair[side].get('platform') != 'pc' for pair in pairs + (control or []) for side in ('baseline', 'candidate')):
        raise ValueError('Bytes verdicts require PC data')
    try:
        left, right, batches = pair_values(row, pairs, design)
        band = sealed_band['record']['absoluteNoiseBandBytes'] if sealed_band else None
        plateaus = []
        if row['kind'] == 'replay' and row['window'] == 'steady':
            for side in ('baseline', 'candidate'):
                series = [[item['bytesPerBlock'] for item in pair[side]['data']['rows']] for pair in pairs]
                plateaus.append(stats.allocation_plateau(series, band, short=design == 'short'))
        noise = None
        if control is not None:
            aa_left, aa_right, aa_batches = pair_values(row, control, design)
            noise = dict(baseline=aa_left, candidate=aa_right, batches=aa_batches)
        result = stats.bytes_verdict(left, right, mode=mode, kind=row['rule'], band=band, batches=batches, control=noise,
                                     plateau=not plateaus or all(item['established'] for item in plateaus))
        result['plateau'] = plateaus
        if row['kind'] == 'direct':
            result['jitCounters'] = [dict(pair=pair['pair'], side=side, batches=[dict(batch=sample['batch'], start=sample['jitCompiledMethodsStart'], end=sample['jitCompiledMethodsEnd']) for item in pair[side]['data'] if item['name'] == row['id'] for sample in item['samples'] if 'jitCompiledMethodsStart' in sample and 'jitCompiledMethodsEnd' in sample]) for pair in pairs for side in ('baseline', 'candidate')]
        return result
    except (ValueError, KeyError, TypeError) as error:
        return dict(status='INCONCLUSIVE', reason='Invalid or incomplete pairs: ' + str(error))


def phase_pairs(output, index, row, phase, count):
    matches = [item for item in index if row['id'] in item['ids'] and item['phase'] == phase]
    if len(matches) > 1:
        raise ValueError('Ambiguous measurement artifact')
    if not matches:
        return []
    item = matches[0]
    path = (output / item['path']).resolve()
    if not path.is_relative_to(output.resolve()) or digest(path) != item['sha256']:
        raise ValueError('Raw data changed or is outside run directory')
    pairs = json.loads(path.read_text())
    if len(pairs) != count or [pair['pair'] for pair in pairs] != list(range(1, count + 1)):
        raise ValueError('Pair count/order differs from seal')
    for pair in pairs:
        for side in ('baseline', 'candidate'):
            spec = row['sides']['baseline' if phase == 'noise' else side]
            expected = {key: spec[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')}
            if pair[side].get('identity') != expected:
                raise ValueError('Measured side identity differs from seal')
            if row.get('processFilter'):
                measurement = dict(kind=row['kind'], counts=list(session.DESIGNS['short' if row['design'] == 'direct' else row['design']]), filter=row['processFilter'])
                if pair[side].get('measurement') != measurement:
                    raise ValueError('Measured harness counts/design/process composition differs from seal')
    return pairs


def report(output, mode=None):
    """Pure: only sealed rules/bands and content-checked raw files in output."""
    output = Path(output)
    seal = read_seal(output)
    if seal['mode'] == 'band':
        raise ValueError('Band runs are not candidate reports')
    if seal.get('ruleVersion') != stats.RULE_VERSION:
        raise ValueError('Unsupported sealed rule version')
    applied_mode = mode or seal['mode']
    index = json.loads((output / 'data/index.json').read_text()) if (output / 'data/index.json').exists() else []
    results, load = {}, []
    for row in seal['rows']:
        if row['kind'] == 'time':
            continue
        phases = [phase_pairs(output, index, row, phase, row['pairs']) for phase in ('first', 'repeat')]
        load += [value for pairs in phases for pair in pairs for side in ('baseline', 'candidate') for value in pair[side].get('load', [])]
        band = seal['bands'].get(row.get('bandKey'))
        first = row_result(row, phases[0], applied_mode, band, row['design'])
        result = first
        if phases[1]:
            if seal['mode'] != 'release' or row_result(row, phases[0], seal['mode'], band, row['design'])['status'] != 'FAIL':
                raise ValueError('Repeat was not allowed by sealed release rule')
            repeat = row_result(row, phases[1], applied_mode, band, row['design'])
            result = row_result(row, phases[0] + phases[1], applied_mode, band, row['design'])
        control = phase_pairs(output, index, row, 'noise', row['pairs'] * (2 if phases[1] else 1))
        if control:
            if row.get('rule') != 'D39' or not result.get('needsNoiseControl'):
                raise ValueError('A/A control was not allowed by the D39 precondition')
            result = row_result(row, phases[0] + phases[1], applied_mode, band, row['design'], control=control)
            load += [value for pair in control for side in ('baseline', 'candidate') for value in pair[side].get('load', [])]
        if phases[1]:
            result.update(first=first, repeat=repeat)
        result['sides'] = row['sides']
        results[row['id']] = result
    import gate_time
    timed, time_lines = gate_time.read_results(output, seal, mode=applied_mode)
    results.update(timed)
    verdict = outcome(seal, results, mode=applied_mode)
    title = f'gate {applied_mode} · ' + ('canonical' if seal['canonical'] else 'non-canonical sides')
    if applied_mode != seal['mode']:
        title += f' · re-derived under {applied_mode}; not the sealed rule'
    lines = [title, 'bytes · this PC · direct B/op, replay B/block · load ' + (f'{min(load):.2f}–{max(load):.2f}' if load else 'not measured'),
             f'{"benchmark":<45} {"rule":>8} {"baseline":>12} {"candidate":>12} {"difference [95%]":>30} {"limit":>10} verdict']
    for row in seal['rows']:
        if row['kind'] == 'time':
            continue
        result = results[row['id']]
        if row.get('rowClass') == 'recorded-only':
            lines.append(row['id'] + ' · harness artefact, explained, not gated · ' + row['explanation'])
        ci = result.get('confidence95DifferenceBytes')
        interval = f'{result["meanDifferenceBytes"]:.3f} [{ci[0]:.3f}, {ci[1]:.3f}]' if ci else '—'
        value = lambda key: f'{result[key]:.3f}' if key in result else '—'
        limit = f'{result["limit"]:.3f}' if result.get('limit') is not None else '—'
        rule = row.get('rule', row['kind']) if row['kind'] == 'direct' else row.get('window', row['kind'])
        lines.append(f'{row["id"]:<45} {rule:>8} {value("baselineMeanBytes"):>12} {value("comparisonMeanBytes"):>12} {interval:>30} {limit:>10} {result["status"]}')
        if row.get('warmupMilliseconds') is not None:
            lines.append(f'  protocol {band_window(row)}; fixed warm-up {row["warmupMilliseconds"]} ms; {row["pairs"]} pairs')
        for entry in result.get('jitCounters', []):
            if entry['batches']:
                lines.append(f'  JIT {entry["side"]} pair {entry["pair"]:>3}: ' + ', '.join(f'{item["batch"]:>2}: {item["start"]:>7}->{item["end"]:>7}' for item in entry['batches']) + ' (diagnostic only)')
        for side, info in row['sides'].items():
            lines.append(f'  {side}: {info["commit"]} tree {info["treeId"]} runtime {info["runtime"]}')
        if result.get('plateau'):
            for side, plateau in zip(('baseline', 'candidate'), result['plateau']):
                lines.append(f'  plateau {side}: ' + ('yes' if plateau['established'] else 'no'))
                for check in plateau['checks']:
                    if check['outsideBand']:
                        low, high = check['confidence95DifferenceBytes']
                        lines.append(f'    group difference [{low:.3f}, {high:.3f}] outside +/-{result["limit"]:.3f}')
        if result['status'] != 'PASS' or result.get('reason') in ('lower', 'equal (noise)'):
            lines.append('  reason: ' + result['reason'])
        if result.get('noiseControl'):
            control = result['noiseControl']
            low, high = control['confidence95DifferenceBytes']
            lines.append(f'  A/A: {control["pairs"]} pairs; difference {control["meanDifferenceBytes"]:.3f} [{low:.3f}, {high:.3f}]; half-width ratio {control["halfWidthRatio"]:.3f}; batch deviations {control["controlDeviations"]}')
        if 'repeat' in result:
            lines.append(f'  first {result["first"]["status"]}; repeat {result["repeat"]["status"]}; joint {result["status"]}')
            for phase in ('first', 'repeat'):
                previous = result[phase]
                ci = previous.get('confidence95DifferenceBytes')
                if ci:
                    lines.append(f'  {phase}: {previous["baselineMeanBytes"]:.3f} / {previous["comparisonMeanBytes"]:.3f}; difference {previous["meanDifferenceBytes"]:.3f} [{ci[0]:.3f}, {ci[1]:.3f}]')
    lines += time_lines
    lines += ['coverage · area · declared bytes / declared time']
    for area, mapping in seal['gated']['areas'].items():
        counts = [sum(any(fnmatch.fnmatchcase(row['id'], pattern) for pattern in mapping[key]) for row in seal['rows']) for key in ('bytes', 'time')]
        lines.append(f'{area:<45} {counts[0]:>12} {counts[1]:>12}')
        if mapping.get('manual'):
            lines.append('  ' + mapping['manual'])
    reasons = subset_reasons(seal, applied_mode) if verdict == 'SUBSET' else []
    lines.append(f'outcome {verdict}' + (' · ' + '; '.join(reasons) if reasons else ''))
    return dict(outcome=verdict, rows=results, text='\n'.join(lines) + '\n')


def previous_alpha(candidate):
    tags = []
    for tag in git('tag', '--list').splitlines():
        version = re.fullmatch(r'v?2\.(\d+)\.(\d+)-alpha', tag)
        if version:
            tags.append((tuple(map(int, version.groups())), tag))
    for _, tag in sorted(tags, reverse=True):
        commit = git('rev-parse', tag + '^{commit}')
        if commit != candidate and subprocess.call(['git', '-C', str(REPO), 'merge-base', '--is-ancestor', commit, candidate],
                env=runner.git_environment(base=session.environment({})), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) == 0:
            return commit
    return None


def measurement_groups(rows, counts=None):
    groups, cohort_counts = {}, {}
    for row in rows:
        if row['kind'] == 'time':
            continue
        key = (row['sides']['candidate']['commit'], row['sides']['candidate']['treeId'], row['sides']['candidate']['runtime'], row['kind'], row.get('replay'), row['design'],
               counts[row['id']] if counts else row['pairs'], row['sides']['baseline']['runtime'], row.get('processFilter'), row.get('warmupMilliseconds'))
        if row.get('rule') == 'range' and row.get('processFilter'):
            cohort = key[:6] + key[7:]
            count = counts[row['id']] if counts else row['pairs']
            if cohort_counts.setdefault(cohort, count) != count:
                raise ValueError('Shared range cohort needs one declared count')
        groups.setdefault(key, []).append(row)
    for members in groups.values():
        if members[0]['kind'] == 'direct':
            process_filter = members[0].get('processFilter') or ','.join(row['id'] for row in members)
            for row in members:
                row['processFilter'] = process_filter
    return list(groups.values())


def run_command(args, output=None, local=None):
    local = local if local is not None else local_config()
    config = json.loads((GATE / 'gated.json').read_text())
    audit = json.loads((REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
    rows = select_rows(args, config, audit)
    for row in rows:
        if row['kind'] != 'time':
            row['corpusId'] = corpus_identity(row, local)
    output = output or new_output()
    print('Run directory:', output, flush=True)
    scripts = snapshot_scripts(output)
    infos = {value: resolve_side(value, args.mode) for value in
             dict.fromkeys([args.baseline, args.candidate, *[row['candidate'] for row in rows if 'candidate' in row]])}
    inputs = measurement_inputs()
    staged = {value: stage_side(info, f'side-{number}', output, local, inputs) for number, (value, info) in enumerate(infos.items())}
    bdn = {side['dllSha256s'].get('BenchmarkDotNet.dll') for side in staged.values()}
    if None in bdn or len(bdn) != 1:
        raise ValueError('BenchmarkDotNet DLL versions differ; refusing measurements')
    baseline, candidate = staged[args.baseline], staged[args.candidate]
    baseline['previousAlpha'] = previous_alpha(candidate['commit']) == baseline['commit']
    for row in rows:
        right = staged[row.get('candidate', args.candidate)]
        left = cold_companion(baseline, right) if row.get('window') == 'cold' else baseline
        row['sides'] = dict(baseline=public_side(left), candidate=public_side(right))
    measurement_groups(rows)  # fix every process filter before sealing, including lazy A/A
    bands = seal_bands(rows)
    seal = dict(schemaVersion=1, mode=args.mode, ruleVersion=stats.RULE_VERSION, rows=rows, bands=bands,
                canonical=canonical(baseline, candidate) and all(row['sides']['candidate']['commit'] == candidate['commit'] for row in rows),
                timeScopeDecided=True, gated={key: value for key, value in config.items() if not key.startswith('_')},
                releaseSet=[row['id'] for row in catalogue(config, audit) if row.get('rowClass') != 'recorded-only'],
                files={file.relative_to(output).as_posix(): digest(file) for file in scripts.iterdir() if file.is_file()})
    import gate_bundle
    timing = gate_bundle.prepare(output, rows, staged, dict(config, _mode=args.mode), local)
    seal['files'].update(timing.pop('files', {}))
    seal.update(timing)
    seal_id = write_seal(output, seal)
    print('Seal:', seal_id, flush=True)
    for number, plan in enumerate(seal['timePlans']):
        print(f'Time session {number}: {plan["estimatedSeconds"] / 60:.1f} min, {plan["limitUsd"]:.4f} USD; with repeat {plan["limitWithRepeatUsd"]:.4f} USD', flush=True)
    index = []

    def measure(selected, phase, counts=None):
        for number, members in enumerate(measurement_groups(selected, counts)):
            row = members[0]
            name = row.get('candidate', args.candidate)
            runtime = row['sides']['baseline']['runtime']
            left = dict(baseline, runtime=runtime)
            right = left if phase == 'noise' else staged[name]
            pairs = counts[row['id']] if counts else row['pairs']
            job = make_job(row, left, right, local, pairs=pairs, design='short' if row['design'] == 'direct' else row['design'], scripts=scripts)
            dest = output / 'data' / f'{phase}-{number:03}'
            execute_job(job, dest)
            index.append(dict(ids=[item['id'] for item in members], phase=phase,
                              path=(dest / 'pairs.json').relative_to(output).as_posix(), sha256=digest(dest / 'pairs.json')))
            session.save(output / 'data/index.json', index)
    try:
        measure(rows, 'first')
        first = report(output)
        if args.mode == 'release':
            failed = [row for row in rows if first['rows'][row['id']]['status'] == 'FAIL' and row.get('rowClass') != 'recorded-only']
            measure(failed, 'repeat')
        current = report(output)
        noisy = [row for row in rows if current['rows'][row['id']].get('needsNoiseControl')]
        measure(noisy, 'noise', {row['id']: current['rows'][row['id']]['pairs'] for row in noisy})
    finally:
        if baseline['commit'] == PIN:
            check_pinned(local['pinnedBaseline'])
        result = report(output)
        session.save(output / 'report.json', {key: value for key, value in result.items() if key != 'text'})
        (output / 'report.txt').write_text(result['text'])
        print(result['text'])
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    run_parser = commands.add_parser('run')
    run_parser.add_argument('--mode', required=True, choices=('change', 'release'))
    run_parser.add_argument('--baseline', required=True)
    run_parser.add_argument('--candidate', required=True)
    subset = run_parser.add_mutually_exclusive_group()
    subset.add_argument('--only')
    subset.add_argument('--plan', type=Path)
    band_parser = commands.add_parser('band')
    band_parser.add_argument('--side', required=True)
    band_parser.add_argument('--set', required=True)
    report_parser = commands.add_parser('report')
    report_parser.add_argument('directory', type=Path)
    report_parser.add_argument('--mode', choices=('change', 'release'))
    fly_parser = commands.add_parser('fly')
    fly_parser.add_argument('--go', required=True)
    args = parser.parse_args()
    try:
        if args.command in ('band', 'run'):
            pc_command(args)
        elif args.command == 'report':
            print(report(args.directory, args.mode)['text'], end='')
        else:
            import gate_bundle
            raise SystemExit(gate_bundle.fly_command(args.go))
    except (ValueError, OSError, RuntimeError) as error:
        parser.exit(2, f'gate: {error}\n')


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '_held':
        payload = json.loads(Path(sys.argv[2]).read_text())
        args = argparse.Namespace(**payload['args'])
        command = band_command if args.command == 'band' else run_command
        command(args, output=Path(payload['output']), local=payload['local'])
    elif len(sys.argv) == 3 and sys.argv[1] == '_fly-held':
        import gate_bundle
        raise SystemExit(gate_bundle.fly_held(Path(sys.argv[2])))
    elif len(sys.argv) == 3 and sys.argv[1] == '_stage':
        stage_worker(sys.argv[2])
    else:
        main()
