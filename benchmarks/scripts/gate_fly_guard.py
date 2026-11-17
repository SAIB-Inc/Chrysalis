#!/usr/bin/env python3
"""Offline resource and seal checks; no credentials or Fly API calls."""
import hashlib
import io
import json
import os
from pathlib import Path
import re
import sys
import tarfile

RUN_PREFIXES = ('chrysalis-bench-', 'chrysalis-gate-')


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def listing(p, keys):
    x = json.loads(p.read_text())
    if isinstance(x, dict):
        found = [k for k in keys if k in x]
        require(len(found) == 1, 'Unknown list shape')
        x = x[found[0]]
    require(isinstance(x, list), 'Unknown list shape')
    return x


def names(p):
    return {a.get('name', a.get('Name')) for a in listing(p, ['apps', 'Apps'])}


from gate_time import MAX_APP_SECONDS, validate_limits
SHUTDOWN_MARGIN_SECONDS = 10


def machine_command(plan):
    guard, _ = validate_limits(plan)
    stop = guard - SHUTDOWN_MARGIN_SECONDS
    return f'remaining=$((P2_MACHINE_CREATED_UNIX + {stop} - $(date +%s))); ((remaining > 0)) || exit 0; exec /usr/bin/sleep "$remaining"'


def verified_launch_inputs(here, ready):
    import gate
    seal = gate.read_seal(ready.parent)
    require(sha(ready) == (ready.parent / 'seal.id').read_text().strip(), 'Seal identity changed')
    # Run plan is selected by a fixed archive path, never by an unsealed local copy.
    relative = os.environ['GATE_BUNDLE']
    bundle = (ready.parent / relative).resolve()
    require(bundle.is_relative_to(ready.parent.resolve()), 'Archive path escapes seal')
    from gate_bundle import verified_bundle
    data = verified_bundle(ready.parent, seal, relative)
    with tarfile.open(fileobj=io.BytesIO(data), mode='r:gz') as stream:
        member = stream.getmember('plan.json')
        require(member.isfile(), 'Archive plan is not regular')
        plan = json.loads(stream.extractfile(member).read())
    # Every launcher helper is snapshotted and content-bound, including its imports.
    for path in here.iterdir():
        if path.is_file():
            name = path.relative_to(ready.parent).as_posix()
            require(seal['files'].get(name) == sha(path), 'Unsealed or stale launcher helper')
    require(all(isinstance(seal.get(key), str) and re.fullmatch(r'mcr\.microsoft\.com/dotnet/sdk@sha256:[0-9a-f]{64}', seal[key]) for key in ('image', 'imageIndex')), 'Missing pinned platform/index image identity')
    version = seal['flyctlVersion']
    return dict(bundle=str(bundle), sha256=hashlib.sha256(data).hexdigest(), machineCommand=machine_command(plan),
                guard=plan['destroyGuardSeconds'], cutoff=plan['measurementCutoffSeconds'], version=version,
                image=seal['image'])


def clean_run_folder(root):
    forbidden = {'config.yml', 'id_rsa', 'id_ed25519', 'id_ecdsa', 'id_dsa'}
    for path in root.rglob('*'):
        if not path.is_file():
            continue
        name = str(path.relative_to(root))
        require(path.name.lower() not in forbidden and path.suffix.lower() not in {'.key', '.pem', '.p12', '.pfx'},
                'Credential-shaped file in run folder (contents not read): ' + name)
        require(path.resolve().is_relative_to(root.resolve()), 'Run-folder symlink escapes audit: ' + name)
        if path.suffix.lower() in {'.gz', '.zip', '.tar'}:
            continue
        with path.open('rb') as stream:
            previous = b''
            while block := stream.read(65536):
                text = previous + block
                require(not re.search(rb'-----BEGIN [A-Z ]*PRIVATE KEY-----|LocalPrivate\s*:|PrivateKey\s*=', text, re.IGNORECASE),
                        'Private-key marker in run folder: ' + name)
                previous = text[-80:]


def descendants(pid):
    found = {pid}
    pending = [pid]
    while pending:
        current = pending.pop()
        try:
            children = [int(value) for value in Path(f'/proc/{current}/task/{current}/children').read_text().split()]
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            children = []
        for child in children:
            if child not in found:
                found.add(child); pending.append(child)
    return found


def lock_holders(lock, roots):
    allowed = {os.getpid()}
    for pid in roots:
        if pid > 0: allowed |= descendants(pid)
    target = lock.resolve()
    unexpected = []
    for process in Path('/proc').iterdir():
        if not process.name.isdigit() or int(process.name) in allowed:
            continue
        try:
            if process.stat().st_uid != os.getuid(): continue
            for descriptor in (process / 'fd').iterdir():
                try:
                    if Path(os.readlink(descriptor)) == target:
                        info = (process / 'fdinfo' / descriptor.name).read_text()
                        if any(line.startswith('lock:') for line in info.splitlines()):
                            unexpected.append(dict(pid=int(process.name), fd=int(descriptor.name)))
                except (FileNotFoundError, ProcessLookupError, PermissionError): pass
        except (FileNotFoundError, ProcessLookupError, PermissionError): pass
    return unexpected


def main():
    mode, *args = sys.argv[1:]
    if mode == 'sealed':
        here, ready = map(lambda x: Path(x).resolve(), args)
        print(json.dumps(verified_launch_inputs(here, ready)))
    elif mode == 'clean-run-folder':
        clean_run_folder(Path(args[0]))
    elif mode == 'watchdog-pid':
        pid = int(args[0])
        process = Path('/proc') / str(pid)
        require(process.exists() and process.stat().st_uid == os.getuid(), 'Watchdog PID is absent or not owned')
        # Only this known-owned watchdog is inspected; no command line is printed.
        values = (process / 'cmdline').read_bytes().split(b'\0')
        values = [os.fsdecode(value) for value in values if value]
        require(len(values) == 7 and Path(values[0]).name == 'bash' and values[1:] == args[1:] and os.getsid(pid) == pid,
                'PID no longer identifies our exact detached watchdog; refusing signal')
    elif mode == 'lock-holders':
        unexpected = lock_holders(Path(args[0]), [int(value) for value in args[1:]])
        print(json.dumps(dict(status='PASS' if not unexpected else 'FAILED', unexpectedHolders=unexpected)))
        require(not unexpected, 'Unexpected process retains the Fly lock file; coordinator action required')
    elif mode == 'apps':
        current = names(Path(args[0]))
        require(all(isinstance(name, str) for name in current), 'Invalid app name in listing')
        leftovers = sorted(name for name in current if name == args[1] or name.startswith(RUN_PREFIXES))
        if leftovers:
            print('Existing benchmark/gate app: ' + ', '.join(leftovers) + '; stop and recover only a proven-owned leftover. Never touch neighbours.', file=sys.stderr)
            raise SystemExit(4)
    elif mode == 'cleanup':
        before, after, app = args
        previous, current = names(Path(before)), names(Path(after))
        require(app not in current, 'Our app is still present')
        print(json.dumps(dict(status='PASS', ourApp=app, ourAppAbsent=True,
                              contextOnly=dict(beforeCount=len(previous), afterCount=len(current)))))
    elif mode == 'machine-id':
        machines = listing(Path(args[0]), ['machines', 'Machines'])
        require(len(machines) == 1, 'Exactly one machine required')
        value = machines[0].get('id', machines[0].get('ID'))
        require(bool(value), 'Missing machine ID')
        print(value)
    elif mode == 'machine':
        root, machine_id, verified_command = Path(args[0]), args[1], args[2]
        machines = listing(root / 'machines-ready.json', ['machines', 'Machines'])
        require(len(machines) == 1, 'Exactly one machine required')
        m = machines[0]
        c = m.get('config', {})
        g = c.get('guest', {})
        require(m.get('id', m.get('ID')) == machine_id and m.get('state') == 'started' and m.get('region') == 'iad', 'Wrong machine identity/state/region')
        require(c.get('image') == (root / 'image.txt').read_text().strip(), 'Machine image differs from seal')
        events = [event for event in m.get('events', []) if event.get('type') == 'start']
        require(len(events) == 1 and not any(event.get('type') in ('restart', 'replace') for event in m.get('events', [])), 'Machine restart/replacement detected')
        require(g == dict(cpu_kind='performance', cpus=8, memory_mb=16384) and not c.get('services') and not c.get('mounts'), 'Wrong shape, services or mounts')
        env = c.get('env', {})
        require(not any(any(s in k.upper() for s in ['TOKEN', 'SECRET', 'PASSWORD', 'KEY']) for k in env), 'Credential-shaped VM environment name')
        created = (root / 'app-guard-start-unix.txt').read_text().strip()
        require(re.fullmatch(r'[0-9]+', created) and env.get('P2_MACHINE_CREATED_UNIX') == created, 'VM absolute cutoff timestamp differs')
        require(c.get('auto_destroy') is True and c.get('restart', {}).get('policy') == 'no', 'VM must auto-destroy on main exit, never restart')
        command = c.get('init', {}).get('cmd', c.get('init', {}).get('exec'))
        stop = re.search(r'P2_MACHINE_CREATED_UNIX \+ ([0-9]+) - ', verified_command)
        require(stop is not None, 'VM expected command is not a verified absolute stop')
        require(machine_command(json.loads((root / 'verified-plan.json').read_text())) == verified_command,
                'VM expected command violates the independent code cap or canonical form')
        require(command == ['/bin/bash', '-c', verified_command], 'VM main process lacks the sealed absolute stop')
        for filename, keys in [('ips', ['ips', 'Ips', 'IPAddresses']), ('volumes', ['volumes', 'Volumes']), ('secrets', ['secrets', 'Secrets'])]:
            require(not listing(root / (filename + '.json'), keys), 'Public IP or volume allocated')
        (root / 'machine.json').write_text(json.dumps(m, indent=2) + '\n')
    elif mode == 'result':
        archive, note = Path(args[0]), Path(args[1])
        with tarfile.open(archive, 'r:gz') as stream:
            try:
                status = json.load(stream.extractfile('results/session-status.json'))
            except (KeyError, TypeError, ValueError):
                status = dict(completedAllDeclaredRounds=False, reason='Missing/invalid completion status')
            recovered = 'results/recovery-diagnostic.json' in stream.getnames()
        complete = status.get('completedAllDeclaredRounds') is True and status.get('exitCode') == 0 and not recovered
        if not complete:
            (note / 'diagnostic-only.json').write_text(json.dumps(dict(diagnosticOnly=True, verdict='INCONCLUSIVE', session=status), indent=2) + '\n')
        require(complete, 'DIAGNOSTIC ONLY / INCONCLUSIVE: failed, budget-stopped or incomplete session; numerical go table not evaluated')
    elif mode == 'lock-inode':
        require(os.stat(args[0]).st_ino == os.stat('/proc/self/fd/9').st_ino and
                os.stat(args[0]).st_dev == os.stat('/proc/self/fd/9').st_dev, 'Fly lock path was replaced')
    elif mode == 'neighbour-apps':
        payload = json.load(sys.stdin) if args[0] == '-' else json.loads(Path(args[0]).read_text())
        if isinstance(payload, dict):
            payload = payload.get('apps', payload.get('Apps'))
        require(isinstance(payload, list), 'Unknown neighbour apps shape')
        for name in sorted({item.get('name', item.get('Name')) for item in payload} - {args[1]}):
            require(isinstance(name, str) and re.fullmatch(r'[a-z0-9-]+', name), 'Invalid neighbour app name')
            print(name)
    elif mode == 'neighbours':
        counts = dict(performance=0, shared=0)
        machines = json.load(sys.stdin)
        if isinstance(machines, dict):
            machines = machines.get('machines', machines.get('Machines'))
        require(isinstance(machines, list), 'Unknown neighbour list shape')
        for machine in machines:
            if machine.get('state') == 'started':
                kind = machine.get('config', {}).get('guest', {}).get('cpu_kind')
                counts[kind if kind in counts else 'unknown'] = counts.get(kind if kind in counts else 'unknown', 0) + 1
        print(json.dumps(counts))
    else:
        raise RuntimeError('Unknown guard mode')


if __name__ == '__main__':
    main()
