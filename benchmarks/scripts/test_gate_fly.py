"""Adapted RECV1 two-seat lifecycle regressions; fake Fly only, no real token."""
import ast
import contextlib
import datetime as dt
import copy
import fcntl
import io
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tarfile
import tempfile
import time
import unittest
from unittest.mock import patch

import gate
import gate_bundle as bundle
import gate_fly_guard as guard
import gate_time as timing
import gate_schedule as schedules
from test_gate_time import pairs, row, probe_records, probe_rule
from test_gate_session import data as replay_observations

HERE = Path(__file__).resolve().parent
APP = 'chrysalis-gate-20990405t000100-abcdef12'
LINUX = sys.platform.startswith('linux')
TEST_WATCHDOG_SECONDS = 30


def fixture(root, *, watchdog_delay=None, region=None, bounded=True):
    root = Path(root)
    host = root / 'input/fly'
    host.mkdir(parents=True)
    for name in bundle.HOST_FILES['fly']:
        shutil.copy2(HERE / name, host / name)
    if watchdog_delay is not None:
        path = host / 'gate_fly_watchdog.sh'
        path.write_text(path.read_text().replace('remaining=$((CREATED_UNIX + GUARD - 70 - $(date +%s)))', 'remaining=' + str(watchdog_delay)))
    if bounded:
        # Bound scratch owners even if a failed test removes the proof directory.
        # The production watchdog/common scripts are not changed.
        common = host / 'gate_fly_common.sh'
        common.write_text(common.read_text().replace('deadline > 0 &&', f'SECONDS >= {TEST_WATCHDOG_SECONDS} || deadline > 0 &&'))
        watchdog = host / 'gate_fly_watchdog.sh'
        watchdog.write_text(watchdog.read_text().replace('if (( remaining > 0 ));', f'(( remaining <= {TEST_WATCHDOG_SECONDS} )) || remaining={TEST_WATCHDOG_SECONDS}\nif (( remaining > 0 ));'))
    stub = root / 'stubbin'; stub.mkdir()
    shutil.copy2(HERE / 'fixtures/fly/fly', stub / 'fly')
    for path in (HERE/'fixtures/fly').glob('*.json'):shutil.copy2(path,stub/path.name)
    fake = stub / 'fly'
    source = fake.read_text()
    marker = "        with tarfile.open(Path(a[-1]), 'w:gz') as tar:"
    source = source.replace(marker, "        probes = json.loads((root / 'fixture-probe.json').read_text())\n"
                            "        if complete: status['probe'] = 'kept'\n" + marker + "\n"
                            "            for probe_name, value in probes.items():\n"
                            "                if probe_name == 'results/host.json' and (root / 'fixture-host.json').exists():\n"
                            "                    value.update(json.loads((root / 'fixture-host.json').read_text()))\n"
                            "                blob = json.dumps(value).encode(); info = tarfile.TarInfo(probe_name)\n"
                            "                info.size = len(blob); tar.addfile(info, io.BytesIO(blob))")
    source = source.replace("            if (root / 'fixture-host.json').is_file():\n                tar.add(root / 'fixture-host.json', arcname='results/host.json')\n", '')
    fake.write_text(source)
    fake.chmod(0o755)
    (root / 'token.fixture').write_text('preflight-not-a-token')
    gate.session.save(root / 'local.json', dict(flyTokenFile=str(root / 'token.fixture'), flyLock=str(root / 'test.lock'), flyOrg='fixture-org', corporaRoot='fixture', pinnedBaseline='fixture', flyBudgetLeftUsd=40))
    mode = 'change' if region else 'release'
    entry = dict(row(pairs=8), region=region, aPairs=0) if region else row()
    plan = probe_plan(entry, mode)
    gate.session.save(root / 'fixture-probe.json', probe_parts(plan, 'keep'))
    gate.session.save(root / 'fixture-pairs.json', pairs(plan['rows'][0]))
    source = root / 'bundle'; source.mkdir()
    gate.session.save(source / 'plan.json', schedules.worker_plan(plan))
    (source / 'placeholder.txt').write_text('OBVIOUS TEST PLACEHOLDER; never a product binary')
    bundle.pack(source, root / 'input/session-0.tar.gz')
    seal = dict(flyBudgetLeftUsd=40, mode=mode, ruleVersion=gate.stats.RULE_VERSION, rows=plan['rows'], timePlans=[plan], bands={},
                canonical=True, timeScopeDecided=True, gated=dict(areas={}), releaseSet=[r['id'] for r in plan['rows']],
                image=bundle.IMAGE, imageIndex=bundle.IMAGE_INDEX, flyctlVersion='fly fixture-v1', files={
                    path.relative_to(root).as_posix(): gate.digest(path) for path in host.iterdir() if path.is_file()})
    seal['files']['input/session-0.tar.gz'] = gate.digest(root / 'input/session-0.tar.gz')
    gate.write_seal(root, seal)
    env = dict(os.environ, PATH=str(stub) + os.pathsep + os.environ['PATH'], CHRYSALIS_GATE_LOCAL=str(root / 'local.json'),
               GATE_BUNDLE='input/session-0.tar.gz', GATE_STUB_STATE=str(root / 'state.json'), GATE_STUB_LOG=str(root / 'argv.jsonl'),
               GATE_STUB_LOCK=str(root / 'test.lock'), GATE_STUB_PAIRS=str(root / 'fixture-pairs.json'),
               FLY_ACCESS_TOKEN='CANARY_ambient', FLY_REFRESH_TOKEN='CANARY_ambient', FLY_CONFIG_DIR='CANARY_ambient')
    return host, seal, env


def launch(root, env, *, send=None, bash='bash', stdin=None):
    with (root / 'console.log').open('w') as stream:
        process = subprocess.Popen([bash, '-x', str(root / 'input/fly/gate_fly.sh'), '--approved', APP, str(root / 'seal.json')],
                                   env=env, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True, stdin=stdin)
        if send:
            deadline = time.monotonic() + 10
            while not (root / 'state.json').exists() or not json.loads((root / 'state.json').read_text()).get('sessionStarted'):
                if process.poll() is not None or time.monotonic() >= deadline:
                    raise RuntimeError('Signal fixture never reached session')
                time.sleep(.02)
            if send == 'group-kill':
                os.killpg(process.pid, signal.SIGKILL)
            else:
                process.send_signal(signal.SIGTERM if send == 'term' else signal.SIGINT)
        status = process.wait(timeout=25)
    return status


def calls(root):
    return [json.loads(line) for line in (root / 'argv.jsonl').read_text().splitlines()] if (root / 'argv.jsonl').exists() else []


def loads_draws(root):
    return json.loads((root / 'time/draws.json').read_text())


def probe_plan(entry, mode):
    plan, = timing.size_sessions([entry], mode, budget=40, probe=probe_rule())
    return schedules.prepare(dict(plan, probeJob=dict(kind='time', replay='offline')))


REJECTION_CASES = {'cv-over-limit', 'gen0-budget-mismatch', 'forged', 'late', 'rejected no proof', 'wrong probe identity', 'wrong probe workload'}


def probe_parts(plan, kind):
    rejected = kind in REJECTION_CASES
    reasons = [kind if kind in ('cv-over-limit', 'gen0-budget-mismatch') else 'cv-over-limit'] if rejected else []
    noisy = (kind in REJECTION_CASES and kind not in ('forged', 'gen0-budget-mismatch')) or kind == 'kept-over-limit'
    budget = 8388608 if kind == 'gen0-budget-mismatch' else 16777216
    rule = plan['probe']
    report = dict(decision='rejected' if rejected else 'kept', reasons=reasons, statistic='cv', limit=rule['limit'],
                  cv=(60 / 59) ** .5 / (2 if noisy else 100), lag1=-59 / 60,
                  gen0BudgetBytes=dict(expected=rule['gen0BudgetBytes'], observed=[budget]))
    worker = schedules.worker_plan(plan)
    reference = worker['builds'][worker['probe']['build']]
    identity = {key: reference[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')}
    records = probe_records(noisy=noisy, budget=budget)
    for record in records.values():
        observed = replay_observations()
        observed.update(runtime=identity['runtime'], replay='offline', processSettings=record['data']['processSettings'])
        for item, sample in zip(observed['rows'], record['data']['rows'], strict=True):
            item.update(sample)
        record.update(identity=identity, platform='fly', data=observed)
        if kind == 'wrong probe identity':
            record['identity'] = dict(commit='f' * 40, treeId='e' * 40, runtime='8.0.1', dllSha256s={})
        if kind == 'wrong probe workload':
            observed.update(replay='tcp', warmupPasses=1)
    parts = {f'results/probe/{name}.json': record for name, record in records.items()}
    parts['results/probe.json'] = report
    parts['results/host.json'] = {'vendor_id': 'AuthenticAMD', 'cpu family': '25', 'model': '33', 'stepping': '0',
                                 'processorCount': 8, 'microcode': None, 'flagsSha256': 'a' * 64}
    return parts


def probe_archive(note, plan, kind, *, selected=None, eligible=False):
    note.mkdir(parents=True)
    repeat = selected is not None
    worker = schedules.worker_plan(plan, repeat=repeat, selected=selected)
    gate.session.save(note / 'verified-plan.json', worker)
    parts = probe_parts(plan, kind)
    report = parts['results/probe.json']
    rejected = report['decision'] == 'rejected'
    status = dict(probe=report['decision'], completedAllDeclaredRounds=not rejected, exitCode=0)
    if kind == 'worker failure':
        status.update(exitCode=2, completedAllDeclaredRounds=False)
        parts['results/recovery-diagnostic.json'] = {}
    parts['results/session-status.json'] = status
    if not rejected:
        rows = {r['id']: r for r in plan['rows']}
        observations = {(p['id'], p['phase'], p['pair']): p for r in rows.values() for p in pairs(r, 1.03, a=[.85] * r['aPairs'] if eligible else None)}
        parts['results/members.json'] = [dict(observations[block['row'], block['phase'], pair['pair']][role], id=identifier,
                                             build=worker['members'][identifier]['build'])
                                         for block in plan['repeatSchedule' if repeat else 'schedule'] for pair in block['pairs']
                                         for role, identifier in pair['members'].items() if identifier in worker['members']]
    if kind == 'no probe': del parts['results/probe.json']
    if kind == 'no host': del parts['results/host.json']
    if kind == 'one cpu': parts['results/host.json']['processorCount'] = 1
    if kind == 'empty fingerprint': parts['results/host.json'] = dict(processorCount=8)
    if kind == 'empty kept': parts['results/members.json'] = []
    if kind == 'corrupt probe': parts['results/probe.json'] = b'{'
    archive = note / 'results.tar.gz'
    with tarfile.open(archive, 'w:gz') as stream:
        for name, value in parts.items():
            contents = value if isinstance(value, bytes) else bundle.gate_encoded(value)
            info = tarfile.TarInfo(name); info.size = len(contents)
            stream.addfile(info, io.BytesIO(contents))
    (note / 'results.sha256').write_text(('0' * 64 if kind in ('bad hash', 'bad hash and cost') else gate.digest(archive)) + '\n')
    if kind not in ('no proof', 'rejected no proof'): gate.session.save(note / 'cleanup-proof.json', dict(ourApp=note.name, ourAppAbsent=True))
    booked = .07 if kind != 'late' else .18
    if kind == 'bad hash and cost': booked = 4.25
    within = kind not in ('past guard', 'bad hash and cost')
    cost = dict(cleanupOutstanding=False, totalEstimateWithReserveUsd=booked, withinSealedGuard=within)
    (note / 'duration-cost.json').write_text(json.dumps(dict(cost, totalEstimateWithReserveUsd=float('nan')) if kind == 'unknown cost' else cost))
    with patch.object(sys, 'argv', ['gate_fly_guard.py', 'result', str(archive), str(note)]):
        try:
            guard.main()
        except RuntimeError:
            return 1
    return 5 if (note / 'probe-rejected.json').exists() else 0


def reseal_fixture(root, seal):
    plan = schedules.prepare(seal['timePlans'][0])
    gate.session.save(root / 'bundle/plan.json', schedules.worker_plan(plan))
    seal['files']['input/session-0.tar.gz'] = bundle.pack(root / 'bundle', root / 'input/session-0.tar.gz')
    return gate.write_seal(root, seal)


def fake_dispatch(root, seal, env, attempts, *, launch_limit=None):
    go = (root / 'seal.id').read_text().strip()
    launches = []
    def launch(command, *, env, stdin, **kwargs):
        position = len(launches)
        if position >= (len(attempts) if launch_limit is None else launch_limit):
            raise AssertionError('Unexpected launch')
        phase = 'repeat' if '/repeat-' in env['GATE_BUNDLE'] else 'first'
        launches.append((phase, env.get('GATE_FIRST_GO')))
        ready = root / 'seal.json'
        dispatch_fd = stdin.fileno() if stdin else 0
        if stdin:
            with patch.dict(os.environ, env, clear=True):
                guard.verified_launch_inputs(root / 'input/fly', ready, dispatch_fd=dispatch_fd)
        if env.get('GATE_FIRST_GO'):
            guard.consume_go(ready, go, dispatch_fd=dispatch_fd)
        row_ids = timing.session_outcome(root, seal, 0, first_only=True)[1] if phase == 'repeat' else None
        kind, eligible = attempts[position]
        return probe_archive(root / 'time/runs' / command[3], seal['timePlans'][0], kind, selected=row_ids, eligible=eligible)
    with patch.dict(os.environ, env), patch.object(bundle, 'held_fly_go', return_value=(root, seal, go)), \
            patch.object(bundle, 'owned_call', side_effect=launch), contextlib.redirect_stdout(io.StringIO()):
        status = bundle.fly_held(root)
    return status, launches


def retry_parent(repo, kind, *, finish=True, expected='FAIL', eligible=True):
    root = repo / 'benchmarks/out/gate-original'
    _, seal, env = fixture(root, bounded=False)
    plan = seal['timePlans'][0]
    plan['rows'][0]['expected'] = dict(release=expected, change='PASS')
    seal['rows'] = plan['rows']
    go = reseal_fixture(root, seal)
    authority = root.parent / 'controls-authority.json'
    gate.session.save(authority, dict(originalSealId=go, mode='release', rows=[dict(id=plan['rows'][0]['id'], expected=plan['rows'][0]['expected'])]))
    if finish:
        attempts = [('cv-over-limit', False), ('keep', eligible and kind == 'first')]
        if kind == 'repeat':
            attempts.append(('keep', eligible))
        if kind == 'repeat' and expected == 'PASS':
            # Make a fully verified archive for a phase that the classifier itself would not authorize.
            with patch.object(timing, 'session_outcome', return_value=('repeat', [plan['rows'][0]['id']])):
                _, launches = fake_dispatch(root, seal, env, attempts)
        else:
            _, launches = fake_dispatch(root, seal, env, attempts)
        assert len(launches) == len(attempts)
        if eligible and expected == 'FAIL':
            assert timing.session_outcome(root, seal, 0)[0] == 'retry-eligible'
    return root, seal, env, authority, go


def retry_child(repo, parent, kind):
    with patch.object(gate, 'REPO', repo):
        go = gate.retry_command(parent, kind=kind)
    child, = [path.parent for path in parent.parent.glob('gate-*/seal.id') if path.read_text().strip() == go]
    return child, go


class GuardTests(unittest.TestCase):
    def test_retry_seal_first_and_repeat(self):
        for kind in ('first', 'repeat'):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as name:
                repo = Path(name)
                parent, original, env, _, original_go = retry_parent(repo, kind)
                (parent / 'data').mkdir()
                (parent / 'data/local-note').write_text('ORIGINAL ALLOCATION EVIDENCE')
                before = {path: gate.digest(path) for path in parent.rglob('*') if path.is_file()}
                child, go = retry_child(repo, parent, kind)
                seal = gate.read_seal(child)
                self.assertNotEqual(go, original_go)
                self.assertEqual(seal['files'], original['files'])
                self.assertEqual(seal['retryOf']['kind'], kind)
                self.assertEqual(gate.outcome(seal, {row['id']: dict(status='PASS') for row in seal['rows']}), 'SUBSET')
                self.assertFalse((child / 'data').exists())
                local = json.loads((parent / 'local.json').read_text())
                local['flyBudgetLeftUsd'] = original['timePlans'][0]['limitUsd']
                with patch.object(gate, 'local_config', return_value=local):
                    if kind == 'repeat':
                        bundle.checked_fly_local(seal)
                    else:
                        with self.assertRaisesRegex(ValueError, 'budget'):
                            bundle.checked_fly_local(seal)
                attempts = [('keep', False)] * (1 if kind == 'repeat' else 2)
                with patch.object(gate, 'REPO', repo):
                    _, launches = fake_dispatch(child, seal, env, attempts)
                self.assertEqual(launches[0], (kind, go))
                self.assertEqual(len(launches), len(attempts))
                with patch.object(gate, 'REPO', repo), patch.dict(os.environ, env), patch.object(bundle, 'owned_call') as launch:
                    with self.assertRaisesRegex(ValueError, 'already consumed'):
                        bundle.fly_command(go)
                    with self.assertRaisesRegex(ValueError, 'not a retry child'):
                        gate.retry_command(child, kind=kind)
                launch.assert_not_called()
                if kind == 'repeat':
                    self.assertEqual((child / 'time/0-first.json').read_bytes(), (parent / 'time/0-first.json').read_bytes())
                    report = gate.report(child)
                    self.assertIn('repeat', report['rows'][seal['timePlans'][0]['rows'][0]['id']])
                    self.assertIn('allocation evidence remains in the original', report['text'])
                    self.assertNotEqual(report['outcome'], 'RELEASE PASS')
                self.assertEqual(before, {path: gate.digest(path) for path in parent.rglob('*') if path.is_file()})

    def test_retry_refusals(self):
        for fault in ('wrong kind', 'no authority', 'link authority', 'wrong original', 'wrong mode', 'wrong rows',
                      'missing cleanup', 'wrong cleanup app', 'changed draw', 'unindexed draw', 'changed payload',
                      'forged repeat payload', 'linked input', 'linked claims', 'already claimed'):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as name:
                repo = Path(name)
                kind = 'repeat' if fault == 'forged repeat payload' else 'first'
                parent, seal, _, authority, go = retry_parent(repo, kind)
                data = json.loads(authority.read_text())
                if fault == 'no authority': authority.unlink()
                if fault == 'link authority':
                    alternate = authority.with_suffix('.copy'); authority.rename(alternate); authority.symlink_to(alternate)
                if fault == 'wrong original':
                    go = gate.write_seal(parent, dict(seal, canonical=not seal['canonical']))
                    gate.session.save(parent / 'time/launch-started.json', dict(sealId=go))
                if fault == 'wrong mode': data['mode'] = 'change'
                if fault == 'wrong rows': data['rows'][0]['expected'] = 'smoke'
                if fault in ('wrong mode', 'wrong rows'): gate.session.save(authority, data)
                if fault in ('missing cleanup', 'wrong cleanup app'):
                    proof = parent / 'time/runs' / loads_draws(parent)[0]['app'] / 'cleanup-proof.json'
                    if fault == 'missing cleanup': proof.unlink()
                    else: gate.session.save(proof, dict(ourAppAbsent=True, ourApp='another-app'))
                if fault == 'changed draw':
                    draws = loads_draws(parent); draws.reverse(); gate.session.save(parent / 'time/draws.json', draws)
                if fault == 'unindexed draw': (parent / 'time/runs/chrysalis-gate-20990405t000100-ffffffff').mkdir()
                if fault == 'changed payload': (parent / 'time/0-first.json').write_text('{}')
                if fault == 'forged repeat payload':
                    path = parent / 'time/0-repeat.json'
                    payload = json.loads(path.read_bytes()); payload['context']['session'] = 'unrelated'
                    gate.session.save(path, payload)
                    index = json.loads((parent / 'time/index.json').read_bytes())
                    index[1]['sha256'] = gate.digest(path)
                    gate.session.save(parent / 'time/index.json', index)
                if fault == 'linked input':
                    path = parent / 'input/session-0.tar.gz'
                    alternate = path.with_suffix('.copy'); path.rename(alternate); path.symlink_to(alternate)
                if fault == 'linked claims':
                    (parent.parent / 'retry-claims').symlink_to(repo)
                if fault == 'already claimed':
                    claims = parent.parent / 'retry-claims'; claims.mkdir(); (claims / go).touch()
                with patch.object(gate, 'REPO', repo), self.assertRaises((ValueError, OSError)):
                    gate.retry_command(parent, kind='repeat' if fault == 'wrong kind' else kind)
                self.assertEqual(list(parent.parent.glob('gate-*')), [parent])

    def test_retry_stale_authority_and_single_use_claim(self):
        with tempfile.TemporaryDirectory() as name:
            repo = Path(name)
            parent, _, env, authority, original_go = retry_parent(repo, 'first')
            child, go = retry_child(repo, parent, 'first')
            second, second_go = retry_child(repo, parent, 'first')
            self.assertNotEqual(go, second_go)
            claim = child.parent / 'retry-claims' / original_go
            marker = child / 'time/launch-started.json'
            raw = authority.read_bytes(); authority.write_bytes(raw + b' ')  # Same JSON, different authority bytes.
            with patch.object(gate, 'REPO', repo), patch.object(bundle, 'held_fly_go', return_value=(child, gate.read_seal(child), go)), \
                    patch.object(bundle, 'owned_call') as launch, self.assertRaisesRegex(ValueError, 'authority|binding'):
                bundle.fly_held(child)
            launch.assert_not_called()
            self.assertFalse(claim.exists()); self.assertFalse(marker.exists())
            authority.write_bytes(raw)
            marker.mkdir(parents=True)  # Simulate loss after atomically acquiring the claim, before writing the GO marker.
            with bundle.retry_dispatch(child, go) as dispatch, self.assertRaises(OSError) as failure:
                guard.consume_go(child / 'seal.json', go, dispatch_fd=dispatch.fileno())
            self.assertEqual(Path(failure.exception.filename), marker)
            self.assertTrue(claim.is_file())
            (second / 'time').mkdir()
            with bundle.retry_dispatch(second, second_go) as dispatch, self.assertRaises(FileExistsError):
                guard.consume_go(second / 'seal.json', second_go, dispatch_fd=dispatch.fileno())
            self.assertFalse((second / 'time/launch-started.json').exists())

    def test_retry_rechecks_reused_first_bytes_under_lock(self):
        with tempfile.TemporaryDirectory() as name:
            repo = Path(name)
            parent, _, _, _, original_go = retry_parent(repo, 'repeat')
            child, go = retry_child(repo, parent, 'repeat')
            first = parent / 'time/0-first.json'
            first.write_bytes(first.read_bytes() + b' ')
            index = json.loads((parent / 'time/index.json').read_bytes())
            index[0]['sha256'] = gate.digest(first)
            gate.session.save(parent / 'time/index.json', index)
            with patch.object(bundle, 'held_fly_go', return_value=(child, gate.read_seal(child), go)), \
                    patch.object(bundle, 'owned_call') as launch, self.assertRaisesRegex(ValueError, 'binding'):
                bundle.fly_held(child)
            launch.assert_not_called()
            self.assertFalse((child / 'time').exists())
            self.assertFalse((child.parent / 'retry-claims' / original_go).exists())

    def test_retry_refuses_legacy_helpers(self):
        with tempfile.TemporaryDirectory() as name:
            repo = Path(name)
            parent, seal, _, authority, _ = retry_parent(repo, 'first')
            relative = 'input/fly/gate_fly_guard.py'
            path = parent / relative
            path.write_bytes(path.read_bytes() + b'\n# old helper')
            seal['files'][relative] = gate.digest(path)
            go = gate.write_seal(parent, seal)
            gate.session.save(parent / 'time/launch-started.json', dict(sealId=go))
            gate.session.save(authority, dict(json.loads(authority.read_bytes()), originalSealId=go))
            with patch.object(gate, 'REPO', repo), self.assertRaisesRegex(ValueError, 'retry-capable launcher'):
                gate.retry_command(parent, kind='first')

    def test_retry_refuses_ineligible_or_unconsumed_original(self):
        cases = [('first', 'PASS', False, False, 'First phase is not retry-eligible'),
                 ('repeat', 'PASS', False, False, 'First phase did not authorize a repeat'),
                 ('repeat', 'FAIL', False, False, 'Repeat phase is not retry-eligible'),
                 ('first', 'FAIL', True, True, 'consumed original seal')]
        for kind, expected, eligible, unconsumed, message in cases:
            with self.subTest(message=message), tempfile.TemporaryDirectory() as name:
                repo = Path(name)
                parent, _, _, _, _ = retry_parent(repo, kind, expected=expected, eligible=eligible)
                if unconsumed:
                    gate.session.save(parent / 'time/launch-started.json', dict(sealId='0' * 64))
                with patch.object(gate, 'REPO', repo), self.assertRaisesRegex(ValueError, message):
                    gate.retry_command(parent, kind=kind)

    def test_retry_rejects_unstarted_and_completed_continuation(self):
        with tempfile.TemporaryDirectory() as name:
            repo = Path(name)
            parent, _, env, _, _ = retry_parent(repo, 'first')
            completed, completed_go = retry_child(repo, parent, 'first')
            sibling, _ = retry_child(repo, parent, 'first')
            _, launches = fake_dispatch(completed, gate.read_seal(completed), env,
                                       [('cv-over-limit', False), ('keep', False), ('keep', False)])
            self.assertEqual([phase for phase, _ in launches], ['first', 'first', 'repeat'])
            for child in (sibling, completed):
                with self.subTest(child=child.name), patch.dict(os.environ, env, clear=True):
                    with self.assertRaisesRegex((ValueError, RuntimeError), 'active retry dispatch'):
                        guard.verified_launch_inputs(child / 'input/fly', child / 'seal.json')
            with bundle.retry_dispatch(completed, completed_go) as other, patch.dict(os.environ, env, clear=True):
                with self.assertRaisesRegex(RuntimeError, 'active retry dispatch'):
                    guard.verified_launch_inputs(completed / 'input/fly', completed / 'seal.json', dispatch_fd=other.fileno())

    def test_retry_binds_restored_first_at_archive_and_report_read(self):
        with tempfile.TemporaryDirectory() as name:
            repo = Path(name)
            parent, _, env, _, _ = retry_parent(repo, 'repeat')
            child, _ = retry_child(repo, parent, 'repeat')
            seal = gate.read_seal(child)
            fake_dispatch(child, seal, env, [('keep', False)])
            index_file = child / 'time/index.json'
            index = json.loads(index_file.read_bytes())
            gate.session.save(index_file, index[:1])
            wrong = dict(seal, retryOf=dict(seal['retryOf'], repeatIds=['unrelated']))
            with self.assertRaisesRegex(ValueError, 'sealed retry selection'):
                bundle.verified_bundle(child, wrong, 'input/repeat-0.tar.gz')
            with self.assertRaisesRegex(ValueError, 'cannot launch a first bundle'):
                bundle.verified_bundle(child, seal, 'input/session-0.tar.gz')
            first = child / 'time/0-first.json'
            first.write_bytes(first.read_bytes() + b' ')
            index[0]['sha256'] = gate.digest(first)
            gate.session.save(index_file, index)
            for label, read in (('archive', lambda: bundle.verified_bundle(child, seal, 'input/repeat-0.tar.gz')),
                                ('report', lambda: gate.report(child))):
                with self.subTest(read=label), self.assertRaisesRegex(ValueError, 'sealed retry first'):
                    read()

    def test_retry_recovers_after_original_token_preflight(self):
        with tempfile.TemporaryDirectory() as name:
            repo = Path(name).resolve()
            parent, seal, env, _, go = retry_parent(repo, 'first', finish=False)
            bash = shutil.which('bash', path='/opt/homebrew/bin') if not LINUX else shutil.which('bash')
            if not bash:
                self.skipTest('Portable launcher preflight needs Bash >= 4')
            if not LINUX:
                # Mac stand-ins reach only the real shell's missing fixture-token branch.
                for tool, body in (('flock', 'exit 0'), ('timeout', 'shift; exec "$@"')):
                    path = parent / 'stubbin' / tool
                    path.write_text('#!/bin/sh\n' + body + '\n'); path.chmod(0o755)
            token = parent / 'token.fixture'
            token.unlink()
            self.assertNotEqual(launch(parent, dict(env, GATE_FIRST_GO=go), bash=bash), 0)
            self.assertIn('Fly token file missing or empty', (parent / 'console.log').read_text())
            self.assertFalse((parent / 'time/launch-started.json').exists())
            self.assertFalse((parent / 'time/runs' / APP).exists())
            self.assertEqual(calls(parent), [])
            token.write_text('preflight-not-a-token')
            fake_dispatch(parent, seal, env, [('cv-over-limit', False), ('keep', True)])
            self.assertEqual(timing.session_outcome(parent, seal, 0)[0], 'retry-eligible')
            retry_child(repo, parent, 'first')  # No deletion of the original's preflight audit.

    def probe_dispatch(self, root, kinds, *, launch_limit=None):
        _, seal, env = fixture(root)
        plan = seal['timePlans'][0]
        repeat = kinds[-1] != 'keep'
        plan.update(probe=probe_rule(), probeJob=dict(kind='time', replay='offline'), repeatAllowed=repeat)
        plan['limitUsd'] = timing.price(plan['destroyGuardSeconds']) + 5 * timing.price(1068)
        plan['limitWithRepeatUsd'] = (2 if repeat else 1) * plan['limitUsd']
        go = reseal_fixture(root, seal)
        status, launches = fake_dispatch(root, seal, env, [(kind, False) for kind in kinds], launch_limit=launch_limit)
        self.assertEqual(launches, [('first', go)] + [('first', None)] * (len(launches) - 1))
        index = json.loads((root / 'time/index.json').read_text())
        self.assertEqual(len(index), 1)
        path = root / index[0]['path']
        self.assertEqual(gate.digest(path), index[0]['sha256'])
        return status, json.loads(path.read_text()), loads_draws(root), len(launches)

    def test_probe_redraws_within_one_go(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            status, payload, draws, launches = self.probe_dispatch(root, ['cv-over-limit', 'gen0-budget-mismatch', 'keep'], launch_limit=3)
            self.assertEqual(launches, 3)
            self.assertNotEqual(status, 0)  # known slow measured B; repeat disabled below
            self.assertIsNone(payload['invalid'])
            self.assertEqual(payload['probe']['decision'], 'kept')
            self.assertEqual(len(payload['probeRecords']), 16)
            self.assertEqual([draw['decision'] for draw in draws], ['rejected', 'rejected', 'kept'])
            self.assertEqual(draws[1]['probe']['gen0BudgetBytes'], dict(expected=16777216, observed=[8388608]))
            self.assertIn('gen0-budget-mismatch', (root / 'report.txt').read_text())

    def test_probe_review_seal_must_probe_before_dispatch(self):
        for rule in ('missing', None, dict(probe_rule(), processes=0), probe_rule()):
            with self.subTest(rule=rule), tempfile.TemporaryDirectory() as name:
                repo = Path(name); root = repo / 'benchmarks/out/gate-no-probe'
                _, seal, env = fixture(root)
                legacy = dict(seal['timePlans'][0])
                legacy.pop('probe', None); legacy.pop('probeJob', None)
                schedules.prepare(legacy)
                gate.session.save(root / 'bundle/plan.json', schedules.worker_plan(legacy))
                seal['files']['input/session-0.tar.gz'] = bundle.pack(root / 'bundle', root / 'input/session-0.tar.gz')
                if rule == 'missing':
                    seal['timePlans'][0].pop('probe', None)
                else:
                    seal['timePlans'][0].update(probe=rule, probeJob=dict(kind='time', replay='offline'))
                schedules.prepare(seal['timePlans'][0])
                go = gate.write_seal(root, seal)
                with patch.object(gate, 'REPO', repo), patch.dict(os.environ, env), patch.object(bundle, 'owned_call') as launch:
                    with self.assertRaisesRegex(ValueError, 'probe'):
                        bundle.fly_command(go, directory=root)
                    launch.assert_not_called()
                self.assertFalse((root / 'time/launch-started.json').exists())
                self.assertNotEqual(gate.report(root)['outcome'], 'RELEASE PASS')

    def test_probe_review_filtered_repeat_validates_its_members_and_original_reference(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            plan = probe_plan(row(), 'release')
            alternate = row('tcp')
            alternate['sides']['baseline']['commit'] = 'c' * 40
            plan['rows'].append(alternate)
            schedules.prepare(plan)
            selected = [alternate['id']]
            for kind in ('keep', 'empty kept'):
                with self.subTest(kind=kind):
                    note = root / kind
                    self.assertEqual(probe_archive(note, plan, kind, selected=selected), 0)
                    machine = bundle.verified_machine(note, plan, 0, repeat=True, selected=selected)
                    if kind == 'keep':
                        self.assertIsNone(machine['invalid'])
                        self.assertEqual(len(machine['probeRecords']), 16)
                    else:
                        self.assertIn('members differ', machine['invalid'])

    def test_probe_review_cost_warning_survives_bad_archive(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            _, payload, draws, launches = self.probe_dispatch(root, ['bad hash and cost'])
            self.assertEqual(launches, 1)
            self.assertIn('hash', payload['invalid'])
            self.assertEqual(draws[0]['bookedUsd'], 4.25)
            text = (root / 'report.txt').read_text()
            self.assertIn('booked $4.250000', text)
            self.assertIn('DRAW WARNING: booked cost past machine guard', text)
            self.assertIn('DRAW WARNING: booked cost exceeds session reservation', text)

    def test_probe_and_machine_refusals(self):
        for kinds, message in [
            (['forged'], 'probe'), (['no probe'], 'probe'), (['corrupt probe'], 'probe'),
            (['worker failure'], 'launcher'), (['no proof'], 'absence'), (['no host'], 'fingerprint'),
            (['rejected no proof'], 'absence'), (['wrong probe identity'], 'probe'), (['wrong probe workload'], 'probe'),
            (['empty kept'], 'members'), (['one cpu'], 'fingerprint'), (['empty fingerprint'], 'fingerprint'),
            (['bad hash'], 'hash'), (['unknown cost'], 'cost'), (['late'] * 4, 'reservation exhausted'),
            (['cv-over-limit'] * 6, 'no machine kept after 6 draws'),
            (['cv-over-limit'] * 5 + ['past guard'], 'guard'), (['kept-over-limit'], 'probe'),
        ]:
            with self.subTest(kinds=kinds), tempfile.TemporaryDirectory() as name:
                status, payload, draws, launches = self.probe_dispatch(Path(name), kinds)
                self.assertNotEqual(status, 0)
                self.assertEqual(launches, len(kinds))
                self.assertIn(message, payload['invalid'])
                self.assertEqual(len(draws), launches)

    def test_guard_accepts_indented_recorded_platform_shapes(self):
        recorded = HERE/'fixtures/fly'
        listing = (recorded/'machines-ready.json').read_text()
        call = subprocess.run([sys.executable,str(HERE/'gate_fly_guard.py'),'neighbours'],input=listing,text=True,capture_output=True)
        self.assertEqual(call.returncode,0,call.stderr)
        self.assertEqual(json.loads(call.stdout)['performance'],1)
        with tempfile.TemporaryDirectory() as td:
            root=Path(td);plan=timing.size_sessions([row()],'release',budget=40)[0]
            command=guard.machine_command(plan)
            machine=json.loads((recorded/'machine.json').read_text())
            machine['config']['env']={'P2_MACHINE_CREATED_UNIX':'1'}
            machine['config']['init']={'cmd':['/bin/bash','-c',command]}
            for name,value in [('machines-ready.json',[machine]),('verified-plan.json',plan),('ips.json',[]),('volumes.json',[]),('secrets.json',[])]:gate.session.save(root/name,value)
            (root/'app-guard-start-unix.txt').write_text('1')
            (root/'image.txt').write_text(bundle.IMAGE)
            call=subprocess.run([sys.executable,str(HERE/'gate_fly_guard.py'),'machine',str(root),machine['id'],command],capture_output=True,text=True)
            self.assertEqual(call.returncode,0,call.stderr)
            self.assertNotEqual(bundle.IMAGE, bundle.IMAGE_INDEX)

    def test_mr4d_cleanup_requires_valid_exact_names(self):
        target = 'chrysalis-gate-20990405t000100-abcdef12'
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); before = root / 'before.json'; after = root / 'after.json'
            gate.session.save(before, [dict(Name=target)])
            for payload, absent in [([], True), ([dict(Name=target+'-other')], True), ([dict(Name=target)], False),
                                    ([{}], False), ([dict(Name='')], False), ([dict(Name=target.upper())], False),
                                    ([dict(name='unrelated', Name=target)], False)]:
                with self.subTest(payload=payload):
                    gate.session.save(after, payload)
                    result = subprocess.run([sys.executable, str(HERE/'gate_fly_guard.py'), 'cleanup', str(before), str(after), target],
                                            capture_output=True, text=True)
                    self.assertEqual(result.returncode == 0, absent, result.stdout + result.stderr)
                    if absent:
                        self.assertTrue(json.loads(result.stdout)['ourAppAbsent'])
                    else:
                        self.assertFalse(result.stdout)

    def test_go_refuses_insufficient_sealed_or_current_budget_without_a_call(self):
        with tempfile.TemporaryDirectory() as td:
            repo=Path(td);root=repo/'benchmarks/out/gate-budget';_,seal,env=fixture(root)
            for where in ('seal','local'):
                local=json.loads((root/'local.json').read_text())
                seal['flyBudgetLeftUsd']=.001 if where=='seal' else 40
                local['flyBudgetLeftUsd']=.001 if where=='local' else 40
                gate.session.save(root/'local.json',local);go=gate.write_seal(root,seal)
                with patch.object(gate,'REPO',repo),patch.dict(os.environ,env),patch.object(bundle,'owned_call') as launch:
                    with self.assertRaisesRegex(ValueError,'budget'):bundle.fly_command(go,directory=root)
                    launch.assert_not_called()

    def test_leftover_refusal_exit_four_and_dispatcher_propagation(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            _, seal, env = fixture(root)
            listing = root / 'existing-apps.json'
            gate.session.save(listing, [dict(name='fixture-neighbour'), dict(name='chrysalis-gate-leftover')])
            result = subprocess.run([sys.executable, str(HERE / 'gate_fly_guard.py'), 'apps', str(listing), APP], capture_output=True, text=True)
            with self.subTest(stage='guard'):
                self.assertEqual(result.returncode, 4, result.stderr)
            go = (root/'seal.id').read_text().strip()
            with self.subTest(stage='dispatcher'), patch.dict(os.environ, env), \
                    patch.object(bundle, 'held_fly_go', return_value=(root, seal, go)), \
                    patch.object(bundle, 'owned_call', return_value=4):
                self.assertEqual(bundle.fly_held(root), 4)

    def test_verified_archive_and_identity_refuse_changed_local_sources(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            host, seal, env = fixture(root)
            with patch.dict(os.environ, env):
                before = guard.verified_launch_inputs(host, root / 'seal.json')
                self.assertIn('P2_MACHINE_CREATED_UNIX', before['machineCommand'])
                (root / 'bundle/plan.json').write_text('{"destroyGuardSeconds":86400}')
                self.assertEqual(guard.verified_launch_inputs(host, root / 'seal.json'), before)  # ignored unsealed local plan
                for change in ('archive', 'helper', 'seal'):
                    path = root / 'input/session-0.tar.gz' if change == 'archive' else host / 'gate_fly_common.sh' if change == 'helper' else root / 'seal.json'
                    old = path.read_bytes(); path.write_bytes(old + b' changed')
                    with self.assertRaises((ValueError, RuntimeError)):
                        guard.verified_launch_inputs(host, root / 'seal.json')
                    path.write_bytes(old)

    def test_python_optimization_cannot_disable_a_runtime_refusal(self):
        for name in sorted({name for files in bundle.HOST_FILES.values() for name in files}):
            if name.endswith('.py'):
                tree = ast.parse((HERE / name).read_text())
                self.assertFalse(any(isinstance(node, ast.Assert) for node in ast.walk(tree)), name)
        env = gate.session.environment({})
        result = subprocess.run([sys.executable, '-O', '-c', 'import gate_time; gate_time.validate_limits(dict(destroyGuardSeconds=86400,measurementCutoffSeconds=1))'], cwd=HERE, env=env, capture_output=True)
        self.assertNotEqual(result.returncode, 0)

    def test_credential_shaped_files_and_private_markers_are_refused(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            for name in ('config.yml', 'id_rsa', 'id_ed25519', 'secret.pem', 'secret.key', 'secret.p12'):
                path = root / name; path.write_text('OBVIOUS TEST PLACEHOLDER')
                with self.assertRaisesRegex(RuntimeError, 'contents not read'):
                    guard.clean_run_folder(root)
                path.unlink()
            path = root / 'output.txt'
            for marker in ('-----BEGIN PRIVATE KEY-----', 'LocalPrivate: PLACEHOLDER', 'PrivateKey = PLACEHOLDER'):
                path.write_text(marker)
                with self.assertRaisesRegex(RuntimeError, 'Private-key marker'):
                    guard.clean_run_folder(root)

    @unittest.skipUnless(LINUX, '/proc and flock are Linux deployment checks')
    def test_lock_waiter_not_holder_inherited_holder_is_detected_no_cmdlines(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); lock = root / 'fixture.lock'
            with lock.open('a') as owner:
                fcntl.flock(owner, fcntl.LOCK_EX)
                waiter = subprocess.Popen(['flock', str(lock), 'true'])
                inherited = subprocess.Popen([sys.executable, '-c', 'import time;print("READY",flush=True);time.sleep(20)'],
                                             pass_fds=(owner.fileno(),), stdout=subprocess.PIPE, text=True)
                try:
                    self.assertEqual(inherited.stdout.readline().strip(), 'READY')
                    time.sleep(.05)
                    holders = guard.lock_holders(lock, [])
                    self.assertTrue(any(value['pid'] == inherited.pid for value in holders))
                    self.assertFalse(any(value['pid'] == waiter.pid for value in holders))
                    self.assertTrue(all(set(value) == {'pid', 'fd'} for value in holders))
                finally:
                    inherited.terminate(); inherited.wait(); inherited.stdout.close()
                    fcntl.flock(owner, fcntl.LOCK_UN); waiter.wait(timeout=3)
            with patch.object(sys, 'argv', ['guard', 'watchdog-pid', str(os.getpid()), 'other']):
                with self.assertRaisesRegex(RuntimeError, 'exact detached watchdog'):
                    guard.main()

    def test_repeat_bundle_only_the_failed_rows_no_new_bytes(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); _, seal, _ = fixture(root)
            raw = root / 'time/first.json'
            gate.session.save(raw, dict(pairs=pairs(seal['rows'][0], 1.03)))
            gate.session.save(root / 'time/index.json', [dict(session=0, phase='first', ids=[seal['rows'][0]['id']], path='time/first.json', sha256=gate.digest(raw))])
            bundle.make_repeat(root, seal, 0, seal['timePlans'][0]['rows'], 'input/repeat-0.tar.gz')
            self.assertTrue(bundle.verified_bundle(root, seal, 'input/repeat-0.tar.gz'))
            bundle.make_repeat(root, seal, 0, [], 'input/repeat-0.tar.gz')
            with self.assertRaisesRegex(ValueError, 'subset'):
                bundle.verified_bundle(root, seal, 'input/repeat-0.tar.gz')


@unittest.skipUnless(LINUX, 'Launcher deploys on Linux; final PC gate exercises all cases')
class LauncherTests(unittest.TestCase):
    def run_case(self, *, fail=None, extra=None, delay=None, send=None, held=False, tamper=False):
        directory = tempfile.TemporaryDirectory(); self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        _, _, env = fixture(root, watchdog_delay=delay)
        env.update(extra or {})
        if fail:
            env['GATE_STUB_FAIL'] = str(fail)
        if tamper:
            (root / 'input/fly/gate_fly.sh').write_text('CHANGED')
        owner = (root / 'test.lock').open('a') if held else contextlib.nullcontext()
        with owner as file:
            if held:
                fcntl.flock(file, fcntl.LOCK_EX)
            status = launch(root, env, send=send)
        if send == 'group-kill':
            deadline = time.monotonic() + 12
            proof = root / 'time/runs' / APP / 'watchdog-proof.json'
            while not proof.exists():
                self.assertLess(time.monotonic(), deadline); time.sleep(.05)
            time.sleep(.1)
        log = (root / 'console.log').read_text()
        self.assertNotIn('preflight-not-a-token', log)
        self.assertNotIn('CANARY_ambient', log)
        return root, status

    def assert_absent_owned(self, root):
        state = json.loads((root / 'state.json').read_text())
        self.assertFalse(state['app']); self.assertFalse(state['machine'])
        for call in calls(root):
            args = call['argv']
            if args[:2] == ['apps', 'destroy']:
                self.assertEqual(args[2], APP)
            if args[:2] == ['machine', 'run'] or args[:2] == ['machine', 'exec'] or args[:3] in (['ssh', 'sftp', 'put'], ['ssh', 'sftp', 'get']):
                self.assertEqual(args[args.index('--app') + 1], APP)

    def test_mr4d_local_failures_do_not_consume_go(self):
        for fail in ('version', 'org', 'lock', 'budget', 'sha', 'location', 'local-binding', 'token-file'):
            with self.subTest(fail=fail), tempfile.TemporaryDirectory() as td:
                repo = Path(td)
                root = repo / ('elsewhere' if fail == 'location' else 'benchmarks/out/gate-local-failure')
                _, seal, env = fixture(root)
                local = json.loads((root/'local.json').read_text())
                if fail == 'version': seal['flyctlVersion'] = 'fly other-fixture'
                if fail == 'org': local['flyOrg'] = 'malformed org'
                if fail == 'lock': local['flyLock'] = os.path.relpath(root/'test.lock')
                if fail == 'budget': local.pop('flyBudgetLeftUsd')
                if fail == 'local-binding':
                    shutil.copy2(root/'local.json', root/'input/fly-local.json')
                    seal['files']['input/fly-local.json'] = gate.digest(root/'input/fly-local.json')
                    local['flyOrg'] = 'other-valid-fixture-org'
                if fail == 'token-file': local['flyTokenFile'] = str(root/'nonexistent-fixture-token')
                gate.session.save(root/'local.json', local)
                go = gate.write_seal(root, seal) if fail != 'sha' else '0' * 64
                with patch.object(gate, 'REPO', repo), patch.dict(os.environ, env):
                    try:
                        result = bundle.fly_command(go, directory=root)
                    except (ValueError, OSError, RuntimeError):
                        result = 2
                self.assertNotEqual(result, 0)
                self.assertFalse((root/'time/launch-started.json').exists(), fail)
                self.assertEqual(calls(root), [])

    def test_retry_launcher_preflight_claim_and_sibling_refusal(self):
        with tempfile.TemporaryDirectory() as name:
            repo = Path(name)
            parent, _, env, _, original_go = retry_parent(repo, 'first')
            child, go = retry_child(repo, parent, 'first')
            sibling, sibling_go = retry_child(repo, parent, 'first')
            marker = child / 'time/launch-started.json'
            claim = child.parent / 'retry-claims' / original_go
            token = parent / 'token.fixture'
            token.unlink()
            marker.parent.mkdir()
            with bundle.retry_dispatch(child, go) as dispatch:
                self.assertNotEqual(launch(child, dict(env, GATE_FIRST_GO=go), stdin=dispatch), 0)
                self.assertIn('Fly token file missing or empty', (child / 'console.log').read_text())
                self.assertFalse(marker.exists())
                self.assertFalse(claim.exists())
                self.assertFalse((child / 'time/runs' / APP).exists())
                self.assertEqual(calls(parent), [])
                token.write_text('preflight-not-a-token')
                marker.mkdir()
                self.assertNotEqual(launch(child, dict(env, GATE_FIRST_GO=go), stdin=dispatch), 0)
            self.assertTrue(claim.is_file())
            self.assertNotEqual(launch(sibling, env), 0)
            self.assertIn('active retry dispatch', (sibling / 'console.log').read_text())
            (sibling / 'time').mkdir()
            with bundle.retry_dispatch(sibling, sibling_go) as dispatch:
                self.assertNotEqual(launch(sibling, dict(env, GATE_FIRST_GO=sibling_go), stdin=dispatch), 0)
            self.assertIn('FileExistsError', (sibling / 'console.log').read_text())
            self.assertFalse((sibling / 'time/launch-started.json').exists())
            self.assertEqual(calls(parent), [])

    def test_mr4d_stale_caller_cannot_consume_go_after_acquiring_lock(self):
        with tempfile.TemporaryDirectory() as td:
            repo = Path(td); root = repo/'benchmarks/out/gate-stale'
            _, _, env = fixture(root)
            go = (root/'seal.id').read_text().strip()
            marker = root/'time/launch-started.json'
            dispatch = bundle.owned_call
            previous_marker = gate.encoded(dict(sealId=go))
            def after_precheck(*args, **kwargs):
                marker.parent.mkdir()
                marker.write_bytes(previous_marker)
                return dispatch(*args, **kwargs)
            with patch.object(gate, 'REPO', repo), patch.dict(os.environ, env), patch.object(bundle, 'owned_call', after_precheck):
                self.assertNotEqual(bundle.fly_command(go, directory=root), 0)
            self.assertEqual(calls(root), [])
            self.assertEqual(marker.read_bytes(), previous_marker)

    def test_mr4d_launcher_rejects_uppercase_app_name(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); host, _, env = fixture(root)
            result = subprocess.run(['bash', str(host/'gate_fly.sh'), '--approved',
                                     'chrysalis-gate-20990405T000100-abcdef12', str(root/'seal.json')],
                                    env=env, capture_output=True, text=True)
            self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
            self.assertIn('Refusing non-run app', result.stdout)
            self.assertEqual(calls(root), [])

    def test_mr4d_whole_sealed_change_go_and_release_repeat_are_one_command_each(self):
        for mode, ratio, expected, machines, blocked in [('change', 1, 0, 1, None), ('release', 1.03, 2, 2, None),
                                                         ('release', 1.03, 2, 1, 'A band'), ('release', 1.03, 2, 1, 'noise')]:
            with self.subTest(mode=mode, blocked=blocked), tempfile.TemporaryDirectory() as td:
                repo = Path(td); root = repo/'benchmarks/out/gate-whole'
                _, seal, env = fixture(root)
                plan = seal['timePlans'][0]
                seal['mode'] = mode
                plan['rows'][0]['noise'] = None if mode == 'release' else plan['rows'][0]['noise']
                if blocked == 'noise':
                    plan['rows'][0]['expected'] = {'release': 'FAIL'}
                if blocked == 'A band':
                    plan['rows'].append(dict(plan['rows'][0], id='time/control-miss'))
                    seal['releaseSet'].append('time/control-miss')
                if mode == 'change':
                    plan['repeatAllowed'] = False
                    for r in plan['rows']:
                        r['aPairs'] = 0
                    gate.session.save(root / 'fixture-host.json', dict(model='33'))
                bundle_input = root / 'bundle'
                timing.finalize_plan(plan, plan['rawSeconds'] - timing.probe_seconds(plan['probe']))
                schedules.prepare(plan)
                gate.session.save(bundle_input / 'plan.json', schedules.worker_plan(plan))
                bundle.pack(bundle_input, root / 'input/session-0.tar.gz')
                seal['files']['input/session-0.tar.gz'] = gate.digest(root / 'input/session-0.tar.gz')
                observed = pairs(plan['rows'][0], ratio)
                if blocked == 'A band':
                    observed += pairs(plan['rows'][1], a=[1.06] * plan['rows'][1]['aPairs'])
                gate.session.save(root / 'fixture-pairs.json', observed)
                if mode == 'release':
                    shutil.copy2(root/'local.json', root/'input/fly-local.json')
                    seal['files']['input/fly-local.json'] = gate.digest(root/'input/fly-local.json')
                    env['CHRYSALIS_GATE_LOCAL'] = str(root/'input/fly-local.json')
                else:
                    self.assertNotIn('input/fly-local.json', seal['files'])
                go = gate.write_seal(root, seal)
                with patch.object(gate, 'REPO', repo), patch.dict(os.environ, env):
                    result = bundle.fly_command(go)
                    self.assertEqual(result, expected, (root / 'report.txt').read_text() if (root / 'report.txt').exists() else (root / 'time/0-first-draw-1-launcher.log').read_text())
                    before = calls(root)
                    with self.assertRaisesRegex(ValueError, 'consumed'):
                        bundle.fly_command(go)
                    self.assertEqual(calls(root), before)
                recorded = calls(root)
                self.assertEqual(sum(c['argv'][:2] == ['machine', 'run'] for c in recorded), machines)
                self.assertFalse(json.loads((root / 'state.json').read_text())['app'])
                report = gate.report(root)
                item = json.loads((root / 'time/index.json').read_text())[0]
                payload_path = root / item['path']
                self.assertEqual(gate.digest(payload_path), item['sha256'])
                payload = json.loads(payload_path.read_text())
                if mode == 'change':
                    self.assertEqual(payload['host']['model'], '33')
                    self.assertEqual(report['text'].count('host fingerprint (information only):'), 1)
                else:
                    self.assertEqual(payload['host']['processorCount'], 8)
                self.assertEqual(report['outcome'], 'SUBSET' if mode == 'change' else 'FAIL')
                self.assertEqual('repeat' in report['rows'][plan['rows'][0]['id']], mode == 'release' and not blocked)
                if blocked:
                    relative = 'input/repeat-0.tar.gz'
                    bundle.make_repeat(root, seal, 0, plan['rows'][:1], relative)
                    with self.assertRaisesRegex(ValueError, 'No failed rows to repeat'):
                        bundle.verified_bundle(root, seal, relative)
                elif mode == 'release':
                    self.assertTrue(bundle.verified_bundle(root, seal, 'input/repeat-0.tar.gz'))
                names = [call['argv'][2] for call in calls(root) if call['argv'][:2] == ['apps', 'create']]
                self.assertEqual(len(names), machines)
                for name in names:
                    self.assertRegex(name, r'^[a-z0-9-]+$')

    def test_scout_stops_after_invalid_second_machine(self):
        with tempfile.TemporaryDirectory() as td:
            repo = Path(td); root = repo / 'benchmarks/out/gate-scout'
            _, seal, env = fixture(root)
            rows = [dict(row(pairs=8), id=f'time/preprod-scout{i}-offline', aPairs=0,
                         region='ewr' if i == 1 else 'ams') for i in range(1, 7)]
            plans = [probe_plan(r, 'change') for r in rows]
            seal.update(mode='change', rows=rows, timePlans=plans)
            for i, plan in enumerate(plans):
                gate.session.save(root / 'bundle/plan.json', schedules.worker_plan(plan))
                name = f'input/session-{i}.tar.gz'
                seal['files'][name] = bundle.pack(root / 'bundle', root / name)
            gate.session.save(root / 'fixture-pairs.json', pairs(rows[0]) + pairs(rows[1]))
            (root / 'invalid-session-1.tar.gz').touch()
            go = gate.write_seal(root, seal)
            with patch.object(gate, 'REPO', repo), patch.dict(os.environ, env):
                self.assertEqual(bundle.fly_command(go), 2)
            self.assertEqual(sum(c['argv'][:2] == ['machine', 'run'] for c in calls(root)), 2)
            index = json.loads((root / 'time/index.json').read_text())
            self.assertEqual([i['session'] for i in index], [0, 1])
            first, second = [json.loads((root / i['path']).read_text()) for i in index]
            self.assertIsNone(first['invalid'])
            self.assertIn('failed worker', second['invalid'])
            text = (root / 'report.txt').read_text()
            self.assertIn('time · Fly performance-8x · arithmetic mean paired ratios', text)
            self.assertIn('session 1 · ewr', text)
            self.assertIn('session 2 · ams', text)
            timing_paths = [root / item['path'] for item in index]
            with patch.object(Path, 'read_bytes', autospec=True, side_effect=Path.read_bytes) as read:
                gate.report(root)
            self.assertEqual([call.args[0] for call in read.call_args_list if call.args[0] in timing_paths], timing_paths)

    def test_sealed_region(self):
        for wrong in (False, True):
            with self.subTest(wrong=wrong), tempfile.TemporaryDirectory() as td:
                root = Path(td)
                _, _, env = fixture(root, region='ams')
                if wrong:
                    env['GATE_STUB_BAD'] = 'region'
                status = launch(root, env)
                argv = next(c['argv'] for c in calls(root) if c['argv'][:2] == ['machine', 'run'])
                self.assertEqual(argv[argv.index('--region') + 1], 'ams')
                self.assertEqual(status, 1 if wrong else 0, (root / 'console.log').read_text())
                self.assertEqual(any(c['argv'][:3] == ['ssh', 'sftp', 'put'] for c in calls(root)), not wrong)
                if wrong:
                    self.assertIn('Wrong machine identity/state/region', (root / 'console.log').read_text())
                note = root / 'time/runs' / APP
                plan = json.loads((note / 'verified-plan.json').read_text())
                self.assertEqual(plan['region'], 'ams')
                cost = json.loads((note / 'duration-cost.json').read_text())
                compute = cost['conservativeSeconds'] / 3600 * .3667 * 1.038461538
                self.assertEqual(cost['computeEstimateUsd'], compute)
                self.assertEqual(cost['totalEstimateWithReserveUsd'], compute + .03)
                self.assertEqual(cost['sealedLimitUsd'], timing.price(plan['destroyGuardSeconds'], plan['region']))
                self.assert_absent_owned(root)

    def test_probe_rejection_shell_exit_after_proven_cleanup(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            _, seal, env = fixture(root)
            plan = seal['timePlans'][0]
            plan.update(probe=probe_rule(), probeJob=dict(kind='time', replay='offline'))
            schedules.prepare(plan)
            gate.session.save(root / 'bundle/plan.json', schedules.worker_plan(plan))
            seal['files']['input/session-0.tar.gz'] = bundle.pack(root / 'bundle', root / 'input/session-0.tar.gz')
            gate.write_seal(root, seal)
            self.assertEqual(probe_archive(root / 'stub-probe', plan, 'gen0-budget-mismatch'), 5)
            stub = root / 'stubbin/fly'
            source = stub.read_text()
            marker = "        s['downloaded'] = True; out = 'Downloaded STUB'"
            self.assertEqual(source.count(marker), 1)
            stub.write_text(source.replace(marker, "        Path(a[-1]).write_bytes((root / 'stub-probe/results.tar.gz').read_bytes())\n" + marker))
            self.assertEqual(launch(root, env), 5)
            self.assert_absent_owned(root)
            note = root / 'time/runs' / APP
            self.assertTrue((note / 'probe-rejected.json').exists())
            self.assertTrue(json.loads((note / 'cleanup-proof.json').read_text())['ourAppAbsent'])
            self.assertFalse((note / 'diagnostic-only.json').exists())

    def test_success_neighbours_context_only_token_fd_and_no_private_fly_config(self):
        root, status = self.run_case(extra=dict(GATE_STUB_NEIGHBOUR_CHANGE='1'))
        self.assertEqual(status, 0, (root / 'console.log').read_text() + '\n' + '\n'.join(str(path.relative_to(root)) + ': ' + path.read_text() for path in (root / 'time').rglob('*') if path.is_file() and path.suffix in ('.log', '.stderr')))
        self.assert_absent_owned(root)
        note = root / 'time/runs' / APP
        plan = gate.read_seal(root)['timePlans'][0]
        self.assertNotIn('region', json.loads((note / 'verified-plan.json').read_text()))
        self.assertEqual(json.loads((note / 'machine.json').read_text())['region'], 'iad')
        payload = bundle.archive_payload(note, plan)
        self.assertIsNone(payload['invalid'])
        startup = payload['startup']
        self.assertEqual(startup['firstLong']['n'], 20)
        self.assertEqual(startup['firstLong']['median'], .002)
        self.assertEqual(startup['firstLong']['observationSpanSeconds'], 300)
        self.assertTrue(startup['informationOnly'])
        self.assertEqual({k:json.loads((note / 'neighbours-start.json').read_text())[k] for k in ('performance','shared')}, dict(performance=1, shared=1))
        self.assertFalse(any(path.name == 'config.yml' for path in root.rglob('*')))

    def test_lock_contention_no_changing_call_and_prefix_leftover_read_only(self):
        root, status = self.run_case(held=True)
        self.assertEqual(status, 75); self.assertEqual(calls(root), [])
        root, status = self.run_case(extra=dict(GATE_STUB_EXISTING='chrysalis-gate-leftover'))
        self.assertEqual(status, 4)
        self.assertFalse(json.loads((root / 'state.json').read_text())['app'])
        self.assertFalse(any(call['argv'][:2] == ['apps', 'create'] for call in calls(root)))

    def test_second_restart_replacement_inode_or_result_hash_invalid_no_repeat(self):
        for bad in ('second', 'restart', 'replacement', 'inode', 'hash'):
            root, status = self.run_case(extra=dict(GATE_STUB_BAD=bad))
            self.assertNotEqual(status, 0, bad)
            self.assert_absent_owned(root)
            plan = gate.read_seal(root)['timePlans'][0]
            self.assertTrue(bundle.archive_payload(root / 'time/runs' / APP, plan)['invalid'])

    def test_each_operational_failure_cleans_only_own_app(self):
        root, status = self.run_case()
        count = next(item['call'] for item in calls(root) if item['argv'][:3] == ['ssh', 'sftp', 'get'])
        for fail in range(1, count + 1):
            with self.subTest(fail=fail):
                root, status = self.run_case(fail=fail)
                state = json.loads((root / 'state.json').read_text())
                self.assertFalse(state['app'])
                self.assertFalse(state['machine'])
                self.assertTrue(any(call['injectedFailure'] for call in calls(root)))

    def test_public_dispatcher_forwards_interrupt_and_waits_for_cleanup(self):
        for sig in (signal.SIGINT,signal.SIGTERM):
            with self.subTest(signal=sig), tempfile.TemporaryDirectory() as td:
                root=Path(td)/'benchmarks/out/gate-interrupt';_,_,env=fixture(root,watchdog_delay=20)
                stub=root/'stubbin/fly';stub.write_text(stub.read_text().replace("get('GATE_STUB_SESSION_DELAY', '0')","get('GATE_STUB_SESSION_DELAY', '10')"))
                command=[sys.executable,'-c',"import sys;from pathlib import Path;sys.path.insert(0,sys.argv[1]);import gate,gate_bundle;gate.REPO=Path(sys.argv[2]).parents[2];raise SystemExit(gate_bundle.fly_command(Path(sys.argv[2],'seal.id').read_text().strip()))",str(HERE),str(root)]
                with (root/'public.log').open('w') as stream:
                    process=subprocess.Popen(command,env=env,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
                    try:
                        deadline=time.monotonic()+8
                        while not (root/'state.json').exists() or not json.loads((root/'state.json').read_text()).get('sessionStarted'):
                            self.assertIsNone(process.poll());self.assertLess(time.monotonic(),deadline);time.sleep(.02)
                        os.killpg(process.pid,sig)
                        self.assertNotEqual(process.wait(timeout=12),0)
                        note=next((root/'time/runs').iterdir())
                        self.assertTrue((note/'cleanup-proof.json').exists(),(root/'public.log').read_text())
                        self.assertFalse((note/'watchdog-proof.json').exists())
                        output=(root/'public.log').read_text()
                        self.assertEqual(output.count('cleanup is running'),1,output)
                        start=int((note/'app-guard-start-unix.txt').read_text())
                        guard=gate.read_seal(root)['timePlans'][0]['destroyGuardSeconds']
                        self.assertIn(dt.datetime.fromtimestamp(start+guard,dt.timezone.utc).isoformat(),output)
                        self.assertFalse(json.loads((root/'state.json').read_text())['app'])
                    finally:
                        if process.poll() is None:process.kill();process.wait()

    def test_scratch_watchdog_stops_even_when_fixture_cleanup_happens_first(self):
        directory=tempfile.TemporaryDirectory();root=Path(directory.name)
        host,seal,env=fixture(root)
        watchdog=host/'gate_fly_watchdog.sh'
        watchdog.write_text(watchdog.read_text().replace(f'remaining={TEST_WATCHDOG_SECONDS}','remaining=1'))
        common=host/'gate_fly_common.sh'
        common.write_text(common.read_text().replace(f'SECONDS >= {TEST_WATCHDOG_SECONDS}','SECONDS >= 1'))
        note=root/'time/runs'/APP;note.mkdir(parents=True)
        lock=(root/'test.lock').open('a')
        command=['bash','-c','exec 9>"$1"; flock -n 9 || exit 75; shift; exec bash "$@"','scratch-watchdog',str(root/'test.lock'),str(host/'gate_fly_watchdog.sh'),APP,str(note),str(int(time.time())),str(host),str(seal['timePlans'][0]['destroyGuardSeconds'])]
        process=subprocess.Popen(command,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True)
        try:
            deadline=time.monotonic()+4
            while not (note/'watchdog.pid').exists():
                self.assertIsNone(process.poll());self.assertLess(time.monotonic(),deadline);time.sleep(.02)
            directory.cleanup()
            try:result=process.wait(timeout=4)
            except subprocess.TimeoutExpired:self.fail('Scratch watchdog kept retrying after its fixture was removed')
            self.assertNotEqual(result,0)
            fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
        finally:
            if process.poll() is None:os.killpg(process.pid,signal.SIGKILL);process.wait()
            lock.close();directory.cleanup()

    def test_failed_cleanup_hands_lock_to_live_watchdog_until_proof(self):
        with tempfile.TemporaryDirectory() as td:
            root=Path(td);_,_,env=fixture(root,watchdog_delay=1)
            env.update(GATE_STUB_DESTROY_FAIL_COUNT='999',GATE_STUB_FAST_CLEANUP='1')
            clock=root/'stubbin/date'
            clock.write_text("#!/usr/bin/env python3\nimport json,os,subprocess,sys\nfrom pathlib import Path\nr=Path(__file__).resolve().parents[1];s=json.loads((r/'state.json').read_text()) if (r/'state.json').exists() else {}\nif sys.argv[1:]==['+%s'] and os.environ.get('GATE_STUB_FAST_CLEANUP') and s.get('destroyAttempts',0):\n print(int(s['created'])+json.loads((r/'seal.json').read_text())['timePlans'][0]['destroyGuardSeconds']+1)\nelse:raise SystemExit(subprocess.call(['/bin/date',*sys.argv[1:]]))\n")
            clock.chmod(0o755)
            status=launch(root,env)
            state=json.loads((root/'state.json').read_text())
            try:
                self.assertNotEqual(status,0)
                self.assertTrue(state['app'])
                with (root/'test.lock').open('a') as lock,self.assertRaises(BlockingIOError):fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
                self.assertTrue((root/'time/runs'/APP/'cleanup-failed.json').exists())
                cost=json.loads((root/'time/runs'/APP/'duration-cost.json').read_text())
                self.assertTrue(cost['cleanupOutstanding']);self.assertIsNone(cost['endedUnix'])
            finally:
                with (root/'state.lock').open('a') as lock:
                    fcntl.flock(lock,fcntl.LOCK_EX);state=json.loads((root/'state.json').read_text());state['destroyAttempts']=1000;gate.session.save(root/'state.json',state)
            deadline=time.monotonic()+15
            proof=root/'time/runs'/APP/'watchdog-proof.json'
            while True:
                self.assertLess(time.monotonic(),deadline)
                if proof.exists() and proof.read_text().strip():
                    with (root/'test.lock').open('a') as lock:
                        try:fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
                        except BlockingIOError:pass
                        else:break
                time.sleep(.05)
            self.assertTrue(json.loads(proof.read_text())['ourAppAbsent'])
            self.assertFalse(json.loads((root/'state.json').read_text())['app'])


    def test_transient_cleanup_recovers_after_three_failures(self):
        root,status=self.run_case(extra=dict(GATE_STUB_DESTROY_FAIL_COUNT='3'))
        self.assertEqual(status,0,(root/'console.log').read_text())
        self.assert_absent_owned(root)
        self.assertGreaterEqual(json.loads((root/'state.json').read_text())['destroyAttempts'],4)

    def test_neighbour_failure_never_invalidates_paid_measurement(self):
        root,status=self.run_case()
        failures=[c['call'] for c in calls(root) if c['argv'][:2]==['machine','list'] and '--app' in c['argv'] and c['argv'][c['argv'].index('--app')+1]!=APP]
        for fail in failures:
            root,status=self.run_case(fail=fail)
            self.assertEqual(status,0,(root/'console.log').read_text())
            plan=gate.read_seal(root)['timePlans'][0]
            self.assertIsNone(bundle.archive_payload(root/'time/runs'/APP,plan)['invalid'])

    def test_abort_recovery(self):
        for name in ('term', 'int'):
            started = time.monotonic()
            root, status = self.run_case(extra=dict(GATE_STUB_SESSION_DELAY='30'), send=name)
            self.assertLess(time.monotonic() - started, 20)
            self.assertNotEqual(status, 0); self.assert_absent_owned(root)
            self.assertTrue(json.loads((root / 'state.json').read_text()).get('partialArchiveAttempted'))

    def test_detached_watchdog_survives_entire_launcher_process_group(self):
        root, status = self.run_case(extra=dict(GATE_STUB_SESSION_DELAY='4'), delay=3, send='group-kill')
        self.assertNotEqual(status, 0); self.assert_absent_owned(root)
        self.assertTrue((root / 'time/runs' / APP / 'watchdog-proof.json').exists())


if __name__ == '__main__':
    unittest.main()


def test_legacy_fly_go_dispatch_and_local_keys(tmp_path):
    root = tmp_path / 'benchmarks/out/gate-fly'
    _, _, env = fixture(root)
    go = (root / 'seal.id').read_text().strip()
    with (patch.object(gate, 'REPO', tmp_path), patch.dict(os.environ, env),
          patch.object(bundle, 'fly_command', return_value=0) as launch,
          patch.object(sys, 'argv', ['gate.py', 'fly', '--go', go])):
        with unittest.TestCase().assertRaises(SystemExit) as exit_:
            gate.main()
        assert exit_.exception.code == 0
        launch.assert_called_once_with(go, root)
        assert gate.LOCAL_KEYS == ('corporaRoot', 'pinnedBaseline', 'flyOrg', 'flyTokenFile', 'flyLock')
        assert all(key in gate.local_config() for key in gate.LOCAL_KEYS)
