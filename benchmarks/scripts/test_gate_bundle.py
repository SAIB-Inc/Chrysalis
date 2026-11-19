"""Bundle preparation uses real content binding with only fake measurements."""
import copy
import json
from pathlib import Path
import shutil
import tempfile
import tarfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import gate
import gate_bundle as bundle
import gate_time as timing
from test_gate import side, fixture
from test_gate_time import row, member, probe_rule


SCOUT = [dict(id=f'time/preprod-scout{i}-offline', kind='time', pairs=8, chargeSeconds=13,
              rowClass='recorded-only', explanation='CAUSE.md') for i in range(1, 7)]


class BundleTests(unittest.TestCase):
    def test_common_micro_work_is_calibrated_once_from_baseline(self):
        self.assertEqual(bundle.calibrate_micro(dict(nsPerBlock=1_000_000, blocks=16), 16), 10)
        self.assertEqual(bundle.calibrate_micro(dict(nsPerBlock=100_000_000, blocks=16), 16), 1)
        for value in (0, -1, float('nan')):
            with self.assertRaises(ValueError):
                bundle.calibrate_micro(dict(nsPerBlock=value, blocks=16), 16)

    def test_probe_review_prepare_refuses_missing_or_null_before_preflight(self):
        for config in ({}, {'probe': None}, {'probe': dict(probe_rule(), processes=0)}):
            with self.subTest(config=config), patch.object(bundle.subprocess, 'check_output') as launch:
                with self.assertRaisesRegex(ValueError, 'probe rule'):
                    bundle.prepare(Path('unused'), [row()], {}, {'time': dict(config, noise={})}, {'flyBudgetLeftUsd': 40})
                launch.assert_not_called()

    def test_dry_run_and_seal_preserve_distinct_and_shared_closures(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            repo = root / 'repo'
            scripts = repo / 'benchmarks/scripts'; scripts.mkdir(parents=True)
            for name in bundle.HOST_FILES['fly']:
                shutil.copy2(Path(bundle.__file__).parent / name, scripts / name)
            fixtures = repo / 'benchmarks/Chrysalis.Benchmarks'; fixtures.mkdir()
            (fixtures / 'fixtures').mkdir()
            block = fixtures / 'fixtures/one.cbor'; block.write_text('OBVIOUS PUBLIC FIXTURE')
            data = repo / 'benchmarks/data/plutus_use_cases'; data.mkdir(parents=True)
            script = data / 'example.flat'; script.write_text('OBVIOUS PUBLIC SCRIPT FIXTURE')
            gate.session.save(fixtures / 'sha256.json', {'fixtures/one.cbor': gate.digest(block), '../data/plutus_use_cases/example.flat': gate.digest(script)})
            python = root / 'python/bin'; python.mkdir(parents=True)
            (python / 'python3.12').write_text('OBVIOUS TEST PLACEHOLDER')
            runtime = root / 'runtime'; runtime.mkdir()
            (runtime / 'dotnet').write_text('OBVIOUS TEST PLACEHOLDER')
            for family in ('host/fxr', 'shared/Microsoft.NETCore.App'):
                path = runtime / family / '11.0.0'; path.mkdir(parents=True)
                (path / 'placeholder.txt').write_text('OBVIOUS TEST PLACEHOLDER')
            client = root / 'client'; client.mkdir()
            (client / 'ReplayClient.dll').write_text('OBVIOUS TEST PLACEHOLDER')
            (client / 'Chrysalis.Benchmarks.dll').write_text('OBVIOUS TEST PLACEHOLDER')
            staged = dict(side(), client=str(client / 'ReplayClient.dll'), harness=str(client / 'Chrysalis.Benchmarks.dll'), dllSha256s=gate.closure_hashes(client))
            baseline_client = root / 'baseline-client'
            shutil.copytree(client, baseline_client)
            (baseline_client / 'ReplayClient.dll').write_text('OBVIOUS DIFFERENT BASELINE DLL')
            baseline = dict(staged, client=str(baseline_client / 'ReplayClient.dll'),
                            harness=str(baseline_client / 'Chrysalis.Benchmarks.dll'), dllSha256s=gate.closure_hashes(baseline_client))
            builds = dict(baseline=baseline, candidate=staged)
            r = row()
            r['sides'] = {role: gate.public_side(spec) for role, spec in builds.items()}
            output = root / 'output'; output.mkdir()
            config = dict(_mode='change', time=dict(noise={'offline': r['noise']}, option=2, probe=probe_rule()))
            calls = []
            mutate = False
            def fake(spec, job, destination):
                calls.append(copy.deepcopy(job))
                self.assertEqual(gate.closure_hashes((Path(job['root']) / spec['client']).parent), spec['dllSha256s'])
                fixture_root = Path(job['environment']['CHRYSALIS_BENCHMARK_DATA'])
                hashes = json.loads((fixture_root / 'sha256.json').read_text())
                self.assertTrue(all(gate.digest(fixture_root / name) == expected for name, expected in hashes.items()))
                self.assertTrue((fixture_root / '../data/plutus_use_cases/example.flat').resolve().is_relative_to(Path(job['root'])))
                result = member()
                result.update(seconds=.01, identity={key: spec[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')})
                result['data']['warmupPasses'] = result['data']['passes'] = 1
                result['data']['warmupRows'] = result['data']['warmupRows'][:1]
                result['data']['rows'] = result['data']['rows'][:1]
                if job.get('replay') == 'micro':
                    for stage in ('rows','warmupRows'):
                        result['data'][stage][0].update(nsPerBlock=1000,blocks=1)
                    if mutate:
                        (fixture_root/'fixtures/one.cbor').write_text('OBVIOUS CHANGED FIXTURE')
                return result
            with patch.object(gate, 'REPO', repo), patch.object(gate.runner, 'DOTNET_ROOT', runtime), \
                    patch.object(bundle.subprocess, 'check_output', return_value='fly fixture-v1') as version, \
                    patch.object(timing.session, 'member', side_effect=fake):
                result = bundle.prepare(output, [r], builds, config,
                                        dict(flyPythonRoot=str(python.parent), corporaRoot='unused', flyBudgetLeftUsd=40))
            self.assertEqual(version.call_args.kwargs['env'].get('FLY_NO_UPDATE_CHECK'), '1')
            installed_sides = result['timePlans'][0]['rows'][0]['sides']
            self.assertNotEqual(installed_sides['baseline']['client'], installed_sides['candidate']['client'])
            self.assertEqual(len(calls), 2)
            self.assertTrue(all(job['counts'] == [1, 1] for job in calls))
            scouts = [dict(entry, sides=r['sides']) for entry in SCOUT]
            with patch.object(gate, 'REPO', repo), patch.object(gate.runner, 'DOTNET_ROOT', runtime), \
                    patch.object(bundle.subprocess, 'check_output', return_value='fly fixture-v1'), \
                    patch.object(timing.session, 'member', side_effect=fake):
                scout_config = dict(config, _sessionPerRow=True)
                with self.assertRaisesRegex(ValueError, 'gated.*probe|probe.*gated'):
                    bundle.prepare(root / 'scout', scouts, builds, scout_config,
                                   dict(flyPythonRoot=str(python.parent), corporaRoot='unused', flyBudgetLeftUsd=40))
            micro=dict(r,id='time/micro-hashing')
            shared_micro = dict(micro, sides={role: gate.public_side(staged) for role in builds})
            micro_output=root/'micro-output';micro_output.mkdir()
            micro_config=copy.deepcopy(config);micro_config['time']['micro']={'hashing':'CoreBench.Blake256'}
            calls.clear()
            with patch.object(gate,'REPO',repo),patch.object(gate.runner,'DOTNET_ROOT',runtime),patch.object(bundle.subprocess,'check_output',return_value='fly fixture-v1'),patch.object(timing.session,'member',side_effect=fake):
                micro_result = bundle.prepare(micro_output, [shared_micro], dict(candidate=staged), micro_config,
                                              dict(flyPythonRoot=str(python.parent), corporaRoot='unused', flyBudgetLeftUsd=40))
            micro_sides = micro_result['timePlans'][0]['rows'][0]['sides']
            self.assertEqual(micro_sides['baseline']['client'], micro_sides['candidate']['client'])
            self.assertEqual(len(calls),3)  # baseline probe, then final baseline/candidate
            self.assertEqual(calls[0]['microInvocations'],16)
            self.assertEqual(calls[1]['microInvocations'],calls[2]['microInvocations'])
            # The configured rule is sealed independently of the measured workload.
            configured = dict(config, time=dict(config['time'], probe=json.loads((gate.GATE / 'gated.json').read_text())['time']['probe']))
            capture = root / 'corpus/capture'; capture.mkdir(parents=True)
            gate.session.save(capture.parent / 'range.json', {})
            for name in ('capture.json', 'server-to-client.bin', 'client-to-server.bin'):
                (capture / name).write_text('OBVIOUS TEST PLACEHOLDER')
            gate.session.save(capture / 'sha256.json', {p.name: gate.digest(p) for p in capture.iterdir()})
            local = dict(flyPythonRoot=str(python.parent), corporaRoot=str(capture.parent), flyBudgetLeftUsd=40)
            with patch.object(gate, 'REPO', repo), patch.object(gate.runner, 'DOTNET_ROOT', runtime), \
                    patch.object(bundle.subprocess, 'check_output', return_value='fly fixture-v1'), \
                    patch.object(timing.session, 'member', side_effect=fake):
                tcp = dict(r, id='time/preprod-tcp', replay='tcp')
                tcp_output = root / 'tcp-only'
                prepared, = bundle.prepare(tcp_output, [tcp], builds, configured, local)['timePlans']
                self.assertEqual(prepared['probe'], probe_rule())
                with tarfile.open(tcp_output / 'input/session-0.tar.gz') as archive:
                    worker = json.load(archive.extractfile('plan.json'))
                    self.assertEqual(worker['workloads'][worker['probe']['workload']]['replay'], 'offline')
                    self.assertEqual(worker['counts'], [180, 60])
                with self.assertRaisesRegex(ValueError, 'gated.*probe|probe.*gated'):
                    bundle.prepare(root / 'no-reference', [dict(tcp, rowClass='recorded-only')], builds, configured, local)
                release = dict(configured, _mode='release', time=dict(configured['time'], micro={'hashing': '*'}))
                band, ordinary = bundle.prepare(root / 'micro-band', [micro], builds, release, local)['timePlans']
                self.assertEqual((band['design'], band['repeatAllowed']), ('micro-band', False))
                self.assertEqual(band['rawSeconds'], 208 + sum(2 * r['chargeSeconds'] * r['aPairs'] for r in band['rows']))
                self.assertAlmostEqual(band['limitUsd'], timing.price(band['destroyGuardSeconds']) + 5 * timing.price(1068))
                self.assertEqual(band['limitWithRepeatUsd'], band['limitUsd'])
                local['flyBudgetLeftUsd'] = ordinary['limitWithRepeatUsd'] + band['limitUsd'] - .00001
                with self.assertRaisesRegex(ValueError, 'Aggregate timing reservation exceeds budget'):
                    bundle.prepare(root / 'aggregate-refusal', [micro], builds, release, local)
            mutate=True
            changed_output=root/'changed-probe-output';changed_output.mkdir()
            with patch.object(gate,'REPO',repo),patch.object(gate.runner,'DOTNET_ROOT',runtime),patch.object(bundle.subprocess,'check_output',return_value='fly fixture-v1'),patch.object(timing.session,'member',side_effect=fake):
                with self.assertRaisesRegex(ValueError,'Bundle input changed'):
                    bundle.prepare(changed_output,[micro],builds,micro_config,dict(flyPythonRoot=str(python.parent),corporaRoot='unused',flyBudgetLeftUsd=40))
            self.assertEqual(result['timePlans'][0]['rows'][0]['expectedWork'], dict(blocks=1020))
            self.assertIn('input/dry-pairs.json', result['files'])
            with tarfile.open(output / 'input/session-0.tar.gz') as archive:
                self.assertEqual(archive.extractfile('benchmarks/Chrysalis.Benchmarks/fixtures/one.cbor').read(), block.read_bytes())
                self.assertEqual(archive.extractfile('benchmarks/data/plutus_use_cases/example.flat').read(), script.read_bytes())
            seal = dict(result, mode='change', rows=[r], bands={})
            gate.write_seal(output, seal)
            gate.read_seal(output)
            (output / 'input/session-0.tar.gz').write_bytes(b'CHANGED')
            with self.assertRaises(ValueError):
                gate.read_seal(output)

    def test_manifest_parent_escape_still_refused_at_common_benchmarks_root(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            source = root / 'benchmarks'
            (source / 'Chrysalis.Benchmarks').mkdir(parents=True)
            (root / 'outside.fixture').write_text('OBVIOUS OUTSIDE-ROOT FIXTURE')
            destination = root / 'bundle'
            with self.assertRaisesRegex(ValueError, 'Escaping bundle file'):
                bundle.copy_files(source, destination, files=[Path('Chrysalis.Benchmarks/../../outside.fixture')])
            self.assertFalse((destination / 'outside.fixture').exists())

    def test_recorded_row_keeps_numbers_link_and_cannot_be_downgraded_by_plan(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            seal = fixture(root)
            r = seal['rows'][0]
            r.update(rowClass='recorded-only', explanation='CAUSE.md')
            seal['releaseSet'] = []
            gate.write_seal(root, seal)
            result = gate.report(root)
            self.assertEqual(result['outcome'], 'RELEASE PASS')
            self.assertIn('harness artefact, explained, not gated', result['text'])
            self.assertIn('CAUSE.md', result['text'])
            self.assertEqual(result['rows']['Example']['baselineMeanBytes'], 10)
            # A normal catalogue row cannot be hidden by an ad-hoc plan.
            config = json.loads((gate.GATE / 'gated.json').read_text())
            audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
            path = root / 'selection.json'
            gate.session.save(path, [dict(id='CoreBench.Blake256', kind='direct', rowClass='recorded-only', explanation='CAUSE.md')])
            with self.assertRaisesRegex(ValueError, 'cannot be removed'):
                gate.select_rows(SimpleNamespace(mode='release', plan=path, only=None), config, audit)

            for value, entries, mode in [(True, SCOUT + [dict(id='CoreBench.Blake256')], 'change'),
                                         (True, SCOUT + [dict(id='scout-only-bytes', kind='direct', rowClass='recorded-only', explanation='CAUSE.md')], 'change'),
                                         (True, SCOUT + [dict(id='time/preprod-offline')], 'change'),
                                         (True, SCOUT, 'release'), (1, SCOUT, 'change'), (None, SCOUT, 'change')]:
                with self.subTest(value=value, entries=len(entries), mode=mode):
                    gate.session.save(path, dict(entries=entries, sessionPerRow=value))
                    with self.assertRaisesRegex(ValueError, 'sessionPerRow'):
                        gate.select_rows(SimpleNamespace(mode=mode, plan=path, only=None), config, audit)
            for region, enabled in [('ams', False), ('zzz', True), ('lhr', True), ('yyz', True), (None, True), ([], True)]:
                with self.subTest(region=region, sessionPerRow=enabled):
                    gate.session.save(path, dict(entries=[dict(SCOUT[0], region=region)], sessionPerRow=enabled))
                    with self.assertRaisesRegex(ValueError, 'region'):
                        gate.select_rows(SimpleNamespace(mode='change', plan=path, only=None), config, audit)
            for payload, enabled in [(dict(entries=[dict(SCOUT[0], region='arn'), *SCOUT[1:]], sessionPerRow=True), True), (SCOUT, False),
                                     (dict(entries=SCOUT, sessionPerRow=False), False)]:
                gate.session.save(path, payload)
                rows = gate.select_rows(SimpleNamespace(mode='change', plan=path, only=None), config, audit)
                self.assertEqual(len(rows), 6)
                self.assertEqual(rows[0].get('region'), 'arn' if enabled else None)
                self.assertIs(config.get('_sessionPerRow', False), enabled)


    def test_az2f4_importer_reports_guest_refusal_reason_and_decodes_clean_status(self):
        with tempfile.TemporaryDirectory() as td:
            note = Path(td)
            archive = note / 'results.tar.gz'
            import io
            diag = {'diagnosticOnly': True, 'reason': 'results-too-large', 'expandedBytes': 68327653, 'maxExpandedBytes': 10000000}
            status = {'completedAllDeclaredRounds': True, 'exitCode': 0, 'probe': 'kept'}
            startup = {'available': True, 'members': 64}
            with tarfile.open(archive, 'w:gz') as stream:
                for name, obj in (('results/recovery-diagnostic.json', diag),
                                  ('results/session-status.json', status),
                                  ('results/session-start.json', startup)):
                    raw = (json.dumps(obj) + '\n').encode()
                    info = tarfile.TarInfo(name)
                    info.size = len(raw)
                    stream.addfile(info, io.BytesIO(raw))
            (note / 'results.sha256').write_text(bundle.sha(archive) + '\n')
            (note / 'machine.json').write_text(json.dumps(dict(provider='azure', platform='azure',
                machineClass='Standard_F8als_v7', region='eastus2')))
            (note / 'cleanup-proof.json').write_text(json.dumps(dict(ourAppAbsent=True)))
            (note / 'duration-cost.json').write_text(json.dumps(dict(totalEstimateWithReserveUsd=0.2,
                cleanupOutstanding=False, withinSealedGuard=True, cleanupStartedSeconds=100, lifetimeSeconds=120)))
            (note / 'diagnostic-only.json').write_text(json.dumps(dict(partial=True, reason='results-too-large')))
            plan = dict(provider='azure', region='eastus2', destroyGuardSeconds=2494, measurementCutoffSeconds=2149,
                        limitUsd=1.0, limitWithRepeatUsd=1.0, probe=probe_rule())
            machine = bundle.verified_machine(note, plan, 0)
            self.assertIn('results-too-large', machine['invalid'])
            self.assertIn('68327653', machine['invalid'])
            self.assertNotIn('missing or malformed session-status.json', machine['invalid'])
            payload = bundle.archive_payload(note, plan, machine=machine)
            self.assertIn('results-too-large', payload['invalid'])
            self.assertEqual(payload['startup'], startup)

    def test_az2f4_fix3_fly_diagnostic_matches_base_import(self):
        import io
        from test_gate_fly import probe_archive, probe_plan
        def write_tgz(path, entries):
            with tarfile.open(path, 'w:gz') as stream:
                for name, data in entries.items():
                    info = tarfile.TarInfo(name)
                    info.size = len(data)
                    stream.addfile(info, io.BytesIO(data))
        with tempfile.TemporaryDirectory() as td:
            note = Path(td) / 'fly'
            plan = probe_plan(row(pairs=2), 'change')
            probe_archive(note, plan, 'keep')
            with tarfile.open(note / 'results.tar.gz') as stream:
                parts = {m.name: stream.extractfile(m).read() for m in stream}
            parts['results/recovery-diagnostic.json'] = b'{"diagnosticOnly":true}\n'
            write_tgz(note / 'results.tar.gz', parts)
            (note / 'results.sha256').write_text(bundle.sha(note / 'results.tar.gz') + '\n')
            (note / 'cleanup-proof.json').write_text(json.dumps(dict(ourAppAbsent=True)))
            (note / 'duration-cost.json').write_text(json.dumps(dict(
                totalEstimateWithReserveUsd=0.1, cleanupOutstanding=False, withinSealedGuard=True)))
            (note / 'diagnostic-only.json').write_text('{"diagnosticOnly":true}\n')
            fly_payload = bundle.archive_payload(note, plan)
            self.assertEqual(fly_payload['invalid'], 'session invalid: diagnostic-only.json')
            mc = len(json.loads(parts['results/members.json']))
            self.assertEqual((len(fly_payload['pairs']), len(fly_payload['context']['members'])), (mc // 2, mc))
            self.assertEqual(bundle.verified_machine(note, plan, 0)['invalid'], 'session invalid: failed worker or recovery diagnostic')
            # Overlong (>32 chars) reason and bare Fly marker without session-status.json both fall back to base decode.
            for marker_bytes in (b'{"diagnosticOnly":true}\n', (json.dumps({'diagnosticOnly': True, 'reason': 'a' * 33}) + '\n').encode()):
                bare = Path(td) / f'bare-{len(marker_bytes)}'
                bare.mkdir()
                write_tgz(bare / 'results.tar.gz', {'results/recovery-diagnostic.json': marker_bytes})
                (bare / 'cleanup-proof.json').write_text(json.dumps(dict(ourAppAbsent=True)))
                res = bundle.archive_payload(bare)
                self.assertEqual(res['invalid'], 'session invalid: missing or malformed completion or pairs')
                self.assertEqual(res['startup']['reason'], 'Results could not be decoded')

if __name__ == '__main__':
    unittest.main()
