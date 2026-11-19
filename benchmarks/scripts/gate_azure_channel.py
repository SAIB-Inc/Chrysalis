"""Scoped Managed Run Command and Blob transfer; no SSH or plaintext SAS logs."""
from datetime import datetime,timezone
import base64
from hashlib import sha256
from http.client import HTTPException
import json
import re
import shlex
import subprocess
from tarfile import TarError
import time
from urllib.parse import parse_qs

from gate_azure_proof import AzureError, storage_account
from gate_azure_guard import GuardFailure, WRITE_END, COPY_END
from gate_azure_guest import (
    GUEST_ACTIONS, MAX_BLOB_BYTES, MAX_DIAGNOSTIC,
    check_results, download, plan_results_limits, results_limits, upload,
)

STORAGE_API='2023-05-01'
VM_API='2024-07-01'
RUN_COMMAND_SECONDS = 130
PUSH_SECONDS = 160
WORKER_START = 483
STATUS_SCRIPT = '#!/bin/bash\nexec 2>/dev/null\nexec /usr/bin/python3 /usr/local/sbin/chrysalis-run-channel compact\n'


def storage_spec(region):
    return dict(location=region,sku=dict(name='Standard_LRS'),kind='StorageV2',properties=dict(
        accessTier='Hot',allowBlobPublicAccess=False,supportsHttpsTrafficOnly=True,
        minimumTlsVersion='TLS1_2',allowSharedKeyAccess=True,publicNetworkAccess='Enabled'))


def signed_blob(az, account, key, name, rights):
    if (name, rights) not in {(blob, direction) for blob in ('payload', 'suite', 'results') for direction in ('r', 'cw')}:
        raise GuardFailure('unrecognized blob or transfer direction')
    minutes = 15 if name == 'results' else 30
    code = """from azure.storage.blob import generate_blob_sas
import sys,json,resource
from datetime import datetime,timedelta,timezone
resource.setrlimit(resource.RLIMIT_CORE,(0,0))
account,key,name,rights,minutes=json.load(sys.stdin)
print(generate_blob_sas(account,'payload',name,account_key=key,permission=rights,
    expiry=datetime.now(timezone.utc)+timedelta(minutes=minutes),protocol='https'))
"""
    p = subprocess.run([str(az.executable.resolve().parent / 'python'), '-IBc', code],
                       input=json.dumps([account, key, name, rights, minutes]), capture_output=True, text=True, timeout=12)
    token = p.stdout.strip()
    if p.returncode or not re.fullmatch(r'[A-Za-z0-9%=&+_.:/-]{40,318}', token):
        raise GuardFailure('blob SAS generation failed; details suppressed')
    try:
        fields = parse_qs(token, strict_parsing=True)
        valid = set(fields) == {'se', 'sp', 'spr', 'sv', 'sr', 'sig'} and all(len(v) == 1 for v in fields.values())
        expiry = datetime.fromisoformat(fields['se'][0].replace('Z', '+00:00'))
        valid &= fields['sr'] == ['b'] and fields['spr'] == ['https'] and fields['sp'] == [rights]
        valid &= 0 < (expiry - datetime.now(timezone.utc)).total_seconds() <= 60 * minutes
        if not valid:
            raise ValueError
    except (KeyError, ValueError):
        raise GuardFailure('blob SAS failed the HTTPS, rights or expiry check') from None
    return token


def seal(public,token):
    from cryptography.hazmat.primitives import hashes,serialization
    from cryptography.hazmat.primitives.asymmetric import padding,rsa
    try:
        key=serialization.load_pem_public_key(public.encode('ascii'))
        if not isinstance(key,rsa.RSAPublicKey) or key.key_size!=3072:
            raise ValueError('unexpected public key')
        return base64.b64encode(key.encrypt(token.encode('ascii'),padding.OAEP(
            mgf=padding.MGF1(hashes.SHA256()),algorithm=hashes.SHA256(),label=None))).decode('ascii')
    except (ValueError,TypeError,UnicodeError):
        raise GuardFailure('guest ephemeral envelope sealing failed') from None


def control_body(region,script,secret=None):
    props=dict(source=dict(script=script),asyncExecution=True,timeoutInSeconds=RUN_COMMAND_SECONDS)
    if secret is not None:
        props['protectedParameters']=[dict(name='CHRYSALIS_SEALED_' + name.upper(), value=value) for name, value in secret.items()]
    return dict(location=region,properties=props)


def _parse_guest_error(output):
    try:
        data = get_summary(output)
    except GuardFailure:
        return None
    step, kind = data.get('step'), data.get('type')
    if (data.get('error') == 'guest control step failed'
            and step in (GUEST_ACTIONS | {'unknown'})
            and isinstance(kind, str) and re.fullmatch(r'[A-Za-z_]{1,40}', kind)):
        return {'step': step, 'type': kind}
    return None


def get_summary(message):
    if not isinstance(message,str) or len(message)>4096:
        raise GuardFailure('oversize or missing Run Command output')
    lines=[line.removeprefix('CHRYSALIS:') for line in message.splitlines() if line.startswith('CHRYSALIS:')]
    if len(lines)!=1:
        raise GuardFailure('missing compact Run Command result')
    try:
        data = json.loads(lines[0])
        if not isinstance(data, dict):
            raise ValueError
    except ValueError:
        raise GuardFailure('malformed compact Run Command result') from None
    return data


def command(draw,label,script,until,*,sealed=None):
    if not re.fullmatch(r'(init|kick|status-(?:0[0-9]|1[0-8])|push-[12])',label):
        raise GuardFailure('unrecognized guest control action')
    if until-time.time()<=1:
        raise GuardFailure('no time left to submit guest Run Command')
    path=draw.path('Compute','virtualMachines')+'/runCommands/'+label
    draw.put(path,VM_API,control_body(draw.region,script,sealed),work=True,until=until)
    url=path+'?api-version='+VM_API+'&$expand=instanceView'
    selector='properties.instanceView.{state:executionState,code:exitCode,output:output}'
    while time.time()<until:
        try:
            result=draw.az.rest('get',url,query=selector,timeout=min(10,max(.1,until-time.time())))
        except AzureError as e:
            if str(e) not in ('ResourceNotFound','timeout','TooManyRequests','ServiceUnavailable'):
                raise
        else:
            if result and isinstance(result,dict) and result.get('state') in ('Succeeded','Failed','Canceled','TimedOut'):
                if result['state']!='Succeeded' or result['code']!=0:
                    err = _parse_guest_error(result.get('output'))
                    if err is not None and getattr(draw, 'guest_error', None) is None:
                        draw.guest_error = {'command': label, **err}
                    raise GuardFailure(f'guest Run Command {label} did not succeed')
                return get_summary(result['output'])
        time.sleep(min(5,max(0,until-time.time())))
    raise GuardFailure(f'guest Run Command {label} exceeded its work deadline')


def boot_script(group, shutdown, helper):
    # Protect the same guest guard: measure only after cloud-init and the exact
    # timer plus effective group-delete action are verified. All output but
    # the explicitly framed result goes to /dev/null, not extension logs.
    from gate_azure import calendar, USER
    expected=f'OnCalendar={calendar(shutdown)}'
    return f'''#!/bin/bash
set -euo pipefail
exec 2>/dev/null
limit=$(( $(date +%s) + 95 ))
while [ "$(date +%s)" -lt "$limit" ]; do
  next=$(systemctl show chrysalis-guest-cleanup.timer -p NextElapseUSecRealtime --value || true)
  if cloud_status=$(cloud-init status 2>/dev/null) &&
     grep -Fxq 'status: done' <<<"$cloud_status" &&
     systemctl is-active --quiet chrysalis-guest-cleanup.timer &&
     systemctl is-enabled --quiet chrysalis-guest-cleanup.timer &&
     systemctl cat chrysalis-guest-cleanup.timer | grep -Fxq {shlex.quote(expected)} &&
     systemctl cat chrysalis-guest-cleanup.service | grep -Fxq {shlex.quote('ExecStart=/usr/bin/python3 /usr/local/sbin/chrysalis-guest-cleanup '+group+' delete')} &&
     test -n "$next" && test "$next" != n/a &&
     test "$(getent passwd {USER} | cut -d: -f7)" = /usr/sbin/nologin &&
     passwd -S {USER} | awk 'END {{exit ($2=="L" ? 0 : 1)}}' &&
     test "$(systemctl is-enabled ssh.service 2>/dev/null)" = masked &&
     test "$(systemctl is-enabled ssh.socket 2>/dev/null)" = masked &&
     ! systemctl is-active --quiet ssh.service ssh.socket &&
     tc qdisc show | grep -Eq '^qdisc tbf .*rate 1Mbit' &&
     /usr/bin/python3 /usr/local/sbin/chrysalis-guest-cleanup {group} check; then
    exec /usr/bin/python3 {helper} init
  fi
  sleep 5
done
exit 75
'''


def create_storage(draw):
    draw.account = storage_account(draw.group)
    draw.store = draw.path('Storage', 'storageAccounts', draw.account)
    draw.put(draw.store, STORAGE_API, storage_spec(draw.region))
    draw.wait(draw.store, STORAGE_API)
    draw.put(draw.store + '/blobServices/default/containers/payload', STORAGE_API, dict(properties=dict(publicAccess='None')))


def storage_key(draw):
    if draw.owner() != draw.group or (draw.guard / 'retired').exists():
        raise GuardFailure('storage key read requires current group ownership')
    key = draw.az.rest('post', draw.store + '/listKeys?api-version=' + STORAGE_API, query="keys[?keyName=='key1'].value | [0]")
    if not isinstance(key, str) or not 40 <= len(key) <= 128:
        raise GuardFailure('scoped storage account key unavailable')
    return key


def upload_inputs(draw, inputs):
    key = storage_key(draw)
    for name, path in inputs.items():
        upload(draw.account, name, signed_blob(draw.az, draw.account, key, name, 'cw'), path,
               MAX_BLOB_BYTES, min(draw.start + WORKER_START, time.time() + 100), unique=True)


def run_channel(draw, inputs, evidence):
    helper = '/usr/local/sbin/chrysalis-run-channel'
    limits = plan_results_limits(inputs['payload'])
    start_by = draw.start + WORKER_START
    init = command(draw, 'init', boot_script(draw.group, draw.guard_end, helper),
                   min(start_by - 1, time.time() + RUN_COMMAND_SECONDS + 15))
    if set(init) != {'payload', 'results'} or not all(isinstance(v, str) for v in init.values()) or init['payload'] == init['results']:
        raise GuardFailure('guest init did not attest both ephemeral keys')
    private_guard = evidence / '.guard/guest-ready'
    private_guard.touch(mode=0o600)
    key = storage_key(draw)
    sealed = {name: seal(init['payload'], signed_blob(draw.az, draw.account, key, name, 'r')) for name in inputs}
    del key
    digests = {name: sha256(path.read_bytes()).hexdigest() for name, path in inputs.items()}
    base = '#!/bin/bash\nexec 2>/dev/null\nexec /usr/bin/python3 ' + helper + ' '
    # Every interpolation is a fixed flag, hash, validated name or sealed integer deadline.
    fetch = f"fetch {draw.account} {digests['payload']} {digests.get('suite', '-')} {draw.start} {start_by} {draw.work_end} {draw.region}"
    try:
        started = command(draw, 'kick', base + fetch + '\n', start_by, sealed=sealed)
        stamp = started.get('started')
        if type(stamp) is not int or not draw.start <= stamp < start_by:
            raise GuardFailure('guest did not prove a timely worker start')
        interval = max(300, draw.cutoff / 18)
        wake = time.time() + interval
        for number in range(19):
            time.sleep(max(0, min(wake, draw.work_end) - time.time()))
            if time.time() >= draw.work_end:
                break
            try:
                result = command(draw, f'status-{number:02}', STATUS_SCRIPT, min(draw.work_end, time.time() + RUN_COMMAND_SECONDS + 15))
            except GuardFailure:
                if time.time() >= draw.work_end:
                    break  # At C, transfer does not wait for a status answer.
                raise
            if result == {'done': True}:
                draw.worker_done = True
                break
            if result != {'done': False}:
                raise GuardFailure('unexpected compact status')
            wake += interval
    except Exception:
        draw.worker_failed = True
        try:
            copy_results(draw, evidence, init['results'], base, limits=limits)
        except Exception:
            pass
        raise
    if not draw.worker_done and time.time() < draw.work_end:
        time.sleep(draw.work_end - time.time())
    return copy_results(draw, evidence, init['results'], base, limits=limits)


def copy_results(draw, evidence, public, base, *, limits=None):
    max_compressed, max_expanded = results_limits(None) if limits is None else limits
    deadline = draw.work_end + COPY_END
    known = None
    target = evidence / '.results-pending'
    for number in (1, 2):
        if time.time() + PUSH_SECONDS > deadline:
            raise GuardFailure('insufficient time to push and verify results')
        key = storage_key(draw)
        envelope = seal(public, signed_blob(draw.az, draw.account, key, 'results', 'cw'))
        until = min(deadline - 20, time.time() + RUN_COMMAND_SECONDS + 15)
        try:
            summary = command(draw, f'push-{number}', base + f'push {draw.account} {int(until)} {int(number == 2)}\n',
                              until, sealed={'results': envelope})
            wire_limit = MAX_DIAGNOSTIC if summary.get('partial') is True else max_compressed
            if (set(summary) != {'sha256', 'size', 'partial'} or not re.fullmatch(r'[0-9a-f]{64}', str(summary['sha256']))
                    or type(summary['size']) is not int or type(summary['partial']) is not bool
                    or not 0 < summary['size'] <= wire_limit
                    or known not in (None, summary)):
                raise ValueError('unexpected or changed push digest')
            known = summary
            read_sas = signed_blob(draw.az, draw.account, key, 'results', 'r')
            try:
                actual = download(draw.account, 'results', read_sas, target, wire_limit, deadline)
            except ValueError:
                # An invalid HTTP status/length or truncated body can follow a good push summary.
                raise GuardFailure('unusable result download') from None
            finally:
                del read_sas
            if (actual != summary['sha256'] or target.stat().st_size != summary['size']
                    or check_results(target, limits=(max_compressed, max_expanded)) != summary['partial']):
                raise ValueError('result digest or allowlist differs')
            (evidence / 'results.sha256').write_text(actual + '\n')
            target.replace(evidence / 'results.tar.gz')
            return summary['partial']
        except (AzureError, GuardFailure, OSError, HTTPException, TarError):
            if number == 2:
                raise GuardFailure('could not obtain bounded guest results') from None
        finally:
            target.unlink(missing_ok=True)
            del key, envelope
