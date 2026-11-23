"""D41, change mode, all designs, pooled repeats and pure report regressions."""
import copy
import io
import json
import time
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


def probe_rule(**changes):
    return dict(dict(statistic='cv', limit=.054, processes=16, maxDraws=6, gen0BudgetBytes=16777216), **changes)


def probe_records(*, noisy=False, budget=16777216):
    record = dict(exitCode=0, data=dict(rows=[dict(nsPerBlock=n) for n in ([1, 3] if noisy else [99, 101]) * 30],
                                      processSettings=dict(gcConfiguration=dict(GCGen0MaxBudget=budget))))
    return {f'probe-{number:02}': copy.deepcopy(record) for number in range(1, 17)}


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
    def test_probe_statistic_and_validation(self):
        records = probe_records(noisy=True)
        cv = (60 / 59) ** .5 / 2
        result = timing.probe_result(records, probe_rule(limit=cv))
        self.assertAlmostEqual(result['cv'], cv)
        self.assertAlmostEqual(result['lag1'], -59 / 60)
        self.assertEqual(result['decision'], 'kept')
        self.assertEqual(timing.probe_result(records, probe_rule(limit=cv - 1e-8))['reasons'], ['cv-over-limit'])
        self.assertEqual(timing.probe_result(probe_records(budget=8388608), probe_rule())['reasons'], ['gen0-budget-mismatch'])
        self.assertEqual(timing.probe_result(probe_records(noisy=True, budget=8388608), probe_rule())['reasons'],
                         ['cv-over-limit', 'gen0-budget-mismatch'])
        for what in ('missing process', 'exit', 'missing budget', 'bad budget', 'nan', 'short', 'constant'):
            with self.subTest(what=what), self.assertRaisesRegex(ValueError, 'probe|Probe'):
                bad = copy.deepcopy(records)
                record = bad['probe-01']
                if what == 'missing process': bad.pop('probe-16')
                if what == 'exit': record['exitCode'] = 1
                if what == 'missing budget': record['data'].pop('processSettings')
                if what == 'bad budget': record['data']['processSettings']['gcConfiguration']['GCGen0MaxBudget'] = True
                if what == 'nan': record['data']['rows'][0]['nsPerBlock'] = float('nan')
                if what == 'short': record['data']['rows'].pop()
                if what == 'constant': record['data']['rows'] = [dict(nsPerBlock=1)] * 60
                timing.probe_result(bad, probe_rule())

    def test_probe_worker_uses_baseline_before_blocks_and_errors_without_redraw(self):
        job = dict(kind='time', replay='offline', dotnet='d', server='s', capture='c', environment={})
        one = dict(client='baseline/client.dll', harness='baseline/bench.dll', dllSha256s={})
        two = dict(one, client='candidate/client.dll')
        plan = dict(builds={'a': one, 'b': two}, counts=[180, 60], measurementCutoffSeconds=6600,
                    probe=dict(probe_rule(), build='a', workload='ref'), workloads={'ref': job, 'block': job},
                    blocks=[dict(members=['measured'], workload='block', chargeSeconds=13)], members={'measured': dict(build='b')})
        for kind, budget in [('keep', 16777216), ('reject', 8388608), ('error', None)]:
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as name:
                root = Path(name)
                records = probe_records(budget=budget)
                seen = []
                def run(spec, job, destination, **_):
                    seen.append(destination.name)
                    if destination.name in records:
                        self.assertEqual(spec['client'], str(root / one['client']))
                        self.assertEqual(job['counts'], [180, 60])
                        return records[destination.name]
                    self.assertEqual(spec['client'], str(root / two['client']))
                    return member()
                out = root / 'out'
                with patch.dict('os.environ', P2_MACHINE_CREATED_UNIX=str(int(time.time()))), patch.object(timing.session, 'member', side_effect=run):
                    self.assertEqual(timing.run(root, plan, out), kind != 'error')
                status = json.loads((out / 'session-status.json').read_text())
                self.assertEqual(status['completedAllDeclaredRounds'], kind == 'keep')
                self.assertEqual(status['exitCode'], 2 if kind == 'error' else 0)
                if kind == 'error':
                    self.assertNotIn('measured', seen)
                else:
                    ids = list(records)
                    self.assertEqual(seen, ids + (['measured'] if kind == 'keep' else []))
                    self.assertEqual({path.stem for path in (out / 'probe').glob('*.json')}, set(ids))
                    self.assertEqual(status['probe'], 'kept' if kind == 'keep' else 'rejected')
                if kind == 'keep':
                    measured, = json.loads((out / 'members.json').read_text())
                    self.assertEqual((measured['id'], measured['predecessorBuild']), ('measured', None))
                else:
                    self.assertFalse((out / 'members.json').exists())

    def test_probe_review_gc_never_changes_verdict(self):
        r = row()
        ref = timing.verdict(r, pairs(r), 'release')
        self.assertEqual(ref['status'], 'PASS')
        for value in (0, None, 'invalid', True):
            with self.subTest(value=value):
                observed = pairs(r)
                for pair in observed:
                    for item in pair['candidate']['data']['rows']:
                        item['gen0Collections'] = 0
                    pair['candidate']['data']['rows'][0]['gen0Collections'] = value
                got = timing.verdict(r, observed, 'release')
                self.assertEqual((got['status'], got.get('confidence95')), (ref['status'], ref['confidence95']))

    def test_probe_review_readme_describes_shipped_probe_and_steal_origin(self):
        text = (Path(__file__).resolve().parents[1] / 'gate/README.md').read_text()
        self.assertTrue('first long scheduled comparison client' in text)
        self.assertIn('excluding host probe processes', text)
        self.assertNotIn('tighter probe limit', text)
        self.assertNotIn('old rule behind the same probe', text)

    def test_probe_reserve_and_work_cap(self):
        r = dict(row(pairs=1), aPairs=0, bPairs=1)
        plan, = timing.size_sessions([r], 'change', budget=40, probe=probe_rule())
        self.assertEqual(plan['rawSeconds'], 26 + 208)
        guard = plan['destroyGuardSeconds']
        self.assertAlmostEqual(plan['limitUsd'], timing.price(guard) + 5 * timing.price(1068))
        self.assertEqual(plan['limitWithRepeatUsd'], plan['limitUsd'])
        self.assertEqual(plan['measurementCutoffSeconds'], 753)
        ams, = timing.size_sessions([dict(r, region='ams')], 'change', budget=40, probe=probe_rule())
        self.assertEqual(ams['probe']['maxDraws'], 4)
        self.assertAlmostEqual(ams['limitUsd'], timing.price(guard, 'ams') + 3 * timing.price(1068, 'ams'))
        with self.assertRaisesRegex(ValueError, 'does not fit'):
            timing.size_sessions([dict(r, chargeSeconds=int(timing.RAW_CAP // 2))], 'change', budget=40, probe=probe_rule())

    def test_probe_report_gc_rates_are_attributed_and_informational(self):
        r = row()
        r['sides']['candidate'] = dict(r['sides']['candidate'], commit='c' * 40)
        measured = pairs(r)
        for pair in measured:
            for name in ('baseline', 'candidate'):
                d = pair[name]['data']
                budget = 8388608 if pair['phase'] == 'B' and name == 'candidate' else 16777216
                d['processSettings'] = dict(gcConfiguration=dict(GCGen0MaxBudget=budget))
                for item in d['rows']:
                    item.update(gen0Collections=(1 if budget == 16777216 else 3), gen1Collections=0, gen2Collections=2)
        result = timing.verdict(r, measured, 'release')
        self.assertEqual(result['status'], 'PASS')
        gc = timing.gc_per_build(measured)
        self.assertEqual(gc['baseline']['passes'], 3 * r['aPairs'] * 60)
        self.assertEqual(gc['baseline']['collectionsPerPass'], dict(gen0Collections=1, gen1Collections=0, gen2Collections=2))
        self.assertEqual(gc['candidate']['collectionsPerPass']['gen0Collections'], 3)
        self.assertEqual(gc['candidate']['gen0BudgetBytes'], [8388608])
        self.assertEqual(timing.gc_per_build(pairs(r))['candidate']['gen0BudgetBytes'], [])
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            plan = dict(design='full', rows=[r], destroyGuardSeconds=7000, limitUsd=.75, limitWithRepeatUsd=1.5)
            seal = dict(mode='release', ruleVersion=gate.stats.RULE_VERSION, timePlans=[plan])
            path = root / 'time/0-first.json'
            gate.session.save(path, dict(pairs=measured, invalid=None))
            gate.session.save(root / 'time/index.json', [dict(session=0, phase='first', path='time/0-first.json', sha256=gate.digest(path))])
            values, lines = timing.read_results(root, seal)
            self.assertEqual(values[r['id']]['status'], 'PASS')
            self.assertTrue(any('first candidate GC' in line and 'gen0 2880 (3/pass)' in line and 'GCGen0MaxBudget bytes: 8388608' in line
                                for line in lines))
            measured[0]['baseline']['data'].pop('processSettings')
            for observed, expected in [(pairs(r), 'GCGen0MaxBudget bytes: not recorded'),
                                       (measured, '16777216 (some processes not recorded)')]:
                gate.session.save(path, dict(pairs=observed, invalid=None))
                index = json.loads((root / 'time/index.json').read_text()); index[0]['sha256'] = gate.digest(path)
                gate.session.save(root / 'time/index.json', index)
                line = next(line for line in timing.read_results(root, seal)[1] if 'first baseline GC' in line)
                self.assertIn(expected, line)
                if expected.endswith(': not recorded'):
                    for i in range(3):
                        self.assertIn(f'gen{i} not recorded', line)
                        self.assertNotIn(f'gen{i} 0', line)


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


def test_azure_region_price_and_draw_cap():
    for region in ('eastus2', 'eastus'):
        plan, = timing.size_sessions([dict(row(), provider='azure', region=region)], 'change', budget=30, probe={
            'processes': 16, 'statistic': 'cv', 'limit': .054, 'gen0BudgetBytes': 16_777_216, 'maxDraws': 6})
        assert plan['probe']['maxDraws'] == 6
        seconds = plan['destroyGuardSeconds'] + 900
        assert timing.price(plan['destroyGuardSeconds'], region, provider='azure') > seconds / 3600 * .495
    assert {p['platform'] for p in timing.PROVIDERS.values()} == timing.session.MEASURING_HOSTS


def test_azure_does_not_enter_legacy_measurement_prepare():
    with patch.object(bundle, 'time_rows', side_effect=AssertionError('legacy prepare reached')):
        try:
            bundle.prepare(None, None, None, {'time': {'platform': {'provider': 'azure'}}}, None)
        except ValueError as failure:
            assert 'Mac' in str(failure)
        else:
            raise AssertionError('legacy Azure prepare accepted')


def test_r1_azure_smoke_classifies_complete_and_rejects_provider_mismatch():
    r = dict(row(pairs=2), aPairs=6, expected='smoke', provider='azure', region='eastus2',
             noise=dict(band=.028, machine='Standard_F8als_v7', design='long'))
    p = pairs(r)
    for pair in p:
        for side in ('baseline', 'candidate'):
            pair[side]['platform'] = 'azure'
    plan = dict(mode='release', repeatAllowed=True, rows=[r])
    def status():
        results = {mode: {r['id']: timing.verdict(r, p, mode, noise=r['noise'])} for mode in ('release', 'change')}
        return timing.controls_outcome(plan, results)[0]
    assert status() == 'complete'
    p[0]['baseline']['platform'] = 'fly'
    assert status() == 'hard-stop'


def test_r1_report_prints_observed_fallback_region_and_steal(tmp_path):
    time_out = tmp_path / 'time'
    time_out.mkdir()
    (time_out / 'draws.json').write_text(json.dumps([dict(session=0, phase='first', draw=1,
        app='chrysalis-gate-20990405t000100-abcdef12', decision='kept', bookedUsd=.24,
        region='eastus', provider='azure', steal='not observable on Azure', fallbackOf='fixture-primary')]))
    text = '\n'.join(timing.draw_lines(tmp_path, first_only=False))
    assert 'eastus' in text and 'eastus2' not in text and 'not observable on Azure' in text


def _run8_member(factor=1.0, *, platform='azure', warm_late=None, meas_groups=None, jit_ticks=0, rejected=()):
    m = member(factor)
    m['platform'] = platform
    m['timingRejectedReasons'] = list(rejected)
    if warm_late is not None:
        for idx, f in enumerate(warm_late):
            for item in m['data']['warmupRows'][120 + idx * 20:120 + (idx + 1) * 20]:
                item['nsPerBlock'] *= f
    if meas_groups is not None:
        for idx, f in enumerate(meas_groups):
            for item in m['data']['rows'][idx * 20:(idx + 1) * 20]:
                item['nsPerBlock'] *= f
    if jit_ticks:
        for b in m['data']['groupBoundaries']:
            if b['stage'] == 'measured' and b['passIndex'] == 1:
                b['delta']['compilationTimeTicks'] = jit_ticks
    return m


def _run8_row(rid='time/micro-signing', *, a_pairs=16, b_pairs=0, reserve=1, provider='azure',
              replay='offline', noise=None, expected=None):
    r = dict(row(replay if replay != 'micro' else 'offline', pairs=max(b_pairs, 5)),
             id=rid, replay=replay, aPairs=a_pairs, bPairs=b_pairs, provider=provider, region='eastus2',
             job={}, noise=noise if noise is not None else dict(band=.0231, machine='Standard_F8als_v7', design='long'))
    if reserve:
        r['reservePairs'] = reserve
    if expected is not None:
        r['expected'] = expected
    return r


def _run8_pairs(r, *, a_specs=None, b_specs=None, platform='azure'):
    reserve = r.get('reservePairs', 0)
    result = []
    for phase, count, specs in (('A', r['aPairs'], a_specs or {}), ('B', r['bPairs'], b_specs or {})):
        if not count:
            continue
        for num in range(1, count + reserve + 1):
            pair = dict(pair=num, id=r['id'], phase=phase)
            for s in ('baseline', 'candidate'):
                spec = specs.get((num, s), {})
                m = _run8_member(platform=platform, **spec) if isinstance(spec, dict) else spec
                ident = r['sides']['baseline' if phase == 'A' else s]
                m['identity'] = {k: ident[k] for k in ('commit', 'treeId', 'runtime', 'dllSha256s')}
                m['data']['runtime'] = ident['runtime']
                pair[s] = m
            result.append(pair)
    return result


def test_run8_d3_run7_shape_triggers_replaces_and_is_outcome_independent():
    r = _run8_row(a_pairs=16, b_pairs=0, reserve=1)
    plan = dict(design='noise-band', provider='azure', region='eastus2', rows=[r], counts=[180, 60])
    # Run 7 micro-signing shape: pair 6 baseline 1.35x across whole life; clean spare pair 17.
    ps = _run8_pairs(r, a_specs={(6, 'baseline'): dict(factor=1.35)})
    # Without replacement (declared 1..16 + spare aside) -> INCONCLUSIVE (band >= 5%).
    raw_res = timing.baseline_band(r, [p for p in ps if p['pair'] <= 16], jit=[p for p in ps if p['pair'] == 17])
    assert raw_res['status'] == 'INCONCLUSIVE'
    decisions = timing.session_outliers(plan, ps)
    d = decisions[r['id']]
    assert d['threshold'] == 0.20 and d['replaced'] is True and d['finding'] is False
    assert d['replacement'] == dict(phase='A', replacedPair=6, sparePair=17, triggerSides=['baseline'])
    assert len(d['triggers']) == 1 and d['triggers'][0]['pair'] == 6 and d['triggers'][0]['side'] == 'baseline'
    assert len(d['triggers'][0]['departures']) == 7 and all(x > 0.20 for x in d['triggers'][0]['departures'])
    assert d['spareAdmitted'] == {'A': True}
    keep, aside = timing.judged(r, ps, d)
    assert sorted(p['pair'] for p in keep) == list(range(1, 17))
    assert [p['pair'] for p in aside] == [17]
    # Archive pairs must not be mutated in place.
    assert sorted(p['pair'] for p in ps) == list(range(1, 18))
    swapped = timing.baseline_band(r, keep, jit=aside)
    assert swapped['status'] == 'PASS' and swapped['band'] < 0.01

    # D3-5: replacement still happens even when the row would PASS without replacement.
    ps_pass = _run8_pairs(r, a_specs={(3, 'baseline'): dict(factor=1.25), (3, 'candidate'): dict(factor=1.25)})
    raw_pass = timing.baseline_band(r, [p for p in ps_pass if p['pair'] <= 16], jit=[p for p in ps_pass if p['pair'] == 17])
    assert raw_pass['status'] == 'PASS'
    d_pass = timing.session_outliers(plan, ps_pass)[r['id']]
    assert d_pass['replaced'] is True and d_pass['replacement']['replacedPair'] == 3
    assert d_pass['replacement']['triggerSides'] == ['baseline', 'candidate']


def test_run8_d3_whole_life_windows_and_threshold_edges():
    r = _run8_row(a_pairs=16, b_pairs=0, reserve=1)
    plan = dict(design='noise-band', provider='azure', region='eastus2', rows=[r], counts=[180, 60])
    # 40% slow in measured passes ONLY (warm-up normal) -> does NOT trigger.
    ps_burst = _run8_pairs(r, a_specs={(4, 'baseline'): dict(warm_late=[1.0, 1.0, 1.0], meas_groups=[1.40, 1.40, 1.40])})
    d_burst = timing.session_outliers(plan, ps_burst)[r['id']]
    assert d_burst['triggers'] == [] and d_burst['replaced'] is False
    assert d_burst['maxMeanDeparture'] > 0.39 and d_burst['maxWholeLifeDeparture'] == 0.0
    # Session 1515 worst shape: mean +19.5%, windows 13%, 27%, 14%, 19%, 20%, 19.5% -> does NOT trigger.
    ps_1515 = _run8_pairs(r, a_specs={(4, 'baseline'): dict(warm_late=[1.13, 1.27, 1.14], meas_groups=[1.19, 1.20, 1.195])})
    d_1515 = timing.session_outliers(plan, ps_1515)[r['id']]
    assert d_1515['triggers'] == [] and d_1515['replaced'] is False
    assert abs(d_1515['maxWholeLifeDeparture'] - 0.13) < 1e-6
    # Exactly +20% in one window (+25% in others) -> does NOT trigger.
    ps_edge = _run8_pairs(r, a_specs={(4, 'baseline'): dict(warm_late=[1.20, 1.25, 1.25], meas_groups=[1.25, 1.25, 1.25])})
    d_edge = timing.session_outliers(plan, ps_edge)[r['id']]
    assert d_edge['triggers'] == [] and d_edge['replaced'] is False
    # Just above +20% in all 7 triggers; just below -20% in all 7 triggers.
    for factor, sign in ((1.205, 1), (0.795, -1)):
        ps_ok = _run8_pairs(r, a_specs={(4, 'baseline'): dict(factor=factor)})
        d_ok = timing.session_outliers(plan, ps_ok)[r['id']]
        assert d_ok['replaced'] is True and len(d_ok['triggers']) == 1
        assert all(sign * x > 0.20 for x in d_ok['triggers'][0]['departures'])


def test_run8_d3_findings_two_rows_and_two_pairs():
    r1 = _run8_row('time/micro-a', a_pairs=16, b_pairs=0, reserve=1)
    r2 = _run8_row('time/micro-b', a_pairs=16, b_pairs=0, reserve=1)
    plan2 = dict(design='noise-band', provider='azure', region='eastus2', rows=[r1, r2], counts=[180, 60])
    # Rule 6a: two triggered rows -> finding, no replacement anywhere.
    ps_two_rows = (_run8_pairs(r1, a_specs={(2, 'baseline'): dict(factor=1.35)})
                   + _run8_pairs(r2, a_specs={(5, 'candidate'): dict(factor=1.35)}))
    dec = timing.session_outliers(plan2, ps_two_rows)
    assert all(d['finding'] is True and d['replaced'] is False and d['findingReason'] for d in dec.values())
    # Rule 6b: two triggering pairs in same phase -> finding, no replacement.
    plan1 = dict(design='noise-band', provider='azure', region='eastus2', rows=[r1], counts=[180, 60])
    ps_same = _run8_pairs(r1, a_specs={(2, 'baseline'): dict(factor=1.35), (8, 'candidate'): dict(factor=1.35)})
    d_same = timing.session_outliers(plan1, ps_same)[r1['id']]
    assert d_same['finding'] is True and d_same['replaced'] is False
    # Rule 6b: one triggering pair in A and one in B (including spare counting as triggering pair!).
    r_ab = _run8_row('time/preprod-offline', a_pairs=8, b_pairs=8, reserve=1)
    plan_ab = dict(design='full', provider='azure', region='eastus2', rows=[r_ab], counts=[180, 60])
    for b_pair_num in (3, 9):  # 3 = declared B pair; 9 = B spare pair
        ps_ab = _run8_pairs(r_ab, a_specs={(2, 'baseline'): dict(factor=1.35)},
                            b_specs={(b_pair_num, 'candidate'): dict(factor=1.35)})
        d_ab = timing.session_outliers(plan_ab, ps_ab)[r_ab['id']]
        assert d_ab['finding'] is True and d_ab['replaced'] is False


def test_run8_d3_non_findings_spare_trigger_and_unadmitted_spare():
    r = _run8_row(a_pairs=16, b_pairs=0, reserve=1)
    plan = dict(design='noise-band', provider='azure', region='eastus2', rows=[r], counts=[180, 60])
    # Rule 6c: only the spare triggers -> no finding, no replacement, declared pairs stand.
    ps_c = _run8_pairs(r, a_specs={(17, 'baseline'): dict(factor=1.35)})
    dc = timing.session_outliers(plan, ps_c)[r['id']]
    assert dc['finding'] is False and dc['replaced'] is False and 'spare' in dc['reason'].lower()
    # Rule 6d: declared pair triggers, spare not admitted -> no finding, no replacement, judged as measured.
    ps_d = _run8_pairs(r, a_specs={(6, 'baseline'): dict(factor=1.35),
                                   (17, 'baseline'): dict(rejected=['outside CPU'])})
    dd = timing.session_outliers(plan, ps_d)[r['id']]
    assert dd['finding'] is False and dd['replaced'] is False and dd['spareAdmitted'] == {'A': False}
    assert 'spare' in dd['reason'].lower()
    # Rule 6e: no trigger, spare not admitted -> no finding, no replacement, reported in entry.
    ps_e = _run8_pairs(r, a_specs={(17, 'candidate'): dict(rejected=['outside CPU'])})
    de = timing.session_outliers(plan, ps_e)[r['id']]
    assert de['finding'] is False and de['replaced'] is False and de['spareAdmitted'] == {'A': False}
    assert 'spare' in de['reason'].lower()


def test_run8_d3_rule9_unused_spare_excluded_and_jit_keeps_all():
    r = _run8_row(a_pairs=6, b_pairs=0, reserve=1)
    plan = dict(design='noise-band', provider='azure', region='eastus2', rows=[r], counts=[180, 60])
    # 4 admitted declared pairs + 1 admitted spare -> still fails < 5 admission floor!
    ps_floor = _run8_pairs(r, a_specs={(1, 'baseline'): dict(rejected=['outside CPU']),
                                       (2, 'baseline'): dict(rejected=['outside CPU'])})
    d_floor = timing.session_outliers(plan, ps_floor)[r['id']]
    keep, aside = timing.judged(r, ps_floor, d_floor)
    res_floor = timing.baseline_band(r, keep, jit=aside)
    assert res_floor['status'] == 'INCONCLUSIVE' and res_floor['admittedPairs']['B'] == 4 and '<5' in res_floor['reason']

    # Unused spare with non-triggering slowness (+15%) changes no ratio, band or descriptive estimate.
    ps_clean = _run8_pairs(r)
    ps_noisy_spare = _run8_pairs(r, a_specs={(7, 'candidate'): dict(factor=1.15)})
    k1, a1 = timing.judged(r, ps_clean, timing.session_outliers(plan, ps_clean)[r['id']])
    k2, a2 = timing.judged(r, ps_noisy_spare, timing.session_outliers(plan, ps_noisy_spare)[r['id']])
    r1 = timing.baseline_band(r, k1, jit=a1)
    r2 = timing.baseline_band(r, k2, jit=a2)
    assert r1['status'] == r2['status'] == 'PASS'
    assert r1['mean'] == r2['mean'] and r1['confidence95'] == r2['confidence95'] and r1['band'] == r2['band']

    # Compile share counts BOTH an unused spare AND a replaced pair.
    big_jit = 1_000_000_000  # 1000 ms >> 0.5% of measured body
    ps_spare_jit = _run8_pairs(r, a_specs={(7, 'baseline'): dict(jit_ticks=big_jit)})
    ks, as_ = timing.judged(r, ps_spare_jit, timing.session_outliers(plan, ps_spare_jit)[r['id']])
    res_sj = timing.baseline_band(r, ks, jit=as_)
    assert res_sj['status'] == 'INCONCLUSIVE' and 'B baseline quiescence' in res_sj['qualificationFailures']

    ps_rep_jit = _run8_pairs(r, a_specs={(3, 'baseline'): dict(factor=1.35, jit_ticks=big_jit)})
    d_rj = timing.session_outliers(plan, ps_rep_jit)[r['id']]
    assert d_rj['replaced'] is True
    kr, ar = timing.judged(r, ps_rep_jit, d_rj)
    res_rj = timing.baseline_band(r, kr, jit=ar)
    assert res_rj['status'] == 'INCONCLUSIVE' and 'B baseline quiescence' in res_rj['qualificationFailures']


def test_run8_d3_inert_without_reserve_and_prechange_report_identical(tmp_path):
    r = _run8_row('time/preprod-offline', a_pairs=6, b_pairs=6, reserve=0)
    plan = dict(design='full', provider='azure', region='eastus2', rows=[r], counts=[180, 60],
                destroyGuardSeconds=2494, limitUsd=0.6223, limitWithRepeatUsd=1.2446, repeatAllowed=True)
    # Run 7 shape without reservePairs: session_outliers returns empty, no outlier line in report.
    ps = _run8_pairs(r, a_specs={(2, 'baseline'): dict(factor=1.35)})
    assert timing.session_outliers(plan, ps) == {}
    payload = dict(pairs=ps, invalid=None, host={'processorCount': 8}, context={'session': 's0'})
    path = tmp_path / 'time/0-first.json'
    timing.session.save(path, payload)
    seal = dict(mode='release', provider='azure', timePlans=[plan])
    timing.session.save(tmp_path / 'time/index.json', [dict(session=0, phase='first', ids=[r['id']],
                        path='time/0-first.json', sha256=bundle.sha(path))])
    vals, lines = timing.read_results(tmp_path, seal)
    text = '\n'.join(lines)
    assert 'outlier' not in vals[r['id']] and 'outlier:' not in text and 'candidate-side replacements' not in text
    # Clean pre-change Azure fixture report text pinned byte-for-byte against 3cc50131.
    clean_dir = tmp_path / 'clean'
    clean_ps = _run8_pairs(r)
    clean_payload = dict(pairs=clean_ps, invalid=None, host={'processorCount': 8}, context={'session': 's0'})
    clean_path = clean_dir / 'time/0-first.json'
    timing.session.save(clean_path, clean_payload)
    timing.session.save(clean_dir / 'time/index.json', [dict(session=0, phase='first', ids=[r['id']],
                        path='time/0-first.json', sha256=bundle.sha(clean_path))])
    clean_text = '\n'.join(timing.read_results(clean_dir, seal)[1])
    assert 'outlier:' not in clean_text and 'candidate-side replacements' not in clean_text
    # Pin exact SHA-256 captured at unchanged 3cc50131 (verified during red run):
    import hashlib
    assert hashlib.sha256(clean_text.encode('utf-8')).hexdigest() == '8f09fa9ea297e2ced73a9a4ccaf2af8b7a58e8bf044bcefdf5ad50d0e55ada28'


def test_run8_d3_sibling_groups_and_candidate_side_reporting(tmp_path):
    # Phase A is ONE group (fails if baseline/candidate are grouped apart: 2 pairs => 4 total vs 2 per side < 3!).
    r_small = _run8_row('time/micro-tiny', a_pairs=1, b_pairs=0, reserve=1)
    plan_small = dict(design='noise-band', provider='azure', region='eastus2', rows=[r_small], counts=[180, 60])
    ps_small = _run8_pairs(r_small, a_specs={(1, 'baseline'): dict(factor=1.35)})
    d_small = timing.session_outliers(plan_small, ps_small)[r_small['id']]
    assert d_small['replaced'] is True and d_small['replacement']['replacedPair'] == 1

    # Phase B: baseline trigger fires, candidate trigger fires, uniformly slower candidate does NOT trigger.
    r_b = _run8_row('time/preprod-offline', a_pairs=0, b_pairs=6, reserve=1)
    plan_b = dict(design='full', provider='azure', region='eastus2', rows=[r_b], counts=[180, 60],
                  destroyGuardSeconds=2494, limitUsd=0.6306, limitWithRepeatUsd=0.6306, repeatAllowed=False)
    ps_uniform = _run8_pairs(r_b, b_specs={(i, 'candidate'): dict(factor=1.35) for i in range(1, 8)})
    assert timing.session_outliers(plan_b, ps_uniform)[r_b['id']]['triggers'] == []

    for side_name in ('baseline', 'candidate'):
        d_dir = tmp_path / side_name
        ps_side = _run8_pairs(r_b, b_specs={(3, side_name): dict(factor=1.35)})
        p_file = d_dir / 'time/0-first.json'
        timing.session.save(p_file, dict(pairs=ps_side, invalid=None, host={'processorCount': 8}, context={}))
        seal = dict(mode='change', provider='azure', timePlans=[plan_b])
        timing.session.save(d_dir / 'time/index.json', [dict(session=0, phase='first', ids=[r_b['id']],
                            path='time/0-first.json', sha256=bundle.sha(p_file))])
        vals, lines = timing.read_results(d_dir, seal)
        text = '\n'.join(lines)
        rep = vals[r_b['id']]['outlier']['replacement']
        assert rep == dict(phase='B', replacedPair=3, sparePair=7, triggerSides=[side_name])
        assert f'first outlier: replaced phase B pair 3 (side {side_name}) with spare pair 7' in text
        if side_name == 'candidate':
            assert f'candidate-side replacements: 1 ({r_b["id"]})' in text
        else:
            assert 'candidate-side replacements:' not in text


def test_run8_d3_sizing_spares_and_release_growth():
    from types import SimpleNamespace
    import gate_prepare as prepare
    config = prepare.azure_config(json.loads((gate.GATE / 'gated.json').read_text()))
    config['time']['noise']['Standard_F8als_v7'] = {}
    audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
    entries = json.loads((gate.GATE / 'plans/noise-records.json').read_text())
    selected = gate.select_rows(SimpleNamespace(mode='release', plan=None, only=None), config, audit, plan_entries=entries)
    seal = dict(gated=config, mode='release', noiseRecords=True, sessionPerRow=False, orderingProof=False,
                rows=[dict(item, sides=dict(baseline=side(), candidate=side())) for item in selected])
    s1, s2 = prepare.time_plans(seal)
    assert (s1['rawSeconds'], s1['limitUsd']) == (1518, 0.6306)
    assert (s2['rawSeconds'], s2['limitUsd']) == (4836, 1.2824)
    assert all(r['reservePairs'] == 1 for r in s1['rows'] + s2['rows'])
    # Leg-2 replay pair grows to 24 B pairs on both rows with reserve=1, prelude=True.
    leg2_rows = [dict(row('offline'), provider='azure', region='eastus2'),
                 dict(row('tcp'), provider='azure', region='eastus2')]
    for lr in leg2_rows:
        lr.pop('aPairs', None); lr.pop('bPairs', None)
    leg2_plan, = timing.size_sessions(leg2_rows, 'release', probe=probe_rule(), prelude=True, reserve=1)
    assert all(r['bPairs'] == 24 and r['reservePairs'] == 1 for r in leg2_plan['rows'])
    # Smoke row gets NO reservePairs.
    smoke_r = dict(row('offline'), provider='azure', region='eastus2', aPairs=5, bPairs=2, expected='smoke')
    smoke_plan, = timing.size_sessions([smoke_r], 'release', probe=probe_rule(), prelude=True, reserve=1)
    assert 'reservePairs' not in smoke_plan['rows'][0]
    # size_sessions without reserve keeps today's plans (no reservePairs).
    plain, = timing.size_sessions([dict(row('offline'), aPairs=16, bPairs=0, provider='azure', region='eastus2')],
                                  'change', probe=probe_rule(), prelude=True)
    assert 'reservePairs' not in plain['rows'][0]


def test_run8_d3_other_sites_finding_exit3_controls_and_repeat(tmp_path, monkeypatch):
    # Finding in non-noise report: finding line printed, controls_outcome unchanged, fly_sessions exits 3.
    r1 = _run8_row('time/preprod-offline', a_pairs=6, b_pairs=6, reserve=1, expected={'release': 'PASS', 'change': 'PASS'})
    r2 = _run8_row('time/preprod-tcp', a_pairs=6, b_pairs=6, reserve=1, expected={'release': 'PASS', 'change': 'PASS'})
    plan = dict(design='full', mode='release', provider='azure', region='eastus2', rows=[r1, r2], counts=[180, 60],
                destroyGuardSeconds=2494, limitUsd=0.6306, limitWithRepeatUsd=1.2612, repeatAllowed=True, probe=probe_rule(maxDraws=1))
    # Two triggered rows -> finding, but both rows still PASS as measured (1.25x on both sides of pair 2 -> ratio 1.0).
    ps = (_run8_pairs(r1, a_specs={(2, 'baseline'): dict(factor=1.25), (2, 'candidate'): dict(factor=1.25)})
          + _run8_pairs(r2, a_specs={(2, 'baseline'): dict(factor=1.25), (2, 'candidate'): dict(factor=1.25)}))
    d1 = tmp_path / 'controls'
    p1 = d1 / 'time/0-first.json'
    timing.session.save(p1, dict(pairs=ps, invalid=None, host={'processorCount': 8}, context={}))
    seal = dict(mode='release', provider='azure', rows=[r1, r2], timePlans=[plan], programStopUsd=40,
                canonical=False, releaseSet=[], gated={'areas': {}}, bands={})
    timing.session.save(d1 / 'time/index.json', [dict(session=0, phase='first', ids=[r1['id'], r2['id']],
                        path='time/0-first.json', sha256=bundle.sha(p1))])
    vals, lines = timing.read_results(d1, seal)
    text = '\n'.join(lines)
    assert 'outlier finding:' in text
    assert timing.session_outcome(d1, seal, 0)[0] == 'complete'
    vals_chg, _ = timing.read_results(d1, seal, mode='change')
    assert timing.controls_outcome(plan, {'release': vals, 'change': vals_chg}) == ('complete', [])
    assert timing.has_outlier_finding(vals) is True

    # Repeat session: row replaced in BOTH first and repeat -> flagged as finding, fly_sessions exits 3.
    r_rep = _run8_row('time/preprod-offline', a_pairs=6, b_pairs=6, reserve=1)
    plan_rep = dict(design='full', mode='release', provider='azure', region='eastus2', rows=[r_rep], counts=[180, 60],
                    destroyGuardSeconds=2494, limitUsd=0.6306, limitWithRepeatUsd=1.2612, repeatAllowed=True, probe=probe_rule(maxDraws=1))
    d2 = tmp_path / 'rep'
    # First: FAIL (B candidate 1.07x) + pair 2 A replaced; Repeat: PASS (B 1.0x) + pair 2 A replaced.
    ps_first = _run8_pairs(r_rep, a_specs={(2, 'baseline'): dict(factor=1.35)},
                           b_specs={(i, 'candidate'): dict(factor=1.07) for i in range(1, 8)})
    ps_repeat = _run8_pairs(r_rep, a_specs={(2, 'baseline'): dict(factor=1.35)},
                            b_specs={(i, 'candidate'): dict(factor=1.0) for i in range(1, 8)})
    pf, pr = d2 / 'time/0-first.json', d2 / 'time/0-repeat.json'
    timing.session.save(pf, dict(pairs=ps_first, invalid=None, host={'processorCount': 8}, context={}))
    timing.session.save(pr, dict(pairs=ps_repeat, invalid=None, host={'processorCount': 8}, context={}))
    seal_rep = dict(mode='release', provider='azure', rows=[r_rep], timePlans=[plan_rep], programStopUsd=40,
                    canonical=False, releaseSet=[], gated={'areas': {}}, bands={})
    timing.session.save(d2 / 'time/index.json', [
        dict(session=0, phase='first', ids=[r_rep['id']], path='time/0-first.json', sha256=bundle.sha(pf)),
        dict(session=0, phase='repeat', ids=[r_rep['id']], path='time/0-repeat.json', sha256=bundle.sha(pr)),
    ])
    vals_rep, lines_rep = timing.read_results(d2, seal_rep)
    assert vals_rep[r_rep['id']].get('outlierFinding') and 'outlier finding:' in '\n'.join(lines_rep)
    assert timing.has_outlier_finding(vals_rep) is True

    # Verify fly_sessions exits 3 (instead of 0) on a non-noise seal with an open finding.
    import gate_azure
    from types import SimpleNamespace
    d3 = tmp_path / 'fly_finding'
    d3.mkdir()
    seal_fly = dict(mode='change', provider='azure', ruleVersion=gate.stats.RULE_VERSION, rows=[r1, r2], timePlans=[dict(plan, repeatAllowed=False)],
                    programStopUsd=40, canonical=False, releaseSet=[], gated={'areas': {}}, bands={})
    gate.write_seal(d3, seal_fly)
    monkeypatch.setattr(bundle, 'checked_azure_local', lambda *_: {})
    monkeypatch.setattr(gate_azure, 'reconcile_and_pre_go', lambda *_: None)
    monkeypatch.setattr(bundle, 'azure_notice', lambda *_: {})
    monkeypatch.setattr(gate_azure, 'Ledger', lambda _: SimpleNamespace(money=lambda *_: {'goBookedUsd': 0.5, 'goAttempts': 1,
                        'programBookedUsd': 1.0, 'unsettledUsd': 0.0, 'observedPostedUsd': 0.0, 'headroomUsd': 39.0, 'deskEstimateUsd': 10.0}))
    monkeypatch.setattr(gate_azure, 'money_text', lambda *_: 'ok')
    def launch(*args, **_kw):
        timing.session.save(d3 / 'time/launch-started.json', {})
        return 0, 'fixture', None
    monkeypatch.setattr(bundle, 'launch_azure', launch)
    monkeypatch.setattr(bundle, 'verified_machine', lambda *a, **kw: dict(invalid=None, probe={'decision': 'kept'}))
    monkeypatch.setattr(bundle, 'archive_payload', lambda *a, **kw: dict(pairs=ps, invalid=None, host={'processorCount': 8}, context={}))
    assert bundle.fly_sessions(d3, seal_fly, 'go', ('first',), []) == 3


def test_run8_d3_spare_completeness_every_caller(tmp_path):
    r = _run8_row('time/micro-signing', a_pairs=6, b_pairs=0, reserve=1)
    plan = dict(design='noise-band', provider='azure', region='eastus2', rows=[r], counts=[180, 60],
                destroyGuardSeconds=2494, limitUsd=0.6306, limitWithRepeatUsd=0.6306, repeatAllowed=False,
                scheduleSeed=1, vmPrelude=True)
    import gate_schedule as schedules
    workload = schedules.opaque(1, ['workload', r['id']])
    applied = {workload: {'expectedWork': {'blocks': 1020, 'wireBytes': 0}}}
    # Fix wireBytes=0 on observations to match expectedWork.
    def make_ps():
        ps = _run8_pairs(r)
        for p in ps:
            for s in ('baseline', 'candidate'):
                for st_key in ('warmupRows', 'rows'):
                    for it in p[s]['data'][st_key]:
                        it['wireBytes'] = 0
        return ps

    for defect, expect in (
        ('missing', 'missing declared pairs'),
        ('identity', 'identity mismatch'),
        ('counts', 'count mismatch'),
        ('work', 'Fixed work differs'),
    ):
        d = tmp_path / defect
        d.mkdir()
        ps = make_ps()
        if defect == 'missing':
            ps = [p for p in ps if p['pair'] != 7]
        elif defect == 'identity':
            ps[-1]['baseline']['identity']['commit'] = 'f' * 40
        elif defect == 'counts':
            ps[-1]['baseline']['data']['rows'].pop()
            ps[-1]['baseline']['data']['passes'] -= 1
        elif defect == 'work':
            ps[-1]['baseline']['data']['rows'][0]['wireBytes'] = 99
        payload = dict(pairs=ps, invalid=None, applied=applied, host={'processorCount': 8}, context={})
        seal = dict(mode='release', provider='azure', noiseRecords=True, platform={'size': 'Standard_F8als_v7'}, timePlans=[plan])
        gate.write_seal(d, seal)
        p_file = d / 'time/0-first.json'
        timing.session.save(p_file, payload)
        timing.session.save(d / 'time/index.json', [dict(session=0, phase='first', ids=[r['id']],
                            path='time/0-first.json', sha256=bundle.sha(p_file))])
        # azure_noise_records refuses, writes rows file, writes NO records file.
        try:
            bundle.azure_noise_records(d, seal, payload, 0)
        except ValueError as err:
            assert 'Noise band did not pass' in str(err)
        else:
            raise AssertionError('Expected refusal for ' + defect)
        assert not (d / 'time/0-noise-records.json').exists()
        assert (d / 'time/0-noise-rows.json').is_file()
        row_doc = json.loads((d / 'time/0-noise-rows.json').read_text())['rows'][0]
        assert row_doc['status'] == 'INCONCLUSIVE' and expect in row_doc['reason']
        # read_results also reports INCONCLUSIVE with the exact reason.
        _, lines = timing.read_results(d, seal)
        assert expect in '\n'.join(lines)

    # baseline_band called without aside pairs for a row with reservePairs is refused.
    clean_ps = make_ps()
    no_aside = timing.baseline_band(r, [p for p in clean_ps if p['pair'] <= 6])
    assert no_aside['status'] == 'INCONCLUSIVE' and 'missing declared pairs' in no_aside['reason']

    # Invalid payload passed to read_results gets no outlier entry and does not raise.
    d_inv = tmp_path / 'inv'
    d_inv.mkdir()
    p_inv = d_inv / 'time/0-first.json'
    timing.session.save(p_inv, dict(pairs=[], invalid='session invalid: fixture', host={'processorCount': 8}, context={}))
    seal_inv = dict(mode='change', provider='azure', timePlans=[dict(plan, design='full')])
    timing.session.save(d_inv / 'time/index.json', [dict(session=0, phase='first', ids=[r['id']],
                        path='time/0-first.json', sha256=bundle.sha(p_inv))])
    vals_inv, lines_inv = timing.read_results(d_inv, seal_inv)
    assert 'outlier' not in vals_inv[r['id']] and 'outlier:' not in '\n'.join(lines_inv)

    # Malformed process record in row B blocks replacement in row A (which has a valid trigger).
    r_a = _run8_row('time/micro-a', a_pairs=6, b_pairs=0, reserve=1)
    r_b = _run8_row('time/micro-b', a_pairs=6, b_pairs=0, reserve=1)
    plan_2 = dict(design='noise-band', provider='azure', region='eastus2', rows=[r_a, r_b], counts=[180, 60])
    ps_a = _run8_pairs(r_a, a_specs={(2, 'baseline'): dict(factor=1.35)})
    ps_b = _run8_pairs(r_b)
    ps_b[0]['candidate']['data']['rows'] = []  # malformed
    dec_2 = timing.session_outliers(plan_2, ps_a + ps_b)
    assert dec_2[r_b['id']]['evaluated'] is False and dec_2[r_a['id']]['replaced'] is False


def test_run8_d3_phases_compile_share_and_re_verdict(tmp_path):
    r = _run8_row('time/preprod-offline', a_pairs=6, b_pairs=6, reserve=1)
    big_jit = 1_000_000_000
    # Phase A spare JIT affects ONLY phase A compile share; phase B spare JIT affects ONLY phase B.
    ps_a_jit = _run8_pairs(r, a_specs={(7, 'baseline'): dict(jit_ticks=big_jit)})
    keep_a, aside_a = timing.judged(r, ps_a_jit, None)
    res_a = timing.verdict(r, keep_a, 'release', jit=aside_a)
    assert set(res_a['qualificationFailures']) == {'A phase quiescence'}

    ps_b_jit = _run8_pairs(r, b_specs={(7, 'baseline'): dict(jit_ticks=big_jit)})
    keep_b, aside_b = timing.judged(r, ps_b_jit, None)
    res_b = timing.verdict(r, keep_b, 'release', jit=aside_b)
    assert set(res_b['qualificationFailures']) == {'B baseline quiescence'}

    # baseline_band counts phase A aside pair and ignores phase B aside pair.
    r_noise = _run8_row('time/preprod-offline', a_pairs=6, b_pairs=0, reserve=1)
    ps_both = _run8_pairs(r, b_specs={(7, 'baseline'): dict(jit_ticks=big_jit)})
    a_keep = [p for p in ps_both if p['phase'] == 'A' and p['pair'] <= 6]
    a_and_b_aside = [p for p in ps_both if p['pair'] == 7]
    assert timing.baseline_band(r_noise, a_keep, jit=a_and_b_aside)['status'] == 'PASS'

    # Alternate-mode re-verdict passes jit=first_aside:
    # (1) valid release FAIL + repeat re-reported in change mode succeeds (does not fail completeness).
    plan = dict(design='full', mode='release', provider='azure', region='eastus2', rows=[r], counts=[180, 60],
                destroyGuardSeconds=2494, limitUsd=0.6306, limitWithRepeatUsd=1.2612, repeatAllowed=True)
    d_ok = tmp_path / 're_ok'
    ps_f = _run8_pairs(r, b_specs={(i, 'candidate'): dict(factor=1.07) for i in range(1, 8)})
    ps_r = _run8_pairs(r, b_specs={(i, 'candidate'): dict(factor=1.0) for i in range(1, 8)})
    pf, pr = d_ok / 'time/0-first.json', d_ok / 'time/0-repeat.json'
    timing.session.save(pf, dict(pairs=ps_f, invalid=None, host={'processorCount': 8}, context={}))
    timing.session.save(pr, dict(pairs=ps_r, invalid=None, host={'processorCount': 8}, context={}))
    seal = dict(mode='release', provider='azure', timePlans=[plan])
    timing.session.save(d_ok / 'time/index.json', [
        dict(session=0, phase='first', ids=[r['id']], path='time/0-first.json', sha256=bundle.sha(pf)),
        dict(session=0, phase='repeat', ids=[r['id']], path='time/0-repeat.json', sha256=bundle.sha(pr)),
    ])
    vals_chg, _ = timing.read_results(d_ok, seal, mode='change')
    assert 'repeat' in vals_chg[r['id']]

    # (2) when first's aside pair alone puts A compile share > 0.005, sealed status is INCONCLUSIVE,
    # so alternate-mode re-verdict MUST agree and reject the repeat as 'Only a failing row may repeat'.
    d_bad = tmp_path / 're_bad'
    ps_f_jit = _run8_pairs(r, a_specs={(7, 'baseline'): dict(jit_ticks=big_jit)},
                           b_specs={(i, 'candidate'): dict(factor=1.07) for i in range(1, 8)})
    pf2, pr2 = d_bad / 'time/0-first.json', d_bad / 'time/0-repeat.json'
    timing.session.save(pf2, dict(pairs=ps_f_jit, invalid=None, host={'processorCount': 8}, context={}))
    timing.session.save(pr2, dict(pairs=ps_r, invalid=None, host={'processorCount': 8}, context={}))
    timing.session.save(d_bad / 'time/index.json', [
        dict(session=0, phase='first', ids=[r['id']], path='time/0-first.json', sha256=bundle.sha(pf2)),
        dict(session=0, phase='repeat', ids=[r['id']], path='time/0-repeat.json', sha256=bundle.sha(pr2)),
    ])
    try:
        timing.read_results(d_bad, seal, mode='change')
    except ValueError as err:
        assert 'Only a failing row may repeat' in str(err)
    else:
        raise AssertionError('Expected re-verdict to see aside-pair compile-share failure')


def _rule_c_row(rid='time/micro-hashing', *, status='INCONCLUSIVE', declared=16, band=.018,
                ratios=None, jit_ms=5.0, body_ms=100_000.0, stab_ratios=None,
                qual_failures=None, outlier=None, checks=None):
    if ratios is None:
        ratios = [1.0 + (0.004 if i % 2 == 0 else -0.004) for i in range(declared)]
    if stab_ratios is None:
        stab_ratios = [1.0 + (0.003 if i % 2 == 0 else -0.003) for i in range(declared)]
    if checks is None:
        checks = [dict(
            phase='B', side='baseline',
            quiescence=dict(summedJitMs=jit_ms, summedMeasuredBodyMs=body_ms,
                            jitFraction=jit_ms / body_ms if body_ms else None, established=body_ms > 0 and jit_ms / body_ms <= .005),
            stability=[dict(groups=[181, 201], pairedRatios=list(stab_ratios)),
                       dict(groups=[201, 221], pairedRatios=list(stab_ratios)),
                       dict(groups=[181, 221], pairedRatios=list(stab_ratios))],
        )]
    return dict(
        id=rid, status=status, reason='fixture', admittedPairs=len(ratios), declaredPairs=declared,
        band=band, mean=1.0, confidence95=[1.0 - (band or 0), 1.0 + (band or 0)] if band is not None else None,
        qualificationFailures=dict(qual_failures or {}),
        compileShares={}, minMeasuredBodyMs=1200.0,
        outlier=outlier if outlier is not None else dict(finding=False, replaced=False),
        pairedRatios=list(ratios), checks=checks,
    )


def test_c3rc_d1_rule_c_pass_and_frozen_band():
    assert timing.RULE_C_BAND == .04
    first_ratios = [1.20, 1.20] + [1.001 if i % 2 == 0 else 0.999 for i in range(14)]
    first = _rule_c_row(status='INCONCLUSIVE', band=.062, ratios=first_ratios)
    second_ratios = [1.005 if i % 2 == 0 else 0.995 for i in range(16)]
    second = _rule_c_row(status='PASS', band=.007, ratios=second_ratios)
    res = timing.rule_c(first, second)
    assert res['eligible'] is True and res['status'] == 'PASS' and res['flagged'] is False
    assert res['pairs'] == 16 and res['remeasureBand'] == .007
    assert res['pooledBand'] < .05 and res['frozenBand'] == max(res['remeasureBand'], res['pooledBand'])
    assert res['pooledStabilityOk'] is True and res['pooledCompileShare'] <= .005


def test_c3rc_d1_rule_c_band_boundaries():
    # Re-measure alone at 4.2% with pooled < 5% -> REFUSED.
    first = _rule_c_row(status='INCONCLUSIVE', band=.055, ratios=[1.001 if i % 2 == 0 else 0.999 for i in range(16)])
    second_42 = _rule_c_row(status='PASS', band=.042, ratios=[1.01 if i % 2 == 0 else 0.99 for i in range(16)])
    r1 = timing.rule_c(first, second_42)
    assert r1['status'] == 'REFUSED' and r1['frozenBand'] is None and r1['pooledBand'] < .05
    # Pooled >= 5% with clean re-measure < 4% -> REFUSED.
    first_wide = _rule_c_row(status='INCONCLUSIVE', band=.15, ratios=[1.16 if i % 2 == 0 else 0.86 for i in range(16)])
    second_clean = _rule_c_row(status='PASS', band=.008, ratios=[1.002 if i % 2 == 0 else 0.998 for i in range(16)])
    r2 = timing.rule_c(first_wide, second_clean)
    assert r2['status'] == 'REFUSED' and r2['frozenBand'] is None and r2['pooledBand'] >= .05


def test_c3rc_d1_rule_c_compile_share_and_stability():
    # Run 7 compile-share pattern (0.049 in first) keeps pooled > 0.005 -> REFUSED.
    first_jit = _rule_c_row(status='INCONCLUSIVE', band=.02, jit_ms=4900.0, body_ms=100_000.0)
    second_clean = _rule_c_row(status='PASS', band=.01, jit_ms=10.0, body_ms=100_000.0)
    r_jit = timing.rule_c(first_jit, second_clean)
    assert r_jit['status'] == 'REFUSED' and r_jit['frozenBand'] is None and r_jit['pooledCompileShare'] > .005
    # Pooled stability outside [0.95, 1.05] on one side -> REFUSED.
    first_stab = _rule_c_row(status='INCONCLUSIVE', band=.02, stab_ratios=[1.12] * 16)
    r_stab = timing.rule_c(first_stab, second_clean)
    assert r_stab['status'] == 'REFUSED' and r_stab['frozenBand'] is None and r_stab['pooledStabilityOk'] is False


def test_c3rc_d1_rule_c_exact_t_formula_and_k_min():
    import math
    import statistics as st
    from summarize import t95
    vals32 = [1.0 + (0.015 if i % 2 == 0 else -0.010) for i in range(32)]
    ci32 = timing._rule_c_ci(vals32, 16)
    mean32, sd32 = st.mean(vals32), st.stdev(vals32)
    want32 = [mean32 - t95(15) * sd32 / math.sqrt(16), mean32 + t95(15) * sd32 / math.sqrt(16)]
    wrong32 = [mean32 - t95(31) * sd32 / math.sqrt(32), mean32 + t95(31) * sd32 / math.sqrt(32)]
    assert abs(ci32[0] - want32[0]) < 1e-12 and abs(ci32[1] - want32[1]) < 1e-12
    assert abs(ci32[1] - wrong32[1]) > 1e-3
    # Q4(a): empty first + 15 admitted second pins k = min(len(values), n) = 15.
    vals15 = [1.0 + (0.012 if i % 2 == 0 else -0.008) for i in range(15)]
    ci15 = timing._rule_c_ci(vals15, 16)
    mean15, sd15 = st.mean(vals15), st.stdev(vals15)
    want15 = [mean15 - t95(14) * sd15 / math.sqrt(15), mean15 + t95(14) * sd15 / math.sqrt(15)]
    wrong15 = [mean15 - t95(15) * sd15 / math.sqrt(16), mean15 + t95(15) * sd15 / math.sqrt(16)]
    assert abs(ci15[0] - want15[0]) < 1e-12 and abs(ci15[1] - want15[1]) < 1e-12
    assert abs(ci15[1] - wrong15[1]) > 1e-4


def test_c3rc_d1_rule_c_eligibility_and_flags():
    second = _rule_c_row(status='PASS', band=.01)
    # First passed -> NOT_ELIGIBLE.
    r_pass = timing.rule_c(_rule_c_row(status='PASS', band=.01), second)
    assert r_pass['eligible'] is False and r_pass['status'] == 'NOT_ELIGIBLE' and r_pass['frozenBand'] is None
    # First finding (top-level or row outlier) -> NOT_ELIGIBLE.
    r_f1 = timing.rule_c(_rule_c_row(status='INCONCLUSIVE'), second, first_finding=True)
    r_f2 = timing.rule_c(_rule_c_row(status='INCONCLUSIVE', outlier=dict(finding=True, replaced=False)), second)
    assert r_f1['status'] == 'NOT_ELIGIBLE' and r_f2['status'] == 'NOT_ELIGIBLE'
    # Both replaced -> flagged=True while still computing Rule 3 figures.
    r_both = timing.rule_c(_rule_c_row(status='INCONCLUSIVE', outlier=dict(finding=False, replaced=True)),
                           _rule_c_row(status='PASS', band=.01, outlier=dict(finding=False, replaced=True)))
    assert r_both['status'] == 'PASS' and r_both['flagged'] is True and 'both' in r_both['flagReason']
    assert r_both['pooledBand'] is not None and r_both['frozenBand'] is not None
    # Re-measure session finding -> flagged=True.
    r_sf = timing.rule_c(_rule_c_row(status='INCONCLUSIVE'), second, second_finding=True)
    assert r_sf['status'] == 'PASS' and r_sf['flagged'] is True and 'finding' in r_sf['flagReason']


def test_c3rc_d1_rule_c_empty_first_and_malformed_second():
    # Empty first pairedRatios and absent checks adds nothing; re-measure still held < 4%.
    first_empty = _rule_c_row(status='INCONCLUSIVE', band=None, ratios=[], checks=[])
    sec_ok = _rule_c_row(status='PASS', band=.012)
    sec_41 = _rule_c_row(status='PASS', band=.041)
    assert timing.rule_c(first_empty, sec_ok)['status'] == 'PASS'
    assert timing.rule_c(first_empty, sec_41)['status'] == 'REFUSED'
    # Malformed second fails closed.
    first_ok = _rule_c_row(status='INCONCLUSIVE', band=.06)
    assert timing.rule_c(first_ok, _rule_c_row(status='PASS', band=.01, ratios=[]))['status'] == 'REFUSED'
    assert timing.rule_c(first_ok, _rule_c_row(status='PASS', band=.01, ratios=[1.0] * 4))['status'] == 'REFUSED'
    assert timing.rule_c(first_ok, _rule_c_row(status='PASS', band=.01, checks=[]))['status'] == 'REFUSED'
    bad_stab = _rule_c_row(status='PASS', band=.01)
    bad_stab['checks'][0]['stability'] = []
    assert timing.rule_c(first_ok, bad_stab)['status'] == 'REFUSED'
    missing_contrast = _rule_c_row(status='PASS', band=.01)
    missing_contrast['checks'][0]['stability'].pop()
    assert timing.rule_c(first_ok, missing_contrast)['status'] == 'REFUSED'
    missing_side = _rule_c_row(status='PASS', band=.01)
    missing_side['checks'][0]['side'] = 'candidate'
    assert timing.rule_c(first_ok, missing_side)['status'] == 'REFUSED'


def test_c3rc_d1_rule_c_cli(tmp_path):
    import subprocess
    import sys
    script = Path(gate.__file__).resolve()
    def run_cli(first_doc, second_doc):
        f1, f2 = tmp_path / 'first.json', tmp_path / 'second.json'
        f1.write_text(json.dumps(first_doc))
        f2.write_text(json.dumps(second_doc))
        return subprocess.run([sys.executable, '-B', str(script), 'rule-c', str(f1), str(f2)], capture_output=True, text=True)

    pass_other = _rule_c_row('time/micro-signing', status='PASS', band=.009)
    bad_first = _rule_c_row('time/micro-hashing', status='INCONCLUSIVE', band=.06,
                            ratios=[1.15, 1.15] + [1.0] * 14)
    good_second = _rule_c_row('time/micro-hashing', status='PASS', band=.008)
    # Exit 0 when all first-refused rows PASS, none flagged, and other rows in second PASS.
    p0 = run_cli(dict(session=1, finding=False, rows=[pass_other, bad_first]),
                 dict(session=1, finding=False, rows=[pass_other, good_second]))
    assert p0.returncode == 0 and 'time/micro-hashing PASS ' in p0.stdout and 'flagged=no' in p0.stdout
    assert 'time/micro-signing' not in p0.stdout
    # Exit 3 when flagged (second finding).
    p_flag = run_cli(dict(session=1, finding=False, rows=[bad_first]),
                     dict(session=1, finding=True, rows=[good_second]))
    assert p_flag.returncode == 3 and 'flagged=yes' in p_flag.stdout
    # Exit 3 when refused (band >= 4%).
    p_ref = run_cli(dict(session=1, finding=False, rows=[bad_first]),
                    dict(session=1, finding=False, rows=[_rule_c_row('time/micro-hashing', status='PASS', band=.042)]))
    assert p_ref.returncode == 3 and 'time/micro-hashing REFUSED ' in p_ref.stdout
    # Exit 3 when zero refused rows in first file.
    p_none = run_cli(dict(session=1, finding=False, rows=[pass_other]),
                     dict(session=1, finding=False, rows=[pass_other]))
    assert p_none.returncode == 3 and 'rule-c: no refused rows in first file' in p_none.stdout
    # Exit 3 when another non-refused row in second failed (whole-session rule).
    p_other = run_cli(dict(session=1, finding=False, rows=[pass_other, bad_first]),
                      dict(session=1, finding=False, rows=[_rule_c_row('time/micro-signing', status='INCONCLUSIVE'), good_second]))
    assert p_other.returncode == 3 and 'second-session refusal: time/micro-signing' in p_other.stdout
    # Exit 2 when a first-refused row is missing from second file.
    p_miss = run_cli(dict(session=1, finding=False, rows=[bad_first]),
                     dict(session=1, finding=False, rows=[pass_other]))
    assert p_miss.returncode == 2


def test_c3rc_d6_invalid_session_descriptive_excludes_unused_spare(tmp_path):
    r = _run8_row('time/preprod-offline', a_pairs=6, b_pairs=6, reserve=1)
    plan = dict(design='full', provider='azure', region='eastus2', rows=[r], counts=[180, 60],
                destroyGuardSeconds=2574, limitUsd=0.6306, limitWithRepeatUsd=1.2612, repeatAllowed=False)
    ps = _run8_pairs(r, b_specs={(7, 'candidate'): dict(factor=1.50)})
    p_file = tmp_path / 'time/0-first.json'
    timing.session.save(p_file, dict(pairs=ps, invalid='session invalid: fixture', host={'processorCount': 8}, context={}))
    seal = dict(mode='release', provider='azure', timePlans=[plan])
    timing.session.save(tmp_path / 'time/index.json', [dict(session=0, phase='first', ids=[r['id']],
                        path='time/0-first.json', sha256=bundle.sha(p_file))])
    vals, _ = timing.read_results(tmp_path, seal)
    assert vals[r['id']]['status'] == 'INCONCLUSIVE'
    # Descriptive B mean must reflect only the 6 declared pairs (~1.0), NOT the 1.50x unused spare pair 7.
    assert abs(vals[r['id']]['mean'] - 1.0) < 0.01


def test_c3rc_q1_frozen_azure_noise_records_valid():
    import gate_prepare as prepare
    from types import SimpleNamespace
    config = prepare.azure_config(json.loads((gate.GATE / 'gated.json').read_text()))
    frozen = config['time']['noise']['Standard_F8als_v7']
    expected_ids = set(config['time']['required'])
    assert set(frozen) == expected_ids and len(frozen) == 8
    for rid, rec in frozen.items():
        assert timing.noise_band(rec, 'azure') == rec['band'] and 0 <= rec['band'] < .05
        assert rec['design'] == 'long' and rec['machine'] == 'Standard_F8als_v7'
        assert rec['counts'] == [180, 60] and rec['pairs'] == 16
        assert len(rec['sourceSeal']) == 64 and len(rec['corpusId']) == 64
    audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
    entries = json.loads((gate.GATE / 'plans/noise-records.json').read_text())
    selected = gate.select_rows(SimpleNamespace(mode='release', plan=None, only=None), config, audit, plan_entries=entries)
    rows = prepare.plan_rows([dict(item, sides=dict(baseline=side(), candidate=side())) for item in selected], config)
    assert all(r['noise'] is not None and timing.noise_band(r['noise'], 'azure') is not None for r in rows)


def test_c3rc_d6_fable_m2_session_outliers_selected_filter():
    r1 = _run8_row('time/micro-a', a_pairs=6, b_pairs=0, reserve=1)
    r2 = _run8_row('time/micro-b', a_pairs=6, b_pairs=0, reserve=1)
    plan = dict(design='noise-band', provider='azure', region='eastus2', rows=[r1, r2], counts=[180, 60])
    ps = (_run8_pairs(r1, a_specs={(2, 'baseline'): dict(factor=1.35)})
          + _run8_pairs(r2, a_specs={(4, 'candidate'): dict(factor=1.35)}))
    # Without selected, both rows trigger -> finding. With selected=[r1['id']], only r1 is evaluated -> clean swap.
    full = timing.session_outliers(plan, ps)
    assert full[r1['id']]['finding'] is True and full[r1['id']]['replaced'] is False
    sub = timing.session_outliers(plan, ps, selected=[r1['id']])
    assert set(sub) == {r1['id']} and sub[r1['id']]['finding'] is False and sub[r1['id']]['replaced'] is True
    assert sub[r1['id']]['replacement']['replacedPair'] == 2


def test_c3rc_d6_fable_m3_judged_swap_stays_in_trigger_phase():
    r = _run8_row('time/preprod-offline', a_pairs=6, b_pairs=6, reserve=1)
    plan = dict(design='full', provider='azure', region='eastus2', rows=[r], counts=[180, 60])
    # Trigger in phase A pair 2; give phase B pair 2 and phase B spare pair 7 distinct markers.
    ps = _run8_pairs(r, a_specs={(2, 'baseline'): dict(factor=1.35)},
                     b_specs={(2, 'candidate'): dict(factor=1.04), (7, 'candidate'): dict(factor=1.18)})
    dec = timing.session_outliers(plan, ps)[r['id']]
    assert dec['replaced'] is True and dec['replacement'] == dict(phase='A', replacedPair=2, sparePair=7, triggerSides=['baseline'])
    keep, aside = timing.judged(r, ps, dec)
    b_keep = {p['pair']: p for p in keep if p['phase'] == 'B'}
    b_aside = [p for p in aside if p['phase'] == 'B']
    # Phase B pair 2 must remain the original 1.04x pair; phase B spare (1.18x) must stay in aside.
    b2_mean = sum(x['nsPerBlock'] for x in b_keep[2]['candidate']['data']['rows']) / 60
    b2_base = sum(x['nsPerBlock'] for x in b_keep[2]['baseline']['data']['rows']) / 60
    assert abs(b2_mean / b2_base - 1.04) < 1e-6
    assert len(b_aside) == 1
    ba_mean = sum(x['nsPerBlock'] for x in b_aside[0]['candidate']['data']['rows']) / 60
    ba_base = sum(x['nsPerBlock'] for x in b_aside[0]['baseline']['data']['rows']) / 60
    assert abs(ba_mean / ba_base - 1.18) < 1e-6


def test_c3rc_d6_run8_opus_m2_and_neon_m2_guards():
    # opus m2(a): unadmitted (timingRejectedReasons) slow pair is excluded from sibling outlier groups.
    r = _run8_row('time/micro-a', a_pairs=6, b_pairs=0, reserve=1)
    plan = dict(design='noise-band', provider='azure', region='eastus2', rows=[r], counts=[180, 60])
    ps_rej = _run8_pairs(r, a_specs={(2, 'baseline'): dict(factor=1.50, rejected=['outside CPU'])})
    d_rej = timing.session_outliers(plan, ps_rej)[r['id']]
    assert d_rej['triggers'] == [] and d_rej['replaced'] is False

    # opus m2(c): phase B group with <3 admitted pairs skips outlier check cleanly.
    r_b2 = _run8_row('time/preprod-offline', a_pairs=0, b_pairs=5, reserve=1)
    plan_b2 = dict(design='full', provider='azure', region='eastus2', rows=[r_b2], counts=[180, 60])
    # Reject pairs 3..6 so only 2 pairs (1, 2) are admitted per side in phase B.
    specs_b2 = {(p, 'baseline'): dict(rejected=['outside CPU']) for p in range(3, 7)}
    specs_b2[(2, 'baseline')] = dict(factor=1.50)
    ps_b2 = _run8_pairs(r_b2, b_specs=specs_b2)
    d_b2 = timing.session_outliers(plan_b2, ps_b2)[r_b2['id']]
    assert d_b2['evaluated'] is True and d_b2['triggers'] == [] and d_b2['maxWholeLifeDeparture'] is None

    # opus m2(d): verdict platform check covers pairs + jit (aside spare with platform='pc' refused).
    r_jit = _run8_row('time/preprod-offline', a_pairs=0, b_pairs=5, reserve=1)
    ps_jit = _run8_pairs(r_jit)
    ps_jit[-1]['baseline']['platform'] = 'pc'
    keep_j, aside_j = timing.judged(r_jit, ps_jit, None)
    res_j = timing.verdict(r_jit, keep_j, 'change', noise=dict(band=.01, design='long', machine='Standard_F8als_v7'), jit=aside_j)
    assert res_j['status'] == 'INCONCLUSIVE' and 'PC time never gates' in res_j['reason']

    # neon m2: opposite-sign departures return 0.0 and do not trigger session_outliers.
    assert timing._whole_life_departure([0.30, -0.30, -0.30, -0.30, 0.30, 0.30, 0.30]) == 0.0
    ps_opp = _run8_pairs(r, a_specs={(3, 'baseline'): dict(warm_late=[0.70, 0.70, 0.70], meas_groups=[1.35, 1.35, 1.35])})
    d_opp = timing.session_outliers(plan, ps_opp)[r['id']]
    assert d_opp['triggers'] == [] and d_opp['replaced'] is False and d_opp['maxWholeLifeDeparture'] == 0.0
