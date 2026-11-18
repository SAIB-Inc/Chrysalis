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
from test_gate_time import pairs, row

HERE = Path(__file__).resolve().parent
APP = 'chrysalis-gate-20990405t000100-abcdef12'
LINUX = sys.platform.startswith('linux')
TEST_WATCHDOG_SECONDS = 30


def fixture(root, *, watchdog_delay=None):
    root = Path(root)
    host = root / 'input/fly'
    host.mkdir(parents=True)
    for name in bundle.HOST_FILES:
        shutil.copy2(HERE / name, host / name)
    if watchdog_delay is not None:
        path = host / 'gate_fly_watchdog.sh'
        path.write_text(path.read_text().replace('remaining=$((CREATED_UNIX + GUARD - 70 - $(date +%s)))', 'remaining=' + str(watchdog_delay)))
    # Bound scratch owners even if a failed test removes the proof directory.
    # The production watchdog/common scripts are not changed.
    common = host / 'gate_fly_common.sh'
    common.write_text(common.read_text().replace('deadline > 0 &&', f'SECONDS >= {TEST_WATCHDOG_SECONDS} || deadline > 0 &&'))
    watchdog = host / 'gate_fly_watchdog.sh'
    watchdog.write_text(watchdog.read_text().replace('if (( remaining > 0 ));', f'(( remaining <= {TEST_WATCHDOG_SECONDS} )) || remaining={TEST_WATCHDOG_SECONDS}\nif (( remaining > 0 ));'))
    stub = root / 'stubbin'; stub.mkdir()
    shutil.copy2(HERE / 'fixtures/fly/fly', stub / 'fly')
    for path in (HERE/'fixtures/fly').glob('*.json'):shutil.copy2(path,stub/path.name)
    (stub / 'fly').chmod(0o755)
    (root / 'token.fixture').write_text('preflight-not-a-token')
    gate.session.save(root / 'local.json', dict(flyTokenFile=str(root / 'token.fixture'), flyLock=str(root / 'test.lock'), flyOrg='fixture-org', corporaRoot='fixture', pinnedBaseline='fixture', flyBudgetLeftUsd=40))
    plan = schedules.prepare(timing.size_sessions([row()], 'release', budget=40)[0])
    gate.session.save(root / 'fixture-pairs.json', pairs(plan['rows'][0]))
    source = root / 'bundle'; source.mkdir()
    gate.session.save(source / 'plan.json', schedules.worker_plan(plan))
    (source / 'placeholder.txt').write_text('OBVIOUS TEST PLACEHOLDER; never a product binary')
    bundle.pack(source, root / 'input/session-0.tar.gz')
    seal = dict(flyBudgetLeftUsd=40, mode='release', ruleVersion=gate.stats.RULE_VERSION, rows=plan['rows'], timePlans=[plan], bands={},
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


def launch(root, env, *, send=None):
    with (root / 'console.log').open('w') as stream:
        process = subprocess.Popen(['bash', '-x', str(root / 'input/fly/gate_fly.sh'), '--approved', APP, str(root / 'seal.json')],
                                   env=env, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
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


class GuardTests(unittest.TestCase):
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
        for name in bundle.HOST_FILES:
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
        for mode, ratio, expected, machines in [('change', 1, 0, 1), ('release', 1.03, 2, 2)]:
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as td:
                repo = Path(td); root = repo/'benchmarks/out/gate-whole'
                _, seal, env = fixture(root)
                plan = seal['timePlans'][0]
                seal['mode'] = mode
                if mode == 'change':
                    plan['repeatAllowed'] = False
                    for r in plan['rows']:
                        r['aPairs'] = 0
                bundle_input = root / 'bundle'
                schedules.prepare(plan)
                gate.session.save(bundle_input / 'plan.json', schedules.worker_plan(plan))
                bundle.pack(bundle_input, root / 'input/session-0.tar.gz')
                seal['files']['input/session-0.tar.gz'] = gate.digest(root / 'input/session-0.tar.gz')
                gate.session.save(root / 'fixture-pairs.json', pairs(plan['rows'][0], ratio))
                if mode == 'release':
                    shutil.copy2(root/'local.json', root/'input/fly-local.json')
                    seal['files']['input/fly-local.json'] = gate.digest(root/'input/fly-local.json')
                    env['CHRYSALIS_GATE_LOCAL'] = str(root/'input/fly-local.json')
                else:
                    self.assertNotIn('input/fly-local.json', seal['files'])
                go = gate.write_seal(root, seal)
                with patch.object(gate, 'REPO', repo), patch.dict(os.environ, env):
                    result = bundle.fly_command(go)
                    self.assertEqual(result, expected, (root / 'report.txt').read_text() if (root / 'report.txt').exists() else (root / 'time/0-first-launcher.log').read_text())
                    before = calls(root)
                    with self.assertRaisesRegex(ValueError, 'consumed'):
                        bundle.fly_command(go)
                    self.assertEqual(calls(root), before)
                recorded = calls(root)
                self.assertEqual(sum(c['argv'][:2] == ['machine', 'run'] for c in recorded), machines)
                self.assertFalse(json.loads((root / 'state.json').read_text())['app'])
                report = gate.report(root)
                self.assertEqual(report['outcome'], 'SUBSET' if mode == 'change' else 'FAIL')
                self.assertEqual('repeat' in report['rows'][plan['rows'][0]['id']], mode == 'release')
                names = [call['argv'][2] for call in calls(root) if call['argv'][:2] == ['apps', 'create']]
                self.assertEqual(len(names), 1 if mode == 'change' else 2)
                for name in names:
                    self.assertRegex(name, r'^[a-z0-9-]+$')

    def test_success_neighbours_context_only_token_fd_and_no_private_fly_config(self):
        root, status = self.run_case(extra=dict(GATE_STUB_NEIGHBOUR_CHANGE='1'))
        self.assertEqual(status, 0, (root / 'console.log').read_text() + '\n' + '\n'.join(str(path.relative_to(root)) + ': ' + path.read_text() for path in (root / 'time').rglob('*') if path.is_file() and path.suffix in ('.log', '.stderr')))
        self.assert_absent_owned(root)
        note = root / 'time/runs' / APP
        plan = gate.read_seal(root)['timePlans'][0]
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
            root, status = self.run_case(extra=dict(GATE_STUB_SESSION_DELAY='10'), send=name)
            self.assertLess(time.monotonic() - started, 8)
            self.assertNotEqual(status, 0); self.assert_absent_owned(root)
            self.assertTrue(json.loads((root / 'state.json').read_text()).get('partialArchiveAttempted'))

    def test_detached_watchdog_survives_entire_launcher_process_group(self):
        root, status = self.run_case(extra=dict(GATE_STUB_SESSION_DELAY='4'), delay=3, send='group-kill')
        self.assertNotEqual(status, 0); self.assert_absent_owned(root)
        self.assertTrue((root / 'time/runs' / APP / 'watchdog-proof.json').exists())


if __name__ == '__main__':
    unittest.main()
