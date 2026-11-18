"""D68 schedule only: no recentering, block inference or new validity veto."""
import copy
import importlib.util
import math
import hashlib
import json
import os
import tarfile
import tempfile
from unittest.mock import patch
from pathlib import Path
import unittest

import gate_bundle as bundle
import gate_time as timing
from test_gate_time import row, pairs, member, probe_rule


def plan():
    result = timing.size_sessions([row('tcp'), row()], 'release', budget=40)[0]
    for item in result['rows']:
        item.update(aPairs=16, bPairs=16)
        for spec in item['sides'].values():
            spec.update(commit='0' * 40, treeId='1' * 40)
    return result


class ScheduleTests(unittest.TestCase):
    def api(self):
        self.assertIsNotNone(importlib.util.find_spec('gate_schedule'), 'D68 sealed scheduler is missing')
        return importlib.import_module('gate_schedule')

    def synthetic(self, prepared, *, slot=1, identity=1):
        api = self.api()
        records = []
        for block in prepared['schedule']:
            roles = {value: name for pair in block['pairs'] for name, value in pair['members'].items()}
            for position, identifier in enumerate(block['members']):
                record = member((slot if position % 2 else 1) * (identity if roles[identifier] == 'candidate' else 1))
                r = next(item for item in prepared['rows'] if item['id'] == block['row'])
                role = 'baseline' if block['phase'] == 'A' else roles[identifier]
                expected = r['sides'][role]
                record['identity'] = {key: expected[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')}
                record['data']['runtime'] = expected['runtime']
                for stage in ('warmupRows', 'rows'):
                    for observation in record['data'][stage]:
                        observation['blocks'] = 1000 if r['replay'] == 'tcp' else 1020
                record.update(id=identifier, build=api.build_id(expected), predecessorBuild=None,
                              startedUtc='OBVIOUS TEST TIMESTAMP', endedUtc='OBVIOUS TEST TIMESTAMP')
                records.append(record)
        return api.restore_pairs(prepared, records)

    def test_seed_reproduces_local_blocks_and_balanced_phase_counts(self):
        api = self.api()
        original = plan()
        first = api.prepare(copy.deepcopy(original))
        self.assertEqual(first, api.prepare(copy.deepcopy(original)))
        self.assertEqual(api.make_schedule(original, 1), api.make_schedule(original, 1))
        self.assertNotEqual(api.make_schedule(original, 1), api.make_schedule(original, 2))
        for seed in (1, 2, 3, 99):
            schedule = api.make_schedule(original, seed)
            phases = [block['phase'] for block in schedule]
            self.assertEqual(phases, sorted(phases))  # homogeneous A prefix
            for phase in ('A', 'B'):
                for r in original['rows']:
                    blocks = [block for block in schedule if block['phase'] == phase and block['row'] == r['id']]
                    self.assertEqual(sum(len(block['pairs']) for block in blocks), r['aPairs' if phase == 'A' else 'bPairs'])
                    first_orders = [block['members'][0] == block['pairs'][0]['members']['baseline'] for block in blocks]
                    self.assertLessEqual(abs(sum(first_orders) - (len(first_orders) - sum(first_orders))), 1)
                    for block in blocks:
                        self.assertEqual(len(block['members']), 4)
                        orders = [''.join('A' if identifier == pair['members']['baseline'] else 'B' for identifier in block['members'] if identifier in pair['members'].values()) for pair in block['pairs']]
                        self.assertEqual(set(orders), {'AB', 'BA'})

    def test_stable_shuffle_golden_rejection_and_orientation_mapping(self):
        api = self.api()
        values = list(range(10))
        api.Shuffle(0).shuffle(values)
        self.assertEqual(values, [1, 7, 4, 5, 8, 0, 3, 6, 2, 9])
        # Rejected draws still consume a counter; the zero draw is accepted.
        with patch.object(api.hashlib, 'sha256') as digest:
            digest.return_value.digest.side_effect = [b'\xff' * 32, b'\x00' * 32]
            rng = api.Shuffle(0)
            self.assertEqual(rng.below(3), 0)
            self.assertEqual(rng.counter, 2)
            self.assertTrue(digest.call_args_list[0].args[0].endswith(b'\x00' * 16))
            self.assertTrue(digest.call_args_list[1].args[0].endswith(b'\x00' * 15 + b'\x01'))
        # Every extra/tail orientation maps0 ->ABBA/AB and1 ->BAAB/BA.
        for bit, first in ((0, 'baseline'), (1, 'candidate')):
            with patch.object(api.Shuffle, 'below', return_value=bit):
                blocks = api.make_schedule(dict(rows=[dict(id='x', aPairs=3, bPairs=0)]), 0)
            for block in blocks:
                self.assertEqual(block['members'][0], block['pairs'][0]['members'][first])

    def test_super_rounds_and_canonical_golden_schedule(self):
        api = self.api()
        core = dict(rows=[dict(id='x', aPairs=5, bPairs=2), dict(id='y', aPairs=0, bPairs=6)],
                    repeatAllowed=True, inputSha256={'fixture': 'a' * 64})
        prepared = api.prepare(core)
        golden = json.loads((Path(__file__).parent / 'fixtures/timing-schedule.json').read_text())
        self.assertEqual(prepared['scheduleCoreSha256'], golden['coreSha256'])
        self.assertEqual(prepared['schedule'], golden['schedule'])
        self.assertEqual(api.make_schedule(dict(rows=list(reversed(core['rows']))), prepared['scheduleSeed']), prepared['schedule'])
        self.assertEqual(hashlib.sha256(prepared['scheduleCoreJson'].encode('utf-8')).hexdigest(), golden['coreSha256'])
        self.assertEqual(api.encoded(dict(z=1, a=2)), b'{"a":2,"z":1}')
        b = [block for block in prepared['schedule'] if block['phase'] == 'B']
        self.assertEqual([block['round'] for block in b if block['row'] == 'x'], [2])
        self.assertEqual([block['round'] for block in b if block['row'] == 'y'], [1, 2, 3])
        for field in ('inputSha256', 'rows'):
            changed = copy.deepcopy(prepared)
            changed[field] = {}
            with self.assertRaises((ValueError, KeyError, TypeError)):
                api.verify(changed)

    def test_probe_reference_survives_baab_and_filtered_repeat(self):
        api = self.api()
        core = plan()
        tcp, offline = core['rows']
        tcp['aPairs'] = offline['aPairs'] = 0
        tcp['bPairs'] = offline['bPairs'] = 2
        tcp['sides']['candidate']['commit'] = 'c' * 40
        offline['sides']['baseline']['commit'] = 'd' * 40
        core.update(probe=probe_rule(), probeJob=dict(kind='time', replay='offline'))
        with patch.object(api.Shuffle, 'below', return_value=1), patch.object(api.Shuffle, 'shuffle', side_effect=list.reverse):
            api.prepare(core)
            worker = api.worker_plan(core)
            repeat = api.worker_plan(core, repeat=True, selected=[offline['id']])
        self.assertEqual(worker['members'][worker['blocks'][0]['members'][0]]['build'], api.build_id(tcp['sides']['candidate']))
        self.assertEqual(worker['probe']['build'], api.build_id(tcp['sides']['baseline']))
        self.assertEqual(worker['probe'], repeat['probe'])
        self.assertEqual(repeat['workloads'][repeat['probe']['workload']]['replay'], 'offline')
        self.assertIn(repeat['probe']['build'], repeat['builds'])
        self.assertNotIn('baseline', worker['probe'])
        self.assertNotIn('candidate', worker['probe'])

    def worker_schedule(self):
        api = self.api()
        p = plan()
        for r in p['rows']:
            r.update(aPairs=0, bPairs=3, chargeSeconds=10, job=dict(dotnet='dotnet', server='server', capture='capture', environment={}))
            r['sides']['candidate'].update(commit='2' * 40, treeId='3' * 40)
            for spec in r['sides'].values():
                spec.update(client='clients/' + api.build_id(spec) + '/ReplayClient.dll', harness='unused', dllSha256s={})
        return api.prepare(p)

    def test_actual_runner_has_neutral_paths_and_atomic_block_and_tail_cutoffs(self):
        api = self.api()
        worker = api.worker_plan(self.worker_schedule())
        fingerprint = dict(vendor_id='fixture CPU', flagsSha256='a' * 64)
        for count in (4, 2):
            block = next(b for b in worker['blocks'] if len(b['members']) == count)
            one = dict(worker, blocks=[block], measurementCutoffSeconds=100, region='ams')
            for remaining, expected in ((count * 11.5 - .01, 0), (count * 11.5, count)):
                with self.subTest(count=count, remaining=remaining), tempfile.TemporaryDirectory() as td:
                    root = Path(td)
                    calls = []
                    def fake(spec, job, dest, *, region=None):
                        self.assertEqual(region, 'ams')
                        calls.append((spec, job, dest))
                        if dest.parent == root / 'output':
                            host = dest.parent / 'host.json'
                            self.assertTrue(host.is_file(), 'Host fingerprint must exist before measuring')
                            self.assertEqual(json.loads(host.read_text()), fingerprint)
                        self.assertEqual(len(dest.name), 16)
                        self.assertEqual(Path(spec['client']).parent.name, api.build_id(spec))
                        return dict(member(), identity={key: spec[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')})
                    with patch.dict(os.environ, P2_MACHINE_CREATED_UNIX='1000'), \
                            patch.object(timing.time, 'time', return_value=1100 - remaining), \
                            patch.object(timing.session, 'host_fingerprint', return_value=fingerprint), \
                            patch.object(timing.session, 'member', side_effect=fake):
                        self.assertEqual(timing.run(root, one, root / 'output'), bool(expected))
                        self.assertEqual(len(calls), expected)
                        if count == 2 and expected:
                            self.assertTrue(timing.run(root, one, root / 'dry-output', dry=True))
                            self.assertFalse((root / 'dry-output/host.json').exists())
                    if calls:
                        raw = json.loads((root / 'output/members.json').read_text())
                        self.assertEqual([r['id'] for r in raw], block['members'])
                        self.assertEqual([r['predecessorBuild'] for r in raw], [None] + [r['build'] for r in raw[:-1]])
                        text = json.dumps(raw)
                        for semantic in ('baseline', 'candidate'):
                            self.assertNotIn(semantic, text)

    def test_optional_fingerprint_write_failure_keeps_session_and_explains_null_host(self):
        prepared = self.worker_schedule()
        worker = self.api().worker_plan(prepared)
        worker.update(blocks=worker['blocks'][:1], measurementCutoffSeconds=100)
        fingerprint = dict(vendor_id='fixture CPU', flagsSha256='a' * 64)
        save_json = timing.session.save

        def save(path, value):
            if Path(path).name == 'host.json':
                raise OSError('fixture: cannot write optional host file')
            save_json(path, value)

        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            output = root / 'results'
            with patch.dict(os.environ, P2_MACHINE_CREATED_UNIX='1000'), \
                    patch.object(timing.time, 'time', return_value=1001), \
                    patch.object(timing.session, 'host_fingerprint', return_value=fingerprint), \
                    patch.object(timing.session, 'member', side_effect=lambda *args: member()) as measure, \
                    patch.object(timing.session, 'save', side_effect=save):
                self.assertTrue(timing.run(root, worker, output))
            self.assertEqual(measure.call_count, len(worker['blocks'][0]['members']))
            self.assertFalse((output / 'host.json').exists())
            save_json(root / 'cleanup-proof.json', dict(ourAppAbsent=True))
            with tarfile.open(root / 'results.tar.gz', 'w:gz') as archive:
                archive.add(output, arcname='results')
            payload = bundle.archive_payload(root, prepared)
            self.assertIsNone(payload['invalid'])
            self.assertEqual(payload['host'], dict(dict.fromkeys(fingerprint), error='Fingerprint write failed'))

    def test_fixed_raw_inference_is_identical_with_schedule_information(self):
        api = self.api()
        r = row()
        for ratio in (1, 1.03, 1.07, [1.06, 1 / 1.06] * 8):
            raw = pairs(r, ratio)
            before = timing.verdict(r, raw, 'release')
            for number, pair in enumerate(raw):
                pair.update(block=str(number // 2), orientation='AB' if number % 2 else 'BA')
            api.block_diagnostics(raw)
            after = timing.verdict(r, raw, 'release')
            self.assertEqual(json.dumps(before, sort_keys=True), json.dumps(after, sort_keys=True))
        legacy = timing.verdict(r, pairs(r, [1.06, 1 / 1.06] * 8, a=[1.06, 1 / 1.06] * 8), 'release')
        self.assertAlmostEqual(legacy['confidence95'][0], .969612, places=6)
        self.assertAlmostEqual(legacy['confidence95'][1], 1.033784, places=6)

    def test_slot_negative_control_legacy_alternation_and_local_abba(self):
        api = self.api()
        r = row()
        # Hypothetical fixed-AB is a negative control, NOT the actual old runner.
        fixed = timing.verdict(r, pairs(r, 1.06, a=[1.06] * 16), 'release')
        self.assertEqual(fixed['status'], 'INCONCLUSIVE')
        legacy = pairs(r, [1.06, 1 / 1.06] * 8, a=[1.06, 1 / 1.06] * 8)
        legacy_result = timing.verdict(r, legacy, 'release')
        self.assertEqual(legacy_result['status'], 'PASS')
        self.assertAlmostEqual(legacy_result['mean'], 1.0016981132075473)
        prepared = api.prepare(plan())
        data = self.synthetic(prepared, slot=1.06)
        for r in prepared['rows']:
            selected = [pair for pair in data if pair['id'] == r['id']]
            result = timing.verdict(r, selected, 'release')
            self.assertEqual(result['status'], 'PASS', result)
            self.assertLess(abs(result['mean'] - 1), .002)  # arithmetic pair rule stays unchanged
            diagnostic = api.block_diagnostics(selected)
            self.assertAlmostEqual(diagnostic['B']['ratio'], 1)

    def test_persistent_identity_bias_and_real_slowdown_do_not_cancel(self):
        api = self.api()
        for seed in (1, 2):
            prepared = api.prepare(plan())
            prepared['schedule'] = api.make_schedule(prepared, seed)
            data = self.synthetic(prepared, identity=1.06)
            for r in prepared['rows']:
                selected = [pair for pair in data if pair['id'] == r['id']]
                result = timing.verdict(r, selected, 'release')
                self.assertEqual(result['status'], 'INCONCLUSIVE')
                self.assertIn('band', result['reason'])
                self.assertAlmostEqual(result['mean'], 1.06)
            # A is unbiased, B has a genuine build cost, regardless of orientation.
            for pair in data:
                if pair['phase'] == 'A':
                    pair['candidate']['data'] = copy.deepcopy(pair['baseline']['data'])
            for pair in data:
                if pair['phase'] == 'B':
                    for stage in ('warmupRows', 'rows'):
                        for observation in pair['candidate']['data'][stage]:
                            observation['nsPerBlock'] *= 1.07 / 1.06
            for r in prepared['rows']:
                result = timing.verdict(r, [p for p in data if p['id'] == r['id']], 'release')
                self.assertEqual(result['status'], 'FAIL', result)

    def test_neutral_worker_plan_and_legacy_odd_counts(self):
        api = self.api()
        p = plan()
        p['rows'][0].update(aPairs=5, bPairs=2)
        p['rows'][1].update(aPairs=0, bPairs=6)
        prepared = api.prepare(p)
        worker = api.worker_plan(prepared)
        text = __import__('json').dumps(worker)
        for word in ('baseline', 'candidate', 'left', 'right'):
            self.assertNotIn(word, text)
        identifiers = [value for block in worker['blocks'] for value in block['members']]
        self.assertEqual(len(identifiers), len(set(identifiers)))
        self.assertTrue(all(len(value) == 16 and set(value) <= set('0123456789abcdef') for value in identifiers))
        self.assertEqual(sum(len(b['members']) for b in worker['blocks']), 2 * (5 + 2 + 6))
        self.assertTrue(any(len(b['members']) == 2 for b in worker['blocks']))
        # C2's six pairs and C4's odd A count retain the existing pair-level outputs.
        c2 = dict(row(pairs=6), aPairs=0)
        before = timing.verdict(c2, pairs(c2), 'change', noise=c2['noise'])
        self.assertEqual(before['status'], 'PASS')
        self.assertEqual(before['pairs'], 6)
        self.assertEqual(timing.verdict(dict(row(pairs=2), aPairs=5), pairs(dict(row(pairs=2), aPairs=5)), 'release')['status'], 'INCONCLUSIVE')

    def test_asymmetric_carryover_keeps_the_half_sized_log_residual(self):
        api = self.api()
        prepared = api.prepare(plan())
        data = self.synthetic(prepared)
        by_id = {pair[side]['id']: pair[side] for pair in data for side in ('baseline', 'candidate')}
        for block in prepared['schedule']:
            if block['phase'] != 'B':
                continue
            roles = {value: role for pair in block['pairs'] for role, value in pair['members'].items()}
            previous = None
            for identifier in block['members']:
                role = roles[identifier]
                # Defined balanced-model control, not a claim about a real host.
                shift = .04 if role == 'candidate' else .02
                factor = math.exp(shift) if previous is not None and previous != role else 1
                for stage in ('warmupRows', 'rows'):
                    for observation in by_id[identifier]['data'][stage]:
                        observation['nsPerBlock'] *= factor
                previous = role
        for r in prepared['rows']:
            diagnostic = api.block_diagnostics([pair for pair in data if pair['id'] == r['id']])
            self.assertAlmostEqual(math.log(diagnostic['B']['ratio']), (.04 - .02) / 2)
            self.assertNotEqual(diagnostic['B']['ratio'], 1)

    def test_one_bad_pair_does_not_remove_its_clean_neighbour_or_change_c2(self):
        api = self.api()
        p = plan()
        for r in p['rows']:
            r.update(aPairs=0, bPairs=6)
        prepared = api.prepare(p)
        data = self.synthetic(prepared, identity=1.07)
        for r in prepared['rows']:
            selected = [pair for pair in data if pair['id'] == r['id']]
            selected[0]['baseline']['timingRejectedReasons'] = ['synthetic steal rejection']
            before = timing.verdict(r, selected, 'change', noise=r['noise'])
            diagnostic = api.block_diagnostics(selected)
            after = timing.verdict(r, selected, 'change', noise=r['noise'])
            self.assertEqual(before, after)
            self.assertEqual(after['status'], 'FAIL')
            self.assertEqual(after['pairs'], 5)
            self.assertEqual(diagnostic['B']['blocks'], 2)
            selected[1]['baseline']['timingRejectedReasons'] = ['synthetic steal rejection']
            self.assertEqual(timing.verdict(r, selected, 'change', noise=r['noise'])['status'], 'INCONCLUSIVE')

    def test_seed_core_and_repeat_subset_are_presealed(self):
        api = self.api()
        p = api.prepare(plan())
        self.assertEqual(p['scheduleSeed'], int(p['scheduleCoreSha256'][:16], 16))
        original = api.worker_plan(p, repeat=True)
        chosen = p['rows'][0]['id']
        subset = api.worker_plan(p, repeat=True, selected=[chosen])
        identifiers = {value for block in subset['blocks'] for value in block['members']}
        self.assertEqual(subset['blocks'], [block for block in original['blocks'] if set(block['members']) <= identifiers])
        for field in ('scheduleSeed', 'scheduleCoreSha256', 'scheduleCoreJson', 'repeatCoreJson', 'schedule', 'repeatSchedule'):
            changed = copy.deepcopy(p)
            changed[field] = 0
            with self.assertRaises(ValueError):
                api.verify(changed)
        changed = copy.deepcopy(p)
        changed['rows'][0]['bPairs'] += 1
        with self.assertRaises(ValueError):
            api.verify(changed)

    def test_startup_metric_reuses_process_counters_and_does_not_filter_anything(self):
        api = self.api()
        records = []
        for start, end, value, counts in [(0, 10, .001, [1, 25]), (100, 120, .002, [180, 60]), (399, 430, .006, [180, 60]), (400, 420, .9, [180, 60])]:
            item = member()
            item.update(wholeProcess=dict(startMonotonic=start, endMonotonic=end, aggregateSteal=value), measurement=dict(counts=counts))
            records.append(item)
        before = copy.deepcopy(records)
        result = api.startup_information(records)
        self.assertEqual(result['firstLong']['n'], 2)
        self.assertEqual(result['firstLong']['median'], .004)
        self.assertEqual(result['firstLong']['observationSpanSeconds'], 330)
        self.assertEqual(result['firstAllClients']['n'], 2)
        self.assertEqual(records, before)
        self.assertFalse(api.startup_information([])['firstLong']['available'])

    def test_stability_uses_arithmetic_twenty_pass_group_means(self):
        original = member()
        values = [1, 4] * 10
        for item, value in zip(original['data']['rows'][:20], values):
            item['nsPerBlock'] = value
        for item in original['data']['rows'][20:]:
            item['nsPerBlock'] = 2.5
        result = timing.stability([copy.deepcopy(original) for _ in range(6)])
        self.assertTrue(all(check['mean'] == 1 for check in result))
        self.assertNotEqual(math.sqrt(4), 2.5)  # geometric averaging of passes would differ


if __name__ == '__main__':
    unittest.main()
