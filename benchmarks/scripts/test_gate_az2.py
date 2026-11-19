"""AZ2 build handoff, prelude and seal-review regressions."""
import copy
import hashlib
import inspect
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
from types import SimpleNamespace
from unittest.mock import patch
import zipfile

import pytest

import gate
import gate_azure_payload
import gate_bundle as bundle
import gate_batch as batch
import gate_prelude as prelude
import gate_prepare as prepare
import gate_schedule as schedule
import gate_session as session
import gate_time as timing
from test_gate_time import probe_rule, row


MARKER = dict(schemaVersion=1, mode='build', runnerSha256='a' * 64, imageDigest='sha256:' + 'b' * 64,
              sdkSha512={'10.0.301': 'c' * 128, '11.0.100': 'd' * 128})
CAPSTONE = 'runtimes/linux-x64/native/libcapstone.so'
NATIVE_FIXTURE = 'runtimes/linux-x64/native/libfixture.so'


def deps():
    target = '.NETCoreApp,Version=v11.0'
    graph = {'Gee.External.Capstone/2.3.0': {'runtimeTargets': {CAPSTONE: dict(rid='linux-x64', assetType='native')}}}
    return dict(runtimeTarget={'name': target}, targets={target: graph}, libraries=dict.fromkeys(graph, {}))


def test_container_prepare_rejects_native_mac(tmp_path, azure, monkeypatch):
    root = tmp_path / 'root'
    monkeypatch.setattr(gate, 'REPO', root / 'project')
    monkeypatch.setattr(gate.runner, 'DOTNET_ROOT', root / 'sdk')
    monkeypatch.setattr(sys, 'platform', 'darwin')
    monkeypatch.setattr(prepare, 'checked_local', lambda **kwargs: (None, {}))
    monkeypatch.setattr(prepare, 'build', lambda *args, **kwargs: None)
    args = SimpleNamespace(build_only=True, mode='release', baseline='a', candidate='b',
                           output='benchmarks/out/container-prepared')
    with pytest.raises(ValueError, match='mbp-container'):
        prepare.command(args)


def test_container_cli_requires_canonical_guest_and_forwards_phase(tmp_path, azure, monkeypatch):
    repo = tmp_path / 'project'
    repo.mkdir()
    monkeypatch.setattr(prepare, 'CONTAINER_ROOT', tmp_path)
    monkeypatch.setattr(gate, 'REPO', repo)
    monkeypatch.setattr(sys, 'platform', 'linux')
    monkeypatch.setattr(prepare.os, 'uname', lambda: SimpleNamespace(machine='x86_64', release='fake-linux'))
    monkeypatch.chdir(repo)
    monkeypatch.delenv('WSL_DISTRO_NAME', raising=False)
    for key, path in {'DOTNET_ROOT': 'sdk/11.0.100', 'CHRYSALIS_DOTNET10_ROOT': 'sdk/10.0.301',
                      'CHRYSALIS_GATE_LOCAL': 'settings.json', 'CHRYSALIS_CONTAINER_BUILD_MARKER': 'build-marker.json',
                      'NUGET_PACKAGES': 'packages', 'CHRYSALIS_NUGET_SOURCE': 'nuget'}.items():
        monkeypatch.setenv(key, str(tmp_path / path))
    public = {key: str(tmp_path / key) for key in ('corporaRoot', 'pinnedBaseline', 'linuxDotnetRoot', 'flyPythonRoot')}
    session.save(tmp_path / 'settings.json', public)
    session.save(tmp_path / 'build-marker.json', MARKER)
    args = SimpleNamespace(build_only=True, mode='release', baseline='a', candidate='b', plan=None,
                           output='benchmarks/out/container-prepared')
    with patch.object(prepare, 'build') as build:
        for phase in ('restore', 'build'):
            monkeypatch.setenv('CHRYSALIS_CONTAINER_BUILD_PHASE', phase)
            prepare.command(args)
            build.assert_called_with(args, repo / args.output, public, marker=MARKER, phase=phase)
        monkeypatch.setenv('WSL_DISTRO_NAME', 'fake-host')
        with pytest.raises(ValueError, match='mbp-container'):
            prepare.command(args)


@pytest.mark.parametrize('pinned', [False, True])
def test_container_stage_reuses_restore_without_network_source(tmp_path, monkeypatch, pinned):
    source = tmp_path / 'source'; source.mkdir()
    (source / 'global.json').write_text('{"sdk":{"version":"11.0.100"}}')
    stage = tmp_path / 'stage'; stage.mkdir()
    (stage / 'Chrysalis.Benchmarks.csproj').write_text('<Project></Project>')
    harness = stage / 'Chrysalis.Benchmarks.dll'
    sdk = '10.0.301' if pinned else '11.0.100'
    root = tmp_path / sdk
    monkeypatch.setattr(gate.runner, 'DOTNET_ROOT', tmp_path / '11.0.100')
    monkeypatch.setenv('CHRYSALIS_DOTNET10_ROOT', str(tmp_path / '10.0.301'))
    monkeypatch.setenv('CHRYSALIS_NUGET_SOURCE', str(tmp_path / 'feed'))
    monkeypatch.setenv('NUGET_PACKAGES', str(tmp_path / 'packages'))
    calls = []
    def execute(argv, *args, **kwargs):
        gate.stage_worker(*argv[3:])
    def child(command, *, env, **kwargs):
        assert env['DOTNET_ROOT'] == str(root) and env['NUGET_PACKAGES'] == str(tmp_path / 'packages')
        assert Path(command[0]) == root / 'dotnet'
        calls.append(command)
        return 0
    info = dict(commit=gate.PIN if pinned else 'a', treeId='b')
    local = dict(pinnedBaseline='pinned')
    inputs = dict(harness={}, harnessProject='', client={}, clientProject='')
    with patch.object(gate, 'export_side', return_value=source) as export, \
         patch.object(gate.runner, 'stage', return_value=(stage, harness, None)), \
         patch.object(gate, 'check_pinned', return_value={'assemblySha256s': {}}), \
         patch.object(gate, 'pinned_references'), patch.object(gate, 'execute_logged', side_effect=execute), \
         patch.object(prepare, 'restore_pinned_native') as repair, \
         patch.object(gate.subprocess, 'call', side_effect=child):
        gate.stage_side(info, 'side', tmp_path, local, inputs, prepare_phase='restore')
        assert len(calls) == 2 and all(cmd[2] == 'restore' and cmd[cmd.index('--source') + 1] == str(tmp_path / 'feed') for cmd in calls)
        gate.stage_side(info, 'side', tmp_path, local, prepare_phase='build')
        export.assert_called_once()
        assert repair.call_count == int(pinned)
    assert len(calls) == 4 and all(cmd[2] == 'build' and '--no-restore' in cmd and '--source' not in cmd for cmd in calls[2:])


def test_legacy_prepare_refuses_lost_runtime_targets_from_real_deps():
    # Native package and its BenchmarkDotNet parent projected from the archived 1.7.14 graph.
    value = prepare.json_file(Path(__file__).parent / 'testdata/az2-legacy-native.deps.json')
    prepare.validate_deps(value, 'net10.0', 'Chrysalis.Benchmarks.deps.json')
    # The direct-reference rebuild omitted the original package runtime target.
    target = value['targets'][value['runtimeTarget']['name']]
    name = next(name for name in target if name.startswith('Gee.External.Capstone/'))
    target.pop(name)
    value['libraries'].pop(name)
    with pytest.raises(ValueError, match='does not resolve Linux-x64'):
        prepare.validate_deps(value, 'net10.0', 'ReplayClient.deps.json')


def test_restore_pinned_native_keeps_managed_deps_unchanged(tmp_path, monkeypatch):
    original = Path(__file__).parent / 'testdata/az2-legacy-native.deps.json'
    source = prepare.json_file(original)
    pinned = tmp_path / 'pinned'
    pinned.mkdir()
    (pinned / 'Chrysalis.Benchmarks.deps.json').write_bytes(original.read_bytes())
    monkeypatch.setattr(prepare, 'PINNED_DEPS_SHA256', bundle.sha(original))
    graph = source['targets'][source['runtimeTarget']['name']]
    selected = graph['Gee.External.Capstone/2.3.0']['runtimeTargets']
    client = tmp_path / 'client'
    direct = copy.deepcopy(source)
    capstone = direct['targets'][source['runtimeTarget']['name']].pop('Gee.External.Capstone/2.3.0')
    direct['targets'][source['runtimeTarget']['name']]['Gee.External.Capstone/2.3.0.0'] = {'runtime': capstone['runtime']}
    direct['libraries'].pop('Gee.External.Capstone/2.3.0')
    direct['libraries']['Gee.External.Capstone/2.3.0.0'] = {'type': 'reference'}
    harness_deps = tmp_path / 'harness' / 'Chrysalis.Benchmarks.deps.json'
    session.save(harness_deps, direct)
    session.save(client / 'ReplayClient.deps.json', direct)
    assert not (client / harness_deps.name).exists()
    dll = client / 'Gee.External.Capstone.dll'
    dll.write_bytes(b'Pinned managed bytes')
    feed = tmp_path / 'feed'
    package = feed / 'gee.external.capstone/2.3.0/gee.external.capstone.2.3.0.nupkg'
    package.parent.mkdir(parents=True)
    platform_binary = b'Linux-x64 native bytes'
    with zipfile.ZipFile(package, 'w') as archive:
        archive.writestr(CAPSTONE, platform_binary)
    monkeypatch.setattr(prepare, 'PINNED_CAPSTONE_SHA256', '0' * 64)
    with pytest.raises(ValueError, match='Pinned native Capstone digest differs'):
        prepare.restore_pinned_native(client, harness_deps, pinned, feed)
    assert not (client / CAPSTONE).exists()
    monkeypatch.setattr(prepare, 'PINNED_CAPSTONE_SHA256', hashlib.sha256(platform_binary).hexdigest())
    prepare.restore_pinned_native(client, harness_deps, pinned, feed)
    assert (client / CAPSTONE).read_bytes() == platform_binary
    assert dll.read_bytes() == b'Pinned managed bytes' and prepare.json_file(harness_deps) == direct
    for name in prepare.DEPS_FILES:
        final = prepare.json_file(client / name)
        assert prepare.validate_deps(final, 'net10.0', name) == {CAPSTONE}
        assert final['targets'][source['runtimeTarget']['name']]['Gee.External.Capstone/2.3.0.0'].pop('runtimeTargets') == selected
        assert final == direct
    # Even reattesting a different .so in all portable hashes cannot substitute the pinned asset.
    def pack_legacy(path):
        members = [*prepare.DEPS_FILES, CAPSTONE]
        hashes = {name: bundle.sha(client / name) for name in members}
        prepare.pack(client, path, members, {prepare.MARKER_FILE: bundle.gate_encoded(MARKER)})
        return hashes
    tfms = dict.fromkeys(prepare.DEPS_FILES, 'net10.0')
    good = tmp_path / 'good.tar.gz'
    prepare.verify_payload(good, MARKER, tfms, expected_hashes=pack_legacy(good))
    (client / CAPSTONE).write_bytes(b'reattested substitute')
    bad = tmp_path / 'bad.tar.gz'
    with pytest.raises(ValueError, match='Pinned native Capstone digest differs'):
        prepare.verify_payload(bad, MARKER, tfms, expected_hashes=pack_legacy(bad))


def test_unrelated_suite_modules_may_lack_capstone():
    target = '.NETCoreApp,Version=v11.0'
    graph = {'Suite/1.0.0': {}}
    module = dict(runtimeTarget={'name': target}, targets={target: graph}, libraries=dict.fromkeys(graph, {}))
    prepare.validate_deps(module, 'net11.0', 'Suite.deps.json', require_capstone=False)
    with pytest.raises(ValueError, match='native assets'):
        prepare.validate_deps(module, 'net11.0', 'ReplayClient.deps.json')


@pytest.mark.parametrize('pinned', [False, True])
def test_path_map_covers_both_builds(tmp_path, pinned):
    source = tmp_path / 'source'
    source.mkdir()
    (source / 'global.json').write_text('{"sdk":{"version":"11.0.100"}}')
    staged = tmp_path / 'staged'
    staged.mkdir()
    (staged / 'Chrysalis.Benchmarks.csproj').write_text('<Project></Project>')
    harness = staged / 'Chrysalis.Benchmarks.dll'
    with patch.object(gate, 'export_side', return_value=source), \
         patch.object(gate.runner, 'stage', return_value=(staged, harness, None)), \
         patch.object(gate, 'check_pinned', return_value={'assemblySha256s': {}}), \
         patch.object(gate, 'pinned_references'), patch.object(gate, 'execute_logged'):
        gate.stage_side(dict(commit=gate.PIN if pinned else 'a', treeId='b'), 'side', tmp_path,
                        dict(pinnedBaseline='pinned'), dict(harness={}, harnessProject='', client={}, clientProject=''))
    jobs = json.loads((tmp_path / 'build/side-staging.job.json').read_text())['commands']
    assert [item['label'] for item in jobs if f'-p:PathMap={tmp_path}=/_/' in item['command']] == ['harness-build', 'client-build']


@pytest.mark.parametrize('name', ['../../outside', '/absolute', 'a/../normalized', './a', 'C:/drive', 'a\\b'])
def test_handoff_refuses_noncanonical_paths(name):
    with pytest.raises(ValueError):
        bundle.prepared_path(name)


def test_noise_records_do_not_accept_missing_or_duplicate_rows():
    first = {'id': 'offline', 'machine': 'stub', 'design': 'long', 'sourceSeal': 'stub-seal', 'pairs': 16, 'band': .01}
    second = dict(first, id='tcp')
    assert bundle.merge_noise_records({'offline', 'tcp'}, [[first], [second]]) == {'offline': first, 'tcp': second}
    for entries in ([[first]], [[first, second], [first]]):
        with pytest.raises(ValueError):
            bundle.merge_noise_records({'offline', 'tcp'}, entries)


def observation(spec, job, counts, *, ordering=False):
    def passes(count):
        return [dict(blocks=1020 if job['replay'] != 'micro' else 16, wireBytes=9338100,
                     nsPerBlock=1_000 if job['replay'] != 'micro' else 1_000_000 if job['microInvocations'] == 16 else 625_000,
                     bytesPerBlock=0, startTimestamp=400_000_100, elapsedSinceSeriesStartNs=100, **{'pass': i})
                for i in range(1, count + 1)]
    def groups(stage, length):
        return [dict(stage=stage, passIndex=i, elapsedNs=0, delta=dict.fromkeys(session.COUNTERS, 0),
                     cumulative=dict.fromkeys(session.COUNTERS, 0)) for i in range(length + 1)]
    a, b = counts
    data = dict(runtime=spec['runtime'], warmupPasses=a, passes=b, warmupRows=passes(a), rows=passes(b),
                groupBoundaries=groups('warmup', a) + groups('measured', b), stopwatchFrequency=1_000_000_000,
                invocationsPerPass=job['microInvocations'], filter=job['filter'])
    proof = dict(ordering=dict(markReceived=1, ackSent=200_000_001)) if ordering else {}
    return dict(data=data, identity={key: spec[key] for key in prelude.IDENTITY}, seconds=1,
                marks=['MEASURED_END', 'MEASURED_START'], **proof)


def timed(root, *, ordering=True, a_pairs=0, b_pairs=2, noise=False):
    r = row(pairs=max(2, b_pairs))
    r.update(id='time/micro-demo', replay='micro', microFilter='stub', aPairs=a_pairs, bPairs=b_pairs)
    if noise:
        r.update(provider='azure', corpusId='fixture-corpus')
    d = root / 'clients'
    d.mkdir(exist_ok=True)
    (d / 'ReplayClient.dll').write_text('fixture bytes')
    for spec in r['sides'].values():
        spec.update(client='clients/ReplayClient.dll', harness='clients/Chrysalis.Benchmarks.dll',
                    dllSha256s=gate.closure_hashes(d))
    r['job'] = prepare.replay_job('micro', 'stub')
    plan, = timing.size_sessions([r], 'release', budget=10, probe=probe_rule(), prelude=True)
    plan.update(probeJob=prepare.replay_job('offline', '*'), orderingProof=prepare.ORDERING_WORK if ordering else None)
    if noise:
        plan.update(design='noise-band', repeatAllowed=False)
    return schedule.prepare(plan)


@pytest.mark.parametrize('breaking', [None, 'ordering', 'fixed', 'over-charge', 'over-calibration', 'ordering-work', 'ordering-counters', 'side-work', 'counted-work', 'marks', 'wire-type', 'reject'])
def test_worker_prelude_runs_only_after_kept_probe(tmp_path, monkeypatch, breaking):
    plan = timed(tmp_path)
    worker = schedule.worker_plan(plan)
    recipe = prelude.recipe(plan)
    workload = next(iter(recipe['rows']))
    order = []

    def fake(spec, job, destination, **kwargs):
        role = 'phase' if destination.parent.name == 'results' else 'probe' if destination.parent.name == 'probe' else destination.name
        order.append((role, job['microInvocations']))
        assert (tmp_path / 'results').is_dir()
        result = observation(spec, job, job['counts'], ordering=job.get('orderingProof'))
        if breaking == 'ordering' and role == 'ordering':
            result['ordering']['ackSent'] = 500_000_000
        if breaking == 'fixed' and role.endswith('-candidate'):
            result['data']['rows'][0]['wireBytes'] += 1
        if (breaking == 'over-charge' and role.endswith('-candidate') or
                breaking == 'over-calibration' and role.endswith('-calibration')):
            result['seconds'] = 7
        if breaking == 'ordering-work' and role == 'ordering':
            for row in result['data']['warmupRows'] + result['data']['rows']:
                row['wireBytes'] += 1
        if breaking == 'ordering-counters' and role == 'ordering':
            result['data']['groupBoundaries'][-2]['delta'][session.COUNTERS[0]] = 1
        if breaking == 'side-work' and role.endswith('-candidate'):
            for row in result['data']['warmupRows'] + result['data']['rows']:
                row['wireBytes'] += 1
        if breaking == 'marks' and role.endswith(('-candidate', '-baseline')):
            result['marks'] = ['MEASURED_START']
        if breaking == 'wire-type' and role.endswith(('-candidate', '-baseline')):
            for row in result['data']['warmupRows'] + result['data']['rows']:
                row['wireBytes'] = False
        if breaking == 'counted-work' and role == 'phase':
            for row in result['data']['warmupRows'] + result['data']['rows']:
                row['wireBytes'] += 1
        return result

    monkeypatch.setenv('P2_MACHINE_CREATED_UNIX', str(int(timing.time.time())))
    monkeypatch.setattr(timing.session, 'member', fake)
    monkeypatch.setattr(timing, 'probe_result', lambda records, rule: dict(decision='rejected' if breaking == 'reject' else 'kept'))
    ok = timing.run(tmp_path, worker, tmp_path / 'results', prelude=recipe)
    assert ok == (breaking in (None, 'reject'))
    assert all(role == 'probe' for role, _ in order[:16])
    if breaking == 'reject':
        assert len(order) == 16 and not (tmp_path / 'prelude').exists()
    elif breaking == 'over-calibration':
        assert order[16:] == [(workload + '-calibration', 16)]
        assert not (tmp_path / 'prelude/applied.json').exists()
    else:
        assert order[16:20] == [(workload + '-calibration', 16), (workload + '-baseline', 10),
                                (workload + '-candidate', 10), ('ordering', 16)]
        if breaking is None:
            assert all(role == 'phase' and count == 10 for role, count in order[20:])
            assert len(order) == 24
            assert json.loads((tmp_path / 'prelude/applied.json').read_text())[workload]['microInvocations'] == 10
        elif breaking == 'counted-work':
            assert len(order) == 21
        else:
            assert len(order) == 20
    with pytest.raises(FileExistsError):
        timing.run(tmp_path, worker, tmp_path / 'results', prelude=recipe)


def test_repeat_builder_and_verifier_rehash_kept_parent_archive(tmp_path, monkeypatch):
    parent = tmp_path / 'gate-parent'; parent.mkdir()
    child = tmp_path / 'gate-child'; (child / 'input').mkdir(parents=True)
    plan = timed(parent, ordering=False)
    worker = schedule.worker_plan(plan)
    def fake(build, workload, _destination, counts, **options):
        job = dict(worker['workloads'][workload])
        if 'invocations' in options:
            job['microInvocations'] = options['invocations']
        return observation(worker['builds'][build], job, counts)
    recipe = prelude.recipe(plan)
    prelude.execute(recipe, worker, parent / 'prelude', fake)
    run = parent / 'time/runs/fixture'; run.mkdir(parents=True)
    result_path = run / 'results.tar.gz'
    def write_results():
        with tarfile.open(result_path, 'w:gz') as output:
            output.add(parent / 'prelude', arcname='prelude')
        return bundle.sha(result_path)
    first_archive = write_results()
    records = [dict(session=0, phase='first', decision='kept', app='fixture', archiveSha256=first_archive)]
    session.save(parent / 'time/draws.json', records)
    source = child / 'input/session-0.tar.gz'
    prepare.pack(child, source, [], {'plan.json': bundle.gate_encoded(worker), 'prelude.json': bundle.gate_encoded(recipe)})
    seal = dict(mode='release', timePlans=[plan], files={'input/session-0.tar.gz': bundle.sha(source)})
    original = gate.write_seal(parent, seal)
    seal['retryOf'] = dict(kind='repeat', originalSealId=original, parent=parent.name,
                           firstArchiveSha256=first_archive, repeatIds=[plan['rows'][0]['id']])
    monkeypatch.setattr(timing, 'session_outcome', lambda *args, **kwargs: ('repeat', seal['retryOf']['repeatIds']))
    name = 'input/repeat-0.tar.gz'
    bundle.make_repeat(child, seal, 0, plan['rows'], name)
    assert 'prelude.json' not in prepare.archive_members(child / name)
    assert bundle.verified_bundle(child, seal, name) == (child / name).read_bytes()
    workload = next(iter(recipe['rows']))
    raw_path = parent / f'prelude/raw/{workload}-calibration.json'
    changed = json.loads(raw_path.read_text()); changed['data']['rows'][0]['nsPerBlock'] *= 2
    session.save(raw_path, changed)
    new_hash = write_results()
    for command in (lambda: bundle.make_repeat(child, seal, 0, plan['rows'], name),
                    lambda: bundle.verified_bundle(child, seal, name)):
        with pytest.raises(ValueError, match='archive changed'):
            command()
        records[0]['archiveSha256'] = new_hash
        session.save(parent / 'time/draws.json', records)
        with pytest.raises(ValueError, match='binding changed'):
            command()
        records[0]['archiveSha256'] = first_archive
        session.save(parent / 'time/draws.json', records)


@pytest.fixture
def azure(monkeypatch):
    # Keep AZ1's real shared resolver and file inventories; substitute only its release marker pin.
    def validate(value, expected=None):
        if value != MARKER or expected is not None and value != expected:
            raise ValueError('Missing or untrusted build marker')
        return value
    monkeypatch.setattr(gate_azure_payload, 'validate_build_marker', validate)
    settings = timing.PROVIDERS['azure']
    return settings


def fake_inputs(root):
    def write(name, value='fixture'):
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value)
        return bundle.sha(path)
    for prefix, name in [('runtime', 'dotnet'), ('python', 'bin/python3.12')]:
        session.save(root / prefix / 'sha256.json', {name: write(f'{prefix}/{name}')})
        (root / prefix / name).chmod(0o755)
    session.save(root / 'pinned/closure.json', dict(commit=gate.PIN, assemblySha256s={'Pinned.dll': write('pinned/Pinned.dll')}))
    session.save(root / 'corpus/capture/sha256.json', {name: write(f'corpus/capture/{name}')
                 for name in ('capture.json', 'server-to-client.bin', 'client-to-server.bin')})
    write('corpus/range.json', '{}')
    scripts = {*prepare.host_files(), *prepare.guest_files()}
    for name in scripts:
        write('repo/benchmarks/scripts/' + name)
    session.save(root / f'repo/{prepare.FIXTURES}/sha256.json', {'fixtures/data.cbor': write(f'repo/{prepare.FIXTURES}/fixtures/data.cbor')})
    session.save(root / f'repo/{prepare.FIXTURES}/audit-allocations.json', {'rows': {}})
    return dict(corporaRoot=str(root / 'corpus'), pinnedBaseline=str(root / 'pinned'),
                linuxDotnetRoot=str(root / 'runtime'), flyPythonRoot=str(root / 'python'))


def fake_container(root, monkeypatch, *, mode='release', kind='controls', noise=False, only_bytes=False, commit='a', missing_native=False):
    local = fake_inputs(root)
    monkeypatch.setattr(gate, 'REPO', root / 'repo')
    monkeypatch.setattr(gate, 'GATE', root / 'repo/benchmarks/gate')
    config = dict(defaults={'change': dict(design='long', timePairs={}), 'release': dict(design='long'), 'timePairs': 16},
                  direct={}, replays=['offline'], areas={'fixture': {'time': ['time/micro-*']}},
                  time=dict(noise={}, probe=probe_rule(), option=2, required=['time/micro-demo', 'time/preprod-offline'],
                            micro={'demo': 'demo-filter'}, platform={'provider': 'fly'}))
    if only_bytes:
        config['defaults'][mode]['replayPairs'] = 16
    session.save(gate.GATE / 'gated.json', config)
    plan = gate.REPO / 'selection.json'
    entries = [{'id': 'replay/offline/steady'}] if only_bytes else [{'id': 'time/micro-demo'}, {'id': 'time/preprod-offline'}]
    session.save(plan, dict(entries=entries, noiseRecords=noise, areas=['fixture'] if kind.startswith('batch') else []))

    def stages(args, output, rows, local, *, phase):
        if phase == 'restore':
            return
        assert set(local) == {'corporaRoot', 'pinnedBaseline', 'linuxDotnetRoot', 'flyPythonRoot'}
        arms = []
        for i, prefix in enumerate(('b', commit)):
            dest = output / f'arm-{i}'; dest.mkdir()
            for dll in ('ReplayClient.dll', 'Chrysalis.Benchmarks.dll', 'BenchmarkDotNet.dll'):
                (dest / dll).write_text('harness' if dll != 'ReplayClient.dll' else prefix)
            (dest / 'ReplayClient.runtimeconfig.json').write_text('{}')
            for name in ('ReplayClient', 'Chrysalis.Benchmarks'):
                graph = deps()
                if name == 'ReplayClient':
                    package = 'Fixture.Native/1.0.0'
                    graph['libraries'][package] = {}
                    graph['targets'][graph['runtimeTarget']['name']][package] = {
                        'runtimeTargets': {NATIVE_FIXTURE: dict(rid='linux-x64', assetType='native')}}
                session.save(dest / (name + '.deps.json'), graph)
            for name in (CAPSTONE, NATIVE_FIXTURE):
                if not (missing_native and name == NATIVE_FIXTURE):
                    path = dest / name
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(name.encode())
            (dest / CAPSTONE).with_name('libnot_selected.so').write_bytes(b'exclude me')
            arms.append(dict(commit=prefix * 40, treeId=prefix * 40, sdk='11.0.100', runtime='11.0.0', tfm='net11.0',
                             client=str(dest / 'ReplayClient.dll'), harness=str(dest / 'Chrysalis.Benchmarks.dll'),
                             dllSha256s=gate.closure_hashes(dest)))
        left, right = arms
        left['previousAlpha'] = False
        for item in rows:
            item['sides'] = dict(baseline=gate.public_side(left), candidate=gate.public_side(right))
        return dict(baseline=left, candidate=right), left, right

    monkeypatch.setattr(gate, 'stage_rows', stages)
    if only_bytes:
        replay = dict(id='replay/offline/steady', replay='offline', window='steady')
        corpus = gate.corpus_identity(replay, local)
        key = '|'.join(('offline', 'long', 'steady', '11.0.0', corpus))
        session.save(gate.GATE / 'bands/frozen.json', dict(key=key, row=replay['id'], replay='offline',
                     design='long', window='steady', runtime='11.0.0', corpusId=corpus, purpose='baseline-noise',
                     pairs=10, absoluteNoiseBandBytes=16))
    args = SimpleNamespace(mode=mode, plan=plan, seal_kind=kind, ordering_proof=False, baseline='b' * 40, candidate=commit * 40, only=None)
    def build(output):
        stage = prepare.build(args, output, local, marker=MARKER, phase='restore')
        assert not output.exists() and (stage / 'restore.json').exists()
        with pytest.raises(ValueError, match='request changed'):
            prepare.build(args, output, local, marker=dict(MARKER, runnerSha256='e' * 64), phase='build')
        try:
            return prepare.build(args, output, local, marker=MARKER, phase='build')
        finally:
            shutil.rmtree(stage)
    build.request = dict(kind='prepare', mode=mode, baseline=args.baseline, candidate=args.candidate,
                         sealKind=kind, plan='selection.json', planSha256=bundle.sha(plan), **TOOLING)
    fake_tooling(monkeypatch, gate.REPO)
    return local, build


def replace_session(directory, seal, mutate, *, prelude_override=None, number=0):
    path = directory / f'input/session-{number}.tar.gz'
    with tarfile.open(path) as stream:
        modes = {item.name: item.mode for item in stream}
        members = {name: stream.extractfile(name).read() for name in modes}
    for key in ('plan.json', 'prelude.json', 'bundle-manifest.json'):
        members.pop(key)
    mutate(members)
    plan = seal['timePlans'][number]
    plan['inputSha256'] = {name: hashlib.sha256(data).hexdigest() for name, data in members.items()}
    schedule.prepare(plan)
    members['plan.json'] = bundle.gate_encoded(schedule.worker_plan(plan))
    if prelude_override is not False:
        members['prelude.json'] = bundle.gate_encoded(prelude_override or prelude.recipe(plan))
    path.unlink()
    import tempfile
    with tempfile.TemporaryDirectory(dir=directory.parent) as temp:
        root = Path(temp)
        for name, content in members.items():
            file = root / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(content)
            file.chmod(modes.get(name, 0o644))
        prepare.pack(root, path, sorted(members), {})


def test_container_pack_preserves_only_selected_native_assets(tmp_path, azure, monkeypatch):
    _, build = fake_container(tmp_path / 'project', monkeypatch)
    path = build(tmp_path / 'prepared')
    seal = prepare.json_file(path / 'plan.json')
    for number, plan in enumerate(seal['timePlans']):
        expanded = prepare.archive_members(path / f'input/session-{number}.tar.gz')
        arms = {schedule.build_id(spec) for row in plan['rows'] for spec in row['sides'].values()}
        natives = {f'clients/{build_id}/{name}': hashlib.sha256(name.encode()).hexdigest()
                   for build_id in arms for name in (CAPSTONE, NATIVE_FIXTURE)}
        assert {name: sha for name, sha in expanded.items() if '/runtimes/' in name} == natives
        assert all(plan['inputSha256'][name] == sha for name, sha in natives.items())


def test_container_pack_refuses_missing_native_asset(tmp_path, azure, monkeypatch):
    _, build = fake_container(tmp_path / 'project', monkeypatch, missing_native=True)
    path = tmp_path / 'prepared'
    with pytest.raises(ValueError, match='Missing staged native asset'):
        build(path)
    assert not (path / 'prepared.json').exists()


@pytest.mark.parametrize('fault', ['absent', 'wrong-hash'])
def test_container_import_requires_native_members_at_bound_hashes(tmp_path, azure, monkeypatch, fault):
    local, build = fake_container(tmp_path / 'project', monkeypatch)
    path = build(tmp_path / 'prepared')
    plan = prepare.json_file(path / 'plan.json')
    verified = copy.deepcopy(plan)
    _, payload = prepare.inputs(local)
    def mutate(members):
        name = next((name for name in members if name.endswith('/' + NATIVE_FIXTURE)), None)
        if name and fault == 'absent':
            members.pop(name)
        if name and fault == 'wrong-hash':
            members[name] = b'changed native bytes'
    replace_session(path, plan, mutate)
    # For absence even a self-consistently reattested archive/plan cannot pass.
    with pytest.raises(ValueError, match='Missing packed native asset' if fault == 'absent' else 'Native asset digest differs'):
        prepare.verify_timing(path, plan if fault == 'absent' else verified, payload, host_hashes=prepare.json_file(path / 'prepared.json')['files'])


@pytest.mark.parametrize('defect', ['missing-marker', 'different-marker', 'missing-deps', 'mac-deps', 'arm64-deps', 'no-capstone'])
def test_container_prepare_rejects_reattested_payload(tmp_path, azure, monkeypatch, defect):
    local, build = fake_container(tmp_path / 'project', monkeypatch)
    path = build(tmp_path / 'prepared')
    plan = prepare.json_file(path / 'plan.json')
    _, payload = prepare.inputs(local)
    def mutate(members):
        if defect == 'missing-marker':
            members.pop('container-build.json', None)
            return
        if defect == 'different-marker':
            members['container-build.json'] = bundle.gate_encoded(dict(MARKER, runnerSha256='e' * 64))
            return
        name = next(name for name in members if name.endswith('/ReplayClient.deps.json'))
        old = members.pop(name)
        if defect == 'no-capstone':
            altered = json.loads(old)
            package = 'Gee.External.Capstone/2.3.0'
            altered['targets'][altered['runtimeTarget']['name']].pop(package)
            altered['libraries'].pop(package)
            members[name] = bundle.gate_encoded(altered)
        if defect in ('mac-deps', 'arm64-deps'):
            members[name] = (old.replace(b'linux-x64', b'osx-arm64').replace(b'.so', b'.dylib') if defect == 'mac-deps'
                             else old.replace(b'linux-x64', b'linux-arm64'))
    replace_session(path, plan, mutate)
    with pytest.raises(ValueError, match='marker' if defect.endswith('-marker') else r'\.deps\.json'):
        prepare.verify_timing(path, plan, payload, host_hashes=prepare.json_file(path / 'prepared.json')['files'])


@pytest.mark.parametrize('extra', ['unselected.so', 'unexpected.json'])
def test_import_verifier_refuses_unselected_flat_files(tmp_path, azure, monkeypatch, extra):
    local, build = fake_container(tmp_path / 'project', monkeypatch)
    path = build(tmp_path / 'prepared')
    plan = prepare.json_file(path / 'plan.json')
    _, payload = prepare.inputs(local)
    def add(members):
        name = next(name for name in members if name.endswith('/ReplayClient.deps.json'))
        members[name.rsplit('/', 1)[0] + '/' + extra] = b'unselected'
    replace_session(path, plan, add)
    with pytest.raises(ValueError, match='Unrecognized packed input'):
        prepare.verify_timing(path, plan, payload, host_hashes=prepare.json_file(path / 'prepared.json')['files'])


@pytest.mark.parametrize('noise,only_bytes', [(False, False), (True, False), (False, True)])
def test_pc_handoff_never_measures_and_checks_live_budget(tmp_path, azure, monkeypatch, noise, only_bytes):
    def forbidden(*args, **kwargs):
        pytest.fail('Build/import entered the measuring boundary')
    for module, names in ((session, ('member', 'monitored_process')), (timing, ('run',)), (gate, ('execute_job',))):
        for name in names:
            monkeypatch.setattr(module, name, forbidden)
    fetched = tmp_path / 'mbp-results'
    # Two fake container runs transfer independently prepared folders to the PC.
    folders = [fetched_prepared(fetched, letter) for letter in 'ab']
    for folder in folders:
        _, build = fake_container(tmp_path / folder.parent.name, monkeypatch, noise=noise, only_bytes=only_bytes,
                                  kind='noise' if noise else 'controls')
        build(folder)
        completion_receipt(folder, request=build.request)
    assert (folders[0] / 'prepared.json').read_bytes() == (folders[1] / 'prepared.json').read_bytes()
    settings, local = completion_settings(tmp_path, monkeypatch)
    def check_local(seal):
        assert bundle.sha(settings) == seal['files']['input/azure-local.json'], 'Settings changed since seal'
    monkeypatch.setattr(bundle, 'checked_azure_local', check_local, raising=False)
    budget = {'room': False}
    def check_budget(actual, seal, go):
        assert actual == local and not (gate.REPO / 'benchmarks/out' / 'seal.json').exists()
        assert prepare.SHA.fullmatch(go) and seal['programStopUsd'] == 40
        if not budget['room']:
            raise ValueError('Moved ledger')
    def import_dir(name):
        if name in folders:
            completion_receipt(name, request=build.request)  # Reattest before testing the independent inner checks.
        return prepare.import_prepared(name, fetched=fetched, settings=settings, local=local,
                                       check_cli=lambda path: '2.3.4', check_budget=check_budget)
    folder = folders[0]
    with pytest.raises(ValueError, match='under work/mbp-results'):
        import_dir(tmp_path / 'elsewhere')
    if not only_bytes:
        with pytest.raises(ValueError, match='Moved ledger'):
            import_dir(folder)
        assert not list((gate.REPO / 'benchmarks/out').glob('gate-*'))
    extra = folder / 'extra.json'
    extra.write_text('{}')
    with pytest.raises(ValueError, match='missing, changed or extra'):
        import_dir(folder)
    extra.unlink()
    portable = folder / 'prepared.json'
    manifest = json.loads(portable.read_text())
    for bad in (None, {'mode': 'build'}, dict(MARKER, runnerSha256='f' * 64)):
        tampered = dict(manifest)
        if bad is None:
            tampered.pop('buildMarker')
        else:
            tampered['buildMarker'] = bad
        session.save(portable, tampered)
        with pytest.raises(ValueError, match='build marker'):
            import_dir(folder)
    session.save(portable, manifest)
    plan_path = folder / 'plan.json'
    original = prepare.json_file(plan_path)
    session.save(plan_path, dict(original, buildMarker=dict(MARKER, runnerSha256='e' * 64)))
    session.save(portable, dict(manifest, files=dict(manifest['files'], **{'plan.json': bundle.sha(plan_path)})))
    with pytest.raises(ValueError, match='marker differs'):
        import_dir(folder)
    session.save(plan_path, original)
    session.save(portable, manifest)
    suite = folder / 'input/suite.tar.gz'
    for items, error in (({}, 'archive build marker'), ({'container-build.json': bundle.gate_encoded(MARKER)}, 'AZ3 importer')):
        prepare.pack(folder, suite, [], items)
        session.save(portable, dict(manifest, files=dict(manifest['files'], **{'input/suite.tar.gz': bundle.sha(suite)})))
        with pytest.raises(ValueError, match=error):
            import_dir(folder)
        suite.unlink()
    session.save(portable, manifest)
    session.save(portable, dict(manifest, files=dict(manifest['files'], **{'/absolute': 'a' * 64})))
    with pytest.raises(ValueError, match='Noncanonical'):
        import_dir(folder)
    session.save(portable, manifest)
    plan = folder / 'plan.json'; original = plan.read_bytes()
    plan.write_text('{}')
    with pytest.raises(ValueError, match='missing, changed or extra'):
        import_dir(folder)
    plan.write_bytes(original)
    budget['room'] = True
    if not only_bytes:
        archive = folder / 'input/session-0.tar.gz'
        original_archive = archive.read_bytes()
        for name in ('container-build.json', 'dotnet/dotnet', 'scripts/gate.py', 'prelude.json'):
            sealed = prepare.json_file(plan)
            def change_archive(members):
                if name == 'container-build.json':
                    members.pop(name)
                elif name != 'prelude.json':
                    members[name] = b'changed with all enclosing hashes refreshed'
            replace_session(folder, sealed, change_archive, prelude_override={'changed': True} if name == 'prelude.json' else None)
            session.save(plan, sealed)
            session.save(portable, dict(manifest, files=dict(manifest['files'], **{
                'input/session-0.tar.gz': bundle.sha(archive), 'plan.json': bundle.sha(plan)})))
            with pytest.raises(ValueError):
                import_dir(folder)
            assert not list((gate.REPO / 'benchmarks/out').glob('gate-*'))
            archive.write_bytes(original_archive)
            plan.write_bytes(original)
            session.save(portable, manifest)
    output = import_dir(folder)
    seal = gate.read_seal(output)
    assert all(plan['design'] == 'noise-band' for plan in seal['timePlans']) if noise else not seal['noiseRecords']
    assert bool(seal['timePlans']) == (not only_bytes)
    assert seal['payloadManifests']['dotnet/dotnet'] == bundle.sha(Path(tmp_path / folders[0].parent.name / 'runtime' / 'dotnet'))
    if noise:
        for number, plan in enumerate(seal['timePlans']):
            rows = [dict(id=row['id'], band=.01, machine=azure['machine'], design='long', sourceSeal=bundle.sha(output / 'seal.json'),
                         baseline=row['sides']['baseline'], pairs=row['aPairs'], counts=plan['counts'], corpusId=row['corpusId']) for row in plan['rows']]
            session.save(output / f'time/{number}-noise-records.json', rows)
        assert set(bundle.finish_noise_records(output, seal)) == {'time/micro-demo', 'time/preprod-offline'}
        session.save(output / 'time/0-noise-records.json', [])
        with pytest.raises(ValueError, match='Missing'):
            bundle.finish_noise_records(output, seal)
    session.save(settings, dict(local, azureLock='changed'))
    with pytest.raises(AssertionError, match='Settings changed'):
        bundle.checked_azure_local(gate.read_seal(output))


@pytest.mark.parametrize('change', ['product', 'subset', 'row-policy', 'derived', 'new-row', 'retry', 'rc', 'caps',
                                    'different-approval', 'script', 'runtime', 'corpus', 'marker', 'unbound-plan', 'execute-bit',
                                    'canonical', 'native', 'local-settings', 'host-snapshot', 'batch-areas', 'empty-rows', 'stale-sides',
                                    'portable-different', 'extra-band'])
def test_seal_diff_accepts_only_known_per_batch_changes(tmp_path, azure, monkeypatch, change):
    def seal_of(name, commit):
        _, build = fake_container(tmp_path / name, monkeypatch, mode='change', kind='batch-leg-1', commit=commit)
        directory = build(tmp_path / (name + '-prepared'))
        portable = prepare.json_file(directory / 'prepared.json')
        seal = prepare.json_file(directory / 'plan.json')
        _, payload = prepare.inputs(fake_inputs(tmp_path / name))
        seal.update(files=portable['files'], payloadManifests=payload, platform=azure, programStopUsd=40)
        session.save(directory / 'input/azure-local.json', dict(azureToolingCommit=TOOLING['toolingCommit'],
                                                               azureToolingTree=TOOLING['toolingTree']))
        seal['files']['input/azure-local.json'] = bundle.sha(directory / 'input/azure-local.json')
        gate.write_seal(directory, seal)
        return directory, seal
    old, old_seal = seal_of('old', 'a')
    new, seal = seal_of('new', 'c')
    if change == 'product':
        # seal-diff must not fall back to the launcher's checkout for script bytes.
        monkeypatch.setattr(gate, 'REPO', tmp_path / 'unrelated-checkout')
    verdicts = [tmp_path / f'{seat}.json' for seat in ('astra', 'opus')]
    def rewrite(directory, value):
        session.save(directory / 'plan.json', {key: item for key, item in value.items() if key not in batch.PC_FIELDS})
        value['files']['plan.json'] = bundle.sha(directory / 'plan.json')
        gate.write_seal(directory, value)
    if change == 'subset':
        seal['rows'] = [r for r in seal['rows'] if r['replay'] != 'micro']
        (new / 'input/session-1.tar.gz').unlink()
        seal['files'].pop('input/session-1.tar.gz')
        seal['timePlans'].pop()
    elif change == 'row-policy':
        seal['rows'][0]['aPairs'] = 4
    elif change == 'derived':
        plan = seal['timePlans'][0]
        plan['rawSeconds'] += 1
        schedule.prepare(plan)
    elif change == 'new-row':
        seal['rows'][0]['id'] = 'time/micro-other'
    elif change == 'retry':
        seal['retryOf'] = {'kind': 'first'}
    elif change == 'rc':
        seal['kind'] = old_seal['kind'] = 'rc'
        rewrite(old, old_seal)
    elif change == 'marker':
        seal['buildMarker'] = dict(MARKER, runnerSha256='e' * 64)
    elif change == 'batch-areas':
        seal['batchAreas'] = ['unknown', 'unknown']
    elif change == 'empty-rows':
        seal.update(rows=[], timePlans=[], bands={})
        for name in list(seal['files']):
            if name.startswith('input/session-'):
                (new / name).unlink()
                del seal['files'][name]
    elif change == 'extra-band':
        seal['bands']['unused'] = old_seal['bands']['unused'] = None
        rewrite(old, old_seal)
    elif change == 'stale-sides':
        name = 'build/sides.json'
        (new / name).write_bytes((old / name).read_bytes())
        seal['files'][name] = bundle.sha(new / name)
    elif change == 'canonical':
        seal['canonical'] = not seal['canonical']
    elif change == 'execute-bit':
        import io
        name = 'input/session-0.tar.gz'
        path = new / name
        with tarfile.open(path) as stream:
            entries = [(member, stream.extractfile(member).read()) for member in stream]
        with tarfile.open(path, 'w:gz') as stream:
            for member, body in entries:
                if member.name == 'python/bin/python3.12':
                    assert member.mode == 0o755
                    member.mode = 0o644
                stream.addfile(member, io.BytesIO(body))
        seal['files'][name] = bundle.sha(path)
    elif change == 'native':
        def change_native(members):
            for name in members:
                if name.endswith('/' + CAPSTONE):
                    members[name] = b'rebuilt native bytes'
        replace_session(new, seal, change_native)
        name = 'input/session-0.tar.gz'
        seal['files'][name] = bundle.sha(new / name)
    elif change in ('local-settings', 'host-snapshot'):
        name = 'input/azure-local.json' if change == 'local-settings' else 'input/azure/gate.py'
        (new / name).write_text('changed')
        seal['files'][name] = bundle.sha(new / name)
    elif change == 'caps':
        seal['programStopUsd'] = 41
    elif change in ('script', 'runtime', 'corpus'):
        name = 'input/session-0.tar.gz'
        path = new / name
        with tarfile.open(path) as stream:
            members = {member.name: stream.extractfile(member).read() for member in stream}
        altered = {'script': 'scripts/gate.py', 'runtime': 'dotnet/dotnet', 'corpus': 'corpora/range.json'}[change]
        members[altered] = b'changed with a fresh tar hash'
        path.unlink()
        members.pop('bundle-manifest.json')
        prepare.pack(new, path, [], members)
        seal['files'][name] = bundle.sha(path)
    rewrite(new, seal)
    if change == 'portable-different':
        path = new / 'plan.json'
        portable = prepare.json_file(path)
        session.save(path, dict(portable, canonical=not portable['canonical']))
        seal['files']['plan.json'] = bundle.sha(path)
        gate.write_seal(new, seal)
    if change == 'unbound-plan':
        seal['files'].pop('plan.json')
        (new / 'plan.json').unlink()
        gate.write_seal(new, seal)
    for seat, path in zip(('astra', 'opus'), verdicts):
        ident = bundle.sha(old / 'seal.json') if change != 'different-approval' or seat == 'astra' else 'f' * 64
        session.save(path, dict(seat=seat, verdict='APPROVED', sealId=ident))
    if change in ('product', 'subset'):
        assert batch.seal_diff(old, new, verdicts)
    else:
        with pytest.raises(ValueError):
            batch.seal_diff(old, new, verdicts)


def test_carry_uses_each_row_limit_and_never_discards_unresolved():
    seal = dict(kind='batch-leg-1', mode='change', batchAreas=['network'],
                gated=dict(areas=dict(network=dict(time=['time/network-*']))),
                rows=[dict(id='covered', kind='time'), dict(id='time/network-a', kind='time'),
                      dict(id='time-out', kind='time'), dict(id='bytes-out', kind='replay', bandKey='band'),
                      dict(id='bytes-equal', kind='replay', bandKey='band'),
                      dict(id='invalid', kind='time'), dict(id='fail', kind='time')],
                bands=dict(band=dict(record=dict(absoluteNoiseBandBytes=16))))
    crossed = dict(status='INCONCLUSIVE', reason='Upper bound >1.05; interval crosses the rule', confidence95=[1.05, 1.08])
    bytes_crossed = dict(status='INCONCLUSIVE', reason='Interval crosses limit', confidence95DifferenceBytes=[16, 22])
    results = {'covered': dict(status='PASS'), 'time/network-a': crossed, 'time-out': crossed, 'bytes-out': bytes_crossed,
               'bytes-equal': dict(bytes_crossed, confidence95DifferenceBytes=[14, 16]),
               'invalid': dict(crossed, reason='invalid session'), 'fail': dict(status='FAIL', reason='lower bound above limit')}
    assert [(record['id'], record['classification'], record['destination'], record['reason']) for record in batch.carry_rows(seal, results)] == [
        ('time-out', 'moved', 'leg-2', crossed['reason']), ('bytes-out', 'moved', 'leg-2', bytes_crossed['reason']),
        ('bytes-equal', 'unresolved', 'leg-2', bytes_crossed['reason']), ('invalid', 'unresolved', 'leg-2', 'invalid session'),
        ('fail', 'moved', 'leg-2', 'lower bound above limit'),
    ]


def test_noise_reuses_existing_machine_record_and_proves_ordering_once(tmp_path, azure, monkeypatch):
    local, build = fake_container(tmp_path, monkeypatch, noise=True, kind='noise')
    all_plans = prepare.json_file(build(tmp_path / 'full') / 'plan.json')['timePlans']
    assert len(all_plans) == 2 and sum(plan['orderingProof'] is not None for plan in all_plans) == 1
    config_path = gate.GATE / 'gated.json'
    config = prepare.json_file(config_path)
    config['time']['noise'] = {azure['machine']: {'time/micro-demo': dict(design='long', machine=azure['machine'], band=.01)}}
    session.save(config_path, config)
    plan, = prepare.json_file(build(tmp_path / 'missing-only') / 'plan.json')['timePlans']
    assert [item['id'] for item in plan['rows']] == ['time/preprod-offline']
    assert not plan['repeatAllowed'] and plan['rows'][0]['bPairs'] == 0


def test_raw_prelude_rederivation_checks_counted_population(tmp_path):
    plan = timed(tmp_path, ordering=False)
    worker, recipe = schedule.worker_plan(plan), prelude.recipe(plan)
    def fake(build, name, destination, counts, **options):
        job = dict(worker['workloads'][name])
        job['microInvocations'] = options.get('invocations', job['microInvocations'])
        return observation(worker['builds'][build], job, counts)
    directory = tmp_path / 'prelude'
    applied = prelude.execute(recipe, worker, directory, fake)
    archive = tmp_path / 'results.tar.gz'
    def archive_results():
        with tarfile.open(archive, 'w:gz') as stream:
            stream.add(directory, arcname='prelude')
    archive_results()
    with tarfile.open(archive) as stream:
        assert prelude.read(stream, plan) == applied
    effective = prelude.apply(worker, applied)
    records = []
    for block in effective['blocks']:
        for name in block['members']:
            build = effective['members'][name]['build']
            records.append(dict(observation(effective['builds'][build], effective['workloads'][block['workload']], (180, 60)),
                                build=build, id=name))
    prelude.verify_population(plan, records, applied)
    records[0]['data']['invocationsPerPass'] += 1
    with pytest.raises(ValueError, match='invocation count'):
        prelude.verify_population(plan, records, applied)
    forged = copy.deepcopy(applied)
    forged[next(iter(forged))]['microInvocations'] += 1
    session.save(directory / 'applied.json', forged)
    archive_results()
    with tarfile.open(archive) as stream, pytest.raises(ValueError, match='differs from raw'):
        prelude.read(stream, plan)


TOOLING = dict(toolingCommit='2' * 40, toolingTree='3' * 40)


def fake_tooling(monkeypatch, repo, fault=None):
    def git(flag, *args, **kwargs):
        assert flag == '--no-replace-objects'
        if args[:2] == ('cat-file', 'commit'):
            if fault == 'unresolvable':
                raise subprocess.CalledProcessError(1, args)
            tree = ('e' * 40) if fault == 'tree' else TOOLING['toolingTree'] if args[2] == TOOLING['toolingCommit'] else args[2]
            headers = 0 if fault == 'unsigned' else 2 if fault == 'two-signatures' else 1
            return f'tree {tree}\n' + 'gpgsig fixture\n' * headers + '\nexample\n'
        if args == ('ls-tree', '-z', TOOLING['toolingTree'], '--', 'benchmarks/gate/bands/'):
            return ''.join(f'100644 blob {"0" * 40}\t{path.relative_to(repo).as_posix()}\0'
                           for path in sorted((repo / 'benchmarks/gate/bands').glob('*.json')))
        if args[:2] == ('merge-base', '--is-ancestor'):
            if fault == 'unreachable':
                raise subprocess.CalledProcessError(1, args)
            return ''
        raise AssertionError(args)
    monkeypatch.setattr(gate, 'git', git)
    monkeypatch.setattr(gate, 'previous_alpha', lambda candidate, **kwargs: None)
    monkeypatch.setattr(prepare, 'git_blob', lambda tree, name: (repo / name).read_bytes())



def test_git_tooling_reads_frozen_blobs_after_checkout_diverges(tmp_path, monkeypatch):
    repo = tmp_path / 'repo'
    repo.mkdir()
    monkeypatch.setattr(gate, 'REPO', repo)
    env = dict(session.environment({}), GIT_INDEX_FILE=str(tmp_path / 'index'))
    def git(*args):
        return subprocess.check_output(['git', '-C', str(repo), *args], env=env, text=True).strip()
    git('init', '-q')
    folder = repo / 'benchmarks/gate/bands'
    band = folder / 'kept.json'
    body = b'{"band": 123}\n'
    folder.mkdir(parents=True)
    band.write_bytes(body)
    git('add', '--', 'benchmarks')
    tree = git('write-tree')
    band.write_bytes(b'{"band": 999}\n')
    (folder / 'not-pinned.json').write_text('{}')
    assert prepare.tooling_bands(tree) == [('kept.json', body)]
    with pytest.raises(ValueError, match='Completion tooling file is unavailable'):
        prepare.git_blob(tree, 'benchmarks/gate/bands/not-pinned.json')


def fetched_prepared(root, token='1'):
    # R5-MAC-FETCH: when supplied, use the path independently emitted by the real transport.
    transport = os.environ.get('CHRYSALIS_TEST_MBP_FETCH')
    sample = Path(transport or f'mac-checkout/{"a" * 32}/mbp-container-{"1" * 32}/prepared')
    return root / sample.parent.with_name('mbp-container-' + token * 32) / sample.name


@pytest.mark.parametrize('fault', [None, 'shallow', 'deep', 'worktree', 'run-id', 'token',
                                    'root-link', 'worktree-link', 'run-link', 'container-link'])
def test_completion_transport_layout(tmp_path, azure, monkeypatch, fault):
    fetched = tmp_path / 'pc/mbp-results'
    folder = fetched_prepared(fetched)
    if fault == 'shallow': folder = fetched / folder.parent.name / folder.name
    if fault == 'deep': folder = folder.parent.parent / 'extra' / folder.parent.name / folder.name
    if fault == 'worktree': folder = fetched.joinpath('-dash', *folder.relative_to(fetched).parts[1:])
    if fault == 'run-id': folder = folder.parent.parent.parent / ('A' * 32) / folder.parent.name / folder.name
    if fault == 'token': folder = folder.parent.with_name('mbp-container-' + 'A' * 32) / folder.name
    _, build = fake_container(tmp_path / 'project', monkeypatch)
    build(folder)
    completion_receipt(folder, request=build.request)
    settings, _ = completion_settings(tmp_path, monkeypatch)
    monkeypatch.setattr(prepare, 'sys', SimpleNamespace(platform='linux'))
    monkeypatch.setenv('WSL_DISTRO_NAME', 'fixture')
    monkeypatch.setenv('CHRYSALIS_GATE_LOCAL', str(settings))
    monkeypatch.setattr(bundle, 'checked_azure_local', lambda *_: None)
    import gate_azure
    def checked_cli(_):
        assert fault is None, 'Untrusted path reached CLI'
        return SimpleNamespace(version=lambda: '2.3.4')
    monkeypatch.setattr(gate_azure, 'Az', checked_cli)
    monkeypatch.setattr(gate_azure, 'check_seal_budget', lambda *_: None)
    if fault and fault.endswith('-link'):
        levels = {'root-link': 3, 'worktree-link': 2, 'run-link': 1, 'container-link': 0}
        path = folder.parents[levels[fault]]
        real = path.with_name('alias-target')
        path.rename(real)
        path.symlink_to(real, target_is_directory=True)
        # Even passing the already-resolved path must not permit a symlinked fetch root.
        if fault == 'root-link':
            folder = folder.resolve()
    args = SimpleNamespace(build_only=False, from_prepared=folder, mode=None, baseline=None, candidate=None,
                           plan=None, only=None, output=None, seal_kind=None, ordering_proof=False)
    if fault:
        with pytest.raises(ValueError, match='under work/mbp-results'):
            prepare.command(args)
    else:
        assert gate.read_seal(prepare.command(args))['provider'] == 'azure'


def completion_settings(tmp_path, monkeypatch):
    local = fake_inputs(tmp_path / 'pc')
    for root, pin in [('linuxDotnetRoot', 'linuxDotnetManifestSha256'), ('flyPythonRoot', 'flyPythonManifestSha256')]:
        local[pin] = bundle.sha(Path(local.pop(root)) / 'sha256.json')
    monkeypatch.setattr(gate, 'REPO', tmp_path / 'pc/repo')
    local.update({key: str(tmp_path / key) for key in ('azureCli', 'azureLock', 'azureBaseline', 'azureLedger')},
                 azureToolingCommit=TOOLING['toolingCommit'], azureToolingTree=TOOLING['toolingTree'])
    settings = tmp_path / 'settings.json'; session.save(settings, local)
    return settings, local


def completion_receipt(folder, *, request=None, fields=None):
    request = request or dict(kind='prepare', mode='release', baseline='b' * 40, candidate='a' * 40,
                              sealKind='controls', **TOOLING)
    control = folder.parent / 'build-files.json'
    session.save(control, prepare.files(folder))
    canonical = json.dumps(request, sort_keys=True, separators=(',', ':'), ensure_ascii=True, allow_nan=False).encode()
    data = dict(schemaVersion=1, token=folder.parent.name.removeprefix('mbp-container-'), buildMarker=MARKER,
                request=request, requestSha256=hashlib.sha256(canonical).hexdigest(), previousAlpha=None,
                preparedSha256=bundle.sha(folder / 'prepared.json'), filesManifestSha256=bundle.sha(control))
    data.update(fields or {})
    session.save(folder.parent / 'completion.json', data)
    return data


@pytest.mark.parametrize('fault', ['missing', 'extra', 'wrong-type', 'token', 'request-digest', 'prepared-digest',
                                    'files-digest', 'request-mode', 'request-side', 'request-tooling',
                                    'local-absent', 'local-shape', 'unresolvable', 'tree', 'unsigned',
                                    'two-signatures', 'unreachable'])
def test_import_refuses_untrusted_completion(tmp_path, azure, monkeypatch, fault):
    fetched = tmp_path / 'mbp-results'
    folder = fetched_prepared(fetched, '1')
    _, build = fake_container(tmp_path / 'project', monkeypatch)
    build(folder)
    request = dict(build.request)
    request.update({'request-mode': dict(mode='change'), 'request-side': dict(baseline='c' * 40),
                    'request-tooling': dict(toolingCommit='d' * 40)}.get(fault, {}))
    faults = {'extra': {'extra': True}, 'wrong-type': {'schemaVersion': True}, 'token': {'token': '0' * 32},
              'request-digest': {'requestSha256': '0' * 64}, 'prepared-digest': {'preparedSha256': '0' * 64},
              'files-digest': {'filesManifestSha256': '0' * 64}}
    if fault != 'missing':
        completion_receipt(folder, request=request, fields=faults.get(fault))
    fake_tooling(monkeypatch, gate.REPO, fault)
    settings, local = completion_settings(tmp_path, monkeypatch)
    if fault == 'local-absent':
        local.pop('azureToolingCommit')
    if fault == 'local-shape':
        local['azureToolingCommit'] += '0'
    session.save(settings, local)
    monkeypatch.setattr(bundle, 'checked_azure_local', lambda seal: None)
    with pytest.raises(ValueError, match='[Cc]ompletion|[Tt]ooling'):
        prepare.import_prepared(folder, fetched=fetched, settings=settings, local=local,
                                check_cli=lambda path: '2.3.4', check_budget=lambda *args: None)
    assert not list((gate.REPO / 'benchmarks/out').glob('gate-*'))


def test_carry_is_scope_specific_and_excludes_recorded_only():
    seal = dict(kind='batch-leg-1', mode='change', batchAreas=['area'],
                gated={'areas': {'area': {'bytes': ['time/*'], 'time': ['replay/*']}}},
                rows=[dict(id='time/real', kind='time'), dict(id='replay/real', kind='replay'),
                      dict(id='observation', kind='time', rowClass='recorded-only')], bands={})
    assert {row['id'] for row in batch.carry_rows(seal, {})} == {'time/real', 'replay/real'}


def test_verify_policy_accepts_pinned_runner_call():
    # mbp-container:1177 (pin 2512c771, gate_azure_payload.py:17) passes seal, builds, receipt, local.
    inspect.signature(prepare.verify_policy).bind(None, None, None, None)


@pytest.mark.parametrize('defect,error', [
    ('release-set', 'derived policy'), ('rule-version', 'derived policy'), ('gated', 'derived policy'),
    ('bands', 'derived policy'), ('corpus', 'derived policy'), ('cold-alias', 'derived policy'),
    ('row-build', 'derived policy'), ('plan-authority', 'carries PC authority'), ('kind', 'kind or mode'),
    ('host', 'host script changed'), ('manifests', 'manifests differ'), ('selection', 'plan digest differs'),
    ('previous', 'previous-alpha'), ('duplicate-receipt', 'Duplicate JSON'), ('empty-dir', 'Extra completion directory'),
])
def test_import_rejects_reattested_policy_and_transport(tmp_path, azure, monkeypatch, defect, error):
    fetched = tmp_path / 'mbp-results'
    folder = fetched_prepared(fetched, '9')
    _, build = fake_container(tmp_path / 'project', monkeypatch)
    build(folder)
    settings, local = completion_settings(tmp_path, monkeypatch)
    plan, portable = (prepare.json_file(folder / name) for name in ('plan.json', 'prepared.json'))
    fields = {}
    request = dict(build.request)
    if defect == 'release-set': plan['releaseSet'] = []
    if defect == 'rule-version': plan['ruleVersion'] += '-fixture'
    if defect == 'gated': plan['gated']['time']['option'] = 3
    if defect == 'bands': plan['bands']['bogus'] = None
    if defect == 'corpus': plan['rows'][0]['corpusId'] = '0' * 64
    if defect == 'cold-alias': plan['rows'][0]['sides']['baseline']['runtime'] = '10.0.9'
    if defect == 'row-build': plan['rows'][0]['sides']['candidate']['treeId'] = 'd' * 40
    if defect == 'plan-authority': plan['programStopUsd'] = 400
    if defect == 'kind': plan['kind'] = 'rc'
    if defect == 'host': (folder / 'input/azure/gate.py').write_text('reattested')
    if defect == 'manifests': portable['manifests'] = {}
    if defect == 'selection': request['planSha256'] = '0' * 64
    if defect == 'previous': fields['previousAlpha'] = dict(name='2.0.0-alpha', commit='b' * 40)
    session.save(folder / 'plan.json', plan)
    portable['files'] = {name: bundle.sha(folder / name) for name in portable['files']}
    session.save(folder / 'prepared.json', portable)
    completion_receipt(folder, request=request, fields=fields)
    if defect == 'duplicate-receipt':
        receipt = folder.parent / 'completion.json'
        receipt.write_text(receipt.read_text().replace('{\n', '{"schemaVersion":1,\n', 1))
    if defect == 'empty-dir': (folder / 'empty').mkdir()
    with pytest.raises(ValueError, match=error):
        prepare.import_prepared(folder, fetched=fetched, settings=settings, local=local,
                                check_cli=lambda *_: pytest.fail('Untrusted input reached CLI'),
                                check_budget=lambda *_: pytest.fail('Untrusted input reached ledger'))
    assert not list((gate.REPO / 'benchmarks/out').glob('gate-*'))



@pytest.mark.parametrize('defect', [None, 'host', 'guest', 'band', 'fixture'])
def test_import_uses_pinned_repository_not_checkout(tmp_path, azure, monkeypatch, defect):
    fetched = tmp_path / 'mbp-results'
    folder = fetched_prepared(fetched)
    _, build = fake_container(tmp_path / 'project', monkeypatch, only_bytes=defect == 'band')
    committed_repo = gate.REPO
    build(folder)
    settings, local = completion_settings(tmp_path, monkeypatch)
    monkeypatch.setattr(gate, 'GATE', gate.REPO / 'benchmarks/gate')
    monkeypatch.setattr(bundle, 'checked_azure_local', lambda *_: None)
    # A stale or dirty launcher can have unrelated versions of all policy inputs.
    shutil.copytree(committed_repo / 'benchmarks', gate.REPO / 'benchmarks', dirs_exist_ok=True)
    plan = prepare.json_file(folder / 'plan.json')
    if defect is None:
        for name in ('benchmarks/scripts/gate.py', 'benchmarks/gate/gated.json',
                     f'{prepare.FIXTURES}/audit-allocations.json', f'{prepare.FIXTURES}/sha256.json',
                     f'{prepare.FIXTURES}/fixtures/data.cbor'):
            (gate.REPO / name).write_text('wrong checkout bytes')
        session.save(gate.GATE / 'bands/extra.json', {})
    elif defect in ('host', 'guest'):
        body = b'exists only in mutable checkout'
        name = 'gate_batch.py' if defect == 'host' else 'gate.py'
        (gate.REPO / 'benchmarks/scripts' / name).write_bytes(body)
        if defect == 'host':
            (folder / 'input/azure' / name).write_bytes(body)
        else:
            replace_session(folder, plan, lambda members: members.update({f'scripts/{name}': body}))
    elif defect == 'band':
        path = gate.GATE / 'bands/frozen.json'
        band = prepare.json_file(path)
        band['absoluteNoiseBandBytes'] += 1000
        session.save(path, band)
        plan['bands'][band['key']] = dict(record=band, sha256=bundle.sha(path),
                                          contentSha256=hashlib.sha256(gate.encoded(band)).hexdigest())
    else:
        # Reattest both the exported fixture manifest and the portable row identities.
        body = b'fixture from mutable checkout'
        (gate.REPO / prepare.FIXTURES / 'fixtures/data.cbor').write_bytes(body)
        fixture = gate.REPO / prepare.FIXTURES / 'sha256.json'
        session.save(fixture, {'fixtures/data.cbor': hashlib.sha256(body).hexdigest()})
        for row in plan['rows']:
            row['corpusId'] = gate.corpus_identity(row, local)
        def replace_fixtures(members):
            members.update({name: (gate.REPO / name).read_bytes() for name in members if name.startswith(prepare.FIXTURES + '/')})
        for number, timing_plan in enumerate(plan['timePlans']):
            for row in timing_plan['rows']:
                row['corpusId'] = gate.corpus_identity(row, local)
            replace_session(folder, plan, replace_fixtures, number=number)
    session.save(folder / 'plan.json', plan)
    portable = prepare.json_file(folder / 'prepared.json')
    if defect == 'fixture':
        portable['manifests']['fixtures'] = bundle.sha(fixture)
    portable['files'] = {name: bundle.sha(folder / name) for name in portable['files']}
    session.save(folder / 'prepared.json', portable)
    completion_receipt(folder, request=build.request)

    def checked_cli(*_):
        assert defect is None, 'Untrusted input reached CLI'
        return '2.3.4'

    if defect is None:
        assert gate.read_seal(prepare.import_prepared(folder, fetched=fetched, settings=settings, local=local,
                                                       check_cli=checked_cli, check_budget=lambda *_: None))['timePlans']
    else:
        error = {'host': 'host script changed', 'guest': 'guest script changed', 'band': 'derived policy',
                 'fixture': 'manifests differ'}[defect]
        with pytest.raises(ValueError, match=error):
            prepare.import_prepared(folder, fetched=fetched, settings=settings, local=local,
                                     check_cli=checked_cli, check_budget=lambda *_: pytest.fail('Untrusted input reached ledger'))
        assert not list((gate.REPO / 'benchmarks/out').glob('gate-*'))

@pytest.mark.parametrize('defect', [None, 'applied', 'population', 'reject', 'repeat', 'noise', 'duplicate-raw'])
def test_verified_machine_prelude_reaches_real_time_report(tmp_path, monkeypatch, defect):
    import time
    work = tmp_path / 'vm'; work.mkdir()
    noise = defect == 'noise'
    plan = timed(work, a_pairs=5, b_pairs=0 if noise else 5, noise=noise)
    worker = schedule.worker_plan(plan)
    rule = plan['probe']
    reject = defect == 'reject'
    fingerprint = dict(processorCount=8, vendor_id='fixture', model='1', stepping='1', **{'cpu family': '1'})
    monkeypatch.setattr(session, 'host_fingerprint', lambda: fingerprint)
    monkeypatch.setenv('P2_MACHINE_CREATED_UNIX', str(int(time.time())))
    if noise:
        monkeypatch.setenv('P2_AZURE_REGION', plan['region'])
        monkeypatch.setattr(session, 'detect_platform', lambda region: 'azure')

    def member(spec, job, destination, **options):
        record = observation(spec, job, job['counts'], ordering=job.get('orderingProof', False))
        data = record['data']
        data.update(replay=job['replay'], processSettings=dict(gcConfiguration=dict(GCGen0MaxBudget=8388608 if reject else 16777216)))
        if destination.parent.name == 'probe':
            for i, item in enumerate(data['rows']):
                item['nsPerBlock'] += i % 2
        return dict(record, platform='azure' if noise else 'fly', timingRejectedReasons=[])
    monkeypatch.setattr(session, 'member', member)
    assert timing.run(work, worker, work / 'results', prelude=prelude.recipe(plan))
    if defect == 'applied':
        marker = work / 'prelude/applied.json'
        applied = prepare.json_file(marker)
        next(iter(applied.values()))['microInvocations'] += 1
        session.save(marker, applied)
    if defect == 'duplicate-raw':
        raw = next((work / 'prelude/raw').glob('*-calibration.json'))
        raw.write_text(raw.read_text().replace('{\n', '{"seconds":1,\n', 1))
    if defect == 'population':
        path = work / 'results/members.json'
        records = prepare.json_file(path)
        records[0]['data']['rows'][0]['wireBytes'] += 1
        session.save(path, records)
    if reject:
        session.save(work / 'prelude/applied.json', {})
    repeated = defect == 'repeat'
    repeat_values = None
    if repeated:
        repeat_values = prepare.json_file(work / 'prelude/applied.json')
        repeat_worker = prelude.apply(schedule.worker_plan(plan, repeat=True), repeat_values)
        (work / 'results').rename(work / 'first')
        assert timing.run(work, repeat_worker, work / 'results')
    note = tmp_path / 'note'
    session.save(note / 'duration-cost.json', dict(totalEstimateWithReserveUsd=.01, cleanupOutstanding=False, withinSealedGuard=True))
    session.save(note / 'cleanup-proof.json', {'ourAppAbsent': True})
    if noise:
        session.save(note / 'machine.json', dict(provider='azure', platform='azure', machineClass=timing.PROVIDERS['azure']['machine'], region=plan['region']))
    archive = note / 'results.tar.gz'
    with tarfile.open(archive, 'w:gz') as tar:
        for dirname in ('results', 'prelude'):
            tar.add(work / dirname, arcname=dirname)
    (note / 'results.sha256').write_text(bundle.sha(archive) + '\n')
    evidence = bundle.verified_machine(note, plan, 0, repeat=repeated, applied=repeat_values)
    if defect and not noise:
        assert {'applied': 'differs from raw', 'population': 'Fixed work differs', 'reject': 'Unexpected prelude',
                'repeat': 'Unexpected prelude', 'duplicate-raw': 'Duplicate JSON'}[defect] in evidence['invalid']
        return
    assert evidence['invalid'] is None
    payload = bundle.archive_payload(note, plan, machine=evidence)
    assert next(iter(payload['applied'].values()))['microInvocations'] == 10
    assert 'applied' not in bundle.draw_record(evidence, 0, 'first', 1, 'fixture', 'kept')
    path = tmp_path / 'time/0-first.json'
    session.save(path, payload)
    seal = dict(mode='release', timePlans=[plan])
    session.save(tmp_path / 'time/index.json', [dict(session=0, phase='first', ids=[plan['rows'][0]['id']],
                                                    path='time/0-first.json', sha256=bundle.sha(path))])
    if noise:
        name = plan['rows'][0]['id']
        seal.update(provider='azure', platform=dict(size=timing.PROVIDERS['azure']['machine']), noiseRecords=True)
        gate.write_seal(tmp_path, seal)
        with pytest.raises(ValueError, match='Noise band did not pass'):
            bundle.azure_noise_records(tmp_path, seal, dict(payload, pairs=payload['pairs'][:-1]), 0)
        bundle.azure_noise_records(tmp_path, seal, payload, 0)
        records = bundle.finish_noise_records(tmp_path, seal)
        assert records[name]['pairs'] == 5 and records[name]['sourceSeal'] == bundle.sha(tmp_path / 'seal.json')
        assert name not in timing.read_results(tmp_path, seal)[0]
        path = tmp_path / 'time/0-noise-records.json'
        session.save(path, [dict(records[name], sourceSeal='0' * 64)])
        with pytest.raises(ValueError, match='Noise provenance'):
            bundle.finish_noise_records(tmp_path, seal)
        return
    result = timing.read_results(tmp_path, seal)[0][plan['rows'][0]['id']]
    assert result['status'] == 'PASS', result


@pytest.mark.parametrize('defect', [None, 'extra', 'rename', 'guest-snapshot', 'first-no-prelude', 'repeat-mode', 'repeat-prelude', 'repeat-extra', 'repeat-missing'])
def test_az1_authorizes_az2_exact_first_and_repeat(tmp_path, azure, monkeypatch, defect):
    from gate_fly_guard import verified_launch_inputs
    assert prepare.host_files() == bundle.HOST_FILES['azure']
    assert prepare.guest_files() == bundle.AZURE_GUEST_FILES
    assert {'gate_prepare.py', 'gate_prelude.py', 'gate_batch.py'} <= set(prepare.host_files())
    assert 'gate_prelude.py' in prepare.guest_files()
    local, build = fake_container(tmp_path / 'repo', monkeypatch)
    directory = build(tmp_path / 'prepared')
    seal = prepare.json_file(directory / 'plan.json')
    seal.update(files=prepare.json_file(directory / 'prepared.json')['files'], payloadManifests=prepare.inputs(local)[1])
    plan = seal['timePlans'][0]
    worker = schedule.worker_plan(plan)
    first = 'input/session-0.tar.gz'
    ready = directory / 'seal.json'
    host = directory / 'input/azure'

    def verify(relative):
        monkeypatch.setenv('GATE_BUNDLE', relative)
        data = verified_launch_inputs(host, ready)
        return gate_azure_payload.verify_inputs(directory, seal, plan, data)
    if defect in ('extra', 'rename', 'first-no-prelude'):
        def modify(members):
            if defect == 'first-no-prelude':
                return
            source = next(name for name in members if name.endswith(NATIVE_FIXTURE))
            members[source + '.renamed.so'] = members[source]
            if defect == 'rename':
                del members[source]
        replace_session(directory, seal, modify, prelude_override=False if defect == 'first-no-prelude' else None)
        seal['files'][first] = bundle.sha(directory / first)
        gate.write_seal(directory, seal)
        expected = 'archive member/provenance' if defect == 'first-no-prelude' else 'paths or digests differ'
        with pytest.raises(ValueError, match=expected):
            verify(first)
        return
    gate.write_seal(directory, seal)
    if defect == 'guest-snapshot':
        (host / 'gate_prelude.py').write_text('changed')
        with pytest.raises(ValueError, match='input file changed|Unsealed'):
            verify(first)
        return
    assert verify(first) == {'payload': directory / first}

    def member(build, workload, destination, counts, **kwargs):
        job = dict(worker['workloads'][workload])
        if 'invocations' in kwargs:
            job['microInvocations'] = kwargs['invocations']
        return observation(worker['builds'][build], job, counts)
    prelude.execute(prelude.recipe(plan), worker, directory / 'prelude', member)
    note = directory / 'time/runs/fixture'; note.mkdir(parents=True)
    returned = note / 'results.tar.gz'
    with tarfile.open(returned, 'w:gz') as tar:
        tar.add(directory / 'prelude', arcname='prelude')
    session.save(directory / 'time/draws.json', [dict(session=0, phase='first', decision='kept', app='fixture', archiveSha256=bundle.sha(returned))])
    monkeypatch.setattr(timing, 'session_outcome', lambda *args, **kwargs: ('repeat', [row['id'] for row in plan['rows']]))
    repeat = 'input/repeat-0.tar.gz'
    bundle.make_repeat(directory, seal, 0, plan['rows'], repeat)
    if defect and defect.startswith('repeat-'):
        import io
        path = directory / repeat
        with tarfile.open(path) as tar:
            entries = [(item, tar.extractfile(item).read()) for item in tar]
        with tarfile.open(path, 'w:gz') as tar:
            for item, data in entries:
                if defect == 'repeat-mode' and item.name == 'python/bin/python3.12':
                    item.mode = 0o644
                if defect == 'repeat-missing' and item.name.endswith(NATIVE_FIXTURE):
                    continue
                tar.addfile(item, io.BytesIO(data))
            if defect in ('repeat-prelude', 'repeat-extra'):
                data = bundle.gate_encoded(prelude.recipe(plan))
                item = tarfile.TarInfo('prelude.json' if defect == 'repeat-prelude' else 'extra.json')
                item.size = len(data)
                tar.addfile(item, io.BytesIO(data))
        error = 'Repeat executable modes changed' if defect == 'repeat-mode' else 'Repeat archive members changed'
        with pytest.raises(ValueError, match=error):
            verify(repeat)
        return
    assert verify(repeat) == {'payload': directory / repeat}
    with pytest.raises(ValueError, match='Repeat must derive'):
        bundle.verified_bundle(directory, dict(seal, files=dict(seal['files'], **{repeat: bundle.sha(directory / repeat)})), repeat)
    for relative in (first, repeat):
        members = prepare.archive_members(directory / relative)
        assert any(name.endswith(CAPSTONE) for name in members)
        assert any(name.endswith(NATIVE_FIXTURE) for name in members)
    assert 'prelude.json' not in prepare.archive_members(directory / repeat)


@pytest.mark.parametrize('exit_code,expected', [(1, 2), (4, 4)])
def test_noise_refusal_preserves_reason_and_status(tmp_path, azure, monkeypatch, capsys, exit_code, expected):
    import gate_azure
    plan = dict(provider='azure', rows=[dict(id='time/fixture')], probe=dict(maxDraws=1))
    seal = dict(provider='azure', noiseRecords=True, timePlans=[plan], programStopUsd=40)
    monkeypatch.setattr(bundle, 'checked_azure_local', lambda *_: {})
    monkeypatch.setattr(gate_azure, 'reconcile_and_pre_go', lambda *_: None)
    monkeypatch.setattr(bundle, 'azure_notice', lambda *_: {})
    monkeypatch.setattr(gate_azure, 'Ledger', lambda _: SimpleNamespace(money=lambda *_: {}))
    def launch(*args):
        session.save(tmp_path / 'time/launch-started.json', {})
        return exit_code, 'fixture', None
    monkeypatch.setattr(bundle, 'launch_azure', launch)
    monkeypatch.setattr(bundle, 'verified_machine', lambda *args, **kwargs: dict(invalid='fixture refusal detail'))
    with patch.object(bundle, 'finish_noise_records', side_effect=AssertionError('Invalid result must not generate output')):
        assert bundle.fly_sessions(tmp_path, seal, 'go', ('first',), []) == expected
    assert 'fixture refusal detail' in capsys.readouterr().err
    assert not (tmp_path / 'time/noise-records.json').exists()


def test_prelude_allowance_uses_whole_process_charge_and_limits_row_fit(tmp_path):
    plan = timed(tmp_path, a_pairs=5, b_pairs=5)
    row, = plan['rows']
    counted = 2 * row['chargeSeconds'] * (row['aPairs'] + row['bPairs'])
    assert row['chargeSeconds'] == 6
    assert plan['rawSeconds'] == counted + timing.probe_seconds(plan['probe']) + 180
    base = timing.size_sessions([row], 'release', budget=10, probe=probe_rule())[0]
    assert plan['rawSeconds'] > base['rawSeconds']
    # Fit counted work plus the probe by a small margin, but not the three dry processes.
    charge = int((timing.RAW_CAP - timing.probe_seconds(plan['probe'])) // 20)
    marginal = dict(row, chargeSeconds=charge)
    assert timing.size_sessions([marginal], 'release', budget=10, probe=probe_rule())
    with pytest.raises(ValueError, match='Micro set does not fit'):
        timing.size_sessions([marginal], 'release', budget=10, probe=probe_rule(), prelude=True)


@pytest.mark.parametrize('name,field', [('dotnet', 'linuxDotnetManifestSha256'), ('python', 'flyPythonManifestSha256')])
@pytest.mark.parametrize('fault', ['missing-pin', 'wrong-pin', 'changed-raw-manifest'])
def test_import_refuses_unbound_runtime_mirror_manifest(tmp_path, azure, monkeypatch, name, field, fault):
    fetched = tmp_path / 'mbp-results'
    folder = fetched_prepared(fetched, '1')
    build_local, build = fake_container(tmp_path / 'guest', monkeypatch)
    build(folder)
    settings, local = completion_settings(tmp_path, monkeypatch)
    for prefix, root_key, pin in [('dotnet', 'linuxDotnetRoot', 'linuxDotnetManifestSha256'), ('python', 'flyPythonRoot', 'flyPythonManifestSha256')]:
        manifest = Path(build_local[root_key]) / 'sha256.json'
        local[pin] = bundle.sha(manifest)
        local.pop(root_key, None)
    if fault == 'missing-pin':
        local.pop(field)
    elif fault == 'wrong-pin':
        local[field] = '0' * 64
    else:
        path = folder / 'input/mirrors' / f'{name}.json'
        path.parent.mkdir(parents=True, exist_ok=True)
        root_key = 'linuxDotnetRoot' if name == 'dotnet' else 'flyPythonRoot'
        path.write_bytes((Path(build_local[root_key]) / 'sha256.json').read_bytes() + b'\n')
        portable_path = folder / 'prepared.json'
        portable = prepare.json_file(portable_path)
        portable['files'][path.relative_to(folder).as_posix()] = bundle.sha(path)
        portable['manifests'][name] = bundle.sha(path)
        session.save(portable_path, portable)
    session.save(settings, local)
    completion_receipt(folder, request=build.request)
    with pytest.raises(ValueError, match=field):
        prepare.import_prepared(folder, fetched=fetched, settings=settings, local=local,
                                 check_cli=lambda *_: pytest.fail('Untrusted mirror reached CLI'),
                                 check_budget=lambda *_: pytest.fail('Untrusted mirror reached ledger'))
    assert not list((gate.REPO / 'benchmarks/out').glob('gate-*'))
