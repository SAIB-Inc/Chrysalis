"""Control-channel worker installed by cloud-init; no Azure token is persisted."""
from collections import deque
from contextlib import contextmanager
import base64
import hashlib
import io
import http.client
import json
import os
from pathlib import Path
import re
import resource
import signal
import ssl
import stat
import subprocess
import sys
import tarfile
import time
import zlib

TEMP=Path('/run/chrysalis-control')
WORK=Path('/opt/chrysalis')
PAYLOAD_KEY = TEMP / 'payload.pem'
RESULTS_KEY = TEMP / 'results.pem'
UNIT = 'chrysalis-gate-worker'
MAX_BLOB_BYTES = 160_000_000
MAX_RESULTS = 10_000_000
MAX_RESULTS_COMPRESSED = MAX_RESULTS
MAX_RESULTS_EXPANDED = 600_000_000
BASE_RESULTS_COMPRESSED = 1_000_000
PER_PROCESS_COMPRESSED = 40_000
PER_PROCESS_EXPANDED = 1_000_000
MAX_DIAGNOSTIC = 1_000_000
MAX_STATUS_BYTES = 65_536
DIAG_SESSION_LOG_BYTES = 256 * 1024
DIAG_PROCESS_LOG_BYTES = 16 * 1024
DIAG_TOTAL_LOG_BYTES = 640 * 1024
DIAG_MAX_PROCESS_LOGS = 64
REDACTED_LINE = b'[redacted: credential-shaped line]'
PEM_BEGIN = re.compile(rb'-----BEGIN [A-Z ]*PRIVATE KEY-----', re.I)
PEM_END = re.compile(rb'-----END\b', re.I)
REFUSAL_REASONS = frozenset((
    'not-started', 'not-done', 'no-status', 'results-too-large',
    'forbidden-member', 'credential-shaped', 'archive-error',
))
FETCH_STAGES = frozenset(('download', 'digest', 'extract', 'limits', 'deadline', 'systemd-run', 'start-window', 'other'))
GUEST_ACTIONS = frozenset(('init', 'fetch', 'compact', 'push'))


class ResultsRefused(ValueError):
    def __init__(self, reason, message, **details):
        super().__init__(message)
        self.reason = reason if reason in REFUSAL_REASONS else 'archive-error'
        self.details = {k: v for k, v in details.items() if type(v) is int and v >= 0}


def results_limits(plan=None, *, strict=False):
    if plan is None:
        return MAX_RESULTS, MAX_RESULTS
    if not isinstance(plan, dict):
        raise ValueError('invalid sealed plan for results limits')
    probe = plan.get('probe')
    processes = probe['processes'] if isinstance(probe, dict) and type(probe.get('processes')) is int and probe['processes'] > 0 else 0
    members = plan.get('members')
    if isinstance(members, dict):
        processes += len(members)
    else:
        for row in plan.get('rows', ()):
            if isinstance(row, dict):
                for key in ('aPairs', 'bPairs'):
                    count = row.get(key, 0)
                    if type(count) is int and count > 0:
                        processes += 2 * count
    # derived_compressed is a seal-time check only; runtime wire cap is MAX_RESULTS_COMPRESSED.
    derived_compressed = BASE_RESULTS_COMPRESSED + processes * PER_PROCESS_COMPRESSED
    expanded = MAX_RESULTS + processes * PER_PROCESS_EXPANDED
    if strict and (derived_compressed > MAX_RESULTS_COMPRESSED or expanded > MAX_RESULTS_EXPANDED):
        raise ValueError(
            f'plan results cap exceeds hard ceiling ({derived_compressed} > {MAX_RESULTS_COMPRESSED}'
            f' or {expanded} > {MAX_RESULTS_EXPANDED})'
        )
    return MAX_RESULTS_COMPRESSED, min(MAX_RESULTS_EXPANDED, expanded)


def plan_results_limits(archive, *, strict=False):
    with tarfile.open(archive, 'r:gz') as stream:
        member = stream.getmember('plan.json')
        if not member.isfile() or not 0 < member.size <= MAX_BLOB_BYTES:
            raise ValueError('invalid sealed plan.json member')
        plan = json.loads(stream.extractfile(member).read())
    return results_limits(plan, strict=strict)


def _save_limits(limits, path=None):
    target = Path(path) if path is not None else (TEMP / 'results-limits.json')
    temp = target.with_suffix('.tmp')
    temp.write_text(json.dumps({'maxCompressedBytes': limits[0], 'maxExpandedBytes': limits[1]}))
    temp.chmod(0o600)
    temp.replace(target)


def _open_regular(path):
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    except OSError:
        return None, None
    try:
        st = os.fstat(fd)
        if not stat.S_ISREG(st.st_mode) or (st.st_mode & 0o7000):
            os.close(fd)
            return None, None
        return fd, st
    except OSError:
        os.close(fd)
        return None, None


def _read_saved_limits():
    path = TEMP / 'results-limits.json'
    if not os.path.lexists(path):
        return results_limits(None), False
    fd, st = _open_regular(path)
    if fd is None or not 0 < st.st_size <= 512:
        if fd is not None:
            os.close(fd)
        return results_limits(None), True
    try:
        with os.fdopen(fd, 'rb') as stream:
            data = json.loads(stream.read(513).decode('utf-8'))
        comp, exp = data.get('maxCompressedBytes'), data.get('maxExpandedBytes')
        if type(comp) is int and type(exp) is int and 0 < comp <= MAX_RESULTS_COMPRESSED and 0 < exp <= MAX_RESULTS_EXPANDED:
            return (comp, exp), False
    except (OSError, UnicodeDecodeError, ValueError, TypeError):
        pass
    return results_limits(None), True


def _saved_limits():
    return _read_saved_limits()[0]


@contextmanager
def transfer_window(deadline):
    remain=deadline-time.time()
    if remain<=0:
        raise TimeoutError('blob transfer deadline exceeded')
    def expired(*_):
        raise TimeoutError('blob transfer deadline exceeded')
    old=signal.signal(signal.SIGALRM,expired)
    try:
        signal.setitimer(signal.ITIMER_REAL,remain)
        yield
    finally:
        signal.setitimer(signal.ITIMER_REAL,0)
        signal.signal(signal.SIGALRM,old)


def blob_connection(account,timeout):
    # Explicit context avoids default-context TLS key logging via ambient configuration.
    context=ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.load_default_certs()
    context.minimum_version=ssl.TLSVersion.TLSv1_2
    return http.client.HTTPSConnection(account+'.blob.core.windows.net',timeout=timeout,context=context)


# Only paths emitted by the worker, not any file beneath a results directory.
SAFE_COMPONENT = r'[A-Za-z0-9][A-Za-z0-9_.-]{0,127}'
PROCESS = r'(?:observations\.json|direct\.json|environment\.json|server(?:\.ready\.json|-summary\.json|\.log)|execution/(?:console\.log|process\.json))'
RESULT_MEMBER = re.compile(r'(?:results/(?:host|probe|members|session-start|session-status|recovery-diagnostic)\.json|results/session\.log|'
    rf'results/probe/probe-[0-9]{{2}}\.json|results/(?:probe/)?{SAFE_COMPONENT}/{PROCESS}|'
    rf'prelude/(?:applied\.json|raw/(?:ordering|[a-f0-9]{{16}}-(?:calibration|baseline|candidate))\.json|raw/(?:{SAFE_COMPONENT}/){{0,3}}(?:{PROCESS}|job\.json|pairs\.json))|'
    rf'bytes/(?:{SAFE_COMPONENT}/){{0,3}}(?:{PROCESS}|job\.json|pairs\.json)|'
    rf'suite/(?:{SAFE_COMPONENT}/){{0,2}}(?:{SAFE_COMPONENT}\.trx|{SAFE_COMPONENT}\.json|{SAFE_COMPONENT}\.log))')
SENSITIVE = re.compile(rb'-----BEGIN [A-Z ]*PRIVATE KEY-----|(?:^|[?&\s])(?:sig=[\w%+/=-]{16,}|sv=20\d\d-\d\d-\d\d&[^\r\n]*sr=b)', re.I)
SENSITIVE_PATH = re.compile(r'(?i)(?:^|/)(?:\.(?:azure|ssh|env|git)|secrets|credentials(?:\.json)?|id_(?:rsa|ed25519)|[^/]+\.(?:pfx|p12|key|pem))(?=/|$)')


def check_results(path, *, limits=None):
    max_compressed, max_expanded = results_limits(None) if limits is None else limits
    compressed = path.stat().st_size
    if compressed <= 0:
        raise ResultsRefused('archive-error', 'results archive is empty')
    if compressed > max_compressed:
        raise ResultsRefused('results-too-large', 'results archive exceeds admitted size',
                             compressedBytes=compressed, maxCompressedBytes=max_compressed,
                             maxExpandedBytes=max_expanded)
    seen, expanded = set(), 0
    with tarfile.open(path, 'r:gz') as stream:
        for entry in stream:
            name = entry.name
            if (name in seen or not RESULT_MEMBER.fullmatch(name) or SENSITIVE_PATH.search(name)
                    or any(part in ('.', '..') for part in name.split('/')) or not entry.isreg()
                    or entry.mode & 0o7000):
                raise ResultsRefused('forbidden-member', 'results archive has a forbidden member')
            expanded += entry.size
            if expanded > max_expanded:
                raise ResultsRefused('results-too-large', 'results archive exceeds expanded limit',
                                     compressedBytes=compressed, maxCompressedBytes=max_compressed,
                                     expandedBytes=expanded, maxExpandedBytes=max_expanded)
            seen.add(name)
            for line, contents in enumerate(stream.extractfile(entry), 1):
                if SENSITIVE.search(contents):
                    raise ResultsRefused('credential-shaped', f'credential-shaped contents at admitted member {name}:{line}')
    if not seen or not any(n in seen for n in ('results/session-status.json', 'results/recovery-diagnostic.json')):
        raise ResultsRefused('no-status', 'results archive has no status')
    partial = 'results/recovery-diagnostic.json' in seen
    if partial and (compressed > MAX_DIAGNOSTIC or expanded > MAX_DIAGNOSTIC):
        raise ResultsRefused('results-too-large', 'recovery diagnostic exceeds admitted diagnostic size',
                             compressedBytes=compressed, maxCompressedBytes=MAX_DIAGNOSTIC,
                             expandedBytes=expanded, maxExpandedBytes=MAX_DIAGNOSTIC)
    return partial


def initialise():
    TEMP.mkdir(mode=0o700, exist_ok=True)
    keys = {}
    for name, path in (('payload', PAYLOAD_KEY), ('results', RESULTS_KEY)):
        if not path.exists():
            subprocess.run(['openssl', 'genpkey', '-algorithm', 'RSA', '-pkeyopt', 'rsa_keygen_bits:3072', '-out', str(path)],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=20)
            path.chmod(0o600)
        keys[name] = subprocess.check_output(['openssl', 'pkey', '-in', str(path), '-pubout'],
                                              stderr=subprocess.DEVNULL, timeout=10).decode('ascii')
    return keys


def unwrap(name, key):
    sealed = base64.b64decode(os.environ.pop('CHRYSALIS_SEALED_' + name.upper()), validate=True)
    sas = subprocess.check_output(['openssl', 'pkeyutl', '-decrypt', '-inkey', str(key), '-pkeyopt', 'rsa_padding_mode:oaep',
                                   '-pkeyopt', 'rsa_oaep_md:sha256'], input=sealed, stderr=subprocess.DEVNULL,
                                  env={'PATH': '/usr/bin:/bin'}, timeout=10).decode('ascii')
    if not re.fullmatch(r'[A-Za-z0-9%=&+_.:/-]{40,318}', sas):
        raise ValueError('malformed sealed credential')
    return sas


def download(account, name, sas, target, limit, deadline):
    conn = blob_connection(account, 8)
    digest, size = hashlib.sha256(), 0
    try:
        with transfer_window(deadline):
            conn.request('GET', '/payload/' + name + '?' + sas)
            response = conn.getresponse()
            length = response.getheader('Content-Length')
            if response.status != 200 or length is None or not 0 < int(length) <= limit:
                raise ValueError('blob status or length invalid')
            with target.open('xb') as out:
                while chunk := response.read(min(1024 * 1024, limit - size + 1)):
                    size += len(chunk)
                    if size > limit:
                        raise ValueError('blob size exceeded')
                    digest.update(chunk)
                    out.write(chunk)
            if size != int(length):
                raise ValueError('blob truncated')
        return digest.hexdigest()
    finally:
        conn.close()


def upload(account, name, sas, path, limit, until, *, unique=False):
    conn = blob_connection(account, 30)
    try:
        size = path.stat().st_size
        if not 0 < size <= limit:
            raise ValueError('blob exceeds admitted size')
        headers = {'Content-Length': str(size), 'Content-Type': 'application/gzip',
                   'x-ms-blob-type': 'BlockBlob', 'x-ms-version': '2023-11-03'}
        if unique:
            headers['If-None-Match'] = '*'
        with transfer_window(until), path.open('rb') as stream:
            conn.request('PUT', '/payload/' + name + '?' + sas, body=stream, headers=headers)
            if conn.getresponse().status != 201:
                raise ValueError('private blob upload failed')
    finally:
        conn.close()


REMOTE_SESSION = """#!/bin/bash
set -euo pipefail
cd /opt/chrysalis
exec > session.log 2>&1
export PYTHONHOME="$PWD/python" PYTHONNOUSERSITE=1 PYTHONDONTWRITEBYTECODE=1
export LD_LIBRARY_PATH="$PWD/python/lib"
export DOTNET_ROOT="$PWD/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
"$PWD/python/bin/python3.12" "$PWD/scripts/gate_bundle.py" verify "$PWD"
exec "$PWD/python/bin/python3.12" "$PWD/scripts/gate_bundle.py" run "$PWD"
"""


def _record_fetch_failure(stage):
    safe_stage = stage if stage in FETCH_STAGES else 'other'
    try:
        TEMP.mkdir(mode=0o700, exist_ok=True)
        target = TEMP / 'fetch-failure.json'
        temp = target.with_suffix('.tmp')
        fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        try:
            os.write(fd, json.dumps({'reason': 'not-started', 'stage': safe_stage}, separators=(',', ':')).encode('ascii'))
        finally:
            os.close(fd)
        temp.replace(target)
    except OSError:
        pass


def _fetch_failure():
    path = TEMP / 'fetch-failure.json'
    try:
        st = path.lstat()
        if not stat.S_ISREG(st.st_mode) or (st.st_mode & 0o7000) or not 0 < st.st_size <= 256:
            return None
        data = json.loads(path.read_text())
        if isinstance(data, dict) and data.get('reason') == 'not-started' and data.get('stage') in FETCH_STAGES:
            return data['stage']
    except (OSError, ValueError):
        pass
    return None


def fetch(account, digest, suite_digest, epoch, start_by, cutoff, region):
    stage = 'other'
    _record_fetch_failure(stage)
    try:
        if (not re.fullmatch(r'chry[0-9a-f]{16}', account)
                or not re.fullmatch(r'[0-9a-f]{64}', digest)
                or region not in ('eastus2', 'eastus')):
            raise ValueError('invalid work request')
        WORK.mkdir(mode=0o700, exist_ok=False)
        try:
            for name, expected in (('payload', digest), ('suite', suite_digest)):
                if expected == '-':
                    continue
                archive = TEMP / (name + '.tar.gz')
                stage = 'download'
                got = download(account, name, unwrap(name, PAYLOAD_KEY), archive, MAX_BLOB_BYTES, start_by - 1)
                stage = 'digest'
                if got != expected:
                    raise ValueError('archive digest differs')
                if name == 'payload':
                    stage = 'limits'
                    _save_limits(plan_results_limits(archive))
                stage = 'extract'
                root = WORK if name == 'payload' else WORK / 'suite-input'
                with tarfile.open(archive) as members:
                    members.extractall(root, filter='data')
                archive.unlink()
        finally:
            PAYLOAD_KEY.unlink(missing_ok=True)
        stage = 'other'
        script = TEMP / 'remote-session.sh'
        script.write_text(REMOTE_SESSION)
        stage = 'deadline'
        now = time.time()
        if not epoch <= now < start_by or now >= cutoff:
            raise ValueError('worker start deadline expired')
        stage = 'systemd-run'
        cmd = ['systemd-run', '--quiet', '--remain-after-exit', '--unit=' + UNIT, '--working-directory=' + str(WORK),
               '--property=RuntimeMaxSec=' + str(max(1, int(cutoff - now))),
               '--setenv=P2_MACHINE_CREATED_UNIX=' + str(epoch), '--setenv=P2_MEASURING_PLATFORM=azure',
               '--setenv=P2_AZURE_REGION=' + region, '/bin/bash', str(script)]
        subprocess.run(cmd, env={'PATH': '/usr/bin:/bin'}, capture_output=True, check=True, timeout=8)
        # A delayed unit dispatch must not make a late start look on-time.
        stage = 'start-window'
        stamp = subprocess.check_output(['systemctl', 'show', UNIT + '.service', '-p', 'ExecMainStartTimestamp', '--value'],
                                        stderr=subprocess.DEVNULL, timeout=4).decode().strip()
        started = int(subprocess.check_output(['date', '-d', stamp, '+%s'], stderr=subprocess.DEVNULL, timeout=4))
        if not epoch <= started < start_by:
            raise ValueError('worker started outside the allowed window')
        (TEMP / 'fetch-failure.json').unlink(missing_ok=True)
        return {'started': started}
    except Exception:
        _record_fetch_failure(stage)
        raise


def compact():
    state = subprocess.check_output(['systemctl', 'show', UNIT + '.service', '-p', 'SubState', '--value'],
                                    stderr=subprocess.DEVNULL, timeout=5).strip()
    return {'done': state in (b'dead', b'exited', b'failed')}


def _clean_status_bytes():
    if (WORK / 'results').is_symlink():
        return None
    fd, st = _open_regular(WORK / 'results/session-status.json')
    if fd is None or not 0 < st.st_size <= MAX_STATUS_BYTES:
        if fd is not None:
            os.close(fd)
        return None
    try:
        with os.fdopen(fd, 'rb') as stream:
            raw = stream.read(MAX_STATUS_BYTES + 1)
        if not 0 < len(raw) <= MAX_STATUS_BYTES or SENSITIVE.search(raw):
            return None
        parsed = json.loads(raw.decode('utf-8'))
        if isinstance(parsed, dict):
            json.dumps(parsed, allow_nan=False)
            return raw
    except (OSError, UnicodeDecodeError, ValueError):
        pass
    return None


def _unit_status():
    try:
        raw = subprocess.check_output(
            ['systemctl', 'show', UNIT + '.service', '-p', 'LoadState,Result,ExecMainCode,ExecMainStatus'],
            stderr=subprocess.DEVNULL, timeout=4).decode('ascii', errors='replace')
    except (OSError, subprocess.SubprocessError):
        return {}
    props = {k: v.strip() for line in raw.splitlines() if '=' in line for k, v in (line.split('=', 1),)}
    if props.get('LoadState') != 'loaded':
        return {}
    info = {'unitResult': props['Result']} if re.fullmatch(r'[a-z-]{1,32}', props.get('Result', '')) else {}
    for field, key in (('ExecMainCode', 'execMainCode'), ('ExecMainStatus', 'execMainStatus')):
        if re.fullmatch(r'[0-9]{1,9}', props.get(field, '')):
            info[key] = int(props[field])
    return info


def _read_redacted_log(path, max_bytes):
    fd, st = _open_regular(path)
    if fd is None or (max_bytes <= 0 and st.st_size > 0):
        if fd is not None:
            os.close(fd)
        return None
    try:
        truncated = st.st_size > max_bytes
        with os.fdopen(fd, 'rb') as stream:
            if truncated:
                stream.seek(st.st_size - max_bytes)
            raw = stream.read(max_bytes + 1)
        if len(raw) > max_bytes:
            raw, truncated = raw[-max_bytes:], True
    except OSError:
        return None
    if truncated:
        nl = raw.find(b'\n')
        raw = b'' if nl < 0 else raw[nl + 1:]
    kept, total, in_pem, seen_pem_begin, redacted = deque(), 0, False, False, 0
    for line in io.BytesIO(raw):
        if in_pem:
            red = True
            if PEM_END.search(line):
                in_pem = False
        elif PEM_BEGIN.search(line):
            red = True
            seen_pem_begin = True
            if not PEM_END.search(line):
                in_pem = True
        elif not seen_pem_begin and PEM_END.search(line):
            # Tail started inside a private key body: redact all kept lines through END.
            kept = deque(
                (REDACTED_LINE + (b'\n' if c.endswith(b'\n') else b''), True)
                for c, _ in kept
            )
            total = sum(len(c) for c, _ in kept)
            redacted = len(kept)
            seen_pem_begin = True
            red = True
        else:
            red = bool(SENSITIVE.search(line))
        chunk = (REDACTED_LINE + (b'\n' if line.endswith(b'\n') else b'')) if red else line
        if len(chunk) > max_bytes:
            kept.clear()
            total = 0
            redacted = 0
            truncated = True
            continue
        kept.append((chunk, red))
        total += len(chunk)
        redacted += int(red)
        while total > max_bytes:
            old_chunk, old_red = kept.popleft()
            total -= len(old_chunk)
            redacted -= int(old_red)
            truncated = True
    if raw and not kept and max_bytes < len(REDACTED_LINE) + 1:
        return None
    return b''.join(c for c, _ in kept), truncated, redacted


def _collect_diagnostic_logs():
    included, counts = [], {'logsIncluded': 0, 'logsTruncated': 0, 'logsSkipped': 0, 'linesRedacted': 0}
    budget = DIAG_TOTAL_LOG_BYTES
    def add_log(path, arc, per_cap):
        nonlocal budget
        if budget < 512:
            counts['logsSkipped'] += 1
            return
        result = _read_redacted_log(path, min(per_cap, (budget - 512) & ~511))
        if result is None:
            counts['logsSkipped'] += 1
            return
        data, truncated, redacted = result
        included.append((arc, data))
        budget -= 512 + ((len(data) + 511) & ~511)
        counts['logsIncluded'] += 1
        counts['logsTruncated'] += int(truncated)
        counts['linesRedacted'] += redacted

    results_dir = WORK / 'results'
    res_session, root_session = results_dir / 'session.log', WORK / 'session.log'
    if not results_dir.is_symlink() and os.path.lexists(res_session):
        add_log(res_session, 'results/session.log', DIAG_SESSION_LOG_BYTES)
    elif os.path.lexists(root_session):
        add_log(root_session, 'results/session.log', DIAG_SESSION_LOG_BYTES)

    candidates = []
    for folder in ('results', 'prelude', 'bytes'):
        base = WORK / folder
        if base.is_symlink() or not base.is_dir():
            continue
        for dirpath, dirs, files in os.walk(base, followlinks=False):
            dirs[:] = [d for d in sorted(dirs) if not (Path(dirpath) / d).is_symlink()]
            for name in sorted(files):
                if name not in ('server.log', 'console.log'):
                    continue
                member = Path(dirpath) / name
                arc = member.relative_to(WORK).as_posix()
                if not (arc.endswith('/server.log') or arc.endswith('/execution/console.log')):
                    continue
                if not RESULT_MEMBER.fullmatch(arc) or SENSITIVE_PATH.search(arc):
                    continue
                try:
                    st = member.lstat()
                except OSError:
                    counts['logsSkipped'] += 1
                    continue
                if not stat.S_ISREG(st.st_mode) or (st.st_mode & 0o7000):
                    counts['logsSkipped'] += 1
                    continue
                candidates.append((st.st_mtime_ns, arc, member))
    candidates.sort(key=lambda item: (-item[0], item[1]))
    if len(candidates) > DIAG_MAX_PROCESS_LOGS:
        counts['logsSkipped'] += len(candidates) - DIAG_MAX_PROCESS_LOGS
        candidates = candidates[:DIAG_MAX_PROCESS_LOGS]
    for _, arc, member in candidates:
        add_log(member, arc, DIAG_PROCESS_LOG_BYTES)
    return included, counts


def _write_diagnostic(path, payload, status_bytes=None, logs=()):
    entries = [('results/recovery-diagnostic.json', (json.dumps(payload, separators=(',', ':'), allow_nan=False) + '\n').encode('ascii'))]
    if status_bytes is not None:
        entries.append(('results/session-status.json', status_bytes))
    entries.extend(logs)
    with tarfile.open(path, 'w:gz') as stream:
        for arc, data in entries:
            entry = tarfile.TarInfo(arc)
            entry.size = len(data)
            stream.addfile(entry, io.BytesIO(data))
    check_results(path, limits=(MAX_DIAGNOSTIC, MAX_DIAGNOSTIC))


def _write_diagnostic_fallback(path, base_payload, limits_fallback=False):
    zero = {'logsIncluded': 0, 'logsTruncated': 0, 'logsSkipped': 0, 'linesRedacted': 0}
    try:
        _write_diagnostic(path, {**base_payload, **zero}, _clean_status_bytes())
        return
    except Exception:
        pass
    minimal = {'diagnosticOnly': True, 'reason': 'archive-error'}
    if limits_fallback:
        minimal['limitsFallback'] = True
    _write_diagnostic(path, minimal)


def archive_results(path, partial, *, limits=None, reason=None):
    # Do not copy an offending archive into diagnostics. Use a fixed, bounded marker,
    # clean session-status.json when valid, and bounded redacted guest logs.
    max_compressed, max_expanded = results_limits(None) if limits is None else limits
    refusal_reason = reason if reason in REFUSAL_REASONS else ('not-done' if partial else 'archive-error')
    details = {}
    try:
        if not partial:
            members, expanded_disk = [], 0
            for folder in ('results', 'prelude', 'bytes', 'suite'):
                base = WORK / folder
                if base.is_symlink():
                    raise ResultsRefused('forbidden-member', 'symlinked results folder')
                if not base.is_dir():
                    continue
                for member in sorted(base.rglob('*')):
                    st = member.lstat()
                    if not stat.S_ISDIR(st.st_mode):
                        if stat.S_ISREG(st.st_mode):
                            expanded_disk += st.st_size
                        members.append((member, member.relative_to(WORK).as_posix()))
            session_log = WORK / 'session.log'
            if os.path.lexists(session_log) and not os.path.lexists(WORK / 'results/session.log'):
                st = session_log.lstat()
                if stat.S_ISREG(st.st_mode):
                    expanded_disk += st.st_size
                members.append((session_log, 'results/session.log'))
            if expanded_disk > max_expanded:
                raise ResultsRefused('results-too-large', 'results tree exceeds expanded limit',
                                     expandedBytes=expanded_disk, maxExpandedBytes=max_expanded,
                                     maxCompressedBytes=max_compressed)
            with tarfile.open(path, 'w:gz') as stream:
                for member, arc in members:
                    stream.add(member, arcname=arc, recursive=False)
            if check_results(path, limits=(max_compressed, max_expanded)):
                raise ResultsRefused('forbidden-member', 'unexpected recovery marker in worker output')
            return False
    except ResultsRefused as exc:
        refusal_reason = exc.reason
        details = dict(exc.details)
    except (ValueError, OSError, tarfile.TarError, EOFError, zlib.error):
        refusal_reason = 'archive-error'
        details = {}
    _, limits_fallback = _read_saved_limits()
    base_payload = {'diagnosticOnly': True, 'reason': refusal_reason}
    if refusal_reason == 'results-too-large':
        base_payload.update(details)
    elif refusal_reason == 'not-started':
        stage = _fetch_failure()
        if stage is not None:
            base_payload['stage'] = stage
    if limits_fallback:
        base_payload['limitsFallback'] = True
    base_payload.update(_unit_status())
    try:
        logs, log_counts = _collect_diagnostic_logs()
        _write_diagnostic(path, {**base_payload, **log_counts}, _clean_status_bytes(), logs)
    except Exception:
        _write_diagnostic_fallback(path, base_payload, limits_fallback)
    return True


def push(account, until, final):
    record, path = TEMP / 'results.json', TEMP / 'results.tar.gz'
    limits, limits_fallback = _read_saved_limits()
    if record.is_file():
        data = json.loads(record.read_text())
        if hashlib.sha256(path.read_bytes()).hexdigest() != data['sha256']:
            raise ValueError('recorded result changed')
    else:
        if _fetch_failure() is not None:
            partial_reason = 'not-started'
        else:
            wait, done = time.monotonic() + 10, False
            try:
                while not (done := compact()['done']) and time.monotonic() < wait:
                    time.sleep(.5)
                if not done:
                    done = compact()['done']
            except (OSError, subprocess.SubprocessError):
                done = False
            status_file = WORK / 'results/session-status.json'
            has_status = not (WORK / 'results').is_symlink() and not status_file.is_symlink() and status_file.is_file()
            partial_reason = 'not-done' if not done else ('no-status' if not has_status else None)
        try:
            partial = archive_results(path, partial_reason is not None, limits=limits, reason=partial_reason)
        except Exception:
            base_payload = {'diagnosticOnly': True, 'reason': partial_reason or 'archive-error'}
            if limits_fallback:
                base_payload['limitsFallback'] = True
            _write_diagnostic_fallback(path, base_payload, limits_fallback)
            partial = True
        data = dict(sha256=hashlib.sha256(path.read_bytes()).hexdigest(), size=path.stat().st_size, partial=partial)
        # Durable publication before uploading; a repeated push cannot rebuild the archive.
        temp = record.with_suffix('.tmp')
        temp.write_text(json.dumps(data))
        temp.replace(record)
    try:
        upload(account, 'results', unwrap('results', RESULTS_KEY), path, MAX_DIAGNOSTIC if data['partial'] else limits[0], until)
    finally:
        if final:
            RESULTS_KEY.unlink(missing_ok=True)
    return data


if __name__ == '__main__':
    action = 'unknown'
    try:
        os.umask(0o077)
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
        action, *args = sys.argv[1:]
        if action == 'init' and not args:
            result = initialise()
        elif action == 'fetch':
            account, digest, suite, epoch, start_by, cutoff, region = args
            result = fetch(account, digest, suite, int(epoch), int(start_by), int(cutoff), region)
        elif action == 'compact' and not args:
            result = compact()
        elif action == 'push':
            account, until, last = args
            result = push(account, int(until), last == '1')
        else:
            raise ValueError('unsupported guest action')
        print('CHRYSALIS:' + json.dumps(result, separators=(',', ':'), allow_nan=False))
    except Exception as exc:
        step = action if action in GUEST_ACTIONS else 'unknown'
        kind = type(exc).__name__
        if not re.fullmatch(r'[A-Za-z_]{1,40}', kind):
            kind = 'Exception'
        print('CHRYSALIS:' + json.dumps({'error': 'guest control step failed', 'step': step, 'type': kind},
                                        separators=(',', ':'), allow_nan=False))
        sys.exit(75)
