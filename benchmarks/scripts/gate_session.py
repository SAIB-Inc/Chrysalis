#!/usr/bin/env python3
"""Sealed fresh-process pairs, on the PC for bytes and on Fly for time.

No resource creation, inherited credentials, process command lines, adaptive
warm-ups or retries. The caller starts this script through heavy on the PC.
"""
import json
import math
import os
import platform
from pathlib import Path
import selectors
import signal
import subprocess
import sys
import time

AMBIENT = ('HOME', 'PATH', 'LANG', 'LC_ALL', 'TZ', 'TMPDIR', 'DOTNET_ROOT',
           'FLY_APP_NAME', 'FLY_REGION', 'FLY_MACHINE_ID',
           'DOTNET_CLI_USE_MSBUILD_SERVER', 'MSBUILDDISABLENODEREUSE', 'UseSharedCompilation')
# These values are supplied by the gate, never copied from the caller.
GENERATED = ('CHRYSALIS_CORPORA', 'CHRYSALIS_BENCHMARK_DATA',
             'CHRYSALIS_BENCHMARK_RESULTS', 'P1_RANGE', 'NUGET_PACKAGES',
             'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'DOTNET_TieredPGO',
             'CHRYSALIS_DIRECT_WARMUP_MS')
DESIGNS = {'short': (1, 25), 'long': (180, 60)}
COUNTERS = ('compiledMethods', 'compiledILBytes', 'compilationTimeTicks')


def detect_platform():
    """Detect before the allowlist strips Fly's machine metadata; never trust a job."""
    app, region = os.environ.get('FLY_APP_NAME'), os.environ.get('FLY_REGION')
    if any(key in os.environ for key in ('FLY_APP_NAME', 'FLY_REGION', 'FLY_MACHINE_ID')):
        if not app or region != 'iad' or os.cpu_count() != 8 or platform.machine() != 'x86_64':
            raise ValueError('Named 8-CPU x64 iad Fly machine required')
        return 'fly'
    return 'pc'


def clock():
    return dict(monotonic=time.monotonic(), unix=time.time(), load1=os.getloadavg()[0])


def environment(generated, ambient=None):
    source = os.environ if ambient is None else ambient
    result = {key: source[key] for key in AMBIENT if key in source}
    if set(generated) - set(GENERATED):
        raise ValueError('Unknown generated environment key')
    result.update(generated)
    result.update(DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
    return result


def save(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')
    temporary.replace(path)


def proc_inventory(root=Path('/proc')):
    """Names and accounting only: never read command lines or environments."""
    result = {}
    for path in root.iterdir():
        if not path.name.isdigit():
            continue
        try:
            text = (path / 'stat').read_text()
            fields = text[text.rindex(')') + 2:].split()
            if 'Kthread:\t1' in (path / 'status').read_text():
                continue
            result[int(path.name)] = dict(name=text[text.index('(') + 1:text.rindex(')')],
                                         ppid=int(fields[1]), startTicks=int(fields[19]),
                                         cpuTicks=int(fields[11]) + int(fields[12]))
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue
    return result


def cpu_snapshot():
    cpu = {}
    for line in Path('/proc/stat').read_text().splitlines():
        fields = line.split()
        if fields and (fields[0] == 'cpu' or fields[0][3:].isdigit()):
            cpu[fields[0]] = [int(value) for value in fields[1:9]]
    return dict(monotonic=time.monotonic(), unix=time.time(), cpuStat=cpu)


def snapshot():
    return dict(cpu_snapshot(), load1=os.getloadavg()[0], processes=proc_inventory())


def descendants(pid, inventory):
    found = {pid}
    while True:
        added = {key for key, row in inventory.items() if row['ppid'] in found} - found
        if not added:
            return found
        found |= added


def ancestors(pid, inventory):
    found = set()
    while pid and pid not in found and pid in inventory:
        found.add(pid)
        pid = inventory[pid]['ppid']
    return found


def admission(steal, per_core, foreign_seconds, foreign_each, *, duration):
    reasons = []
    if steal is None or not math.isfinite(steal) or steal > .01:
        reasons.append('aggregate steal missing or >1%')
    if len(per_core) != 8 or any(x is None or not math.isfinite(x) or x > .02 for x in per_core):
        reasons.append('per-core steal missing or >2%')
    if not math.isfinite(duration) or duration <= 0 or foreign_seconds is None or not math.isfinite(foreign_seconds):
        reasons.append('outside-process accounting missing')
    elif foreign_seconds / duration > .05 or any(x / duration > .02 for x in foreign_each):
        reasons.append('outside process CPU >5% total or >2% per process of one core')
    return reasons


def steal_fraction(before, after):
    delta = [right - left for left, right in zip(before, after)]
    if len(delta) != 8 or min(delta) < 0 or sum(delta) <= 0:
        return None
    return delta[7] / sum(delta)


def stop(process):
    if process.poll() is None:
        os.killpg(process.pid, signal.SIGTERM)
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()


def monitored_process(command, root, env, dest, timeout, server_pid=None, *, observed=True, platform=None):
    """Ack each mark only after the measured-window snapshot, outside timed passes."""
    dest = Path(dest)
    dest.mkdir(parents=True, exist_ok=False)
    platform = platform or detect_platform()
    on_fly = platform == 'fly'
    before = snapshot() if on_fly else clock()
    known = ancestors(os.getpid(), before['processes']) if on_fly else set()
    marks, samples, foreign = {}, [], {}
    previous = None
    process = subprocess.Popen(list(map(str, command)), cwd=root, env=env,
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, start_new_session=True)
    deadline, error, pending = time.monotonic() + timeout, None, b''
    hz = os.sysconf('SC_CLK_TCK') if on_fly else None

    def sample():
        nonlocal previous
        if not on_fly:
            return clock()
        now = snapshot()
        if previous is not None:
            ours = descendants(process.pid, now['processes']) | known
            if server_pid:
                ours |= descendants(server_pid, now['processes'])
            for pid, row in now['processes'].items():
                old = previous['processes'].get(pid)
                ticks = max(0, row['cpuTicks'] - old['cpuTicks']) if old and old['startTicks'] == row['startTicks'] else row['cpuTicks']
                if pid not in ours and ticks:
                    key = (pid, row['startTicks'])
                    foreign[key] = foreign.get(key, 0) + ticks / hz
        previous = now
        samples.append(now)
        return now

    with selectors.DefaultSelector() as selector, (dest / 'console.log').open('wb') as log:
        selector.register(process.stdout, selectors.EVENT_READ)
        try:
            while selector.get_map():
                if time.monotonic() >= deadline:
                    raise TimeoutError('Fixed process budget expired')
                for key, _ in selector.select(.25):
                    block = os.read(key.fd, 65536)
                    if not block:
                        selector.unregister(key.fileobj)
                        continue
                    log.write(block)
                    pending += block
                    while b'\n' in pending:
                        line, pending = pending.split(b'\n', 1)
                        mark = line.strip().decode(errors='replace')
                        if observed and mark in ('MEASURED_START', 'MEASURED_END'):
                            expected = 'MEASURED_START' if not marks else 'MEASURED_END'
                            if mark != expected or mark in marks:
                                raise ValueError('Duplicate or out-of-order measured mark')
                            if mark == 'MEASURED_START':
                                previous = None
                            marks[mark] = sample()
                            process.stdin.write(b'ACK\n')
                            process.stdin.flush()
                if on_fly and 'MEASURED_START' in marks and 'MEASURED_END' not in marks:
                    sample()
            process.wait(timeout=max(.01, deadline - time.monotonic()))
        except Exception as exception:
            error = f'{type(exception).__name__}: {exception}'
        finally:
            stop(process)
            process.stdin.close()
            process.stdout.close()
    after = snapshot() if on_fly else clock()
    reasons = [] if on_fly else ['PC time never gates']
    if set(marks) != {'MEASURED_START', 'MEASURED_END'}:
        reasons.append('Missing measured-start or measured-end mark')
    elif on_fly:
        start, end = marks['MEASURED_START'], marks['MEASURED_END']
        cores = [steal_fraction(value, end['cpuStat'].get(key, []))
                 for key, value in start['cpuStat'].items() if key != 'cpu']
        reasons += admission(steal_fraction(start['cpuStat']['cpu'], end['cpuStat']['cpu']), cores,
                             sum(foreign.values()), list(foreign.values()),
                             duration=end['monotonic'] - start['monotonic'])
    if process.returncode:
        reasons.append('Client exited nonzero')
    if error:
        reasons.append(error)
    result = dict(pid=process.pid, exitCode=process.returncode, before=before, after=after,
                  seconds=after['monotonic'] - before['monotonic'], marks=marks, samples=samples,
                  foreignCpuSeconds=sum(foreign.values()), timingRejectedReasons=reasons)
    if error:
        result['error'] = error
    save(dest / 'process.json', result)
    return result


def validate_observations(data, case, runtime, counts):
    warmup, measured = counts
    blocks = None if case == 'micro' else 1000 if case == 'tcp' else 1020
    if data.get('runtime') != runtime or (data.get('warmupPasses'), data.get('passes')) != (warmup, measured):
        raise ValueError('Runtime/design count mismatch')
    for key, count in [('warmupRows', warmup), ('rows', measured)]:
        rows = data.get(key, [])
        if len(rows) != count or [row['pass'] for row in rows] != list(range(1, count + 1)):
            raise ValueError('Incomplete fixed work: ' + key)
        if any((row['blocks'] != blocks if blocks is not None else row['blocks'] <= 0) or row['nsPerBlock'] <= 0 or any(not math.isfinite(row[k]) or row[k] < 0 for k in ('nsPerBlock', 'bytesPerBlock')) for row in rows):
            raise ValueError('Invalid fixed work: ' + key)
    for stage, count in [('warmup', warmup), ('measured', measured)]:
        boundaries = [row for row in data.get('groupBoundaries', []) if row['stage'] == stage]
        if [row['passIndex'] for row in boundaries] != list(range(count + 1)):
            raise ValueError('Missing per-pass JIT counters')
        if any(not isinstance(row[part].get(key), int) or row[part][key] < 0
               for row in boundaries for part in ('cumulative', 'delta') for key in COUNTERS):
            raise ValueError('Invalid JIT counters')


def jit_quiet(data):
    def group(stage, low, high):
        boundaries = [row for row in data['groupBoundaries'] if row['stage'] == stage and low <= row['passIndex'] <= high]
        rows = data['warmupRows'] if stage == 'warmup' else data['rows']
        return dict(bodyMs=sum(row['nsPerBlock'] * row['blocks'] for row in rows[low - 1:high]) / 1e6,
                    **{key: sum(row['delta'][key] for row in boundaries) for key in COUNTERS})
    groups = [group('measured', low, low + 19) for low in (1, 21, 41)]
    return dict(groups=groups, warmupGroups=[group('warmup', low, low + 19) for low in range(1, 181, 20)],
                bodyMs=sum(row['bodyMs'] for row in groups),
                jitMs=sum(row['compilationTimeTicks'] for row in groups) / 10000)


def side_quiescence(rows):
    """D49: compile-time share gates; methods count is reported, not a veto."""
    if not rows:
        return dict(established=False, reason='Missing side processes')
    jit = [row['jit'] for row in rows]
    body = sum(row['bodyMs'] for row in jit)
    compiled = sum(row['jitMs'] for row in jit)
    counts = [sum(row['groups'][index]['compiledMethods'] for row in jit) for index in range(3)]
    fraction = compiled / body if body > 0 else None
    return dict(established=fraction is not None and 0 <= fraction <= .005,
                processes=len(rows), jitFraction=fraction, limitFraction=.005,
                summedMeasuredBodyMs=body, summedJitMs=compiled,
                groupCompiledMethods=counts, group3NotAboveGroup1=counts[2] <= counts[0])


def member(spec, job, dest):
    """One client/direct process; called inside the PC's heavy invocation."""
    dest = Path(dest)
    dest.mkdir(parents=True, exist_ok=False)
    platform = detect_platform()
    generated = dict(job['environment'], CHRYSALIS_BENCHMARK_RESULTS=str(dest))
    env = environment(generated)
    save(dest / 'environment.json', env)
    output = dest / ('direct.json' if job['kind'] == 'direct' else 'observations.json')
    command = [job['dotnet'], '--fx-version', spec['runtime'], spec['harness' if job['kind'] == 'direct' else 'client']]
    server = log = None
    try:
        if job['kind'] == 'direct':
            command += ['direct', job.get('filter', '*')]
        else:
            if job['replay'] == 'tcp':
                ready = dest / 'server.ready.json'
                log = (dest / 'server.log').open('w')
                server = subprocess.Popen([sys.executable, job['server'], '--capture', job['capture'],
                                           '--ready', str(ready), '--summary', str(dest / 'server-summary.json'),
                                           '--connections', '1', '--send-mode', 'captured-chunks'],
                                          env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
                deadline = time.monotonic() + 30
                while not ready.exists():
                    if server.poll() is not None or time.monotonic() > deadline:
                        raise RuntimeError('TCP server did not become ready')
                    time.sleep(.05)
            command += [job['replay'], str(output), *map(str, job['counts'])]
            if job['replay'] == 'micro':
                command += [job['filter'], str(job['microInvocations'])]
            command += ['--observed']
        result = monitored_process(command, job['root'], env, dest / 'execution', job.get('timeout', 480),
                                   server.pid if server else None, observed=job['kind'] != 'direct', platform=platform)
        if result['exitCode'] or result.get('error'):
            raise RuntimeError('Measurement process failed; raw output retained')
        data = json.loads(output.read_text())
        if job['kind'] != 'direct':
            validate_observations(data, job['replay'], spec['runtime'], job['counts'])
        if server:
            server.wait(timeout=15)
            proof = json.loads((dest / 'server-summary.json').read_text())
            if server.returncode or len(proof) != 1 or proof[0]['completedRanges'] != sum(job['counts']):
                raise ValueError('TCP fixed work not verified')
        return dict(data=data, identity={key: spec[key] for key in ('commit', 'treeId', 'runtime', 'dllSha256s')},
                    platform=platform, timingRejectedReasons=result['timingRejectedReasons'],
                    measurement=dict(kind=job['kind'], counts=job['counts'], filter=job['filter']),
                    load=[result['before']['load1'], result['after']['load1']],
                    seconds=result['seconds'], marks=sorted(result['marks']),
                    wholeProcess=dict(startMonotonic=result['before']['monotonic'], endMonotonic=result['after']['monotonic'],
                                      aggregateSteal=steal_fraction(result['before']['cpuStat']['cpu'], result['after']['cpuStat']['cpu']) if platform == 'fly' else None))
    finally:
        if server:
            stop(server)
        if log:
            log.close()


def run(job, output):
    """Fresh alternating sides, fixed counts; no admission-driven retries."""
    output = Path(output)
    output.mkdir(parents=True, exist_ok=False)
    save(output / 'job.json', job)
    pairs = []
    for number in range(1, job['pairs'] + 1):
        pair = dict(pair=number)
        order = ('baseline', 'candidate') if number % 2 else ('candidate', 'baseline')
        for side in order:
            pair[side] = member(job['sides'][side], job, output / f'pair-{number:03}' / side)
            print(job.get('id', job['kind']), number, side, flush=True)
        pairs.append(pair)
        save(output / 'pairs.json', pairs)
    return pairs


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[1]).read_text()), Path(sys.argv[2]))
