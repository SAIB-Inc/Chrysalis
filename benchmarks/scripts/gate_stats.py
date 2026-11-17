"""Independent-process paired statistics and PC allocation verdicts."""
import math
import statistics

from summarize import confidence95

RULE_VERSION = "p2-d49-d39-cold-v2"


def _paired(baseline, comparison):
    if len(baseline) != len(comparison) or len(baseline) < 2:
        raise ValueError("At least two complete, matched process pairs are required")
    if any(not math.isfinite(value) for value in [*baseline, *comparison]):
        raise ValueError("Non-finite measurements are inconclusive, never zeros")
    return list(zip(baseline, comparison))


def paired_ratio(baseline, comparison):
    pairs = _paired(baseline, comparison)
    if any(left <= 0 or right <= 0 for left, right in pairs):
        raise ValueError("Timing denominators and process means must be positive")
    ratios = [right / left for left, right in pairs]
    return {"pairs": len(pairs), "mean": statistics.mean(ratios), "confidence95": confidence95(ratios),
            "pairedRatios": ratios, "method": "arithmetic mean paired process ratios; two-sided Student-t95% CI"}


def paired_difference(baseline, comparison):
    pairs = _paired(baseline, comparison)
    differences = [right - left for left, right in pairs]
    return {"pairs": len(pairs), "meanDifferenceBytes": statistics.mean(differences),
            "confidence95DifferenceBytes": confidence95(differences), "pairedDifferencesBytes": differences,
            "baselineMeanBytes": statistics.mean(baseline), "comparisonMeanBytes": statistics.mean(comparison),
            "baselineConfidence95Bytes": confidence95(baseline), "comparisonConfidence95Bytes": confidence95(comparison)}


def allocation_noise_band(baseline, repeat, *, purpose):
    if purpose != "baseline-noise":
        raise ValueError("Allocation noise may be frozen only from baseline-noise, never candidate observations")
    if len(baseline) < 5:
        raise ValueError("At least 5 independent baseline process pairs are required to freeze an allocation band")
    result = paired_difference(baseline, repeat)
    result["absoluteNoiseBandBytes"] = max(abs(value) for value in result["confidence95DifferenceBytes"])
    result["observedDifferenceMinimumBytes"] = min(result["pairedDifferencesBytes"])
    result["observedDifferenceMaximumBytes"] = max(result["pairedDifferencesBytes"])
    result["purpose"] = purpose
    return result


def baseline_time_noise(baseline, repeat, *, family):
    if family not in ["micro", "whole", "replay"]:
        raise ValueError("Unknown timing family")
    result = paired_ratio(baseline, repeat)
    minimum, tolerance = (2, 0.03) if family == "micro" else (5, 0.05)
    noise = max(abs(value - 1) for value in result["confidence95"])
    enough = result["pairs"] >= minimum
    below = enough and noise < tolerance
    result.update(noise=noise, tolerance=tolerance, belowTolerance=below,
                  notGatingReason="" if below else f"Need at least {minimum} pairs" if not enough else "Noise exceeds tolerance: more runs or a quieter machine, never a wider tolerance")
    return result



def modal_batches(batches):
    if not batches or len(batches) != 2 or any(not values or any(not math.isfinite(x) for x in values) for values in batches):
        raise ValueError('Missing/invalid batch figures')
    return [statistics.multimode(values) for values in batches]


def noise_precondition(result):
    modes = result.get('modalBatches', [])
    low, high = result['confidence95DifferenceBytes']
    return (len(modes) == 2 and all(len(mode) == 1 for mode in modes) and modes[0] == modes[1]
            and round(result['meanDifferenceBytes'], 1) == 0 and low <= 0 <= high and (high - low) / 2 < .1)


def equal_noise(result, batches, control):
    """The matching baseline-only phase may explain tiny noise, never widen D39."""
    result['needsNoiseControl'] = True
    if control is None:
        result['reason'] = 'equal (noise): missing matching A/A control'
        return result
    try:
        if len(control['baseline']) != result['pairs'] or len(control['candidate']) != result['pairs']:
            raise ValueError('A/A pair count differs')
        aa = paired_difference(control['baseline'], control['candidate'])
        aa['modalBatches'] = modal_batches(control['batches'])
        if not noise_precondition(aa) or aa['modalBatches'] != result['modalBatches']:
            raise ValueError('A/A fails the equal-mode, rounding, zero containment or <0.1 B half-width conditions')
        width = lambda row: (row['confidence95DifferenceBytes'][1] - row['confidence95DifferenceBytes'][0]) / 2
        widths = [width(result), width(aa)]
        if min(widths) <= 0 or max(widths) / min(widths) > 10:
            raise ValueError('A/A and A/P need nonzero half-widths within a factor of ten')
        mode = result['modalBatches'][0][0]
        deviations = lambda sides: {abs(value - mode) for values in sides for value in values if value != mode}
        observed, explained = deviations(batches), deviations(control['batches'])
        if not observed <= explained:
            raise ValueError('A/P non-modal batch deviation absent from A/A control')
        aa.update(halfWidthRatio=max(widths) / min(widths), observedDeviations=sorted(observed), controlDeviations=sorted(explained))
        result.update(status='PASS', reason='equal (noise)', noiseControl=aa)
    except (ValueError, KeyError, TypeError) as error:
        result['reason'] = 'equal (noise): ' + str(error)
    return result


def bytes_verdict(baseline, candidate, *, mode, kind, band=None, batches=None, plateau=True, control=None):
    """Process means are independent units; batches only determine the D39 modal."""
    if mode not in ('change', 'release') or kind not in ('D39', 'range', 'replay'):
        raise ValueError('Unknown bytes rule')
    minimum = 5 if kind == 'replay' else 3
    if len(baseline) != len(candidate) or len(baseline) < minimum:
        return dict(status='INCONCLUSIVE', reason=f'Need at least {minimum} complete pairs', pairs=min(len(baseline), len(candidate)))
    try:
        result = paired_difference(baseline, candidate)
    except ValueError as error:
        return dict(status='INCONCLUSIVE', reason=str(error))
    low, high = result['confidence95DifferenceBytes']
    result.update(status='INCONCLUSIVE', reason='Interval crosses limit', limit=0 if kind == 'D39' else band)
    # Resolved increases do not need a modal, plateau or band to fail at release.
    threshold = 0 if kind == 'D39' or mode == 'release' else band
    if threshold is not None and low > threshold:
        return dict(result, status='FAIL', reason='Interval wholly above zero' if threshold == 0 else 'Interval wholly above band')
    if kind == 'D39':
        try:
            modes = result['modalBatches'] = modal_batches(batches)
        except ValueError as error:
            return dict(result, reason=str(error))
        if any(len(values) != 1 for values in modes):
            result['reason'] = 'No unique modal batch figure'
        else:
            left, right = [values[0] for values in modes]
            if right > left:
                result.update(status='FAIL', reason='Modal higher')
            elif right <= left and high <= 0:
                result.update(status='PASS', reason='lower' if right < left else 'equal')
            elif noise_precondition(result):
                return equal_noise(result, batches, control)
        return result
    if band is None:
        result['reason'] = 'no band'
    elif not math.isfinite(band) or band < 0:
        raise ValueError('Invalid band')
    elif kind == 'replay' and mode == 'release' and not plateau:
        result['reason'] = 'no plateau'
    elif high <= band and (mode == 'change' or low <= 0):
        result.update(status='PASS', reason='Within band')
    return result


def allocation_plateau(series, band, *, short=False):
    groups = [(10, 15), (15, 20), (20, 25)] if short else [(0, 20), (20, 40), (40, 60)]
    if band is None or len(series) < 5 or any(len(rows) < groups[-1][1] for rows in series):
        return dict(established=False, checks=[], reason='Missing plateau data or band')
    checks = []
    for left, right in ((0, 1), (1, 2), (0, 2)):
        a, b = groups[left], groups[right]
        baseline = [statistics.mean(rows[a[0]:a[1]]) for rows in series]
        candidate = [statistics.mean(rows[b[0]:b[1]]) for rows in series]
        result = paired_difference(baseline, candidate)
        low, high = result['confidence95DifferenceBytes']
        result['outsideBand'] = high < -band or low > band
        checks.append(result)
    return dict(established=not any(row['outsideBand'] for row in checks), checks=checks,
                groups=[[low + 1, high] for low, high in groups])
