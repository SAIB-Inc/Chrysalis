"""Bundle preparation uses real content binding with only fake measurements."""
import copy
import json
from pathlib import Path
import shutil
import tempfile
import tarfile
import unittest
from unittest.mock import patch

import gate
import gate_bundle as bundle
import gate_time as timing
from test_gate import side, fixture
from test_gate_time import row, member


class BundleTests(unittest.TestCase):
    def test_common_micro_work_is_calibrated_once_from_baseline(self):
        self.assertEqual(bundle.calibrate_micro(dict(nsPerBlock=1_000_000, blocks=16), 16), 10)
        self.assertEqual(bundle.calibrate_micro(dict(nsPerBlock=100_000_000, blocks=16), 16), 1)
        for value in (0, -1, float('nan')):
            with self.assertRaises(ValueError):
                bundle.calibrate_micro(dict(nsPerBlock=value, blocks=16), 16)

    def test_dry_run_and_seal_preserve_distinct_and_shared_closures(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            repo = root / 'repo'
            scripts = repo / 'benchmarks/scripts'; scripts.mkdir(parents=True)
            for name in bundle.HOST_FILES:
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
            config = dict(_mode='change', time=dict(noise={'offline': r['noise']}, option=2))
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
                    patch.object(bundle.subprocess, 'check_output', return_value='fly fixture-v1'), \
                    patch.object(timing.session, 'member', side_effect=fake):
                result = bundle.prepare(output, [r], builds, config,
                                        dict(flyPythonRoot=str(python.parent), corporaRoot='unused', flyBudgetLeftUsd=40))
            installed_sides = result['timePlans'][0]['rows'][0]['sides']
            self.assertNotEqual(installed_sides['baseline']['client'], installed_sides['candidate']['client'])
            self.assertEqual(len(calls), 2)
            self.assertTrue(all(job['counts'] == [1, 1] for job in calls))
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
            from types import SimpleNamespace
            with self.assertRaisesRegex(ValueError, 'cannot be removed'):
                gate.select_rows(SimpleNamespace(mode='release', plan=path, only=None), config, audit)


if __name__ == '__main__':
    unittest.main()
