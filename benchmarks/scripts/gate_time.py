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
RESERVE = .03
RAW_CAP = min(CUTOFF / 1.15 - 420, GUARD / 1.15 - 720)


def require(ok, message):
    if not ok:
        raise ValueError(message)


def price(seconds):
    return seconds / 3600 * PRICE + RESERVE


def validate_limits(plan):
    guard, cutoff = plan['destroyGuardSeconds'], plan['measurementCutoffSeconds']
    require(type(guard) is int and 120 < guard <= MAX_APP_SECONDS, 'Lifetime exceeds independent7200s code cap')
    require(type(cutoff) is int and 0 < cutoff <= guard - 300, 'Invalid measurement/copy/cleanup deadlines')
    require(plan['limitUsd'] >= price(guard), 'Priced lifetime exceeds approved session price')
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
        repeat = mode == 'release'
        result = dict(design=design, rows=entries, counts=[180, 60], rawSeconds=raw(),
                      estimatedSeconds=limit, destroyGuardSeconds=limit, measurementCutoffSeconds=math.ceil(1.15 * (420 + raw())),
                      limitUsd=price(limit), estimatedUsd=price(limit), repeatAllowed=repeat,
                      limitWithRepeatUsd=price(limit) * (2 if repeat else 1))
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
        result['band'] = max(abs(value - 1) for value in result['confidence95'])
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
    try:
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
                record = session.member(actual, job, output / identifier)
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
                                                    reason=error, seconds=time.monotonic() - started, dry=dry))
    return error is None


def stability(processes):
    result = []
    for left, right in ((0, 1), (1, 2), (0, 2)):
        means = [[st.mean(item['nsPerBlock'] for item in record['data']['rows'][group * 20:(group + 1) * 20])
                  for record in processes] for group in (left, right)]
        check = stats.paired_ratio(*means)
        check['groups'] = [181 + left * 20, 181 + right * 20]
        result.append(check)
    return result


def admitted_pairs(pairs):
    return [pair for pair in pairs if all(not pair[side]['timingRejectedReasons'] and
            pair[side].get('marks') == ['MEASURED_END', 'MEASURED_START'] for side in ('baseline', 'candidate'))]


def pair_times(pairs):
    return stats.paired_ratio(*[[st.mean(item['nsPerBlock'] for item in pair[side]['data']['rows']) for pair in pairs]
                               for side in ('baseline', 'candidate')])


def verdict(row, pairs, mode, *, noise=None, invalid=None):
    """Every declared pair must complete; rejected timing still counts for JIT."""
    result = dict(status='INCONCLUSIVE', reason=invalid or 'Missing timing data', rowClass=row.get('rowClass', 'gated'))
    # Descriptive B estimate survives an invalid A/session check; never a verdict.
    try:
        result.update(pair_times(admitted_pairs([pair for pair in pairs if pair['phase'] == 'B'])))
    except (ValueError, KeyError, TypeError, ZeroDivisionError):
        pass
    if invalid:
        return result
    try:
        require(row['bPairs'] >= 5 and (mode != 'release' or row.get('rowClass') == 'fail-only' or row['aPairs'] >= 5), 'Missing required phase counts')
        require(all(pair[side]['platform'] == 'fly' for pair in pairs for side in ('baseline', 'candidate')), 'PC time never gates')
        checks, ratios = [], []
        result['checks'] = checks
        result['admittedPairs'] = {}
        for phase, count in (('A', row['aPairs']), ('B', row['bPairs'])):
            if not count:
                continue
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
            admitted = admitted_pairs(population)
            result['admittedPairs'][phase] = len(admitted)
            require(len(admitted) >= 5, f'{phase} admission: {len(admitted)} admitted pairs <5; ' + '; '.join(side + ': ' + ','.join(pair[side]['timingRejectedReasons']) for pair in population for side in ('baseline', 'candidate') if pair[side]['timingRejectedReasons']))
            for side in ('baseline', 'candidate'):
                complete = [dict(jit=session.jit_quiet(pair[side]['data'])) for pair in population]
                quiet = session.side_quiescence(complete)
                require(quiet['established'], phase + ' ' + side + f' quiescence {quiet.get("jitFraction")} >0.005')
                stable = stability([pair[side] for pair in admitted])
                checks.append(dict(phase=phase, side=side, quiescence=quiet, stability=stable))
                failed = [check for check in stable if not (.95 <= check['confidence95'][0] and check['confidence95'][1] <= 1.05)]
                require(not failed, phase + ' ' + side + ' stability intervals outside[0.95,1.05]: ' + json.dumps(failed))
            measurement = pair_times(admitted)
            if phase == 'A':
                band = max(abs(value - 1) for value in measurement['confidence95'])
                require(band < .05, f'A band {band:.6f} >=0.05')
                result['band'] = band
            else:
                ratios = measurement['pairedRatios']
                result.update(measurement)
                result['usPerOperation'] = [st.mean(st.mean(item['nsPerBlock'] for item in pair[side]['data']['rows']) for pair in admitted) / 1000 for side in ('baseline', 'candidate')]
                result['warmupNs'] = [st.mean(pair[side]['data'].get('warmupElapsedNs', sum(item['nsPerBlock'] * item['blocks'] for item in pair[side]['data']['warmupRows'])) for pair in population) for side in ('baseline', 'candidate')]
                result['gcCounts'] = {side: {key: sum(item[key] for pair in population for item in pair[side]['data']['rows']) for key in ('gen0Collections', 'gen1Collections', 'gen2Collections') if all(key in item for pair in population for item in pair[side]['data']['rows'])} for side in ('baseline', 'candidate')}
        result['checks'] = checks
        if mode == 'change':
            require(noise is not None, 'no noise record')
            require(noise.get('design') == 'long' and noise.get('machine') == 'performance-8x' and 0 <= noise['band'] < .05, 'Invalid noise record')
            result['band'] = noise['band']
        return apply_rule(result, ratios, mode)
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


def pooled(first, repeat, mode):
    if first['status'] != 'FAIL':
        raise ValueError('Only a failing row may repeat')
    if 'ratios' not in repeat:
        return dict(status='INCONCLUSIVE', reason='Repeat invalid: ' + repeat['reason'], first=first, repeat=repeat)
    result = apply_rule(dict(rowClass=first['rowClass'], band=max(first.get('band', 0), repeat.get('band', 0))),
                        first['ratios'] + repeat['ratios'], mode)
    return dict(result, first=first, repeat=repeat)


def read_results(output, seal):
    """Pure re-reporting from sealed inputs plus hashed session archives."""
    import gate
    values, lines = {}, []
    if seal.get('timePlans'):
        lines += ['time · Fly performance-8x iad · arithmetic mean paired ratios',
                  f'{"benchmark":<38} {"A/B":>7} {"band N":>8} {"ratio [95%]":>31} verdict']
    index_file = output / 'time/index.json'
    index = json.loads(index_file.read_text()) if index_file.exists() else []
    band_failures = set()
    for number, plan in enumerate(seal.get('timePlans', [])):
        for row in plan['rows']:
            first = None
            for phase in ('first', 'repeat'):
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
                    if row is plan['rows'][0]:
                        lines.append('neighbours / duration / cost (context only): ' + json.dumps(payload.get('context', {}), sort_keys=True))
                    chosen = [pair for pair in payload['pairs'] if pair['id'] == row['id']]
                    current = baseline_band(row, chosen) if plan['design'] == 'micro-band' and not invalid else verdict(row, chosen, seal['mode'], noise=row.get('noise'), invalid=invalid)
                    current['blockDiagnostic'] = schedules.block_diagnostics(chosen)
                    if row['id'] in band_failures:
                        current = dict(status='INCONCLUSIVE', reason='One-time micro band invalid')
                if phase == 'first':
                    first = current
                elif matches and row['id'] in item['ids']:
                    require(seal['mode'] == 'release' and plan['repeatAllowed'], 'Unsealed repeat')
                    first = pooled(first, current, seal['mode'])
            if plan['design'] == 'micro-band':
                if first['status'] != 'PASS':
                    band_failures.add(row['id'])
            else:
                values[row['id']] = first
            ci = first.get('confidence95')
            interval = f'{first["mean"]:.5f} [{ci[0]:.5f}, {ci[1]:.5f}]' if ci else '—'
            absolute = first.get('usPerOperation', first.get('first', {}).get('usPerOperation'))
            units = 'us/op' if row['replay'] == 'micro' else 'us/block'
            lines.append(f'{row["id"]:<38} {row["aPairs"]:>3}/{row["bPairs"]:>3} {first.get("band", 0):>8.3%} {interval:>31} {first["status"]}')
            for side, identity in row['sides'].items():
                lines.append(f'  {side}: {identity["commit"]} tree {identity["treeId"]} runtime {identity["runtime"]}; corpus {row.get("corpusId", "audit-preprod")}')
            lines.append('  rule: ' + row.get('rowClass', 'gated') + ' · ' + first['reason'])
            if absolute:
                lines.append(f'  {units} baseline/candidate {absolute[0]:.3f}/{absolute[1]:.3f}; warm-up ns {first.get("warmupNs", first.get("first", {}).get("warmupNs"))} (reported, not gated)')
            if 'repeat' in first:
                for label in ('first', 'repeat'):
                    part = first[label]
                    lines.append(f'  {label}: {part["status"]} ratio {part.get("mean")} {part.get("confidence95")}; joint {first["status"]}')
            for label, part in [('current', first), *([("first", first['first']), ("repeat", first['repeat'])] if 'repeat' in first else [])]:
                if part.get('blockDiagnostic'):
                    lines.append('  block contrast / orientation ' + label + ' (information only): ' + json.dumps(part['blockDiagnostic'], sort_keys=True))
            lines.append('  admitted pairs: ' + json.dumps(first.get('admittedPairs', {})) + '; GC collections: ' + json.dumps(first.get('gcCounts', {})))
            for check in first.get('checks', []):
                classifications = [('detected drift beyond the declared band' if low > 1.05 or high < .95 else 'steady state unproved') for low, high in [item['confidence95'] for item in check['stability']] if not (.95 <= low and high <= 1.05)]
                if classifications:
                    lines.append('  stability: ' + '; '.join(classifications))
                lines.append('  ' + json.dumps(check, sort_keys=True))
        lines.append(f'time session {number} · {plan["design"]} · limit {plan["destroyGuardSeconds"]}s / {plan["limitUsd"]:.4f} USD · reserved with repeat {plan["limitWithRepeatUsd"]:.4f} USD')
    return values, lines
