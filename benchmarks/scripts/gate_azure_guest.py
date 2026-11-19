"""Control-channel worker installed by cloud-init; no Azure token is persisted."""
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
import subprocess
import sys
import tarfile
import time

TEMP=Path('/run/chrysalis-control')
WORK=Path('/opt/chrysalis')
PAYLOAD_KEY = TEMP / 'payload.pem'
RESULTS_KEY = TEMP / 'results.pem'
UNIT = 'chrysalis-gate-worker'
MAX_BLOB_BYTES = 160_000_000
MAX_RESULTS = 10_000_000
MAX_DIAGNOSTIC = 1_000_000


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


def check_results(path):
    if not 0 < path.stat().st_size <= MAX_RESULTS:
        raise ValueError('results archive exceeds admitted size')
    seen, expanded = set(), 0
    with tarfile.open(path, 'r:gz') as stream:
        for entry in stream:
            name = entry.name
            expanded += entry.size
            if (name in seen or not RESULT_MEMBER.fullmatch(name) or SENSITIVE_PATH.search(name)
                    or any(part in ('.', '..') for part in name.split('/')) or not entry.isreg()
                    or entry.mode & 0o7000 or expanded > MAX_RESULTS):
                raise ValueError('results archive has a forbidden member or exceeds expanded limit')
            seen.add(name)
            for line, contents in enumerate(stream.extractfile(entry), 1):
                if SENSITIVE.search(contents):
                    raise ValueError(f'credential-shaped contents at admitted member {name}:{line}')
    if not seen or not any(n in seen for n in ('results/session-status.json', 'results/recovery-diagnostic.json')):
        raise ValueError('results archive has no status')
    return 'results/recovery-diagnostic.json' in seen


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


def fetch(account, digest, suite_digest, epoch, start_by, cutoff, region):
    if not re.fullmatch(r'chry[0-9a-f]{16}', account) or not re.fullmatch(r'[0-9a-f]{64}', digest) or region not in ('eastus2', 'eastus'):
        raise ValueError('invalid work request')
    WORK.mkdir(mode=0o700, exist_ok=False)
    try:
        for name, expected in (('payload', digest), ('suite', suite_digest)):
            if expected == '-':
                continue
            archive = TEMP / (name + '.tar.gz')
            if download(account, name, unwrap(name, PAYLOAD_KEY), archive, MAX_BLOB_BYTES, start_by - 1) != expected:
                raise ValueError('archive digest differs')
            root = WORK if name == 'payload' else WORK / 'suite-input'
            with tarfile.open(archive) as members:
                members.extractall(root, filter='data')
            archive.unlink()
    finally:
        PAYLOAD_KEY.unlink(missing_ok=True)
    script = TEMP / 'remote-session.sh'
    script.write_text(REMOTE_SESSION)
    now = time.time()
    if not epoch <= now < start_by or now >= cutoff:
        raise ValueError('worker start deadline expired')
    cmd = ['systemd-run', '--quiet', '--remain-after-exit', '--unit=' + UNIT, '--working-directory=' + str(WORK),
           '--property=RuntimeMaxSec=' + str(max(1, int(cutoff - now))),
           '--setenv=P2_MACHINE_CREATED_UNIX=' + str(epoch), '--setenv=P2_MEASURING_PLATFORM=azure',
           '--setenv=P2_AZURE_REGION=' + region, '/bin/bash', str(script)]
    subprocess.run(cmd, env={'PATH': '/usr/bin:/bin'}, capture_output=True, check=True, timeout=8)
    # A delayed unit dispatch must not make a late start look on-time.
    stamp = subprocess.check_output(['systemctl', 'show', UNIT + '.service', '-p', 'ExecMainStartTimestamp', '--value'],
                                    stderr=subprocess.DEVNULL, timeout=4).decode().strip()
    started = int(subprocess.check_output(['date', '-d', stamp, '+%s'], stderr=subprocess.DEVNULL, timeout=4))
    if not epoch <= started < start_by:
        raise ValueError('worker started outside the allowed window')
    return {'started': started}


def compact():
    state = subprocess.check_output(['systemctl', 'show', UNIT + '.service', '-p', 'SubState', '--value'],
                                    stderr=subprocess.DEVNULL, timeout=5).strip()
    return {'done': state in (b'dead', b'exited', b'failed')}


def archive_results(path, partial):
    # Do not copy an offending archive into diagnostics. Use a fixed, bounded marker.
    try:
        if not partial:
            with tarfile.open(path, 'w:gz') as stream:
                for folder in ('results', 'prelude', 'bytes', 'suite'):
                    for member in sorted((WORK / folder).rglob('*')):
                        if not member.is_dir() or member.is_symlink():
                            stream.add(member, arcname=member.relative_to(WORK).as_posix(), recursive=False)
                if (WORK / 'session.log').is_file() and not (WORK / 'results/session.log').exists():
                    stream.add(WORK / 'session.log', 'results/session.log', recursive=False)
            check_results(path)
            return False
    except (ValueError, OSError, tarfile.TarError):
        pass
    marker = b'{"diagnosticOnly":true}\n'
    with tarfile.open(path, 'w:gz') as stream:
        entry = tarfile.TarInfo('results/recovery-diagnostic.json')
        entry.size = len(marker)
        stream.addfile(entry, io.BytesIO(marker))
    return True


def push(account, until, final):
    record, path = TEMP / 'results.json', TEMP / 'results.tar.gz'
    if record.is_file():
        data = json.loads(record.read_text())
        if hashlib.sha256(path.read_bytes()).hexdigest() != data['sha256']:
            raise ValueError('recorded result changed')
    else:
        wait = time.monotonic() + 10
        while not compact()['done'] and time.monotonic() < wait:
            time.sleep(.5)
        partial = archive_results(path, not compact()['done'] or not (WORK / 'results/session-status.json').is_file())
        data = dict(sha256=hashlib.sha256(path.read_bytes()).hexdigest(), size=path.stat().st_size, partial=partial)
        # Durable publication before uploading; a repeated push cannot rebuild the archive.
        temp = record.with_suffix('.tmp')
        temp.write_text(json.dumps(data))
        temp.replace(record)
    try:
        upload(account, 'results', unwrap('results', RESULTS_KEY), path, MAX_DIAGNOSTIC if data['partial'] else MAX_RESULTS, until)
    finally:
        if final:
            RESULTS_KEY.unlink(missing_ok=True)
    return data


if __name__ == '__main__':
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
    except Exception:
        print('CHRYSALIS:{"error":"guest control step failed"}')
        sys.exit(75)
