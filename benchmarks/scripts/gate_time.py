"""Sealed timing designs, validity and reports. No Fly API or credential access."""
import hashlib
import datetime as dt
from decimal import Decimal
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
PROVIDERS = {
    'fly': dict(platform='fly', machine='performance-8x', price=PRICE, regions=REGIONS, fullDraws=('iad',), tail=0),
    'azure': dict(platform='azure', machine='Standard_F8als_v7', price=.495, regions=dict(eastus2=1, eastus=1),
                  fullDraws=('eastus2', 'eastus'), tail=900, image='Canonical:ubuntu-24_04-lts:server:24.04.202611170'),
}
RAW_CAP = min(CUTOFF / 1.15 - 420, GUARD / 1.15 - 720)
PROBE_COUNTS = (180, 60)
PROBE_PROCESS_SECONDS = 13
GC_KEYS = ('gen0Collections', 'gen1Collections', 'gen2Collections')


def require(ok, message):
    if not ok:
        raise ValueError(message)


require({item['platform'] for item in PROVIDERS.values()} == session.MEASURING_HOSTS, 'Provider and measuring-host sets differ')


def provider_for(plan):
    key = plan.get('provider', 'fly')
    require(isinstance(key, str) and key in PROVIDERS, 'Unknown timing provider')
    return PROVIDERS[key]


def region_for(plan):
    regions = provider_for(plan)['regions']
    region = plan.get('region', next(iter(regions)))
    require(isinstance(region, str) and region in regions, 'Unknown timing region')
    return region


def compute_price(seconds, region='iad', *, provider='fly'):
    spec = PROVIDERS[provider]
    return seconds / 3600 * spec['price'] * spec['regions'][region]


def azure_booking(seconds):
    minutes = math.ceil(seconds / 60)
    buckets = math.ceil(minutes / 60) + 1  # any possible hour overlap
    cost = (Decimal(str(PROVIDERS['azure']['price'])) * (minutes + 1) / 60 + Decimal('.0244') * buckets
            + Decimal('.075') * max(1, math.ceil(seconds / 4500)) + Decimal('.02'))
    return float(cost.quantize(Decimal('.0001'), rounding='ROUND_UP'))


def price(seconds, region='iad', *, provider='fly'):
    if provider == 'azure':
        require(region in PROVIDERS['azure']['regions'], 'Unknown Azure region')
        return azure_booking(seconds + PROVIDERS['azure']['tail'])
    return compute_price(seconds, region, provider=provider) + RESERVE


def plan_price(plan):
    return price(plan['destroyGuardSeconds'], region_for(plan), provider=plan.get('provider', 'fly'))


def validate_limits(plan):
    guard, cutoff = plan['destroyGuardSeconds'], plan['measurementCutoffSeconds']
    require(type(guard) is int and 120 < guard <= MAX_APP_SECONDS, 'Lifetime exceeds independent7200s code cap')
    require(type(cutoff) is int and 0 < cutoff <= guard - 300, 'Invalid measurement/copy/cleanup deadlines')
    require(plan['limitUsd'] >= plan_price(plan), 'Priced lifetime exceeds approved session price')
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


def validate_probe_rule(rule):
    require(isinstance(rule, dict) and rule.get('statistic') in ('cv', 'lag1') and
            type(rule.get('limit')) in (int, float) and -1 <= rule['limit'] <= 1 and
            (rule['statistic'] != 'cv' or rule['limit'] >= 0) and
            type(rule.get('processes')) is int and rule['processes'] == 16 and
            type(rule.get('maxDraws')) is int and 1 <= rule['maxDraws'] <= 6 and
            type(rule.get('gen0BudgetBytes')) is int and rule['gen0BudgetBytes'] > 0, 'Missing or invalid probe rule')
    return rule


def probe_ids(rule):
    return tuple(f'probe-{number:02}' for number in range(1, rule['processes'] + 1))


def probe_seconds(rule):
    return 0 if rule is None else rule['processes'] * PROBE_PROCESS_SECONDS


def finalize_plan(plan, raw):
    probe = plan.get('probe')
    probe_raw = probe_seconds(probe)
    raw += probe_raw
    require(raw <= RAW_CAP, 'Declared timing work including probe does not fit')
    guard = math.ceil(1.15 * (720 + raw))
    region = region_for(plan)
    provider = plan.get('provider', 'fly')
    cost = price(guard, region, provider=provider)
    reservation = cost + ((probe['maxDraws'] - 1) * price(math.ceil(1.15 * (720 + probe_raw)), region) if probe and provider == 'fly' else 0)
    plan.update(rawSeconds=raw, estimatedSeconds=guard, destroyGuardSeconds=guard,
                measurementCutoffSeconds=math.ceil(1.15 * (420 + raw)), estimatedUsd=cost,
                limitUsd=reservation, limitWithRepeatUsd=reservation * (2 if plan['repeatAllowed'] else 1))
    validate_limits(plan)
    return plan


def size_sessions(rows, mode, *, ratios=None, option=2, budget=None, probe=None):
    """Price each whole process once. Full first; D51 defaults to two sessions."""
    rows = [dict(row, chargeSeconds=charges(row, ratios)) for row in rows if row['kind'] == 'time']
    if not rows:
        return []
    providers = {row.get('provider', 'fly') for row in rows}
    require(len(providers) == 1, 'Timing rows disagree on provider')
    provider = providers.pop()
    require(provider == 'azure' or type(budget) in (int, float) and math.isfinite(budget) and budget >= 0, 'Missing or invalid remaining budget')

    row_cap = RAW_CAP - probe_seconds(probe)

    def sized(members, design, *, floor=16, fail_only=False):
        entries = [dict(row, aPairs=row.get('aPairs', 0 if mode == 'change' or fail_only and row.get('mainnet') else (floor if row.get('mainnet') else 16)),
                        bPairs=row.get('bPairs', row['pairs'] if mode == 'change' else (6 if fail_only and row.get('mainnet') else floor if row.get('mainnet') else 24 if fail_only else 16)),
                        rowClass='fail-only' if fail_only and row.get('mainnet') else row.get('rowClass', 'gated')) for row in members]
        def raw():
            return sum(2 * row['chargeSeconds'] * (row['aPairs'] + row['bPairs']) for row in entries)
        if raw() > row_cap:
            return None
        if mode == 'release' and design != 'option1':
            while True:
                changed = False
                for declared, row in zip(members, entries):
                    if 'bPairs' not in declared and row['bPairs'] < (16 if fail_only and row.get('mainnet') else 24) and raw() + 4 * row['chargeSeconds'] <= row_cap:
                        row['bPairs'] += 2
                        changed = True
                if not changed:
                    break
        regions = {region_for(row) for row in entries}
        require(len(regions) == 1, 'Timing session rows disagree on region')
        region = regions.pop()
        result = dict(design=design, rows=entries, counts=list(PROBE_COUNTS), repeatAllowed=mode == 'release')
        if provider != 'fly':
            result['provider'] = provider
        if provider != 'fly' or any('region' in row for row in entries):
            result['region'] = region
        if probe is not None:
            result['probe'] = dict(probe, maxDraws=probe['maxDraws'] if region in provider_for(result)['fullDraws'] else min(4, probe['maxDraws']))
        return finalize_plan(result, raw())

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
    require(provider == 'azure' or sum(plan['limitWithRepeatUsd'] for plan in plans) <= budget, 'Full repeat reservation exceeds budget left')
    return plans


def baseline_band(row, pairs):
    transformed = [dict(pair, phase='B') for pair in pairs if pair['phase'] == 'A']
    spec = dict(row, aPairs=0, bPairs=row['aPairs'])
    result = verdict(spec, transformed, 'change', noise=dict(band=0, design='long', machine=provider_for(row)['machine']))
    if result['status'] == 'PASS':
        result['aBand'] = result['band'] = max(abs(value - 1) for value in result['confidence95'])
        if result['band'] >= .05:
            result.update(status='INCONCLUSIVE', reason='One-time baseline band >=5%')
    return result


def schedule(plan):
    """Host-only sealed local ABBA blocks; no observed value changes the index."""
    schedules.verify(plan)
    return iter(plan['schedule'])


def gen0_budget(data):
    try:
        value = data['processSettings']['gcConfiguration']['GCGen0MaxBudget']
    except (KeyError, TypeError):
        return None
    return value if type(value) is int and value > 0 else None


def probe_statistic(records, statistic):
    values = []
    for record in records.values():
        times = [item['nsPerBlock'] for item in record['data']['rows']]
        if statistic == 'cv':
            values.append(st.stdev(times) / st.mean(times))
        else:
            scale = max(times)
            # Center before scaling so close, distinct pass times stay distinct.
            mean = st.mean(times)
            centered = [(value - mean) / scale for value in times]
            values.append(math.fsum(a * b for a, b in zip(centered, centered[1:])) / math.fsum(a * a for a in centered))
    return st.mean(values)


def probe_result(records, rule):
    require(set(records) == set(probe_ids(rule)), 'Invalid probe process set')
    budgets = set()
    try:
        for record in records.values():
            require(type(record['exitCode']) is int and record['exitCode'] == 0, 'Invalid probe process exit')
            data = record['data']
            times = [item['nsPerBlock'] for item in data['rows']]
            require(len(times) == PROBE_COUNTS[1] and all(type(x) in (int, float) and math.isfinite(x) and x > 0 for x in times)
                    and min(times) < max(times), 'Invalid probe measured pass series')
            budget = gen0_budget(data)
            require(budget is not None, 'Invalid probe GCGen0MaxBudget')
            budgets.add(budget)
        result = {key: probe_statistic(records, key) for key in ('cv', 'lag1')}
        require(all(math.isfinite(value) for value in result.values()), 'Invalid probe statistic')
    except (KeyError, TypeError, ArithmeticError) as error:
        raise ValueError('Invalid probe record') from error
    statistic, limit, expected = rule['statistic'], rule['limit'], rule['gen0BudgetBytes']
    reasons = [f'{statistic}-over-limit'] if result[statistic] > limit else []
    if budgets != {expected}:
        reasons.append('gen0-budget-mismatch')
    return dict(result, statistic=statistic, limit=limit, decision='rejected' if reasons else 'kept', reasons=reasons,
                gen0BudgetBytes=dict(expected=expected, observed=sorted(budgets)))


def run(root, plan, output, *, dry=False):
    root, output = Path(root), Path(output)
    output.mkdir(parents=True, exist_ok=False)
    started = time.monotonic()
    deadline = (time.time() if dry else int(os.environ['P2_MACHINE_CREATED_UNIX'])) + plan['measurementCutoffSeconds']
    records, error, predecessor, probe = [], None, None, None
    host_fallback = {}

    def member(build, workload, dest, counts, **region):
        spec = plan['builds'][build]
        job = dict(plan['workloads'][workload], root=str(root), counts=counts, timeout=max(.01, deadline - time.time()))
        for key in ('dotnet', 'server', 'capture'):
            job[key] = str(root / job[key])
        job['environment'] = {key: str(root / value) if key in ('CHRYSALIS_CORPORA', 'CHRYSALIS_BENCHMARK_DATA', 'P1_RANGE') else value
                              for key, value in job['environment'].items()}
        actual = dict(spec, client=str(root / spec['client']), harness=str(root / spec['harness']))
        return session.member(actual, job, dest, **region)

    try:
        region = dict(region=region_for(plan)) if 'region' in plan else {}
        if plan.get('provider') == 'azure':
            actual = os.environ.get('P2_AZURE_REGION')
            require(actual in provider_for(plan)['regions'] and (actual == region['region'] or (region['region'], actual) == ('eastus2', 'eastus'))
                    and session.detect_platform(actual) == 'azure', 'Sealed Azure host required')
            region['region'] = actual
        if not dry:
            fingerprint = session.host_fingerprint()
            try:
                session.save(output / 'host.json', fingerprint)
            except OSError:
                # A missing fingerprint will fail the PC's verification for a probed session.
                host_fallback['host'] = dict(dict.fromkeys(fingerprint), error='Fingerprint write failed')
        for spec in plan['builds'].values():
            actual = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in (root / spec['client']).parent.glob('*.dll')}
            require(actual == spec['dllSha256s'], 'Every-DLL closure differs before measurement')
        if not dry and (rule := plan.get('probe')) is not None:
            directory = output / 'probe'
            directory.mkdir()
            evidence = {}
            for identifier in probe_ids(rule):
                if time.time() + PROBE_PROCESS_SECONDS * 1.15 > deadline:
                    raise TimeoutError('budget stop: next probe process cannot finish before cutoff')
                # member raises on nonzero exit before returning an observation.
                record = member(rule['build'], rule['workload'], directory / identifier, list(PROBE_COUNTS), **region)
                evidence[identifier] = dict(record, exitCode=0)
            probe = probe_result(evidence, rule)
            for identifier, record in evidence.items():
                session.save(directory / f'{identifier}.json', record)
            session.save(output / 'probe.json', probe)
        for block in (() if probe and probe['decision'] == 'rejected' else plan['blocks']):
            # A full local block is atomic. Odd legacy tails have two members.
            if not dry and time.time() + len(block['members']) * block['chargeSeconds'] * 1.15 > deadline:
                raise TimeoutError('budget stop: next atomic block cannot finish before cutoff')
            for identifier in block['members']:
                build = plan['members'][identifier]['build']
                stamp = dt.datetime.now(dt.timezone.utc).isoformat()
                record = member(build, block['workload'], output / identifier, plan['counts'], **region)
                record.update(id=identifier, build=build, predecessorBuild=predecessor, startedUtc=stamp,
                              endedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
                records.append(record)
                predecessor = build
                session.save(output / 'members.json', records)
    except (ValueError, OSError, RuntimeError, TimeoutError) as exception:
        error = str(exception)
    finally:
        session.save(output / 'session-start.json', schedules.startup_information(records))
    status = dict(completedAllDeclaredRounds=error is None and (probe is None or probe['decision'] == 'kept'),
                  exitCode=0 if error is None else 2, reason=error, seconds=time.monotonic() - started, dry=dry, **host_fallback)
    if probe is not None:
        status['probe'] = probe['decision']
    session.save(output / 'session-status.json', status)
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
            if any(pair[side]['platform'] != provider_for(row)['platform'] for side in ('baseline', 'candidate')):
                return False
    return True


def noise_band(noise, provider='fly'):
    if isinstance(noise, dict) and noise.get('design') == 'long' and noise.get('machine') == PROVIDERS[provider]['machine']:
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
        require(all(pair[side]['platform'] == provider_for(row)['platform'] for pair in pairs for side in ('baseline', 'candidate')), 'PC time never gates')
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
                result['gcCounts'] = {side: gc_totals([item for pair in population for item in pair[side]['data']['rows']])
                                      for side in ('baseline', 'candidate')}
        if 'aBand' in result:
            result['band'] = result['aBand']
        if mode == 'change':
            band = noise_band(noise, row.get('provider', 'fly'))
            if band is None:
                issue = 'missing' if noise is None else 'invalid'
                failures['noise record'] = f"{issue}; requires a long {provider_for(row)['machine']} record with 0 <= N < 0.05"
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


def gc_totals(rows):
    return {key: sum(item[key] for item in rows) for key in GC_KEYS
            if rows and all(type(item.get(key)) is int and item[key] >= 0 for item in rows)}


def gc_per_build(pairs):
    observed = {side: [] for side in ('baseline', 'candidate')}
    for pair in pairs:
        for side in observed:
            data = pair[side].get('data')
            if isinstance(data, dict):
                observed['baseline' if pair['phase'] == 'A' else side].append(data)
    result = {}
    for side, data in observed.items():
        rows = [item for process in data for item in process.get('rows', ())]
        totals = gc_totals(rows)
        budgets = {gen0_budget(process) for process in data}
        result[side] = dict(passes=len(rows), collections=totals, collectionsPerPass={key: value / len(rows) for key, value in totals.items()},
                            gen0BudgetBytes=sorted(value for value in budgets if value is not None), budgetMissing=not budgets or None in budgets)
    return result


def draw_lines(output, *, first_only):
    path = output / 'time/draws.json'
    if not path.exists():
        return []
    lines = []
    try:
        for draw in json.loads(path.read_text()):
            if first_only and draw['phase'] != 'first':
                continue
            probe = draw.get('probe', {})
            booked = draw['bookedUsd']
            cost = 'unknown' if booked is None else f'${booked:.6f}'
            observed = '' if draw.get('provider') != 'azure' else f' · observed {draw.get("region", "unknown")} · steal {draw.get("steal", "not observable on Azure")}'
            lines.append(f'time session {draw["session"]} {draw["phase"]} draw {draw["draw"]} · {draw["app"]} · {draw["decision"]}{observed}'
                         f' · booked {cost} · CV16 {probe.get("cv")} · P16 {probe.get("lag1")} · limit {probe.get("limit")}'
                         f' · gen0 budget bytes {probe.get("gen0BudgetBytes")} · {", ".join(probe.get("reasons", ())) or "no probe rejection recorded"}'
                         f' · archive {draw.get("archiveSha256", "unavailable")} · host {json.dumps(draw.get("host"), sort_keys=True)}')
            warnings = draw.get('costWarnings', ['unknown booked cost'] if booked is None else [])
            if draw.get('invalid') and draw['invalid'] != 'session invalid: ' + '; '.join(warnings):
                lines.append('  DRAW WARNING: ' + draw['invalid'])
            lines.extend('  DRAW WARNING: ' + warning for warning in warnings)
    except (OSError, KeyError, TypeError, AttributeError, ValueError):
        return ['DRAW WARNING: draw diagnostics could not be read']
    return lines


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
        provider = seal.get('provider', 'fly')
        host = 'Fly' if provider == 'fly' else 'Azure'
        lines += [f"time · {host} {provider_for(seal)['machine']}{home} · arithmetic mean paired ratios",
                  f'{"benchmark":<38} {"A/B":>7} {"band N":>8} {"ratio [95%]":>31} verdict']
        lines.extend(draw_lines(output, first_only=first_only))
        money = output / 'time/azure-money.json'
        if provider == 'azure' and money.is_file():
            from gate_azure import money_text
            summary = json.loads(money.read_text())
            lines += [f"Azure go booked ${summary['goBookedUsd']:.4f} across {summary['goAttempts']} attempts",
                      'Azure program: ' + money_text(summary)]
    else:
        lines.append('GC timing observations: not recorded (no timing sessions)')
    index_file = output / 'time/index.json'
    has_index = index_file.exists()
    index = json.loads(index_file.read_bytes()) if has_index else []
    retry = seal.get('retryOf', {})
    if retry.get('kind') == 'repeat' and (has_index or (output / 'time/launch-started.json').exists()):
        first = [item for item in index if item['phase'] == 'first']
        require(gate.encoded(first) == gate.encoded([retry['firstIndex']]), 'Timing index differs from sealed retry first entry')
        require(all(item['ids'] == retry['repeatIds'] for item in index if item['phase'] == 'repeat'),
                'Timing index differs from sealed retry selection')
    band_failures = set()
    for number, plan in enumerate(plans):
        if show_regions:
            region_label = 'planned ' if plan.get('provider') == 'azure' else ''
            lines.append(f'session {number + 1} · {region_label}{region_for(plan)}')
        for row in plan['rows']:
            first = None
            first_evidence = None
            for phase in ('first',) if first_only else ('first', 'repeat'):
                matches = [item for item in index if item['session'] == number and item['phase'] == phase]
                require(len(matches) <= 1, 'Ambiguous time artifact')
                if not matches:
                    current = dict(status='INCONCLUSIVE', reason='No sealed Azure session' if plan.get('provider') == 'azure' else 'No sealed Fly session')
                else:
                    item = matches[0]
                    path = (output / item['path']).resolve()
                    require(path.is_relative_to(output.resolve()), 'Timing artifact is outside the run directory')
                    raw = path.read_bytes()
                    require(hashlib.sha256(raw).hexdigest() == item['sha256'], 'Timing raw data changed')
                    payload = json.loads(raw)
                    invalid = payload.get('invalid')
                    if row is plan['rows'][0]:
                        if plan.get('provider') == 'azure':
                            lines.append('session-start steal: not observable on Azure')
                        else:
                            lines.append('session-start steal, first five minutes (information only): ' + json.dumps(payload.get('startup', dict(available=False, reason='Not recorded in this legacy fixture')), sort_keys=True))
                        lines.append('host fingerprint (information only): ' + json.dumps(payload.get('host'), sort_keys=True))
                        lines.append('neighbours / duration / cost (context only): ' + json.dumps(payload.get('context', {}), sort_keys=True))
                    chosen = [pair for pair in payload['pairs'] if pair['id'] == row['id']]
                    current = baseline_band(row, chosen) if plan['design'] == 'micro-band' and not invalid else verdict(row, chosen, applied_mode, noise=row.get('noise'), invalid=invalid)
                    current['blockDiagnostic'] = schedules.block_diagnostics(chosen)
                    if row['id'] in band_failures:
                        current = dict(status='INCONCLUSIVE', reason='One-time micro band invalid')
                    current['gcPerBuild'] = gc_per_build(chosen)
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
                lines.append(f'  {label} admitted pairs: ' + json.dumps(part.get('admittedPairs', {})))
                for side in ('baseline', 'candidate'):
                    gc = part.get('gcPerBuild', {}).get(side, {})
                    totals, rates = gc.get('collections', {}), gc.get('collectionsPerPass', {})
                    summary = ', '.join(f'gen{i} {totals[key]} ({rates[key]:.6g}/pass)' if key in rates else f'gen{i} not recorded'
                                        for i, key in enumerate(GC_KEYS))
                    budget = ', '.join(map(str, gc.get('gen0BudgetBytes', ()))) or 'not recorded'
                    if gc.get('budgetMissing') and budget != 'not recorded':
                        budget += ' (some processes not recorded)'
                    lines.append(f'  {label} {side} GC ({gc.get("passes", 0)} measured passes): {summary}; GCGen0MaxBudget bytes: {budget} (information only)')
                for check in part.get('checks', []):
                    classifications = [('detected drift beyond the declared band' if low > 1.05 or high < .95 else 'steady state unproved') for low, high in [item['confidence95'] for item in check['stability']] if not (.95 <= low and high <= 1.05)]
                    if classifications:
                        lines.append(f'  {label} stability: ' + '; '.join(classifications))
                    lines.append(f'  {label} check: ' + json.dumps(check, sort_keys=True))
        lines.append(f'time session {number} · {plan["design"]} · limit {plan["destroyGuardSeconds"]}s / {plan["limitUsd"]:.4f} USD · reserved with repeat {plan["limitWithRepeatUsd"]:.4f} USD')
    return values, lines
