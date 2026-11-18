"""Sealed timing designs, validity and reports. No Fly API or credential access."""
import hashlib
import datetime as dt
import json
import math
import os
import statistics as st
import time
from pathlib import Path

import gate_session as session
import gate_schedule as schedules
import gate_stats as stats
from summarize import confidence95

# Governing D24 ceiling is code, never a configurable input (coordinator ruling).
MAX_APP_SECONDS = 7200
GUARD = 7000
CUTOFF = 6600
PRICE = .3667
# Fly Started Fly Machines regional price selector, captured 2026-11-19.
# https://fly.io/docs/about/pricing/
REGIONS = dict(iad=1, ewr=1, ams=1.038461538, arn=1.038461538)
RESERVE = .03
RAW_CAP = min(CUTOFF / 1.15 - 420, GUARD / 1.15 - 720)


def require(ok, message):
    if not ok:
        raise ValueError(message)


def region_for(plan):
    region = plan.get('region', 'iad')
    require(isinstance(region, str) and region in REGIONS, 'Unknown Fly region')
    return region


def compute_price(seconds, region='iad'):
    return seconds / 3600 * PRICE * REGIONS[region]


def price(seconds, region='iad'):
    return compute_price(seconds, region) + RESERVE


def validate_limits(plan):
    guard, cutoff = plan['destroyGuardSeconds'], plan['measurementCutoffSeconds']
    require(type(guard) is int and 120 < guard <= MAX_APP_SECONDS, 'Lifetime exceeds independent7200s code cap')
    require(type(cutoff) is int and 0 < cutoff <= guard - 300, 'Invalid measurement/copy/cleanup deadlines')
    require(plan['limitUsd'] >= price(guard, region_for(plan)), 'Priced lifetime exceeds approved session price')
    require(plan['limitWithRepeatUsd'] >= plan['limitUsd'] * (2 if plan['repeatAllowed'] else 1), 'Full repeat not reserved')
    return guard, cutoff


def charges(row, ratios=None):
    declared = row.get('chargeSeconds', 0)
    require(type(declared) is int and declared >= 0, 'Invalid declared process charge')
    if row.get('replay') == 'micro':
        # Measured 1+1 dry run extrapolated to240 passes; never charge below one sec.
        return max(declared, 1, math.ceil((ratios or {}).get(row['id'], 1)))
    case = row['replay']
    if row.get('mainnet'):
        size = row['corpusBytes']
        high = 240 * ((.066 if case == 'tcp' else .032) + (5e-9 if case == 'tcp' else 3e-9) * size)
        return max(declared, math.ceil(max(high, (22 if case == 'tcp' else 13) * (ratios or {}).get(row['id'], 0))))
    return max(declared, 22 if case == 'tcp' else 13)


def size_sessions(rows, mode, *, ratios=None, option=2, budget=None):
    """Price each whole process once. Full first; D51 defaults to two sessions."""
    rows = [dict(row, chargeSeconds=charges(row, ratios)) for row in rows if row['kind'] == 'time']
    if not rows:
        return []
    require(type(budget) in (int, float) and math.isfinite(budget) and budget >= 0, 'Missing or invalid remaining budget')

    def sized(members, design, *, floor=16, fail_only=False):
        entries = [dict(row, aPairs=row.get('aPairs', 0 if mode == 'change' or fail_only and row.get('mainnet') else (floor if row.get('mainnet') else 16)),
                        bPairs=row.get('bPairs', row['pairs'] if mode == 'change' else (6 if fail_only and row.get('mainnet') else floor if row.get('mainnet') else 24 if fail_only else 16)),
                        rowClass='fail-only' if fail_only and row.get('mainnet') else row.get('rowClass', 'gated')) for row in members]
        def raw():
            return sum(2 * row['chargeSeconds'] * (row['aPairs'] + row['bPairs']) for row in entries)
        if raw() > RAW_CAP:
            return None
        if mode == 'release' and design != 'option1':
            while True:
                changed = False
                for declared, row in zip(members, entries):
                    if 'bPairs' not in declared and row['bPairs'] < (16 if fail_only and row.get('mainnet') else 24) and raw() + 4 * row['chargeSeconds'] <= RAW_CAP:
                        row['bPairs'] += 2
                        changed = True
                if not changed:
                    break
        limit = math.ceil(1.15 * (720 + raw()))
        regions = {region_for(row) for row in entries}
        require(len(regions) == 1, 'Timing session rows disagree on region')
        region = regions.pop()
        cost = price(limit, region)
        repeat = mode == 'release'
        result = dict(design=design, rows=entries, counts=[180, 60], rawSeconds=raw(),
                      estimatedSeconds=limit, destroyGuardSeconds=limit, measurementCutoffSeconds=math.ceil(1.15 * (420 + raw())),
                      limitUsd=cost, estimatedUsd=cost, repeatAllowed=repeat,
                      limitWithRepeatUsd=cost * (2 if repeat else 1))
        if any('region' in row for row in entries):
            result['region'] = region
        validate_limits(result)
        return result

    replays = [row for row in rows if row.get('replay') != 'micro']
    micros = [row for row in rows if row.get('replay') == 'micro']
    plans = []
    if replays:
        full = sized(replays, 'full')
        if full:
            plans.append(full)
        elif option == 2:
            # Pair the slow and fast workloads, as P2 §6 option2 specifies.
            groups = [[row for row in replays if (row.get('mainnet', False) != (row['replay'] == case))]
                      for case in ('tcp', 'offline')]
            plans += [sized(group, 'option2') for group in groups if group]
        elif option == 1:
            for floor in range(16, 7, -2):
                candidate = sized(replays, 'option1', floor=floor)
                if candidate:
                    plans.append(candidate)
                    break
        elif option == 3:
            plans.append(sized(replays, 'option3', floor=16, fail_only=True))
        else:
            raise ValueError('Unknown design option')
        require(plans and all(plans), 'Declared timing design does not fit; no counts silently cut')
    if micros:
        plans.append(sized(micros, 'micro'))
        require(plans[-1] is not None, 'Micro set does not fit one session; main decision required')
    require(sum(plan['limitWithRepeatUsd'] for plan in plans) <= budget, 'Full repeat reservation exceeds budget left')
    return plans


def baseline_band(row, pairs):
    transformed = [dict(pair, phase='B') for pair in pairs if pair['phase'] == 'A']
    spec = dict(row, aPairs=0, bPairs=row['aPairs'])
    result = verdict(spec, transformed, 'change', noise=dict(band=0, design='long', machine='performance-8x'))
    if result['status'] == 'PASS':
        result['aBand'] = result['band'] = max(abs(value - 1) for value in result['confidence95'])
        if result['band'] >= .05:
            result.update(status='INCONCLUSIVE', reason='One-time baseline band >=5%')
    return result


def schedule(plan):
    """Host-only sealed local ABBA blocks; no observed value changes the index."""
    schedules.verify(plan)
    return iter(plan['schedule'])


def run(root, plan, output, *, dry=False):
    root, output = Path(root), Path(output)
    output.mkdir(parents=True, exist_ok=False)
    started = time.monotonic()
    deadline = (time.time() if dry else int(os.environ['P2_MACHINE_CREATED_UNIX'])) + plan['measurementCutoffSeconds']
    records, error, predecessor = [], None, None
    host_fallback = {}
    try:
        region = dict(region=region_for(plan)) if 'region' in plan else {}
        if not dry:
            fingerprint = session.host_fingerprint()
            try:
                session.save(output / 'host.json', fingerprint)
            except OSError:
                # Preserve the optional diagnostic in the mandatory status, without paths from the error.
                host_fallback['host'] = dict(dict.fromkeys(fingerprint), error='Fingerprint write failed')
        for spec in plan['builds'].values():
            actual = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in (root / spec['client']).parent.glob('*.dll')}
            require(actual == spec['dllSha256s'], 'Every-DLL closure differs before measurement')
        for block in plan['blocks']:
            # A full local block is atomic. Odd legacy tails have two members.
            if not dry and time.time() + len(block['members']) * block['chargeSeconds'] * 1.15 > deadline:
                raise TimeoutError('budget stop: next atomic block cannot finish before cutoff')
            for identifier in block['members']:
                build = plan['members'][identifier]['build']
                spec = plan['builds'][build]
                job = dict(plan['workloads'][block['workload']], root=str(root), counts=plan['counts'],
                           timeout=max(.01, deadline - time.time()))
                for key in ('dotnet', 'server', 'capture'):
                    job[key] = str(root / job[key])
                job['environment'] = {key: str(root / value) if key in ('CHRYSALIS_CORPORA', 'CHRYSALIS_BENCHMARK_DATA', 'P1_RANGE') else value
                                      for key, value in job['environment'].items()}
                actual = dict(spec, client=str(root / spec['client']), harness=str(root / spec['harness']))
                stamp = dt.datetime.now(dt.timezone.utc).isoformat()
                record = session.member(actual, job, output / identifier, **region)
                record.update(id=identifier, build=build, predecessorBuild=predecessor, startedUtc=stamp,
                              endedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
                records.append(record)
                predecessor = build
                session.save(output / 'members.json', records)
    except (ValueError, OSError, RuntimeError, TimeoutError) as exception:
        error = str(exception)
    finally:
        session.save(output / 'session-start.json', schedules.startup_information(records))
    session.save(output / 'session-status.json', dict(completedAllDeclaredRounds=error is None, exitCode=0 if error is None else 2,
                                                    reason=error, seconds=time.monotonic() - started, dry=dry, **host_fallback))
    return error is None


def stability(processes, members=1):
    result = []
    for left, right in ((0, 1), (1, 2), (0, 2)):
        means = [[st.mean(item['nsPerBlock'] for item in record['data']['rows'][group * 20:(group + 1) * 20])
                  for record in processes] for group in (left, right)]
        check = stats.paired_ratio(*means)
        if members == 2:
            ratios = [st.mean(pair) for pair in zip(*[iter(check['pairedRatios'])] * 2, strict=True)]
            check.update(pairs=len(ratios), mean=st.mean(ratios), confidence95=confidence95(ratios), pairedRatios=ratios,
                         method='arithmetic mean of within-pair averages; two-sided Student-t95% CI')
        check['groups'] = [181 + left * 20, 181 + right * 20]
        result.append(check)
    return result


def admitted_pairs(pairs):
    return [pair for pair in pairs if all(not pair[side]['timingRejectedReasons'] and
            pair[side].get('marks') == ['MEASURED_END', 'MEASURED_START'] for side in ('baseline', 'candidate'))]


def pair_times(pairs):
    return stats.paired_ratio(*[[st.mean(item['nsPerBlock'] for item in pair[side]['data']['rows']) for pair in pairs]
                               for side in ('baseline', 'candidate')])


def complete_population(row, pairs, phase, count):
    population = [pair for pair in pairs if pair['phase'] == phase]
    require(len(population) == count and sorted(pair['pair'] for pair in population) == list(range(1, count + 1)), f'{phase} budget stop or missing declared pairs: {len(population)}/{count}')
    for pair in population:
        for side in ('baseline', 'candidate'):
            record = pair[side]
            expected = row['sides']['baseline' if phase == 'A' else side]
            require(record['identity'] == {key: expected[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')}, phase + ' ' + side + ' DLL/runtime/side identity mismatch')
            session.validate_observations(record['data'], row['replay'], expected['runtime'], (180, 60))
            for key, expected_value in row.get('expectedWork', {}).items():
                require(all(item.get(key) == expected_value for stage in ('rows', 'warmupRows') for item in record['data'][stage]), phase + ' ' + side + ' fixed work differs: ' + key)
            if row.get('microFilter'):
                require(record['data']['invocationsPerPass'] == row['job']['microInvocations'], 'Micro invocation count changed')
                require(record['data']['filter'] == row['microFilter'], 'Micro process composition changed')
    return population


def smoke_complete(row, pairs):
    if len(pairs) != row['aPairs'] + row['bPairs'] or len(admitted_pairs(pairs)) != len(pairs):
        return False
    for phase, count in (('A', row['aPairs']), ('B', row['bPairs'])):
        for pair in complete_population(row, pairs, phase, count):
            if any(pair[side]['platform'] != 'fly' for side in ('baseline', 'candidate')):
                return False
    return True


def noise_band(noise):
    if isinstance(noise, dict) and noise.get('design') == 'long' and noise.get('machine') == 'performance-8x':
        band = noise.get('band')
        if type(band) in (int, float) and 0 <= band < .05:
            return band
    return None


def verdict(row, pairs, mode, *, noise=None, invalid=None):
    """Every declared pair must complete; rejected timing still counts for JIT."""
    result = dict(status='INCONCLUSIVE', reason=invalid or 'Missing timing data', rowClass=row.get('rowClass', 'gated'))
    # Descriptive B estimate survives an invalid A/session check; never a verdict.
    try:
        result.update(pair_times(admitted_pairs([pair for pair in pairs if pair['phase'] == 'B'])))
        del result['pairedRatios']
    except (ValueError, KeyError, TypeError, ZeroDivisionError):
        pass
    if invalid:
        return result
    try:
        if row.get('expected') == 'smoke':
            result['smokeComplete'] = False
            result['smokeComplete'] = smoke_complete(row, pairs)
        require(row['bPairs'] >= 5 and (mode != 'release' or row.get('rowClass') == 'fail-only' or row['aPairs'] >= 5), 'Missing required phase counts')
        require(all(pair[side]['platform'] == 'fly' for pair in pairs for side in ('baseline', 'candidate')), 'PC time never gates')
        populations = {}
        result['admittedPairs'] = {}
        for phase, count in (('A', row['aPairs']), ('B', row['bPairs'])):
            if not count:
                continue
            population = complete_population(row, pairs, phase, count)
            admitted = admitted_pairs(population)
            result['admittedPairs'][phase] = len(admitted)
            require(len(admitted) >= 5, f'{phase} admission: {len(admitted)} admitted pairs <5; ' + '; '.join(side + ': ' + ','.join(pair[side]['timingRejectedReasons']) for pair in population for side in ('baseline', 'candidate') if pair[side]['timingRejectedReasons']))
            populations[phase] = population, admitted
        checks, failures = [], {}
        result['checks'] = checks
        for phase, (population, admitted) in populations.items():
            sides = (('phase', ('baseline', 'candidate')),) if phase == 'A' else tuple((side, (side,)) for side in ('baseline', 'candidate'))
            for label, roles in sides:
                quiet = session.side_quiescence([dict(jit=session.jit_quiet(pair[side]['data'])) for pair in population for side in roles])
                stable = stability([pair[side] for pair in admitted for side in roles], members=len(roles))
                checks.append(dict(phase=phase, side=label, quiescence=quiet, stability=stable))
                if not quiet['established']:
                    failures[f'{phase} {label} quiescence'] = f'{quiet.get("jitFraction")} >0.005'
                failed = [check for check in stable if not (.95 <= check['confidence95'][0] and check['confidence95'][1] <= 1.05)]
                if failed:
                    failures[f'{phase} {label} stability'] = 'intervals outside[0.95,1.05]: ' + json.dumps(failed)
            measurement = pair_times(admitted)
            if phase == 'A':
                band = result['aBand'] = max(abs(value - 1) for value in measurement['confidence95'])
                if band >= .05:
                    failures['A band'] = f'{band:.6f} >=0.05'
            else:
                ratios = measurement.pop('pairedRatios')
                result.update(measurement)
                result['usPerOperation'] = [st.mean(st.mean(item['nsPerBlock'] for item in pair[side]['data']['rows']) for pair in admitted) / 1000 for side in ('baseline', 'candidate')]
                result['warmupNs'] = [st.mean(pair[side]['data'].get('warmupElapsedNs', sum(item['nsPerBlock'] * item['blocks'] for item in pair[side]['data']['warmupRows'])) for pair in population) for side in ('baseline', 'candidate')]
                result['gcCounts'] = {side: {key: sum(item[key] for pair in population for item in pair[side]['data']['rows']) for key in ('gen0Collections', 'gen1Collections', 'gen2Collections') if all(key in item for pair in population for item in pair[side]['data']['rows'])} for side in ('baseline', 'candidate')}
        if 'aBand' in result:
            result['band'] = result['aBand']
        if mode == 'change':
            band = noise_band(noise)
            if band is None:
                issue = 'missing' if noise is None else 'invalid'
                failures['noise record'] = f'{issue}; requires a long performance-8x record with 0 <= N < 0.05'
            else:
                result['band'] = band
        result['qualificationFailures'] = failures
        require(not failures, '; '.join(f'{name} {detail}' for name, detail in failures.items()))
        return apply_rule(dict(result, qualified=True, pairedRatios=ratios), ratios, mode)
    except (ValueError, KeyError, TypeError, ZeroDivisionError) as error:
        return dict(result, reason=str(error))


def apply_rule(result, ratios, mode):
    require(len(ratios) >= 5, 'Missing B ratios')
    low, high = confidence95(ratios)
    threshold = 1.05 if mode == 'change' else 1
    if low > threshold:
        status, reason = 'FAIL', f'Lower bound {low:.5f} >{threshold:.2f}'
    elif mode == 'release' and result['rowClass'] == 'fail-only':
        status, reason = 'CLEAR', 'fail-only: no slowdown resolved'
    elif high <= 1.05:
        status, reason = 'PASS', 'D41 L<=1.00 and U<=1.05' if mode == 'release' else '5% tripwire'
    else:
        status, reason = 'INCONCLUSIVE', f'Upper bound {high:.5f} >1.05; interval crosses the rule'
    return dict(result, status=status, reason=reason,
                mean=st.mean(ratios), confidence95=[low, high], ratios=ratios)


def pooled(first, repeat, mode, *, sealed_first_status=None):
    if (first['status'] if sealed_first_status is None else sealed_first_status) != 'FAIL':
        raise ValueError('Only a failing row may repeat')
    for label, phase in (('First', first), ('Repeat', repeat)):
        if 'ratios' not in phase:
            return dict(status='INCONCLUSIVE', reason=f'{label} invalid: ' + phase['reason'], first=first, repeat=repeat)
    result = apply_rule(dict(rowClass=first['rowClass'], band=max(first.get('band', 0), repeat.get('band', 0))),
                        first['ratios'] + repeat['ratios'], mode)
    return dict(result, first=first, repeat=repeat)


ELIGIBLE_CHECKS = {'A band', 'A phase quiescence', 'A phase stability', 'B baseline quiescence', 'B baseline stability'}


def control_modes(plan):
    return ('release', 'change') if any('expected' in row and row.get('rowClass') != 'recorded-only'
                                        for row in plan['rows']) else (plan['mode'],)


def controls_outcome(plan, results):
    """Return the classification and the row IDs to repeat, if any."""
    modes = control_modes(plan)
    eligible, joint, failed = False, False, []
    for row in plan['rows']:
        if row.get('rowClass') == 'recorded-only':
            continue
        expected = row.get('expected', {})
        for mode in modes:
            result = results.get(mode, {}).get(row['id'], {})
            if not isinstance(result, dict):
                return 'hard-stop', []
            repeated = 'repeat' in result
            joint |= repeated
            parts = (result.get('first'), result['repeat']) if repeated else (result,)
            for part in parts:
                if not isinstance(part, dict):
                    return 'hard-stop', []
                if expected == 'smoke':
                    if (repeated or part.get('smokeComplete') is not True or part.get('status') != 'INCONCLUSIVE'
                            or part.get('reason') != 'Missing required phase counts'):
                        return 'hard-stop', []
                elif part.get('qualified') is not True:
                    checks = part.get('qualificationFailures')
                    if not isinstance(checks, dict) or not checks or not checks.keys() <= ELIGIBLE_CHECKS:
                        return 'hard-stop', []
                    eligible = True
            if isinstance(expected, dict) and mode in expected:
                compared = ([parts[0]] if parts[0].get('qualified') is True else [])
                if repeated and all(part.get('qualified') is True for part in parts):
                    compared.append(result)
                if any(part.get('status') != expected[mode] for part in compared):
                    return 'hard-stop', []
            if mode == 'release' and result.get('status') == 'FAIL':
                failed.append(row['id'])
    if eligible:
        return 'retry-eligible', []
    if not joint and plan['mode'] == 'release' and plan.get('repeatAllowed') and failed:
        return 'repeat', failed
    return 'complete', []


def session_outcome(output, seal, number, *, first_only=False, results=None):
    plan = dict(seal['timePlans'][number], mode=seal['mode'])
    results = {} if results is None else results
    for mode in control_modes(plan):
        if mode not in results:
            results[mode] = read_results(output, seal, mode=mode, first_only=first_only)[0]
    return controls_outcome(plan, results)


def read_results(output, seal, *, mode=None, first_only=False):
    """Pure re-reporting from sealed inputs plus hashed session archives."""
    import gate
    sealed_mode = seal['mode']
    applied_mode = mode or sealed_mode
    values, lines = {}, []
    plans = seal.get('timePlans', [])
    show_regions = any('region' in plan for plan in plans)
    if plans:
        home = '' if show_regions else ' iad'
        lines += [f'time · Fly performance-8x{home} · arithmetic mean paired ratios',
                  f'{"benchmark":<38} {"A/B":>7} {"band N":>8} {"ratio [95%]":>31} verdict']
    index_file = output / 'time/index.json'
    index = json.loads(index_file.read_text()) if index_file.exists() else []
    band_failures = set()
    for number, plan in enumerate(plans):
        if show_regions:
            lines.append(f'session {number + 1} · {region_for(plan)}')
        for row in plan['rows']:
            first = None
            first_evidence = None
            for phase in ('first',) if first_only else ('first', 'repeat'):
                matches = [item for item in index if item['session'] == number and item['phase'] == phase]
                require(len(matches) <= 1, 'Ambiguous time artifact')
                if not matches:
                    current = dict(status='INCONCLUSIVE', reason='No sealed Fly session')
                else:
                    item = matches[0]
                    path = (output / item['path']).resolve()
                    require(path.is_relative_to(output.resolve()) and gate.digest(path) == item['sha256'], 'Timing raw data changed')
                    payload = json.loads(path.read_text())
                    invalid = payload.get('invalid')
                    if row is plan['rows'][0]:
                        lines.append('session-start steal, first five minutes (information only): ' + json.dumps(payload.get('startup', dict(available=False, reason='Not recorded in this legacy fixture')), sort_keys=True))
                        lines.append('host fingerprint (information only): ' + json.dumps(payload.get('host'), sort_keys=True))
                        lines.append('neighbours / duration / cost (context only): ' + json.dumps(payload.get('context', {}), sort_keys=True))
                    chosen = [pair for pair in payload['pairs'] if pair['id'] == row['id']]
                    current = baseline_band(row, chosen) if plan['design'] == 'micro-band' and not invalid else verdict(row, chosen, applied_mode, noise=row.get('noise'), invalid=invalid)
                    current['blockDiagnostic'] = schedules.block_diagnostics(chosen)
                    if row['id'] in band_failures:
                        current = dict(status='INCONCLUSIVE', reason='One-time micro band invalid')
                if phase == 'first':
                    first = current
                    if matches and row['id'] not in band_failures:
                        first_evidence = chosen, invalid
                elif matches and row['id'] in item['ids']:
                    require(sealed_mode == 'release' and plan['repeatAllowed'], 'Unsealed repeat')
                    sealed_status = first['status']
                    if applied_mode != sealed_mode and first_evidence is not None:
                        # Alternate reporting never grants or removes a sealed release repeat.
                        first_pairs, first_invalid = first_evidence
                        sealed_status = verdict(row, first_pairs, sealed_mode, noise=row.get('noise'), invalid=first_invalid)['status']
                    first = pooled(first, current, applied_mode, sealed_first_status=sealed_status)
            if plan['design'] == 'micro-band':
                if first['status'] != 'PASS':
                    band_failures.add(row['id'])
            else:
                values[row['id']] = first
            ci = first.get('confidence95')
            interval = f'{first["mean"]:.5f} [{ci[0]:.5f}, {ci[1]:.5f}]' if ci else '—'
            absolute = first.get('usPerOperation', first.get('first', {}).get('usPerOperation'))
            units = 'us/op' if row['replay'] == 'micro' else 'us/block'
            parts = [(label, first[label]) for label in ('first', 'repeat')] if 'repeat' in first else [('first', first)]
            bands = [part['aBand'] for _, part in parts if 'aBand' in part]
            band = f'{max(bands):.3%}' if bands else '—'
            lines.append(f'{row["id"]:<38} {row["aPairs"]:>3}/{row["bPairs"]:>3} {band:>8} {interval:>31} {first["status"]}')
            for side, identity in row['sides'].items():
                lines.append(f'  {side}: {identity["commit"]} tree {identity["treeId"]} runtime {identity["runtime"]}; corpus {row.get("corpusId", "audit-preprod")}')
            lines.append('  rule: ' + row.get('rowClass', 'gated') + ' · ' + first['reason'])
            if absolute:
                lines.append(f'  {units} baseline/candidate {absolute[0]:.3f}/{absolute[1]:.3f}; warm-up ns {first.get("warmupNs", first.get("first", {}).get("warmupNs"))} (reported, not gated)')
            if applied_mode == 'change' and plan['design'] != 'micro-band' and (noise := noise_band(row.get('noise'))) is not None:
                lines.append(f'  noise record N: {noise:.3%}')
            for label, part in parts:
                if 'repeat' in first:
                    lines.append(f'  {label}: {part["status"]} ratio {part.get("mean")} {part.get("confidence95")}; joint {first["status"]} · {part["reason"]}')
                if part.get('blockDiagnostic'):
                    lines.append('  block contrast / orientation ' + label + ' (information only): ' + json.dumps(part['blockDiagnostic'], sort_keys=True))
                lines.append(f'  {label} admitted pairs: ' + json.dumps(part.get('admittedPairs', {})) + '; GC collections: ' + json.dumps(part.get('gcCounts', {})))
                for check in part.get('checks', []):
                    classifications = [('detected drift beyond the declared band' if low > 1.05 or high < .95 else 'steady state unproved') for low, high in [item['confidence95'] for item in check['stability']] if not (.95 <= low and high <= 1.05)]
                    if classifications:
                        lines.append(f'  {label} stability: ' + '; '.join(classifications))
                    lines.append(f'  {label} check: ' + json.dumps(check, sort_keys=True))
        lines.append(f'time session {number} · {plan["design"]} · limit {plan["destroyGuardSeconds"]}s / {plan["limitUsd"]:.4f} USD · reserved with repeat {plan["limitWithRepeatUsd"]:.4f} USD')
    return values, lines
