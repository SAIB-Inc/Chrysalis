"""P1 runner primitives plus measured-window and environment regressions."""
import copy
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

import gate_session as session


def data(short=False):
    warmup, measured = (1, 25) if short else (180, 60)
    def rows(count):
        return [dict(blocks=1020, nsPerBlock=1000000, bytesPerBlock=0, **{'pass': i}) for i in range(1, count + 1)]
    def counters():
        return dict.fromkeys(session.COUNTERS, 0)
    return dict(runtime='10.0.9', warmupPasses=warmup, passes=measured,
                warmupRows=rows(warmup), rows=rows(measured),
                groupBoundaries=[dict(stage=stage, passIndex=i, delta=counters(), cumulative=counters())
                                 for stage, count in [('warmup', warmup), ('measured', measured)] for i in range(count + 1)])


class SessionTests(unittest.TestCase):
    def test_host_fingerprint_is_an_allowlist_of_independent_reads(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            empty = session.host_fingerprint(root)
            (root / 'proc').mkdir()
            (root / 'proc/cpuinfo').write_text('processor: 0\nvendor_id: FixtureCPU\nmodel: 33\nbogomips: 12.34\n'
                                              'flags: z  tsc\tz a\nhostname: CANARY-host\n\nprocessor: 1\nstepping: 91\n')
            (root / 'proc/meminfo').write_text('MemTotal: 100 kB\nMemFree: 99 kB\n')
            (root / 'proc/sys/kernel').mkdir(parents=True)
            (root / 'proc/sys/kernel/osrelease').write_text('fixture-kernel\n')
            clock = root / 'sys/devices/system/clocksource/clocksource0'; clock.mkdir(parents=True)
            (clock / 'current_clocksource').write_text('tsc\n')
            (clock / 'available_clocksource').write_text('tsc kvm-clock\n')
            expected = dict.fromkeys(('vendor_id', 'cpu family', 'model', 'model name', 'stepping', 'microcode',
                                      'cpu MHz', 'cache size', 'bogomips', 'address sizes', 'processorCount', 'flagsSha256',
                                      'flagsCount', 'MemTotal', 'clocksourceCurrent', 'clocksourceAvailable', 'kernelRelease'))
            self.assertEqual(empty, expected)
            expected.update(vendor_id='FixtureCPU', model='33', bogomips='12.34', processorCount=2, flagsCount=3,
                            flagsSha256=hashlib.sha256(b'a tsc z').hexdigest(), MemTotal='100 kB',
                            clocksourceCurrent='tsc', clocksourceAvailable='tsc kvm-clock', kernelRelease='fixture-kernel')
            with patch.object(session.os, 'environ', None):
                self.assertEqual(session.host_fingerprint(root), expected)
                (clock / 'available_clocksource').write_bytes(b'\xff')
                self.assertEqual(session.host_fingerprint(root), dict(expected, clocksourceAvailable=None))

    def test_fly_metadata_survives_allowlist_and_detects_machine_only(self):
        metadata = dict(FLY_APP_NAME='fixture-app', FLY_REGION='iad', FLY_MACHINE_ID='fixture-machine')
        self.assertEqual({key: session.environment({}, metadata).get(key) for key in metadata}, metadata)
        with patch.dict(os.environ, {'FLY_MACHINE_ID': 'fixture-machine'}, clear=True):
            with self.assertRaises(ValueError):
                session.detect_platform()

    def test_build_isolation_environment_survives_allowlist(self):
        isolated = dict(DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1', UseSharedCompilation='false')
        self.assertEqual(session.environment({}, isolated), dict(isolated, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1'))
        self.assertFalse(set(isolated) & session.environment({}, {}).keys())

    def test_admission_limits(self):
        self.assertEqual(session.admission(.002, [.003] * 8, 0, [], duration=10), [])
        for steal, cores, total, each, duration in [(None, [0] * 8, 0, [], 10), (.005, [.03] + [0] * 7, 0, [], 10),
                                                   (0, [0] * 8, .6, [], 10), (0, [0] * 8, .1, [.21], 10),
                                                   (0, [0] * 7, 0, [], 10), (0, [0] * 8, None, [], 10),
                                                   (0, [0] * 8, 0, [], float('nan'))]:
            with self.subTest(values=(steal, cores, total, each, duration)):
                self.assertTrue(session.admission(steal, cores, total, each, duration=duration))

    def test_fixed_work_and_negative_counters(self):
        session.validate_observations(data(), 'offline', '10.0.9', (180, 60))
        session.validate_observations(data(True), 'offline-n2c', '10.0.9', (1, 25))
        for key in ('rows', 'warmupRows', 'groupBoundaries'):
            altered = data()
            altered[key].pop()
            with self.assertRaises(ValueError):
                session.validate_observations(altered, 'offline', '10.0.9', (180, 60))
        altered = data()
        altered['groupBoundaries'][-1]['delta']['compiledMethods'] = -1
        with self.assertRaises(ValueError):
            session.validate_observations(altered, 'offline', '10.0.9', (180, 60))

    def test_aggregate_quiescence_d49_methods_are_descriptive(self):
        first, second = session.jit_quiet(data()), session.jit_quiet(data())
        first.update(bodyMs=100, jitMs=1)
        second.update(bodyMs=10000, jitMs=0)
        second['groups'][2]['compiledMethods'] = 10
        result = session.side_quiescence([dict(jit=first), dict(jit=second)])
        self.assertTrue(result['established'])
        self.assertFalse(result['group3NotAboveGroup1'])
        self.assertAlmostEqual(result['jitFraction'], 1 / 10100)
        first.update(bodyMs=1000, jitMs=5)
        self.assertTrue(session.side_quiescence([dict(jit=first)])['established'])
        first['jitMs'] = 5.001
        self.assertFalse(session.side_quiescence([dict(jit=first)])['established'])

    def test_one_allowlist_never_passes_caller_generated_paths_or_credentials(self):
        ambient = {key: 'ambient-canary' for key in session.GENERATED}
        ambient.update(PATH=os.environ['PATH'], GH_TOKEN='CANARY_token', API_KEY='CANARY_key', DOTNET_TieredPGO='ambient-canary')
        clean = session.environment({'DOTNET_TieredPGO': '1', 'P1_RANGE': '/generated/range'}, ambient)
        self.assertNotIn('ambient-canary', json.dumps(clean))
        self.assertEqual(clean['P1_RANGE'], '/generated/range')
        self.assertEqual(clean['DOTNET_TieredPGO'], '1')
        self.assertNotIn('GH_TOKEN', clean)
        self.assertNotIn('API_KEY', clean)
        with self.assertRaises(ValueError):
            session.environment({'GH_TOKEN': 'CANARY'})

    def fake(self, script, *, timeout=3, snapshot=None):
        def synthetic():
            stamp = time.monotonic()
            ticks = int(stamp * 1000000)
            cpu = {key: [ticks, 0, 0, ticks, 0, 0, 0, 0] for key in ['cpu', *['cpu' + str(i) for i in range(8)]]}
            return dict(monotonic=stamp, unix=time.time(), load1=0, cpuStat=cpu, processes={})
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fake = root / 'client.py'
            fake.write_text(script)
            env = session.environment({})
            with patch.dict(os.environ, {'GH_TOKEN': 'CANARY_token', 'API_KEY': 'CANARY_key'}), \
                    patch.object(session, 'snapshot', side_effect=snapshot or synthetic):
                result = session.monitored_process([sys.executable, str(fake)], root, env, root / 'execution', timeout, platform='fly')
            contents = '\n'.join(path.read_text() for path in root.rglob('*') if path.is_file())
            self.assertNotIn('CANARY_token', contents)
            self.assertNotIn('CANARY_key', contents)
            return result

    def test_acknowledged_measured_marks_and_no_secret(self):
        result = self.fake("import os,time\nprint('MEASURED_START',flush=True)\nassert input()=='ACK'\ntime.sleep(.02)\nprint('MEASURED_END',flush=True)\nassert input()=='ACK'\n")
        self.assertEqual(result['exitCode'], 0)
        self.assertEqual(set(result['marks']), {'MEASURED_START', 'MEASURED_END'})
        self.assertGreater(result['marks']['MEASURED_END']['monotonic'], result['marks']['MEASURED_START']['monotonic'])

    def test_no_mark_and_nonanswering_client_never_admitted(self):
        missing = self.fake("print('no marks')\n")
        self.assertIn('Missing measured-start or measured-end mark', missing['timingRejectedReasons'])
        hung = self.fake("import time\nprint('MEASURED_START',flush=True)\ninput()\ntime.sleep(30)\n", timeout=.15)
        self.assertIn('TimeoutError', hung['error'])
        self.assertTrue(hung['timingRejectedReasons'])

    def test_only_measured_contamination_enters_admission(self):
        # First snapshot is before warm-up; snapshots at/after START define the window.
        def run(contaminated):
            count = 0
            def snap():
                nonlocal count
                count += 1
                ticks = (0 if count == 1 else 100) + (10 * (count - 2) if contaminated and count > 2 else 0)
                cpu = {key: [count * 100, 0, 0, count * 100, 0, 0, 0, 0]
                       for key in ['cpu', *['cpu' + str(i) for i in range(8)]]}
                return dict(monotonic=float(count), unix=float(count), load1=0, cpuStat=cpu,
                            processes={os.getpid(): dict(ppid=100000001, startTicks=1, cpuTicks=0, name='runner'),
                                       100000001: dict(ppid=0, startTicks=1, cpuTicks=10 * max(0, count - 2), name='control-parent'),
                                       999999: dict(ppid=0, startTicks=1, cpuTicks=ticks, name='outside')})
            return self.fake("import time\nprint('MEASURED_START',flush=True)\ninput()\ntime.sleep(.02)\nprint('MEASURED_END',flush=True)\ninput()\n", snapshot=snap)
        self.assertEqual(run(False)['timingRejectedReasons'], [])
        self.assertTrue(any('outside process CPU' in reason for reason in run(True)['timingRejectedReasons']))

    def test_inventory_never_reads_command_lines(self):
        original = Path.read_text
        def guarded(path, *args, **kwargs):
            self.assertNotIn(path.name, ('cmdline', 'environ'))
            return original(path, *args, **kwargs)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            process = root / str(os.getpid())
            process.mkdir()
            fields = ['0'] * 20
            fields[0], fields[1], fields[11], fields[12], fields[19] = 'S', '1', '2', '3', '123'
            (process / 'stat').write_text(f'{os.getpid()} (fake process) ' + ' '.join(fields))
            (process / 'status').write_text('Kthread:\t0\n')
            with patch.object(Path, 'read_text', guarded):
                result = session.proc_inventory(root)
            self.assertEqual(result[os.getpid()]['cpuTicks'], 5)


class ReviewSessionTests(unittest.TestCase):
    def test_pair_order_alternates_fresh_members(self):
        calls = []
        def member(spec, job, dest):
            calls.append((spec, dest.name))
            return {}
        with tempfile.TemporaryDirectory() as directory, patch.object(session, 'member', side_effect=member):
            session.run(dict(pairs=3, kind='direct', sides=dict(baseline='left', candidate='right')), Path(directory) / 'run')
        self.assertEqual(calls, [('left', 'baseline'), ('right', 'candidate'), ('right', 'candidate'), ('left', 'baseline'), ('left', 'baseline'), ('right', 'candidate')])

    def test_platform_detects_machine_not_job_default(self):
        with patch.dict(os.environ, {}, clear=True):
            self.assertEqual(session.detect_platform(), 'pc')
        with patch.dict(os.environ, dict(FLY_APP_NAME='performance-test', FLY_REGION='iad'), clear=True), \
                patch.object(session.os, 'cpu_count', return_value=8), patch.object(session.platform, 'machine', return_value='x86_64'):
            self.assertEqual(session.detect_platform(), 'fly')
            with patch.object(session.os, 'cpu_count', return_value=24), self.assertRaisesRegex(ValueError, 'Fly'):
                session.detect_platform()
            with self.assertRaisesRegex(ValueError, 'Fly'):
                session.detect_platform('ams')
            with patch.dict(os.environ, FLY_REGION='ams'):
                self.assertEqual(session.detect_platform('ams'), 'fly')
                with self.assertRaisesRegex(ValueError, 'Fly'):
                    session.detect_platform()
        with patch.dict(os.environ, dict(FLY_REGION='iad'), clear=True), self.assertRaisesRegex(ValueError, 'Fly'):
            session.detect_platform()

    def test_member_passes_sealed_or_default_region_to_platform_check(self):
        with tempfile.TemporaryDirectory() as directory:
            for kwargs, expected in [({}, 'iad'), (dict(region='ams'), 'ams')]:
                with self.subTest(expected=expected), patch.object(session, 'detect_platform', side_effect=ValueError('stop')) as detected:
                    with self.assertRaisesRegex(ValueError, '^stop$'):
                        session.member({}, {}, Path(directory) / expected, **kwargs)
                    detected.assert_called_once_with(expected)

    def test_pc_acknowledges_marks_without_reading_proc(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            script = root / 'client.py'
            script.write_text("print('MEASURED_START',flush=True)\nassert input()=='ACK'\nprint('MEASURED_END',flush=True)\nassert input()=='ACK'\n")
            with patch.object(session, 'snapshot', side_effect=AssertionError('PC /proc sampling')):
                result = session.monitored_process([sys.executable, str(script)], root, session.environment({}), root / 'execution', 3, platform='pc')
            self.assertEqual(result['exitCode'], 0)
            self.assertEqual(set(result['marks']), {'MEASURED_START', 'MEASURED_END'})
            self.assertEqual(result['samples'], [])
            self.assertIn('PC time never gates', result['timingRejectedReasons'])


if __name__ == '__main__':
    unittest.main()
