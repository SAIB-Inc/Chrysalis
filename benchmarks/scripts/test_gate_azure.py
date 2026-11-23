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
import signal
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


def _patch_clock(monkeypatch, mods, clock, *, sleeps=None):
    from types import SimpleNamespace
    def _sleep(n):
        s = max(0.0, n)
        if sleeps is not None:
            sleeps.append(s)
        clock[0] += s
    ns = SimpleNamespace(time=lambda: clock[0], sleep=_sleep, monotonic=time.monotonic,
                         strftime=time.strftime, gmtime=time.gmtime)
    for mod in mods:
        monkeypatch.setattr(mod, 'time', ns)
    return ns


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


def make_draw(fake, tmp_path, *, group=RUN, region='eastus2', start=None, lifetime=1256, raw_seconds=None):
    state = tmp_path / '.guard'
    state.mkdir(exist_ok=True)
    guard.mark(state, 'ready')
    raw = max(0, round((lifetime - 345 - guard.WORKER_START) / 1.15)) if raw_seconds is None else raw_seconds
    return azure.Draw(fake, group, region, int(time.time()) if start is None else start, state, lifetime, lifetime - 345, raw_seconds=raw)


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
    for verb in ('put', 'delete'):
        with pytest.raises(proof.AzureError, match='ARM mutation'):
            fake.rest(verb, SUB + '/resourceGroups/' + RUN + '?api-version=2021-04-01', waits=proof.ONCE)
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
    _patch_clock(monkeypatch, (azure, guard, proof), now)
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
    g, raw = 6436, 4875
    c = g - 345
    assert e < e + guard.provision_seconds(c, raw) < e + guard.provision_seconds(c, raw) + guard.START_WINDOW <= e + guard.latest_worker_start_seconds(c, raw) < e + c < e + c + guard.COPY_END < e + g - guard.WATCH_MARGIN < e + g < e + g + guard.ALERT_TAIL < e + g + guard.RESERVE_TAIL
    assert e < e + guard.WORKER_START < e + c
    assert azure.shutdown_time(e + g) == '0118'
    assert azure.shutdown_time(e + g + 44) == '0118'
    for minutes in range(1440):
        start = e + minutes * 60 + 13
        for lifetime, raw_s in ((828, 0), (1256, 372), (2494, 1448), (6109, 4592), (7200, 5540)):
            h, m = azure.shutdown_time(start + lifetime)[:2], azure.shutdown_time(start + lifetime)[2:]
            latest_sched = guard.latest_worker_start_seconds(lifetime - 345, raw_s)
            for offset in (720, latest_sched):
                created = datetime.fromtimestamp(start + offset, timezone.utc)
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
        bundle.sha(directory / 'seal.json'), directory, dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372),
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
    t_first = datetime(2026, 11, 19, tzinfo=timezone.utc).timestamp()
    t_now = datetime(2026, 11, 21, tzinfo=timezone.utc).timestamp()
    trial_rows = [[.05, 20261119, RUN, 'USD'], [0., 20261119, 'baseline-group', 'USD'],
                  [.0052, 20261119, 'chrysalis-probe-0123456789abcdef-3', 'USD'],
                  [.0033, 20261119, 'chrysalis-probe-0123456789abcdef-4', 'USD']]
    fake_state(fake, cost=dict(response, rows=trial_rows))
    clean = proof.cost_readback(fake, 'baseline-group', attempts, t_first, t_now)
    assert not clean['stops'] and clean['otherRows'] == 1 and clean['trialRows'] == 2
    assert clean['trialUsd'] == pytest.approx(.0085) and 'chrysalis-probe-' not in json.dumps(clean)
    fake_state(fake, cost=dict(response, rows=[[.98, 20261119, 'chrysalis-probe-0123456789abcdef-1', 'USD']]))
    over = proof.cost_readback(fake, 'baseline-group', attempts, t_first, t_now)
    assert over['stops'] == ['charge outside run groups and exact baseline'] and over['trialRows'] == 1
    for row_data in ([.01, 20261121, 'chrysalis-probe-0123456789abcdef-1', 'USD'],
                     [.01, 20261119, 'chrysalis-gate-20990101t000000-cdcdcdcd', 'USD']):
        fake_state(fake, cost=dict(response, rows=[row_data]))
        stopped = proof.cost_readback(fake, 'baseline-group', attempts, t_first, t_now)
        assert stopped['stops'] == ['charge outside run groups and exact baseline'] and stopped['trialRows'] == 0


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
    from types import SimpleNamespace
    waits = []  # only the readback's own waits; subprocess's polling keeps the real time.sleep
    monkeypatch.setattr(proof, 'time', SimpleNamespace(sleep=waits.append))
    fake_state(fake, costThrottled=3)
    with pytest.raises(azure.Refusal, match='throttled.*no posted cost or cost stop written'):
        azure.daily_cost(directory, local)
    throttled = azure.Ledger(local)
    assert waits == [60, 120] and not (directory / 'cost-d1.json').exists()
    assert throttled.data['lastDailyCheck'] == now + 86400 and not throttled.data.get('costStops')
    assert all('posted' not in a for a in throttled.attempts.values())
    waits.clear()
    fake_state(fake, costThrottled=1)
    assert azure.daily_cost(directory, local) == 0
    assert waits == [60]
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
    assert epoch < epoch+guard.provision_seconds(c, 372) < epoch+guard.WORKER_START < epoch+guard.latest_worker_start_seconds(c, 372) < epoch+c < epoch+c+guard.COPY_END < epoch+g-70 < epoch+g < epoch+g+300
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
    monkeypatch.setattr(guest.subprocess, 'check_output', lambda *a, **kw: b'LoadState=loaded\nResult=success\nExecMainCode=1\nExecMainStatus=0\n')
    (work / 'suite/run/execution').mkdir(parents=True)
    (work / 'suite/run/execution/console.log').write_text('suite log\n')  # not a process log on either archive path
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
    assert guest.MAX_RESULTS_COMPRESSED == 12_000_000
    limits = guest.results_limits(s0_plan, strict=True)
    assert limits == (12_000_000, 90_000_000)
    assert guest.archive_results(green, False, limits=limits) is False
    assert guest.check_results(green, limits=limits) is False
    assert green.stat().st_size < guest.MAX_RESULTS_COMPRESSED
    with tarfile.open(green) as stream:
        summary = json.load(stream.extractfile('results/guest-summary.json'))
        expanded = sum(item.size for item in stream.getmembers() if item.name != 'results/guest-summary.json')
    assert summary == {'unitResult': 'success', 'execMainCode': 1, 'execMainStatus': 0, 'logsIncluded': 113, 'logsTruncated': 0,
                       'logsSkipped': 0, 'linesRedacted': 0, 'expandedBytes': expanded, 'maxCompressedBytes': limits[0], 'maxExpandedBytes': limits[1]}
    # GREEN archive bytes match ec97c99a's non-partial packing rule, then the guest summary, byte-for-byte.
    with tarfile.open(ref, 'w:gz') as stream:
        for folder in ('results', 'prelude', 'bytes', 'suite'):
            if (work / folder).exists():
                for member in sorted((work / folder).rglob('*')):
                    if not member.is_dir():
                        stream.add(member, arcname=member.relative_to(work).as_posix(), recursive=False)
        stream.add(work / 'session.log', arcname='results/session.log', recursive=False)
        entry = tarfile.TarInfo('results/guest-summary.json')
        entry.size = len(data := (json.dumps(summary, separators=(',', ':')) + '\n').encode())
        stream.addfile(entry, io.BytesIO(data))
    assert gzip.decompress(green.read_bytes()) == gzip.decompress(ref.read_bytes())
    # The caught worker exit (run 3's outcome) packs the same summary and log; a worker-written summary is refused.
    dump(work / 'results/session-status.json', {'completedAllDeclaredRounds': False, 'exitCode': 2, 'reason': 'Prelude process exceeds sealed charge'})
    monkeypatch.setattr(guest, '_read_saved_limits', lambda: (limits, True))
    assert guest.archive_results(green, False, limits=limits) is False
    with tarfile.open(green) as stream:
        assert stream.extractfile('results/session.log').read() == b'ok\n'
        again = json.load(stream.extractfile('results/guest-summary.json'))
    assert again == dict(summary, expandedBytes=again['expandedBytes'], limitsFallback=True)
    dump(work / 'results/guest-summary.json', summary)
    assert guest.archive_results(green, False, limits=limits) is True
    with tarfile.open(green) as stream:
        assert json.load(stream.extractfile('results/recovery-diagnostic.json'))['reason'] == 'forbidden-member'
    s1_plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(192)}}
    max_plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(259)}}
    assert guest.results_limits(s1_plan, strict=True) == (12_000_000, 218_000_000)
    assert guest.results_limits(max_plan, strict=True) == (12_000_000, 285_000_000)
    for over in (260, 480, 1000):
        over_plan = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(over)}}
        with pytest.raises(ValueError, match='hard ceiling'):
            guest.results_limits(over_plan, strict=True)
    runaway = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(1000)}}
    assert guest.results_limits(runaway) == (guest.MAX_RESULTS_COMPRESSED, guest.MAX_RESULTS_EXPANDED)


def test_rehearsal_checks_rejects_run3_and_accepts_mr1_archives(tmp_path, monkeypatch):
    import gate_prelude as prelude, gate_prepare as prepare
    from test_gate_az2 import rehearsal_fixture
    base, members, seed_plan = rehearsal_fixture(tmp_path / 'fix')
    plans = prepare.rehearsal_plans(seed_plan)
    run3, run3_tar, run3_diag = tmp_path / 'run3', tmp_path / 'run3.tar.gz', tmp_path / 'run3-diag.json'
    _build_noise_s0_tree(run3)
    (run3 / 'session.log').write_text('')
    dump(run3 / 'results/session-status.json', {'completedAllDeclaredRounds': False, 'exitCode': 2, 'reason': prelude.CHARGE_EXCEEDED, 'probe': 'kept'})
    dump(run3_diag, {'diagnosticOnly': True, 'verdict': 'INCONCLUSIVE'})
    with tarfile.open(run3_tar, 'w:gz') as stream:
        for arc, src in (('results/session-status.json', run3 / 'results/session-status.json'), ('results/session.log', run3 / 'session.log')):
            stream.add(src, arcname=arc, recursive=False)
    bad_info = prepare.rehearsal_checks('fail', run3_tar, plans['fail'], dict(exitCode=2, partial=False, archiveSha256=bundle.sha(run3_tar),
                                        importerCode=None, importerError='DIAGNOSTIC ONLY / INCONCLUSIVE', diagnosticPath=run3_diag))
    assert {k for k, v in bad_info['checks'].items() if not v} == {'log_nonempty', 'log_start', 'log_probe', 'log_prelude', 'log_end', 'log_stopped', 'summary_fields'}
    outcomes = {c: prepare.rehearse_case(c, base, members, plans[c], tmp_path / f'run-{c}') for c in ('fail', 'pass')}
    for c, out in outcomes.items():
        checks = prepare.rehearsal_checks(c, out['archive'], plans[c], out)['checks']
        assert all(checks.values()), (c, {k: v for k, v in checks.items() if not v})
    assert bad_info['worker'] == 'exit:2,stage:none,marker:none,error:none'
    run4_work, run4_temp, run4_tar = tmp_path / 'run4-work', tmp_path / 'run4-temp', tmp_path / 'run4.tar.gz'
    for d in (run4_work, run4_temp):
        d.mkdir()
    (run4_work / 'session.log').write_text("python3.12: /lib/x86_64-linux-gnu/libc.so.6: version `GLIBC_2.38' not found\n")
    with monkeypatch.context() as mp:
        mp.setattr(guest, 'WORK', run4_work)
        mp.setattr(guest, 'TEMP', run4_temp)
        assert guest.archive_results(run4_tar, False) is True
    run4_info = prepare.rehearsal_checks('fail', run4_tar, plans['fail'], dict(exitCode=127, partial=True, archiveSha256=bundle.sha(run4_tar)))
    assert run4_info['artefacts'] == ['sha256', 'log'] and run4_info['worker'] == 'exit:127,stage:none,marker:no-status,error:none'
    for idx, (reason, log_line) in enumerate((('custom-unlisted', 'loader failed\n'), ('caf\xe9-reason', 'Caf\xe9Error: secret\n'))):
        arc = tmp_path / f'bad-worker-{idx}.tar.gz'
        with tarfile.open(arc, 'w:gz') as stream:
            for name, raw in (('results/recovery-diagnostic.json', json.dumps({'diagnosticOnly': True, 'reason': reason}).encode('utf-8')),
                              ('results/session.log', log_line.encode('utf-8'))):
                ti = tarfile.TarInfo(name)
                ti.size = len(raw)
                stream.addfile(ti, io.BytesIO(raw))
        info = prepare.rehearsal_checks('fail', arc, plans['fail'], dict(exitCode=127, partial=True, archiveSha256=bundle.sha(arc)))
        assert info['worker'] == 'exit:127,stage:none,marker:none,error:none', (reason, info['worker'])
    def check_mut(idx, case, mut=None, drop=None, **ov):
        arc = outcomes[case]['archive']
        if mut or drop:
            arc = tmp_path / f'mut-{idx}.tar.gz'
            with tarfile.open(outcomes[case]['archive'], 'r:gz') as zin, tarfile.open(arc, 'w:gz') as zout:
                for item in (it for it in zin if it.name != drop):
                    raw = zin.extractfile(item).read()
                    if mut and item.name == mut[0]:
                        obj = json.loads(raw)
                        mut[1](obj)
                        raw = (json.dumps(obj, separators=(',', ':')) + '\n').encode('ascii')
                    ti = tarfile.TarInfo(item.name)
                    ti.size = len(raw)
                    zout.addfile(ti, io.BytesIO(raw))
        return prepare.rehearsal_checks(case, arc, plans[case], {**outcomes[case], 'archive': arc, 'archiveSha256': bundle.sha(arc), **ov})['checks']
    cal_arc = next(n for n in tarfile.open(outcomes['pass']['archive']).getnames() if n.endswith('-calibration.json'))
    for i, (case, mut, drop, ov, expect) in enumerate((
        ('fail', None, None, {'partial': True}, 'archive_full'), ('fail', None, None, {'archiveSha256': '0' * 64}, 'archive_full'),
        ('fail', None, 'results/session-status.json', {}, 'status_present'),
        ('fail', ('results/session-status.json', lambda d: d.update(probe='rejected')), None, {}, 'probe_kept'),
        ('fail', ('results/session-status.json', lambda d: d.update(exitCode=1)), None, {}, 'exit_code'),
        ('fail', None, None, {'exitCode': 1}, 'exit_code'),
        ('fail', ('results/session-status.json', lambda d: d.update(reason='other')), None, {}, 'fail_reason'),
        ('fail', None, None, {'diagnosticPath': None}, 'importer_inconclusive'),
        ('pass', ('results/session-status.json', lambda d: d.update(completedAllDeclaredRounds=False)), None, {}, 'completed'),
        ('pass', ('prelude/applied.json', lambda d: d.clear()), None, {}, 'applied_rows'),
        ('pass', ('results/members.json', lambda d: d.pop()), None, {}, 'members_count'),
        ('pass', None, None, {'importerCode': 5}, 'importer_ok'),
        ('pass', ('results/members.json', lambda d: d[0]['data'].update(invocationsPerPass=d[0]['data']['invocationsPerPass'] + 1)), None, {}, 'prelude_population'),
        ('pass', (cal_arc, lambda d: d['data']['rows'][0].update(nsPerBlock=d['data']['rows'][0]['nsPerBlock'] * 10)), None, {}, 'prelude_read'),
    )):
        assert not check_mut(i, case, mut, drop, **ov)[expect], (case, expect)


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
    assert summary['partial'] is True and summary['reason'] == 'not-done' and summary.items() >= zero.items()
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
    assert expected == (12_000_000, 90_000_000)
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

    seen_lims = []
    def fake_dl(_a, _b, _s, target, lim, _d):
        seen_lims.append(lim)
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
    assert seen_lims == [expected[0], guest.MAX_RESULTS]


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
            monkeypatch.setattr(guest, 'plan_results_limits', lambda _a: (12_000_000, 90_000_000))
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
    assert guest._saved_limits() == guest.results_limits(plan) == (12_000_000, 90_000_000)
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
            if '/runCommands/status-' in url:
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
        label = 'kick' if kind == 'kick' else 'status-02'
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
    assert ds.guest_error == {'command': 'status-02', 'step': 'compact', 'type': 'RuntimeError'}
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


def slow_once(monkeypatch, site, answers_after, landed=False, *, times=1):
    real = proof.subprocess.run
    method, needle = site
    hits = []
    def wrapped(cmd, *args, **kwargs):
        if (len(hits) < times and 'rest' in cmd and '--method' in cmd and '--url' in cmd
                and cmd[cmd.index('--method') + 1] == method and needle in cmd[cmd.index('--url') + 1]):
            hits.append(kwargs.get('timeout', 15))
            if kwargs.get('timeout', 15) < answers_after:
                if landed:
                    real(cmd, *args, **kwargs)
                raise subprocess.TimeoutExpired(cmd, kwargs.get('timeout', 15))
        return real(cmd, *args, **kwargs)
    monkeypatch.setattr(proof.subprocess, 'run', wrapped)
    return hits


@pytest.mark.parametrize('site', [('get', '/virtualMachines/gate?'), ('put', '/virtualMachines/gate?')])
def test_setup_survives_one_slow_call(fake, tmp_path, monkeypatch, capsys, site):
    _patch_clock(monkeypatch, (proof,), [time.time()])
    draw = make_draw(fake, tmp_path)
    monkeypatch.setattr(azure, 'provision_public_key', lambda: 'test-public-key')
    monkeypatch.setattr(azure, 'upload_inputs', lambda _draw, _inputs: None)
    hits = slow_once(monkeypatch, site, 20, landed=site[0] == 'put')
    draw.setup({})
    puts = [detail for verb, detail in events(fake) if verb == 'put']
    assert puts.count(['Microsoft.Compute', 'virtualMachines']) == 1 and len(hits) == 1
    out = capsys.readouterr().out
    assert ('Azure call get virtualMachines timed out' in out) == (site[0] == 'get')


@pytest.mark.parametrize('landed', [True, False])
def test_init_submit_timeout_polls_without_resending(fake, tmp_path, monkeypatch, landed):
    spark = 10000
    now = [spark]
    _patch_clock(monkeypatch, (azure, guard, channel, proof), now)
    draw = make_draw(fake, tmp_path, start=spark)
    fake_state(fake, groups={draw.scope: dict(location='eastus2', tags={'chrysalisGate': RUN})},
               commandResults={'init': {'payload': 'k1', 'results': 'k2'}})
    hits = slow_once(monkeypatch, ('put', '/runCommands/init?'), 20, landed=landed)
    until = spark + 30
    if landed:
        assert channel.command(draw, 'init', '#!/bin/bash\n', until) == {'payload': 'k1', 'results': 'k2'}
        assert [e for e in events(fake) if e == ['put', ['RunCommand', 'init']]] == [['put', ['RunCommand', 'init']]]
    else:
        with pytest.raises(guard.GuardFailure, match='deadline'):
            channel.command(draw, 'init', '#!/bin/bash\n', until)
        assert now[0] >= until and not any(e == ['put', ['RunCommand', 'init']] for e in events(fake))
        draw.commands.add('kick')
        draw.worker_done = True
        now[0] = spark
        slow_once(monkeypatch, ('put', '/runCommands/push-1?'), 20, landed=False)
        with pytest.raises(proof.AzureError, match='timeout'):
            channel.command(draw, 'push-1', '#!/bin/bash\n', spark + 30)
        assert now[0] == spark and not any(e == ['get', ['RunCommand', 'push-1']] for e in events(fake))
    assert len(hits) == 1


def test_repeated_slow_read_ends_invalid_with_cleanup_and_labels(fake, site, ledger, monkeypatch, capsys):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    _patch_clock(monkeypatch, (proof,), [time.time()])
    fake_state(fake, deletesBeforeAbsent=1)
    hits = slow_once(monkeypatch, ('get', '/networkWatchers?'), 20, times=3)
    assert attempt(fake, local, ledger, directory, RUN) == 2
    assert len(hits) == 3 and ledger.attempts[RUN]['state'] == 'settled'
    assert json.loads((directory / 'time/runs' / RUN / 'cleanup-proof.json').read_text())['ourAppAbsent']
    assert not fake_state(fake)['groups']
    out = capsys.readouterr().out.splitlines()
    assert out.count('Azure call get networkWatchers timed out') == 3
    assert any(line.startswith('Azure attempt: timeout; last Azure call: get networkWatchers; step check networkWatchers; +') for line in out)


def _settle_pre_ready(directory, ledger, go, number, phase, draw, app, *, status=2, fallback=None, region='eastus2',
                      booked=.25, exit_code=None):
    note = directory / 'time/runs' / app
    gdir = note / '.guard'
    gdir.mkdir(parents=True, exist_ok=True)
    for name in ('group-put-sent', 'group-put-confirmed', 'done'):
        guard.mark(gdir, name)
    dump(note / 'machine.json', dict(provider='azure', platform='azure', machineClass=azure.SIZE, region=region))
    dump(note / 'cleanup-proof.json', dict(restored=True, ourAppAbsent=True, remainingRunRoleAssignments=0))
    dump(note / 'duration-cost.json', dict(cleanupOutstanding=False, totalEstimateWithReserveUsd=booked, withinSealedGuard=True,
                                           lifetimeSeconds=100, guardSeconds=1256, cleanupStartedSeconds=50))
    dump(note / 'pre-ready-failure.json', {'class': 'CapacityUnavailable' if (exit_code or status) == 6 else 'GuardFailure',
                                           'step': 'put virtualMachines' if (exit_code or status) == 6 else 'run-command init',
                                           'seconds': 12.3})
    ledger.attempts[app] = dict(state='settled', go=go, session=number, draw=draw, phase=phase, region=region,
                                fallbackOf=fallback, epoch=1000, guard=1256, cutoff=911, reservation=.62,
                                lineage=None, directory=directory.name, booked=booked, expectedPosted=.15, ended=1100,
                                exitCode=status if exit_code is None else exit_code)
    ledger.write()
    return note


def _apply_fault(fault, d, ledger, app, fallback, g):
    note = d / 'time/runs' / app
    gdir = note / '.guard'
    status = 2
    if fault.startswith('g:'):
        (gdir / fault[2:]).write_text('x')
    elif fault == 'g-dangling':
        (gdir / 'guest-ready').symlink_to(d / 'missing-target')
    elif fault.startswith('n:'):
        (note / fault[2:]).write_text('x')
    elif fault == 'n-dangling':
        (note / 'cleanup-failed.json').symlink_to(d / 'missing-target')
    elif fault.startswith('miss:'):
        (gdir / fault[5:]).unlink()
    elif fault.startswith('sym:'):
        name = fault[4:]
        real = d / f'real-{name}'
        (note / name).replace(real)
        (note / name).symlink_to(real)
    elif fault.startswith('proof:'):
        data = json.loads((note / 'cleanup-proof.json').read_text())
        key, val = {'absent': ('ourAppAbsent', False), 'restored': ('restored', False),
                    'roles-1': ('remainingRunRoleAssignments', 1), 'roles-bool': ('remainingRunRoleAssignments', False)}[fault[6:]]
        data[key] = val
        dump(note / 'cleanup-proof.json', data)
    elif fault.startswith('cost:'):
        data = json.loads((note / 'duration-cost.json').read_text())
        sub = fault[5:]
        if sub == 'outstanding':
            data['cleanupOutstanding'] = True
        elif sub == 'over-guard':
            data['withinSealedGuard'] = False
        elif sub == 'mismatch':
            data['totalEstimateWithReserveUsd'] = .26
        elif sub == 'over-cap':
            data['totalEstimateWithReserveUsd'] = 9.99
            ledger.attempts[app]['booked'] = 9.99
            ledger.write()
        dump(note / 'duration-cost.json', data)
    elif fault.startswith('marker:'):
        sub = fault[7:]
        mp = note / 'pre-ready-failure.json'
        if sub == 'missing':
            mp.unlink()
        else:
            data = json.loads(mp.read_text())
            if sub == 'bad-class':
                data['class'] = 'bad class!'
            elif sub == 'bad-step':
                data['step'] = 'unknown'
            elif sub == 'bad-seconds':
                data['seconds'] = -1
            elif sub == 'extra-key':
                data['extra'] = 1
            dump(mp, data)
    elif fault == 'ledger:cost-stop':
        ledger.data['costStops'] = ['over']
        ledger.write()
    elif fault == 'ledger:cost-latch':
        ledger.cost_latch.write_text('{}')
    elif fault == 'ledger:other-active':
        other = 'chrysalis-gate-20990405t000099-00000099'
        ledger.attempts[other] = dict(ledger.attempts[app], go='0' * 64, state='active', draw=1)
        ledger.write()
    elif fault == 'ledger:self-active':
        ledger.attempts[app]['state'] = 'active'
        ledger.write()
    elif fault.startswith('ledger:'):
        field = fault[7:]
        bad = {'go': '0' * 64, 'session': 1, 'phase': 'repeat', 'draw': 2, 'directory': 'gate-other',
               'fallbackOf': 'chrysalis-gate-20990405t000098-00000098', 'exitCode': 5}[field]
        ledger.attempts[app][field] = bad
        ledger.write()
    elif fault.startswith('status:'):
        status = int(fault[7:])
    elif fault.startswith('fb:'):
        fb_note = d / 'time/runs' / fallback
        sub = fault[3:]
        if sub == 'bad-proof':
            dump(fb_note / 'cleanup-proof.json', dict(restored=True, ourAppAbsent=False, remainingRunRoleAssignments=0))
        elif sub == 'bad-exit':
            ledger.attempts[fallback]['exitCode'] = 2
            ledger.write()
        elif sub == 'missing-marker':
            (fb_note / 'pre-ready-failure.json').unlink()
    return status


def _run_fly(monkeypatch, site, ledger, outcomes, *, max_draws=6, seal_patch=None, tag='a'):
    local, base_dir = site
    directory = base_dir.parent / f'gate-redraw-{tag}'
    directory.mkdir(exist_ok=True)
    go, seal = sealed_site((local, directory))
    plan = seal['timePlans'][0]
    plan['probe']['maxDraws'] = max_draws
    plan['rows'][0]['noise'] = dict(band=.04, machine=azure.SIZE, design='long')
    if seal_patch:
        seal_patch(seal, plan)
    else:
        schedules.prepare(plan)
    go = gate.write_seal(directory, seal)
    monkeypatch.setattr(azure, 'reconcile_and_pre_go', lambda *_: None)
    calls, it = [], iter(outcomes)

    def fake_launch(host, d, g, number, phase, draw, pl, relative, first, ctx, disp, *, region=None):
        reg = region or pl['region']
        kind = next(it)
        app = f'chrysalis-gate-20990405t000{len(calls) + 1:03d}-' + f'{len(calls) + 1:08x}'
        calls.append((number, phase, draw, app, kind, reg))
        if first:
            dump(d / 'time/launch-started.json', dict(sealId=g))
        if kind in ('kept', 'rejected'):
            note = d / 'time/runs' / app
            write_azure_result(note, pl, 'cv-over-limit' if kind == 'rejected' else 'keep')
            m = json.loads((note / 'machine.json').read_text()); m['region'] = reg; dump(note / 'machine.json', m)
            dump(note / 'cleanup-proof.json', dict(restored=True, ourAppAbsent=True, remainingRunRoleAssignments=0))
            code = 5 if kind == 'rejected' else 0
            ledger.attempts[app] = dict(state='settled', go=g, session=number, draw=draw, phase=phase, region=reg,
                                        fallbackOf=None, epoch=1000, guard=pl['destroyGuardSeconds'], cutoff=pl['measurementCutoffSeconds'],
                                        reservation=.62, lineage=None, directory=d.name, booked=.25, expectedPosted=.15, ended=1100, exitCode=code)
            ledger.write()
            return code, app, None
        fallback = None
        if kind == 'eastus-cap':
            _settle_pre_ready(d, ledger, g, number, phase, draw, app, status=6, region=reg)
            return 6, app, None
        if kind == 'both-cap':
            fallback = f'chrysalis-gate-20990405t000{len(calls) + 90:03d}-' + f'{len(calls) + 90:08x}'
            _settle_pre_ready(d, ledger, g, number, phase, draw, fallback, status=6, region='eastus2')
            _settle_pre_ready(d, ledger, g, number, phase, draw, app, status=6, fallback=fallback, region='eastus')
            return 6, app, fallback
        if kind == 'fallback-pre-ready' or kind.startswith(('fb:', 'fb-step:')):
            fallback = f'chrysalis-gate-20990405t000{len(calls) + 90:03d}-' + f'{len(calls) + 90:08x}'
            _settle_pre_ready(d, ledger, g, number, phase, draw, fallback, status=6, region='eastus2')
            _settle_pre_ready(d, ledger, g, number, phase, draw, app, status=2, fallback=fallback, region='eastus')
        else:
            _settle_pre_ready(d, ledger, g, number, phase, draw, app, status=2, region=reg)
        if kind.startswith(('step:', 'fb-step:')):
            step_name = kind.split(':', 1)[1]
            dump(d / 'time/runs' / app / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': step_name, 'seconds': 695.0})
            return 2, app, fallback
        status = 2 if kind in ('pre-ready', 'fallback-pre-ready') else _apply_fault(kind, d, ledger, app, fallback, g)
        return status, app, fallback

    monkeypatch.setattr(bundle, 'launch_azure', fake_launch)
    (directory / 'time').mkdir(exist_ok=True)
    rc = bundle.fly_sessions(directory, seal, go, ('first', 'repeat'), [], context=('test', 'fixture'))
    return rc, directory, seal, go, calls, json.loads((directory / 'time/draws.json').read_text())


def test_redraw_one_pre_ready_then_kept_settles_before_second_reserve(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    go, seal = sealed_site(site)
    plan = seal['timePlans'][0]
    plan['rows'][0]['noise'] = dict(band=.04, machine=azure.SIZE, design='long')
    schedules.prepare(plan)
    go = gate.write_seal(directory, seal)
    monkeypatch.setattr(azure, 'reconcile_and_pre_go', lambda *_: None)
    timeline, real_write, seq = [], ledger.write, [0]

    def tracked_write():
        for name, entry in ledger.attempts.items():
            tag = ('reserve' if entry['state'] == 'active' else entry['state'], name)
            if tag not in timeline:
                timeline.append(tag)
        real_write()

    def run_ch(draw, inputs, note):
        seq[0] += 1
        if seq[0] == 1:
            draw.step = 'run-command init'
            raise guard.GuardFailure('no time left to submit guest Run Command')
        guard.mark(note / '.guard', 'guest-ready', since=draw.start)
        staged = directory / 'staged-kept'
        write_azure_result(staged, plan, 'keep')
        for f in ('results.tar.gz', 'results.sha256'):
            shutil.copy2(staged / f, note / f)
        return False

    def real_one(host, d, g, number, phase, draw_n, pl, relative, first, ctx, disp, **_kw):
        app = f'chrysalis-gate-20990405t000{draw_n:03d}-' + f'{draw_n:08x}'
        if first:
            monkeypatch.setenv('GATE_FIRST_GO', g)
        else:
            monkeypatch.delenv('GATE_FIRST_GO', raising=False)
        return azure.one_attempt(fake, local, ledger, seal, g, d, pl, app, pl['region'], {}, number, draw_n, phase, None, ctx, 0), app, None

    monkeypatch.setattr(ledger, 'write', tracked_write)
    monkeypatch.setattr(azure, 'run_channel', run_ch)
    monkeypatch.setattr(azure, 'probe_result', lambda *_: 0)
    monkeypatch.setattr(bundle, 'launch_azure', real_one)
    (directory / 'time').mkdir(exist_ok=True)
    rc = bundle.fly_sessions(directory, seal, go, ('first',), [], context=('test', 'fixture'))
    draws = json.loads((directory / 'time/draws.json').read_text())
    assert rc == 0 and [d['decision'] for d in draws] == ['invalid-pre-ready', 'kept']
    a1, a2 = draws[0]['app'], draws[1]['app']
    assert timeline == [('reserve', a1), ('settled', a1), ('reserve', a2), ('settled', a2)]
    assert ledger.attempts[a1]['exitCode'] == 2 and ledger.attempts[a2]['exitCode'] == 0
    assert ledger.attempts[a1]['booked'] > 0 and not json.loads((directory / 'time/0-first.json').read_text())['invalid']
    mf = directory / 'time/runs' / a1 / 'pre-ready-failure.json'
    assert mf.is_file() and (mf.stat().st_mode & 0o777) == 0o600
    assert json.loads(mf.read_text())['class'] == 'GuardFailure' and json.loads(mf.read_text())['step'] == 'run-command init'
    assert not (directory / 'time/runs' / a2 / 'pre-ready-failure.json').exists()


def test_sigterm_to_real_one_attempt_gets_no_redraw(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    go, seal = sealed_site(site)
    plan = seal['timePlans'][0]
    plan['rows'][0]['noise'] = dict(band=.04, machine=azure.SIZE, design='long')
    schedules.prepare(plan)
    go = gate.write_seal(directory, seal)
    monkeypatch.setattr(azure, 'reconcile_and_pre_go', lambda *_: None)

    def sigterm_in_channel(draw, _inputs, _note):
        os.kill(os.getpid(), signal.SIGTERM)

    monkeypatch.setattr(azure, 'run_channel', sigterm_in_channel)
    calls = []

    def real_one(host, d, g, number, phase, draw_n, pl, relative, first, ctx, disp, **_kw):
        app = f'chrysalis-gate-20990405t000{draw_n:03d}-' + f'{draw_n:08x}'
        calls.append(app)
        if first:
            monkeypatch.setenv('GATE_FIRST_GO', g)
        else:
            monkeypatch.delenv('GATE_FIRST_GO', raising=False)
        prev = signal.signal(signal.SIGTERM, lambda *_: sys.exit(2))
        try:
            code = azure.one_attempt(fake, local, ledger, seal, g, d, pl, app, pl['region'], {}, number, draw_n, phase, None, ctx, 0)
        except SystemExit as exc:
            code = exc.code
        finally:
            signal.signal(signal.SIGTERM, prev)
        return code, app, None

    monkeypatch.setattr(bundle, 'launch_azure', real_one)
    (directory / 'time').mkdir(exist_ok=True)
    rc = bundle.fly_sessions(directory, seal, go, ('first',), [], context=('test', 'fixture'))
    draws = json.loads((directory / 'time/draws.json').read_text())
    assert rc == 2 and len(calls) == 1 and [d['decision'] for d in draws] == ['invalid']
    assert ledger.attempts[calls[0]]['state'] == 'settled' and ledger.attempts[calls[0]]['exitCode'] == 2
    assert not (directory / 'time/runs' / calls[0] / 'pre-ready-failure.json').exists()


def test_redraw_three_pre_ready_redraws_allowed_fourth_stops_same_and_cross_phase(fake, site, ledger, monkeypatch):
    assert bundle.MAX_PRE_READY_REDRAWS == 3
    rc_ok, _, _, _, calls_ok, draws_ok = _run_fly(
        monkeypatch, site, ledger, ['fallback-pre-ready', 'pre-ready', 'pre-ready', 'kept'], tag='three-ok')
    assert rc_ok == 0 and len(calls_ok) == 4
    assert [d['decision'] for d in draws_ok] == ['invalid-pre-ready', 'invalid-pre-ready', 'invalid-pre-ready', 'kept']

    ledger.attempts.clear()
    ledger.write()
    rc, _, _, _, calls, draws = _run_fly(
        monkeypatch, site, ledger, ['fallback-pre-ready', 'pre-ready', 'pre-ready', 'pre-ready', 'kept'], tag='same')
    assert rc == 2 and len(calls) == 4
    assert [d['decision'] for d in draws] == ['invalid-pre-ready', 'invalid-pre-ready', 'invalid-pre-ready', 'invalid']

    def make_release(seal, plan):
        entry = dict(plan['rows'][0], aPairs=16, bPairs=16, expected=dict(release='FAIL', change='PASS'))
        rp = probe_plan(entry, 'release')
        rp.update(inputSha256=plan['inputSha256'], repeatAllowed=True)
        schedules.prepare(rp)
        seal.update(mode='release', rows=rp['rows'], timePlans=[rp])

    monkeypatch.setattr(bundle, 'make_repeat', lambda *_: None)
    monkeypatch.setattr(timing, 'session_outcome', lambda *a, **k: ('repeat', [a[1]['timePlans'][0]['rows'][0]['id']]))
    ledger.attempts.clear()
    ledger.write()
    rc2, _, _, _, calls2, draws2 = _run_fly(
        monkeypatch, site, ledger, ['pre-ready', 'pre-ready', 'kept', 'pre-ready', 'pre-ready', 'kept'],
        seal_patch=make_release, tag='cross')
    assert rc2 == 2 and len(calls2) == 5
    assert [d['decision'] for d in draws2] == ['invalid-pre-ready', 'invalid-pre-ready', 'kept', 'invalid-pre-ready', 'invalid']


@pytest.mark.parametrize('fault', [
    'g:guest-ready', 'g:failed', 'g:urgent', 'g:provisional-reported', 'g:escalated', 'g:alerts.log', 'g:watchdog-proof.json', 'g-dangling',
    'n:cleanup-failed.json', 'n:results.tar.gz', 'n:results.sha256', 'n:diagnostic-only.json', 'n:guest-error.json', 'n:session-invalid.json', 'n-dangling',
    'miss:group-put-sent', 'miss:group-put-confirmed', 'miss:done',
    'sym:cleanup-proof.json', 'sym:duration-cost.json', 'sym:pre-ready-failure.json',
    'proof:absent', 'proof:restored', 'proof:roles-1', 'proof:roles-bool',
    'cost:outstanding', 'cost:over-guard', 'cost:mismatch', 'cost:over-cap',
    'marker:missing', 'marker:bad-class', 'marker:bad-step', 'marker:bad-seconds', 'marker:extra-key',
    'ledger:cost-stop', 'ledger:cost-latch', 'ledger:other-active', 'ledger:self-active',
    'ledger:go', 'ledger:session', 'ledger:phase', 'ledger:draw', 'ledger:directory', 'ledger:fallbackOf', 'ledger:exitCode',
    'status:4', 'status:6',
    'fb:bad-proof', 'fb:bad-exit', 'fb:missing-marker',
])
def test_redraw_refused_after_guest_ready_or_any_doubt(fake, site, ledger, monkeypatch, fault):
    rc, _, _, _, calls, draws = _run_fly(monkeypatch, site, ledger, [fault, 'kept'], tag=re.sub(r'[^a-z0-9]+', '-', fault))
    assert rc == (4 if fault == 'status:4' else 2)
    assert len(calls) == 1 and [d['decision'] for d in draws] == ['invalid']


def test_redraw_and_probe_rejection_stay_inside_max_draws(fake, site, ledger, monkeypatch):
    rc, _, _, _, calls, draws = _run_fly(monkeypatch, site, ledger, ['rejected', 'pre-ready', 'kept'], max_draws=3, tag='fit')
    assert rc == 0 and len(calls) == 3 and [d['decision'] for d in draws] == ['rejected', 'invalid-pre-ready', 'kept']
    ledger.attempts.clear()
    ledger.write()
    rc2, _, _, _, calls2, draws2 = _run_fly(monkeypatch, site, ledger, ['rejected', 'rejected', 'pre-ready', 'kept'], max_draws=3, tag='cap')
    assert rc2 == 2 and len(calls2) == 3 and [d['decision'] for d in draws2] == ['rejected', 'rejected', 'invalid']


def test_failure_lines_carry_fixed_class_step_seconds_and_no_free_text(fake, site, ledger, monkeypatch, capsys):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    clock = [10_000.0]
    _patch_clock(monkeypatch, (azure, guard, channel, proof), clock)

    def fail_guard(draw, _inputs, _note):
        draw.step = 'run-command init'
        clock[0] += 52.5
        raise guard.GuardFailure('CANARY_SECRET_GUARD 10.1.2.3')

    monkeypatch.setattr(azure, 'run_channel', fail_guard)
    assert attempt(fake, local, ledger, directory, RUN) == 2
    out = capsys.readouterr().out
    assert 'Azure attempt invalid: GuardFailure; step run-command init; +52.5s' in out
    assert 'details suppressed' not in out and 'CANARY_SECRET_GUARD' not in out
    assert azure._fixed_step(Mock(step='CANARY_FREE_STEP')) == 'unknown'
    ev_k = directory / 'kick-step'; ev_k.mkdir()
    d_kick = make_draw(fake, ev_k, start=10_000)
    d_kick.account = 'chry' + '0' * 16
    monkeypatch.setattr(channel, 'plan_results_limits', lambda *_: (1024, 2048))
    monkeypatch.setattr(channel, 'storage_key', lambda *_: 'K' * 48)
    monkeypatch.setattr(channel, 'signed_blob', lambda *_: 'tok')
    monkeypatch.setattr(channel, 'seal', lambda *_: 'sealed')
    monkeypatch.setattr(channel, 'command', lambda dr, lbl, *a, **k: (setattr(dr, 'step', f'run-command {lbl}'), {'payload': 'P', 'results': 'R'})[1] if lbl == 'init' else (setattr(dr, 'step', f'run-command {lbl}'), (_ for _ in ()).throw(guard.GuardFailure('kick-fail')))[1])
    monkeypatch.setattr(channel, 'copy_results', lambda dr, *a, **k: setattr(dr, 'step', 'copy-results'))
    pf = directory / 'k.tar.gz'; pf.write_bytes(b'x')
    with pytest.raises(guard.GuardFailure, match='kick-fail'):
        channel.run_channel(d_kick, {'payload': pf}, ev_k)
    assert d_kick.step == 'run-command kick'

    monkeypatch.delenv('GATE_FIRST_GO')
    clock[0], real_run = 11_000.0, proof.subprocess.run

    def fail_az_stderr(cmd, *args, **kwargs):
        if 'rest' in cmd and cmd[cmd.index('--method') + 1] == 'get' and '/virtualMachines/gate?' in cmd[cmd.index('--url') + 1]:
            clock[0] += 11.2
            return subprocess.CompletedProcess(cmd, 1, b'', b'CANARY_SECRET_AZ https://10.9.8.7/sub/secret')
        return real_run(cmd, *args, **kwargs)

    monkeypatch.setattr(proof.subprocess, 'run', fail_az_stderr)
    assert azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
                             bundle.sha(directory / 'seal.json'), directory, dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372),
                             OTHER, 'eastus2', {}, 0, 2, 'first', None, ('test', 'fixture'), 0) == 2
    out2 = capsys.readouterr().out
    assert 'Azure wait virtualMachines transient read failure (1/3); continuing' in out2
    assert 'Azure wait virtualMachines transient read failure (2/3); continuing' in out2
    assert 'last Azure call: get virtualMachines; step wait virtualMachines; +43.6s' in out2 and 'CANARY_SECRET_AZ' not in out2
    assert json.loads((directory / 'time/runs' / OTHER / 'pre-ready-failure.json').read_text())['class'] == 'AzureError'

    # Failure AFTER guest-ready must NOT write pre-ready-failure.json.
    post_app = 'chrysalis-gate-20990405t000103-11223344'
    monkeypatch.setattr(proof.subprocess, 'run', real_run)
    monkeypatch.setattr(azure, 'run_channel', lambda dr, _i, nt: (guard.mark(nt / '.guard', 'guest-ready', since=dr.start), (_ for _ in ()).throw(guard.GuardFailure('post')))[1])
    assert azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
                             bundle.sha(directory / 'seal.json'), directory, dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372),
                             post_app, 'eastus2', {}, 0, 3, 'first', None, ('test', 'fixture'), 0) == 2
    assert not (directory / 'time/runs' / post_app / 'pre-ready-failure.json').exists()

    proc = subprocess.run([sys.executable, '-B', str(HERE / 'gate_azure.py'), 'recover'],
        env=dict(os.environ, CHRYSALIS_GATE_LOCAL='/tmp/CANARY_MAIN_SECRET/missing.json'), capture_output=True, text=True)
    assert proc.returncode == 2 and proc.stderr.strip() == 'Azure launcher failed: ValueError'
    assert 'CANARY_MAIN_SECRET' not in proc.stderr and 'details suppressed' not in proc.stderr


def test_host_step_markers_written_with_times_and_stop_at_failure_step(fake, site, ledger, monkeypatch, capsys):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    clock, vm_fail, real_run = [20_000.0], [False], proof.subprocess.run
    _patch_clock(monkeypatch, (azure, guard, channel, proof), clock)
    monkeypatch.setattr(channel, 'storage_key', lambda _d: 'K' * 48)
    monkeypatch.setattr(channel, 'signed_blob', lambda *_a: 'se=2099-01-01T00%3A00%3A00Z&sp=cw&spr=https&sv=2022-11-02&sr=b&sig=' + 'a' * 43)
    monkeypatch.setattr(channel, 'seal', lambda *_a: 'sealed')
    monkeypatch.setattr(channel, 'plan_results_limits', lambda *_: (1024, 2048))
    monkeypatch.setattr(channel, 'upload', lambda *_a, **_k: clock.__setitem__(0, clock[0] + 15.0))
    monkeypatch.setattr(azure, 'upload_inputs', channel.upload_inputs)

    def ticking_run(cmd, *args, **kwargs):
        if 'rest' in cmd and '--url' in cmd:
            url, method = cmd[cmd.index('--url') + 1], cmd[cmd.index('--method') + 1]
            if method == 'put' and '/virtualMachines/gate?' in url:
                clock[0] += 10.0
            elif method == 'get' and '/virtualMachines/gate/instanceView?' in url:
                return subprocess.CompletedProcess(cmd, 0, b'[]', b'')
            elif method == 'get' and '/virtualMachines/gate?' in url:
                clock[0] += 65.0
                if vm_fail[0]:
                    return subprocess.CompletedProcess(cmd, 0, b'"Failed"', b'')
            elif method == 'get' and '/schedules/' in url:
                clock[0] += 2.0
            elif method == 'put' and '/runCommands/init?' in url:
                clock[0] += 5.0
            elif method == 'get' and '/runCommands/init?' in url:
                clock[0] += 25.0
            elif method == 'put' and '/runCommands/kick?' in url:
                clock[0] += 7.0
        return real_run(cmd, *args, **kwargs)

    monkeypatch.setattr(proof.subprocess, 'run', ticking_run)
    fake_state(fake, commandResults={'init': {'payload': 'PUB1', 'results': 'PUB2'}, 'kick': {'started': 20_125}, 'status-00': {'done': True}})
    monkeypatch.setattr(channel, 'copy_results', lambda *_a, **_k: True)
    monkeypatch.setattr(azure, 'run_channel', channel.run_channel)
    payload_file = directory / 'p.tar.gz'
    payload_file.write_bytes(b'payload')
    seal_stub = dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40)
    plan_stub = dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372)
    assert azure.one_attempt(fake, local, ledger, seal_stub, bundle.sha(directory / 'seal.json'), directory, plan_stub,
                             RUN, 'eastus2', {'payload': payload_file}, 0, 1, 'first', None, ('test', 'fixture'), 0) == 2
    gdir = directory / 'time/runs' / RUN / '.guard'
    times = []
    for name in ('vm-put-sent', 'upload-done', 'vm-provisioned', 'init-sent', 'guest-ready'):
        p = gdir / name
        assert p.is_file() and (p.stat().st_mode & 0o777) == 0o600
        data = json.loads(p.read_text())
        assert set(data) == {'unix', 'utc', 'elapsedSeconds'}
        times.append(data['elapsedSeconds'])
    assert all(a < b for a, b in zip(times, times[1:]))

    # F4: mark(..., since=...) must refuse symlinks (O_NOFOLLOW).
    target = directory / 'untouched.txt'
    target.write_text('untouched')
    (gdir / 'sym-marker').symlink_to(target)
    with pytest.raises(OSError):
        guard.mark(gdir, 'sym-marker', since=20_000.0)
    assert target.read_text() == 'untouched'

    monkeypatch.delenv('GATE_FIRST_GO')
    vm_fail[0] = True
    fake_state(fake, deletesBeforeAbsent=1)
    assert azure.one_attempt(fake, local, ledger, seal_stub, bundle.sha(directory / 'seal.json'), directory, plan_stub,
                             OTHER, 'eastus2', {'payload': payload_file}, 0, 2, 'first', None, ('test', 'fixture'), 0) == 2
    gdir2 = directory / 'time/runs' / OTHER / '.guard'
    assert (gdir2 / 'vm-put-sent').is_file() and (gdir2 / 'upload-done').is_file()
    assert not any((gdir2 / n).exists() for n in ('vm-provisioned', 'init-sent', 'guest-ready'))
    assert 'Azure attempt invalid: GuardFailure; step wait virtualMachines;' in capsys.readouterr().out


def test_consumers_accept_pre_ready_then_kept_and_retry_consumers_refuse(fake, site, ledger, monkeypatch):
    rc, directory, seal, go, _, draws = _run_fly(monkeypatch, site, ledger, ['pre-ready', 'kept'], tag='change')
    assert rc == 0 and [d['decision'] for d in draws] == ['invalid-pre-ready', 'kept']
    assert timing.session_outcome(directory, seal, 0)[0] == 'complete'
    lines = timing.draw_lines(directory, first_only=False)
    assert any('invalid-pre-ready' in l for l in lines) and any('DRAW WARNING:' in l for l in lines)
    rep = gate.report(directory)
    assert rep['outcome'] == 'SUBSET' and 'invalid-pre-ready' in rep['text']

    # R1: retry consumers keep failing closed on 'invalid-pre-ready'.
    plan, first_item = seal['timePlans'][0], json.loads((directory / 'time/index.json').read_text())[0]
    with pytest.raises(ValueError, match='Retry draw differs'):
        bundle.retry_phase(directory, first_item, plan, plan['rows'], 'first', draws)
    with pytest.raises(ValueError, match='Invalid settled Azure retry outcome'):
        bundle.retry_azure_apps(directory, go, ledger.attempts, draws)
    bytes_plans = [dict(provider='azure'), dict(bytesOnly=True)]
    bytes_draws = [dict(session=1, draw=1, phase='first', app=draws[0]['app'], decision='invalid-pre-ready'),
                   dict(session=1, draw=2, phase='first', app=draws[1]['app'], decision='kept', archiveSha256=draws[1]['archiveSha256'])]
    dump(directory / 'time/bytes-index.json', [dict(session=1, phase='first', app=draws[1]['app'], archiveSha256=draws[1]['archiveSha256'])])
    with pytest.raises(KeyError) as kerr:
        bundle.check_azure_retry_bytes(directory, bytes_plans, bytes_draws)
    assert kerr.value.args == ('archiveSha256',)
    with pytest.raises(ValueError, match='Invalid original bytes draw order or slot'):
        bundle.check_azure_retry_bytes(directory, bytes_plans, [dict(bytes_draws[0], archiveSha256='0' * 64), bytes_draws[1]])

    # Noise go: finish_noise_records accepts invalid-pre-ready then kept.
    ledger.attempts.clear()
    ledger.write()
    real_vm = bundle.verified_machine

    def vm_with_applied(note, pl, spent, **kw):
        ev = real_vm(note, pl, spent, **kw)
        if not ev['invalid']:
            wl = schedules.opaque(pl['scheduleSeed'], ['workload', pl['rows'][0]['id']])
            ev['applied'] = {wl: {'expectedWork': pl['rows'][0].get('expectedWork', {})}}
        return ev

    def make_noise(s, p):
        p['rows'][0].update(aPairs=16, bPairs=0, corpusId='fixture-corpus', job={})
        p.update(design='noise-band', repeatAllowed=False)
        schedules.prepare(p)
        s.update(noiseRecords=True, rows=p['rows'], timePlans=[p])

    monkeypatch.setattr(bundle, 'verified_machine', vm_with_applied)
    rc_n, dir_n, seal_n, _, _, draws_n = _run_fly(monkeypatch, site, ledger, ['pre-ready', 'kept'], seal_patch=make_noise, tag='noise')
    assert rc_n == 0 and [d['decision'] for d in draws_n] == ['invalid-pre-ready', 'kept']
    assert set(json.loads((dir_n / 'time/noise-records.json').read_text())) == {seal_n['timePlans'][0]['rows'][0]['id']}


def _run_vm_deadline_attempt(fake, local, ledger, directory, monkeypatch, diag_handler, *, group=RUN, draw_num=1, plan=None, step=100.0, clock_start=30_000.0):
    clock, diag_calls, real_rest = [clock_start], [], fake.rest
    _patch_clock(monkeypatch, (azure, guard, channel, proof), clock)
    fake_state(fake, deletesBeforeAbsent=1)

    def fake_rest(method, url, **kwargs):
        if method == 'get' and '/virtualMachines/gate?' in url and 'expand=instanceView' in url:
            diag_calls.append((method, url, kwargs))
            return diag_handler(method, url, kwargs)
        if method == 'get' and '/virtualMachines/gate?' in url and kwargs.get('query') == 'properties.provisioningState':
            clock[0] += step
            return 'Creating'
        return real_rest(method, url, **kwargs)

    monkeypatch.setattr(fake, 'rest', fake_rest)
    plan = plan or dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372, provider='azure',
                        limitUsd=timing.azure_booking(1256 + guard.RESERVE_TAIL))
    rc = azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
                           bundle.sha(directory / 'seal.json'), directory, plan, group, 'eastus2', {},
                           0, draw_num, 'first', None, ('test', 'fixture'), 0)
    monkeypatch.setattr(fake, 'rest', real_rest)
    return rc, plan, diag_calls


def test_azdiag_vm_deadline_captures_creating_and_preserves_failure_and_redraw(fake, site, ledger, monkeypatch, capsys):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    payload_resp = dict(provisioningState='Creating', statuses=['ProvisioningState/creating', 'PowerState/starting'],
                        agentStatuses=['ProvisioningState/succeeded'])
    rc, plan, diag_calls = _run_vm_deadline_attempt(fake, local, ledger, directory, monkeypatch, lambda *_: payload_resp)
    out = capsys.readouterr().out.splitlines()
    note = directory / 'time/runs' / RUN
    assert rc == 2 and len(diag_calls) == 1
    assert diag_calls[0][2].get('timeout') == 7 and diag_calls[0][2].get('waits') == proof.ONCE
    failure = json.loads((note / 'pre-ready-failure.json').read_text())
    assert set(failure) == {'class', 'step', 'seconds'}
    assert failure['class'] == 'GuardFailure' and failure['step'] == 'wait virtualMachines' and failure['seconds'] >= guard.provision_seconds(plan['measurementCutoffSeconds'], plan['rawSeconds'])
    assert f'Azure attempt invalid: GuardFailure; step wait virtualMachines; +{failure["seconds"]:.1f}s' in out
    expected_diag = {'vm': payload_resp}
    diag_file = note / 'pre-ready-diag.json'
    assert diag_file.is_file() and not diag_file.is_symlink() and (diag_file.stat().st_mode & 0o777) == 0o600
    assert json.loads(diag_file.read_text()) == expected_diag
    assert 'Azure attempt diag: ' + json.dumps(expected_diag, sort_keys=True, separators=(',', ':')) in out
    assert not (note / 'guest-error.json').exists()
    assert bundle._pre_ready_note_ok(note, plan, ledger.attempts[RUN]['booked']) is True
    assert bundle.retry_run_ok(note, RUN, True) is True

    # Direct Draw.wait: VM deadline raises exact GuardFailure; non-VM deadline makes no diag read.
    (directory / 'nonvm').mkdir()
    d = make_draw(fake, directory / 'nonvm', start=40_000)
    monkeypatch.setattr(azure.time, 'time', lambda: 40_000.0 + guard.WRITE_END + 1)
    extra = []
    monkeypatch.setattr(fake, 'rest', lambda *a, **k: extra.append((a, k)))
    with pytest.raises(guard.GuardFailure, match='^Azure provision deadline reached$'):
        d.wait(d.path('Storage', 'storageAccounts', 'chry0000000000000000'), '2023-05-01')
    assert extra == [] and d.diag == {}


@pytest.mark.parametrize('case,expected', [
    ('timeout', dict(provisioningState='unavailable', statuses='unavailable', agentStatuses='unavailable')),
    ('junk', dict(provisioningState='unavailable', statuses='unavailable', agentStatuses='unavailable')),
    ('unknown', dict(provisioningState='other', statuses=['other'], agentStatuses=['other'])),
])
def test_azdiag_vm_diag_read_failure_or_junk_gives_unavailable_and_same_outcome(fake, site, ledger, monkeypatch, capsys, case, expected):
    local, directory = site
    prepare_attempt(monkeypatch, directory)

    def handler(*_):
        if case == 'timeout':
            raise proof.AzureError('timeout')
        if case == 'junk':
            return 'not-a-dict-junk'
        return dict(provisioningState='BogusState', statuses=['bad status'], agentStatuses=['123'])

    rc, plan, diag_calls = _run_vm_deadline_attempt(fake, local, ledger, directory, monkeypatch, handler)
    out = capsys.readouterr().out
    note = directory / 'time/runs' / RUN
    assert rc == 2 and len(diag_calls) == 1
    assert json.loads((note / 'pre-ready-diag.json').read_text()) == {'vm': expected}
    assert json.loads((note / 'pre-ready-failure.json').read_text())['step'] == 'wait virtualMachines'
    assert 'Azure attempt diag: ' + json.dumps({'vm': expected}, sort_keys=True, separators=(',', ':')) in out
    assert bundle._pre_ready_note_ok(note, plan, ledger.attempts[RUN]['booked']) is True


def test_azdiag_init_failed_exit75_and_work_deadline_stay_redraw_eligible(fake, site, ledger, monkeypatch, capsys):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    clock, real_rest = [50_000.0], fake.rest
    _patch_clock(monkeypatch, (azure, guard, channel, proof), clock)
    fake_state(fake, deletesBeforeAbsent=1)
    monkeypatch.setattr(channel, 'plan_results_limits', lambda *_: (1024, 2048))
    monkeypatch.setattr(azure, 'run_channel', channel.run_channel)

    def init_fail_rest(method, url, **kwargs):
        if method == 'get' and '/runCommands/init?' in url:
            clock[0] += 5.0
            return dict(state='Failed', code=75, output='CHRYSALIS-BOOT:cleanup-check\n')
        return real_rest(method, url, **kwargs)

    monkeypatch.setattr(fake, 'rest', init_fail_rest)
    payload_file = directory / 'p.tar.gz'
    payload_file.write_bytes(b'x')
    plan = dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372, provider='azure',
                limitUsd=timing.azure_booking(1256 + guard.RESERVE_TAIL))
    rc = azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
                           bundle.sha(directory / 'seal.json'), directory, plan, RUN, 'eastus2', {'payload': payload_file},
                           0, 1, 'first', None, ('test', 'fixture'), 0)
    out = capsys.readouterr().out
    note = directory / 'time/runs' / RUN
    expected_diag = {'init': {'executionState': 'Failed', 'exitCode': 75, 'bootCheck': 'cleanup-check'}}
    assert rc == 2
    assert json.loads((note / 'pre-ready-diag.json').read_text()) == expected_diag
    assert set(json.loads((note / 'pre-ready-failure.json').read_text())) == {'class', 'step', 'seconds'}
    assert not (note / 'guest-error.json').exists()
    assert channel._parse_guest_error('CHRYSALIS-BOOT:cleanup-check\n') is None
    assert 'Azure attempt diag: ' + json.dumps(expected_diag, sort_keys=True, separators=(',', ':')) in out
    assert bundle._pre_ready_note_ok(note, plan, ledger.attempts[RUN]['booked']) is True

    # Work-deadline path: captures last observed state ('Running'), 'unavailable' when polls never answered,
    # and 'other' when polls return a planted junk state (kills unnormalised last_state mutant).
    for poll_reply, want_state in ((dict(state='Running', code=None, output=None), 'Running'),
                                   (proof.AzureError('ResourceNotFound'), 'unavailable'),
                                   (dict(state='CANARY_STATE_198.51.100.7', code=None, output=None), 'other')):
        ev = directory / f'wd-{want_state}'; ev.mkdir()
        d = make_draw(fake, ev, start=60_000)
        clock[0] = 60_000.0
        monkeypatch.setattr(d, 'put', lambda *a, **k: None)

        def poll_rest(method, url, **kwargs):
            if isinstance(poll_reply, Exception):
                raise poll_reply
            return poll_reply

        monkeypatch.setattr(fake, 'rest', poll_rest)
        with pytest.raises(guard.GuardFailure, match='exceeded its work deadline'):
            channel.command(d, 'init', '#!/bin/bash\n', 60_010.0)
        assert d.diag == {'init': {'executionState': want_state, 'exitCode': 'unavailable', 'bootCheck': 'unavailable'}}

    # Init terminal non-success with planted non-int / out-of-range exitCode through a real poll gives exitCode='other'.
    for bad_code in (9999, -999, '75', True):
        ev_bc = directory / f'badcode-{type(bad_code).__name__}-{bad_code}'; ev_bc.mkdir()
        dbc = make_draw(fake, ev_bc, start=65_000)
        clock[0] = 65_000.0
        monkeypatch.setattr(dbc, 'put', lambda *a, **k: None)
        monkeypatch.setattr(fake, 'rest', lambda *a, _c=bad_code, **k: dict(state='Failed', code=_c, output='CHRYSALIS-BOOT:timer\n'))
        with pytest.raises(guard.GuardFailure, match='did not succeed'):
            channel.command(dbc, 'init', '#!/bin/bash\n', 65_010.0)
        assert dbc.diag == {'init': {'executionState': 'Failed', 'exitCode': 'other', 'bootCheck': 'timer'}}

    # Non-init command (kick) non-success does NOT populate draw.diag.
    ev_k = directory / 'kick-nodiag'; ev_k.mkdir()
    dk = make_draw(fake, ev_k, start=70_000)
    clock[0] = 70_000.0
    monkeypatch.setattr(dk, 'put', lambda *a, **k: None)
    monkeypatch.setattr(fake, 'rest', lambda *a, **k: dict(state='Failed', code=75, output='CHRYSALIS-BOOT:qdisc\n'))
    with pytest.raises(guard.GuardFailure, match='did not succeed'):
        channel.command(dk, 'kick', '#!/bin/bash\n', 70_010.0)
    assert dk.diag == {}


def test_azdiag_no_extra_call_on_success_or_capacity_failure(fake, site, ledger, monkeypatch, capsys):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    vm_expand_calls, real_rest = [], fake.rest
    fake_state(fake, deletesBeforeAbsent=1, commandResults={'init': {'payload': 'PUB1', 'results': 'PUB2'}})

    def spy_rest(method, url, **kwargs):
        if method == 'get' and '/virtualMachines/gate?' in url and 'expand=instanceView' in url:
            vm_expand_calls.append(url)
        return real_rest(method, url, **kwargs)

    monkeypatch.setattr(fake, 'rest', spy_rest)
    monkeypatch.setattr(azure, 'run_channel', lambda draw, _i, note: (
        channel.command(draw, 'init', '#!/bin/bash\n', time.time() + 30),
        guard.mark(note / '.guard', 'guest-ready', since=draw.start),
        False)[2])
    monkeypatch.setattr(azure, 'probe_result', lambda *_: 0)
    assert attempt(fake, local, ledger, directory, RUN) == 0
    assert vm_expand_calls == []
    assert not (directory / 'time/runs' / RUN / 'pre-ready-diag.json').exists()
    assert not (directory / 'time/runs' / RUN / 'pre-ready-failure.json').exists()
    assert 'Azure attempt diag:' not in capsys.readouterr().out

    # Capacity-code path on VM Failed: exit 6, zero $expand=instanceView VM diag calls, no pre-ready-diag.json.
    monkeypatch.delenv('GATE_FIRST_GO')

    def cap_rest(method, url, **kwargs):
        if method == 'get' and '/virtualMachines/gate?' in url and 'expand=instanceView' in url:
            vm_expand_calls.append(url)
        if method == 'get' and '/virtualMachines/gate?' in url and kwargs.get('query') == 'properties.provisioningState':
            return 'Failed'
        if method == 'get' and '/virtualMachines/gate/instanceView?' in url:
            return ['AllocationFailed']
        return real_rest(method, url, **kwargs)

    monkeypatch.setattr(fake, 'rest', cap_rest)
    assert azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
                             bundle.sha(directory / 'seal.json'), directory, dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372),
                             OTHER, 'eastus2', {}, 0, 2, 'first', None, ('test', 'fixture'), 0) == 6
    assert vm_expand_calls == []
    assert not (directory / 'time/runs' / OTHER / 'pre-ready-diag.json').exists()
    assert json.loads((directory / 'time/runs' / OTHER / 'pre-ready-failure.json').read_text())['class'] == 'CapacityUnavailable'

    # Non-capacity VM Failed in Draw.wait: raises GuardFailure('resource provisioning failed'), ZERO $expand=instanceView diag reads, no 'vm' diag.
    noncap_app = 'chrysalis-gate-20990405t000109-ef' + '01' * 3

    def noncap_rest(method, url, **kwargs):
        if method == 'get' and '/virtualMachines/gate?' in url and 'expand=instanceView' in url:
            vm_expand_calls.append(url)
        if method == 'get' and '/virtualMachines/gate?' in url and kwargs.get('query') == 'properties.provisioningState':
            return 'Failed'
        if method == 'get' and '/virtualMachines/gate/instanceView?' in url:
            return ['OtherProvisioningFailure']
        return real_rest(method, url, **kwargs)

    monkeypatch.setattr(fake, 'rest', noncap_rest)
    assert azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
                             bundle.sha(directory / 'seal.json'), directory, dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372),
                             noncap_app, 'eastus2', {}, 0, 3, 'first', None, ('test', 'fixture'), 0) == 2
    assert vm_expand_calls == []
    assert not (directory / 'time/runs' / noncap_app / 'pre-ready-diag.json').exists()
    assert json.loads((directory / 'time/runs' / noncap_app / 'pre-ready-failure.json').read_text())['class'] == 'GuardFailure'


def test_azdiag_closed_sets_block_planted_ids_addresses_urls_and_free_text(fake, site, ledger, monkeypatch, capsys):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    canaries = ('12345678-1234-1234-1234-1234567890ab', '198.51.100.7', 'https://example.invalid/sub/secret',
                'chrysalis-gate-20990102t030405-deadbeef', 'CANARY_LEAK')
    planted = dict(provisioningState=canaries[0],
                   statuses=[canaries[0], canaries[1], canaries[2], canaries[3], 'ProvisioningState/ok 123', 'ProvisioningState/creating'],
                   agentVersion=canaries[1],
                   agentStatuses=[canaries[4], canaries[2], canaries[1], 'ProvisioningState/succeeded'])
    rc, _, _ = _run_vm_deadline_attempt(fake, local, ledger, directory, monkeypatch, lambda *_: planted)
    out = capsys.readouterr().out
    raw_file = (directory / 'time/runs' / RUN / 'pre-ready-diag.json').read_text()
    assert rc == 2
    assert json.loads(raw_file) == {'vm': dict(
        provisioningState='other',
        statuses=['other', 'other', 'other', 'other', 'other', 'ProvisioningState/creating'],
        agentStatuses=['other', 'other', 'other', 'ProvisioningState/succeeded'])}
    for token in canaries:
        assert token not in raw_file and token not in out

    # Init command: planted values in state, code (including bool/out-of-range), and CHRYSALIS-BOOT line.
    for val in (True, 9999, -999, '75', canaries[4]):
        assert channel.norm_diag(val, range(-255, 256)) == 'other'
    assert channel._boot_check('CHRYSALIS-BOOT:' + canaries[0] + '\n') == 'other'
    assert channel._boot_check('CHRYSALIS-BOOT:' + canaries[3] + '\n') == 'other'
    assert channel._boot_check('CHRYSALIS-BOOT:timer\nCHRYSALIS-BOOT:ssh\n') == 'other'
    assert channel._boot_check('CHRYSALIS-BOOT:timer\n' + 'x' * 4100) == 'unavailable'
    assert channel._boot_check(None) == 'unavailable'


def test_azdiag_boot_script_reports_each_failing_check_and_execs_helper_on_pass(tmp_path):
    import shlex
    shutdown = 1_800_000_000
    expected_cal = f'OnCalendar={azure.calendar(shutdown)}'
    raw_script = channel.boot_script(RUN, shutdown, '/usr/local/sbin/chrysalis-run-channel')
    assert 'set -euo pipefail' in raw_script and 'exec 2>/dev/null' in raw_script
    assert '+ 95' in raw_script and 'sleep 5' in raw_script and 'failed=unknown' in raw_script
    assert f'/usr/bin/python3 /usr/local/sbin/chrysalis-guest-cleanup {RUN} check; then' in raw_script
    assert 'exec /usr/bin/python3 /usr/local/sbin/chrysalis-run-channel init' in raw_script
    assert 'printf \'CHRYSALIS-BOOT:%s\\n\' "$failed"' in raw_script

    fbin = tmp_path / 'bin'; fbin.mkdir()
    def shim(name, body):
        p = fbin / name
        p.write_text('#!/bin/sh\n' + body)
        p.chmod(0o755)

    shim('date', 'n=$(cat "$DATE_COUNT" 2>/dev/null || echo 0); n=$((n+1)); echo "$n" > "$DATE_COUNT"; '
                 'if [ "$n" -eq 1 ]; then echo 1000; elif [ "$n" -eq 2 ]; then echo 1001; else echo 2000; fi\n')
    shim('sleep', 'exit 0\n')
    shim('cloud-init', 'if [ "$FAIL_CHECK" = cloud-init ]; then echo "status: running"; exit 1; fi; echo "status: done"\n')
    shim('systemctl', f"""case "$1:$2:$3" in
  show:chrysalis-guest-cleanup.timer:*) [ "$FAIL_CHECK" = next-elapse ] && echo n/a || echo "2099-01-01 UTC" ;;
  is-active:--quiet:chrysalis-guest-cleanup.timer) [ "$FAIL_CHECK" = timer ] && exit 1 || exit 0 ;;
  is-enabled:--quiet:chrysalis-guest-cleanup.timer) exit 0 ;;
  cat:chrysalis-guest-cleanup.timer:*) echo {shlex.quote(expected_cal)} ;;
  cat:chrysalis-guest-cleanup.service:*) [ "$FAIL_CHECK" = service ] && echo "ExecStart=/bin/false" || echo {shlex.quote('ExecStart=/usr/bin/python3 /usr/local/sbin/chrysalis-guest-cleanup ' + RUN + ' delete')} ;;
  is-enabled:ssh.service:*) [ "$FAIL_CHECK" = ssh ] && {{ echo disabled; exit 1; }} || {{ echo masked; exit 1; }} ;;
  is-enabled:ssh.socket:*) echo masked; exit 1 ;;
  is-active:--quiet:ssh.service) exit 1 ;;
  *) exit 1 ;;
esac
""")
    shim('getent', 'if [ "$FAIL_CHECK" = user ]; then echo "chrysalis:x:1:1::/:/bin/bash"; else echo "chrysalis:x:1:1::/:/usr/sbin/nologin"; fi\n')
    shim('passwd', 'echo "chrysalis L 01/01/2026 0 99999 7 -1"\n')
    shim('tc', 'if [ "$FAIL_CHECK" = qdisc ]; then echo "qdisc fq_codel 0: dev eth0"; else echo "qdisc tbf 8001: dev eth0 root rate 1Mbit burst 32Kb"; fi\n')

    fake_cleanup = tmp_path / 'fake_cleanup.py'
    fake_cleanup.write_text('import os, sys\nsys.exit(1 if os.environ.get("FAIL_CHECK") == "cleanup-check" else 0)\n')
    fake_helper = tmp_path / 'fake_helper.py'
    fake_helper.write_text('import sys\nassert sys.argv[1:] == ["init"]\nprint(\'CHRYSALIS:{"payload":"P","results":"R"}\')\n')

    # Substitute only the hardcoded Linux guest paths in the test copy (asserted verbatim above).
    test_script = raw_script.replace(
        f'/usr/bin/python3 /usr/local/sbin/chrysalis-guest-cleanup {RUN} check',
        f'{shlex.quote(sys.executable)} {shlex.quote(str(fake_cleanup))} {RUN} check'
    ).replace(
        '/usr/bin/python3 /usr/local/sbin/chrysalis-run-channel init',
        f'{shlex.quote(sys.executable)} {shlex.quote(str(fake_helper))} init'
    )
    script_file = tmp_path / 'boot.sh'
    script_file.write_text(test_script)

    def run_boot(fail_check):
        counter = tmp_path / f'date-{fail_check or "pass"}'
        counter.unlink(missing_ok=True)
        env = {'PATH': f'{fbin}:/usr/bin:/bin', 'FAIL_CHECK': fail_check, 'DATE_COUNT': str(counter)}
        return subprocess.run(['/bin/bash', str(script_file)], env=env, capture_output=True, text=True, timeout=15)

    for word in ('cloud-init', 'timer', 'service', 'next-elapse', 'user', 'ssh', 'qdisc', 'cleanup-check'):
        res = run_boot(word)
        assert res.returncode == 75, f'{word}: rc={res.returncode} out={res.stdout!r}'
        assert res.stdout == f'CHRYSALIS-BOOT:{word}\n'
        assert channel._parse_guest_error(res.stdout) is None
        assert channel._boot_check(res.stdout) == word

    ok = run_boot('')
    assert ok.returncode == 0
    assert ok.stdout == 'CHRYSALIS:{"payload":"P","results":"R"}\n'
    assert 'CHRYSALIS-BOOT:' not in ok.stdout
    assert channel.get_summary(ok.stdout) == {'payload': 'P', 'results': 'R'}


def test_azpatience_window_math_and_consistency(fake, site, ledger, monkeypatch):
    assert guard.WORKER_START == 483 and not hasattr(channel, 'WORKER_START')
    assert guard.WRITE_END == 720 and guard.START_WINDOW == 300 and guard.SESSION_FRACTION == 0.80
    assert guard.RUN_COMMAND_SECONDS == 130 and channel.RUN_COMMAND_SECONDS == 130
    assert guard.INIT_COMMAND_MAX == 360 and guard.BOOT_MARGIN == 35 and guard.POLL_MARGIN == 15
    assert guard.KICK_RESERVE == 155 and guard.MIN_POST_VERIFY == 290
    for cutoff, raw, want_work, want_latest, want_prov in (
        (2149, 1448, 1159, 990, 690),
        (5764, 4592, 3674, 2090, 720),
        (6091, 4875, 3900, 2191, 720),
        (911, 372, 298, 613, 313),
        (483, 0, 0, 483, 183),
    ):
        assert guard.session_work_seconds(raw) == want_work
        assert guard.latest_worker_start_seconds(cutoff, raw) == want_latest
        assert guard.provision_seconds(cutoff, raw) == want_prov
        assert 0 < want_prov <= guard.WRITE_END
        assert want_prov + guard.START_WINDOW <= want_latest <= cutoff
    # M4: missing/invalid rawSeconds fails closed (no cutoff inversion fallback) in guard, one_attempt, and checked_azure_go.
    for bad in (None, -1, '1448', True, float('inf'), float('nan')):
        with pytest.raises(guard.GuardFailure, match='rawSeconds'):
            guard.session_work_seconds(bad)
    local, directory = site
    go, seal = sealed_site(site)
    for bad_raw in (None, 1):
        if bad_raw is None:
            seal['timePlans'][0].pop('rawSeconds', None)
        else:
            seal['timePlans'][0]['rawSeconds'] = bad_raw
        schedules.prepare(seal['timePlans'][0])
        bad_go = gate.write_seal(directory, seal)
        with pytest.raises(ValueError, match='Invalid Azure session windows'):
            bundle.checked_azure_go(directory, bad_go)
    prepare_attempt(monkeypatch, directory)
    bad_plan = dict(destroyGuardSeconds=2494, measurementCutoffSeconds=2149, provider='azure',
                    limitUsd=timing.azure_booking(2494 + guard.RESERVE_TAIL))
    before_ev = len(events(fake))
    with pytest.raises(guard.GuardFailure, match='rawSeconds'):
        azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
                          go, directory, bad_plan, RUN, 'eastus2', {}, 0, 1, 'first', None, ('test', 'fixture'), 0)
    assert len(events(fake)) == before_ev and RUN not in ledger.attempts and not (directory / 'time/runs' / RUN).exists()

    # One-clock start_plan boundaries (S1, S2, stub 911, min 483, plus all post-provision delay cases).
    ep = 10_000
    for c, raw, vm_off, now_off, want in (
        (2149, 1448, 104, 107, (ep + 637, 360, 325)),
        (2149, 1448, 517, 520, (ep + 990, 300, 265)),
        (2149, 1448, 571.6, 573.9, (ep + 990, 246, 211)),
        (2149, 1448, 690, 693, (ep + 990, 130, 95)),
        (2149, 1448, 517, 567, (ep + 990, 253, 218)),
        (2149, 1448, 104, 194, (ep + 724, 360, 325)),
        (2149, 1448, 104, 481, (ep + 990, 339, 304)),
        (2149, 1448, 104, 482.5, (ep + 990, 337, 302)),
        (2149, 1448, 690, 700, (ep + 990, 130, 95)),
        (5764, 4592, 104, 107, (ep + 637, 360, 325)),
        (5764, 4592, 517, 520, (ep + 1050, 360, 325)),
        (5764, 4592, 720, 723, (ep + 1253, 360, 325)),
        (911, 372, 75, 78, (ep + 608, 360, 325)),
        (911, 372, 313, 316, (ep + 613, 130, 95)),
        (483, 0, 104, 107, (ep + 483, 206, 171)),
        (483, 0, 183, 186, (ep + 483, 130, 95)),
        (483, 0, 183, 193, (ep + 483, 130, 95)),
    ):
        sb, it, bs = guard.start_plan(ep, c, ep + vm_off, ep + now_off, raw)
        assert (sb, it, bs) == want
        init_until = min(sb - 1, ep + now_off + it + guard.POLL_MARGIN)
        assert init_until == ep + now_off + it + guard.POLL_MARGIN
        assert sb - init_until >= guard.RUN_COMMAND_SECONDS + guard.POLL_MARGIN
    for c, raw, vm_off, now_off in ((2149, 1448, 690, 980), (2149, 1448, 690, 700.1), (483, 0, 183, 194), (483, 100, 0, 0)):
        with pytest.raises(guard.GuardFailure, match='cannot fit a full session'):
            guard.start_plan(ep, c, ep + vm_off, ep + now_off, raw)
    # W2 stays inside maxDraws=6 (12 booked attempts/session): RUN8 worst case $22.9560.
    s1_res = timing.azure_booking(2574 + guard.RESERVE_TAIL)
    s2_res = timing.azure_booking(6390 + guard.RESERVE_TAIL)
    assert (s1_res, s2_res) == (0.6306, 1.2824) and round(12 * (s1_res + s2_res), 4) == 22.9560


def _run_azpatience_attempt(fake, local, ledger, directory, monkeypatch, plan, payload_file, group, draw_num, vm_at, *,
                            retry_sched=False, init_dur=25.0, vm_put_at=None, upload_at=None, sched_at=None):
    from types import SimpleNamespace
    epoch = 30_000.0 + draw_num * 10_000.0
    clock, real_rest, orig_command, orig_mark = [epoch], fake.rest, channel.command, azure.mark
    sched_puts, captured, cmd_timeouts, init_calls = [0], {}, {}, []
    for mod in (azure, guard, channel, proof):
        monkeypatch.setattr(mod, 'time', SimpleNamespace(
            time=lambda: clock[0], sleep=lambda n: clock.__setitem__(0, clock[0] + max(0, n)),
            monotonic=time.monotonic, strftime=time.strftime, gmtime=time.gmtime,
        ))
    if draw_num > 1:
        monkeypatch.delenv('GATE_FIRST_GO', raising=False)
    fake_state(fake, deletesBeforeAbsent=1)
    monkeypatch.setattr(channel, 'command', lambda dr, lbl, scr, until, *, sealed=None, **kw: (
        captured.__setitem__(lbl, (scr, until, clock[0])), orig_command(dr, lbl, scr, until, sealed=sealed, **kw))[1])
    if retry_sched or upload_at is not None:
        def hook_mark(s, n, **kw):
            if n == 'upload-done' and upload_at is not None:
                clock[0] = epoch + upload_at
            r = orig_mark(s, n, **kw)
            if retry_sched and n == 'vm-provisioned':
                clock[0] = epoch + 690.5
            return r
        monkeypatch.setattr(azure, 'mark', hook_mark)
        monkeypatch.setattr(channel, 'mark', hook_mark)

    def fake_rest(method, url, **kwargs):
        if '/runCommands/init' in url:
            init_calls.append((method, url))
        if method == 'put' and '/runCommands/' in url:
            lbl = url.split('/runCommands/', 1)[1].split('?', 1)[0]
            cmd_timeouts[lbl] = json.loads(kwargs['stdin_body'].decode())['properties']['timeoutInSeconds']
        if method == 'put' and '/virtualMachines/gate?' in url and vm_put_at is not None:
            clock[0] = epoch + vm_put_at
        if method == 'put' and '/schedules/shutdown-computevm-gate?' in url:
            sched_puts[0] += 1
            if retry_sched and sched_puts[0] == 1:
                raise proof.AzureError('timeout')
        elif method == 'get' and '/virtualMachines/gate?' in url and kwargs.get('query') == 'properties.provisioningState':
            clock[0] = epoch + vm_at
            return 'Succeeded'
        elif method == 'get' and '/schedules/shutdown-computevm-gate?' in url:
            clock[0] = (epoch + sched_at) if sched_at is not None else (clock[0] + 3.0)
        elif method == 'get' and '/runCommands/init?' in url:
            clock[0] += init_dur
            return dict(state='Succeeded', code=0, output='CHRYSALIS:{"payload":"PUB1","results":"PUB2"}\n')
        elif method == 'get' and '/runCommands/kick?' in url:
            clock[0] += 10.0
            return dict(state='Succeeded', code=0, output=f'CHRYSALIS:{{"started":{int(clock[0])}}}\n')
        elif method == 'get' and '/runCommands/status-00?' in url:
            return dict(state='Succeeded', code=0, output='CHRYSALIS:{"done":true}\n')
        return real_rest(method, url, **kwargs)

    monkeypatch.setattr(fake, 'rest', fake_rest)
    seal_stub = dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40)
    rc = azure.one_attempt(fake, local, ledger, seal_stub, bundle.sha(directory / 'seal.json'), directory, plan,
                           group, 'eastus2', {'payload': payload_file}, 0, draw_num, 'first', None, ('test', 'fixture'), 0)
    monkeypatch.setattr(azure, 'mark', orig_mark)
    monkeypatch.setattr(channel, 'mark', orig_mark)
    monkeypatch.setattr(channel, 'command', orig_command)
    monkeypatch.setattr(fake, 'rest', real_rest)
    return rc, int(epoch), captured, cmd_timeouts, sched_puts[0], init_calls, directory / 'time/runs' / group


def test_azpatience_run4_run6_ontime_and_late_vm_timelines(fake, site, ledger, monkeypatch, tmp_path, capsys):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    s1_plan = dict(destroyGuardSeconds=2494, measurementCutoffSeconds=2149, rawSeconds=1448,
                   provider='azure', limitUsd=timing.azure_booking(2494 + guard.RESERVE_TAIL))
    payload_file = directory / 'payload.tar.gz'
    archive(payload_file, {'plan.json': {'probe': {'processes': 16}, 'members': {f'{i:016x}': {'build': 'b'} for i in range(64)}}})
    payload_sha = bundle.sha(payload_file)
    monkeypatch.setattr(channel, 'storage_key', lambda _d: 'K' * 48)
    monkeypatch.setattr(channel, 'signed_blob', lambda *_a: 'se=2099-01-01T00%3A00%3A00Z&sp=r&spr=https&sv=2022-11-02&sr=b&sig=' + 'a' * 43)
    monkeypatch.setattr(channel, 'seal', lambda *_a: 'sealed')
    monkeypatch.setattr(channel, 'upload', lambda *_a, **_k: None)
    monkeypatch.setattr(azure, 'upload_inputs', channel.upload_inputs)
    monkeypatch.setattr(channel, 'copy_results', lambda *_a, **_k: True)
    monkeypatch.setattr(azure, 'run_channel', channel.run_channel)
    go = bundle.sha(directory / 'seal.json')

    # (a) Run 4 replay (+517 s): proceeds to init (300 s / boot +265) and kick (start_by +990, 130 s), guest agrees.
    rc, ep1, cap1, to1, _, _, note1 = _run_azpatience_attempt(fake, local, ledger, directory, monkeypatch, s1_plan, payload_file, RUN, 1, 517.0)
    assert rc == 2 and (note1 / '.guard/init-sent').is_file() and (note1 / '.guard/guest-ready').is_file()
    init_script, init_until, init_now = cap1['init']
    kick_script, kick_until, _ = cap1['kick']
    assert '+ 265' in init_script and to1['init'] == 300 and to1['kick'] == 130 and to1['status-00'] == 130
    assert init_until == min(ep1 + 990 - 1, init_now + 300 + 15) and kick_until == ep1 + 990
    parts = [ln for ln in kick_script.splitlines() if ' fetch ' in ln][0].split()
    f_acct, f_sha, f_suite, f_epoch, f_start_by, f_cutoff, f_region = parts[parts.index('fetch') + 1:]
    assert (int(f_epoch), int(f_start_by), int(f_cutoff), f_region) == (ep1, ep1 + 990, ep1 + 2149, 'eastus2')
    for case, now_off, ok in (('late-ok', 550, True), ('at-bound', 990, False)):
        store, work = tmp_path / case / 'store', tmp_path / case / 'work'
        store.mkdir(parents=True)
        with monkeypatch.context() as mp:
            _patch_guest_push(mp, store, work)
            mp.setattr(guest, 'PAYLOAD_KEY', store / 'payload.pem')
            mp.setattr(guest.time, 'time', lambda off=now_off: ep1 + off)
            mp.setattr(guest, 'download', lambda _a, _n, _s, dst, _l, _d: (shutil.copyfile(payload_file, dst), payload_sha)[1])
            mp.setattr(guest.subprocess, 'run', lambda *a, **kw: subprocess.CompletedProcess(a[0], 0))
            mp.setattr(guest.subprocess, 'check_output', lambda cmd, **kw: b'2026-11-22\n' if cmd[0] == 'systemctl' else str(ep1 + 560).encode())
            if ok:
                assert guest.fetch(f_acct, f_sha, f_suite, int(f_epoch), int(f_start_by), int(f_cutoff), f_region) == {'started': ep1 + 560}
            else:
                with pytest.raises(ValueError, match='worker start deadline expired'):
                    guest.fetch(f_acct, f_sha, f_suite, int(f_epoch), int(f_start_by), int(f_cutoff), f_region)

    # (b) Run 6 S1 draw 2 real timeline: vm-put-sent +76.9, upload-done +90.2, vm-provisioned +571.6, verify +573.9 -> init + kick + status-00 succeed.
    app_r6 = 'chrysalis-gate-20990405t000102-cdcdcdcd'
    rc_r6, ep_r6, cap_r6, to_r6, _, _, note_r6 = _run_azpatience_attempt(
        fake, local, ledger, directory, monkeypatch, s1_plan, payload_file, app_r6, 2, 571.6,
        vm_put_at=76.9, upload_at=90.2, sched_at=573.9)
    assert rc_r6 == 2 and (note_r6 / '.guard/init-sent').is_file() and (note_r6 / '.guard/guest-ready').is_file()
    assert '+ 211' in cap_r6['init'][0] and to_r6['init'] == 246 and to_r6['kick'] == 130 and to_r6['status-00'] == 130 and cap_r6['kick'][1] == ep_r6 + 990

    # (c) On-time VM (+104 s, Run 5 draw 1 replay): 180 s init inside 360 s / 325 s boot succeeds; start_by +637 >= +483.
    app_r5 = 'chrysalis-gate-20990405t000103-11223344'
    _, ep2, cap2, to2, _, _, note2 = _run_azpatience_attempt(fake, local, ledger, directory, monkeypatch, s1_plan, payload_file, app_r5, 3, 104.0, init_dur=180.0)
    assert (note2 / '.guard/guest-ready').is_file() and '+ 325' in cap2['init'][0] and to2['init'] == 360 and cap2['kick'][1] == ep2 + 637 >= ep2 + 483

    # (d) Boundary S1 (+689.5 s) + retried schedule PUT at +690.5 s: keeps baseline 130/95, start_by +990 s.
    app_b = 'chrysalis-gate-20990405t000104-11223344'
    _, ep3, cap3, to3, puts3, _, note3 = _run_azpatience_attempt(fake, local, ledger, directory, monkeypatch, s1_plan, payload_file, app_b, 4, 689.5, retry_sched=True)
    assert puts3 == 2 and (note3 / '.guard/guest-ready').is_file() and '+ 95' in cap3['init'][0] and to3['init'] == 130 and to3['kick'] == 130 and to3['status-00'] == 130 and cap3['kick'][1] == ep3 + 990

    # (e) Last poll answering at +695 s > 690 s refuses at wait virtualMachines (with diag, no vm-provisioned); post-verify stall (+690 s -> +980 s) refuses at session-window; both AZ-REDRAW eligible.
    capsys.readouterr()
    for draw_n, app_id, vm_s, sc_s, want_step, want_vm_mark in (
            (5, 'chrysalis-gate-20990405t000105-11223344', 695.0, None, 'wait virtualMachines', False),
            (6, 'chrysalis-gate-20990405t000106-11223344', 690.0, 980.0, 'session-window', True)):
        rc_l, _, _, _, _, init_l, note_l = _run_azpatience_attempt(
            fake, local, ledger, directory, monkeypatch, s1_plan, payload_file, app_id, draw_n, vm_s, sched_at=sc_s)
        out_l = capsys.readouterr().out
        fl = json.loads((note_l / 'pre-ready-failure.json').read_text())
        assert rc_l == 2 and init_l == [] and (note_l / '.guard/vm-provisioned').is_file() is want_vm_mark
        assert not (note_l / '.guard/init-sent').exists() and not (note_l / '.guard/guest-ready').exists()
        assert (note_l / 'pre-ready-diag.json').is_file() is (not want_vm_mark)
        assert fl['class'] == 'GuardFailure' and fl['step'] == want_step and fl['seconds'] >= (sc_s or vm_s)
        assert f'Azure attempt invalid: GuardFailure; step {want_step}; +{fl["seconds"]:.1f}s' in out_l
        assert json.loads((note_l / 'cleanup-proof.json').read_text())['ourAppAbsent'] is True and ledger.attempts[app_id]['state'] == 'settled'
        assert bundle._pre_ready_note_ok(note_l, s1_plan, ledger.attempts[app_id]['booked']) is True
        assert bundle._pre_ready_settled(directory, local, go, 0, 'first', draw_n, app_id, None, s1_plan) is True


@pytest.mark.parametrize('guard_s,cutoff_s,raw_s,want_deadline', [(2494, 2149, 1448, 690), (6109, 5764, 4592, 720)])
def test_azpatience_stuck_vm_and_setup_wait_deadlines(fake, site, ledger, monkeypatch, guard_s, cutoff_s, raw_s, want_deadline):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    plan = dict(destroyGuardSeconds=guard_s, measurementCutoffSeconds=cutoff_s, rawSeconds=raw_s,
                provider='azure', limitUsd=timing.azure_booking(guard_s + guard.RESERVE_TAIL))
    payload_resp = dict(provisioningState='Creating', statuses=['ProvisioningState/creating'], agentStatuses=['ProvisioningState/succeeded'])
    rc, _, diag_calls = _run_vm_deadline_attempt(fake, local, ledger, directory, monkeypatch, lambda *_: payload_resp,
                                                 plan=plan, step=30.0, clock_start=40_000.0)
    note = directory / 'time/runs' / RUN
    failure = json.loads((note / 'pre-ready-failure.json').read_text())
    assert rc == 2 and len(diag_calls) == 1
    assert failure['class'] == 'GuardFailure' and failure['step'] == 'wait virtualMachines'
    assert want_deadline <= failure['seconds'] < want_deadline + 35
    assert json.loads((note / 'pre-ready-diag.json').read_text()) == {'vm': payload_resp}
    assert json.loads((note / 'cleanup-proof.json').read_text())['ourAppAbsent'] is True and ledger.attempts[RUN]['state'] == 'settled'
    assert bundle._pre_ready_note_ok(note, plan, ledger.attempts[RUN]['booked']) is True

    # M3 + M17 + M18: pin upload_inputs, identity wait (:369), Contributor-grant wait (:378), and one_attempt rawSeconds forwarding.
    if want_deadline == 690:
        waits, up_dl = [], []
        s1_dir, short_dir = directory / 's1-setup', directory / 'short-up'
        s1_dir.mkdir(); short_dir.mkdir()
        d_s1 = make_draw(fake, s1_dir, start=10_000, lifetime=2494, raw_seconds=1448)
        d_s1.account = 'chry' + '0' * 16
        orig_wf = azure.wait_for
        pf = directory / 'u.tar.gz'; pf.write_bytes(b'x')
        with monkeypatch.context() as mp:
            mp.setattr(azure.time, 'time', lambda: 10_010.0)
            mp.setattr(azure, 'wait_for', lambda fn, until, **kw: (waits.append((kw.get('label'), until)), orig_wf(fn, until, **kw))[1])
            mp.setattr(azure, 'upload_inputs', lambda *_a: None)
            d_s1.setup({'payload': pf})
        assert ('User-assigned identity', 10_690) in waits and ('Scoped Contributor grant', 10_690) in waits
        with monkeypatch.context() as mp:
            b_clock = [10_688.5]
            _patch_clock(mp, (azure, guard, proof), b_clock)
            d_s1.put(d_s1.path('Network', 'virtualNetworks'), azure.API_NET, {'location': 'eastus2'})
            b_clock[0] = 10_689.0
            with pytest.raises(guard.GuardFailure, match='ARM write deadline expired before dispatch'):
                d_s1.put(d_s1.path('Network', 'virtualNetworks'), azure.API_NET, {'location': 'eastus2'})
            b_clock[0] = 10_690.0
            with pytest.raises(guard.GuardFailure, match='guard expired'):
                d_s1.put(d_s1.path('Network', 'virtualNetworks'), azure.API_NET, {'location': 'eastus2'})
        d_short = make_draw(fake, short_dir, start=10_000, lifetime=1256, raw_seconds=372)
        d_short.account = 'chry' + '0' * 16
        with monkeypatch.context() as mp:
            mp.setattr(channel.time, 'time', lambda: 10_280.0)
            mp.setattr(channel, 'storage_key', lambda _d: 'K' * 48)
            mp.setattr(channel, 'signed_blob', lambda *_a: 'tok')
            mp.setattr(channel, 'upload', lambda _a, _n, _s, _p, _m, dl, **_k: up_dl.append(dl))
            channel.upload_inputs(d_short, {'payload': pf})
        assert up_dl == [10_313]


def test_azpatience2_m1_bounded_read_retries_in_preflight_and_final_proof(fake, site, ledger, monkeypatch):
    from types import SimpleNamespace
    local, directory = site
    assert proof.READ_WAITS == (2, 5)
    assert set(proof.TRANSIENT_READ_ERRORS) >= {'timeout', 'TooManyRequests', 'ServiceUnavailable', 'InternalServerError', 'RetryableError'}
    sleeps, calls = [], {}
    monkeypatch.setattr(proof, 'time', SimpleNamespace(time=time.time, sleep=sleeps.append))
    orig_call = fake._call

    def flaky_call(args, **kwargs):
        url = args[args.index('--url') + 1] if '--url' in args else ''
        toks = ('/providers/Microsoft.Compute?', '/permissions?', '/resourceGroups?', '/providers/Microsoft.Compute/disks?', '/roleAssignments?', '/vmSizes?', '/versions/')
        if any(tok in url for tok in toks):
            key = next(tok for tok in toks if tok in url)
            calls[key] = calls.get(key, 0) + 1
            if calls[key] == 1:
                raise proof.AzureError('ServiceUnavailable' if 'resourceGroups' in key else 'timeout')
        return orig_call(args, **kwargs)

    monkeypatch.setattr(fake, '_call', flaky_call)
    azure.preflight(fake, local['azureBaseline'], 'eastus2')
    assert len(calls) == 7 and all(v == 2 for v in calls.values())
    assert sleeps == [2] * 7
    for verb in ('put', 'delete'):
        with pytest.raises(proof.AzureError, match='Refusing retry on ARM mutation'):
            fake.rest(verb, f'/subscriptions/{SID}/resourceGroups/{RUN}?api-version=2021-04-01', waits=proof.READ_WAITS)

    # Deadline bound on proof(..., until=...): bounds call timeout, stops before sleeping past deadline, refuses calls past until, exhausts [2, 5], and never retries non-transient errors.
    sleeps.clear()
    clock = [1000.0]
    monkeypatch.setattr(proof, 'time', SimpleNamespace(time=lambda: clock[0], sleep=sleeps.append))
    attempts, timeouts, err_kind = [0], [], ['TooManyRequests']

    def always_err(args, **kwargs):
        attempts[0] += 1
        timeouts.append(kwargs.get('timeout'))
        raise proof.AzureError(err_kind[0])

    monkeypatch.setattr(fake, '_call', always_err)
    with pytest.raises(proof.AzureError, match='TooManyRequests'):
        proof.proof(fake, local['azureBaseline'], until=1001.5)
    assert attempts[0] == 1 and timeouts == [1.5] and sleeps == []
    attempts[0] = 0; timeouts.clear(); clock[0] = 1001.5
    with pytest.raises(proof.AzureError, match='timeout'):
        proof.proof(fake, local['azureBaseline'], until=1001.5)
    assert attempts[0] == 0 and sleeps == []
    clock[0] = 1000.0; attempts[0] = 0; timeouts.clear()
    with pytest.raises(proof.AzureError, match='TooManyRequests'):
        proof.proof(fake, local['azureBaseline'])
    assert attempts[0] == 3 and sleeps == [2, 5]
    sleeps.clear(); attempts[0] = 0; err_kind[0] = 'AuthorizationFailed'
    with pytest.raises(proof.AzureError, match='AuthorizationFailed'):
        proof.proof(fake, local['azureBaseline'])
    assert attempts[0] == 1 and sleeps == []

    # Final absence proof in one_attempt passes until=draw.deadline, retries transient error within it, and settles cleanly.
    monkeypatch.setattr(fake, '_call', orig_call)
    prepare_attempt(monkeypatch, directory)
    clock[0] = 20_000.0
    sleeps.clear()
    inv_hits, proof_untils = [0], []
    orig_proof = azure.proof
    monkeypatch.setattr(azure, 'proof', lambda *a, **kw: (proof_untils.append(kw.get('until')), orig_proof(*a, **kw))[1])
    for mod in (azure, guard, channel, proof):
        monkeypatch.setattr(mod, 'time', SimpleNamespace(
            time=lambda: clock[0],
            sleep=lambda s: (sleeps.append((inv_hits[0], s)), clock.__setitem__(0, clock[0] + s)),
            monotonic=time.monotonic, strftime=time.strftime, gmtime=time.gmtime,
        ))
    monkeypatch.setattr(azure, 'run_channel', lambda *_: True)
    fake_state(fake, deletesBeforeAbsent=1)

    def flaky_final(args, **kwargs):
        url = args[args.index('--url') + 1] if '--url' in args else ''
        if '/roleAssignments?' in url:
            inv_hits[0] += 1
            if inv_hits[0] == 2:  # 1st is preflight; 2nd is final absence proof
                raise proof.AzureError('InternalServerError')
        return orig_call(args, **kwargs)

    monkeypatch.setattr(fake, '_call', flaky_final)
    assert attempt(fake, local, ledger, directory, RUN) == 2
    assert inv_hits[0] == 3 and (2, 2) in sleeps and ledger.attempts[RUN]['state'] == 'settled'
    assert proof_untils[-1] == 20_000 + 1256 + guard.RESERVE_TAIL
    assert json.loads((directory / 'time/runs' / RUN / 'cleanup-proof.json').read_text())['ourAppAbsent'] is True

    # Watchdog proof() stops at draw.deadline while preserving an earlier provisional proof on a straddling pass.
    wd_state = directory / 'time/runs' / OTHER / '.guard'
    wd_state.mkdir(parents=True); wd_state.chmod(0o700)
    (wd_state / 'local-path').write_text(os.environ['CHRYSALIS_GATE_LOCAL'])
    wd_start, wd_guard = 30_000, 1256
    wd_deadline = wd_start + wd_guard + guard.RESERVE_TAIL
    dump(wd_state / 'watchdog.json', dict(group=OTHER, region='eastus2', start=wd_start, guard=wd_guard,
                                          cutoff=wd_guard - 345, distro='test', user='fixture',
                                          subscriptionFingerprint=proof.fingerprint(SID)))
    guard.mark(wd_state, 'group-put-sent')  # Unconfirmed PUT: keeper re-proves until deadline.
    wd_untils = []
    clock[0] = wd_deadline - 20.0

    def slow_call(args, **kwargs):
        clock[0] += 1.0
        return orig_call(args, **kwargs)

    with monkeypatch.context() as mp:
        mp.setattr(azure, 'HERE', directory / 'input/azure')
        mp.setattr(azure, 'wsl_context', lambda: ('test', 'fixture'))
        mp.setattr(bundle, 'checked_azure_local', lambda _: local)
        mp.setattr(gate, 'read_seal', lambda _: {})
        mp.setattr(azure, 'Az', lambda *_: fake)
        mp.setattr(fake, 'pin', lambda: SID)
        mp.setattr(fake, '_call', slow_call)
        for mod in (azure, guard, proof):
            mp.setattr(mod, 'time', SimpleNamespace(
                time=lambda: clock[0], sleep=lambda s: clock.__setitem__(0, clock[0] + s),
                monotonic=time.monotonic, strftime=time.strftime, gmtime=time.gmtime,
            ))
        mp.setattr(azure, 'proof', lambda *a, **kw: (wd_untils.append(kw.get('until')), orig_proof(*a, **kw))[1])
        azure.watchdog(wd_state)
    assert (wd_state / 'watchdog-proof.json').is_file() and not (wd_state / 'failed').exists()
    saved = json.loads((wd_state / 'watchdog-proof.json').read_text())
    assert saved['provisional'] is True and saved['summary']['ourAppAbsent'] is True and saved['ended'] < wd_deadline
    alerts = (wd_state / 'alerts.log').read_text()
    assert 'provisional absence, re-proof owed at next GO' in alerts and 'CLEANUP FAILURE' not in alerts
    assert clock[0] <= wd_deadline and wd_untils == [wd_deadline, wd_deadline]


def test_azpatience2_m2_status_poll_tolerates_transient_failures_up_to_limit(tmp_path, monkeypatch, capsys):
    orig_cmd = channel.command
    assert channel.MAX_STATUS_FAILURES == 3
    pf = tmp_path / 'p.tar.gz'
    archive(pf, {'plan.json': {'probe': {'processes': 16}, 'members': {}}})
    monkeypatch.setattr(channel, 'storage_key', lambda _d: 'K' * 48)
    monkeypatch.setattr(channel, 'signed_blob', lambda *_a: 'tok')
    monkeypatch.setattr(channel, 'seal', lambda *_a: 'sealed')

    def run_seq(name, replies, *, advance_to_end_on=None):
        ev = tmp_path / name / 'ev'; (ev / '.guard').mkdir(parents=True)
        dr = tmp_path / name / 'dr'; dr.mkdir(parents=True)
        clock = [10_000.0]
        for mod in (azure.time, guard.time, channel.time):
            monkeypatch.setattr(mod, 'time', lambda: clock[0])
        monkeypatch.setattr(channel.time, 'sleep', lambda s: clock.__setitem__(0, clock[0] + max(0.0, s)))
        d = make_draw(Mock(subscription=SID), dr, start=int(clock[0]), lifetime=2494)
        d.account = 'chry' + '0' * 16
        seen, times, copies = [], [], []
        monkeypatch.setattr(channel, 'copy_results', lambda *_a, **_k: (copies.append(d.worker_failed), False)[1])
        it = iter(replies)

        def fake_cmd(draw_arg, label, _script, _until, **_kw):
            if label == 'init':
                return {'payload': 'PUB1', 'results': 'PUB2'}
            if label == 'kick':
                return {'started': int(clock[0]) + 5}
            seen.append(label); times.append(clock[0])
            if advance_to_end_on == label:
                clock[0] = d.work_end + 1.0
            nxt = next(it)
            if isinstance(nxt, Exception):
                raise nxt
            return nxt

        monkeypatch.setattr(channel, 'command', fake_cmd)
        return d, seen, times, copies, lambda: channel.run_channel(d, {'payload': pf}, ev)

    # 2 failures -> healthy reset -> 2 more failures -> done: succeeds cleanly, paces wake, logs each tolerated failure.
    capsys.readouterr()
    d1, seen1, times1, copies1, go1 = run_seq('recover', [
        guard.GuardFailure('status timeout'),
        proof.AzureError('ServiceUnavailable'),
        {'done': False},
        guard.GuardFailure('status failed'),
        proof.AzureError('TooManyRequests'),
        {'done': True},
    ])
    assert go1() is False and d1.worker_done is True and d1.worker_failed is False
    assert seen1 == [f'status-{i:02}' for i in range(6)] and copies1 == [False]
    assert all(times1[i + 1] - times1[i] >= 300 for i in range(len(times1) - 1))
    out1 = capsys.readouterr().out
    assert 'Azure status-00 transient failure (1/3); continuing' in out1
    assert 'Azure status-01 transient failure (2/3); continuing' in out1
    assert 'Azure status-03 transient failure (1/3); continuing' in out1

    # 3 consecutive failures -> aborts on status-02.
    d2, seen2, _, copies2, go2 = run_seq('abort3', [
        guard.GuardFailure('f1'),
        proof.AzureError('ServiceUnavailable'),
        guard.GuardFailure('f3'),
    ])
    with pytest.raises(guard.GuardFailure, match='f3'):
        go2()
    assert seen2 == ['status-00', 'status-01', 'status-02'] and d2.worker_failed is True and copies2 == [True]

    # Failure at/after work_end still breaks cleanly to copy_results.
    d3, seen3, _, copies3, go3 = run_seq('at-end', [proof.AzureError('timeout')], advance_to_end_on='status-00')
    assert go3() is False and seen3 == ['status-00'] and d3.worker_failed is False and copies3 == [False]

    # Unexpected compact status and non-(AzureError, GuardFailure) exceptions still fail closed immediately.
    d4, seen4, _, _, go4 = run_seq('bad-shape', [{'unexpected': True}])
    with pytest.raises(guard.GuardFailure, match='unexpected compact status'):
        go4()
    assert seen4 == ['status-00']
    d4b, seen4b, _, _, go4b = run_seq('bad-val', [ValueError('bug')])
    with pytest.raises(ValueError, match='bug'):
        go4b()
    assert seen4b == ['status-00']

    # Corrupt/malformed CHRYSALIS: output from real command()/get_summary() aborts immediately on status-00.
    ev5 = tmp_path / 'bad-json/ev'; (ev5 / '.guard').mkdir(parents=True)
    dr5 = tmp_path / 'bad-json/dr'; dr5.mkdir(parents=True)
    clock5 = [10_000.0]
    az5 = Mock(subscription=SID)
    d5 = make_draw(az5, dr5, start=int(clock5[0]), lifetime=2494)
    d5.account = 'chry' + '0' * 16
    seen5 = []
    def fake_rest5(verb, url, **_k):
        if verb == 'put':
            return {}
        if '/runCommands/init?' in url:
            return {'state': 'Succeeded', 'code': 0, 'output': 'CHRYSALIS:{"payload":"PUB1","results":"PUB2"}\n'}
        if '/runCommands/kick?' in url:
            return {'state': 'Succeeded', 'code': 0, 'output': f'CHRYSALIS:{{"started":{int(clock5[0]) + 5}}}\n'}
        if '/runCommands/status-' in url:
            seen5.append(url.split('/runCommands/')[1].split('?')[0])
            return {'state': 'Succeeded', 'code': 0, 'output': 'CHRYSALIS:not-json\n'}
        raise AssertionError(f'unexpected {verb} {url}')
    az5.rest.side_effect = fake_rest5
    with monkeypatch.context() as mp:
        for mod in (azure.time, guard.time, channel.time):
            mp.setattr(mod, 'time', lambda: clock5[0])
        mp.setattr(channel.time, 'sleep', lambda s: clock5.__setitem__(0, clock5[0] + max(0.0, s)))
        mp.setattr(channel, 'copy_results', lambda *_a, **_k: False)
        mp.setattr(channel, 'command', orig_cmd)
        with pytest.raises(guard.GuardFailure, match='malformed compact Run Command result'):
            channel.run_channel(d5, {'payload': pf}, ev5)
    assert seen5 == ['status-00']


def test_azpatience2_m3_host_subprocess_timeouts_become_guard_failure(fake, site, ledger, monkeypatch, tmp_path, capsys):
    def boom(*_a, **_k):
        raise subprocess.TimeoutExpired(['cmd'], 10)

    with monkeypatch.context() as mp:
        mp.setattr(channel.subprocess, 'run', boom)
        with pytest.raises(guard.GuardFailure, match='timed out'):
            channel.signed_blob(fake, 'chry' + '0' * 16, 'K' * 48, 'payload', 'r')
    with monkeypatch.context() as mp:
        mp.setattr(azure.subprocess, 'run', boom)
        with pytest.raises(guard.GuardFailure, match='timed out'):
            azure.provision_public_key()
    with monkeypatch.context() as mp:
        ps = tmp_path / 'powershell.exe'; ps.write_text('x')
        mp.setattr(guard, 'POWERSHELL', ps)
        mp.setattr(guard, 'wsl_context', lambda: ('Ubuntu', 'tester'))
        mp.setattr(guard.subprocess, 'run', boom)
        with pytest.raises(guard.GuardFailure, match='timed out'):
            guard.arm_native(tmp_path / 'gate.py', tmp_path, time.time() + 60, 'Ubuntu', 'tester')

    # Through one_attempt: arm_native TimeoutExpired records class/step/seconds + pre-ready-failure.json (no traceback).
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    monkeypatch.setattr(azure, 'arm_native', lambda *_a, **_k: (_ for _ in ()).throw(guard.GuardFailure('Windows watchdog launch timed out; no ARM writes')))
    rc = attempt(fake, local, ledger, directory, RUN)
    out = capsys.readouterr().out
    note = directory / 'time/runs' / RUN
    assert rc == 2 and 'Azure attempt invalid: GuardFailure; step arm-watchdog; +' in out
    failure = json.loads((note / 'pre-ready-failure.json').read_text())
    assert failure['class'] == 'GuardFailure' and failure['step'] == 'arm-watchdog'


def test_azpatience2_m4_pre_group_put_failure_is_redraw_eligible_and_half_states_refused(fake, site, ledger, monkeypatch):
    local, directory = site
    prepare_attempt(monkeypatch, directory)
    monkeypatch.setattr(azure, 'arm_native', lambda *_a, **_k: (_ for _ in ()).throw(guard.GuardFailure('Windows watchdog launch failed; no ARM writes')))
    go = bundle.sha(directory / 'seal.json')
    plan = dict(destroyGuardSeconds=1256, measurementCutoffSeconds=911, rawSeconds=372, provider='azure',
                limitUsd=timing.azure_booking(1256 + guard.RESERVE_TAIL))
    rc = azure.one_attempt(fake, local, ledger, dict(platform={'azCliVersion': '2.99.1'}, programStopUsd=40),
                           go, directory, plan, RUN, 'eastus2', {}, 0, 1, 'first', None, ('test', 'fixture'), 0)
    note = directory / 'time/runs' / RUN
    gdir = note / '.guard'
    assert rc == 2 and (gdir / 'done').is_file()
    assert not (gdir / 'group-put-sent').exists() and not (gdir / 'group-put-confirmed').exists()
    assert ledger.attempts[RUN]['state'] == 'settled'
    booked = ledger.attempts[RUN]['booked']
    assert bundle._pre_ready_note_ok(note, plan, booked) is True
    assert bundle._pre_ready_settled(directory, local, go, 0, 'first', 1, RUN, None, plan) is True

    # Half-states and symlinks remain strictly refused.
    (gdir / 'group-put-sent').touch()
    assert bundle._pre_ready_note_ok(note, plan, booked) is False
    (gdir / 'group-put-sent').unlink()
    (gdir / 'group-put-confirmed').touch()
    assert bundle._pre_ready_note_ok(note, plan, booked) is False
    (gdir / 'group-put-confirmed').unlink()
    (gdir / 'group-put-sent').symlink_to(directory / 'nonexistent')
    assert bundle._pre_ready_note_ok(note, plan, booked) is False
    (gdir / 'group-put-sent').unlink()
    assert bundle._pre_ready_note_ok(note, plan, booked) is True
    real_done = directory / 'real-done'; real_done.write_text('ok')
    (gdir / 'done').unlink(); (gdir / 'done').symlink_to(real_done)
    assert bundle._pre_ready_note_ok(note, plan, booked) is False


def test_azpatience2_stray_pycache_in_input_azure_refused_by_dry_run(site, ledger, monkeypatch):
    _, directory = site
    go, _ = sealed_site(site)
    pycache = directory / 'input/azure/__pycache__'
    pycache.mkdir()
    (pycache / 'gate_azure.cpython-312.pyc').write_bytes(b'pyc')
    with pytest.raises(ValueError, match='Azure host snapshots missing, stale or unsealed'):
        bundle.checked_azure_go(directory, go)
    with pytest.raises(ValueError, match='Azure host snapshots missing, stale or unsealed'):
        bundle.azure_command(go, directory, dry=True)


def test_azpatience2_region_alternation_on_provision_failure_only(fake, site, ledger, monkeypatch):
    real_launch_azure = bundle.launch_azure
    assert bundle.PROVISION_FAILURE_STEPS == frozenset({'put virtualMachines', 'wait virtualMachines', 'session-window'})
    # wait virtualMachines -> eastus; run-command init -> keeps eastus; session-window -> alternates back to eastus2 -> kept.
    rc, d1, seal1, go1, calls1, draws1 = _run_fly(
        monkeypatch, site, ledger, ['step:wait virtualMachines', 'step:run-command init', 'step:session-window', 'kept'], tag='alt1')
    assert rc == 0 and [(c[2], c[5]) for c in calls1] == [(1, 'eastus2'), (2, 'eastus'), (3, 'eastus'), (4, 'eastus2')]
    assert [d['region'] for d in draws1] == ['eastus2', 'eastus', 'eastus', 'eastus2']
    assert [d['decision'] for d in draws1] == ['invalid-pre-ready', 'invalid-pre-ready', 'invalid-pre-ready', 'kept']
    assert draws1[0]['preReadyStep'] == 'wait virtualMachines' and 'regionReason' not in draws1[0]
    assert draws1[1]['regionReason'] == 'provision-alternation' and draws1[1]['preReadyStep'] == 'run-command init'
    assert 'regionReason' not in draws1[2] and draws1[2]['preReadyStep'] == 'session-window'
    assert draws1[3]['regionReason'] == 'provision-alternation'

    # Boot-check failure alone keeps eastus2 (does NOT alternate).
    ledger.attempts.clear(); ledger.write()
    rc2, d2, seal2, go2, calls2, draws2 = _run_fly(monkeypatch, site, ledger, ['step:run-command init', 'kept'], tag='alt2')
    assert rc2 == 0 and [(c[2], c[5]) for c in calls2] == [(1, 'eastus2'), (2, 'eastus2')]
    assert all('regionReason' not in d for d in draws2)

    # expected_draw_region authorizes eastus ONLY after a settled provisioning failure and NEVER while an attempt is active.
    assert bundle.expected_draw_region(d2, ledger, go2, 0, seal2['timePlans'][0], draw=3) is None
    first_app = calls2[0][3]
    entry1 = dict(ledger.attempts[first_app], exitCode=2)
    ledger.attempts.clear()
    ledger.attempts[first_app] = entry1
    ledger.write()
    dump(d2 / 'time/runs' / first_app / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
    assert bundle.expected_draw_region(d2, ledger, go2, 0, seal2['timePlans'][0], draw=2) == 'eastus'
    # Direct gate_azure.launch authorization for alternated eastus draw 2 vs refused dry-run / wrong draw / active ledger.
    import types
    import gate_fly_guard, gate_azure_payload
    with monkeypatch.context() as mp:
        mp.setenv('GATE_BUNDLE', 'input/session-0.tar.gz')
        mp.setattr(azure, 'held_lock', lambda *_: None)
        mp.setattr(azure, 'authorize_dispatch', lambda *_: None)
        mp.setattr(gate_fly_guard, 'verified_launch_inputs', lambda *_a, **_k: {'go': go2})
        mp.setattr(gate_azure_payload, 'verify_inputs', lambda *_a, **_k: {})
        mp.setattr(azure, 'Az', lambda *_: fake)
        seen_launch = []
        mp.setattr(azure, 'one_attempt', lambda _az, _loc, _led, _s, _g, _d, _pl, _app, reg, *_a: (seen_launch.append(reg), 0)[1])
        mk = lambda reg, draw=2, dry=False, fb=None: types.SimpleNamespace(
            seal=d2 / 'seal.json', session=0, draw=draw, phase='first', approved=OTHER, region=reg, fallback_of=fb, context=('test', 'fixture'), dry_run=dry)
        assert azure.launch(mk('eastus', draw=2)) == 0 and seen_launch == ['eastus']
        # launch() must also refuse plan['region'] (eastus2) on draw 2 when ledger expects eastus.
        with pytest.raises(azure.Refusal, match='Unsealed region'):
            azure.launch(mk('eastus2', draw=2))
        assert azure.launch(mk('eastus2', draw=1, dry=True)) == 0
        with pytest.raises(azure.Refusal, match='Unsealed region'):
            azure.launch(mk('eastus', draw=2, dry=True))
        with pytest.raises(azure.Refusal, match='Unsealed region'):
            azure.launch(mk('eastus', draw=3))
        ledger.attempts[first_app]['state'] = 'active'; ledger.write()
        assert bundle.expected_draw_region(d2, ledger, go2, 0, seal2['timePlans'][0], draw=2) is None
        with pytest.raises(azure.Refusal, match='Unsealed region'):
            azure.launch(mk('eastus', draw=2))
        with pytest.raises(azure.Refusal, match='Unsealed region'):
            azure.launch(mk('eastus2', draw=2))

        # Capacity fallback + probe rejection (eastus2 exit 6 -> eastus exit 5) keeps eastus2 for draw 2.
        plan2 = seal2['timePlans'][0]
        p_app, fb_app = 'chrysalis-gate-20990405t000201-aaaa0001', 'chrysalis-gate-20990405t000202-aaaa0002'
        ledger.attempts.clear()
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 1, p_app, status=6, region='eastus2')
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 1, fb_app, status=5, fallback=p_app, region='eastus')
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=2) == 'eastus2'
        seen_launch.clear()
        assert azure.launch(mk('eastus2', draw=2)) == 0 and seen_launch == ['eastus2']
        with pytest.raises(azure.Refusal, match='Unsealed region'):
            azure.launch(mk('eastus', draw=2))

        # Single-entry eastus capacity refusal (6) alternates back to eastus2; invalid region and unverified fallback refuse (None).
        d1_app, d2_app = 'chrysalis-gate-20990405t000211-bbbb0001', 'chrysalis-gate-20990405t000212-bbbb0002'
        ledger.attempts.clear()
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 1, d1_app, status=2, region='eastus2')
        dump(d2 / 'time/runs' / d1_app / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 2, d2_app, status=6, region='eastus')
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=3) == 'eastus2'
        seen_launch.clear()
        assert azure.launch(mk('eastus2', draw=3)) == 0 and seen_launch == ['eastus2']
        ledger.attempts[d2_app]['exitCode'] = 5; ledger.attempts[d2_app]['region'] = 'westus'; ledger.write()
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=3) is None
        # Unverified fallbackOf primary on an exitCode=2 fallback draw must be rejected.
        ledger.attempts.clear()
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 1, p_app, status=6, region='eastus2')
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 1, fb_app, status=2, fallback=p_app, region='eastus')
        (d2 / 'time/runs' / p_app / 'pre-ready-failure.json').unlink()
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=2) is None

        # Cross-phase MAX_PRE_READY_REDRAWS (3 in first blocks 4th in repeat).
        ledger.attempts.clear()
        for idx, reg in ((1, 'eastus2'), (2, 'eastus'), (3, 'eastus2')):
            gname = f'chrysalis-gate-20990405t00022{idx}-cccc000{idx}'
            _settle_pre_ready(d2, ledger, go2, 0, 'first', idx, gname, status=2, region=reg)
            dump(d2 / 'time/runs' / gname / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 4, 'chrysalis-gate-20990405t000224-cccc0004', status=0, region='eastus')
        rep1 = 'chrysalis-gate-20990405t000225-cccc0005'
        _settle_pre_ready(d2, ledger, go2, 0, 'repeat', 1, rep1, status=2, region='eastus2')
        dump(d2 / 'time/runs' / rep1 / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'repeat', draw=2) is None

        # Cost latch / costStops, kept exitCode=0 draw, dirty note, fallback gate, and fallback entry selection.
        ledger.attempts.clear()
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 1, first_app, status=2, region='eastus2')
        dump(d2 / 'time/runs' / first_app / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=2) == 'eastus'
        # cost latch & costStops block expected_draw_region
        ledger.cost_latch.write_text('{}')
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=2) is None
        ledger.cost_latch.unlink()
        ledger.data['costStops'] = ['over']
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=2) is None
        ledger.data['costStops'] = []
        # dirty note (missing cleanup-proof.json) blocks expected_draw_region
        cp = d2 / 'time/runs' / first_app / 'cleanup-proof.json'
        saved_cp = cp.read_bytes(); cp.unlink()
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=2) is None
        cp.write_bytes(saved_cp)
        # kept exitCode=0 draw blocks draw=3
        kept_app = 'chrysalis-gate-20990405t000231-dddd0001'
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 2, kept_app, status=0, region='eastus')
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=3) is None
        # fallback_of with non-eastus region refused by launch().
        with pytest.raises(azure.Refusal, match='Unsealed region'):
            azure.launch(mk('eastus2', draw=1, fb=first_app))
        # Provision failure on eastus capacity-fallback VM (eastus2(6)->eastus(2, wait virtualMachines)) expects eastus2.
        ledger.attempts.clear()
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 1, p_app, status=6, region='eastus2')
        _settle_pre_ready(d2, ledger, go2, 0, 'first', 1, fb_app, status=2, fallback=p_app, region='eastus')
        dump(d2 / 'time/runs' / fb_app / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=2) == 'eastus2'
        # Boot/init failure on eastus capacity-fallback VM (eastus2(6)->eastus(2, run-command init)) restores primary eastus2.
        dump(d2 / 'time/runs' / fb_app / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'run-command init', 'seconds': 12.3})
        assert bundle.expected_draw_region(d2, ledger, go2, 0, plan2, 'first', draw=2) == 'eastus2'

    # Boot/init failure on eastus capacity-fallback VM restarts draw 2 in primary eastus2 (no regionReason).
    ledger.attempts.clear(); ledger.write()
    rc_p1, _, _, _, calls_p1, draws_p1 = _run_fly(monkeypatch, site, ledger, ['fb-step:run-command init', 'kept'], tag='p1')
    assert rc_p1 == 0 and [(c[2], c[5]) for c in calls_p1] == [(1, 'eastus2'), (2, 'eastus2')]
    assert [d['region'] for d in draws_p1] == ['eastus', 'eastus2'] and all('regionReason' not in d for d in draws_p1)

    # Provision failure on eastus fallback VM returns to eastus2 with provision-alternation.
    ledger.attempts.clear(); ledger.write()
    rc_p2, _, _, _, calls_p2, draws_p2 = _run_fly(monkeypatch, site, ledger, ['fb-step:wait virtualMachines', 'kept'], tag='p2')
    assert rc_p2 == 0 and [(c[2], c[5]) for c in calls_p2] == [(1, 'eastus2'), (2, 'eastus2')]
    assert [d['region'] for d in draws_p2] == ['eastus', 'eastus2'] and draws_p2[1]['regionReason'] == 'provision-alternation'

    # An eastus-sealed plan never alternates to eastus2.
    ledger.attempts.clear(); ledger.write()
    def _seal_eastus(_s, pl):
        pl['region'] = 'eastus'; schedules.prepare(pl)
    rc_eu, d_eu, seal_eu, go_eu, calls_eu, draws_eu = _run_fly(
        monkeypatch, site, ledger, ['step:wait virtualMachines', 'kept'], seal_patch=_seal_eastus, tag='eu')
    assert rc_eu == 0 and [(c[2], c[5]) for c in calls_eu] == [(1, 'eastus'), (2, 'eastus')]
    assert all('regionReason' not in d for d in draws_eu)
    eu_app = calls_eu[0][3]
    eu_entry = dict(ledger.attempts[eu_app], exitCode=2)
    ledger.attempts.clear(); ledger.attempts[eu_app] = eu_entry; ledger.write()
    assert bundle.expected_draw_region(d_eu, ledger, go_eu, 0, seal_eu['timePlans'][0], 'first', draw=2) == 'eastus'

    # launch_azure passes --region eastus and does NOT fall back on exit 6 from eastus.
    cmds = []
    monkeypatch.setattr(bundle, 'owned_call', lambda cmd, **_k: (cmds.append(cmd), 6)[1])
    st, _, fb = real_launch_azure(d2, d2, go2, 0, 'first', 2, {'region': 'eastus2'}, 'input/session-0.tar.gz', False, ('test', 'fixture'), None, region='eastus')
    assert st == 6 and fb is None and len(cmds) == 1 and cmds[0][cmds[0].index('--region') + 1] == 'eastus'


def test_run8_d1_guest_result_member_allows_sizing_only(tmp_path):
    row_id = '0123456789abcdef'
    ok_tar = tmp_path / 'ok.tar.gz'
    bad_tar = tmp_path / 'bad.tar.gz'
    archive(ok_tar, {
        'results/session-status.json': {'completedAllDeclaredRounds': True, 'exitCode': 0},
        f'prelude/raw/{row_id}-calibration.json': {'ok': True},
        f'prelude/raw/{row_id}-sizing.json': {'ok': True},
        f'prelude/raw/{row_id}-baseline.json': {'ok': True},
        f'prelude/raw/{row_id}-candidate.json': {'ok': True},
        'prelude/applied.json': {},
    })
    assert guest.check_results(ok_tar) is False
    archive(bad_tar, {
        'results/session-status.json': {'completedAllDeclaredRounds': True, 'exitCode': 0},
        f'prelude/raw/{row_id}-spare.json': {'ok': True},
    })
    with pytest.raises(guest.ResultsRefused) as err:
        guest.check_results(bad_tar)
    assert err.value.reason == 'forbidden-member'


def test_run8_d3_guest_stdlib_only_and_s2_row_member_limits_agree():
    import ast
    from types import SimpleNamespace
    import gate_prepare as prepare
    from test_gate import side

    tree = ast.parse((HERE / 'gate_azure_guest.py').read_text())
    imported = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imported.update(alias.name.split('.')[0] for alias in node.names)
        elif isinstance(node, ast.ImportFrom):
            assert node.level == 0 and node.module is not None
            imported.add(node.module.split('.')[0])
    assert imported <= sys.stdlib_module_names, sorted(imported - sys.stdlib_module_names)

    config = prepare.azure_config(json.loads((gate.GATE / 'gated.json').read_text()))
    config['time']['noise']['Standard_F8als_v7'] = {}
    audit = json.loads((gate.REPO / 'benchmarks/Chrysalis.Benchmarks/audit-allocations.json').read_text())
    entries = json.loads((gate.GATE / 'plans/noise-records.json').read_text())
    selected = gate.select_rows(SimpleNamespace(mode='release', plan=None, only=None), config, audit, plan_entries=entries)
    seal = dict(gated=config, mode='release', noiseRecords=True, sessionPerRow=False, orderingProof=False,
                rows=[dict(item, sides=dict(baseline=side(), candidate=side())) for item in selected])
    s2 = schedules.prepare(prepare.time_plans(seal)[1])
    worker = schedules.worker_plan(s2)
    assert len(worker['members']) == 204
    assert guest.results_limits(s2, strict=True) == guest.results_limits(worker, strict=True) == (12_000_000, 230_000_000)


def test_run8_d10_f1_results_limit_12mb_and_no_plan_10mb(tmp_path):
    plan220 = {'probe': {'processes': 16}, 'members': {f'{i:016x}': {} for i in range(204)}}
    limits = guest.results_limits(plan220, strict=True)
    assert limits == (12_000_000, 230_000_000)

    def make_tar(path, payload_bytes):
        status = b'{"completedAllDeclaredRounds":true,"exitCode":0}\n'
        chunk = (b'a' * 99 + b'\n') * (payload_bytes // 100)
        with tarfile.open(path, 'w:gz', compresslevel=0) as stream:
            for name, raw in (('results/session-status.json', status), ('results/session.log', chunk)):
                ti = tarfile.TarInfo(name)
                ti.size = len(raw)
                stream.addfile(ti, io.BytesIO(raw))

    ok_105 = tmp_path / 'ok-10.5mb.tar.gz'
    make_tar(ok_105, 10_500_000)
    assert 10_000_000 < ok_105.stat().st_size <= 12_000_000
    assert guest.check_results(ok_105, limits=limits) is False
    # Without a plan, the default stays 10 MB and refuses 10.5 MB.
    assert guest.results_limits(None) == (10_000_000, 10_000_000)
    with pytest.raises(guest.ResultsRefused) as err_default:
        guest.check_results(ok_105)
    assert err_default.value.reason == 'results-too-large'
    over_125 = tmp_path / 'over-12.5mb.tar.gz'
    make_tar(over_125, 12_500_000)
    assert over_125.stat().st_size > 12_000_000
    with pytest.raises(guest.ResultsRefused) as err_over:
        guest.check_results(over_125, limits=limits)
    assert err_over.value.reason == 'results-too-large'


def test_c3rc_d2_draw_wait_transient_read_and_cli_tolerance(fake, tmp_path, monkeypatch, capsys):
    clock, sleeps = [10_000.0], []
    _patch_clock(monkeypatch, (azure, guard, proof), clock, sleeps=sleeps)
    draw = make_draw(fake, tmp_path, start=int(clock[0]), lifetime=2574, raw_seconds=1518)
    vm = draw.path('Compute', 'virtualMachines')
    # 1. Transient errors twice on poll 1 (within READ_WAITS=(2,5)), then across two consecutive polls, then Succeeded.
    seq = iter([
        proof.AzureError('timeout'),
        proof.AzureError('TooManyRequests'),
        proof.AzureError('ServiceUnavailable'),
        proof.AzureError('timeout'),
        proof.AzureError('TooManyRequests'),
        proof.AzureError('ServiceUnavailable'),
        'Succeeded',
    ])
    def _step(it):
        v = next(it)
        if isinstance(v, BaseException):
            raise v
        return v
    monkeypatch.setattr(fake, '_call', lambda *_a, **_k: _step(seq))
    draw.wait(vm, azure.API_VM)
    assert sleeps[:2] == [2, 5] and clock[0] < draw.provision_deadline

    # 2. 2 consecutive unclassified CLI read failures -> fixed log line, counter resets on healthy poll, succeeds.
    capsys.readouterr()
    cli_err = proof.AzureError('rest command failed (exit 1; details suppressed)')
    seq2 = iter([cli_err, cli_err, 'Creating', cli_err, cli_err, 'Succeeded'])
    monkeypatch.setattr(fake, '_call', lambda *_a, **_k: _step(seq2))
    draw.wait(vm, azure.API_VM)
    out2 = capsys.readouterr().out
    assert out2.count('Azure wait virtualMachines transient read failure (1/3); continuing') == 2
    assert out2.count('Azure wait virtualMachines transient read failure (2/3); continuing') == 2

    # 3. 3 consecutive unclassified CLI read failures -> raises immediately on 3rd.
    capsys.readouterr()
    seq3 = iter([cli_err, cli_err, cli_err, 'Succeeded'])
    monkeypatch.setattr(fake, '_call', lambda *_a, **_k: _step(seq3))
    with pytest.raises(proof.AzureError, match='rest command failed'):
        draw.wait(vm, azure.API_VM)
    out3 = capsys.readouterr().out
    assert '(1/3); continuing' in out3 and '(2/3); continuing' in out3 and '(3/3)' not in out3


def test_c3rc_d2_draw_wait_non_vm_and_deadline_and_non_transient(fake, tmp_path, monkeypatch):
    clock, sleeps = [10_000.0], []
    _patch_clock(monkeypatch, (azure, guard, proof), clock, sleeps=sleeps)
    draw = make_draw(fake, tmp_path, start=int(clock[0]), lifetime=1256, raw_seconds=372)
    nic = draw.path('Network', 'networkInterfaces')
    # Non-VM Draw.wait survives transient error + 1 unclassified CLI failure.
    seq = iter([
        proof.AzureError('ServiceUnavailable'),
        proof.AzureError('rest command failed (exit 1; details suppressed)'),
        'Succeeded',
    ])
    def _step(it):
        v = next(it)
        if isinstance(v, BaseException):
            raise v
        return v
    monkeypatch.setattr(fake, '_call', lambda *_a, **_k: _step(seq))
    draw.wait(nic, azure.API_NET)
    # Non-VM deadline expiry makes zero _diag_vm calls and wait_for does not overshoot provision_deadline.
    diag_calls = []
    monkeypatch.setattr(draw, '_diag_vm', lambda *a: diag_calls.append(a))
    clock[0] = draw.provision_deadline - 2.0
    monkeypatch.setattr(fake, '_call', lambda *_a, **_k: 'Creating')
    with pytest.raises(guard.GuardFailure, match='Azure provision deadline reached'):
        draw.wait(nic, azure.API_NET)
    assert diag_calls == [] and clock[0] == draw.provision_deadline

    # VM deadline-cutoff transient error turns into GuardFailure('Azure provision deadline reached') + _diag_vm,
    # and timeout passed to _call is clamped to remaining time.
    vm = draw.path('Compute', 'virtualMachines')
    clock[0] = draw.provision_deadline - 1.5
    timeouts = []
    def transient_near_end(_args, **kw):
        timeouts.append(kw.get('timeout'))
        clock[0] += 0.5
        raise proof.AzureError('TooManyRequests')
    monkeypatch.setattr(fake, '_call', transient_near_end)
    with pytest.raises(guard.GuardFailure, match='Azure provision deadline reached'):
        draw.wait(vm, azure.API_VM)
    assert diag_calls == [(vm, azure.API_VM)]
    assert timeouts and all(t <= 1.5 for t in timeouts) and clock[0] <= draw.provision_deadline

    # Non-transient AuthorizationFailed raises on try 1 with 0 sleeps.
    sleeps.clear()
    clock[0] = 10_010.0
    calls_auth = []
    def auth_err(*_a, **_k):
        calls_auth.append(1)
        raise proof.AzureError('AuthorizationFailed')
    monkeypatch.setattr(fake, '_call', auth_err)
    with pytest.raises(proof.AzureError, match='AuthorizationFailed'):
        draw.wait(vm, azure.API_VM)
    assert len(calls_auth) == 1 and sleeps == []

    # Failed + instanceView transient error retried via READ_WAITS -> CapacityUnavailable via allocate().
    sleeps.clear()
    iv_seq = iter(['Failed', proof.AzureError('ServiceUnavailable'), ['AllocationFailed']])
    monkeypatch.setattr(fake, '_call', lambda *_a, **_k: _step(iv_seq))
    with pytest.raises(azure.CapacityUnavailable, match='AllocationFailed'):
        draw.allocate(draw.wait, vm, azure.API_VM)
    assert sleeps == [2]


def test_c3rc_d2_q2_adjacent_setup_and_schedule_gets_bounded(fake, tmp_path, monkeypatch):
    clock, sleeps = [10_000.0], []
    _patch_clock(monkeypatch, (azure, guard, channel, proof), clock, sleeps=sleeps)
    draw = make_draw(fake, tmp_path, start=int(clock[0]), lifetime=2574, raw_seconds=1518)
    monkeypatch.setattr(azure, 'provision_public_key', lambda: 'ssh-ed25519 ' + 'A' * 68)
    monkeypatch.setattr(azure, 'upload_inputs', lambda *_a: None)
    orig_rest = fake.rest
    seen = {}
    targets = ('/userAssignedIdentities/gate?', '/roleAssignments/', '/networkWatchers?', '/schedules/shutdown-computevm-gate?')

    def check_rest(method, path, **kw):
        if method == 'get':
            for tok in targets:
                if tok in path:
                    seen.setdefault(tok, []).append((kw.get('waits'), kw.get('until')))
        return orig_rest(method, path, **kw)

    monkeypatch.setattr(fake, 'rest', check_rest)
    orig_call = fake._call
    fail_counts = {}

    def flaky_adjacent(args, **kw):
        url = args[args.index('--url') + 1] if '--url' in args else ''
        method = args[args.index('--method') + 1] if '--method' in args else ''
        if method == 'get':
            for tok in targets:
                if tok in url:
                    fail_counts[tok] = fail_counts.get(tok, 0) + 1
                    if fail_counts[tok] <= 2:
                        raise proof.AzureError('ServiceUnavailable')
        return orig_call(args, **kw)

    monkeypatch.setattr(fake, '_call', flaky_adjacent)
    draw.setup({})
    assert set(seen) == set(targets)
    for tok in targets[:3]:
        assert seen[tok][0] == (proof.READ_WAITS, draw.provision_deadline)
    assert seen[targets[3]][0] == (proof.READ_WAITS, draw.latest_worker_start)
    assert all(fail_counts[t] == 3 for t in targets)


def test_c3rc_d3_eastus_capacity_redraws_to_eastus2(fake, site, ledger, monkeypatch):
    # Draw 1 fails wait virtualMachines in eastus2 -> draw 2 settles exit 6 in eastus -> draw 3 runs in eastus2 & kept.
    rc, d1, seal1, go1, calls1, draws1 = _run_fly(
        monkeypatch, site, ledger, ['step:wait virtualMachines', 'eastus-cap', 'kept'], tag='d3cap')
    assert rc == 0 and [(c[2], c[5]) for c in calls1] == [(1, 'eastus2'), (2, 'eastus'), (3, 'eastus2')]
    assert [d['region'] for d in draws1] == ['eastus2', 'eastus', 'eastus2']
    assert [d['decision'] for d in draws1] == ['invalid-pre-ready', 'invalid-pre-ready', 'kept']
    assert draws1[1]['regionReason'] == 'provision-alternation' and draws1[1]['preReadyStep'] == 'put virtualMachines'
    assert draws1[2]['regionReason'] == 'capacity-alternation'

    # Direct expected_draw_region + gate_azure.launch authorizes draw 3 in eastus2 and refuses eastus.
    import types
    import gate_fly_guard, gate_azure_payload
    plan1 = seal1['timePlans'][0]
    ledger.attempts.pop(calls1[2][3])
    ledger.write()
    assert bundle.expected_draw_region(d1, ledger, go1, 0, plan1, 'first', draw=3) == 'eastus2'
    with monkeypatch.context() as mp:
        mp.setenv('GATE_BUNDLE', 'input/session-0.tar.gz')
        mp.setattr(azure, 'held_lock', lambda *_: None)
        mp.setattr(azure, 'authorize_dispatch', lambda *_: None)
        mp.setattr(gate_fly_guard, 'verified_launch_inputs', lambda *_a, **_k: {'go': go1})
        mp.setattr(gate_azure_payload, 'verify_inputs', lambda *_a, **_k: {})
        mp.setattr(azure, 'Az', lambda *_: fake)
        seen = []
        mp.setattr(azure, 'one_attempt', lambda _az, _loc, _led, _s, _g, _d, _pl, _app, reg, *_a: (seen.append(reg), 0)[1])
        mk = lambda reg, draw=3: types.SimpleNamespace(
            seal=d1 / 'seal.json', session=0, draw=draw, phase='first', approved=OTHER,
            region=reg, fallback_of=None, context=('test', 'fixture'), dry_run=False)
        assert azure.launch(mk('eastus2', draw=3)) == 0 and seen == ['eastus2']
        with pytest.raises(azure.Refusal, match='Unsealed region'):
            azure.launch(mk('eastus', draw=3))


def test_c3rc_d3_eastus_capacity_negative_guards_and_cross_phase_cap(site, ledger, monkeypatch):
    # (a) 2-entry eastus2(6) -> eastus(6) stops invalid, expected_draw_region is None.
    rc_both, d_both, seal_both, go_both, calls_both, draws_both = _run_fly(
        monkeypatch, site, ledger, ['both-cap', 'kept'], tag='d3both')
    assert rc_both == 2 and len(calls_both) == 1 and draws_both[0]['decision'] == 'invalid'
    assert bundle.expected_draw_region(d_both, ledger, go_both, 0, seal_both['timePlans'][0], 'first', draw=2) is None

    # (b) single-entry eastus2(6) refused.
    local, _ = site
    plan = seal_both['timePlans'][0]
    ledger.attempts.clear()
    g1 = 'chrysalis-gate-20990405t000301-eeee0001'
    _settle_pre_ready(d_both, ledger, go_both, 0, 'first', 1, g1, status=6, region='eastus2')
    assert bundle.expected_draw_region(d_both, ledger, go_both, 0, plan, 'first', draw=2) is None

    # (c) eastus-sealed plan getting exit 6 stops with no re-draw.
    ledger.attempts.clear(); ledger.write()
    def _seal_eu(_s, pl):
        pl['region'] = 'eastus'; schedules.prepare(pl)
    rc_eu, d_eu, seal_eu, go_eu, calls_eu, draws_eu = _run_fly(
        monkeypatch, site, ledger, ['eastus-cap', 'kept'], seal_patch=_seal_eu, tag='d3eu6')
    assert rc_eu == 2 and len(calls_eu) == 1 and draws_eu[0]['decision'] == 'invalid'
    assert bundle.expected_draw_region(d_eu, ledger, go_eu, 0, seal_eu['timePlans'][0], 'first', draw=2) is None

    # (d) eastus(6) with wrong class or wrong step in pre-ready-failure.json refused.
    ledger.attempts.clear()
    a1, a2 = 'chrysalis-gate-20990405t000311-ffff0001', 'chrysalis-gate-20990405t000312-ffff0002'
    _settle_pre_ready(d_both, ledger, go_both, 0, 'first', 1, a1, status=2, region='eastus2')
    dump(d_both / 'time/runs' / a1 / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
    _settle_pre_ready(d_both, ledger, go_both, 0, 'first', 2, a2, status=6, region='eastus')
    dump(d_both / 'time/runs' / a2 / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'put virtualMachines', 'seconds': 12.3})
    assert bundle.expected_draw_region(d_both, ledger, go_both, 0, plan, 'first', draw=3) is None
    assert bundle._pre_ready_settled(d_both, local, go_both, 0, 'first', 2, a2, None, plan, status=6) is False
    dump(d_both / 'time/runs' / a2 / 'pre-ready-failure.json', {'class': 'CapacityUnavailable', 'step': 'run-command init', 'seconds': 12.3})
    assert bundle.expected_draw_region(d_both, ledger, go_both, 0, plan, 'first', draw=3) is None
    assert bundle._pre_ready_settled(d_both, local, go_both, 0, 'first', 2, a2, None, plan, status=6) is False

    # (e) Cross-phase MAX_PRE_READY_REDRAWS = 3 counts an eastus(6) re-draw in first and blocks a 4th in repeat.
    ledger.attempts.clear()
    c1, c2, c3, c4, r1 = [f'chrysalis-gate-20990405t00032{i}-abcd000{i}' for i in range(1, 6)]
    _settle_pre_ready(d_both, ledger, go_both, 0, 'first', 1, c1, status=2, region='eastus2')
    dump(d_both / 'time/runs' / c1 / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
    _settle_pre_ready(d_both, ledger, go_both, 0, 'first', 2, c2, status=6, region='eastus')
    _settle_pre_ready(d_both, ledger, go_both, 0, 'first', 3, c3, status=2, region='eastus2')
    dump(d_both / 'time/runs' / c3 / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'run-command init', 'seconds': 120.0})
    _settle_pre_ready(d_both, ledger, go_both, 0, 'first', 4, c4, status=0, region='eastus2')
    _settle_pre_ready(d_both, ledger, go_both, 0, 'repeat', 1, r1, status=2, region='eastus2')
    dump(d_both / 'time/runs' / r1 / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
    assert bundle.expected_draw_region(d_both, ledger, go_both, 0, plan, 'repeat', draw=2) is None


@pytest.mark.parametrize('lead', [1.5, 4.0])
def test_c3rc_d4_watchdog_transient_near_deadline_bounded_and_preserves_provisional(fake, site, monkeypatch, lead):
    local, directory = site
    wd_state = directory / 'time/runs' / OTHER / '.guard'
    wd_state.mkdir(parents=True); wd_state.chmod(0o700)
    (wd_state / 'local-path').write_text(os.environ['CHRYSALIS_GATE_LOCAL'])
    wd_start, wd_guard = 30_000, 1256
    wd_deadline = wd_start + wd_guard + guard.RESERVE_TAIL
    dump(wd_state / 'watchdog.json', dict(group=OTHER, region='eastus2', start=wd_start, guard=wd_guard,
                                          cutoff=wd_guard - 345, distro='test', user='fixture',
                                          subscriptionFingerprint=proof.fingerprint(SID)))
    guard.mark(wd_state, 'group-put-sent')
    clock, wd_untils, pass_n = [wd_deadline - 20.0], [], [0]
    orig_call, orig_proof = fake._call, azure.proof

    def pass2_transient(args, **kw):
        url = args[args.index('--url') + 1] if '--url' in args else ''
        if pass_n[0] >= 2 and '/resourceGroups?' in url:
            clock[0] = max(clock[0], wd_deadline - lead)
            raise proof.AzureError('TooManyRequests')
        return orig_call(args, **kw)

    def tracked_proof(*a, **kw):
        pass_n[0] += 1
        wd_untils.append(kw.get('until'))
        return orig_proof(*a, **kw)

    with monkeypatch.context() as mp:
        _patch_clock(mp, (azure, guard, proof), clock)
        mp.setattr(azure, 'HERE', directory / 'input/azure')
        mp.setattr(azure, 'wsl_context', lambda: ('test', 'fixture'))
        mp.setattr(bundle, 'checked_azure_local', lambda _: local)
        mp.setattr(gate, 'read_seal', lambda _: {})
        mp.setattr(azure, 'Az', lambda *_: fake)
        mp.setattr(fake, 'pin', lambda: SID)
        mp.setattr(fake, '_call', pass2_transient)
        mp.setattr(azure, 'proof', tracked_proof)
        azure.watchdog(wd_state)

    assert wd_untils == [wd_deadline, wd_deadline]
    assert clock[0] <= wd_deadline
    assert (wd_state / 'watchdog-proof.json').is_file() and not (wd_state / 'failed').exists()
    assert 'CLEANUP FAILURE' not in (wd_state / 'alerts.log').read_text()


def test_c3rc_d6_status_failure_log_and_guest_error_clear(tmp_path, monkeypatch, capsys):
    pf = tmp_path / 'p.tar.gz'
    archive(pf, {'plan.json': {'probe': {'processes': 16}, 'members': {}}})
    monkeypatch.setattr(channel, 'storage_key', lambda _d: 'K' * 48)
    monkeypatch.setattr(channel, 'signed_blob', lambda *_a: 'tok')
    monkeypatch.setattr(channel, 'seal', lambda *_a: 'sealed')

    # 1. Tolerated status-00 sets guest_error -> cleared on continue so later push/status failure does not inherit it;
    #    log carries exception class and +Ns AFTER "; continuing".
    ev1, dr1 = tmp_path / 'tol/ev', tmp_path / 'tol/dr'
    (ev1 / '.guard').mkdir(parents=True); dr1.mkdir(parents=True)
    clock1 = [10_000.0]
    _patch_clock(monkeypatch, (azure, guard, channel), clock1)
    d1 = make_draw(Mock(subscription=SID), dr1, start=int(clock1[0]), lifetime=2494)
    d1.account = 'chry' + '0' * 16
    monkeypatch.setattr(channel, 'copy_results', lambda *_a, **_k: False)
    seen1 = []
    def cmd1(draw_arg, label, _script, _until, **_kw):
        seen1.append(label)
        if label == 'init':
            return {'payload': 'P', 'results': 'R'}
        if label == 'kick':
            return {'started': int(clock1[0]) + 5}
        if label == 'status-00':
            clock1[0] += 12.5
            draw_arg.guest_error = {'command': 'status-00', 'reason': 'transient-rc'}
            raise guard.GuardFailure('status-00 boom')
        if label == 'status-01':
            assert draw_arg.guest_error is None
            return {'done': True}
        raise AssertionError(label)
    monkeypatch.setattr(channel, 'command', cmd1)
    assert channel.run_channel(d1, {'payload': pf}, ev1) is False
    assert d1.guest_error is None and seen1 == ['init', 'kick', 'status-00', 'status-01']
    out1 = capsys.readouterr().out
    assert re.search(r'Azure status-00 transient failure \(1/3\); continuing \(GuardFailure; \+[0-9]+\.[0-9]s\)', out1)

    # 2. Failed status-00 at/after work_end (break branch) ALSO clears draw.guest_error for status-00.
    ev2, dr2 = tmp_path / 'end/ev', tmp_path / 'end/dr'
    (ev2 / '.guard').mkdir(parents=True); dr2.mkdir(parents=True)
    clock2 = [10_000.0]
    _patch_clock(monkeypatch, (azure, guard, channel), clock2)
    d2 = make_draw(Mock(subscription=SID), dr2, start=int(clock2[0]), lifetime=2494)
    d2.account = 'chry' + '0' * 16
    def cmd2(draw_arg, label, _script, _until, **_kw):
        if label == 'init':
            return {'payload': 'P', 'results': 'R'}
        if label == 'kick':
            return {'started': int(clock2[0]) + 5}
        if label == 'status-00':
            clock2[0] = float(draw_arg.work_end)
            draw_arg.guest_error = {'command': 'status-00', 'reason': 'at-cutoff'}
            raise guard.GuardFailure('late status')
        raise AssertionError(label)
    monkeypatch.setattr(channel, 'command', cmd2)
    assert channel.run_channel(d2, {'payload': pf}, ev2) is False
    assert d2.guest_error is None

    # 3. 3rd consecutive aborting status failure KEEPS draw.guest_error for that command.
    ev3, dr3 = tmp_path / 'abort/ev', tmp_path / 'abort/dr'
    (ev3 / '.guard').mkdir(parents=True); dr3.mkdir(parents=True)
    clock3 = [10_000.0]
    _patch_clock(monkeypatch, (azure, guard, channel), clock3)
    d3 = make_draw(Mock(subscription=SID), dr3, start=int(clock3[0]), lifetime=2494)
    d3.account = 'chry' + '0' * 16
    def cmd3(draw_arg, label, _script, _until, **_kw):
        if label == 'init':
            return {'payload': 'P', 'results': 'R'}
        if label == 'kick':
            return {'started': int(clock3[0]) + 5}
        draw_arg.guest_error = {'command': label, 'reason': 'err-' + label}
        raise guard.GuardFailure('fail-' + label)
    monkeypatch.setattr(channel, 'command', cmd3)
    with pytest.raises(guard.GuardFailure, match='fail-status-02'):
        channel.run_channel(d3, {'payload': pf}, ev3)
    assert d3.guest_error == {'command': 'status-02', 'reason': 'err-status-02'}


def test_c3rc_d6_no_global_time_sleep_patching_in_subprocess_tests():
    import ast
    tree = ast.parse(Path(__file__).read_text())
    target_fns = {
        'test_cleanup_waits_through_accepted_delete_and_transient',
        'test_init_submit_timeout_polls_without_resending',
        'test_host_step_markers_written_with_times_and_stop_at_failure_step',
        '_run_vm_deadline_attempt',
        'test_azdiag_init_failed_exit75_and_work_deadline_stay_redraw_eligible',
    }
    bad = []
    for node in ast.walk(tree):
        if isinstance(node, ast.FunctionDef) and node.name in target_fns:
            for call in ast.walk(node):
                if isinstance(call, ast.Call) and isinstance(call.func, ast.Attribute) and call.func.attr == 'setattr':
                    if len(call.args) >= 2 and isinstance(call.args[1], ast.Constant) and call.args[1].value in ('time', 'sleep'):
                        arg0 = ast.unparse(call.args[0])
                        if arg0 in ('time', 'azure.time', 'guard.time', 'channel.time', 'proof.time', 'mod'):
                            bad.append((node.name, arg0, call.args[1].value))
    assert bad == [], f'Global time module patched in subprocess tests: {bad}'


def _run_wd_case(fake, site, monkeypatch, tag, *, start_offset, call_hook=None, delete_hook=None, seed_provisional=False):
    local, directory = site
    wd_state = directory / 'time/runs' / f'chrysalis-gate-20990405t0004{tag}-abab0000' / '.guard'
    wd_state.mkdir(parents=True, exist_ok=True); wd_state.chmod(0o700)
    (wd_state / 'local-path').write_text(os.environ['CHRYSALIS_GATE_LOCAL'])
    wd_start, wd_guard = 30_000, 1256
    wd_deadline = wd_start + wd_guard + guard.RESERVE_TAIL
    dump(wd_state / 'watchdog.json', dict(group=wd_state.parent.name, region='eastus2', start=wd_start, guard=wd_guard,
                                          cutoff=wd_guard - 345, distro='test', user='fixture',
                                          subscriptionFingerprint=proof.fingerprint(SID)))
    guard.mark(wd_state, 'group-put-sent')
    if seed_provisional:
        dump(wd_state / 'watchdog-proof.json', dict(ended=wd_deadline - 30, summary={'ourAppAbsent': True}, provisional=True))
    clock, passes = [wd_deadline - start_offset], [0]
    orig_call, orig_proof, orig_del = fake._call, azure.proof, azure.Draw.delete

    def tracked_proof(*a, **kw):
        passes[0] += 1
        return orig_proof(*a, **kw)

    with monkeypatch.context() as mp:
        _patch_clock(mp, (azure, guard, proof), clock)
        mp.setattr(azure, 'HERE', directory / 'input/azure')
        mp.setattr(azure, 'wsl_context', lambda: ('test', 'fixture'))
        mp.setattr(bundle, 'checked_azure_local', lambda _: local)
        mp.setattr(gate, 'read_seal', lambda _: {})
        mp.setattr(azure, 'Az', lambda *_: fake)
        mp.setattr(fake, 'pin', lambda: SID)
        if call_hook:
            mp.setattr(fake, '_call', lambda args, **kw: call_hook(args, kw, orig_call, clock, passes, wd_deadline))
        if delete_hook:
            mp.setattr(azure.Draw, 'delete', lambda self, until, **kw: delete_hook(self, until, kw, orig_del, clock, passes, wd_deadline))
        mp.setattr(azure, 'proof', tracked_proof)
        azure.watchdog(wd_state)
    return wd_state, clock[0], passes[0], wd_deadline


def test_c3rc_d4_watchdog_guards_leftover_early_delete_and_first_pass(fake, site, monkeypatch):
    # Guard 3: real leftover on pass 2 at >= wd_deadline -> unlinks provisional proof, marks failed, logs CLEANUP FAILURE.
    def leftover_p2(args, kw, orig, clock, passes, dl):
        url = args[args.index('--url') + 1] if '--url' in args else ''
        if passes[0] >= 2 and '/resourceGroups?' in url:
            res = orig(args, **kw)
            return dict(res, items=list(res['items']) + [{'id': f'/subscriptions/{SID}/resourceGroups/stray-orphan-group', 'location': 'eastus2'}])
        if passes[0] >= 2 and '/networkSecurityGroups?' in url:
            clock[0] = dl
        return orig(args, **kw)
    st3, _, p3, _ = _run_wd_case(fake, site, monkeypatch, '01', start_offset=20.0, call_hook=leftover_p2)
    assert p3 == 2 and not (st3 / 'watchdog-proof.json').exists() and (st3 / 'failed').is_file()
    assert 'CLEANUP FAILURE' in (st3 / 'alerts.log').read_text()

    # Guard 4: early transient error (wd_deadline - 40s) unlinks stale provisional proof and retries fresh pass.
    unlinked_seen = []
    def early_transient(args, kw, orig, clock, passes, dl):
        url = args[args.index('--url') + 1] if '--url' in args else ''
        if passes[0] == 2 and '/resourceGroups?' in url:
            raise proof.AzureError('TooManyRequests')
        if passes[0] == 3 and '/resourceGroups?' in url:
            unlinked_seen.append(not (st4_ref[0] / 'watchdog-proof.json').exists())
            clock[0] = dl - 1.0  # finish cleanly after re-proving
        return orig(args, **kw)
    st4_ref = [site[1] / 'time/runs/chrysalis-gate-20990405t000402-abab0000/.guard']
    st4, _, p4, _ = _run_wd_case(fake, site, monkeypatch, '02', start_offset=50.0, call_hook=early_transient)
    assert p4 >= 3 and unlinked_seen == [True] and (st4 / 'watchdog-proof.json').is_file()

    # Guard 5: late group failing draw.delete on pass 2 near deadline -> unlinks provisional proof, marks failed.
    def fail_del_p2(self, until, kw, orig, clock, passes, dl):
        if passes[0] >= 1:
            clock[0] = dl
            raise guard.GuardFailure('group not confirmed absent by cleanup attempt deadline')
        return orig(self, until, **kw)
    st5, _, _, _ = _run_wd_case(fake, site, monkeypatch, '03', start_offset=20.0, delete_hook=fail_del_p2)
    assert not (st5 / 'watchdog-proof.json').exists() and (st5 / 'failed').is_file()
    assert 'CLEANUP FAILURE' in (st5 / 'alerts.log').read_text()

    # Guard 6: pass 1 only straddling wd_deadline (no earlier provisional proof) -> fails closed.
    def slow_p1(args, kw, orig, clock, passes, dl):
        clock[0] += 1.0
        return orig(args, **kw)
    st6, clk6, p6, dl6 = _run_wd_case(fake, site, monkeypatch, '04', start_offset=5.0, call_hook=slow_p1)
    assert p6 == 1 and clk6 <= dl6 and not (st6 / 'watchdog-proof.json').exists() and (st6 / 'failed').is_file()
    assert 'CLEANUP FAILURE' in (st6 / 'alerts.log').read_text()


def test_c3rc_d6_opus_m2_and_fable_m1_defensive_guards(site, ledger, tmp_path, monkeypatch):
    # 1. expected_draw_region defensive branches (gate_bundle.py:712, 716, 725, 726, 728).
    local, directory = site
    go, seal = sealed_site(site)
    plan = seal['timePlans'][0]
    g1, g2, g3 = [f'chrysalis-gate-20990405t00050{i}-cafe000{i}' for i in range(1, 4)]

    # Gap in draw numbering (draw 1 + draw 3, missing draw 2) -> None (:712).
    ledger.attempts.clear()
    _settle_pre_ready(directory, ledger, go, 0, 'first', 1, g1, status=5, region='eastus2')
    _settle_pre_ready(directory, ledger, go, 0, 'first', 3, g2, status=5, region='eastus2')
    assert bundle.expected_draw_region(directory, ledger, go, 0, plan, 'first', draw=4) is None

    # >2 entries for same draw -> None (:716).
    ledger.attempts.clear()
    for i, name in enumerate((g1, g2, g3)):
        _settle_pre_ready(directory, ledger, go, 0, 'first', 1, name, status=6 if i < 2 else 5,
                          fallback=None if i == 0 else g1, region='eastus2' if i == 0 else 'eastus')
    assert bundle.expected_draw_region(directory, ledger, go, 0, plan, 'first', draw=2) is None

    # 2-entry pentry checks (:725): bad pentry exitCode / region / fallbackOf / wrong fallback region -> None.
    for mut in (
        lambda p, f: p.update(exitCode=2),
        lambda p, f: p.update(region='eastus'),
        lambda p, f: p.update(fallbackOf=g3),
        lambda p, f: f.update(region='eastus2'),
    ):
        ledger.attempts.clear()
        _settle_pre_ready(directory, ledger, go, 0, 'first', 1, g1, status=6, region='eastus2')
        _settle_pre_ready(directory, ledger, go, 0, 'first', 1, g2, status=5, fallback=g1, region='eastus')
        mut(ledger.attempts[g1], ledger.attempts[g2])
        assert bundle.expected_draw_region(directory, ledger, go, 0, plan, 'first', draw=2) is None

    # 2-entry fallback draw when expected primary region is already eastus -> None (:726).
    ledger.attempts.clear()
    _settle_pre_ready(directory, ledger, go, 0, 'first', 1, g1, status=2, region='eastus2')
    dump(directory / 'time/runs' / g1 / 'pre-ready-failure.json', {'class': 'GuardFailure', 'step': 'wait virtualMachines', 'seconds': 691.0})
    _settle_pre_ready(directory, ledger, go, 0, 'first', 2, g2, status=6, region='eastus2')
    _settle_pre_ready(directory, ledger, go, 0, 'first', 2, g3, status=5, fallback=g2, region='eastus')
    assert bundle.expected_draw_region(directory, ledger, go, 0, plan, 'first', draw=3) is None

    # Single-entry draw with fallbackOf != None or wrong region != expected -> None (:728).
    ledger.attempts.clear()
    _settle_pre_ready(directory, ledger, go, 0, 'first', 1, g1, status=5, fallback=g2, region='eastus2')
    assert bundle.expected_draw_region(directory, ledger, go, 0, plan, 'first', draw=2) is None
    ledger.attempts.clear()
    _settle_pre_ready(directory, ledger, go, 0, 'first', 1, g1, status=5, region='eastus')
    assert bundle.expected_draw_region(directory, ledger, go, 0, plan, 'first', draw=2) is None

    # 2. fly_sessions keeps current_region (eastus) and clears region_reason across probe rejection (:1300).
    ledger.attempts.clear(); ledger.write()
    rc_pr, _, _, _, calls_pr, draws_pr = _run_fly(
        monkeypatch, site, ledger, ['step:wait virtualMachines', 'rejected', 'kept'], tag='prkeep')
    assert rc_pr == 0 and [(c[2], c[5]) for c in calls_pr] == [(1, 'eastus2'), (2, 'eastus'), (3, 'eastus')]
    assert [d['decision'] for d in draws_pr] == ['invalid-pre-ready', 'rejected', 'kept']
    assert draws_pr[1]['regionReason'] == 'provision-alternation' and 'regionReason' not in draws_pr[2]

    # 3. run_channel slow failed status poll advances wake >= finish + interval (:291) & work_end break (:283-284).
    pf = tmp_path / 'p.tar.gz'
    archive(pf, {'plan.json': {'probe': {'processes': 16}, 'members': {}}})
    monkeypatch.setattr(channel, 'storage_key', lambda _d: 'K' * 48)
    monkeypatch.setattr(channel, 'signed_blob', lambda *_a: 'tok')
    monkeypatch.setattr(channel, 'seal', lambda *_a: 'sealed')
    monkeypatch.setattr(channel, 'copy_results', lambda *_a, **_k: False)
    ev, dr = tmp_path / 'slowpoll/ev', tmp_path / 'slowpoll/dr'
    (ev / '.guard').mkdir(parents=True); dr.mkdir(parents=True)
    clock = [10_000.0]
    _patch_clock(monkeypatch, (azure, guard, channel), clock)
    d = make_draw(Mock(subscription=SID), dr, start=int(clock[0]), lifetime=2494)
    d.account = 'chry' + '0' * 16
    poll_starts = []
    def slow_fail_cmd(draw_arg, label, _script, _until, **_kw):
        if label == 'init':
            return {'payload': 'P', 'results': 'R'}
        if label == 'kick':
            return {'started': int(clock[0]) + 5}
        poll_starts.append((label, clock[0]))
        if label == 'status-00':
            clock[0] += 120.0  # slow failed poll finishes 120s after wake
            raise proof.AzureError('timeout')
        return {'done': True}
    monkeypatch.setattr(channel, 'command', slow_fail_cmd)
    assert channel.run_channel(d, {'payload': pf}, ev) is False
    # status-00 started at t0, ended at t0 + 120; status-01 must wait full interval (300s) after finish (>= t0 + 420).
    assert len(poll_starts) == 2 and poll_starts[1][1] - poll_starts[0][1] >= 420.0
