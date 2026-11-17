"""P1 arithmetic retained; known values and each PC verdict branch."""
import math
from pathlib import Path
import json
import unittest

import gate_stats as stats
from summarize import confidence95, t95


class StatsTests(unittest.TestCase):
    def test_mean_of_paired_ratios_not_ratio_of_unpaired_means(self):
        result = stats.paired_ratio([1, 100], [2, 100])
        self.assertEqual(result['mean'], 1.5)
        self.assertAlmostEqual(result['confidence95'][1], 1.5 + t95(1) * .5)

    def test_known_student_values_and_intervals(self):
        self.assertAlmostEqual(t95(4), 2.776445, places=5)
        self.assertAlmostEqual(t95(15), 2.131450, places=5)
        low, high = confidence95([1, 2, 3, 4, 5])
        self.assertAlmostEqual(low, 1.0368, places=4)
        self.assertAlmostEqual(high, 4.9632, places=4)
        self.assertEqual(confidence95([7] * 5), [7, 7])

    def test_noise_whole_interval_and_minimum(self):
        self.assertTrue(stats.baseline_time_noise([10, 20], [10, 20], family='micro')['belowTolerance'])
        self.assertFalse(stats.baseline_time_noise([10, 20], [10, 20], family='whole')['belowTolerance'])
        result = stats.allocation_noise_band([100] * 5, [98, 99, 100, 101, 102], purpose='baseline-noise')
        self.assertAlmostEqual(result['absoluteNoiseBandBytes'], t95(4) * math.sqrt(2.5 / 5))
        for purpose, count in [('candidate', 5), ('baseline-noise', 4)]:
            with self.assertRaises(ValueError):
                stats.allocation_noise_band([100] * count, [100] * count, purpose=purpose)

    def test_rx1_frozen_interval_recomputed_from_stored_differences(self):
        # The frozen public RX1 TCP/runtime-11 observations, not candidate data.
        differences = [6.311200000000099, -1.3591999999998734, 5.8023999999995794, 6.163199999999961, -.6768000000001848]
        result = stats.allocation_noise_band([0] * 5, differences, purpose='baseline-noise')
        self.assertAlmostEqual(result['absoluteNoiseBandBytes'], 8.098478935320633)
        # Committed bands remain independently re-derivable without local evidence paths.
        root = Path(__file__).parents[1] / 'gate/bands'
        for path in root.glob('*.json'):
            frozen = json.loads(path.read_text())
            differences = frozen['pairedDifferencesBytes']
            self.assertEqual(len(differences), 10)
            self.assertEqual((frozen['pairs'], frozen['purpose']), (10, 'baseline-noise'))
            self.assertAlmostEqual(frozen['absoluteNoiseBandBytes'], max(map(abs, confidence95(differences))))


    def test_invalid_pairs_never_become_zeros(self):
        for left, right in [([1], []), ([0, 1], [1, 1]), ([1, 1], [1, float('nan')])]:
            with self.subTest(left=left), self.assertRaises(ValueError):
                stats.paired_ratio(left, right)

    def verdict(self, left, right, **kwargs):
        return stats.bytes_verdict(left, right, mode=kwargs.pop('mode', 'change'), kind=kwargs.pop('kind', 'D39'),
                                   batches=kwargs.pop('batches', [left, right]), **kwargs)

    def test_d39_equal_positive_upper_equal_nonpositive_higher_lower(self):
        for mode in ('change', 'release'):
            with self.subTest(mode=mode):
                self.assertEqual(self.verdict([10] * 5, [10, 10, 10, 10, 11], mode=mode)['status'], 'INCONCLUSIVE')
                self.assertEqual(self.verdict([10] * 5, [10] * 5, mode=mode)['status'], 'PASS')
                self.assertEqual(self.verdict([10] * 5, [11] * 5, mode=mode)['status'], 'FAIL')
                result = self.verdict([10] * 5, [9] * 5, mode=mode)
                self.assertEqual((result['status'], result['reason']), ('PASS', 'lower'))
                self.assertEqual(self.verdict([10] * 5, [9, 9, 9, 9, 14], mode=mode)['status'], 'INCONCLUSIVE')

    def test_modal_below_does_not_hide_resolved_increase(self):
        result = self.verdict([10] * 5, [12] * 5, batches=[[10] * 25, [9] * 25])
        self.assertEqual(result['status'], 'FAIL')
        self.assertEqual(self.verdict([10] * 5, [10] * 5, batches=[[1, 2], [1, 2]])['status'], 'INCONCLUSIVE')

    def test_band_change_release_and_no_band(self):
        for kind in ('range', 'replay'):
            for right, expected in [(12, 'PASS'), (14, 'FAIL')]:
                self.assertEqual(self.verdict([10] * 5, [right] * 5, kind=kind, band=3)['status'], expected)
            self.assertEqual(self.verdict([10] * 5, [12] * 5, kind=kind, band=3, mode='release')['status'], 'FAIL')
            self.assertEqual(self.verdict([10] * 5, [10] * 5, kind=kind, band=3, mode='release')['status'], 'PASS')
            self.assertEqual(self.verdict([10] * 5, [10] * 5, kind=kind)['reason'], 'no band')
        self.assertEqual(self.verdict([10] * 5, [10] * 5, kind='replay', band=3, mode='release', plateau=False)['reason'], 'no plateau')

    def test_pair_minimums_in_every_mode(self):
        for mode in ('change', 'release'):
            for kind, count in [('D39', 2), ('range', 2), ('replay', 4)]:
                self.assertEqual(self.verdict([10] * count, [10] * count, kind=kind, band=3, mode=mode)['status'], 'INCONCLUSIVE')

    def test_plateau_checks_outer_as_well_as_adjacent(self):
        series = [[0] * 20 + [2] * 20 + [4] * 20 for _ in range(5)]
        result = stats.allocation_plateau(series, 3)
        self.assertEqual([row['outsideBand'] for row in result['checks']], [False, False, True])
        self.assertFalse(result['established'])
        self.assertTrue(stats.allocation_plateau([[0] * 60 for _ in range(5)], 0)['established'])
        self.assertEqual(stats.allocation_plateau([[0] * 25 for _ in range(5)], 1, short=True)['groups'], [[11, 15], [16, 20], [21, 25]])

    def test_one_repeat_is_pooled_not_winner_selected(self):
        first = self.verdict([10] * 5, [12] * 5, kind='range', band=1, mode='release')
        joint = self.verdict([10] * 10, [12] * 5 + [8] * 5, kind='range', band=2, mode='release')
        self.assertEqual(first['status'], 'FAIL')
        self.assertEqual(joint['status'], 'PASS')
        self.assertEqual(joint['pairs'], 10)
        self.assertEqual(self.verdict([10] * 10, [12] * 10, kind='range', band=2, mode='release')['status'], 'FAIL')

    def test_seeded_pc_controls(self):
        self.assertEqual(self.verdict([0] * 5, [48] * 5)['status'], 'FAIL')  # C1
        for _control in ('C2', 'C3', 'C5'):
            self.assertEqual(self.verdict([10] * 5, [10] * 5)['status'], 'PASS')
        self.assertEqual(self.verdict([10] * 2, [10] * 2)['status'], 'INCONCLUSIVE')  # C4


class ReviewRegressionTests(unittest.TestCase):
    verdict = StatsTests.verdict
    # Reuse the established verdict helper without duplicating its arithmetic.
    def test_d39_upper_zero_and_positive_epsilon(self):
        from unittest.mock import patch
        for high, expected in [(0, 'PASS'), (.000001, 'INCONCLUSIVE')]:
            with patch.object(stats, 'paired_difference', return_value=dict(meanDifferenceBytes=-1, confidence95DifferenceBytes=[-2, high])):
                self.assertEqual(self.verdict([10] * 5, [10] * 5)['status'], expected)

    def test_modal_higher_fails_even_when_mean_interval_is_negative(self):
        self.assertEqual(self.verdict([10] * 5, [9] * 5, batches=[[10] * 25, [11] * 25])['status'], 'FAIL')

    def test_band_upper_boundary_is_not_one_and_a_half_bands(self):
        from unittest.mock import patch
        for kind in ('range', 'replay'):
            for mode in ('change', 'release'):
                for high, expected in [(3, 'PASS'), (3.000001, 'INCONCLUSIVE')]:
                    with self.subTest(kind=kind, mode=mode, high=high), patch.object(stats, 'paired_difference', return_value=dict(meanDifferenceBytes=0, confidence95DifferenceBytes=[-1, high])):
                        self.assertEqual(self.verdict([10] * 5, [10] * 5, kind=kind, mode=mode, band=3)['status'], expected)

    def test_resolved_increase_precedes_missing_modal_band_and_plateau(self):
        for mode in ('change', 'release'):
            for batches in (None, [[1, 2], [3, 4]], [[240] * 7 + [528] * 7 + [600], [288] * 7 + [576] * 7 + [648]]):
                with self.subTest(mode=mode, batches=batches):
                    self.assertEqual(self.verdict([0] * 5, [48] * 5, mode=mode, batches=batches)['status'], 'FAIL')
        for kind in ('range', 'replay'):
            for band, plateau in ((None, True), (3, False)):
                self.assertEqual(self.verdict([0] * 5, [48] * 5, kind=kind, mode='release', band=band, plateau=plateau)['status'], 'FAIL')
            self.assertEqual(self.verdict([0] * 5, [4] * 5, kind=kind, band=3, plateau=False)['status'], 'FAIL')
            self.assertEqual(self.verdict([0] * 5, [2] * 5, kind=kind, band=3)['status'], 'PASS')

    @staticmethod
    def noisy_values():
        left, right = [288.02, 288, 288, 288, 288], [288, 288.02, 288, 288, 288]
        batches = [[288.1] + [288] * 24, [288] * 5 + [288.1] + [288] * 19]
        return left, right, batches

    def test_equal_noise_requires_matching_control_and_deviation_quantums(self):
        left, right, batches = self.noisy_values()
        control = dict(baseline=left, candidate=right, batches=batches)
        self.assertEqual(self.verdict(left, right, batches=batches)['status'], 'INCONCLUSIVE')
        for mode in ('change', 'release'):
            result = self.verdict(left, right, batches=batches, mode=mode, control=control)
            self.assertEqual((result['status'], result['reason']), ('PASS', 'equal (noise)'))
            self.assertAlmostEqual(result['noiseControl']['halfWidthRatio'], 1)
        mismatched = [[288.2] + [288] * 24, [288] * 5 + [288.2] + [288] * 19]
        result = self.verdict(left, right, batches=mismatched, control=control)
        self.assertEqual(result['status'], 'INCONCLUSIVE')
        self.assertIn('deviation', result['reason'])

    def test_equal_noise_each_precondition_boundary(self):
        from unittest.mock import patch
        left, right, batches = self.noisy_values()
        control = dict(baseline=left, candidate=right, batches=batches)
        def interval(mean=0, low=-.01, high=.01):
            return dict(pairs=5, meanDifferenceBytes=mean, confidence95DifferenceBytes=[low, high])
        cases = [
            ('accepted', interval(), interval(), 'PASS'),
            ('run-touches-zero', interval(.01, 0, .02), interval(), 'PASS'),
            ('control-touches-zero', interval(), interval(.01, 0, .02), 'PASS'),
            ('round-positive', interval(.05, -.01, .11), interval(), 'INCONCLUSIVE'),
            ('round-negative', interval(-.05, -.11, .01), interval(), 'INCONCLUSIVE'),
            ('round-inside', interval(.049, -.001, .099), interval(), 'PASS'),
            ('width-at-point-one', interval(0, -.1, .1), interval(), 'INCONCLUSIVE'),
            ('width-below-point-one', interval(0, -.099, .099), interval(), 'PASS'),
            ('control-width-at-point-one', interval(), interval(0, -.1, .1), 'INCONCLUSIVE'),
            ('control-excludes-zero', interval(), interval(.02, .01, .03), 'INCONCLUSIVE'),
            ('control-rounds-positive', interval(), interval(.05, -.01, .11), 'INCONCLUSIVE'),
            ('control-rounds-negative', interval(), interval(-.05, -.11, .01), 'INCONCLUSIVE'),
            ('ratio-at-ten', interval(), interval(0, -.001, .001), 'PASS'),
            ('ratio-over-ten', interval(), interval(0, -.000999, .000999), 'INCONCLUSIVE'),
            ('reverse-ratio-at-ten', interval(0, -.001, .001), interval(), 'PASS'),
            ('reverse-ratio-over-ten', interval(0, -.000999, .000999), interval(), 'INCONCLUSIVE'),
            ('control-zero-width', interval(), interval(0, 0, 0), 'INCONCLUSIVE'),
            ('run-excludes-zero', interval(.02, .01, .03), interval(), 'FAIL'),
        ]
        for name, run, aa, expected in cases:
            with self.subTest(name=name), patch.object(stats, 'paired_difference', side_effect=[run, aa]):
                self.assertEqual(self.verdict(left, right, batches=batches, control=control)['status'], expected)
        for bad in (dict(control, batches=[[1, 2], [1, 2]]), dict(control, batches=[[289] * 25] * 2),
                    dict(control, baseline=left[:3], candidate=right[:3])):
            self.assertEqual(self.verdict(left, right, batches=batches, control=bad)['status'], 'INCONCLUSIVE')

    def test_plateau_needs_an_interval_wholly_outside_the_band(self):
        from unittest.mock import patch
        for low, high, expected in [(-2, 2, True), (1, 1, True), (-1, -1, True),
                                    (.999, 2, True), (-2, -.999, True), (1.000001, 2, False), (-2, -1.000001, False)]:
            with self.subTest(interval=(low, high)), patch.object(stats, 'paired_difference', return_value=dict(confidence95DifferenceBytes=[low, high])):
                self.assertEqual(stats.allocation_plateau([[0] * 60 for _ in range(5)], 1)['established'], expected)

    def test_plateau_accepts_all_twelve_unchanged_band_sides(self):
        root = Path(__file__).with_name('fixtures') / 'gate-plateau-aa.json'
        controls = json.loads(root.read_text())
        self.assertEqual(len(controls), 12)
        for row in controls:
            series = [[value for mean in groups for value in [mean] * 20] for groups in row['groupMeans']]
            with self.subTest(replay=row['replay'], runtime=row['runtime'], side=row['side']):
                self.assertTrue(stats.allocation_plateau(series, row['band'])['established'])
        leak = [[block * .5 for block in range(60)] for _ in range(10)]
        self.assertFalse(stats.allocation_plateau(leak, 1)['established'])


if __name__ == '__main__':
    unittest.main()
