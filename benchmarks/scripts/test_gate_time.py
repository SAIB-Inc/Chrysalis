"""D41, change mode, all designs, pooled repeats and pure report regressions."""
import copy
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import gate
import gate_bundle as bundle
import gate_time as timing
from test_gate_session import data
from test_gate import side


def row(case='offline', *, pairs=16, mainnet=False, size=10_000_000):
    return dict(id='time/' + ('mainnet-' if mainnet else 'preprod-') + case, kind='time', replay=case,
                pairs=pairs, aPairs=16, bPairs=pairs, rowClass='gated', corpusBytes=size, mainnet=mainnet,
                sides=dict(baseline=side(), candidate=side()),
                noise=dict(band=.0231, machine='performance-8x', design='long'))


def member(factor=1):
    d = data()
    d['runtime'] = '11.0.0'
    for key in ('rows', 'warmupRows'):
        for item in d[key]:
            item['nsPerBlock'] *= factor
    return dict(data=d, platform='fly', timingRejectedReasons=[], marks=['MEASURED_END', 'MEASURED_START'],
                identity={key: side()[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')})


def pairs(r=None, ratio=1, *, a=None):
    r = r or row()
    result = []
    for phase, count in (('A', r['aPairs']), ('B', r['bPairs'])):
        for number in range(1, count + 1):
            value = (a[number - 1] if a else 1) if phase == 'A' else ratio[number - 1] if isinstance(ratio, list) else ratio
            pair = dict(pair=number, id=r['id'], phase=phase, baseline=member(), candidate=member(value))
            for name in ('baseline', 'candidate'):
                identity = r['sides']['baseline' if phase == 'A' else name]
                pair[name]['identity'] = {key: identity[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')}
                pair[name]['data']['runtime'] = identity['runtime']
            result.append(pair)
    return result


class TimeTests(unittest.TestCase):
    def test_regional_reserve_and_limit(self):
        self.assertEqual(timing.price(3600, 'ams'), .3667 * 1.038461538 + .03)
        plan = timing.size_sessions([dict(row(), region='ams')], 'change', budget=1)[0]
        iad = timing.price(plan['destroyGuardSeconds'])
        self.assertGreater(plan['limitUsd'], iad)
        with self.assertRaisesRegex(ValueError, 'Priced lifetime'):
            timing.validate_limits(dict(plan, limitUsd=iad))

    def test_d41_rule_exact_edges_point_estimate_and_change(self):
        r = row()
        for ratio, release, change in [(1, 'PASS', 'PASS'), (1.02, 'FAIL', 'PASS'), (1.06, 'FAIL', 'FAIL')]:
            p = pairs(r, ratio)
            for mode, status in [('release', release), ('change', change)]:
                rr = r if mode == 'release' else dict(r, aPairs=0)
                actual = timing.verdict(rr, [item for item in p if item['phase'] == 'B' or mode == 'release'], mode, noise=r['noise'])
                self.assertEqual(actual['status'], status, actual)
                self.assertAlmostEqual(actual['mean'], ratio)
        self.assertEqual(timing.apply_rule(dict(rowClass='gated'), [.95, 1.05] * 8, 'release')['status'], 'PASS')
        self.assertEqual(timing.apply_rule(dict(rowClass='gated'), [.90, 1.15] * 8, 'release')['status'], 'INCONCLUSIVE')

    def test_b_only_change_needs_matching_noise_record(self):
        r = dict(row(), aPairs=0)
        p = pairs(r)
        self.assertEqual(timing.verdict(r, p, 'change', noise=r['noise'])['status'], 'PASS')
        for noise in [None, dict(r['noise'], band=.05), dict(r['noise'], design='short'), dict(r['noise'], machine='shared')]:
            self.assertEqual(timing.verdict(r, p, 'change', noise=noise)['status'], 'INCONCLUSIVE')
        self.assertEqual(timing.verdict(r, p, 'release')['status'], 'INCONCLUSIVE')

    def test_fail_only_and_invalid_never_pretend_pass(self):
        r = dict(row(), aPairs=0, rowClass='fail-only')
        for ratio, expected in [(1, 'CLEAR'), (1.01, 'FAIL')]:
            self.assertEqual(timing.verdict(r, pairs(r, ratio), 'release')['status'], expected)
        self.assertEqual(timing.verdict(r, pairs(r), 'release', invalid='session invalid: replacement')['status'], 'INCONCLUSIVE')

    def test_declared_counts_minimums_and_noise_band(self):
        r = row(pairs=8)
        r['aPairs'] = 8
        p = pairs(r)
        self.assertEqual(timing.verdict(r, p, 'release')['status'], 'PASS')
        for bad in [p[:-1], p + [p[-1]], [dict(pair, pair=1) for pair in p]]:
            self.assertEqual(timing.verdict(r, bad, 'release')['status'], 'INCONCLUSIVE')
        p = pairs(r, a=[.8, 1.2] * 4)
        self.assertIn('band', timing.verdict(r, p, 'release')['reason'])
        p = pairs(r)
        for pair in p:
            if pair['pair'] > 4:
                pair['candidate']['timingRejectedReasons'] = ['foreign CPU']
        self.assertIn('admission', timing.verdict(r, p, 'release')['reason'])

    def test_phase_populations_and_named_failures(self):
        r = row()
        p = pairs(r)
        for pair in p[:16]:
            for side in ('baseline', 'candidate'):
                for item in pair[side]['data']['rows'][40:]:
                    item['nsPerBlock'] *= .99 if pair['pair'] % 2 else 1.01
        actual = timing.verdict(r, p, 'release')['checks'][0]
        self.assertEqual(actual['quiescence']['processes'], 32)
        folded = actual['stability'][-1]
        flat = timing.stability([pair[side] for pair in p[:16] for side in ('baseline', 'candidate')])[-1]
        self.assertEqual(folded['pairs'], 16)
        self.assertAlmostEqual(folded['mean'], flat['mean'])
        self.assertGreater(folded['confidence95'][1], flat['confidence95'][1])
        for side, passes, factor in [('baseline', slice(40, 60), 1.1), ('candidate', slice(20, 40), 1.2)]:
            p = pairs(r)
            for pair in p[16:]:
                for item in pair[side]['data']['rows'][passes]:
                    item['nsPerBlock'] *= factor
            result = timing.verdict(r, p, 'release')
            self.assertEqual(result['status'], 'INCONCLUSIVE')
            self.assertEqual(list(result['qualificationFailures']), ['B ' + side + ' stability'])
            self.assertIn('mean', result)  # D49 always prints the descriptive B estimate
        p = pairs(r)
        p[-1]['candidate']['timingRejectedReasons'] = ['steal']
        p[-1]['candidate']['data']['groupBoundaries'][-1]['delta']['compilationTimeTicks'] = 1_000_000_000_000
        result = timing.verdict(r, p, 'release')
        self.assertEqual(list(result['qualificationFailures']), ['B candidate quiescence'])
        self.assertEqual(result['admittedPairs']['B'], 15)
        p = pairs(r)
        for pair in p:
            pair['candidate']['data']['groupBoundaries'][-1]['delta']['compiledMethods'] = 10
        self.assertEqual(timing.verdict(r, p, 'release')['status'], 'PASS')

    def test_every_failed_check_is_named_and_unqualified_rows_have_no_ratios(self):
        r = row()
        p = pairs(r, a=[1.06] * 16)
        for pair in p:
            for side in (('baseline', 'candidate') if pair['phase'] == 'A' else ('candidate',)):
                pair[side]['data']['groupBoundaries'][-1]['delta']['compilationTimeTicks'] = 4_000_000
                for item in pair[side]['data']['rows'][20:40]:
                    item['nsPerBlock'] *= 1.2
        with patch.object(timing, 'apply_rule', side_effect=AssertionError('unqualified result must not call apply_rule')):
            result = timing.verdict(r, p, 'release')
        self.assertEqual(result['status'], 'INCONCLUSIVE')
        self.assertEqual(set(result['qualificationFailures']), {'A band', 'A phase quiescence', 'A phase stability',
                                                                'B candidate quiescence', 'B candidate stability'})
        self.assertAlmostEqual(result['aBand'], .06)
        self.assertTrue(all(name in result['reason'] and detail in result['reason']
                            for name, detail in result['qualificationFailures'].items()))
        self.assertFalse({'ratios', 'pairedRatios'} & result.keys())
        self.assertIn('confidence95', result)

    def test_qualified_verdict_unchanged(self):
        r = row(pairs=8)
        p, q = pairs(r, [1.06, 1.07] * 4), pairs(r, [.96, .99] * 4)
        broken = q[:-1]
        for mode in ('change', 'release'):
            def read(dataset):
                return timing.verdict(r, dataset, mode, noise=r['noise'])
            first, repeat = read(p), read(q)
            ratios = [timing.pair_times([x for x in ds if x['phase'] == 'B'])['pairedRatios'] for ds in (p, q)]
            for actual, values in ((first, ratios[0]), (repeat, ratios[1]),
                                   (timing.pooled(first, repeat, mode), ratios[0] + ratios[1])):
                expected = timing.apply_rule(dict(rowClass='gated'), values, mode)
                self.assertEqual([actual[k] for k in ('ratios', 'confidence95', 'status')],
                                 [expected[k] for k in ('ratios', 'confidence95', 'status')])
            invalid = timing.pooled(first, read(broken), mode)
            self.assertEqual(invalid['status'], 'INCONCLUSIVE')
            self.assertNotIn('ratios', invalid)

    def test_controls_outcome(self):
        c2 = dict(row(pairs=6), id='c2', aPairs=6, expected=dict(release='FAIL', change='FAIL'))
        c3 = dict(c2, id='c3', expected=dict(release='PASS', change='PASS'))
        c4 = dict(c2, id='c4', bPairs=2, expected='smoke')
        c5 = dict(c2, id='c5-prime', expected=dict(release='FAIL'))
        miss = [1.06] * 6
        def both(r, ratio=1, *, a=None, invalid=None, edit=lambda p: None, repeat=None):
            p = pairs(r, ratio, a=a)
            edit(p)
            results = {mode: timing.verdict(r, p, mode, noise=r['noise'], invalid=invalid) for mode in ('release', 'change')}
            return results if repeat is None else {m: timing.pooled(first, repeat[m], m, sealed_first_status=results['release']['status'])
                                                  for m, first in results.items()}
        def classify(*entries, mode='release'):
            plan = dict(mode=mode, repeatAllowed=True, rows=[r for r, _ in entries])
            results = {m: {r['id']: result[m] for r, result in entries} for m in ('release', 'change')}
            return timing.controls_outcome(plan, results)[0]
        def bad_candidate(p):
            for pair in p[6:]:
                for item in pair['candidate']['data']['rows'][20:40]:
                    item['nsPerBlock'] *= 1.2
        isolated = both(c2, 1.08, a=miss)
        smoke = both(c4)
        bad = both(c2, 1.08, edit=bad_candidate)
        contradiction = both(c2, 1.02)
        with self.subTest(smoke='complete'):
            self.assertEqual(classify((c4, smoke), (c2, isolated)), 'retry-eligible')
        full_smoke = dict(c4, bPairs=6)
        for ratio in (1, 1.08):
            self.assertEqual(classify((full_smoke, both(full_smoke, ratio))), 'hard-stop')
        for change in (dict(status='PASS'), dict(reason='no noise record')):
            self.assertEqual(classify((c4, {m: dict(v, **change) for m, v in smoke.items()})), 'hard-stop')
        joint_smoke = {m: timing.pooled(v, v, m, sealed_first_status='FAIL') for m, v in smoke.items()}
        self.assertEqual(classify((c4, joint_smoke)), 'hard-stop')
        for edit in (lambda p: p[-1]['candidate'].update(marks=[]), lambda p: p[-1]['baseline'].update(identity={})):
            self.assertEqual(classify((c4, both(c4, edit=edit))), 'hard-stop')
        self.assertEqual(classify((c3, both(c3, [.9, 1.1] * 3))), 'hard-stop')
        self.assertEqual(classify((c3, both(c3, a=miss))), 'retry-eligible')
        for result in (contradiction, bad, both(c2, invalid='No sealed Fly session')):
            self.assertEqual(classify((c2, result)), 'hard-stop')
        self.assertEqual(classify((c2, contradiction), (c5, both(c5, a=miss))), 'hard-stop')
        for ratio, change in [(1.02, 'PASS'), ([1.03, 1.06] * 3, 'INCONCLUSIVE'), (1.08, 'FAIL')]:
            valid = both(c5, ratio)
            self.assertEqual(valid['change']['status'], change)
            self.assertEqual(classify((c5, valid)), 'repeat')
        no_band = dict(c5, noise=None)
        for a, edit, check in ((miss, lambda p: None, 'A band'), (None, bad_candidate, 'B candidate stability')):
            with self.subTest(check=check):
                results = both(no_band, 1.02, a=a, edit=edit)
                self.assertEqual(classify((no_band, results)), 'hard-stop')
                failures = results['change'].get('qualificationFailures', {})
                self.assertEqual(set(failures), {check, 'noise record'})
                self.assertTrue(all(f'{name} {detail}' in results['change']['reason'] for name, detail in failures.items()))
        for noise in ([], {}, dict(c5['noise'], band=[])):
            with self.subTest(noise=noise):
                malformed = dict(c5, noise=noise)
                results = both(malformed, 1.02)
                self.assertEqual(classify((malformed, results)), 'hard-stop')
                self.assertIn('noise record', results['change'].get('qualificationFailures', {}))
        rc = dict(no_band); del rc['expected']
        self.assertEqual(classify((rc, both(rc, 1.02))), 'repeat')
        self.assertEqual(classify((rc, both(rc, 1.02, a=miss))), 'retry-eligible')
        for mode in ('release', 'change'):
            for repeat, expected in ((isolated, 'retry-eligible'), (bad, 'hard-stop'), (both(c2, .88), 'hard-stop'),
                                     (both(c2, 1.049), 'complete')):
                self.assertEqual(classify((c2, both(c2, 1.08, repeat=repeat)), mode=mode), expected)
            self.assertEqual(classify((c2, both(c2, 1.08, repeat=isolated)),
                                      (c5, both(c5, 1.02, repeat=both(c5, .96))), mode=mode), 'hard-stop')

    def test_missing_marks_pc_fixed_work_and_side_identity_fail_closed(self):
        r = row()
        for alteration in ('marks', 'platform', 'runtime', 'counts', 'identity'):
            p = pairs(r)
            for pair in p:
                target = pair['candidate']
                if alteration == 'marks':
                    target['marks'] = []
                elif alteration == 'platform':
                    target['platform'] = 'pc'
                elif alteration == 'runtime':
                    target['data']['runtime'] = 'other'
                elif alteration == 'counts':
                    target['data']['rows'].pop()
                else:
                    target['identity']['dllSha256s'] = {'wrong.dll': 'wrong'}
            self.assertEqual(timing.verdict(r, p, 'release')['status'], 'INCONCLUSIVE', alteration)

    def test_one_repeat_pooled_not_replacement_invalid_holds(self):
        r = row()
        first = timing.verdict(r, pairs(r, 1.03), 'release')
        repeat = timing.verdict(r, pairs(r, .96), 'release')
        self.assertEqual(timing.pooled(first, repeat, 'release')['status'], 'PASS')
        repeat = timing.verdict(r, pairs(r, .99), 'release')
        self.assertEqual(repeat['status'], 'PASS')
        self.assertEqual(timing.pooled(first, repeat, 'release')['status'], 'FAIL')
        invalid = timing.verdict(r, [], 'release')
        self.assertEqual(timing.pooled(first, invalid, 'release')['status'], 'INCONCLUSIVE')
        self.assertIn('First invalid:', timing.pooled(invalid, repeat, 'change', sealed_first_status='FAIL')['reason'])
        with self.assertRaises(ValueError):
            timing.pooled(repeat, repeat, 'release')

    def test_valid_straddling_repeat_is_always_pooled(self):
        r = row()
        for first_values, repeat_values, status, low, high in [
                (1.01, [.90, 1.10] * 8, 'PASS', .97903347945, 1.03096652055),
                ([1., 1.06] * 8, [.94, 1.12] * 8, 'FAIL', 1.0054, 1.0546)]:
            first = timing.verdict(r, pairs(r, first_values), 'release')
            repeat = timing.verdict(r, pairs(r, repeat_values), 'release')
            self.assertEqual(first['status'], 'FAIL')
            self.assertEqual(repeat['status'], 'INCONCLUSIVE')
            actual = timing.pooled(first, repeat, 'release')
            self.assertEqual(actual['status'], status)
            self.assertAlmostEqual(actual['confidence95'][0], low, places=4)
            self.assertAlmostEqual(actual['confidence95'][1], high, places=4)

    def test_sizing_full_option2_mainnet_cost_and_one_change_command(self):
        rows = [row('tcp'), row(), row('tcp', mainnet=True), row(mainnet=True)]
        for r in rows:
            r.pop('aPairs');r.pop('bPairs')
        full = timing.size_sessions(rows, 'release', budget=40)
        self.assertEqual(len(full), 1)
        self.assertEqual(full[0]['rawSeconds'], 5304)
        self.assertTrue(all(r['aPairs'] == 16 and r['bPairs'] == 18 for r in full[0]['rows']))
        for size in (13_000_000, 20_000_000, 35_000_000):
            for r in rows:
                r['corpusBytes'] = size
            plans = timing.size_sessions(rows, 'release', budget=40)
            self.assertEqual(len(plans), 2)
            self.assertEqual({p['design'] for p in plans}, {'option2'})
            for plan in plans:
                self.assertTrue(all(r['aPairs'] % 2 == r['bPairs'] % 2 == 0 for r in plan['rows']))
                self.assertLessEqual(plan['destroyGuardSeconds'], 7000)
                self.assertEqual(plan['limitWithRepeatUsd'], 2 * plan['limitUsd'])
        change = timing.size_sessions(rows[:2], 'change', budget=40)[0]
        self.assertAlmostEqual(change['estimatedSeconds'] / 60, 35.3, delta=.2)
        self.assertAlmostEqual(change['limitUsd'], .25, delta=.01)
        self.assertEqual(change['limitWithRepeatUsd'], change['limitUsd'])
        with self.assertRaisesRegex(ValueError, 'budget'):
            timing.size_sessions(rows, 'release', budget=.01)
        with self.assertRaises(ValueError):
            timing.size_sessions([dict(row(mainnet=True), corpusBytes=1_000_000_000)], 'release', budget=40)

    def test_declared_micro_charge_is_a_validated_maximum(self):
        r=dict(row(),id='time/micro-test',replay='micro',chargeSeconds=16)
        sealed=timing.size_sessions([r],'release',ratios={r['id']:2},budget=40)[0]
        self.assertEqual(sealed['rows'][0]['chargeSeconds'],16)
        self.assertEqual(timing.charges(dict(r,chargeSeconds=1),{r['id']:2.1}),3)
        for invalid in (-1,True,1.5,'16'):
            with self.subTest(invalid=invalid),self.assertRaisesRegex(ValueError,'process charge'):
                timing.charges(dict(r,chargeSeconds=invalid),{r['id']:2})

    def test_micro_band_uses_a_only_and_requires_sub5percent(self):
        r = row()
        r['bPairs'] = 0
        self.assertEqual(timing.baseline_band(r, pairs(r))['status'], 'PASS')
        p = pairs(r)
        for pair in p:
            for item in pair['baseline']['data']['rows'][40:]:
                item['nsPerBlock'] *= 1.1
        self.assertEqual(list(timing.baseline_band(r, p)['qualificationFailures']), ['B baseline stability'])
        self.assertEqual(timing.baseline_band(r, pairs(r, a=[.8, 1.2] * 8))['status'], 'INCONCLUSIVE')

    def test_options_one_and_three_keep_preprod_floors(self):
        rows = [row('tcp'), row(), row('tcp', mainnet=True, size=35_000_000), row(mainnet=True, size=35_000_000)]
        for r in rows:
            r.pop('aPairs');r.pop('bPairs')
        for option in (1, 3):
            p = timing.size_sessions(rows, 'release', option=option, budget=40)[0]
            self.assertLessEqual(p['destroyGuardSeconds'], 7000)
            for r in p['rows']:
                if not r['mainnet']:
                    self.assertEqual(r['aPairs'], 16)
                    self.assertEqual(r['bPairs'], 16 if option == 1 else 24)
                elif option == 1:
                    self.assertGreaterEqual(r['aPairs'], 8)
                    self.assertEqual(r['aPairs'] % 2, 0)
                else:
                    self.assertEqual(r['aPairs'], 0)
                    self.assertEqual(r['bPairs'] % 2, 0)
                    self.assertEqual(r['rowClass'], 'fail-only')

    def test_schedule_a_first_round_robin_and_same_counts(self):
        import gate_schedule
        plan = gate_schedule.prepare(timing.size_sessions([row('tcp'), row()], 'release', budget=40)[0])
        schedule = list(timing.schedule(plan))
        split = next(i for i, block in enumerate(schedule) if block['phase'] == 'B')
        self.assertTrue(all(block['phase'] == 'A' for block in schedule[:split]))
        for phase in ('A', 'B'):
            for r in plan['rows']:
                self.assertEqual(sum(len(block['pairs']) for block in schedule if block['phase'] == phase and block['row'] == r['id']), r['aPairs' if phase == 'A' else 'bPairs'])

    def test_hard_cap_archive_authority_and_cost_guard(self):
        from gate_fly_guard import machine_command
        plan = timing.size_sessions([row()], 'release', budget=40)[0]
        for seconds in (6450, 7000, 7200):
            p = dict(plan, destroyGuardSeconds=seconds, measurementCutoffSeconds=seconds - 350,
                     limitUsd=timing.price(seconds), limitWithRepeatUsd=2 * timing.price(seconds))
            self.assertIn(str(seconds - 10), machine_command(p))
        for change in [dict(destroyGuardSeconds=7201), dict(limitUsd=0), dict(measurementCutoffSeconds=100000), dict(limitWithRepeatUsd=0)]:
            with self.assertRaises(ValueError):
                machine_command(dict(plan, **change))

    def test_reports_every_design_subset_recorded_only_and_no_source_reads(self):
        for design in ('full', 'option1', 'option2', 'option3'):
            with tempfile.TemporaryDirectory() as td:
                root = Path(td)
                r = row()
                if design == 'option1':
                    r.update(aPairs=8, bPairs=8)
                if design == 'option3':
                    r.update(aPairs=0, rowClass='fail-only')
                plan = dict(design=design, rows=[r], destroyGuardSeconds=7000, limitUsd=.75, limitWithRepeatUsd=1.5)
                seal = dict(mode='release', ruleVersion=gate.stats.RULE_VERSION, rows=[r], timePlans=[plan],
                            canonical=True, timeScopeDecided=True, bands={}, releaseSet=[r['id']], gated=dict(areas={}))
                gate.write_seal(root, seal)
                raw = root / 'time/raw.json'
                gate.session.save(raw, dict(pairs=pairs(r), invalid=None))
                gate.session.save(root / 'time/index.json', [dict(session=0, phase='first', ids=[r['id']], path='time/raw.json', sha256=gate.digest(raw))])
                actual = gate.report(root)
                self.assertEqual(actual['outcome'], 'RELEASE PASS', actual)
                with patch.object(gate, 'GATE', root / 'nonexistent'):
                    self.assertEqual(gate.report(root), actual)
                seal['mode'] = 'change'
                for entry in plan['rows']:
                    entry['aPairs'] = 0
                gate.write_seal(root, seal)
                gate.session.save(raw, dict(pairs=pairs(r, [0.99774, 1.05226] * 8), invalid=None))
                index = json.loads((root / 'time/index.json').read_text()); index[0]['sha256'] = gate.digest(raw)
                gate.session.save(root / 'time/index.json', index)
                self.assertEqual(gate.report(root)['outcome'], 'SUBSET')
        # C5: one release seal permits its repeat; only the reporting rule changes.
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            r = dict(row(pairs=24), expected=dict(release='FAIL'))
            plan = dict(design='full', rows=[r], repeatAllowed=True, destroyGuardSeconds=4145,
                        limitUsd=.45221430555555553, limitWithRepeatUsd=.9044286111111111)
            seal = dict(mode='release', ruleVersion=gate.stats.RULE_VERSION, rows=[r], timePlans=[plan],
                        canonical=False, timeScopeDecided=True, bands={}, releaseSet=[r['id']], gated=dict(areas={}))
            gate.write_seal(root, seal)
            index = []
            for phase in ('first', 'repeat'):
                path = f'time/{phase}.json'
                gate.session.save(root / path, dict(pairs=pairs(r, 1.025), invalid=None))
                index.append(dict(session=0, phase=phase, ids=[r['id']], path=path, sha256=gate.digest(root / path)))
            gate.session.save(root / 'time/index.json', index)
            saved = {path: path.read_bytes() for path in root.rglob('*') if path.is_file()}
            for mode, expected in ((None, 'FAIL'), ('change', 'PASS')):
                report = gate.report(root, mode=mode)
                actual = report['rows'][r['id']]
                for phase in ('first', 'repeat'):
                    self.assertIn(f'{phase} admitted pairs: {{"A": 16, "B": 24}}', report['text'])
                    self.assertIn(f'{phase} check:', report['text'])
                self.assertRegex(report['text'], r'16/ 24 +0\.000%')
                self.assertIn('controls outcome: complete', report['text'])
                self.assertEqual('noise record N:' in report['text'], mode == 'change')
                if mode == 'change':
                    self.assertIn('noise record N: 2.310%', report['text'])
                self.assertEqual([actual[phase]['status'] for phase in ('first', 'repeat')] + [actual['status']],
                                 [expected] * 3, f'C5 first/repeat/joint in {mode or "sealed release"} mode')
            self.assertEqual({path: path.read_bytes() for path in root.rglob('*') if path.is_file()}, saved)
            plan['repeatAllowed'] = False
            gate.write_seal(root, seal)
            with self.assertRaisesRegex(ValueError, 'Unsealed repeat'):
                gate.report(root, mode='change')
            raw = root / index[0]['path']
            gate.session.save(raw, dict(pairs=pairs(r, 1.025, a=[1.06] * 16)))
            gate.session.save(root / 'time/index.json', [dict(index[0], sha256=gate.digest(raw))])
            for weak in ('A band', 'micro-band'):
                with self.subTest(weak=weak):
                    if weak == 'A band':
                        self.assertIn('noise record N: 2.310%', '\n'.join(timing.read_results(root, seal, mode='change')[1]))
                    else:
                        plan['design'], r['bPairs'] = 'micro-band', 0
                        self.assertRegex('\n'.join(timing.read_results(root, seal)[1]), r'16/  0 +—')
        seal = dict(mode='release', canonical=True, timeScopeDecided=True,
                    rows=[dict(id='real'), dict(id='artifact', rowClass='recorded-only')], releaseSet=['real'])
        self.assertEqual(gate.outcome(seal, dict(real=dict(status='PASS'), artifact=dict(status='FAIL'))), 'RELEASE PASS')


if __name__ == '__main__':
    unittest.main()
