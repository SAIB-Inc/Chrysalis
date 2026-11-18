"""Sides, provenance, sealed pure reports, coverage and release outcomes."""
import copy
import json
import os
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch

import gate


def side(runtime='11.0.0', commit='candidate'):
    return dict(commit=commit, treeId=commit + '-tree', runtime=runtime, sdk='11.0.100', tfm='net11.0', dllSha256s={})


def pair(number, value=10, *, identity=None):
    identity = identity or side()
    member = dict(identity={key: identity[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')},
                  data=[dict(name='Example', samples=[dict(bytes=value)] * 5)], load=[1, 2], platform='pc')
    return dict(pair=number, baseline=copy.deepcopy(member), candidate=copy.deepcopy(member))


def fixture(root, *, mode='release', rows=None):
    rows = rows or [dict(id='Example', kind='direct', rule='D39', design='direct', pairs=5,
                        sides=dict(baseline=side(), candidate=side()))]
    seal = dict(mode=mode, ruleVersion=gate.stats.RULE_VERSION, rows=rows, canonical=True, bands={}, timeScopeDecided=True,
                gated=dict(areas={'Example area': dict(bytes=['Example'], time=[])}), releaseSet=[row['id'] for row in rows])
    gate.write_seal(root, seal)
    pairs = [pair(number) for number in range(1, 6)]
    path = root / 'data/first.json'
    gate.session.save(path, pairs)
    gate.session.save(root / 'data/index.json', [dict(ids=['Example'], phase='first', path='data/first.json', sha256=gate.digest(path))])
    return seal


class GateTests(unittest.TestCase):
    def test_local_file_lists_only_missing_names(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'local.json'
            path.write_text(json.dumps(dict(flyOrg='CANARY_private_value')))
            with patch.dict(os.environ, CHRYSALIS_GATE_LOCAL=str(path)):
                with self.assertRaises(ValueError) as error:
                    gate.local_config()
            self.assertNotIn('CANARY_private_value', str(error.exception))
            self.assertIn('pinnedBaseline', str(error.exception))

    def test_git_children_share_the_allowlist(self):
        with patch.dict(os.environ, {'GH_TOKEN': 'CANARY_ambient'}), \
                patch.object(gate.subprocess, 'check_output', return_value='tree') as call:
            self.assertEqual(gate.git('rev-parse', 'HEAD'), 'tree')
            passed = call.call_args.kwargs['env']
            self.assertNotIn('GH_TOKEN', passed)
            self.assertEqual(passed['GIT_OPTIONAL_LOCKS'], '0')

    def test_release_and_band_refuse_working_tree_side(self):
        with tempfile.TemporaryDirectory() as directory:
            for mode in ('release', 'band'):
                with self.subTest(mode=mode), self.assertRaisesRegex(ValueError, 'Working-tree'):
                    gate.resolve_side(directory, mode)
            with patch.object(gate, 'git', return_value='commit'), patch.object(gate.runner, 'working_tree_id', return_value='tree'):
                resolved = gate.resolve_side(directory, 'change')
                self.assertEqual((resolved['commit'], resolved['treeId']), ('commit', 'tree'))

    def test_field_warm10_protocol_is_sealed_and_old_band_window_does_not_match(self):
        config = json.loads((gate.GATE / 'gated.json').read_text())
        audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
        row = next(item for item in gate.catalogue(config, audit) if item['id'] == 'FieldReadBench.ReadEveryField')
        self.assertEqual(row.get('warmupMilliseconds'), 10000)
        self.assertEqual(gate.band_window(row), 'readeveryfield/warm10')
        job = gate.make_job(row, side(), side(), dict(corporaRoot='fixture'), pairs=10, design='short')
        self.assertEqual(job['environment'].get('CHRYSALIS_DIRECT_WARMUP_MS'), '10000')
        raw = pair(1)
        for role in ('baseline', 'candidate'):
            raw[role]['data'][0].update(name=row['id'], warmupMilliseconds=1000)
        with self.assertRaisesRegex(ValueError, 'warm-up'):
            gate.pair_values(row, [raw], 'direct')

    def test_direct_warm10_keys_and_job_composition_are_versioned(self):
        config = json.loads((gate.GATE / 'gated.json').read_text())
        audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
        for row in gate.catalogue(config, audit):
            if row['kind'] == 'direct' and row['rule'] == 'range':
                self.assertEqual(row['warmupMilliseconds'], 10000)
                self.assertTrue(gate.band_window(row).endswith('/warm10'))
                self.assertNotEqual(gate.band_key(row, '11.0.0', 'direct'), gate.band_key(dict(row, window='direct'), '11.0.0', 'direct'))
                job = gate.make_job(row, side(), side(), dict(corporaRoot='fixture'), pairs=10, design='short')
                self.assertEqual(job['environment']['CHRYSALIS_DIRECT_WARMUP_MS'], '10000')
                self.assertEqual(job['filter'], row.get('processFilter', row['id']))

    def test_declared_counts_keep_zero_band_and_replay_defaults(self):
        config = json.loads((gate.GATE / 'gated.json').read_text())
        audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
        for mode in ('change', 'release'):
            args = SimpleNamespace(mode=mode, plan=None, only='*')
            rows = {r['id']: r for r in gate.select_rows(args, config, audit)}
            self.assertEqual(rows['FieldReadBench.ReadEveryField']['pairs'], 16)
            for name in ('TxBench.SimpleBuildSign', 'TxBench.ScriptBuildFeeSign', 'BlockBench.DecodeBlockReadInputs'):
                self.assertEqual(rows[name]['pairs'], config['defaults'][mode]['directPairs'])
            cohort=[dict(r,sides=dict(baseline=side(),candidate=side())) for r in rows.values() if r['kind']=='direct' and r.get('window')=='direct/warm10']
            self.assertEqual(len(gate.measurement_groups(cohort)),1)
            cohort[1]['pairs'] += 1
            with self.assertRaisesRegex(ValueError,'cohort'):
                gate.measurement_groups(cohort)
            self.assertEqual(rows['CoreBench.Sign']['pairs'], config['defaults'][mode]['directPairs'])
            self.assertEqual(rows['replay/tcp/steady']['pairs'], config['defaults'][mode]['replayPairs'])
            self.assertEqual(rows['time/micro-hashing']['pairs'], 16)
            self.assertEqual(rows['time/preprod-tcp']['pairs'], 34 if mode == 'change' else 16)
            self.assertEqual(rows['time/preprod-offline']['pairs'], 40 if mode == 'change' else 16)

    def test_all_staged_dotnet_commands_disable_build_servers(self):
        candidate_sdk = json.loads((gate.REPO / 'global.json').read_text())['sdk']['version']
        for commit, sdk in ((gate.PIN, '10.0.301'), ('candidate', candidate_sdk)):
            with self.subTest(sdk=sdk), tempfile.TemporaryDirectory() as directory:
                def stage(command, *_args, **_kwargs):
                    gate.stage_worker(command[-1])
                with patch.object(gate, 'export_side', return_value=gate.REPO), \
                        patch.object(gate, 'check_pinned', return_value=dict(assemblySha256s={})), \
                        patch.object(gate, 'execute_logged', side_effect=stage), \
                        patch.object(gate.subprocess, 'call', return_value=0) as run:
                    gate.stage_side(side(commit=commit), 'fixture', Path(directory), dict(pinnedBaseline='fixture'))
                self.assertEqual(run.call_count, 4)
                self.assertEqual([call.args[0][2] for call in run.call_args_list], ['restore', 'build', 'restore', 'build'])
                for call in run.call_args_list:
                    command = call.args[0]
                    self.assertIn('/sdk/' + sdk + '/dotnet.dll', command[1])
                    with self.subTest(command=command):
                        for flag in ('-nodeReuse:false', '--disable-build-servers', '-p:UseSharedCompilation=false'):
                            self.assertIn(flag, command)

    def test_public_worker_keeps_nonsecret_preparation_settings(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local = dict(corporaRoot='corpus', pinnedBaseline='baseline', flyPythonRoot='python',
                         flyBudgetLeftUsd=.001, flyOrg='fixture', flyTokenFile='DO NOT FORWARD', flyLock='lock')
            def held(command, log, **kwargs):
                payload = json.loads(Path(command[-1]).read_text())
                self.assertEqual(payload['local'], {k:local[k] for k in ('corporaRoot','pinnedBaseline','flyPythonRoot','flyBudgetLeftUsd')})
                import gate_time
                from test_gate_time import row
                with self.assertRaisesRegex(ValueError,'budget'):
                    gate_time.size_sessions([row()], 'change', budget=payload['local']['flyBudgetLeftUsd'])
                (root / 'report.txt').write_text('fixture')
            with patch.object(gate,'local_config',return_value=local), patch.object(gate,'new_output',return_value=root), patch.object(gate,'heavy',side_effect=held):
                gate.pc_command(SimpleNamespace(command='run'))

    def test_remaining_budget_is_required_and_fails_closed(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / 'local.json'
            for value in (None, 'not a number', -1, float('nan')):
                local = {k:'fixture' for k in gate.LOCAL_KEYS}
                if value is None:local.pop('flyBudgetLeftUsd',None)
                else:local['flyBudgetLeftUsd']=value
                path.write_text(json.dumps(local))
                with patch.dict(os.environ,CHRYSALIS_GATE_LOCAL=str(path)), self.assertRaisesRegex(ValueError,'flyBudgetLeftUsd'):
                    gate.local_config()

    def test_declared_time_counts_and_controls_budget_are_preserved(self):
        import gate_time
        config = json.loads((gate.GATE / 'gated.json').read_text())
        audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / 'plan.json'
            controls=[dict(id='time/control-'+name,kind='time',replay=case,aPairs=a,pairs=b,chargeSeconds=charge)
                      for name,case,a,b,charge in [('c2','offline',16,6,16),('c3','offline',16,16,13),('c5','offline',16,24,13),('c4','tcp',5,2,22)]]
            path.write_text(json.dumps(controls))
            for mode in ('change','release'):
                selected=gate.select_rows(SimpleNamespace(mode=mode,plan=path,only=None),config,audit)
                for r in selected:r['corpusBytes']=10_000_000
                plans=gate_time.size_sessions(selected,mode,budget=.95)
                self.assertEqual([(r['aPairs'],r['bPairs']) for p in plans for r in p['rows']],[(16,6),(16,16),(16,24),(5,2)])
                self.assertEqual(plans[0]['rows'][0]['chargeSeconds'],16)
                c4=plans[0]['rows'][-1]
                self.assertEqual(gate_time.verdict(c4,[],mode)['status'],'INCONCLUSIVE')
            with self.assertRaisesRegex(ValueError,'budget'):
                gate_time.size_sessions(selected,'release',budget=.65)
            validation=[dict(id='time/preprod-'+case,kind='time',replay=case,aPairs=a,pairs=b) for case,a,b in [('tcp',32,32),('offline',24,36)]]
            path.write_text(json.dumps(validation))
            selected=gate.select_rows(SimpleNamespace(mode='release',plan=path,only=None),config,audit)
            self.assertEqual([(r['aPairs'],r['bPairs']) for p in gate_time.size_sessions(selected,'release',budget=2) for r in p['rows']],[(32,32),(24,36)])

    def test_band_warmup_provenance_cannot_drift_with_the_label(self):
        source=next(p for p in (gate.GATE/'bands').glob('*.json') if json.loads(p.read_text()).get('window')=='readeveryfield/warm10')
        band=json.loads(source.read_text())
        row=dict(id=band['row'],kind='direct',window=band['window'],corpusId=band['corpusId'],warmupMilliseconds=2000)
        with self.assertRaisesRegex(ValueError,'warm-up'):
            gate.load_band(row,band['runtime'],'direct')

    def test_canonical_sides_and_cold_companion(self):
        baseline = side('10.0.9', gate.PIN)
        candidate = side()
        self.assertTrue(gate.canonical(baseline, candidate))
        self.assertEqual(gate.cold_companion(baseline, candidate)['runtime'], '11.0.0')
        self.assertEqual(gate.cold_companion(candidate, candidate), candidate)
        self.assertEqual(gate.cold_companion(baseline, baseline)['runtime'], '10.0.9')
        self.assertFalse(gate.canonical(dict(baseline, runtime='11.0.0'), candidate))
        self.assertFalse(gate.canonical(side(), candidate))
        self.assertTrue(gate.canonical(dict(side(), previousAlpha=True), candidate))

    def test_band_refuses_candidate_and_short_counts(self):
        baseline = side()
        pairs = [pair(number) for number in range(1, 11)]
        row = dict(id='Example', kind='direct', rule='range', warmupMilliseconds=10000)
        for item in pairs:
            for role in ('baseline','candidate'):item[role]['data'][0]['warmupMilliseconds']=10000
        record = gate.freeze_band(row, pairs, baseline, design='direct', seal_id='seal', raw_hash='raw')
        self.assertEqual(record['pairs'], 10)
        self.assertEqual(record.get('warmupMilliseconds'),10000)
        for bad in (pairs[:5], copy.deepcopy(pairs)):
            if len(bad) == 10:
                bad[-1]['candidate']['identity']['treeId'] = 'other'
            with self.assertRaisesRegex(ValueError, 'baseline-only'):
                gate.freeze_band(row, bad, baseline, design='direct', seal_id='seal', raw_hash='raw')
        self.assertNotIn(str(Path.home()), json.dumps(record))

    def test_band_provenance_wrong_runtime_is_refused(self):
        row = dict(id='Example', kind='direct', rule='range')
        record = gate.freeze_band(row, [pair(n) for n in range(10)], side(), design='direct', seal_id='s', raw_hash='r')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'bands').mkdir()
            record['runtime'] = '10.0.9'  # Key still claims runtime 11.
            gate.session.save(root / 'bands/bad.json', record)
            with patch.object(gate, 'GATE', root), self.assertRaisesRegex(ValueError, 'provenance'):
                gate.load_band(row, '11.0.0', 'direct')

    def test_catalogue_audit_is_one_source_and_note_headings_match(self):
        config = json.loads((gate.GATE / 'gated.json').read_text())
        audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
        rows = gate.catalogue(config, audit)
        direct = {row['id']: row['rule'] for row in rows if row['kind'] == 'direct'}
        self.assertEqual(len(direct), 53)
        self.assertEqual(sum(rule == 'range' for rule in direct.values()), 7)
        self.assertEqual(direct['FieldReadBench.ReadEveryField'], 'range')
        headings = {line[3:] for line in (gate.REPO / 'benchmarks/HOT-PATHS.md').read_text().splitlines() if line.startswith('## ')}
        self.assertEqual(headings, set(config['areas']))

    def test_full_subset_missing_change_and_precedence(self):
        seal = dict(mode='release', canonical=True, timeScopeDecided=True,
                    releaseSet=['one', 'two'], rows=[dict(id='one'), dict(id='two')])
        passing = dict(one=dict(status='PASS'), two=dict(status='PASS'))
        self.assertEqual(gate.outcome(seal, passing), 'RELEASE PASS')
        self.assertEqual(gate.outcome(dict(seal, rows=[dict(id='one')]), passing), 'SUBSET')
        self.assertEqual(gate.outcome(seal, dict(one=dict(status='PASS'))), 'INCONCLUSIVE')
        self.assertEqual(gate.outcome(dict(seal, mode='change'), passing), 'SUBSET')
        self.assertEqual(gate.outcome(seal, passing, mode='change'), 'SUBSET')
        self.assertEqual(gate.outcome(dict(seal, canonical=False), dict(one=dict(status='FAIL'))), 'FAIL')
        self.assertEqual(gate.outcome(dict(seal, timeScopeDecided=False), passing), 'SUBSET')

    def test_report_is_pure_and_mode_cannot_upgrade(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture(root)
            original = gate.report(root)
            self.assertEqual(original['outcome'], 'RELEASE PASS')
            with patch.object(gate, 'GATE', root / 'missing-repository'), patch.object(gate, 'local_config', side_effect=AssertionError('Not pure')):
                self.assertEqual(gate.report(root), original)
            self.assertEqual(gate.report(root, 'change')['outcome'], 'SUBSET')
            self.assertIn('not the sealed rule', gate.report(root, 'change')['text'])
            sealed = json.loads((root / 'seal.json').read_text())
            sealed['ruleVersion'] = 'unimplemented-rule'
            gate.write_seal(root, sealed)
            with self.assertRaisesRegex(ValueError, 'Unsupported sealed rule'):
                gate.report(root)

    def test_seal_and_raw_tampering_never_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture(root)
            path = root / 'data/first.json'
            path.write_text(path.read_text() + ' ')
            with self.assertRaisesRegex(ValueError, 'Raw data changed'):
                gate.report(root)
            seal = fixture(root)
            script = root / 'input/session.py'
            script.parent.mkdir()
            script.write_text('original')
            seal['files'] = {'input/session.py': gate.digest(script)}
            gate.write_seal(root, seal)
            script.write_text('changed')
            with self.assertRaisesRegex(ValueError, 'Sealed input'):
                gate.report(root)
            (root / 'seal.json').write_text((root / 'seal.json').read_text() + ' ')
            with self.assertRaisesRegex(ValueError, 'Seal changed'):
                gate.report(root)

    def test_report_declared_missing_row_is_inconclusive(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            seal = fixture(root)
            seal['rows'].append(dict(id='missing', kind='direct', rule='D39', pairs=5, design='direct', sides=dict(baseline=side(), candidate=side())))
            gate.write_seal(root, seal)
            self.assertEqual(gate.report(root)['outcome'], 'INCONCLUSIVE')

    def test_release_repeat_joint_report_once(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            seal = fixture(root)
            row = seal['rows'][0]
            row['rule'] = 'range'
            band = dict(key='band', absoluteNoiseBandBytes=2)
            seal['bands']['band'] = dict(record=band, contentSha256=__import__('hashlib').sha256(gate.encoded(band)).hexdigest())
            row['bandKey'] = 'band'
            gate.write_seal(root, seal)
            index = []
            for phase, value in [('first', 12), ('repeat', 8)]:
                pairs = [pair(number) for number in range(1, 6)]
                for entry in pairs:
                    entry['candidate']['data'][0]['samples'] = [dict(bytes=value)] * 5
                path = root / f'data/{phase}.json'
                gate.session.save(path, pairs)
                index.append(dict(ids=['Example'], phase=phase, path=path.relative_to(root).as_posix(), sha256=gate.digest(path)))
            gate.session.save(root / 'data/index.json', index)
            result = gate.report(root)
            self.assertEqual(result['rows']['Example']['first']['status'], 'FAIL')
            self.assertEqual(result['outcome'], 'RELEASE PASS')
            self.assertIn('first FAIL; repeat PASS; joint PASS', result['text'])
            index.append(index[-1])
            gate.session.save(root / 'data/index.json', index)
            with self.assertRaisesRegex(ValueError, 'Ambiguous'):
                gate.report(root)

    def test_entry_cannot_loosen_an_audit_class(self):
        config = json.loads((gate.GATE / 'gated.json').read_text())
        audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'entries.json'
            args = SimpleNamespace(mode='change', plan=path, only=None)
            for entry in [dict(id='CoreBench.Sign', kind='replay', replay='tcp'),
                          dict(id='CoreBench.PrimitiveIntRead', kind='direct', rule='range')]:
                path.write_text(json.dumps([entry]))
                with self.assertRaises(ValueError):
                    gate.select_rows(args, config, audit)
            path.write_text(json.dumps([dict(id='CoreBench.PrimitiveIntRead', kind='direct', pairs=2)]))
            self.assertEqual(gate.select_rows(args, config, audit)[0]['rule'], 'D39')

    def test_captured_script_closure_imports_without_source_checkout(self):
        import subprocess
        import sys
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            scripts = gate.snapshot_scripts(root)
            code = f"import sys; sys.path.insert(0, {str(scripts)!r}); import gate_session, n2n_replay_server"
            result = subprocess.run([sys.executable, '-I', '-c', code], cwd=root,
                                    env=gate.session.environment({}), capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_pinned_executables_have_explicit_metadata_not_project_dependencies(self):
        import xml.etree.ElementTree as xml
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / 'client.csproj'
            project.write_text('<Project><ItemGroup><ProjectReference Include="product" /><PackageReference Include="loose" /></ItemGroup></Project>')
            gate.pinned_references(project, {'Chrysalis.Codec.dll': Path('pinned/Chrysalis.Codec.dll'),
                                             'Chrysalis.Benchmarks.dll': Path('built/Chrysalis.Benchmarks.dll')})
            root = xml.fromstring(project.read_text())
            self.assertEqual(root.findall('.//ProjectReference'), [])
            self.assertEqual(root.findall('.//PackageReference'), [])
            self.assertEqual([item.findtext('HintPath') for item in root.findall('.//Reference')],
                             ['pinned/Chrysalis.Codec.dll', 'built/Chrysalis.Benchmarks.dll'])

    def test_pc_time_and_fly_bytes_cannot_gate(self):
        row = dict(kind='time')
        self.assertEqual(gate.row_result(row, [], 'release', None, 'long')['status'], 'INCONCLUSIVE')
        raw = pair(1)
        raw['candidate']['platform'] = 'fly'
        with self.assertRaisesRegex(ValueError, 'PC data'):
            gate.row_result(dict(kind='direct'), [raw], 'release', None, 'direct')


class ReviewGateTests(unittest.TestCase):
    def test_cold_is_first_actual_pass_and_band_uses_same_window(self):
        from test_gate_session import data
        row = dict(id='replay/offline/cold', kind='replay', rule='replay', replay='offline', window='cold')
        pairs = [pair(n) for n in range(1, 21)]
        for item in pairs:
            for member in ('baseline', 'candidate'):
                item[member]['data'] = data(True)
                item[member]['data']['warmupRows'][0]['bytesPerBlock'] = 10 if member == 'baseline' else 58
                for measured in item[member]['data']['rows']:
                    measured['bytesPerBlock'] = 10
        for mode in ('change', 'release'):
            self.assertEqual(gate.row_result(row, pairs, mode, dict(record=dict(absoluteNoiseBandBytes=0)), 'short')['status'], 'FAIL')
        record = gate.freeze_band(row, pairs[:10], side(), design='short', seal_id='s', raw_hash='r')
        self.assertEqual(record['absoluteNoiseBandBytes'], 48)
        self.assertNotEqual(record['key'], 'offline|short|cold|11.0.0|audit-preprod')

    def test_short_and_long_windows_keep_their_exact_passes(self):
        from test_gate_session import data
        row = dict(id='replay/offline/steady', kind='replay', replay='offline', window='steady')
        for design, short in (('short', True), ('long', False)):
            item = pair(1)
            for member in ('baseline', 'candidate'):
                item[member]['data'] = data(short)
                for index, measured in enumerate(item[member]['data']['rows']):
                    measured['bytesPerBlock'] = 100 if (index < 15 if short else index < 20) else 10
            left, right, _ = gate.pair_values(row, [item], design)
            self.assertEqual(left, [10 if short else 40])
            self.assertEqual(right, left)

    def test_previous_alpha_is_a_rising_version_proper_ancestor_not_candidate_or_rc(self):
        tags = 'v2.2.0-alpha\nv2.1.0-alpha\nv2.0.2-alpha-rc1\nv2.0.1-alpha\nv2.0.0-alpha\nv2.3.0\nv9.0.0-alpha\n'
        commits = {'v2.2.0-alpha': 'candidate', 'v2.1.0-alpha': 'minor', 'v2.0.1-alpha': 'patch', 'v2.0.0-alpha': 'first', 'v9.0.0-alpha': 'unrelated'}
        def query(*args, **kwargs):
            if args[0] == 'tag':
                return tags
            return commits[args[-1].removesuffix('^{commit}')]
        for available, expected in [(('minor', 'patch', 'first'), 'minor'), (('patch', 'first'), 'patch'), (('first',), 'first')]:
            with patch.object(gate, 'git', side_effect=query), patch.object(gate.subprocess, 'call', side_effect=lambda command, **kw: 0 if command[-2] in available else 1):
                self.assertEqual(gate.previous_alpha('candidate'), expected)

    def test_duplicate_band_keys_name_both_files(self):
        row = dict(id='Example', kind='direct', rule='range')
        record = gate.freeze_band(row, [pair(n) for n in range(10)], side(), design='direct', seal_id='s', raw_hash='r')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ('first.json', 'second.json'):
                gate.session.save(root / 'bands' / name, record)
            with patch.object(gate, 'GATE', root), self.assertRaisesRegex(ValueError, 'first.json.*second.json'):
                gate.load_band(row, '11.0.0', 'direct')

    def test_existing_band_preflights_entire_selection_before_staging_or_heavy(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            config = dict(direct={'Example': 'range', 'Other': 'range'}, replays=[], time={'required': []})
            gate.session.save(root / 'gated.json', config)
            with patch.object(gate, 'GATE', root), patch.object(gate, 'local_config', return_value={}), \
                    patch.object(gate, 'resolve_side', return_value=side()), patch.object(gate, 'corpus_identity', return_value='corpus'), \
                    patch.object(gate, 'load_band', side_effect=[None, dict(record={})]), \
                    patch.object(gate, 'stage_side') as stage, patch.object(gate, 'heavy') as heavy:
                args = SimpleNamespace(command='band', side='base', set='Example,Other')
                with self.assertRaisesRegex(ValueError, 'frozen band'):
                    gate.pc_command(args)
                stage.assert_not_called()
                heavy.assert_not_called()

    def test_subset_outcome_explains_each_reason(self):
        for field, value, text in [('mode', 'change', 'change mode'), ('canonical', False, 'non-canonical sides'),
                                   ('timeScopeDecided', False, 'time scope undecided'), ('releaseSet', ['Example', 'Missing'], '1 of 2 release rows declared')]:
            with tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                seal = fixture(root)
                seal[field] = value
                gate.write_seal(root, seal)
                result = gate.report(root)
                self.assertEqual(result['outcome'], 'SUBSET')
                self.assertIn(text, result['text'].splitlines()[-1])
        seal = dict(mode='release', canonical=False, timeScopeDecided=True, releaseSet=['one'], rows=[dict(id='one')])
        self.assertEqual(gate.outcome(seal, {'one': dict(status='PASS')}), 'SUBSET')

    def test_repeat_alone_pass_cannot_replace_failing_pooled_report(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            seal = fixture(root)
            seal['rows'][0]['rule'] = 'range'
            band = dict(key='band', absoluteNoiseBandBytes=10)
            seal['bands']['band'] = dict(record=band, contentSha256=__import__('hashlib').sha256(gate.encoded(band)).hexdigest())
            seal['rows'][0]['bandKey'] = 'band'
            gate.write_seal(root, seal)
            index = []
            for phase, value in [('first', 110), ('repeat', 10)]:
                pairs = [pair(n) for n in range(1, 6)]
                for item in pairs:
                    item['candidate']['data'][0]['samples'] = [dict(bytes=value)] * 5
                path = root / f'data/{phase}.json'
                gate.session.save(path, pairs)
                index.append(dict(ids=['Example'], phase=phase, path=path.relative_to(root).as_posix(), sha256=gate.digest(path)))
            gate.session.save(root / 'data/index.json', index)
            result = gate.report(root)
            self.assertEqual(result['rows']['Example']['repeat']['status'], 'PASS')
            self.assertEqual(result['rows']['Example']['pairs'], 10)
            self.assertEqual(result['outcome'], 'FAIL')

    def command_run(self, root, rows, *, noisy=False):
        calls = []
        def execute(job, destination):
            calls.append(copy.deepcopy(job))
            pairs = []
            for number in range(1, job['pairs'] + 1):
                item = dict(pair=number)
                for member in ('baseline', 'candidate'):
                    samples = []
                    for name in job['filter'].split(','):
                        values = [288] * 5
                        if noisy and name == 'Example' and number == (1 if member == 'baseline' else 2):
                            values[0] += .1
                        samples.append(dict(name=name, samples=[dict(bytes=x) for x in values]))
                    info = job['sides'][member]
                    item[member] = dict(data=samples, platform='pc', load=[0, 0],
                                        measurement=dict(kind=job['kind'], counts=job['counts'], filter=job['filter']),
                                        identity={key: info[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')})
                pairs.append(item)
            gate.session.save(destination / 'pairs.json', pairs)
            return pairs
        scripts = root / 'input/scripts'
        scripts.mkdir(parents=True)
        staged = {value: dict(side(commit=value), dllSha256s={'BenchmarkDotNet.dll': 'bdn'}, harness='harness', client='client') for value in ('base', 'candidate')}
        plan = root / 'entries.json'
        gate.session.save(plan, [{key: row[key] for key in ('id', 'kind', 'pairs')} for row in rows])
        args = SimpleNamespace(mode='change', baseline='base', candidate='candidate', only=None, plan=plan)
        with patch.object(gate, 'local_config', return_value=dict(corporaRoot=str(root))), \
                patch.object(gate, 'corpus_identity', return_value='corpus'), \
                patch.object(gate, 'new_output', return_value=root), patch.object(gate, 'snapshot_scripts', return_value=scripts), \
                patch.object(gate, 'resolve_side', side_effect=lambda value, mode: staged[value]), \
                patch.object(gate, 'measurement_inputs', return_value={}), patch.object(gate, 'stage_side', side_effect=lambda info, *args: info), \
                patch.object(gate, 'git', return_value=''), patch.object(gate, 'load_band', return_value=None), \
                patch.object(gate, 'execute_job', side_effect=execute), patch.object(gate, 'heavy') as heavy:
            gate.run_command(args)
            heavy.assert_not_called()  # the public dispatcher owns the single held slot
        return calls, gate.report(root)

    def test_noise_phase_is_lazy_baseline_only_same_harness_counts_and_filter(self):
        rows = [dict(id=name, kind='direct', rule='D39', design='direct', pairs=5) for name in ('Example', 'Stable')]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            calls, result = self.command_run(root, rows, noisy=True)
            self.assertEqual(gate.read_seal(root)['gated'], json.loads((gate.GATE / 'gated.json').read_text()))
            self.assertEqual(len(calls), 2)
            first, aa = calls
            self.assertEqual(first['filter'], 'Example,Stable')
            self.assertEqual(aa['filter'], first['filter'])
            self.assertEqual(aa['counts'], first['counts'])
            self.assertEqual(aa['pairs'], first['pairs'])
            self.assertEqual(aa['sides']['baseline'], aa['sides']['candidate'])
            self.assertEqual(aa['sides']['baseline'], first['sides']['baseline'])
            index = json.loads((root / 'data/index.json').read_text())
            control = next(item for item in index if item['phase'] == 'noise')
            self.assertEqual(control['ids'], ['Example'])
            self.assertEqual(result['rows']['Example']['reason'], 'equal (noise)')
            self.assertIn('A/A', result['text'])
            with patch.object(gate, 'GATE', root / 'absent'):
                self.assertEqual(gate.report(root), result)
            raw = root / control['path']
            original = json.loads(raw.read_text())
            for flaw in ('identity', 'counts', 'filter', 'pair count'):
                altered = copy.deepcopy(original)
                if flaw == 'identity':
                    altered[0]['candidate']['identity']['dllSha256s'] = {'harness': 'wrong'}
                elif flaw == 'pair count':
                    altered.pop()
                else:
                    altered[0]['candidate']['measurement'][flaw] = 'wrong'
                gate.session.save(raw, altered)
                control['sha256'] = gate.digest(raw)
                gate.session.save(root / 'data/index.json', index)
                with self.subTest(flaw=flaw), self.assertRaisesRegex(ValueError, 'identity|count|harness|composition|design'):
                    gate.report(root)
        with tempfile.TemporaryDirectory() as directory:
            calls, result = self.command_run(Path(directory), rows, noisy=False)
            self.assertEqual(len(calls), 1)

    def test_range_filters_match_frozen_composition_even_for_subsets(self):
        config = json.loads((gate.GATE / 'gated.json').read_text())
        audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
        catalogue = {row['id']: row for row in gate.catalogue(config, audit)}
        names = ['CoreBench.PrimitiveIntRead', 'CoreBench.Sign', 'FieldReadBench.ReadEveryField']
        rows = [dict(catalogue[name], pairs=3, design='direct') for name in names]
        frozen = json.loads((Path(__file__).with_name('fixtures') / 'gate-direct-composition.json').read_text())
        expected = frozen['processFilter']
        self.assertEqual(expected, ','.join(name for name, row in audit['rows'].items() if row['kind'] == 'range'))
        with tempfile.TemporaryDirectory() as directory:
            calls, _ = self.command_run(Path(directory), rows)
            self.assertEqual([job['filter'] for job in calls], [names[0], expected, names[2]])
            self.assertEqual(catalogue[names[2]]['rule'], 'range')

    def test_public_pc_dispatch_holds_one_slot_for_all_children(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'report.txt').write_text('mock held worker completed')
            args = SimpleNamespace(command='run', mode='change', baseline='base', candidate='candidate', only='Example', plan=None)
            with patch.object(gate, 'new_output', return_value=root), patch.object(gate, 'local_config', return_value={}), \
                    patch.object(gate, 'heavy') as heavy:
                gate.pc_command(args)
                self.assertEqual(heavy.call_count, 1)
                command = heavy.call_args.args[0]
                self.assertIn('_held', command)
            with patch.object(gate.subprocess, 'call', return_value=0) as call:
                gate.execute_logged(['child'], root / 'child.log', cwd=root, env={})
                self.assertNotIn('heavy', call.call_args.args[0])


if __name__ == '__main__':
    unittest.main()
