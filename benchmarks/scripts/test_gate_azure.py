"""Azure gate safety; the only CLI executable here is the socket-free fake az."""
import base64
from datetime import datetime, timedelta, timezone
from decimal import Decimal
import fcntl
import hashlib
import io
from http.client import IncompleteRead
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import time
from unittest.mock import Mock, patch
import uuid

import pytest

import gate
import gate_azure as azure
import gate_azure_channel as channel
import gate_azure_guard as guard
import gate_azure_guest as guest
import gate_azure_payload as payload
import gate_azure_proof as proof
import gate_bundle as bundle
import gate_schedule as schedules
import gate_time as timing
from test_gate_fly import probe_parts, probe_plan
from test_gate_time import row

HERE = Path(__file__).resolve().parent
SID = str(uuid.UUID(int=101))  # fake; values are not included in evidence
RUN = 'chrysalis-gate-20990405t000101-' + 'ab' * 4
OTHER = 'chrysalis-gate-20990405t000102-' + 'cd' * 4
SUB = '/subscriptions/' + SID


def dump(path, data):
    path.write_text(json.dumps(data))


def archive(path, files):
    with tarfile.open(path, 'w:gz') as stream:
        for name, value in files.items():
            data = value if type(value) is bytes else json.dumps(value).encode()
            entry = tarfile.TarInfo(name)
            entry.size = len(data)
            stream.addfile(entry, io.BytesIO(data))


@pytest.fixture
def fake(tmp_path):
    executable = tmp_path / 'az'
    shutil.copy2(HERE / 'fixtures/azure/az', executable)
    az = proof.Az(executable)
    az.pin()
    yield az
    assert not (tmp_path / 'forbidden-register').exists()


@pytest.fixture
def site(tmp_path, fake, monkeypatch):
    baseline = tmp_path / 'baseline.json'
    baseline_data, _ = proof.inventory(fake)
    dump(baseline, baseline_data)
    local = dict(azureCli=str(fake.executable), azureLock=str(tmp_path / 'go.lock'),
                 azureBaseline=str(baseline), azureLedger=str(tmp_path / 'ledger.json'), azureRemainingProjectionUsd=12.50)
    ledger_path = Path(local['azureLedger'])
    dump(ledger_path, dict(openingBalanceUsd=4.29, attempts={}, lastDailyCheck=None))
    ledger_path.chmod(0o600)
    settings = tmp_path / 'local.json'
    dump(settings, local)
    monkeypatch.setenv('CHRYSALIS_GATE_LOCAL', str(settings))
    out = tmp_path / 'benchmarks/out'
    out.mkdir(parents=True)
    directory = out / 'gate-test'
    directory.mkdir()
    monkeypatch.setattr(gate, 'REPO', tmp_path)
    monkeypatch.setattr(payload, 'BUILD_RUNNER_SHA256', FIXTURE_RUNNER)
    return local, directory


def acquire(path):
    # Expose fd 9 only within the patch in Ledger unit tests; the real fd-9
    # path is exercised in a dedicated subprocess below.
    stream = Path(path).open('a')
    fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
    return stream


@pytest.fixture
def ledger(site, monkeypatch):
    local, _ = site
    with acquire(local['azureLock']) as fd:
        monkeypatch.setattr(azure, 'held_lock', lambda path: path.samefile(Path(fd.name)))
        yield azure.Ledger(local)


def make_draw(fake, tmp_path, *, group=RUN, region='eastus2', start=None, lifetime=1256):
    state = tmp_path / '.guard'
    state.mkdir(exist_ok=True)
    guard.mark(state, 'ready')
    return azure.Draw(fake, group, region, int(time.time()) if start is None else start, state, lifetime, lifetime - 345)


def events(fake):
    path = fake.executable.parent / 'events.jsonl'
    return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []


def fake_state(fake, **updates):
    path = fake.executable.parent / 'state.json'
    content = json.loads(path.read_text()) if path.exists() else {}
    content.update(updates)
    dump(path, content)
    return content


def test_no_sdk_or_provider_registration_even_after_409(fake):
    for cli in [['provider', 'register'], ['vm', 'create']]:
        with pytest.raises(proof.AzureError, match='auto-registration'):
            fake._call(cli)
    for verb in ('get', 'put', 'post', 'delete'):
        with pytest.raises(proof.AzureError):
            fake.rest(verb, SUB + '/providers/Microsoft.Compute/register?api-version=2021-04-01')
    with pytest.raises(proof.AzureError, match='pinned subscription'):
        fake.rest('get', '/subscriptions/unknown/resourceGroups?api-version=2021-04-01')
    assert not events(fake)


def test_devtestlab_required_before_any_write(fake, site):
    fake_state(fake, unregistered='Microsoft.DevTestLab')
    with pytest.raises(guard.GuardFailure, match='Registered'):
        azure.preflight(fake, site[0]['azureBaseline'], 'eastus2')
    assert all(verb == 'get' for verb, _ in events(fake))


def test_cleanup_waits_through_accepted_delete_and_transient(fake, tmp_path, monkeypatch):
    spark = int(time.time())
    draw = make_draw(fake, tmp_path, start=spark)
    draw.put(draw.scope, '2021-04-01', dict(location='eastus2', tags={'chrysalisGate': RUN}))
    fake_state(fake, deletesBeforeAbsent=12)
    now = [spark]
    monkeypatch.setattr(azure.time, 'time', lambda: now[0])
    monkeypatch.setattr(azure.time, 'sleep', lambda n: now.__setitem__(0, now[0] + n))
    draw.delete(draw.deadline)
    assert now[0] - spark > 135
    assert draw.owner() is False and draw.deadline > now[0]


def test_unowned_and_ambiguous_group_are_different(fake, tmp_path):
    draw = make_draw(fake, tmp_path)
    fake_state(fake, groups={draw.scope: dict(location='eastus2', tags={})})
    with pytest.raises(guard.GuardFailure, match='owner tag'):
        draw.delete(draw.deadline)
    assert not any(method == 'delete' for method, _ in events(fake))
    guard.mark(draw.guard, 'group-put-sent')
    assert azure.creation_unconfirmed(draw.guard)
    guard.mark(draw.guard, 'group-put-confirmed')
    assert not azure.creation_unconfirmed(draw.guard)


@pytest.mark.parametrize('code', ['Conflict', 'AnotherOperationInProgress', 'TooManyRequests', 'ServiceUnavailable'])
def test_retriable_delete_codes(tmp_path, code, monkeypatch):
    az = Mock(subscription=SID)
    az.rest.side_effect = [RUN, proof.AzureError(code), RUN, None, proof.AzureError('ResourceGroupNotFound')]
    draw = make_draw(az, tmp_path)
    monkeypatch.setattr(azure.time, 'sleep', lambda seconds: None)
    draw.delete(draw.deadline)
    assert [call.args[0] for call in az.rest.call_args_list] == ['get', 'delete', 'get', 'delete', 'get']


def test_group_creation_and_run_command_share_retirement_fence(fake, tmp_path):
    now = int(time.time())
    draw = make_draw(fake, tmp_path, start=now - guard.WRITE_END - 1)
    with pytest.raises(guard.GuardFailure, match='expired'):
        draw.put(draw.scope, '2021-04-01', {})
    guard.mark(draw.guard, 'retired')
    with pytest.raises(guard.GuardFailure, match='expired'):
        draw.put(draw.path('Compute', 'virtualMachines') + '/runCommands/init', channel.VM_API, {'properties': {}}, work=True)
    assert not events(fake)


def test_retirement_cannot_declare_absence_with_inflight_authorized_write(tmp_path):
    guard.mark(tmp_path, 'ready')
    with guard.mutation(tmp_path, time.time() + 50):
        with pytest.raises(BlockingIOError), guard.retire(tmp_path):
            pytest.fail('retired during an in-flight write')
    with pytest.raises(guard.GuardFailure, match='expired'), guard.mutation(tmp_path, time.time() + 50):
        pytest.fail('write escaped retirement')


def test_sealed_deadline_order_and_midnight():
    epoch = datetime(2026, 11, 19, 23, 30, tzinfo=timezone.utc)
    e = int(epoch.timestamp())
    g = 6436
    c = g - 345
    assert e < e + channel.WORKER_START < e + c < e + c + guard.COPY_END < e + g - guard.WATCH_MARGIN < e + g < e + g + guard.ALERT_TAIL < e + g + guard.RESERVE_TAIL
    assert azure.shutdown_time(e + g) == '0118'
    assert azure.shutdown_time(e + g + 44) == '0118'
    for minutes in range(1440):
        start = e + minutes * 60 + 13
        for lifetime in (828, 1256, 7200):
            h, m = azure.shutdown_time(start + lifetime)[:2], azure.shutdown_time(start + lifetime)[2:]
            created = datetime.fromtimestamp(start + 720, timezone.utc)
            first = created.replace(hour=int(h), minute=int(m), second=0)
            if first <= created:
                first += timedelta(days=1)
            assert 0 <= first.timestamp() - (start + lifetime) < 60


@pytest.mark.parametrize('settings', [dict(scheduleRefused=2), dict(scheduleReadback={'timeZoneId': 'PST'}),
    dict(scheduleReadback={'dailyRecurrence': {'time': '0959'}}), dict(scheduleReadback={'targetResourceId': 'unrelated'})])
def test_bad_schedule_prevents_init_and_group_is_removed(fake, site, ledger, monkeypatch, settings):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    fake_state(fake, **settings, deletesBeforeAbsent=1)
    side = attempt(fake, local, ledger, directory, RUN)
    assert side == 2
    assert not ledger.attempts[RUN]['state'] == 'active'
    assert not any(detail == ['RunCommand', 'init'] for _, detail in events(fake))
    assert json.loads((directory / 'time/runs' / RUN / 'cleanup-proof.json').read_text())['ourAppAbsent']
    assert not fake_state(fake)['groups']


def test_schedule_created_before_init_and_retried_once(fake, tmp_path, monkeypatch):
    draw = make_draw(fake, tmp_path)
    monkeypatch.setattr(azure, 'provision_public_key', lambda: 'test-public-key')
    monkeypatch.setattr(azure, 'upload_inputs', lambda _draw, _inputs: None)
    fake_state(fake, scheduleRefused=1)
    draw.setup({})
    state = fake_state(fake)
    (schedule,) = [r for k, r in state['resources'].items() if '/schedules/' in k]
    assert schedule['properties'] == dict(status='Enabled', taskType='ComputeVmShutdownTask',
        dailyRecurrence={'time': azure.shutdown_time(draw.guard_end)}, timeZoneId='UTC',
        targetResourceId=draw.path('Compute', 'virtualMachines'), notificationSettings={'status': 'Disabled'})
    puts = [detail for verb, detail in events(fake) if verb == 'put']
    assert puts.index(['Microsoft.Authorization', 'roleAssignments']) < puts.index(['Microsoft.Compute', 'virtualMachines'])
    assert puts.count(['Microsoft.DevTestLab', 'schedules']) == 2


def test_reserved_booking_equality_and_unsettled_full(ledger):
    assert timing.price(6436, 'eastus2', provider='azure') == 1.2906
    assert timing.price(5723, 'eastus', provider='azure') == 1.1672
    ledger.attempts[RUN] = dict(state='active', reservation=30.71, booked=0, lineage='test')
    seal = dict(programStopUsd=40)
    with pytest.raises(azure.Refusal, match='cap stop'):
        ledger.reserve(seal, 'test', [5])
    assert ledger.reserve(seal, 'test', [4.9999])[1] == 5
    assert not ledger.notice(seal, 'test', [2] * 2, 0, 'first')['mayStop']
    assert ledger.notice(seal, 'test', [2] * 6, 0, 'repeat')['guaranteedAttempts'] == 2


def test_controls_retry_spends_original_lineage(ledger):
    ledger.attempts[RUN] = dict(state='settled', reservation=3., booked=2., lineage='original')
    ledger.attempts[OTHER] = dict(state='cleanup-failed', reservation=2., lineage='original')
    retry = dict(programStopUsd=40, lineageCapUsd=6, retryOf={'originalSealId': 'original', 'kind': 'repeat'})
    with pytest.raises(azure.Refusal):
        ledger.reserve(retry, 'child', [2.])
    assert ledger.reserve(retry, 'child', [1.99])[0] == 'original'
    assert ledger.notice(retry, 'child', [1.2] * 6, 0, 'repeat')['guaranteedAttempts'] == 1


def prepare_attempt(monkeypatch, directory, *, real_dispatch=False):
    if not real_dispatch:
        monkeypatch.setattr(azure, 'authorize_dispatch', lambda *_: {})
    dump(directory / 'seal.json', {'synthetic': True})
    monkeypatch.setenv('GATE_FIRST_GO', bundle.sha(directory / 'seal.json'))
    monkeypatch.setattr(azure, 'provision_public_key', lambda: 'test-public-key')
    monkeypatch.setattr(azure, 'upload_inputs', lambda *args: None)
    monkeypatch.setattr(azure, 'process_id', lambda _pid: 'dead-driver-proof')
    monkeypatch.setattr(azure, 'check_context', lambda *_: None)
    monkeypatch.setattr(azure, 'arm_native', lambda _script, path, *_: guard.mark(path, 'ready'))
    monkeypatch.setattr(azure, 'run_channel', Mock(side_effect=AssertionError('no unmocked guest call')))


def attempt(fake, local, ledger, directory, group, *, region='eastus2', fallback=None, dispatch=0):
    return azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
        bundle.sha(directory / 'seal.json'), directory, dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911),
        group, region, {}, 0, 1, 'first', fallback, ('test', 'fixture'), dispatch)


def test_real_attempt_lifecycle_consumes_once_and_fallback_books_separately(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    fake_state(fake, vmErrors={'eastus2': 'SkuNotAvailable'}, deletesBeforeAbsent=1)
    assert attempt(fake, local, ledger, directory, RUN) == 6
    monkeypatch.delenv('GATE_FIRST_GO')
    monkeypatch.setattr(azure, 'run_channel', lambda *_: False)
    monkeypatch.setattr(azure, 'probe_result', lambda *_: 0)
    assert attempt(fake, local, ledger, directory, OTHER, region='eastus', fallback=RUN) == 0
    assert ledger.attempts[OTHER]['fallbackOf'] == RUN
    assert ledger.attempts[RUN]['booked'] >= .16
    assert not fake_state(fake)['groups']
    assert (directory / 'time/launch-started.json').is_file()
    for name in (RUN, OTHER):
        note = directory / 'time/runs' / name
        assert {file.name for file in note.iterdir()} >= {'.guard', 'machine.json', 'cleanup-proof.json', 'duration-cost.json', 'expected-posted.json'}
        assert all(not proof.GUID.search(f.read_text()) for f in note.rglob('*') if f.is_file())


def test_quota_never_classified_as_capacity(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    fake_state(fake, vmErrors={'eastus2': 'QuotaExceeded'}, deletesBeforeAbsent=1)
    assert attempt(fake, local, ledger, directory, RUN) == 2


def test_lifecycle_persists_active_before_arm(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    def callback(_script, path, *_):
        state = json.loads(Path(local['azureLedger']).read_text())
        assert state['attempts'][RUN]['state'] == 'active'
        assert not any(method == 'put' for method, _ in events(fake))
        raise guard.GuardFailure('missing keeper')
    monkeypatch.setattr(azure, 'arm_native', callback)
    assert attempt(fake, local, ledger, directory, RUN) == 2
    assert ledger.attempts[RUN]['state'] == 'settled'
    assert not any(method == 'put' for method, _ in events(fake))


def abandoned(site, ledger, state, *, confirmed):
    local, directory = site
    note = directory / 'time/runs' / RUN / '.guard'
    note.mkdir(parents=True)
    guard.mark(note, 'group-put-sent')
    if confirmed:
        guard.mark(note, 'group-put-confirmed')
    epoch = int(time.time())
    ledger.attempts[RUN] = dict(state=state, directory=directory.name, region='eastus2', epoch=epoch, guard=828, cutoff=483,
                                 reservation=timing.price(828, 'eastus2', provider='azure'))
    dump(note / 'watchdog.json', dict(subscriptionFingerprint=proof.fingerprint(SID)))
    ledger.write()
    return note


def test_dead_driver_momentarily_clean_then_late_group(fake, site, ledger):
    local, directory = site
    note = abandoned(site, ledger, 'active', confirmed=False)
    with pytest.raises(azure.Refusal, match='unconfirmed'):
        azure.recover(fake, ledger, local['azureBaseline'], directory.parent)
    assert ledger.attempts[RUN]['state'] == 'active'
    fake_state(fake, groups={SUB + '/resourceGroups/' + RUN: dict(location='eastus2', tags={'chrysalisGate': RUN})}, deletesBeforeAbsent=1)
    guard.mark(note, 'group-put-confirmed')
    azure.recover(fake, ledger, local['azureBaseline'], directory.parent)
    assert ledger.attempts[RUN]['state'] == 'settled'
    assert not fake_state(fake)['groups']


def test_held_recover_deletes_nothing(fake, site):
    local, _ = site
    with acquire(local['azureLock']):
        with pytest.raises(azure.Refusal, match='live go'):
            azure.standalone('recover', None)
    assert not any(method == 'delete' for method, _ in events(fake))


def test_baseline_identity_substitution_is_not_accepted(fake, site):
    local, _ = site
    fake_state(fake, swapWatcher='-not-the-baseline')
    with pytest.raises(proof.AzureError, match='identity'):
        proof.proof(fake, local['azureBaseline'])


def test_daily_proof_only_excludes_deliberately_live_group(fake, site):
    local, _ = site
    fake_state(fake, groups={SUB + '/resourceGroups/' + RUN: dict(location='eastus2', tags={'chrysalisGate': RUN})})
    assert proof.proof(fake, local['azureBaseline'], [RUN], live=[RUN])[0]['restored']
    with pytest.raises(proof.AzureError, match='Orphan'):
        proof.proof(fake, local['azureBaseline'], [RUN])


def test_cost_uses_whole_subscription_and_stops_on_unattributed_rows(fake, site):
    _, directory = site
    ymd = int(datetime.now(timezone.utc).strftime('%Y%m%d'))
    response = dict(columns=['PreTaxCost', 'UsageDate', 'ResourceGroup', 'Currency'], rows=[
        [.18, ymd, RUN, 'USD'], [.07, ymd, 'unknown-neighbour', 'USD'], [.01, ymd, None, 'USD']], paginated=False)
    fake_state(fake, cost=response)
    attempts = {RUN: dict(state='settled', booked=.16, expectedPosted=.08)}
    first = now = time.time()
    report = proof.cost_readback(fake, 'baseline-group', attempts, first, now)
    assert len(report['stops']) == 3 and report['posted'][RUN] == .18 and report['otherRows'] == 2
    assert 'unknown-neighbour' not in json.dumps(report)
    for defect in (dict(paginated=True), dict(columns=['unexpected']), dict(rows=[[1, ymd, RUN]])):
        fake_state(fake, cost=dict(response, **defect))
        with pytest.raises(proof.AzureError, match='unsupported|incomplete'):
            proof.cost_readback(fake, 'baseline-group', attempts, first, now)


def test_payload_manifest_does_not_allow_unsealed_canary(tmp_path):
    expected = {'python/bin/python3.12': hashlib.sha256(b'tool').hexdigest()}
    blob = tmp_path / 'payload.tar.gz'
    archive(blob, {'python/bin/python3.12': b'tool', 'bundle-manifest.json': expected})
    payload.verify(blob, expected, manifest='bundle-manifest.json')
    canary = 'python/lib/harmless.txt'
    archive(blob, {'python/bin/python3.12': b'tool', canary: b'canary',
        'bundle-manifest.json': dict(expected, **{canary: hashlib.sha256(b'canary').hexdigest()})})
    with pytest.raises(ValueError, match='provenance'):
        payload.verify(blob, expected, manifest='bundle-manifest.json')


def test_guest_sensitive_diagnostic_is_refused_before_upload(tmp_path, monkeypatch):
    root = tmp_path / 'work'
    (root / 'results').mkdir(parents=True)
    dump(root / 'results/session-status.json', {})
    (root / 'results/session.log').write_text('?sig=' + 'CANARY' * 8)
    monkeypatch.setattr(guest, 'WORK', root)
    dest = tmp_path / 'results.tar.gz'
    assert guest.archive_results(dest, False) is True
    with tarfile.open(dest) as stream:
        assert sorted(stream.getnames()) == ['results/recovery-diagnostic.json', 'results/session-status.json', 'results/session.log']
        assert json.load(stream.extractfile('results/recovery-diagnostic.json')) == {
            'diagnosticOnly': True, 'reason': 'credential-shaped',
            'logsIncluded': 1, 'logsTruncated': 0, 'logsSkipped': 0, 'linesRedacted': 1,
        }
        redacted_log = stream.extractfile('results/session.log').read().decode()
        assert redacted_log == '[redacted: credential-shaped line]' and 'CANARY' not in redacted_log
    assert guest.check_results(dest) is True


def test_blob_sas_scope_direction_and_expiry(fake, monkeypatch):
    from urllib.parse import quote, parse_qs
    seen = []
    def execute(cmd, **kwargs):
        assert not any('CANARY' in arg for arg in cmd)
        assert not any('CANARY' in value for value in kwargs.get('env', {}).values())
        account, key, blob, rights, minutes = json.loads(kwargs['input'])
        assert key == 'CANARY_FAKE_ACCOUNT_KEY' and account == 'account'
        seen.append((blob, rights, minutes))
        expiry = datetime.now(timezone.utc) + timedelta(minutes=minutes, seconds=-1)
        return subprocess.CompletedProcess(cmd, 0, f'se={quote(expiry.isoformat())}&sp={rights}&spr=https&sv=2023-11-03&sr=b&sig=CANARY%2BMOCK%3D', '')
    monkeypatch.setattr(channel.subprocess, 'run', execute)
    for name, minutes in (('payload', 30), ('suite', 30), ('results', 15)):
        for rights in ('r', 'cw'):
            sas = channel.signed_blob(fake, 'account', 'CANARY_FAKE_ACCOUNT_KEY', name, rights)
            fields = parse_qs(sas)
            assert fields['spr'] == ['https'] and fields['sr'] == ['b'] and fields['sp'] == [rights]
            assert (name, rights, minutes) in seen
            wrapped = channel.control_body('eastus2', '# test', {name: 'encrypted-fixture'})
            assert 'CANARY' not in json.dumps(wrapped)
    assert len(seen) == 6
    with pytest.raises(guard.GuardFailure):
        channel.signed_blob(fake, 'account', 'CANARY_FAKE_ACCOUNT_KEY', 'results', 'rl')


@pytest.mark.skipif(shutil.which('openssl') is None, reason='guest requires OpenSSL')
def test_guest_two_keys_unwrap_without_azure(tmp_path, monkeypatch):
    import base64
    monkeypatch.setattr(guest, 'TEMP', tmp_path)
    monkeypatch.setattr(guest, 'PAYLOAD_KEY', tmp_path / 'payload.pem')
    monkeypatch.setattr(guest, 'RESULTS_KEY', tmp_path / 'results.pem')
    keys = guest.initialise()
    assert keys['payload'] != keys['results']
    sas = 'sv=2023-11-03&spr=https&sr=b&sp=r&sig=' + 'CANARY' * 8
    for name, public_name, path in (('suite', 'payload', guest.PAYLOAD_KEY), ('results', 'results', guest.RESULTS_KEY)):
        public = tmp_path / 'public.pem'
        public.write_text(keys[public_name])
        cipher = subprocess.check_output(['openssl', 'pkeyutl', '-encrypt', '-pubin', '-inkey', str(public),
            '-pkeyopt', 'rsa_padding_mode:oaep', '-pkeyopt', 'rsa_oaep_md:sha256'], input=sas.encode(), stderr=subprocess.DEVNULL)
        monkeypatch.setenv('CHRYSALIS_SEALED_' + name.upper(), base64.b64encode(cipher).decode())
        assert guest.unwrap(name, path) == sas
        assert 'CHRYSALIS_SEALED_' + name.upper() not in os.environ
        assert (path.stat().st_mode & 0o777) == 0o600
        assert all(sas.encode() not in artifact.read_bytes() for artifact in tmp_path.iterdir())


def test_run_command_classes_respect_count_and_copy_window(tmp_path, monkeypatch):
    now = [10000]
    monkeypatch.setattr(azure.time, 'time', lambda: now[0])
    az = Mock(subscription=SID)
    draw = make_draw(az, tmp_path, start=now[0], lifetime=6436)
    base = draw.path('Compute', 'virtualMachines') + '/runCommands/'
    body = channel.control_body(draw.region, '# harmless')
    def put(label, value=body):
        return draw.put(base + label, channel.VM_API, value, work=True)
    put('init')
    put('kick')
    with pytest.raises(guard.GuardFailure, match='completion'):
        put('push-1')
    with pytest.raises(guard.GuardFailure, match='read-only'):
        put('status-00', channel.control_body(draw.region, '# test', {'payload': 'opaque'}))
    for n in range(19):
        put(f'status-{n:02}', channel.control_body(draw.region, channel.STATUS_SCRIPT))
    now[0] = draw.work_end
    with pytest.raises(guard.GuardFailure):
        put('status-19')
    put('push-1')
    now[0] += 20
    put('push-2')
    assert len(draw.commands) == az.rest.call_count == 23
    guard.mark(draw.guard, 'retired')
    with pytest.raises(guard.GuardFailure):
        put('push-3')


@pytest.mark.parametrize('fault', ['command', 'http', 'status', 'truncated', 'changed', 'sensitive', 'corrupt'])
def test_resend_keeps_exact_digest_and_fresh_tokens(tmp_path, monkeypatch, fault):
    clock = [100]
    monkeypatch.setattr(channel.time, 'time', lambda: clock[0])
    draw = Mock(work_end=100, account='storage-fixture')
    output = tmp_path / 'fixture.tgz'
    archive(output, {'results/session-status.json': {'exitCode': 0}})
    if fault == 'sensitive':
        archive(output, {'results/session-status.json': {'fixture': '-----BEGIN PRIVATE KEY-----'}})
    elif fault == 'corrupt':
        output.write_bytes(b'not a gzip archive')
    summary = dict(sha256=bundle.sha(output), size=output.stat().st_size, partial=False)
    writes, calls = [], []
    def command(_draw, label, _script, until, *, sealed):
        calls.append(label)
        assert until <= 280
        assert 'CANARY' not in json.dumps(sealed)
        if label == 'push-1' and fault == 'command':
            clock[0] += 20
            raise proof.AzureError('timeout')
        return dict(summary, sha256='0' * 64) if label == 'push-2' and fault == 'changed' else summary
    def sign(_az, _account, key, name, right):
        writes.append((name, right))
        return 'CANARY' * 8
    def download(_account, _blob, _secret, target, _max, deadline):
        assert deadline == 300
        shutil.copyfile(output, target)
        if calls == ['push-1'] and fault in ('http', 'changed'):
            raise IncompleteRead(b'fixture')
        if calls == ['push-1'] and fault == 'status':
            raise ValueError('blob status or length invalid')
        if calls == ['push-1'] and fault == 'truncated':
            raise ValueError('blob truncated')
        return summary['sha256']
    monkeypatch.setattr(channel, 'storage_key', lambda _: 'CANARY' * 8)
    monkeypatch.setattr(channel, 'signed_blob', sign)
    monkeypatch.setattr(channel, 'seal', lambda *_: 'ciphertext')
    monkeypatch.setattr(channel, 'command', command)
    monkeypatch.setattr(channel, 'download', download)
    if fault in ('sensitive', 'corrupt', 'changed'):
        with pytest.raises((ValueError, guard.GuardFailure, tarfile.TarError)):
            channel.copy_results(draw, tmp_path, 'unused', '# ')
        assert list(tmp_path.iterdir()) == [output]
        return
    assert channel.copy_results(draw, tmp_path, 'unused', '# ') is False
    assert calls == ['push-1', 'push-2']
    expected = [('results', 'cw'), ('results', 'r')]
    assert writes == (expected[:1] if fault == 'command' else expected) + expected
    assert 'CANARY' not in (tmp_path / 'results.sha256').read_text()
    clock[0] = 150
    with pytest.raises(guard.GuardFailure, match='insufficient'):
        channel.copy_results(draw, tmp_path, 'unused', '# ')


def test_guest_push_builds_once_after_lost_response(tmp_path, monkeypatch):
    store = tmp_path / 'store'
    store.mkdir()
    work = tmp_path / 'work'
    (work / 'results').mkdir(parents=True)
    status = work / 'results/session-status.json'
    dump(status, {'exitCode': 0})
    monkeypatch.setattr(guest, 'TEMP', store)
    monkeypatch.setattr(guest, 'WORK', work)
    monkeypatch.setattr(guest, 'RESULTS_KEY', store / 'results.pem')
    monkeypatch.setattr(guest, 'compact', lambda: {'done': True})
    monkeypatch.setattr(guest, 'unwrap', lambda *_: 'synthetic-credential')
    seen = []
    def upload(_account, _name, _secret, path, limit, _until):
        seen.append(bundle.sha(path))
        assert limit == guest.MAX_RESULTS
        if len(seen) == 1:
            raise TimeoutError('reply lost')
    monkeypatch.setattr(guest, 'upload', upload)
    with pytest.raises(TimeoutError):
        guest.push('fixture', time.time() + 130, False)
    dump(status, {'new': True})
    second = guest.push('fixture', time.time() + 130, True)
    assert seen == [second['sha256']] * 2


def test_oversize_results_are_bounded_never_counted(tmp_path, monkeypatch):
    work = tmp_path / 'work'
    (work / 'results').mkdir(parents=True)
    dump(work / 'results/session-status.json', {})
    with (work / 'results/session.log').open('wb') as stream:
        stream.truncate(guest.MAX_RESULTS + 1)
    monkeypatch.setattr(guest, 'WORK', work)
    dest = tmp_path / 'blob.tar.gz'
    assert guest.archive_results(dest, False)
    assert dest.stat().st_size < guest.MAX_DIAGNOSTIC
    assert guest.check_results(dest)


def test_already_sealed_go_can_be_refused_by_current_ledger(ledger, site, monkeypatch):
    local, directory = site
    seal = dict(programStopUsd=40, timePlans=[dict(destroyGuardSeconds=6436)])
    go = '0' * 64
    azure.check_seal_budget(local, seal, go)
    ledger.attempts[RUN] = dict(state='settled', reservation=34.4194, booked=34.4194, lineage=None)
    ledger.write()
    with pytest.raises(azure.Refusal, match='cap stop'):
        azure.check_seal_budget(local, seal, go)


def test_cleanup_failed_keeps_full_reservation(fake, site, ledger, monkeypatch):
    local, directory = site
    note = abandoned(site, ledger, 'active', confirmed=True)
    fake_state(fake, groups={SUB + '/resourceGroups/' + RUN: dict(location='eastus2', tags={})})
    with pytest.raises(azure.Refusal, match='not proven'):
        azure.recover(fake, ledger, local['azureBaseline'], directory.parent)
    entry = ledger.attempts[RUN]
    assert entry['state'] == 'cleanup-failed'
    assert ledger.total() == Decimal('4.29') + Decimal(str(entry['reservation']))
    assert not (note.parent / 'cleanup-proof.json').exists()


FIXTURE_RUNNER = 'a' * 64
BUILD_MARKER = dict(schemaVersion=1, mode='build', runnerSha256=FIXTURE_RUNNER,
                    imageDigest=payload.BUILD_IMAGE_DIGEST, sdkSha512=payload.BUILD_SDK_SHA512)


def sealed_site(site, *, tfm='net11.0'):
    local, directory = site
    payload_dir = directory / 'build'
    payload_dir.mkdir()
    h = hashlib.sha256(b'client').hexdigest()
    spec = dict(row()['sides']['baseline'], tfm=tfm, dllSha256s={'ReplayClient.dll': h})
    spec['client'] = f'clients/{schedules.build_id(spec)}/ReplayClient.dll'
    plan = probe_plan(dict(row(pairs=8), aPairs=0, provider='azure', region='eastus2', sides=dict(baseline=spec, candidate=spec)), 'change')
    (payload_dir / spec['client']).parent.mkdir(parents=True)
    (payload_dir / spec['client']).write_bytes(b'client')
    gate.session.save(payload_dir / payload.BUILD_MARKER, BUILD_MARKER)
    plan['inputSha256'] = {spec['client']: h, payload.BUILD_MARKER: bundle.sha(payload_dir / payload.BUILD_MARKER)}
    schedules.prepare(plan)
    gate.session.save(payload_dir / 'plan.json', schedules.worker_plan(plan))
    host = directory / 'input/azure'
    host.mkdir(parents=True)
    for name in bundle.HOST_FILES['azure']:
        shutil.copy2(HERE / name, host / name)
    relative = 'input/session-0.tar.gz'
    checksum = bundle.pack(payload_dir, directory / relative)
    raw = Path(os.environ['CHRYSALIS_GATE_LOCAL'])
    shutil.copyfile(raw, directory / 'input/azure-local.json')
    files = {f'input/azure/{name}': bundle.sha(host / name) for name in bundle.HOST_FILES['azure']}
    files.update({relative: checksum, 'input/azure-local.json': bundle.sha(raw)})
    seal = dict(provider='azure', platform=dict(size=azure.SIZE, regions=list(azure.REGIONS), image=azure.IMAGE, azCliVersion='2.99.1'),
        programStopUsd=40, mode='change', timePlans=[plan], payloadManifests={}, files=files, rows=plan['rows'],
        ruleVersion=gate.stats.RULE_VERSION, canonical=False, releaseSet=[], gated={"areas": {}}, buildMarker=BUILD_MARKER)
    return gate.write_seal(directory, seal), seal


def add_fake_keeper(tmp, real_python):
    # The adapter is test-only; sealed production files remain byte-identical.
    # The fake native process decodes the actual PowerShell argv and calls the
    # real watchdog entry, then reaches a bounded cut immediately after ready.
    shim = tmp / 'test-python'
    power = tmp / 'fixture-powershell'
    power.write_text(f'#!{real_python}\n' + '''import base64, os, re, subprocess, sys
outer = sys.argv[-1]
encoded = re.search(r'EncodedCommand ([A-Za-z0-9+/=]+)', outer)[1]
script = base64.b64decode(encoded).decode('utf-16le')
distro, user, target, state = re.search(r"'--distribution','([^']+)','--user','([^']+)',.*?'([^']+)','--watchdog','([^']+)'", script).groups()
env = dict(os.environ, WSL_DISTRO_NAME=distro)
if distro != 'fixture-linux':
    sys.exit(2)
'''+ f'sys.exit(subprocess.run([{str(shim)!r}, target, "--watchdog", state], env=env, timeout=12).returncode)\n')
    power.chmod(0o700)
    shim.write_text(f'#!{real_python}\n' + '''import runpy, sys
from pathlib import Path
import re
sys.dont_write_bytecode = True
if sys.argv[1] == '-B':
    sys.argv.pop(1)
source = Path(sys.argv[1])
sys.path.insert(0, str(source.parent))
sys.argv = sys.argv[1:]
import gate_azure as az
import gate_azure_guard as guard
import gate_azure_payload as payload
payload.BUILD_RUNNER_SHA256 = 'a' * 64
'''+ f'guard.POWERSHELL = Path({str(power)!r})\nsys.executable = {str(shim)!r}\n' + '''
if source.name == 'gate.py':
    runpy.run_path(str(source), run_name='__main__')
else:
    if '--watchdog' in sys.argv:
        def ready_cut(_):
            raise SystemExit(0 if (Path(sys.argv[-1]) / 'ready').is_file() else 3)
        az.time.sleep = ready_cut
    az.process_id = lambda _: 'fake-driver-birth'
    az.provision_public_key = lambda: 'fixture-public-key'
    az.upload_inputs = lambda *_: None
    az.run_channel = lambda *_: True  # approved evidence path is diagnostic-only in this test
    try:
        raise SystemExit(az.main(sys.argv[1:]))
    except az.Refusal:
        raise SystemExit(4)
''')
    shim.chmod(0o700)
    return shim


def test_public_dispatch_real_fd9_sealed_launcher_and_keeper(fake, site, monkeypatch):
    local, directory = site
    go, seal = sealed_site(site)
    with pytest.raises(guard.GuardFailure, match='context'):
        monkeypatch.delenv('WSL_DISTRO_NAME', raising=False)
        bundle.go_command(go)
    assert not any(method == 'put' for method, _ in events(fake))
    assert bundle.go_command(go, dry=True) == 0
    monkeypatch.setenv('WSL_DISTRO_NAME', 'fixture-linux')
    test_python = add_fake_keeper(directory.parent.parent, sys.executable)
    monkeypatch.setattr(bundle.sys, 'executable', str(test_python))
    fake_state(fake, vmErrors={'eastus2': 'AllocationFailed'}, deletesBeforeAbsent=1)
    assert bundle.go_command(go) == 2  # fallback kept only a diagnostic
    ledger = azure.Ledger(local, read_only=True)
    assert len(ledger.attempts) == 2
    primary, = [a for a in ledger.attempts.values() if a['region'] == 'eastus2']
    second, = [a for a in ledger.attempts.values() if a['region'] == 'eastus']
    assert primary['exitCode'] == 6 and second['exitCode'] == 2 and second['fallbackOf']
    assert all(a['state'] == 'settled' for a in ledger.attempts.values())
    assert any(note.is_file() for note in directory.glob('time/runs/*/diagnostic-only.json'))
    assert not fake_state(fake)['groups']
    summary = json.loads((directory / 'time/azure-money.json').read_text())
    assert summary['goAttempts'] == 2 and summary['goBookedUsd'] == pytest.approx(sum(a['booked'] for a in ledger.attempts.values()))
    assert summary['postedObservedAttempts'] == 0 and summary['remainingWorkEstimateUsd'] == 12.50
    audit = [Path(local['azureLedger']), directory/'report.txt', *[p for p in directory.glob('time/runs/**/*') if p.is_file()]]
    ipv4 = re.compile(rb'(?<![\w.])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![\w.])')
    for text in (file.read_bytes() for file in audit):
        assert not proof.GUID.search(text.decode(errors='replace')) and not ipv4.search(text)

    with pytest.raises(ValueError, match='Go already consumed'):
        bundle.go_command(go)


def test_parent_bytes_hash_and_guard_shape_for_controls_retry(site):
    _, directory = site
    run = directory / 'time/runs' / RUN
    (run / '.guard').mkdir(parents=True)
    (run / '.guard/ready').touch()
    dump(run / 'cleanup-proof.json', dict(ourAppAbsent=True))
    archive(run / 'results.tar.gz', {'results/session-status.json': {}})
    original = run / 'results.tar.gz'
    dump(directory / 'time/bytes-index.json', [dict(app=RUN, session=1, phase='first', archiveSha256=bundle.sha(original))])
    plan = [dict(provider='azure'), dict(bytesOnly=True), dict(bytesOnly=True, bytesPhase='noise')]  # optional noise never executed
    draws = [dict(app=RUN, session=1, draw=1, phase='first', decision='kept', archiveSha256=bundle.sha(original))]
    bundle.check_azure_retry_bytes(directory, plan, draws)
    assert bundle.retry_run_ok(run, RUN, True)
    original.write_bytes(b'tampered')
    with pytest.raises(ValueError, match='bytes phase changed'):
        bundle.check_azure_retry_bytes(directory, plan, draws)
    (run / '.guard/unexpected-dir').mkdir()
    assert not bundle.retry_run_ok(run, RUN, True)


def write_azure_result(note, plan, kind, *, eligible=False):
    from test_gate_fly import probe_archive
    probe_archive(note, plan, kind, eligible=eligible)
    path = note / 'results.tar.gz'
    with tarfile.open(path) as stream:
        parts = {m.name: json.load(stream.extractfile(m)) for m in stream}
    for name, part in parts.items():
        if name.startswith('results/probe/'):
            part['platform'] = 'azure'
        if name == 'results/members.json':
            for record in part:
                record['platform'] = 'azure'
    records = {Path(name).stem: part for name, part in parts.items() if name.startswith('results/probe/')}
    parts['results/probe.json'] = timing.probe_result(records, plan['probe'])
    archive(path, parts)
    (note / 'results.sha256').write_text(bundle.sha(path) + '\n')
    dump(note / 'machine.json', dict(provider='azure', platform='azure', machineClass=azure.SIZE, region='eastus'))
    (note / '.guard').mkdir()
    (note / '.guard/done').touch()
    return path


def test_late_rejected_draw_books_tail_without_passing_any_result(site):
    _, directory = site
    _, seal = sealed_site(site)
    plan = seal['timePlans'][0]
    note = directory / 'time/runs' / RUN
    archive_path = write_azure_result(note, plan, 'cv-over-limit')
    dump(note / 'duration-cost.json', dict(cleanupOutstanding=False, totalEstimateWithReserveUsd=timing.plan_price(plan), withinSealedGuard=False))
    machine = bundle.verified_machine(note, plan, 0, selected=[r['id'] for r in plan['rows']])
    assert azure.probe_result(archive_path, plan, note) == 5
    assert machine['invalid'] is None and machine['probe']['decision'] == 'rejected'
    assert machine['costWarnings'] == ['booked cost past machine guard']
    assert bundle.archive_payload(note, plan, machine=machine)['invalid']


def test_two_session_controls_retry_revalidates_fallback_and_every_archive(site):
    local, parent = site
    _, seal = sealed_site(site)
    timing_plan = seal['timePlans'][0]
    entry = dict(timing_plan['rows'][0], aPairs=16, bPairs=16, expected=dict(release='FAIL', change='PASS'))
    plan = probe_plan(entry, 'release')
    plan['inputSha256'] = timing_plan['inputSha256']
    schedules.prepare(plan)
    seal.update(mode='release', lineageCapUsd=6, rows=plan['rows'],
                timePlans=[plan, {'bytesOnly': True}, {'bytesOnly': True, 'bytesPhase': 'repeat'}])
    go = gate.write_seal(parent, seal)
    note = parent / 'time/runs' / RUN
    write_azure_result(note, plan, 'keep', eligible=True)
    fallback = note.parent / OTHER
    fallback.mkdir()
    dump(fallback / 'cleanup-proof.json', dict(ourAppAbsent=True))
    archive_app = 'chrysalis-gate-20990405t000103-' + 'ef' * 4
    bytes_note = note.parent / archive_app
    bytes_note.mkdir()
    archive(bytes_note / 'results.tar.gz', {'results/session-status.json': {}})
    dump(bytes_note / 'cleanup-proof.json', dict(ourAppAbsent=True))
    repeat_app = 'chrysalis-gate-20990405t000105-' + '11' * 4
    reject_app = 'chrysalis-gate-20990405t000106-' + '22' * 4
    rejected_primary = 'chrysalis-gate-20990405t000107-' + '33' * 4
    repeat_primary = 'chrysalis-gate-20990405t000108-' + '44' * 4
    prior_app = 'chrysalis-gate-20990405t000104-' + '00' * 4
    for app in (repeat_app, reject_app, rejected_primary, repeat_primary):
        (note.parent / app).mkdir()
        dump(note.parent / app / 'cleanup-proof.json', dict(ourAppAbsent=True))
    repeat_path = note.parent / repeat_app / 'results.tar.gz'
    shutil.copyfile(bytes_note / 'results.tar.gz', repeat_path)
    bytes_index = parent / 'time/bytes-index.json'
    items = [dict(app=archive_app, session=1, phase='first', archiveSha256=bundle.sha(bytes_note / 'results.tar.gz'),
                  rejectedApps=[rejected_primary, reject_app]), dict(app=repeat_app, session=2, phase='repeat',
                  archiveSha256=bundle.sha(repeat_path), fallbackOf=repeat_primary)]
    dump(bytes_index, items)
    ids = [entry['id']]
    machine = bundle.verified_machine(note, plan, 0, selected=ids)
    payload_value = bundle.archive_payload(note, plan, machine=machine)
    assert not payload_value['invalid']
    timing_draw = bundle.draw_record(machine, 0, 'first', 1, RUN, 'kept', OTHER)
    dump(parent / 'time/draws.json', [timing_draw])
    bytes_draws = [dict(session=1, draw=1, phase='first', app=reject_app, decision='rejected', fallbackOf=rejected_primary),
                   *(dict(session=item['session'], draw=2 if item['session'] == 1 else 1, phase='first', app=item['app'],
                          decision='kept', archiveSha256=item['archiveSha256']) for item in items)]
    bytes_draws[-1]['fallbackOf'] = repeat_primary
    draw_path = parent / 'time/bytes-draws.json'
    dump(draw_path, bytes_draws)
    entries = {}
    for draw in [timing_draw, *bytes_draws]:
        settled = dict(session=draw['session'], phase='first', draw=draw['draw'], state='settled', directory=parent.name, go=go)
        fallback = draw.get('fallbackOf')
        entries[draw['app']] = dict(settled, exitCode=0 if draw['decision'] == 'kept' else 5, fallbackOf=fallback)
        if fallback:
            entries[fallback] = dict(settled, exitCode=6, fallbackOf=None)
    entries[prior_app] = dict(entries[RUN], go='b' * 64, directory='earlier-go', fallbackOf=None)
    ledger_path = Path(local['azureLedger'])
    ledger_data = dict(openingBalanceUsd=4.29, attempts=entries)
    dump(ledger_path, ledger_data)
    dump(parent / 'time/launch-started.json', dict(sealId=go))
    dump(parent / 'time/0-first.json', payload_value)
    dump(parent / 'time/index.json', [dict(session=0, phase='first', ids=ids, path='time/0-first.json', sha256=bundle.sha(parent / 'time/0-first.json'))])
    dump(parent.parent / 'controls-authority.json', dict(originalSealId=go, mode='release', rows=[dict(id=entry['id'], expected=entry['expected'])]))
    with patch.object(timing, 'session_outcome', return_value=('retry-eligible', [])):
        assert bundle.checked_retry_parent(parent, parent.parent, 'first')[2]['originalSealId'] == go
        # A reservation in this GO may be stranded before its run folder exists.
        orphan = dict(entries[prior_app], go=go, directory=parent.name, state='active')
        dump(ledger_path, dict(ledger_data, attempts={**entries, prior_app: orphan}))
        with pytest.raises(ValueError, match='cover every original Azure attempt'):
            bundle.checked_retry_parent(parent, parent.parent, 'first')
        dump(ledger_path, ledger_data)
        draws = [timing_draw, *bytes_draws]
        with pytest.raises(ValueError, match='differs from Azure attempt ledger'):
            bundle.retry_azure_apps(parent, go, entries, [*draws, bytes_draws[0]])
        mismatched = {**entries, repeat_app: dict(entries[repeat_app], fallbackOf=rejected_primary)}
        with pytest.raises(ValueError, match='differs from Azure attempt ledger'):
            bundle.retry_azure_apps(parent, go, mismatched, draws)
        with pytest.raises(ValueError, match='Invalid original bytes draw order or slot'):
            bundle.check_azure_retry_bytes(parent, seal['timePlans'], [bytes_draws[0], dict(bytes_draws[1], draw=3), bytes_draws[2]])
        (note.parent / prior_app).mkdir()
        with pytest.raises(ValueError, match='name every app'):
            bundle.checked_retry_parent(parent, parent.parent, 'first')
        (note.parent / prior_app).rmdir()
        dump(note.parent / reject_app / 'cleanup-proof.json', dict(ourAppAbsent=False))
        with pytest.raises(ValueError, match='name every app'):
            bundle.checked_retry_parent(parent, parent.parent, 'first')
        dump(note.parent / reject_app / 'cleanup-proof.json', dict(ourAppAbsent=True))
        original_repeat = repeat_path.read_bytes()
        repeat_path.write_bytes(b'tampered')
        with pytest.raises(ValueError, match='bytes phase changed'):
            bundle.checked_retry_parent(parent, parent.parent, 'first')
        dump(bytes_index, [dict(items[0], rejectedApps=[rejected_primary, reject_app, repeat_app])])
        with pytest.raises(ValueError, match='bytes'):
            bundle.checked_retry_parent(parent, parent.parent, 'first')
        dump(bytes_index, items)
        dump(draw_path, [dict(d, decision='rejected', session=1, draw=2) if d['app'] == repeat_app else d for d in bytes_draws])
        with pytest.raises(ValueError, match='attempt ledger'):
            bundle.checked_retry_parent(parent, parent.parent, 'first')
        dump(draw_path, bytes_draws)
        repeat_path.write_bytes(original_repeat)
        for invalid in ([items[0], items[0], items[1]], [items[0], dict(items[1], phase='noise')]):
            dump(bytes_index, invalid)
            with pytest.raises(ValueError, match='Invalid original bytes phase'):
                bundle.checked_retry_parent(parent, parent.parent, 'first')
        dump(bytes_index, items)
        (note / 'results.tar.gz').write_bytes(b'tampered')
        with pytest.raises(ValueError, match='differs from verified'):
            bundle.checked_retry_parent(parent, parent.parent, 'first')
        seal['timePlans'].append({'bytesOnly': True})
        new_go = gate.write_seal(parent, seal)
        dump(parent / 'time/launch-started.json', dict(sealId=new_go))
        with pytest.raises(ValueError, match='authority differs'):
            bundle.checked_retry_parent(parent, parent.parent, 'first')


def test_cost_readback_at_day_one_and_day_three_includes_failed_primary(fake, site, ledger, monkeypatch):
    local, directory = site
    now = int(time.time())
    go = 'c' * 64
    (directory / 'seal.id').write_text(go + '\n')
    for name, charge, status in ((RUN, .16, 6), (OTHER, .24, 0)):
        ledger.attempts[name] = dict(go=go, state='settled', epoch=now - 600, ended=now, reservation=.4,
                                     booked=charge, expectedPosted=charge, exitCode=status)
    ledger.write()
    ymd = int(datetime.fromtimestamp(now, timezone.utc).strftime('%Y%m%d'))
    columns = ['PreTaxCost', 'UsageDate', 'ResourceGroup', 'Currency']
    fake_state(fake, cost=dict(columns=columns, rows=[[.05, ymd, RUN, 'USD']], paginated=False))
    monkeypatch.setattr(azure.time, 'time', lambda: now + 86400 - 1)
    assert azure.daily_cost(directory, local) == 0
    assert not (directory / 'cost-d1.json').exists()
    monkeypatch.setattr(azure.time, 'time', lambda: now + 86400)
    assert azure.daily_cost(directory, local) == 0
    assert json.loads((directory / 'cost-d1.json').read_text())['posted'][RUN] == .05
    fake_state(fake, cost=dict(columns=columns, rows=[[.09, ymd, RUN, 'USD']], paginated=False))
    monkeypatch.setattr(azure.time, 'time', lambda: now + 3 * 86400)
    assert azure.daily_cost(directory, local) == 0
    assert json.loads((directory / 'cost-d3.json').read_text())['posted'][RUN] == .09
    restored = azure.Ledger(local)
    assert restored.attempts[RUN]['posted'] == {'d1': .05, 'd3': .09}
    assert restored.data['lastDailyCheck'] == now + 3 * 86400


def test_paid_fallback_fails_at_shared_cap_before_creating_group(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    fake_state(fake, vmErrors={'eastus2': 'SkuNotAvailable'})
    assert attempt(fake, local, ledger, directory, RUN) == 6
    monkeypatch.delenv('GATE_FIRST_GO')
    # Book the remaining amount down to exactly one full fallback reservation.
    booked = timing.azure_booking(1256 + azure.RESERVE_TAIL)
    ledger.attempts[RUN]['booked'] = round(40 - 4.29 - booked, 4)
    ledger.write()
    before = len(events(fake))
    with pytest.raises(azure.Refusal, match='booked.*unsettled.*posted.*available'):
        attempt(fake, local, ledger, directory, OTHER, region='eastus', fallback=RUN)
    assert OTHER not in ledger.attempts
    assert not any(method == 'put' for method, _ in events(fake)[before:])


def test_r1_closed_original_dispatch_cannot_start_another_attempt(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory, real_dispatch=True)
    (directory / 'time').mkdir()
    fake_state(fake, vmErrors={'eastus2': 'SkuNotAvailable'})
    go = bundle.sha(directory / 'seal.json')
    retained = None
    read_copy = None
    try:
        with bundle.azure_dispatch(directory, go) as (token, read):
            retained, read_copy = os.dup(token.fileno()), os.dup(read)
            monkeypatch.setenv('GATE_AZURE_PARENT', str(os.getppid()))
            monkeypatch.setenv('GATE_AZURE_LIVE_FD', str(read_copy))
            assert attempt(fake, local, ledger, directory, RUN, dispatch=retained) == 6
            monkeypatch.delenv('GATE_FIRST_GO')
            before = len(events(fake))
            with pytest.raises(azure.Refusal, match='logical attempt'):
                attempt(fake, local, ledger, directory, OTHER, dispatch=retained)
        # Retaining both leaf descriptors cannot preserve the parent-only writer.
        with pytest.raises(azure.Refusal, match='dispatch'):
            attempt(fake, local, ledger, directory, OTHER, region='eastus', fallback=RUN, dispatch=retained)
    finally:
        for fd in (retained, read_copy):
            if fd is not None:
                os.close(fd)
    assert OTHER not in ledger.attempts
    assert not any(method == 'put' for method, _ in events(fake)[before:])


def test_r1_watchdog_does_not_kill_driver_polling_delete(fake, site, ledger, monkeypatch):
    from concurrent.futures import ThreadPoolExecutor
    from threading import Event, current_thread
    from types import SimpleNamespace
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    monkeypatch.setattr(azure, 'HERE', directory / 'input/azure')
    monkeypatch.setattr(azure, 'cloud_init', lambda *_: 'fixture cloud init')
    monkeypatch.setattr(azure, 'wsl_context', lambda: ('test', 'fixture'))
    monkeypatch.setattr(bundle, 'checked_azure_local', lambda _: local)
    monkeypatch.setattr(gate, 'read_seal', lambda _: {})
    fake_state(fake, vmErrors={'eastus2': 'SkuNotAvailable'})
    deleting, release, driver_done = Event(), Event(), Event()
    clock = SimpleNamespace(now=int(time.time()))
    watcher = current_thread()
    def sleep(_):
        if current_thread() is not watcher:
            return
        release.set()
        assert driver_done.wait(12), 'driver failed to settle'
    monkeypatch.setattr(azure, 'time', SimpleNamespace(time=lambda: clock.now, sleep=sleep, monotonic=time.monotonic))
    original = azure.Draw.delete
    def delayed(draw, until, **kwargs):
        if not deleting.is_set():
            deleting.set()
            assert release.wait(12), 'watchdog did not overlap cleanup'
        return original(draw, until, **kwargs)
    killed = Mock()
    monkeypatch.setattr(azure.Draw, 'delete', delayed)
    monkeypatch.setattr(azure, 'stop_paused_driver', killed)
    def driver():
        try:
            return attempt(fake, local, ledger, directory, RUN)
        finally:
            driver_done.set()
    with ThreadPoolExecutor(max_workers=1) as pool:
        result = pool.submit(driver)
        try:
            assert deleting.wait(12), 'driver never began cleanup'
            clock.now += 1256 - azure.WATCH_MARGIN + 1
            azure.watchdog(directory / 'time/runs' / RUN / '.guard')
        finally:
            release.set()
        assert result.result() == 6
    killed.assert_not_called()
    assert ledger.attempts[RUN]['state'] == 'settled'
    assert not fake_state(fake)['groups']


@pytest.mark.parametrize('failure_marker', [False, True])
def test_r1_expired_unproved_attempt_requires_explicit_recovery(fake, site, ledger, monkeypatch, failure_marker):
    local, directory = site
    note = abandoned(site, ledger, 'active', confirmed=True)
    ledger.attempts[RUN]['epoch'] -= 828 + azure.RESERVE_TAIL + 1
    ledger.write()
    if failure_marker:
        guard.mark(note, 'failed')
    fake_state(fake, groups={SUB + '/resourceGroups/' + RUN: dict(location='eastus2', tags={'chrysalisGate': RUN})})
    with pytest.raises(azure.Refusal, match='coordinator'):
        azure.reconcile_and_pre_go(local, directory, dict(programStopUsd=40, timePlans=[dict(destroyGuardSeconds=828)]), 'a' * 64)
    assert not any(method == 'delete' for method, _ in events(fake))
    assert azure.Ledger(local).attempts[RUN]['state'] == 'cleanup-failed'
    azure.recover(fake, azure.Ledger(local), local['azureBaseline'], directory.parent, coordinator=True)
    assert azure.Ledger(local).attempts[RUN]['state'] == 'settled'
    assert not fake_state(fake)['groups']


def test_r1_due_cost_cannot_succeed_unchecked_while_go_holds_lock(fake, site, ledger, monkeypatch):
    local, directory = site
    now = int(time.time())
    go = 'c' * 64
    (directory / 'seal.id').write_text(go + '\n')
    ledger.attempts[RUN] = dict(go=go, state='settled', epoch=now - 2 * 86400, ended=now - 2 * 86400,
                                booked=.16, expectedPosted=.16)
    ledger.attempts[OTHER] = dict(go='f' * 64, state='active', epoch=now, guard=828, reservation=.3)
    ledger.write()
    fake_state(fake, groups={SUB + '/resourceGroups/' + OTHER: dict(location='eastus2', tags={'chrysalisGate': OTHER})},
               cost=dict(columns=['PreTaxCost','UsageDate','ResourceGroup','Currency'],
                         rows=[[.07, int(datetime.fromtimestamp(now, timezone.utc).strftime('%Y%m%d')), 'unattributed', 'USD']], paginated=False))
    bytes_before = ledger.path.read_bytes()
    monkeypatch.setattr(azure, 'held_lock', Mock(side_effect=azure.Refusal('GO locked')))
    with pytest.raises(azure.Refusal, match='charge outside run groups'):
        azure.daily_cost(directory, local)
    assert ledger.path.read_bytes() == bytes_before
    if hasattr(ledger, 'cost_latch'):
        assert ledger.cost_latch.exists()
        with pytest.raises(azure.Refusal, match='unresolved'):
            ledger.reserve(dict(programStopUsd=40), 'f' * 64, [1.0])
    assert any(method == 'post' for method, _ in events(fake))


def test_r1_post_vm_capacity_error_is_not_a_capacity_fallback(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    monkeypatch.setattr(azure, 'run_channel', Mock(side_effect=proof.AzureError('AllocationFailed')))
    assert attempt(fake, local, ledger, directory, RUN) == 2
    assert ledger.attempts[RUN]['exitCode'] == 2 and len(ledger.attempts) == 1
    assert not fake_state(fake)['groups']


def test_r1_azure_group_keeps_timestamped_external_shape(site, monkeypatch):
    _, directory = site
    (directory / 'time').mkdir()
    monkeypatch.setattr(bundle, 'owned_call', lambda *_a, **_k: 2)
    _, app, _ = bundle.launch_azure(directory, directory, 'f' * 64, 0, 'first', 1, {'region': 'eastus2'},
                                    'input/session-0.tar.gz', True, ('test', 'fixture'), None)
    assert re.fullmatch(r'chrysalis-gate-[0-9]{8}t[0-9]{6}-[a-f0-9]{8}', app)
    mark = directory / 'time/runs' / app / '.guard'
    mark.mkdir(parents=True)
    assert proof.GROUP.fullmatch(app)
    dump(mark.parent / 'cleanup-proof.json', dict(ourAppAbsent=True))
    assert bundle.retry_run_ok(mark.parent, app, True)


@pytest.mark.parametrize('cap', [40, 39])
def test_r1_cap_equality_reports_projected_rest_not_just_headroom(ledger, cap):
    ledger.attempts[RUN] = dict(state='settled', booked=30.71, posted={'d1': 12.25}, reservation=31.0)
    ledger.attempts[OTHER] = dict(state='active', reservation=4.0)
    with pytest.raises(azure.Refusal) as error:
        ledger.reserve({'programStopUsd': cap}, 'f' * 64, [1.0])
    text = str(error.value)
    assert 'remaining-work estimate $12.5000' in text
    assert f'headroom ${cap - 39:.4f}' in text
    assert 'booked $35.0000' in text and 'unsettled $4.0000' in text
    assert 'posted $12.2500' in text and '1 of 2' in text


@pytest.mark.parametrize('projection', [None, -1])
def test_r1_go_requires_nonnegative_remaining_projection(site, projection):
    local, directory = site
    settings = Path(os.environ['CHRYSALIS_GATE_LOCAL'])
    if projection is None:
        local.pop('azureRemainingProjectionUsd')
    else:
        local['azureRemainingProjectionUsd'] = projection
    dump(settings, local)
    go, _ = sealed_site(site)
    with pytest.raises(ValueError, match='remaining.*estimate|azureRemainingProjectionUsd'):
        bundle.go_command(go, dry=True)


@pytest.mark.parametrize('wrong', [None, 'native-mac'])
def test_r1_bad_build_marker_refused_before_provider_operations(fake, site, wrong):
    _, directory = site
    go, seal = sealed_site(site)
    if wrong is None:
        del seal['buildMarker']
    else:
        seal['buildMarker'] = dict(BUILD_MARKER, mode=wrong)
    go = gate.write_seal(directory, seal)
    before = events(fake)
    with pytest.raises(ValueError, match='container build marker'):
        bundle.checked_azure_go(directory, go)
    assert events(fake) == before


def test_r1_build_marker_requires_the_approved_runner_and_rejects_mutation(monkeypatch):
    monkeypatch.setattr(payload, 'BUILD_RUNNER_SHA256', None)
    for marker in (BUILD_MARKER, dict(BUILD_MARKER, runnerSha256=None)):
        with pytest.raises(ValueError, match='container build marker'):
            payload.validate_build_marker(marker)
    monkeypatch.setattr(payload, 'BUILD_RUNNER_SHA256', FIXTURE_RUNNER)
    assert payload.validate_build_marker(BUILD_MARKER) == BUILD_MARKER
    for changed in (dict(runnerSha256='d' * 64), dict(imageDigest='sha256:' + 'd' * 64), dict(sdkSha512={})):
        with pytest.raises(ValueError, match='container build marker'):
            payload.validate_build_marker(dict(BUILD_MARKER, **changed))


@pytest.mark.parametrize('marker', [None, dict(BUILD_MARKER, mode='native-mac')])
def test_r1_payload_marker_must_match_the_sealed_approved_build(fake, site, marker):
    _, directory = site
    _, seal = sealed_site(site)
    source = directory / 'build'
    plan = seal['timePlans'][0]
    worker = schedules.worker_plan(plan)
    marker_path = source / payload.BUILD_MARKER
    if marker is None:
        marker_path.unlink()
        plan['inputSha256'].pop(payload.BUILD_MARKER)
    else:
        gate.session.save(marker_path, marker)
        plan['inputSha256'][payload.BUILD_MARKER] = bundle.sha(marker_path)
    archive = directory / 'input/session-0.tar.gz'
    info = dict(bundle=archive, plan=worker, sha256=bundle.pack(source, archive))
    before = events(fake)
    with pytest.raises(ValueError, match='container build marker'):
        payload.verify_inputs(directory, seal, plan, info)
    assert events(fake) == before


def test_r1_sealed_epoch_reaches_guest_keeper_and_long_async_cleanup(fake, site, ledger, monkeypatch):
    from types import SimpleNamespace
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    epoch = int(time.time())
    clock = [epoch]
    monkeypatch.setattr(azure, 'time', SimpleNamespace(time=lambda: clock[0], sleep=lambda n: clock.__setitem__(0, clock[0]+n)))
    fake_state(fake, vmErrors={'eastus2': 'SkuNotAvailable'}, deletesBeforeAbsent=16)
    native = Mock(side_effect=azure.arm_native)
    cloud = Mock(wraps=azure.cloud_init)
    monkeypatch.setattr(azure, 'arm_native', native)
    monkeypatch.setattr(azure, 'cloud_init', cloud)
    assert attempt(fake, local, ledger, directory, RUN) == 6
    entry = ledger.attempts[RUN]
    g = entry['guard']; c = entry['cutoff']
    assert entry['epoch'] == epoch and entry['ended'] - epoch > 135
    assert epoch < epoch+channel.WORKER_START < epoch+c < epoch+c+guard.COPY_END < epoch+g-70 < epoch+g < epoch+g+300
    cloud.assert_called_once_with(RUN, epoch+g)
    timer = next(x['content'] for x in json.loads(azure.cloud_init(RUN, epoch+g).split('\n',1)[1])['write_files'] if x['path'].endswith('.timer'))
    assert 'OnCalendar=' + azure.calendar(epoch+g) in timer
    assert native.call_args.args[2] == epoch+g+900 and entry['ended'] < native.call_args.args[2]


def test_r1_go_checks_sum_of_all_initial_session_reservations(fake, site, ledger, monkeypatch):
    local, directory = site
    one = timing.azure_booking(828 + azure.RESERVE_TAIL)
    ledger.attempts[RUN] = dict(state='settled', booked=round(40-4.29-2*one, 4))
    ledger.write()
    seal = dict(programStopUsd=40, timePlans=[dict(destroyGuardSeconds=828, repeatAllowed=False, provider='azure', region='eastus2')] * 2)
    ledger.attempts[RUN]['reservation'] = ledger.attempts[RUN]['booked']
    ledger.write()
    ledger.reserve(seal, 'f' * 64, [one])  # Independently affordable; not enough for both.
    with pytest.raises(azure.Refusal, match='cap stop'):
        azure.reconcile_and_pre_go(local, directory, seal, 'f' * 64)
    monkeypatch.setattr(bundle, 'checked_azure_go', lambda *_: (seal, local))
    with pytest.raises(azure.Refusal, match='cap stop'):
        bundle.azure_command('f' * 64, directory, dry=True)
    assert not any(method == 'put' for method, _ in events(fake))


def test_r1_push_put_refuses_immediately_after_copy_fence(tmp_path, monkeypatch):
    now = [10000]
    monkeypatch.setattr(azure.time, 'time', lambda: now[0])
    az = Mock(subscription=SID)
    draw = make_draw(az, tmp_path, start=now[0])
    draw.commands.update(('init', 'kick'))
    now[0] = draw.work_end + guard.COPY_END + 1
    with pytest.raises(guard.GuardFailure, match='expired'):
        draw.put(draw.path('Compute','virtualMachines')+'/runCommands/push-1', channel.VM_API, {}, work=True)
    az.rest.assert_not_called()


def test_r1_kept_archive_remains_valid_if_absence_crosses_g(site):
    _, directory = site
    _, seal = sealed_site(site)
    plan = seal['timePlans'][0]
    note = directory / 'time/runs' / RUN
    write_azure_result(note, plan, None)
    g = plan['destroyGuardSeconds']
    dump(note / 'duration-cost.json', dict(cleanupOutstanding=False, totalEstimateWithReserveUsd=timing.plan_price(plan),
         withinSealedGuard=False, lifetimeSeconds=g+9, cleanupStartedSeconds=g-70, guardSeconds=g, tailSeconds=9))
    machine = bundle.verified_machine(note, plan, 0, selected=[r['id'] for r in plan['rows']])
    assert machine['invalid'] is None
    assert 'over-guard cleanup tail' in machine['costWarnings'][0]


def test_r1_missing_ledger_does_not_restart_money(site):
    local, _ = site
    Path(local['azureLedger']).unlink(missing_ok=True)
    with pytest.raises(azure.Refusal, match='Missing Azure ledger'):
        azure.Ledger(local, read_only=True)


@pytest.mark.parametrize('outcome', ['watch-start', 'reproved', 'late-group', 'no-proof'])
def test_r1_watchdog_timing_and_provisional_absence(fake, site, ledger, monkeypatch, capsys, outcome):
    from types import SimpleNamespace
    local, directory = site
    name = abandoned(site, ledger, 'active', confirmed=outcome == 'watch-start')
    name.chmod(0o700)
    (name / 'local-path').write_text(os.environ['CHRYSALIS_GATE_LOCAL'])
    entry = ledger.attempts[RUN]
    e, g = entry['epoch'], entry['guard']
    dump(name/'watchdog.json', dict(group=RUN, region='eastus2', start=e, guard=g, cutoff=g-345,
         distro='test', user='fixture', subscriptionFingerprint=proof.fingerprint(SID)))
    monkeypatch.setattr(azure, 'HERE', directory / 'input/azure')
    monkeypatch.setattr(azure, 'wsl_context', lambda: ('test', 'fixture'))
    monkeypatch.setattr(bundle, 'checked_azure_local', lambda _: local)
    monkeypatch.setattr(gate, 'read_seal', lambda _: {})
    end, turn = e+g+900, e+g-70
    now = [e if outcome == 'watch-start' else end-30]
    def tick(_):
        if outcome == 'watch-start' and now[0] >= turn:
            pytest.fail('watchdog did not begin cleanup at G−70')
        now[0] = turn if now[0] < turn else end
    monkeypatch.setattr(azure, 'time', SimpleNamespace(time=lambda: now[0], sleep=tick))
    delete = Mock(wraps=azure.Draw.delete)
    monkeypatch.setattr(azure.Draw, 'delete', lambda *a, **k: delete(*a, **k))
    missing = Mock(side_effect=proof.AzureError('no exact baseline'))
    if outcome == 'no-proof': monkeypatch.setattr(azure, 'proof', missing)
    azure.watchdog(name)
    if outcome == 'watch-start':
        assert now[0] == turn and delete.call_count == 1 and (name/'watchdog-proof.json').is_file()
        return
    text = capsys.readouterr().out
    if outcome == 'no-proof':
        assert 'CLEANUP FAILURE' in text and (name/'failed').is_file()
        return
    assert 'provisional absence, re-proof owed at next GO' in text and 'CLEANUP FAILURE' not in text
    assert entry['state'] == 'active' and ledger.total() == Decimal('4.29') + Decimal(str(entry['reservation']))
    if outcome == 'late-group': monkeypatch.setattr(azure, 'proof', missing)
    if outcome == 'late-group':
        with pytest.raises(azure.Refusal, match='coordinator'):
            azure.recover(fake, ledger, local['azureBaseline'], directory.parent)
        assert entry['state'] == 'cleanup-failed'
    else:
        azure.recover(fake, ledger, local['azureBaseline'], directory.parent)
        assert entry['state'] == 'settled' and entry['ended'] >= end


@pytest.mark.parametrize(('tfm', 'fault'), [('10.0', None), ('11.0', None), ('11.0', 'missing'), ('11.0', 'extra')])
def test_r1a_deps_selected_capstone_layout(site, tfm, fault):
    _, directory = site
    _, seal = sealed_site(site, tfm='net' + tfm)
    source = directory / 'build'
    plan = seal['timePlans'][0]
    worker = schedules.worker_plan(plan)
    root = Path(next(iter(worker['builds'].values()))['client']).parent
    # AZ2 restores runtimeTargets on the .NET 10 direct-reference closure;
    # .NET 11 carries the same Linux asset in the normal package graph.
    target = f'.NETCoreApp,Version=v{tfm}'
    native = 'runtimes/linux-x64/native/libcapstone.so'
    capstone = 'Gee.External.Capstone/2.3.0' + ('.0' if tfm == '10.0' else '')
    graph = {capstone: {'runtimeTargets': {native: dict(rid='linux-x64', assetType='native')}}}
    files = {str(root / 'ReplayClient.deps.json'): json.dumps(dict(runtimeTarget={'name': target}, targets={target: graph}, libraries={capstone: {}})).encode()}
    if fault != 'missing':
        files[str(root / native)] = b'fixture Linux binary'
    if fault == 'extra':
        files[str(root / native.replace('capstone', 'unselected'))] = b'extra Linux binary'
    for name, data in files.items():
        path = source / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        plan['inputSha256'][name] = hashlib.sha256(data).hexdigest()
    archive = directory / 'input/session-0.tar.gz'
    info = dict(bundle=archive, plan=worker, sha256=bundle.pack(source, archive))
    if fault:
        with pytest.raises(ValueError, match='Azure input paths'):
            payload.verify_inputs(directory, seal, plan, info)
    else:
        assert payload.verify_inputs(directory, seal, plan, info) == {'payload': archive}


@pytest.mark.parametrize('creation_busy', [False, True], ids=['cleanup-only', 'both-locks'])
def test_r1a_stalled_cleanup_lease_cannot_block_watchdog_delete(fake, site, ledger, monkeypatch, creation_busy):
    from select import select
    from types import SimpleNamespace
    local, directory = site
    state = abandoned(site, ledger, 'active', confirmed=True)
    state.chmod(0o700)
    (state / 'local-path').write_text(os.environ['CHRYSALIS_GATE_LOCAL'])
    e = ledger.attempts[RUN]['epoch']
    dump(state/'watchdog.json', dict(group=RUN, region='eastus2', start=e, guard=828, cutoff=483,
         distro='test', user='fixture', subscriptionFingerprint=proof.fingerprint(SID)))
    scope = SUB + '/resourceGroups/' + RUN
    fake_state(fake, groups={scope: dict(location='eastus2', tags={'chrysalisGate': RUN})}, deletesBeforeAbsent=2)
    monkeypatch.setattr(azure, 'HERE', directory / 'input/azure')
    monkeypatch.setattr(azure, 'wsl_context', lambda: ('test', 'fixture'))
    monkeypatch.setattr(bundle, 'checked_azure_local', lambda _: local)
    monkeypatch.setattr(gate, 'read_seal', lambda _: {})
    deadline = e + 828 + guard.RESERVE_TAIL
    # With the creation fence busy, proof is allowed only in the final window.
    now = [deadline - 20 if creation_busy else e + 828 - guard.WATCH_MARGIN]
    def tick(seconds):
        now[0] += seconds
    monkeypatch.setattr(azure, 'time', SimpleNamespace(time=lambda: now[0], sleep=tick))
    killed = Mock()
    monkeypatch.setattr(azure, 'stop_paused_driver', killed)
    hold = """from contextlib import nullcontext
from pathlib import Path
import sys
import gate_azure_guard as guard
state = Path(sys.argv[1])
with guard.cleanup_lease(state), (guard.retire(state) if sys.argv[2] == 'True' else nullcontext()):
    print('held', flush=True)
    sys.stdin.read()  # Keep the locks until the watchdog returns and closes stdin.
"""
    with subprocess.Popen([sys.executable, '-B', '-c', hold, str(state), str(creation_busy)], cwd=HERE,
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE) as cleaner:
        try:
            assert select([cleaner.stdout], [], [], 10)[0] and cleaner.stdout.readline() == b'held\n'
            azure.watchdog(state)
            assert cleaner.poll() is None
        finally:
            try:
                cleaner.communicate(timeout=10)
            except subprocess.TimeoutExpired:
                cleaner.kill()
                raise
    assert cleaner.returncode == 0  # It exited on EOF, not by signal.
    killed.assert_not_called()
    assert not fake_state(fake)['groups'] and any(method == 'delete' for method, _ in events(fake))
    assert json.loads((state / 'watchdog-proof.json').read_text())['provisional'] is creation_busy
    assert (state / 'provisional-reported').exists() is creation_busy and not (state / 'failed').exists()
    assert ledger.attempts[RUN]['state'] == 'active'  # no competing settlement


def test_r1a_daily_inventory_for_live_go_has_no_cost_due(fake, site, capsys):
    local, directory = site
    go = '9' * 64
    (directory / 'seal.id').write_text(go + '\n')
    path = Path(local['azureLedger'])
    data = json.loads(path.read_text())
    data['attempts'][RUN] = dict(go=go, state='active', epoch=int(time.time()), guard=828, reservation=.3)
    dump(path, data)
    fake_state(fake, groups={SUB + '/resourceGroups/' + RUN: dict(location='eastus2', tags={'chrysalisGate': RUN})})
    before = path.read_bytes()
    with acquire(local['azureLock']):
        assert azure.daily_cost(directory, local) == 0
    assert 'No +1 or +3 day Azure cost readback due' in capsys.readouterr().out
    assert path.read_bytes() == before
    assert not any(method == 'post' for method, _ in events(fake))


def test_r2_typical_controls_notice_uses_assessed_history(ledger):
    seal = dict(programStopUsd=40, lineageCapUsd=6)
    def guaranteed(amount):
        return ledger.notice(seal, 'original', [amount] * 6, 0, 'first')['guaranteedAttempts']
    assert guaranteed(1.2906) == 4
    ledger.attempts[RUN] = dict(state='settled', reservation=2.5812, booked=.90, lineage='original')
    assert guaranteed(1.1672) == 4
    ledger.attempts[OTHER] = dict(state='settled', reservation=2.3344, booked=.76, lineage='original')
    seal['retryOf'] = dict(originalSealId='original', kind='first')
    assert guaranteed(1.2906) == 3


@pytest.mark.parametrize('fault', ['missing', 'mismatch', 'outer-digest', None])
def test_r2_suite_requires_approved_marker_and_prechecks_sha(site, fault):
    _, directory = site
    _, seal = sealed_site(site)
    plan = seal['timePlans'][0]
    plan['suite'] = True
    schedules.prepare(plan)
    worker = schedules.worker_plan(plan)
    source = directory / 'build'
    gate.session.save(source / 'plan.json', worker)
    packed = directory / 'input/session-0.tar.gz'
    info = dict(plan=worker, bundle=packed, sha256=bundle.pack(source, packed))
    suite = directory / 'input/suite.tar.gz'
    members = {} if fault == 'missing' else {payload.BUILD_MARKER: dict(BUILD_MARKER)}
    if fault == 'mismatch':
        members[payload.BUILD_MARKER]['mode'] = 'native-mac'
    archive(suite, members)
    seal['suite'] = {'members': {name: hashlib.sha256(json.dumps(value).encode()).hexdigest() for name, value in members.items()}}
    seal['files']['input/suite.tar.gz'] = bundle.sha(suite)
    if fault == 'outer-digest':
        suite.write_bytes(b'tar reader must not see this')
    if fault:
        with pytest.raises(ValueError, match='build marker|suite.*changed'):
            payload.verify_inputs(directory, seal, plan, info)
    else:
        assert payload.verify_inputs(directory, seal, plan, info)['suite'] == suite


@pytest.mark.parametrize('state', ['active', 'cleanup-failed'])
def test_r2_actual_cost_cannot_exceed_unsettled_reservation(fake, state):
    now = int(time.time())
    ymd = int(datetime.fromtimestamp(now, timezone.utc).strftime('%Y%m%d'))
    fake_state(fake, cost=dict(columns=['PreTaxCost', 'UsageDate', 'ResourceGroup', 'Currency'],
                              rows=[[.31, ymd, RUN, 'USD']], paginated=False))
    checks = proof.cost_readback(fake, 'baseline', {RUN: dict(state=state, reservation=.30)}, now - 3600, now)
    assert checks['stops'] == ['posted exceeds booking']


def test_r2_windows_keeper_lets_watchdog_write_terminal_marker(tmp_path, monkeypatch):
    monkeypatch.setattr(guard, 'POWERSHELL', tmp_path / 'powershell.exe')
    guard.POWERSHELL.touch()
    (tmp_path / 'ready').touch()
    monkeypatch.setattr(guard, 'check_context', lambda *_: None)
    spawn = Mock(return_value=Mock(returncode=0))
    monkeypatch.setattr(guard.subprocess, 'run', spawn)
    guard.arm_native(tmp_path / 'driver.py', tmp_path, 123456, 'fixture', 'user')
    argv = spawn.call_args.args[0][-1]
    encoded = re.search(r'EncodedCommand ([A-Za-z0-9+/=]+)', argv)[1]
    assert 'FromUnixTimeSeconds(123471)' in base64.b64decode(encoded).decode('utf-16le')


@pytest.mark.parametrize('history', ['ledger', 'run'])
def test_r2_initialize_ledger_refuses_to_reset_any_history(site, history):
    local, directory = site
    path = Path(local['azureLedger'])
    original = path.read_bytes()
    if history == 'run':
        path.unlink()
        (directory / 'time/runs' / RUN).mkdir(parents=True)
    script = ('import gate, runpy, sys; from pathlib import Path; gate.REPO = Path(sys.argv[1]); '
              'sys.argv = ["gate_azure.py", "initialize-ledger"]; runpy.run_module("gate_azure", run_name="__main__")')
    result = subprocess.run([sys.executable, '-B', '-c', script, str(gate.REPO)], cwd=HERE, capture_output=True, text=True)
    assert result.returncode == 4 and 'restore the original ledger' in result.stderr
    assert (path.read_bytes() if path.exists() else None) == (original if history == 'ledger' else None)


def _patch_guest_push(monkeypatch, store, work, *, done=True, upload_fn=None):
    monkeypatch.setattr(guest, 'TEMP', store)
    monkeypatch.setattr(guest, 'WORK', work)
    monkeypatch.setattr(guest, 'RESULTS_KEY', store / 'results.pem')
    monkeypatch.setattr(guest, 'compact', done if callable(done) else (lambda: {'done': done}))
    monkeypatch.setattr(guest, 'unwrap', lambda *_: 'synthetic-credential')
    monkeypatch.setattr(guest, 'upload', upload_fn or (lambda *a, **kw: None))


def _make_realistic_obs(warmup, measured, seed=1):
    rows = [
        {
            'pass': i if i <= warmup else i - warmup,
            'blocks': 1020,
            'allocated': 7928912,
            'bytesPerBlock': 7928.9 + (seed + i) * 0.001,
            'nsPerBlock': 91000.0 + ((seed * 31 + i * 17) % 1000) * 0.13,
            'wireBytes': 4040750,
            'startTimestamp': 4203503727759 + i * 95_000_000,
            'endTimestamp': 4203503822759 + i * 95_000_000,
            'elapsedSinceSeriesStartNs': i * 95_000_000,
            'gen0Collections': 0,
            'gen1Collections': 0,
            'gen2Collections': 0,
        }
        for i in range(1, warmup + measured + 1)
    ]
    bounds = [
        dict(
            stage=st,
            passIndex=i,
            elapsedNs=i * 95_000_000,
            cumulative=dict(
                compiledMethods=688, compiledILBytes=45356, compilationTimeTicks=895376,
                gen0=0, gen1=0, gen2=0,
            ),
            delta=dict(
                compiledMethods=0, compiledILBytes=0, compilationTimeTicks=0,
                gen0=0, gen1=0, gen2=0,
            ),
        )
        for st, cnt in (('warmup', warmup), ('measured', measured))
        for i in range(cnt + 1)
    ]
    assemblies = [
        dict(identity=f'System.Mod{i:02d}, Version=11.0.0.0', path=f'/opt/chrysalis/dotnet/System.Mod{i:02d}.dll')
        for i in range(45)
    ]
    return dict(
        runtime='11.0.0', replay='offline', warmupPasses=warmup, passes=measured,
        warmupRows=rows[:warmup], rows=rows[warmup:], warmupElapsedNs=warmup * 95_000_000,
        groupBoundaries=bounds, stopwatchFrequency=1_000_000_000, loadedAssemblies=assemblies,
        processSettings=dict(gcConfiguration=dict(GCGen0MaxBudget=16777216)),
    )


def _build_noise_s0_tree(work):
    res = work / 'results'
    (res / 'probe').mkdir(parents=True)
    (work / 'prelude/raw').mkdir(parents=True)
    dlls = {f'Chrysalis.Mod{i:02d}.dll': f'{i:064x}' for i in range(41)}
    slim_proc = json.dumps(
        dict(
            pid=100, exitCode=0,
            before=dict(monotonic=1.0, unix=1.0, load1=0.1),
            after=dict(monotonic=14.0, unix=14.0, load1=0.1),
            seconds=13.0,
            marks=dict(
                MEASURED_START=dict(monotonic=2.0, unix=2.0, load1=0.1),
                MEASURED_END=dict(monotonic=13.5, unix=13.5, load1=0.1),
            ),
            samples=[dict(monotonic=2.0 + i * 0.25, unix=2.0 + i * 0.25, load1=0.1) for i in range(48)],
            foreignCpuSeconds=0.0, timingRejectedReasons=[],
            steal='not observable on Azure', stealFieldMoved=False,
        ),
        indent=2,
    ) + '\n'
    obs = _make_realistic_obs(180, 60, seed=1)
    obs_txt = json.dumps(obs, indent=2) + '\n'
    env_txt = json.dumps({'DOTNET_NOLOGO': '1', 'CHRYSALIS_CORPORA': '/opt/chrysalis/corpora'}, indent=2) + '\n'
    rec_base = dict(
        data=obs,
        identity=dict(commit='3' * 40, treeId='4' * 40, runtime='11.0.0', dllSha256s=dlls),
        platform='azure', timingRejectedReasons=[],
        measurement=dict(kind='time', counts=[180, 60], filter='*'),
        load=[0.1, 0.1], seconds=13.0, marks=['MEASURED_END', 'MEASURED_START'],
        wholeProcess=dict(
            startMonotonic=1.0, endMonotonic=14.0,
            aggregateSteal='not observable on Azure', stealFieldMoved=False,
        ),
    )
    rec_txt = json.dumps(rec_base, indent=2) + '\n'

    def write_proc(target):
        (target / 'execution').mkdir(parents=True)
        (target / 'observations.json').write_text(obs_txt)
        (target / 'environment.json').write_text(env_txt)
        (target / 'execution/console.log').write_text('MEASURED_START\nMEASURED_END\n')
        (target / 'execution/process.json').write_text(slim_proc)

    for i in range(1, 17):
        write_proc(res / 'probe' / f'probe-{i:02d}')
        (res / 'probe' / f'probe-{i:02d}.json').write_text(rec_txt)
    members = []
    for idx in range(1, 65):
        mid = f'{idx:016x}'
        mdir = res / mid
        write_proc(mdir)
        if idx > 32:
            (mdir / 'server.ready.json').write_text('{"port":45678}\n')
            (mdir / 'server-summary.json').write_text('[{"completedRanges":240}]\n')
            (mdir / 'server.log').write_text('done\n')
        members.append(dict(rec_base, id=mid, build='cb39d34dd5e031f2'))
    (res / 'members.json').write_text(json.dumps(members, indent=2) + '\n')
    pre_txt = json.dumps(dict(rec_base, data=_make_realistic_obs(1, 1, seed=2)), indent=2) + '\n'
    for i in range(4):
        (work / 'prelude/raw' / f'{i:016x}-baseline.json').write_text(pre_txt)
    ordering = dict(rec_base, data=_make_realistic_obs(180, 5, seed=3))
    (work / 'prelude/raw/ordering.json').write_text(json.dumps(ordering, indent=2) + '\n')
    (work / 'prelude/applied.json').write_text('{"applied":true}\n')
    for name, obj in (
        ('host.json', {'processorCount': 8, 'vendor_id': 'AuthenticAMD'}),
        ('probe.json', {'cv': 0.01, 'lag1': 0.01, 'decision': 'kept'}),
        ('session-start.json', {'available': True}),
        ('session-status.json', {'completedAllDeclaredRounds': True, 'exitCode': 0, 'probe': 'kept'}),
    ):
        dump(res / name, obj)
    (work / 'session.log').write_text('ok\n')
    return {'probe': {'processes': 16}, 'members': {f'{i:016x}': {'build': 'b'} for i in range(1, 65)}}


def test_az2f4_noise_seal_full_size_results_archive_and_limits(tmp_path, monkeypatch):
    import gzip
    work = tmp_path / 'work'
    s0_plan = _build_noise_s0_tree(work)
    monkeypatch.setattr(guest, 'WORK', work)
    red = tmp_path / 'red.tar.gz'
    green = tmp_path / 'green.tar.gz'
    ref = tmp_path / 'ref.tar.gz'
    assert guest.archive_results(red, False) is True
    assert guest.check_results(red) is True
    with tarfile.open(red) as stream:
        red_names = set(stream.getnames())
        assert {'results/recovery-diagnostic.json', 'results/session-status.json', 'results/session.log'} <= red_names
        assert any(n.endswith('/server.log') or n.endswith('/execution/console.log') for n in red_names)
        diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
        status = json.load(stream.extractfile('results/session-status.json'))
    assert diag['diagnosticOnly'] is True and diag['reason'] == 'results-too-large'
    assert diag['expandedBytes'] > guest.MAX_RESULTS == diag['maxExpandedBytes']
    assert status['exitCode'] == 0 and status['completedAllDeclaredRounds'] is True
    assert guest.MAX_RESULTS_COMPRESSED == 10_000_000
    limits = guest.results_limits(s0_plan, strict=True)
    assert limits == (10_000_000, 90_000_000)
    assert guest.archive_results(green, False, limits=limits) is False
    assert guest.check_results(green, limits=limits) is False
    assert green.stat().st_size < guest.MAX_RESULTS_COMPRESSED
    # GREEN archive bytes match ec97c99a's non-partial packing rule byte-for-byte.
    with tarfile.open(ref, 'w:gz') as stream:
        for folder in ('results', 'prelude', 'bytes', 'suite'):
            if (work / folder).exists():
                for member in sorted((work / folder).rglob('*')):
                    if not member.is_dir():
                        stream.add(member, arcname=member.relative_to(work).as_posix(), recursive=False)
        stream.add(work / 'session.log', arcname='results/session.log', recursive=False)
    assert gzip.decompress(green.read_bytes()) == gzip.decompress(ref.read_bytes())
    s1_plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(192)}}
    max_plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(209)}}
    assert guest.results_limits(s1_plan, strict=True) == (10_000_000, 218_000_000)
    assert guest.results_limits(max_plan, strict=True) == (10_000_000, 235_000_000)
    for over in (210, 480, 1000):
        over_plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(over)}}
        with pytest.raises(ValueError, match='hard ceiling'):
            guest.results_limits(over_plan, strict=True)
    runaway = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(1000)}}
    assert guest.results_limits(runaway) == (guest.MAX_RESULTS_COMPRESSED, guest.MAX_RESULTS_EXPANDED)


def test_az2f4_refusal_reasons_and_clean_status_preservation(tmp_path, monkeypatch):
    work = tmp_path / 'work'
    (work / 'results').mkdir(parents=True)
    status = work / 'results/session-status.json'
    dump(status, {'completedAllDeclaredRounds': False, 'exitCode': 2, 'reason': 'budget stop'})
    monkeypatch.setattr(guest, 'WORK', work)
    dest = tmp_path / 'out.tar.gz'
    zero = {'logsIncluded': 0, 'logsTruncated': 0, 'logsSkipped': 0, 'linesRedacted': 0}
    assert guest.archive_results(dest, True, reason='not-done') is True
    with tarfile.open(dest) as stream:
        assert sorted(stream.getnames()) == ['results/recovery-diagnostic.json', 'results/session-status.json']
        assert json.load(stream.extractfile('results/recovery-diagnostic.json')) == {
            'diagnosticOnly': True, 'reason': 'not-done', **zero,
        }
    summary = azure._diagnostic_summary(dest)
    assert summary['partial'] is True and summary['reason'] == 'not-done'
    assert summary['sessionStatus'] == {'completedAllDeclaredRounds': False, 'exitCode': 2}
    (work / 'results/unexpected.txt').write_text('bad')
    assert guest.archive_results(dest, False) is True
    with tarfile.open(dest) as stream:
        assert sorted(stream.getnames()) == ['results/recovery-diagnostic.json', 'results/session-status.json']
        assert json.load(stream.extractfile('results/recovery-diagnostic.json'))['reason'] == 'forbidden-member'
    (work / 'results/unexpected.txt').unlink()
    dump(status, {'leak': '-----BEGIN PRIVATE KEY-----'})
    assert guest.archive_results(dest, False) is True
    with tarfile.open(dest) as stream:
        assert stream.getnames() == ['results/recovery-diagnostic.json']
        assert json.load(stream.extractfile('results/recovery-diagnostic.json'))['reason'] == 'credential-shaped'
    status.write_text('{not json')
    assert guest.archive_results(dest, True, reason='not-done') is True
    with tarfile.open(dest) as stream:
        assert stream.getnames() == ['results/recovery-diagnostic.json']
    status.unlink()
    assert guest.archive_results(dest, True, reason='no-status') is True
    with tarfile.open(dest) as stream:
        assert stream.getnames() == ['results/recovery-diagnostic.json']
        assert json.load(stream.extractfile('results/recovery-diagnostic.json')) == {
            'diagnosticOnly': True, 'reason': 'no-status', **zero,
        }


def test_az2f4_channel_and_guest_plan_limits_agree(tmp_path, monkeypatch):
    plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {'build': 'b'} for i in range(64)}}
    payload_tar = tmp_path / 'session-0.tar.gz'
    archive(payload_tar, {'plan.json': plan})
    expected = guest.results_limits(plan)
    assert guest.plan_results_limits(payload_tar) == channel.plan_results_limits(payload_tar) == expected
    assert expected == (10_000_000, 90_000_000)
    store = tmp_path / 'store'
    work = tmp_path / 'work'
    store.mkdir()
    (work / 'results').mkdir(parents=True)
    dump(work / 'results/session-status.json', {'completedAllDeclaredRounds': True, 'exitCode': 0})
    (work / 'results/session.log').write_bytes(b'A' * 12_000_000)
    guest._save_limits(expected, store / 'results-limits.json')
    uploaded = []
    _patch_guest_push(
        monkeypatch, store, work,
        upload_fn=lambda _a, _n, _s, path, lim, _u: uploaded.append((path.read_bytes(), lim)),
    )
    summary = guest.push('fixture', time.time() + 130, True)
    assert summary['partial'] is False and uploaded[0][1] == expected[0]
    built = store / 'results.tar.gz'
    monkeypatch.setattr(channel.time, 'time', lambda: 100)
    draw = Mock(work_end=100, account='storage-fixture')
    for attr, fn in (
        ('storage_key', lambda _: 'CANARY' * 8),
        ('signed_blob', lambda *_: 'CANARY' * 8),
        ('seal', lambda *_: 'ciphertext'),
        ('command', lambda *a, **kw: summary),
    ):
        monkeypatch.setattr(channel, attr, fn)

    def fake_dl(_a, _b, _s, target, lim, _d):
        assert lim == expected[0]
        shutil.copyfile(built, target)
        return summary['sha256']

    monkeypatch.setattr(channel, 'download', fake_dl)
    ev = tmp_path / 'ev'
    ev2 = tmp_path / 'ev2'
    ev.mkdir()
    ev2.mkdir()
    assert channel.copy_results(draw, ev, 'unused', '# ', limits=expected) is False
    with pytest.raises(ValueError, match='results-too-large|exceeds'):
        channel.copy_results(draw, ev2, 'unused', '# ')


def test_az2f4_fix2_results_too_large_includes_bounded_logs_and_unit_status(tmp_path, monkeypatch):
    work = tmp_path / 'work'
    for sub in ('results/probe/probe-01/execution', 'prelude/raw/p1', 'bytes/b1/execution'):
        (work / sub).mkdir(parents=True)
    dump(work / 'results/session-status.json', {'completedAllDeclaredRounds': False, 'exitCode': 2})
    (work / 'results/probe/probe-01/observations.json').write_bytes(b'X' * (guest.MAX_RESULTS + 50_000))
    (work / 'session.log').write_bytes(b'SESSION_HEAD\n' + (b's' * 63 + b'\n') * 4800 + b'SESSION_TAIL\n')
    base_mtime = 1_700_000_000
    created = []
    for i in range(30):
        if i == 0:
            p = work / 'prelude/raw/p1/server.log'
        elif i == 1:
            p = work / 'bytes/b1/execution/console.log'
        else:
            d = work / 'results' / f'{i:016x}'
            (d / 'execution').mkdir(parents=True, exist_ok=True)
            p = (d / 'server.log') if i % 2 == 0 else (d / 'execution/console.log')
        p.write_bytes(f'HEAD-{i:02d}\n'.encode() + (b'l' * 63 + b'\n') * 320 + f'TAIL-{i:02d}\n'.encode())
        os.utime(p, (base_mtime + i * 10, base_mtime + i * 10))
        created.append((i, p.relative_to(work).as_posix()))
    outside = tmp_path / 'outside-secret.txt'
    outside.write_text('OUTSIDE_SECRET_NEVER_READ\n')
    bad_dir = work / 'results' / f'{99:016x}' / 'execution'
    bad_dir.mkdir(parents=True)
    (bad_dir.parent / 'server.log').symlink_to(outside)
    sticky = bad_dir / 'console.log'
    sticky.write_text('STICKY_NEVER_READ\n')
    sticky.chmod(0o1600)
    fifo_dir = work / 'results' / f'{98:016x}'
    fifo_dir.mkdir(parents=True)
    os.mkfifo(fifo_dir / 'server.log')
    monkeypatch.setattr(guest, 'WORK', work)
    unit_ok = b'LoadState=loaded\nResult=timeout\nExecMainCode=2\nExecMainStatus=15\n'
    monkeypatch.setattr(guest.subprocess, 'check_output', lambda *a, **kw: unit_ok)
    dest = tmp_path / 'diag.tar.gz'
    assert guest.archive_results(dest, False) is True
    assert guest.check_results(dest) is True
    assert dest.stat().st_size <= guest.MAX_DIAGNOSTIC
    with tarfile.open(dest) as stream:
        names = set(stream.getnames())
        assert sum(m.size for m in stream.getmembers()) <= guest.MAX_DIAGNOSTIC
        diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
        status = json.load(stream.extractfile('results/session-status.json'))
        slog = stream.extractfile('results/session.log').read()
        proc_names = sorted(n for n in names if n.endswith('/server.log') or n.endswith('/execution/console.log'))
        assert proc_names == sorted(arc for i, arc in created if i >= 6)
        for i, arc in created:
            if i >= 6:
                body = stream.extractfile(arc).read()
                assert 0 < len(body) <= 16 * 1024
                assert body.endswith(f'TAIL-{i:02d}\n'.encode())
                assert f'HEAD-{i:02d}'.encode() not in body
        forbidden = {
            f'results/{99:016x}/server.log',
            f'results/{99:016x}/execution/console.log',
            f'results/{98:016x}/server.log',
        }
        assert not (forbidden & names)
    assert 255 * 1024 < len(slog) <= 256 * 1024
    assert slog.endswith(b'SESSION_TAIL\n') and b'SESSION_HEAD' not in slog
    assert status == {'completedAllDeclaredRounds': False, 'exitCode': 2}
    assert diag['reason'] == 'results-too-large'
    assert (diag['logsIncluded'], diag['logsTruncated'], diag['logsSkipped'], diag['linesRedacted']) == (25, 25, 9, 0)
    assert (diag['unitResult'], diag['execMainCode'], diag['execMainStatus']) == ('timeout', 2, 15)
    summary = azure._diagnostic_summary(dest)
    assert summary['reason'] == 'results-too-large' and summary['logsIncluded'] == 25
    assert summary['unitResult'] == 'timeout' and 'SESSION_TAIL' not in json.dumps(summary)
    unit_bad = b'LoadState=loaded\nResult=INVALID RESULT\nExecMainCode=nan\nExecMainStatus=-1\n'
    monkeypatch.setattr(guest.subprocess, 'check_output', lambda *a, **kw: unit_bad)
    dest2 = tmp_path / 'diag2.tar.gz'
    assert guest.archive_results(dest2, False) is True
    with tarfile.open(dest2) as stream:
        diag2 = json.load(stream.extractfile('results/recovery-diagnostic.json'))
    assert not any(k in diag2 for k in ('unitResult', 'execMainCode', 'execMainStatus'))
    bad_marker = tmp_path / 'bad-marker.tar.gz'
    archive(bad_marker, {
        'results/recovery-diagnostic.json': {
            'diagnosticOnly': True, 'reason': 'invented-reason', 'stage': 'invented-stage',
        },
    })
    assert not ({'reason', 'stage'} & set(azure._diagnostic_summary(bad_marker)))


def test_az2f4_fix2_worker_crash_no_status_returns_traceback_tail(tmp_path, monkeypatch):
    work = tmp_path / 'work'
    store = tmp_path / 'store'
    work.mkdir()
    store.mkdir()
    tb = (
        'starting worker\n'
        'Traceback (most recent call last):\n'
        '  File "gate_bundle.py", line 1\n'
        'RuntimeError: synthetic worker crash\n'
    )
    (work / 'session.log').write_text(tb)
    _patch_guest_push(monkeypatch, store, work)
    unit_out = b'LoadState=loaded\nResult=exit-code\nExecMainCode=1\nExecMainStatus=1\n'
    monkeypatch.setattr(guest.subprocess, 'check_output', lambda *a, **kw: unit_out)
    assert guest.push('fixture', time.time() + 130, True)['partial'] is True
    assert guest.check_results(store / 'results.tar.gz') is True
    with tarfile.open(store / 'results.tar.gz') as stream:
        assert sorted(stream.getnames()) == ['results/recovery-diagnostic.json', 'results/session.log']
        diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
        slog = stream.extractfile('results/session.log').read().decode()
    assert diag['reason'] == 'no-status' and diag['logsIncluded'] == 1
    assert (diag['unitResult'], diag['execMainCode'], diag['execMainStatus']) == ('exit-code', 1, 1)
    assert 'RuntimeError: synthetic worker crash' in slog


def test_az2f4_fix2_credential_shaped_log_lines_redacted_and_accepted(tmp_path, monkeypatch):
    work = tmp_path / 'work'
    (work / 'results/0000000000000001').mkdir(parents=True)
    dump(work / 'results/session-status.json', {'completedAllDeclaredRounds': True, 'exitCode': 0})
    (work / 'session.log').write_text(
        'line-before\n'
        'url https://example.invalid/?sig=' + 'CANARY' * 8 + '\n'
        '-----BEGIN RSA PRIVATE KEY-----\n'
        '-----END RSA PRIVATE KEY-----\n'
        'line-after\n'
    )
    (work / 'results/0000000000000001/server.log').write_text(
        'server-ok\nquery sv=2026-11-20&sp=r&sr=b&other=1\nserver-end\n'
    )
    monkeypatch.setattr(guest, 'WORK', work)
    dest = tmp_path / 'redacted.tar.gz'
    assert guest.archive_results(dest, False) is True
    assert guest.check_results(dest) is True
    with tarfile.open(dest) as stream:
        assert sorted(stream.getnames()) == [
            'results/0000000000000001/server.log',
            'results/recovery-diagnostic.json',
            'results/session-status.json',
            'results/session.log',
        ]
        diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
        slog = stream.extractfile('results/session.log').read().decode()
        srv = stream.extractfile('results/0000000000000001/server.log').read().decode()
    assert diag['reason'] == 'credential-shaped' and diag['logsIncluded'] == 2 and diag['linesRedacted'] == 4
    assert slog == 'line-before\n' + '[redacted: credential-shaped line]\n' * 3 + 'line-after\n'
    assert srv == 'server-ok\n[redacted: credential-shaped line]\nserver-end\n'
    assert not any(leak in slog or leak in srv for leak in ('CANARY', 'BEGIN RSA PRIVATE KEY', 'sr=b'))


def test_az2f4_fix2_fetch_stage_failures_and_main_error_line(tmp_path, monkeypatch):
    plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {'build': 'b'} for i in range(64)}}
    good_tar = tmp_path / 'good.tar.gz'
    bad_tar = tmp_path / 'bad.tar.gz'
    no_plan_tar = tmp_path / 'no-plan.tar.gz'
    archive(good_tar, {'plan.json': plan})
    bad_tar.write_bytes(b'not-a-tarball')
    archive(no_plan_tar, {'other.txt': b'hi'})
    good_sha = bundle.sha(good_tar)
    bad_sha = bundle.sha(bad_tar)
    no_plan_sha = bundle.sha(no_plan_tar)
    account = 'chry' + '0' * 16
    now = int(time.time())
    orig_pl = guest.plan_results_limits

    def setup_fetch(name, *, src=good_tar, stage=None):
        store = tmp_path / name / 'store'
        work = tmp_path / name / 'work'
        store.mkdir(parents=True)
        _patch_guest_push(monkeypatch, store, work)
        monkeypatch.setattr(guest, 'PAYLOAD_KEY', store / 'payload.pem')
        monkeypatch.setattr(guest.time, 'time', lambda: now + 10)
        if stage == 'extract':
            monkeypatch.setattr(guest, 'plan_results_limits', lambda _a: (10_000_000, 90_000_000))
        else:
            monkeypatch.setattr(guest, 'plan_results_limits', orig_pl)
        if stage == 'download':
            def fail_dl(*_a, **_kw):
                raise OSError('https://secret.blob.invalid/?sig=LEAK')
            monkeypatch.setattr(guest, 'download', fail_dl)
        else:
            def copy_dl(_a, _n, _s, target, _l, _d):
                shutil.copyfile(src, target)
                return 'f' * 64 if stage == 'digest' else bundle.sha(src)
            monkeypatch.setattr(guest, 'download', copy_dl)
        if stage == 'systemd-run':
            def fail_run(*_a, **_kw):
                raise subprocess.CalledProcessError(1, 'systemd-run')
            monkeypatch.setattr(guest.subprocess, 'run', fail_run)
        else:
            monkeypatch.setattr(guest.subprocess, 'run', lambda *a, **kw: subprocess.CompletedProcess(a[0], 0))
        ts = now + 9999 if stage == 'start-window' else now + 10
        monkeypatch.setattr(
            guest.subprocess, 'check_output',
            lambda cmd, **kw: b'2026-11-20\n' if cmd[0] == 'systemctl' else str(ts).encode(),
        )
        return store

    for stage in ('download', 'digest', 'limits', 'extract', 'deadline', 'systemd-run', 'start-window', 'other'):
        if stage == 'limits':
            src, exp_sha = no_plan_tar, no_plan_sha
        elif stage == 'extract':
            src, exp_sha = bad_tar, bad_sha
        else:
            src, exp_sha = good_tar, good_sha
        store = setup_fetch(stage, src=src, stage=stage)
        req_acct = 'bad-account' if stage == 'other' else account
        start_by = (now + 5) if stage == 'deadline' else (now + 300)
        with pytest.raises((ValueError, KeyError, OSError, subprocess.CalledProcessError, tarfile.TarError)):
            guest.fetch(req_acct, exp_sha, '-', now, start_by, now + 900, 'eastus2')
        rec = store / 'fetch-failure.json'
        assert rec.is_file() and (rec.stat().st_mode & 0o777) == 0o600
        assert json.loads(rec.read_text()) == {'reason': 'not-started', 'stage': stage}
        assert 'LEAK' not in rec.read_text() and str(tmp_path) not in rec.read_text()
        assert guest.push(account, now + 900, True)['partial'] is True
        with tarfile.open(store / 'results.tar.gz') as stream:
            diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
        assert diag['reason'] == 'not-started' and diag['stage'] == stage
        assert azure._diagnostic_summary(store / 'results.tar.gz')['stage'] == stage

    ok_store = setup_fetch('ok')
    assert guest.fetch(account, good_sha, '-', now, now + 300, now + 900, 'eastus2') == {'started': now + 10}
    assert guest._saved_limits() == guest.results_limits(plan) == (10_000_000, 90_000_000)
    assert not (ok_store / 'fetch-failure.json').exists()
    monkeypatch.undo()
    proc = subprocess.run(
        [sys.executable, str(HERE / 'gate_azure_guest.py'), 'fetch', 'bad', '0' * 64, '-', '1', '2', '3', 'eastus2'],
        capture_output=True, text=True,
    )
    assert proc.returncode == 75
    assert channel.get_summary(proc.stdout) == {
        'error': 'guest control step failed', 'step': 'fetch', 'type': 'ValueError',
    }
    assert 'invalid work request' not in proc.stdout


def test_az2f4_fix2_host_collects_diagnostics_on_kick_and_status_failure(fake, site, ledger, tmp_path, monkeypatch):
    local, directory = site
    plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {'build': 'b'} for i in range(64)}}
    payload_tar = tmp_path / 'session-0.tar.gz'
    archive(payload_tar, {'plan.json': plan})
    expected = guest.results_limits(plan)
    inputs = {'payload': payload_tar}
    clock = [10_000]
    for mod in (azure, channel):
        monkeypatch.setattr(mod.time, 'time', lambda: clock[0])
    monkeypatch.setattr(channel.time, 'sleep', lambda _s: None)
    (tmp_path / 'proto').mkdir()
    d = make_draw(Mock(subscription=SID), tmp_path / 'proto', start=clock[0], lifetime=6436)
    base_url = d.path('Compute', 'virtualMachines') + '/runCommands/'
    body = channel.control_body(d.region, '# harmless')
    d.put(base_url + 'init', channel.VM_API, body, work=True)
    d.put(base_url + 'kick', channel.VM_API, body, work=True)
    with pytest.raises(guard.GuardFailure, match='completion'):
        d.put(base_url + 'push-1', channel.VM_API, body, work=True)
    d.worker_failed = True
    d.put(base_url + 'push-1', channel.VM_API, body, work=True)
    assert d.push_started is True
    with pytest.raises(guard.GuardFailure, match='read-only'):
        d.put(base_url + 'status-00', channel.VM_API, channel.control_body(d.region, channel.STATUS_SCRIPT), work=True)
    for attr, fn in (
        ('storage_key', lambda _d: 'CANARY' * 8),
        ('signed_blob', lambda *_a: 'CANARY' * 8),
        ('seal', lambda *_a: 'ciphertext'),
    ):
        monkeypatch.setattr(channel, attr, fn)
    orig_copy = channel.copy_results

    def run_case(kind, *, copy_raises=False, late=False):
        case_dir = tmp_path / f'case-{kind}-{copy_raises}-{late}'
        ev = case_dir / 'ev'
        (ev / '.guard').mkdir(parents=True)
        (case_dir / 'draw').mkdir(parents=True)
        clock[0] = 10_000
        az_case = Mock(subscription=SID)
        draw = make_draw(az_case, case_dir / 'draw', start=clock[0], lifetime=1256)
        draw.account = 'chry' + '0' * 16
        calls = []
        if late:
            monkeypatch.setattr(channel, 'copy_results', orig_copy)
        else:
            def fake_copy(dr, evidence, _p, _b, *, limits=None):
                calls.append((limits, getattr(dr, 'worker_failed', False)))
                dr.put(dr.path('Compute', 'virtualMachines') + '/runCommands/push-1', channel.VM_API, body, work=True)
                if copy_raises:
                    raise RuntimeError('copy-boom')
                archive(evidence / 'results.tar.gz', {
                    'results/recovery-diagnostic.json': {
                        'diagnosticOnly': True, 'reason': 'not-started', 'stage': 'download',
                        'logsIncluded': 1, 'logsTruncated': 0, 'logsSkipped': 0, 'linesRedacted': 0,
                    },
                })
                return True
            monkeypatch.setattr(channel, 'copy_results', fake_copy)

        def fake_rest(verb, url, **kw):
            if verb == 'put':
                return {}
            if '/runCommands/init?' in url:
                return {'state': 'Succeeded', 'code': 0, 'output': 'CHRYSALIS:{"payload":"PUB1","results":"PUB2"}\n'}
            if '/runCommands/kick?' in url:
                if kind == 'kick':
                    if late:
                        clock[0] = draw.work_end + guard.COPY_END - channel.PUSH_SECONDS + 1
                    err_line = 'CHRYSALIS:{"error":"guest control step failed","step":"fetch","type":"ValueError"}\n'
                    return {'state': 'Succeeded', 'code': 75, 'output': err_line}
                return {'state': 'Succeeded', 'code': 0, 'output': f'CHRYSALIS:{{"started":{clock[0] + 5}}}\n'}
            if '/runCommands/status-00?' in url:
                if kind == 'status':
                    if late:
                        clock[0] = draw.work_end + guard.COPY_END - channel.PUSH_SECONDS + 1
                    err_line = 'CHRYSALIS:{"error":"guest control step failed","step":"compact","type":"RuntimeError"}\n'
                    return {'state': 'Failed', 'code': 75, 'output': err_line}
                return {'state': 'Succeeded', 'code': 0, 'output': 'CHRYSALIS:{"done":true}\n'}
            raise AssertionError(f'unexpected rest {verb} {url}')

        az_case.rest.side_effect = fake_rest
        if kind == 'ok':
            assert channel.run_channel(draw, inputs, ev) is True
            return calls, ev, draw
        label = 'kick' if kind == 'kick' else 'status-00'
        with pytest.raises(guard.GuardFailure, match=f'Run Command {label} did not succeed'):
            channel.run_channel(draw, inputs, ev)
        return calls, ev, draw

    assert run_case('ok')[0] == [(expected, False)]
    ck, ek, dk = run_case('kick')
    assert ck == [(expected, True)]
    assert dk.guest_error == {'command': 'kick', 'step': 'fetch', 'type': 'ValueError'}
    assert not (ek / 'guest-error.json').exists()
    cs, es, ds = run_case('status')
    assert cs == [(expected, True)]
    assert ds.guest_error == {'command': 'status-00', 'step': 'compact', 'type': 'RuntimeError'}
    assert not (es / 'guest-error.json').exists()
    assert run_case('kick', copy_raises=True)[0] == [(expected, True)]
    assert 'push-1' not in run_case('kick', late=True)[2].commands
    monkeypatch.setattr(azure.time, 'time', time.time)
    prepare_attempt(monkeypatch, directory)
    fake_state(fake, deletesBeforeAbsent=1)

    def failing_channel(draw, _inputs, note):
        archive(note / 'results.tar.gz', {
            'results/recovery-diagnostic.json': {
                'diagnosticOnly': True, 'reason': 'not-started', 'stage': 'digest',
                'logsIncluded': 1, 'logsTruncated': 0, 'logsSkipped': 0, 'linesRedacted': 0,
            },
            'results/session.log': b'DIAG_LOG_BODY_MUST_NOT_LEAK_TO_SUMMARY\n',
        })
        draw.guest_error = {'command': 'kick', 'step': 'fetch', 'type': 'ValueError'}
        raise guard.GuardFailure('guest Run Command kick did not succeed')

    monkeypatch.setattr(azure, 'run_channel', failing_channel)
    assert attempt(fake, local, ledger, directory, RUN) == 2
    assert ledger.attempts[RUN]['state'] == 'settled' and ledger.attempts[RUN]['exitCode'] == 2
    run_note = directory / 'time/runs' / RUN
    dj = json.loads((run_note / 'diagnostic-only.json').read_text())
    assert dj['partial'] is True and dj['reason'] == 'not-started'
    assert dj['stage'] == 'digest' and dj['logsIncluded'] == 1
    assert 'DIAG_LOG_BODY_MUST_NOT_LEAK_TO_SUMMARY' not in json.dumps(dj)
    assert json.loads((run_note / 'guest-error.json').read_text()) == {
        'command': 'kick', 'step': 'fetch', 'type': 'ValueError',
    }


def test_az2f4_fix3_push_always_returns_archive_and_caps_logs(tmp_path, monkeypatch):
    import zlib
    store = tmp_path / 'store'
    work = tmp_path / 'work'
    store.mkdir()
    (work / 'results').mkdir(parents=True)
    dump(work / 'results/session-status.json', {'completedAllDeclaredRounds': False, 'exitCode': 2})
    (work / 'session.log').write_text('worker-line\n')

    def fail_compact():
        raise subprocess.CalledProcessError(1, 'systemctl')

    _patch_guest_push(monkeypatch, store, work, done=fail_compact)
    monkeypatch.setattr(guest.time, 'sleep', lambda _s: None)
    (store / 'results-limits.json').write_text('{bad-json')
    for i in range(80):
        d = work / 'results' / f'{i:016x}'
        d.mkdir(parents=True)
        (d / 'server.log').write_bytes(b'')
    assert guest.push('fixture', time.time() + 130, True)['partial'] is True
    with tarfile.open(store / 'results.tar.gz') as stream:
        diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
    assert diag['reason'] == 'not-done' and diag['limitsFallback'] is True
    assert (diag['logsIncluded'], diag['logsSkipped']) == (65, 16)
    assert azure._diagnostic_summary(store / 'results.tar.gz').get('limitsFallback') is True
    orig_check = guest.check_results

    def flaky(fail_n, exc=None):
        count = [0]
        def _inner(p, *, limits=None):
            count[0] += 1
            if count[0] <= fail_n:
                raise exc or guest.ResultsRefused('results-too-large', 'forced')
            return orig_check(p, limits=limits)
        return _inner

    monkeypatch.setattr(guest, 'check_results', flaky(1))
    assert guest.archive_results(tmp_path / 'nologs.tar.gz', True, reason='not-done') is True
    with tarfile.open(tmp_path / 'nologs.tar.gz') as stream:
        assert sorted(stream.getnames()) == ['results/recovery-diagnostic.json', 'results/session-status.json']
    monkeypatch.setattr(guest, 'check_results', flaky(2))
    assert guest.archive_results(tmp_path / 'markeronly.tar.gz', True, reason='not-done') is True
    with tarfile.open(tmp_path / 'markeronly.tar.gz') as stream:
        assert stream.getnames() == ['results/recovery-diagnostic.json']
        assert json.load(stream.extractfile('results/recovery-diagnostic.json'))['reason'] == 'archive-error'
    for err in (EOFError('eof'), zlib.error('corrupt')):
        monkeypatch.setattr(guest, 'check_results', flaky(1, err))
        out = tmp_path / f'z-{type(err).__name__}.tar.gz'
        assert guest.archive_results(out, False) is True
        with tarfile.open(out) as stream:
            assert json.load(stream.extractfile('results/recovery-diagnostic.json'))['reason'] == 'archive-error'


def test_az2f4_fix3_whole_line_and_pem_block_redaction(tmp_path, monkeypatch):
    work = tmp_path / 'work'
    (work / 'results/0000000000000001').mkdir(parents=True)
    tail = b'ig=SECRET_CANARY_TOKEN_1234\nwhole-tail-line\n'
    pad = 256 * 1024 - len(tail)
    filler = (b'0123456789\n' * (pad // 11 + 1))[:pad]
    (work / 'session.log').write_bytes(b'head\n?s' + tail + filler)
    (work / 'results/0000000000000001/server.log').write_text(
        'before\n'
        '-----BEGIN RSA PRIVATE KEY-----\n'
        'MIIE_SECRET_BODY_1\n'
        'MIIE_SECRET_BODY_2\n'
        '-----END RSA PRIVATE KEY-----\n'
        'after-ok\n'
        '-----BEGIN PRIVATE KEY-----\n'
        'EOF_SECRET_BODY\n'
    )
    monkeypatch.setattr(guest, 'WORK', work)
    dest = tmp_path / 'redacted3.tar.gz'
    assert guest.archive_results(dest, True, reason='no-status') is True
    assert guest.check_results(dest) is True
    with tarfile.open(dest) as stream:
        diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
        slog = stream.extractfile('results/session.log').read().decode()
        srv = stream.extractfile('results/0000000000000001/server.log').read().decode()
    assert 'SECRET_CANARY_TOKEN_1234' not in slog and 'whole-tail-line' in slog
    assert not any(k in srv for k in ('MIIE_SECRET_BODY_1', 'MIIE_SECRET_BODY_2', 'EOF_SECRET_BODY'))
    expected_srv = (
        'before\n'
        + '[redacted: credential-shaped line]\n' * 4
        + 'after-ok\n'
        + '[redacted: credential-shaped line]\n' * 2
    )
    assert srv == expected_srv and diag['linesRedacted'] == 6


def test_az2f4_fix3_unit_loadstate_and_killed_fetch_stage(tmp_path, monkeypatch):
    store = tmp_path / 'store'
    work = tmp_path / 'work'
    store.mkdir()
    work.mkdir()
    _patch_guest_push(monkeypatch, store, work)
    monkeypatch.setattr(guest, 'PAYLOAD_KEY', store / 'payload.pem')
    not_found = b'LoadState=not-found\nResult=success\nExecMainCode=0\nExecMainStatus=0\n'
    monkeypatch.setattr(guest.subprocess, 'check_output', lambda *a, **kw: not_found)
    assert guest._unit_status() == {}
    shuffled = b'ExecMainStatus=15\nResult=timeout\nLoadState=loaded\nExecMainCode=2\n'
    monkeypatch.setattr(guest.subprocess, 'check_output', lambda *a, **kw: shuffled)
    assert guest._unit_status() == {'unitResult': 'timeout', 'execMainCode': 2, 'execMainStatus': 15}
    monkeypatch.setattr(guest, 'WORK', tmp_path / 'work2')

    def kill_download(*_a, **_kw):
        raise SystemExit(137)

    monkeypatch.setattr(guest, 'download', kill_download)
    with pytest.raises(SystemExit):
        guest.fetch('chry' + '0' * 16, '0' * 64, '-', 1000, 1300, 1900, 'eastus2')
    assert json.loads((store / 'fetch-failure.json').read_text()) == {'reason': 'not-started', 'stage': 'other'}
    assert guest.push('chry' + '0' * 16, 1900, True)['partial'] is True
    with tarfile.open(store / 'results.tar.gz') as stream:
        diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
    assert diag['reason'] == 'not-started' and diag['stage'] == 'other'


def test_az2f4_fix3_diagnostic_summary_and_modes(fake, site, ledger, tmp_path, monkeypatch):
    local, directory = site
    real_res = tmp_path / 'real-res'
    real_res.mkdir()
    dump(real_res / 'session-status.json', {'exitCode': 0})
    work = tmp_path / 'work'
    work.mkdir()
    (work / 'results').symlink_to(real_res)
    monkeypatch.setattr(guest, 'WORK', work)
    assert guest._clean_status_bytes() is None
    _, seal = sealed_site(site)
    plan = seal['timePlans'][0]
    prepare_attempt(monkeypatch, directory)
    fake_state(fake, deletesBeforeAbsent=1)
    full_src = write_azure_result(tmp_path / 'full-src', plan, 'keep')

    def failing_with_full(draw, _inputs, note):
        with tarfile.open(full_src) as stream:
            parts = {m.name: json.load(stream.extractfile(m)) for m in stream}
        parts['results/session-status.json'].update(reason='RAW_EXCEPTION_/opt/chrysalis/secret', probe='kept')
        archive(note / 'results.tar.gz', parts)
        (note / 'results.sha256').write_text(bundle.sha(note / 'results.tar.gz') + '\n')
        draw.guest_error = {'command': 'status-00', 'step': 'compact', 'type': 'RuntimeError'}
        raise guard.GuardFailure('guest Run Command status-00 did not succeed')

    monkeypatch.setattr(azure, 'run_channel', failing_with_full)
    assert attempt(fake, local, ledger, directory, RUN) == 2
    run_note = directory / 'time/runs' / RUN
    diag = json.loads((run_note / 'diagnostic-only.json').read_text())
    assert diag['partial'] is False
    assert diag['sessionStatus'] == {'completedAllDeclaredRounds': True, 'exitCode': 0, 'probe': 'kept'}
    assert 'RAW_EXCEPTION' not in json.dumps(diag)
    assert (run_note / 'guest-error.json').stat().st_mode & 0o777 == 0o600
    assert bundle.verified_machine(run_note, plan, 0)['invalid'] is not None


def test_az2f4_fix4_tail_inside_pem_body_redacted_through_end(tmp_path, monkeypatch):
    work = tmp_path / 'work'
    (work / 'results/0000000000000001').mkdir(parents=True)
    # Build a >16 KiB per-process log whose 16 KiB tail starts inside a PEM key body
    # (the BEGIN line is before the cut; two body lines and the END line are inside the tail).
    tail_body = (
        b'BODY_CUT_PARTIAL_LINE_012345678901234567890123456789\n'
        b'MIIE_TAIL_KEY_BODY_1\n'
        b'MIIE_TAIL_KEY_BODY_2\n'
        b'-----END RSA PRIVATE KEY-----\n'
        b'normal-after-key\n'
    )
    pad = 16 * 1024 - len(tail_body)
    filler = (b'0123456789\n' * (pad // 11 + 1))[:pad]
    (work / 'results/0000000000000001/server.log').write_bytes(
        b'before\n-----BEGIN RSA PRIVATE KEY-----\n' + tail_body + filler
    )
    monkeypatch.setattr(guest, 'WORK', work)
    dest = tmp_path / 'pem-tail.tar.gz'
    assert guest.archive_results(dest, True, reason='no-status') is True
    assert guest.check_results(dest) is True
    with tarfile.open(dest) as stream:
        diag = json.load(stream.extractfile('results/recovery-diagnostic.json'))
        srv = stream.extractfile('results/0000000000000001/server.log').read().decode()
    assert 'MIIE_TAIL_KEY_BODY_1' not in srv
    assert 'MIIE_TAIL_KEY_BODY_2' not in srv
    assert 'END RSA PRIVATE KEY' not in srv
    assert srv.startswith('[redacted: credential-shaped line]\n' * 3 + 'normal-after-key\n')
    assert diag['linesRedacted'] == 3
