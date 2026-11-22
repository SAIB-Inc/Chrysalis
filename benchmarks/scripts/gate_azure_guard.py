"""Durable per-draw fence and native Windows keeper; no Windows configuration/files."""
from contextlib import contextmanager
import base64
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time

POWERSHELL = Path('/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe')
WRITE_END = 720
COPY_END = 200
WATCH_MARGIN = 70
ALERT_TAIL = 300
RESERVE_TAIL = 900


class GuardFailure(RuntimeError):
    pass


def alert(state, message):
    with (state/'alerts.log').open('a') as f:
        text = f'{time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())} {message}'
        print(text, file=f, flush=True)
        print(text, flush=True)


def mark(state, name, *, since=None):
    path = state / name
    if since is None:
        path.touch(mode=0o600, exist_ok=True)
        return
    now = time.time()
    body = json.dumps(dict(unix=round(now, 3), utc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime(now)),
                           elapsedSeconds=round(max(0.0, now - since), 3)), sort_keys=True) + '\n'
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
    try:
        os.fchmod(fd, 0o600)
        os.write(fd, body.encode('ascii'))
    finally:
        os.close(fd)


@contextmanager
def mutation(state, until):
    # Hold one advisory lock from the last expiry check until the REST child
    # exits. The watcher must acquire it before declaring an absent group final.
    with (state/'creation.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if time.time() >= until or (state/'retired').exists() or (state/'done').exists():
            raise GuardFailure('guard expired; refusing ARM mutation')
        if not (state/'ready').is_file():
            raise GuardFailure('native Windows watchdog not armed')
        yield lock.fileno()


@contextmanager
def retire(state):
    mark(state, 'retired')  # blocks all subsequent writers even if lock is held
    with (state/'creation.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield


@contextmanager
def cleanup_lease(state):
    with (state / 'cleanup.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield


def process_id(pid):
    boot = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
    birth = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[19]
    return hashlib.sha256((boot + ':' + birth).encode()).hexdigest()


def stop_paused_driver(context):
    # A durable lease identifies exactly the gate driver, never another WSL
    # process or another boot. pidfd removes the check-to-kill PID-reuse race.
    pid = context['driverPid']
    try:
        handle = os.pidfd_open(pid)
        try:
            if process_id(pid) == context['driverBirth']:
                signal.pidfd_send_signal(handle, signal.SIGKILL)
        finally:
            os.close(handle)
    except (FileNotFoundError, ProcessLookupError):
        pass


def wsl_context():
    import pwd
    return os.environ.get('WSL_DISTRO_NAME', ''), pwd.getpwuid(os.getuid()).pw_name


def check_context(distro, user):
    if not all(re.fullmatch(r'[A-Za-z0-9_.-]+', part) for part in (distro, user)) or user != wsl_context()[1]:
        raise GuardFailure('missing or wrong WSL context; no ARM writes')


def arm_native(script, state, deadline, distro, user, *, smoke=False):
    check_context(distro, user)
    steady = [str(script), '--keeper-smoke' if smoke else '--watchdog', str(state)]
    if not all(re.fullmatch(r'[a-zA-Z0-9_./-]+', tok) for tok in (distro, user, *steady)) or not POWERSHELL.is_file():
        raise GuardFailure('native WSL watchdog unavailable; no ARM writes')
    deadline = int(deadline + 15 if not smoke else time.time() + 25)  # Grace for the WSL terminal alert only.
    # Launcher exits immediately; Windows starts this plain bounded process in
    # its user session. It holds no token, address, key or subscription ID.
    body = f'''$ErrorActionPreference='SilentlyContinue'
$root = Join-Path $env:SystemRoot 'System32\\wsl.exe'
$end = [DateTimeOffset]::FromUnixTimeSeconds({deadline})
while ([DateTimeOffset]::UtcNow -lt $end) {{
  $arg = @('--distribution','{distro}','--user','{user}','--exec','/usr/bin/python3','-B','{steady[0]}','{steady[1]}','{steady[2]}')
  $p = Start-Process -FilePath $root -ArgumentList $arg -WindowStyle Hidden -PassThru
  if ($null -ne $p) {{
    $left = [Math]::Max(1,[int]($end-[DateTimeOffset]::UtcNow).TotalMilliseconds)
    if ($p.WaitForExit($left)) {{ if ($p.ExitCode -eq 0) {{ exit 0 }} }} else {{ $p.Kill(); break }}
  }}
  Start-Sleep -Seconds 3
}}
exit 1
'''
    encoded = base64.b64encode(body.encode('utf-16le')).decode('ascii')
    launcher = f"Start-Process -FilePath '{POWERSHELL.name}' -WindowStyle Hidden -ArgumentList '-NoProfile -NonInteractive -EncodedCommand {encoded}'"
    result = subprocess.run([str(POWERSHELL), '-NoProfile', '-NonInteractive', '-Command', launcher], timeout=12,
                            stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise GuardFailure('Windows watchdog launch failed; no ARM writes')
    limit = time.monotonic()+15
    while time.monotonic() < limit:
        if (state/'ready').is_file():
            return
        time.sleep(.2)
    raise GuardFailure('Windows child did not call back through WSL; no ARM writes')


if __name__ == '__main__':
    # Smoke runs this file directly: no provider module, settings or Azure CLI.
    import sys
    if len(sys.argv) != 3 or sys.argv[1] != '--keeper-smoke':
        raise SystemExit(2)
    directory = Path(sys.argv[2])
    if json.loads((directory / 'smoke-context.json').read_text()) != list(wsl_context()):
        raise SystemExit(2)
    mark(directory, 'ready')
