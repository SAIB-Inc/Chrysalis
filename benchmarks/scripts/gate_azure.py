#!/usr/bin/env python3
"""One sealed Azure attempt; inventory and cleanup are part of the gate."""
import argparse
import base64
from contextlib import ExitStack
from datetime import datetime, timezone
from decimal import Decimal
import fcntl
import fnmatch
from http.client import HTTPException
import json
import math
import os
from pathlib import Path
import re
import resource
import hashlib
import select
import secrets
import signal
import stat
import subprocess
import sys
from tarfile import TarError
import tempfile
import time
import uuid

from gate_azure_channel import STATUS_SCRIPT, create_storage, run_channel, upload_inputs
from gate_azure_guest import FETCH_STAGES, LOG_COUNTS, REFUSAL_REASONS
from gate_azure_guard import (ALERT_TAIL, COPY_END, RESERVE_TAIL, WATCH_MARGIN, WRITE_END, GuardFailure,
                              alert, arm_native, check_context, cleanup_lease, mark, mutation, process_id, retire, stop_paused_driver, wsl_context)
from gate_azure_proof import GROUP, GUID, ONCE, Az, AzureError, cost_readback, expected_posted, fingerprint, proof

from gate_time import PROVIDERS

HERE = Path(__file__).resolve().parent
AZURE = PROVIDERS['azure']
REGIONS = tuple(AZURE['regions'])
SIZE, IMAGE = AZURE['machine'], AZURE['image']
REQUIRED_PROVIDERS = ('Microsoft.Compute', 'Microsoft.Network', 'Microsoft.Resources', 'Microsoft.Authorization',
             'Microsoft.ManagedIdentity','Microsoft.Storage','Microsoft.DevTestLab')
USER = 'chrysalis'
OPENING_BALANCE = Decimal('4.29')
CONTRIBUTOR = 'b24988ac-6180-42a0-ab88-20f7382dd24c'
UNAVAILABLE = {'SkuNotAvailable', 'AllocationFailed', 'OverconstrainedAllocationRequest', 'ZonalAllocationFailed'}
ABSENT = {'ResourceGroupNotFound', 'ResourceNotFound'}
API_NET = '2024-05-01'
API_VM = '2024-07-01'


def calendar(epoch):
    return datetime.fromtimestamp(epoch, timezone.utc).strftime('%Y-%m-%d %H:%M:%S UTC')


def log(message):
    print(message, flush=True)


def matches(permissions, action):
    def includes(items):
        return any(fnmatch.fnmatchcase(action.lower(), pat.lower()) for pat in items)
    return any(includes(row['actions']) and not includes(row['notActions']) for row in permissions)


def permissions(az, scope):
    info = az.rest('get', scope + '/providers/Microsoft.Authorization/permissions?api-version=2022-04-01', waits=ONCE)
    if info.get('nextLink'):
        raise GuardFailure('incomplete Azure permissions response')
    return {action: matches(info['value'], action) for action in ('Microsoft.Authorization/roleAssignments/write',
                'Microsoft.Resources/subscriptions/resourceGroups/delete', 'Microsoft.Compute/virtualMachines/write',
                'Microsoft.Compute/virtualMachines/runCommands/write','Microsoft.Storage/storageAccounts/write',
                'Microsoft.Storage/storageAccounts/blobServices/containers/write','Microsoft.Storage/storageAccounts/listKeys/action',
                'Microsoft.ManagedIdentity/userAssignedIdentities/write', 'Microsoft.DevTestLab/schedules/write')}


def preflight(az, baseline, region, *, version=None, groups=()):
    if version is not None and az.version() != version:
        raise GuardFailure('Azure CLI version changed since seal')
    sid = az.subscription
    states = [az.rest('get', f'/subscriptions/{sid}/providers/{name}?api-version=2021-04-01',
                      query='registrationState') for name in REQUIRED_PROVIDERS]
    if states != ['Registered'] * len(REQUIRED_PROVIDERS):
        raise GuardFailure('Required provider not already Registered; no ARM writes')
    preview = permissions(az, '/subscriptions/' + sid)
    if not all(allowed for action, allowed in preview.items() if 'roleAssignments' not in action):
        raise GuardFailure('Insufficient Azure create or delete permissions')
    proof(az, baseline, groups)
    if available(az, region) != IMAGE.rsplit(':', 1)[1]:
        raise GuardFailure('Sealed size or pinned image unavailable in region')


def available(az, region):
    skus = az.rest('get', f'/subscriptions/{az.subscription}/providers/Microsoft.Compute/locations/{region}/vmSizes?api-version={API_VM}',
                   query=f"value[?name=='{SIZE}'].name")
    # This reports the size in the region; the VM PUT is the capacity check.
    if skus != [SIZE]:
        return None
    publisher, offer, sku, version = IMAGE.split(':')
    path = f'/subscriptions/{az.subscription}/providers/Microsoft.Compute/locations/{region}/publishers/{publisher}/artifacttypes/vmimage/offers/{offer}/skus/{sku}/versions/{version}'
    image = az.rest('get', path+'?api-version='+API_VM, query='name')
    return image


def yaml_file(path, payload, *, private=False):
    return dict(path=path, owner='root:root', permissions='0700' if private else '0644', content=payload)


def cloud_init(group, guard_end):
    mode = 'delete'
    service = '[Unit]\nDescription=Delete only this gate resource group (or power off)\n[Service]\nType=oneshot\n'
    service += f'ExecStart=/usr/bin/python3 /usr/local/sbin/chrysalis-guest-cleanup {group} {mode}\n'
    service += 'TimeoutStartSec=160\nNoNewPrivileges=true\nPrivateTmp=true\nProtectHome=true\n'
    timer = ('[Unit]\nDescription=Fixed Chrysalis cleanup deadline\n[Timer]\n'
             f'OnCalendar={calendar(guard_end)}\nAccuracySec=1s\nPersistent=true\n'
             '[Install]\nWantedBy=timers.target\n')
    return '#cloud-config\n' + json.dumps(dict(package_update=False, package_upgrade=False,
        write_files=[yaml_file('/usr/local/sbin/chrysalis-run-channel',(HERE/'gate_azure_guest.py').read_text(),private=True),
                     yaml_file('/usr/local/sbin/chrysalis-guest-cleanup', (HERE/'gate_azure_cleanup.py').read_text(), private=True),
                     yaml_file('/etc/systemd/system/chrysalis-guest-cleanup.service', service),
                     yaml_file('/etc/systemd/system/chrysalis-guest-cleanup.timer', timer)],
        runcmd=[['systemctl','daemon-reload'],['systemctl','enable','--now','chrysalis-guest-cleanup.timer'],
                ['usermod','-L','-s','/usr/sbin/nologin',USER],
                ['systemctl','mask','--now','ssh.service','ssh.socket'],
                ['sh','-ec', "dev=$(ip -o -4 route show default | awk '{print $5;exit}'); test -n \"$dev\"; tc qdisc replace dev \"$dev\" root tbf rate 1mbit burst 32kb latency 400ms"]]), indent=2) + '\n'


def provision_public_key():
    # Only the public half escapes this short-lived process. No private
    # serialization, core dump, private file or SSH command takes place.
    source='''import resource
resource.setrlimit(resource.RLIMIT_CORE,(0,0))
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding,PublicFormat
print(Ed25519PrivateKey.generate().public_key().public_bytes(Encoding.OpenSSH,PublicFormat.OpenSSH).decode('ascii'))
'''
    result=subprocess.run([sys.executable,'-IBc',source],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL,check=False,timeout=10,text=True)
    public=result.stdout.strip()
    if result.returncode or not re.fullmatch(r'ssh-ed25519 [A-Za-z0-9+/]{68}',public):
        raise GuardFailure('throwaway provisioning public key generation failed')
    return public


def wait_for(compare, until, interval=5, label='progress'):
    while time.time() < until:
        value = compare()
        if value:
            return value
        time.sleep(interval)
    raise GuardFailure(f'{label} deadline reached')


def creation_unconfirmed(guard):
    return (guard/'group-put-sent').exists() and not (guard/'group-put-confirmed').exists()


STEP_KINDS = frozenset({'resourceGroups', 'userAssignedIdentities', 'roleAssignments', 'storageAccounts',
                        'containers', 'networkWatchers', 'networkSecurityGroups', 'virtualNetworks',
                        'publicIPAddresses', 'networkInterfaces', 'virtualMachines', 'schedules'})
FIXED_STEP = re.compile(r'^(?:arm-watchdog|group-check|group-permissions|check networkWatchers|vm-prepare|'
                        r'upload-inputs|verify-schedule|prepare-kick|copy-results|download-results|probe-result|'
                        r'(?:put|wait) (?:arm|' + '|'.join(sorted(STEP_KINDS)) + r')|'
                        r'run-command (?:init|kick|status-(?:0[0-9]|1[0-8])|push-[12]))$')


def _exc_class(exc):
    name = type(exc).__name__
    return name if re.fullmatch(r'[A-Za-z_]{1,40}', name) else 'Error'


def _step_kind(path, scope):
    if path == scope:
        return 'resourceGroups'
    if path.endswith('/containers/payload'):
        return 'containers'
    if '/providers/' in path:
        parts = path.split('/providers/', 1)[1].split('/')
        if len(parts) > 1 and parts[1] in STEP_KINDS:
            return parts[1]
    return 'arm'


def _fixed_step(draw):
    step = getattr(draw, 'step', '')
    return step if isinstance(step, str) and FIXED_STEP.fullmatch(step) else 'unknown'


class Draw:
    def __init__(self, az, group, region, start, guard, guard_seconds, cutoff):
        if not GROUP.fullmatch(group) or region not in REGIONS:
            raise ValueError('invalid run group or region')
        self.az, self.group, self.region, self.start = az, group, region, start
        self.scope = f'/subscriptions/{az.subscription}/resourceGroups/{group}'
        self.guard = guard
        self.guard_seconds, self.cutoff = guard_seconds, cutoff
        self.guard_end = start + guard_seconds
        self.work_end = start + cutoff
        self.deadline = self.guard_end + RESERVE_TAIL
        self.worker_done = self.worker_failed = self.push_started = False
        self.guest_error = None
        self.commands = set()
        self.step = 'arm-watchdog'

    def path(self, namespace, kind, name='gate'):
        return f'{self.scope}/providers/Microsoft.{namespace}/{kind}/{name}'

    @property
    def group_url(self):
        return self.scope+'?api-version=2021-04-01'

    def owner(self, *, timeout=7, waits=ONCE):
        try:
            return self.az.rest('get', self.group_url, query='tags.chrysalisGate', timeout=timeout, waits=waits)
        except AzureError as exc:
            if str(exc) not in ABSENT:
                raise
            return False

    def delete(self, until, *, log_attempt=log):
        # Re-submit after any transient failure, even after an accepted async
        # response if the group still exists. A 404 when polling is the proof.
        retryable = {'Conflict', 'AnotherOperationInProgress', 'timeout', 'TooManyRequests', 'RetryableError',
                     'InternalServerError', 'ServiceUnavailable'}
        backoff = 2
        while time.time() < until:
            try:
                owner = self.owner(timeout=min(7, max(.1, until - time.time())), waits=())
                if owner is False:
                    return
                if owner != self.group:
                    raise GuardFailure('refusing to delete a group without the exact run owner tag')
                self.az.rest('delete', self.group_url, timeout=min(7, max(.1, until - time.time())))
                log_attempt('Azure accepted DELETE (not confirmed absent yet).')
            except AzureError as exc:
                if str(exc) in ABSENT:
                    return
                if str(exc) not in retryable:
                    raise
                log_attempt(f'Retrying owned group deletion after {exc}.')
            time.sleep(min(backoff, max(0, until-time.time())))
            backoff = min(backoff*2, 15)
        raise GuardFailure('group not confirmed absent by cleanup attempt deadline')

    def put(self, path, api, payload, *, work=False, until=None):
        url=path+'?api-version='+api
        if path!=self.scope and not path.startswith(self.scope+'/providers/'):
            raise GuardFailure('refusing out-of-group ARM mutation')
        deadline = self.start + WRITE_END
        if work:
            match = re.fullmatch(re.escape(self.path('Compute', 'virtualMachines')) + r'/runCommands/(init|kick|status-(?:0[0-9]|1[0-8])|push-[12])', path)
            if not match or match[1] in self.commands or len(self.commands) >= 23:
                raise GuardFailure('invalid or repeated Run Command')
            label = match[1]
            if label.startswith('status-'):
                if ('kick' not in self.commands or self.push_started or 'protectedParameters' in payload['properties']
                        or payload['properties'].get('source', {}).get('script') != STATUS_SCRIPT):
                    raise GuardFailure('status is read-only and must precede push')
                deadline = self.work_end
            elif label.startswith('push-'):
                if ('kick' not in self.commands
                        or not (self.worker_done or self.worker_failed or time.time() >= self.work_end)
                        or (label == 'push-2' and 'push-1' not in self.commands)):
                    raise GuardFailure('push outside completion or copy window')
                self.push_started = True
                deadline = self.work_end + COPY_END
            elif label == 'kick' and 'init' not in self.commands:
                raise GuardFailure('kick before init')
            self.step = f'run-command {label}'
            self.commands.add(label)
        else:
            self.step = 'put ' + _step_kind(path, self.scope)
        deadline = min(deadline, until if until is not None else float('inf'))
        with mutation(self.guard, deadline) as lease:
            if path == self.scope:
                # Persist intent before dispatch, covering kill or timeout after
                # ARM receives the PUT but before the response reaches the driver.
                mark(self.guard,'group-put-sent')
            left = deadline - time.time()
            if left<=1:
                raise GuardFailure('ARM write deadline expired before dispatch')
            if path == self.path('Compute', 'virtualMachines'):
                mark(self.guard, 'vm-put-sent', since=self.start)
            elif work and label == 'init':
                mark(self.guard, 'init-sent', since=self.start)
            result = self.az.rest('put',url,stdin_body=json.dumps(payload).encode('utf8'),timeout=min(left, 15 if work else 75),fence_fd=lease)
            if path == self.scope:
                if self.owner() != self.group:
                    raise GuardFailure('ARM group PUT not confirmed by an ownership read')
                mark(self.guard,'group-put-confirmed')
            return result

    def wait(self, path, api):
        self.step = 'wait ' + _step_kind(path, self.scope)
        def check():
            try:
                result = self.az.rest('get', path + '?api-version=' + api, query='properties.provisioningState', waits=ONCE)
            except AzureError as exc:
                if str(exc) in ABSENT:
                    return False
                raise
            if result in ('Failed', 'Canceled'):
                if 'virtualMachines/' in path:
                    # Only extract fixed failure codes. Retain no raw diagnostics.
                    codes = self.az.rest('get', path + '/instanceView?api-version=' + api, query='statuses[?level==`Error`].code', waits=ONCE)
                    for known in UNAVAILABLE:
                        if any(known.lower() in x.lower() for x in (codes or [])):
                            raise AzureError(known)
                raise GuardFailure('resource provisioning failed')
            return result == 'Succeeded'
        wait_for(check, self.start + WRITE_END, label='Azure provision')

    def resource(self, namespace, kind, props, *, name='gate', api=API_NET, sku=None):
        path = self.path(namespace, kind, name)
        payload = dict(location=self.region, properties=props)
        if sku:
            payload['sku'] = sku
        self.put(path, api, payload)
        self.wait(path, api)
        return path

    def setup(self, inputs):
        self.step = 'group-check'
        if self.owner() is not False:
            raise GuardFailure('refusing existing resource group')
        self.put(self.scope, '2021-04-01', dict(location=self.region, tags={'chrysalisGate': self.group}))
        self.step = 'group-permissions'
        if not permissions(self.az, self.scope)['Microsoft.Authorization/roleAssignments/write']:
            raise GuardFailure('Group-scope role assignment unavailable')
        identity = self.path('ManagedIdentity', 'userAssignedIdentities')
        identity_api = '2023-01-31'
        self.put(identity, identity_api, dict(location=self.region))
        # The identity endpoint need not expose provisioningState. Attest the principal.
        self.step = 'wait userAssignedIdentities'
        principal = wait_for(lambda: self.az.rest('get', identity + '?api-version=' + identity_api, query='properties.principalId', waits=ONCE),
                             self.start + WRITE_END, label='User-assigned identity')
        if not isinstance(principal, str) or not GUID.fullmatch(principal):
            raise GuardFailure('User-assigned identity principal not confirmed')
        role = f'/subscriptions/{self.az.subscription}/providers/Microsoft.Authorization/roleDefinitions/{CONTRIBUTOR}'
        path, role_api = self.scope + '/providers/Microsoft.Authorization/roleAssignments/' + str(uuid.uuid4()), '2022-04-01'
        grant = dict(principalId=principal, principalType='ServicePrincipal', roleDefinitionId=role)
        self.put(path, role_api, dict(properties=grant))
        self.step = 'wait roleAssignments'
        wait_for(lambda: self.az.rest('get', path + '?api-version=' + role_api, query='properties.{principalId:principalId,principalType:principalType,roleDefinitionId:roleDefinitionId}', waits=ONCE) == grant,
                 self.start + WRITE_END, label='Scoped Contributor grant')
        create_storage(self)
        # The VNet may auto-enable a region's first watcher. Create and verify
        # its only watcher in our own group before putting any VNet.
        self.resource('Network', 'networkWatchers', {}, name='NetworkWatcher_'+self.region)
        self.step = 'check networkWatchers'
        wc = self.az.rest('get', '/subscriptions/'+self.az.subscription+'/providers/Microsoft.Network/networkWatchers?api-version='+API_NET,
             query=f"{{ids:value[?location=='{self.region}'].id,paginated:nextLink!=`null`}}", waits=ONCE)
        if (wc['paginated'] or [i.lower() for i in wc['ids']] != [self.path('Network','networkWatchers','NetworkWatcher_'+self.region).lower()]):
            raise GuardFailure('Network Watcher not uniquely scoped to run group')
        def rule(name, priority, source, destination_port, access):
            return dict(name=name, properties=dict(priority=priority, direction='Inbound', access=access,
                protocol='Tcp' if access == 'Allow' else '*', sourceAddressPrefix=source, destinationAddressPrefix='*',
                sourcePortRange='*', destinationPortRange=destination_port))
        def outbound(name, priority, destination, access):
            return dict(name=name, properties=dict(priority=priority, direction='Outbound', access=access,
                protocol='Tcp' if access=='Allow' else '*', sourceAddressPrefix='*', destinationAddressPrefix=destination,
                sourcePortRange='*', destinationPortRange='443' if access=='Allow' else '*'))
        nsg = self.resource('Network', 'networkSecurityGroups', dict(securityRules=[
            rule('deny-all-inbound',100,'*','*','Deny'), outbound('allow-arm',100,'AzureResourceManager','Allow'),
            outbound('allow-guest-agent',110,'AzureCloud','Allow'), outbound('deny-other-outbound',120,'*','Deny')]))
        vnet = self.resource('Network', 'virtualNetworks', dict(addressSpace=dict(addressPrefixes=['10.73.0.0/24']),
            subnets=[dict(name='gate', properties=dict(addressPrefix='10.73.0.0/28', defaultOutboundAccess=False,
                                                        networkSecurityGroup=dict(id=nsg)))]))
        pip = self.resource('Network', 'publicIPAddresses', dict(publicIPAllocationMethod='Static', publicIPAddressVersion='IPv4'),
                            sku=dict(name='Standard'))
        nic = self.resource('Network', 'networkInterfaces', dict(ipConfigurations=[dict(name='gate', properties=dict(
            subnet=dict(id=vnet+'/subnets/gate'), publicIPAddress=dict(id=pip), privateIPAllocationMethod='Dynamic'))]))
        self.create_vm(nic, identity, inputs)

    @staticmethod
    def allocate(operation, *args):
        try:
            return operation(*args)
        except AzureError as error:
            if str(error) in UNAVAILABLE:
                raise CapacityUnavailable(str(error)) from None
            raise

    def create_vm(self, nic, identity, inputs):
        self.step = 'vm-prepare'
        image_parts = IMAGE.split(':')
        vm = self.path('Compute', 'virtualMachines')
        user_data = cloud_init(self.group, self.guard_end)
        public = provision_public_key()
        body = dict(location=self.region, properties=dict(hardwareProfile=dict(vmSize=SIZE),
            storageProfile=dict(imageReference=dict(zip(('publisher', 'offer', 'sku', 'version'), image_parts)),
                osDisk=dict(name='gate-os', createOption='FromImage', diskSizeGB=32, deleteOption='Delete',
                            managedDisk=dict(storageAccountType='StandardSSD_LRS'))),
            osProfile=dict(computerName='chrysalis-gate', adminUsername=USER,
                customData=base64.b64encode(user_data.encode()).decode(),
                linuxConfiguration=dict(disablePasswordAuthentication=True,provisionVMAgent=True,
                    ssh=dict(publicKeys=[dict(path=f'/home/{USER}/.ssh/authorized_keys',keyData=public)]))),
            networkProfile=dict(networkInterfaces=[dict(id=nic, properties=dict(primary=True, deleteOption='Delete'))]),
            diagnosticsProfile=dict(bootDiagnostics=dict(enabled=False))))
        body['identity'] = dict(type='UserAssigned', userAssignedIdentities={identity: {}})
        self.allocate(self.put, vm, API_VM, body)
        schedule = self.path('DevTestLab', 'schedules', 'shutdown-computevm-gate')
        api = '2018-09-15'
        props = dict(status='Enabled', taskType='ComputeVmShutdownTask', dailyRecurrence={'time': shutdown_time(self.guard_end)},
                     timeZoneId='UTC', targetResourceId=vm, notificationSettings={'status': 'Disabled'})
        spec = dict(location=self.region, properties=props)
        try:
            self.put(schedule, api, spec)
            retry = False
        except AzureError:
            retry = True
        upload_inputs(self, inputs)  # VM provisioning runs while the private blobs upload.
        self.allocate(self.wait, vm, API_VM)
        mark(self.guard, 'vm-provisioned', since=self.start)
        if retry:
            self.put(schedule, api, spec)
        self.step = 'verify-schedule'
        actual = self.az.rest('get', schedule + '?api-version=' + api, waits=ONCE)
        if not schedule_matches(actual, spec):
            raise GuardFailure('platform shutdown not confirmed; refusing init')


def shutdown_time(epoch):
    return datetime.fromtimestamp(((epoch + 59) // 60) * 60, timezone.utc).strftime('%H%M')


def schedule_matches(actual, expected):
    if not isinstance(actual, dict) or actual.get('location') != expected['location']:
        return False
    props = actual.get('properties', {})
    return props.get('provisioningState') == 'Succeeded' and all(
        all(props.get(k, {}).get(field) == item for field, item in v.items()) if isinstance(v, dict) else props.get(k) == v
        for k, v in expected['properties'].items())


class CapacityUnavailable(AzureError):
    pass


class Refusal(GuardFailure):
    """An outstanding attempt or reservation stop; launch nothing further."""


def atomic_json(path, value):
    with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, prefix='.azure-', delete=False) as stream:
        temporary = Path(stream.name)
        try:
            json.dump(value, stream, allow_nan=False, sort_keys=True)
            stream.write('\n')
            stream.flush()
            os.fsync(stream.fileno())
            temporary.replace(path)
            fd = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(fd)
            finally:
                os.close(fd)
        finally:
            temporary.unlink(missing_ok=True)


def held_lock(path):
    try:
        if not os.path.samestat(os.fstat(9), path.stat()):
            raise Refusal('Wrong inherited Azure lock')
        fcntl.flock(9, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        raise Refusal('Azure lock not owned by this go') from None


class Ledger:
    def __init__(self, local, *, read_only=False):
        self.path, self.lock = map(Path, (local['azureLedger'], local['azureLock']))
        self.read_only = read_only
        self.projection = local.get('azureRemainingProjectionUsd')
        self.cost_latch = self.path.with_name(self.path.name + '.cost-stop')
        if not read_only:
            held_lock(self.lock)
        try:
            info = self.path.lstat()
        except FileNotFoundError:
            raise Refusal('Missing Azure ledger; coordinator must restore or initialize explicitly') from None
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) != 0o600:
            raise Refusal('Unsafe Azure ledger file')
        self.data = json.loads(self.path.read_text())
        self.attempts = self.data['attempts']
        if (self.data['openingBalanceUsd'] != float(OPENING_BALANCE) or not isinstance(self.attempts, dict)
                or any(not GROUP.fullmatch(k) or v.get('state') not in ('active', 'settled', 'cleanup-failed') for k, v in self.attempts.items())):
            raise Refusal('Invalid Azure ledger')

    def write(self):
        if self.read_only:
            raise Refusal('Ledger write requires the Azure lock')
        held_lock(self.lock)
        atomic_json(self.path, self.data)

    def total(self, lineage=None):
        start = OPENING_BALANCE if lineage is None else Decimal(0)
        for entry in self.attempts.values():
            if lineage is not None and entry['lineage'] != lineage:
                continue
            amount = entry['reservation' if entry['state'] != 'settled' else 'booked']
            if type(amount) not in (int, float) or not math.isfinite(amount) or amount < 0:
                raise Refusal('Invalid Azure ledger amount; no ARM writes')
            start += Decimal(str(amount))
        return start

    def money(self, go, cap):
        settled = [e for e in self.attempts.values() if e['state'] == 'settled']
        selected = [e for e in settled if e.get('go') == go]
        booked = sum((Decimal(str(e['booked'])) for e in settled), OPENING_BALANCE)
        exposure = self.total()
        observed = [e['posted'] for e in self.attempts.values() if e.get('posted')]
        return dict(bookedUsd=float(booked), unsettledUsd=float(exposure - booked), headroomUsd=float(Decimal(str(cap)) - exposure),
                    postedUsd=sum(e.get('d3', e.get('d1', 0)) for e in observed), postedObservedAttempts=len(observed),
                    programAttempts=len(self.attempts), remainingWorkEstimateUsd=self.projection,
                    goBookedUsd=sum(e['booked'] for e in selected), goAttempts=sum(e.get('go') == go for e in self.attempts.values()))

    def reserve(self, seal, go, amounts):
        lineage = seal.get('retryOf', {}).get('originalSealId', go) if seal.get('lineageCapUsd') is not None else None
        program = Decimal(str(seal['programStopUsd']))
        if self.data.get('costStops') or self.cost_latch.exists():
            raise Refusal('Azure cost stop unresolved; coordinator action required')
        ceiling = program - self.total()
        if lineage is not None:
            ceiling = min(ceiling, Decimal(str(seal['lineageCapUsd'])) - self.total(lineage))
        extra = sum(Decimal(str(n)) for n in amounts)
        if extra >= ceiling:
            summary = self.money(go, program)
            raise Refusal(f'Azure cap stop: {money_text(summary)}; next reservation ${extra:.4f}; available ${ceiling:.4f}')
        return lineage, ceiling

    def notice(self, seal, go, amounts, number, phase, *, prior=0.):
        amounts = list(amounts)
        try:
            _, left = self.reserve(seal, go, [])
        except Refusal:
            left = Decimal(0)
        used = Decimal(str(prior))
        count = 0
        for amount in amounts:
            used += Decimal(str(amount))
            if used >= left:
                break
            count += 1
        return dict(session=number, phase=phase, guaranteedAttempts=count, plannedAttempts=len(amounts), mayStop=count < len(amounts))


def money_text(summary):
    estimate = summary['remainingWorkEstimateUsd']
    projection = 'unavailable' if estimate is None else f'${estimate:.4f}'
    return (f"booked ${summary['bookedUsd']:.4f}; unsettled ${summary['unsettledUsd']:.4f}; "
            f"posted ${summary['postedUsd']:.4f} ({summary['postedObservedAttempts']} of {summary['programAttempts']} "
            f"attempts observed; missing charges not included); headroom ${summary['headroomUsd']:.4f}; "
            f"remaining-work estimate {projection}")


def seals_reservations(seal):
    from gate_time import azure_booking
    return [azure_booking(plan['destroyGuardSeconds'] + RESERVE_TAIL) for plan in seal['timePlans']
            if not seal.get('retryOf') or not plan.get('bytesOnly')]


def check_seal_budget(local, seal, go):
    amounts = seals_reservations(seal)
    if not amounts:
        raise Refusal('Azure seal has no session to reserve')
    Ledger(local, read_only=True).reserve(seal, go, amounts[:1])


def _diagnostic_summary(archive):
    import tarfile
    summary = dict(partial=True)
    if not archive.is_file():
        return summary
    try:
        with tarfile.open(archive, 'r:gz') as stream:
            names = set(stream.getnames())
            summary['partial'] = 'results/recovery-diagnostic.json' in names
            if summary['partial']:
                marker = json.load(stream.extractfile('results/recovery-diagnostic.json'))
                if isinstance(marker, dict):
                    reason = marker.get('reason')
                    if isinstance(reason, str) and reason in REFUSAL_REASONS:
                        summary['reason'] = reason
                    stage = marker.get('stage')
                    if isinstance(stage, str) and stage in FETCH_STAGES:
                        summary['stage'] = stage
                    unit_result = marker.get('unitResult')
                    if isinstance(unit_result, str) and re.fullmatch(r'[a-z-]{1,32}', unit_result):
                        summary['unitResult'] = unit_result
                    if marker.get('limitsFallback') is True:
                        summary['limitsFallback'] = True
                    for key in ('compressedBytes', 'maxCompressedBytes', 'expandedBytes', 'maxExpandedBytes',
                                *LOG_COUNTS, 'execMainCode', 'execMainStatus'):
                        if type(marker.get(key)) is int and marker[key] >= 0:
                            summary[key] = marker[key]
            if 'results/session-status.json' in names:
                status = json.load(stream.extractfile('results/session-status.json'))
                if isinstance(status, dict):
                    clean = {k: status[k] for k in ('exitCode',) if type(status.get(k)) is int}
                    clean.update({k: status[k] for k in ('completedAllDeclaredRounds', 'dry') if type(status.get(k)) is bool})
                    if status.get('probe') in ('kept', 'rejected'):
                        clean['probe'] = status['probe']
                    if clean:
                        summary['sessionStatus'] = clean
    except (OSError, ValueError, KeyError, tarfile.TarError):
        pass
    return summary


def probe_result(archive, plan, note):
    import tarfile
    from gate_fly_guard import result_status
    from gate_time import probe_ids, probe_result as decide
    rule = plan.get('probe')
    status = result_status(archive, note, rule)
    if rule:
        with tarfile.open(archive, 'r:gz') as stream:
            records = {key: json.load(stream.extractfile(f'results/probe/{key}.json')) for key in probe_ids(rule)}
            reported = json.load(stream.extractfile('results/probe.json'))
        result = decide(records, rule)
        if result != reported or (status == 5) != (result['decision'] == 'rejected'):
            raise GuardFailure('guest probe disagrees with the sealed rule')
    return status


def finish(draw, ledger, note, summary, ended, code, *, cleanup_started=None):
    from gate_time import azure_booking
    elapsed = max(0, ended - draw.start)
    cost = max(azure_booking(elapsed), .16 if code == 6 else 0)
    posted = expected_posted(elapsed)
    atomic_json(note / 'cleanup-proof.json', summary)
    atomic_json(note / 'duration-cost.json', dict(lifetimeSeconds=elapsed, guardSeconds=draw.guard_seconds,
        tailSeconds=max(0, elapsed - draw.guard_seconds), totalEstimateWithReserveUsd=cost, cleanupOutstanding=False,
        withinSealedGuard=elapsed <= draw.guard_seconds,
        cleanupStartedSeconds=None if cleanup_started is None else cleanup_started - draw.start))
    atomic_json(note / 'expected-posted.json', dict(expectedPostedUsd=posted))
    ledger.attempts[draw.group].update(state='settled', booked=cost, expectedPosted=posted, ended=ended, exitCode=code)
    ledger.write()
    (note / 'cleanup-failed.json').unlink(missing_ok=True)
    mark(draw.guard, 'done')


def recover(az, ledger, baseline, out, *, coordinator=False):
    for group, entry in ledger.attempts.items():
        if entry['state'] == 'settled':
            continue
        if entry['state'] == 'cleanup-failed' and not coordinator:
            raise Refusal('Earlier cleanup not proven; run coordinator recovery')
        directory = entry['directory']
        if not re.fullmatch(r'gate-[A-Za-z0-9_.-]+', directory):
            raise Refusal('Invalid Azure recovery directory')
        note = out / directory / 'time/runs' / group
        guard = note / '.guard'
        if not (guard.is_dir() and guard.resolve() == guard and guard.stat().st_uid == os.getuid()):
            raise Refusal('Missing or unsafe recovery state; coordinator action required')
        draw = Draw(az, group, entry['region'], entry['epoch'], guard, entry['guard'], entry['cutoff'])
        state = json.loads((guard / 'watchdog.json').read_text())
        if state['subscriptionFingerprint'] != fingerprint(az.subscription):
            raise Refusal('Selected subscription differs from the recovery entry; no ARM write')
        ended = time.time()
        watched = guard / 'watchdog-proof.json'
        evidence = json.loads(watched.read_text()) if watched.is_file() else None
        proven = (isinstance(evidence, dict) and type(evidence.get('ended')) in (int, float) and
                  draw.start <= evidence['ended'] <= draw.deadline and
                  (evidence.get('summary') or {}).get('ourAppAbsent') is True)
        if not coordinator and ((guard / 'failed').exists() or ended >= draw.deadline and not proven):
            entry['state'] = 'cleanup-failed'
            ledger.write()
            raise Refusal('Earlier cleanup reserve exhausted without proof; run coordinator recovery')
        provisional = proven and evidence.get('provisional') is True
        if creation_unconfirmed(guard) and ended < draw.deadline:
            raise Refusal('Earlier group creation unconfirmed; wait for reservation and exact proof')
        try:
            with cleanup_lease(guard):
                with retire(guard):
                    pass
                draw.delete(max(time.time() + 135, draw.deadline))
                summary, _ = proof(az, baseline, ledger.attempts)
                finish(draw, ledger, note, summary, evidence['ended'] if proven and not provisional else time.time(), 4)
        except (AzureError, GuardFailure, OSError):
            entry['state'] = 'cleanup-failed'
            ledger.write()
            raise Refusal('Earlier cleanup not proven; coordinator action required') from None


def daily(az, ledger, baseline, *, live=()):
    summary, group = proof(az, baseline, ledger.attempts, live=live)
    if not ledger.read_only:
        ledger.data['lastDailyCheck'] = int(time.time())
        ledger.write()
    return summary, group


def authorize_dispatch(directory, go, dispatch, first):
    message = 'Azure attempt requires its live GO dispatch'
    try:
        live = int(os.environ['GATE_AZURE_LIVE_FD'])
        parent = int(os.environ['GATE_AZURE_PARENT'])
        token = os.pread(dispatch, 96, 0)
        info = os.fstat(dispatch)
        if (parent != os.getppid() or not stat.S_ISREG(info.st_mode) or info.st_nlink != 0 or
                len(token) != 96 or token[:64] != go.encode('ascii') or
                not stat.S_ISFIFO(os.fstat(live).st_mode) or select.select([live], [], [], 0)[0]):
            raise Refusal(message)
        binding = dict(dispatchSha256=hashlib.sha256(token).hexdigest(), parent=parent)
        authority = directory / 'time/azure-dispatch.json'
        if not first and json.loads(authority.read_text()) != binding:
            raise Refusal(message)
        return binding
    except (KeyError, OSError, ValueError, TypeError):
        raise Refusal(message) from None


def one_attempt(az, local, ledger, seal, go, directory, plan, group, region, inputs, number, draw_number, phase, fallback, context, dispatch):
    from gate_fly_guard import consume_go
    from gate_time import azure_booking
    if group in ledger.attempts or not GROUP.fullmatch(group) or region not in REGIONS:
        raise Refusal('Azure attempt already exists or has an invalid shape')
    first = not (directory / 'time/launch-started.json').is_file()
    binding = authorize_dispatch(directory, go, dispatch, first)
    key = (go, number, phase, draw_number)
    if any((e.get('go'), e.get('session'), e.get('phase'), e.get('draw')) == key and
           (e.get('fallbackOf') is None) == (fallback is None) for e in ledger.attempts.values()):
        raise Refusal('Azure logical attempt already exists')
    baseline = local['azureBaseline']
    recover(az, ledger, baseline, directory.parent)
    preflight(az, baseline, region, version=seal['platform']['azCliVersion'], groups=ledger.attempts)
    check_context(*context)
    reserve = azure_booking(plan['destroyGuardSeconds'] + RESERVE_TAIL)
    lineage, _ = ledger.reserve(seal, go, [reserve])
    note = directory / 'time/runs' / group
    note.mkdir(mode=0o700, parents=True)
    guard = note / '.guard'
    guard.mkdir(mode=0o700)
    start = int(time.time())
    draw = Draw(az, group, region, start, guard, plan['destroyGuardSeconds'], plan['measurementCutoffSeconds'])
    (guard / 'local-path').write_text(str(Path(os.environ['CHRYSALIS_GATE_LOCAL']).resolve()))
    atomic_json(guard / 'watchdog.json', dict(group=group, subscriptionFingerprint=fingerprint(az.subscription), region=region,
        start=start, guard=draw.guard_seconds, cutoff=draw.cutoff, driverPid=os.getpid(), driverBirth=process_id(os.getpid()),
        distro=context[0], user=context[1]))
    if first:
        if os.environ.get('GATE_FIRST_GO') != go:
            raise Refusal('First launch authority missing')
        consume_go(directory / 'seal.json', go, dispatch_fd=dispatch)
        atomic_json(directory / 'time/azure-dispatch.json', binding)
    elif os.environ.get('GATE_FIRST_GO') or json.loads((directory / 'time/launch-started.json').read_text()) != {'sealId': go}:
        raise Refusal('Go already consumed or launch authority changed')
    ledger.attempts[group] = dict(state='active', go=go, session=number, draw=draw_number, phase=phase, region=region,
        fallbackOf=fallback, epoch=start, guard=draw.guard_seconds, cutoff=draw.cutoff, reservation=reserve, lineage=lineage,
        directory=directory.name, batch=seal.get('batch'))
    ledger.write()  # This durable entry precedes the keeper, key generation and first ARM write.
    atomic_json(note / 'machine.json', dict(provider='azure', platform='azure', machineClass=SIZE, region=region,
                machineCreatedUnix=start, guardSeconds=draw.guard_seconds, cutoffSeconds=draw.cutoff, steal='not observable on Azure'))
    code = 2
    try:
        arm_native(Path(__file__).resolve(), guard, draw.deadline, *context)
        draw.setup(inputs)
        try:
            partial = run_channel(draw, inputs, note)
        except Exception:
            if (note / 'results.tar.gz').is_file():
                atomic_json(note / 'diagnostic-only.json', _diagnostic_summary(note / 'results.tar.gz'))
            if draw.guest_error is not None:
                atomic_json(note / 'guest-error.json', draw.guest_error)
            raise
        if partial:
            atomic_json(note / 'diagnostic-only.json', _diagnostic_summary(note / 'results.tar.gz'))
        else:
            draw.step = 'probe-result'
            code = probe_result(note / 'results.tar.gz', plan, note)
    except AzureError as exc:
        code = 6 if isinstance(exc, CapacityUnavailable) else 2
        step, seconds = _fixed_step(draw), round(max(0.0, time.time() - draw.start), 1)
        log(f'Azure attempt: {exc}; last Azure call: {az.last_call or "none"}; step {step}; +{seconds:.1f}s')
        if not os.path.lexists(guard / 'guest-ready'):
            atomic_json(note / 'pre-ready-failure.json', {'class': _exc_class(exc), 'step': step, 'seconds': seconds})
    except (GuardFailure, HTTPException, KeyError, OSError, RuntimeError, TarError, TypeError, ValueError) as exc:
        cls, step, seconds = _exc_class(exc), _fixed_step(draw), round(max(0.0, time.time() - draw.start), 1)
        log(f'Azure attempt invalid: {cls}; step {step}; +{seconds:.1f}s')
        if not os.path.lexists(guard / 'guest-ready'):
            atomic_json(note / 'pre-ready-failure.json', {'class': cls, 'step': step, 'seconds': seconds})
    finally:
        cleanup_started = time.time()
        try:
            if creation_unconfirmed(guard):
                raise Refusal('Group PUT unconfirmed; keeper and recovery retain reservation')
            with cleanup_lease(guard):
                with retire(guard):
                    pass
                # Do not leave early merely because the service accepted DELETE.
                if (guard / 'group-put-sent').exists():
                    draw.delete(draw.deadline)
                independent, _ = proof(az, baseline, ledger.attempts)
                ended = time.time()
                if ended > draw.deadline:
                    raise Refusal('Cleanup reserve exhausted without timely proof')
                finish(draw, ledger, note, independent, ended, code, cleanup_started=cleanup_started)
        except (AzureError, GuardFailure, OSError):
            mark(guard, 'urgent')
            if time.time() >= draw.deadline:
                ledger.attempts[group]['state'] = 'cleanup-failed'
                ledger.write()
            atomic_json(note / 'cleanup-failed.json', dict(ourAppAbsent=False, reservationUsd=reserve))
            alert(guard, 'CLEANUP STOP: absence unproven; coordinator action required')
            code = 4
    return code


def watchdog(guard):
    from gate_bundle import checked_azure_local
    ready = HERE.parent.parent
    if guard.name != '.guard' or guard.resolve() != guard or guard.parents[3] != ready:
        raise GuardFailure('Invalid keeper state path')
    info = guard.stat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
        raise GuardFailure('Unsafe keeper state')
    state = json.loads((guard / 'watchdog.json').read_text())
    if (state['distro'], state['user']) != wsl_context() or state['group'] != guard.parent.name:
        raise GuardFailure('Keeper returned from the wrong WSL context')
    import gate
    # WSL's new process does not inherit the launcher's sanitized environment.
    os.environ['CHRYSALIS_GATE_LOCAL'] = (guard / 'local-path').read_text()
    local = checked_azure_local(gate.read_seal(ready))
    start, guard_seconds = state['start'], state['guard']
    stop = start + guard_seconds
    mark(guard, 'ready')
    while time.time() < stop - WATCH_MARGIN and not (guard / 'done').exists():
        if (guard / 'urgent').exists() and not creation_unconfirmed(guard):
            break
        time.sleep(3)
    az = Az(Path(local['azureCli']))
    while not (guard / 'done').exists() and time.time() < stop + RESERVE_TAIL:
        if time.time() >= stop + ALERT_TAIL and not (guard / 'escalated').exists():
            mark(guard, 'escalated')
            alert(guard, 'CLEANUP ALERT: absence not proven; retries continuing within reserve')
        try:
            if not az.subscription and fingerprint(az.pin()) != state['subscriptionFingerprint']:
                az.subscription = None
                raise GuardFailure('Selected subscription changed; no ARM write')
            draw = Draw(az, state['group'], state['region'], start, guard, guard_seconds, state['cutoff'])
            with ExitStack() as leases:
                exclusive, fenced = True, True
                try:
                    leases.enter_context(cleanup_lease(guard))
                except BlockingIOError:
                    if time.time() < stop:
                        raise  # Give the current cleaner until G to finish first.
                    exclusive = False  # An open cleanup lease is not a liveness proof.
                try:
                    with retire(guard):
                        pass
                except BlockingIOError:
                    if exclusive:
                        stop_paused_driver(state)  # Only an exclusive watcher may signal.
                        raise
                    fenced = False  # Deletion is safe; absence cannot yet be final.
                if (guard / 'done').exists():
                    return
                draw.delete(min(draw.deadline, time.time() + 22), log_attempt=lambda _: None)
                unconfirmed = not fenced or creation_unconfirmed(guard)
                if not unconfirmed or time.time() >= draw.deadline - WATCH_MARGIN:
                    summary, _ = proof(az, local['azureBaseline'], [draw.group])
                    ended = time.time()
                    if ended < draw.deadline:
                        atomic_json(guard / 'watchdog-proof.json', dict(ended=ended, summary=summary, provisional=unconfirmed))
                        if not unconfirmed:
                            return
                        if not (guard / 'provisional-reported').exists():
                            alert(guard, 'provisional absence, re-proof owed at next GO')
                            mark(guard, 'provisional-reported')
        except BlockingIOError:
            pass  # Try again within the bounded reserve.
        except (AzureError, GuardFailure, OSError):
            (guard / 'watchdog-proof.json').unlink(missing_ok=True)
        time.sleep(3)
    if not (guard / 'done').exists() and not (guard / 'watchdog-proof.json').is_file():
        alert(guard, 'CLEANUP FAILURE: reserve exhausted without proof; coordinator recovery required')
        mark(guard, 'failed')


def reconcile_and_pre_go(local, directory, seal, go):
    ledger = Ledger(local)
    az = Az(Path(local['azureCli']))
    az.pin()
    recover(az, ledger, local['azureBaseline'], directory.parent)
    if not ledger.data['lastDailyCheck'] or time.time() - ledger.data['lastDailyCheck'] >= 86400:
        daily(az, ledger, local['azureBaseline'])
    ledger.reserve(seal, go, seals_reservations(seal))  # One full reservation per session.


def daily_cost(directory, local):
    from gate_bundle import checked_go_directory
    import gate
    directory = checked_go_directory(directory, gate.REPO / 'benchmarks/out')
    go = (directory / 'seal.id').read_text().strip()
    az = Az(Path(local['azureCli']))
    az.pin()
    ledger = Ledger(local, read_only=True)
    active = [group for group, entry in ledger.attempts.items() if entry['state'] == 'active' and
              time.time() < entry['epoch'] + entry['guard'] + RESERVE_TAIL]
    contended = False
    try:
        held_lock(Path(local['azureLock']))
    except Refusal:
        with Path(local['azureLock']).open('a') as stream:
            try:
                fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                contended = True
        if not contended:
            raise
    if contended:
        _, baseline_group = proof(az, local['azureBaseline'], ledger.attempts, live=active)
    else:
        ledger = Ledger(local)
        _, baseline_group = daily(az, ledger, local['azureBaseline'])
    if contended and any(ledger.attempts[group]['go'] == go for group in active):
        log('Azure inventory verified. No +1 or +3 day Azure cost readback due')
        return 0
    target = [entry for entry in ledger.attempts.values() if entry['go'] == go]
    if not target or any(entry['state'] != 'settled' for entry in target):
        raise GuardFailure('Cost readback requires a completed Azure go')
    now = time.time()
    age = now - max(entry['ended'] for entry in target)
    phases = [phase for day, phase in ((1, 'd1'), (3, 'd3')) if age >= day * 86400 and not all(phase in e.get('posted', {}) for e in target)]
    if not phases:
        log('No +1 or +3 day Azure cost readback due')
        return 0
    try:
        result = cost_readback(az, baseline_group, ledger.attempts, min(e['epoch'] for e in ledger.attempts.values()), now)
    except AzureError as exc:
        if str(exc) == 'TooManyRequests':
            raise Refusal('Azure cost readback throttled; no posted cost or cost stop written; recheck later') from None
        raise
    result.update(bookedUsd=float(ledger.total()), postedUsd=sum(result['posted'].values()))
    if contended:
        if result['stops']:
            # This write is separate from the ledger: its current writer cannot lose it.
            atomic_json(ledger.cost_latch, dict(stops=result['stops']))
            raise Refusal('Azure cost stop: ' + '; '.join(result['stops']))
        raise Refusal('Azure cost readback passed; recording due check requires the Azure lock')
    for phase in phases:
        atomic_json(directory / f'cost-{phase}.json', result)
        for group, entry in ledger.attempts.items():
            if entry['go'] == go:
                entry.setdefault('posted', {})[phase] = result['posted'][group]
    if result['stops']:
        ledger.data['costStops'] = result['stops']
    ledger.write()
    if result['stops']:
        raise Refusal('Azure cost stop: ' + '; '.join(result['stops']))
    log(f"Azure cost: ${result['bookedUsd']:.4f} booked; ${result['postedUsd']:.4f} posted")
    return 0


def launch(args):
    from gate_bundle import checked_azure_local
    from gate_fly_guard import verified_launch_inputs
    from gate_azure_payload import verify_inputs
    import gate
    ready = args.seal.resolve()
    directory = ready.parent
    seal = gate.read_seal(directory)
    local = checked_azure_local(seal)
    held_lock(Path(local['azureLock']))
    dispatch = int(os.environ.get('GATE_RETRY_FD', '0'))
    info = verified_launch_inputs(HERE, ready, dispatch_fd=dispatch)
    if not 0 <= args.session < len(seal['timePlans']) or args.draw < 1 or not GROUP.fullmatch(args.approved):
        raise Refusal('Invalid Azure attempt identifier')
    plan = seal['timePlans'][args.session]
    relative = f'input/{"repeat" if args.phase == "repeat" else "session"}-{args.session}.tar.gz'
    if os.environ.get('GATE_BUNDLE') != relative or args.draw > plan.get('probe', {}).get('maxDraws', 1):
        raise Refusal('Azure attempt differs from sealed session or draw cap')
    if not args.dry_run:
        authorize_dispatch(directory, info['go'], dispatch, not (directory / 'time/launch-started.json').is_file())
    inputs = verify_inputs(directory, seal, plan, info)
    if args.region == 'eastus' and plan['region'] != 'eastus':
        ledger = Ledger(local)
        previous = ledger.attempts.get(args.fallback_of, {})
        if (previous.get('exitCode'), previous.get('session'), previous.get('draw'), previous.get('go'), previous.get('phase')) != (
                6, args.session, args.draw, info['go'], args.phase) or previous.get('region') != 'eastus2' or previous.get('fallbackOf') is not None:
            raise Refusal('East US fallback requires the same depleted primary draw')
    elif args.region != plan['region'] or args.fallback_of is not None:
        raise Refusal('Unsealed region or unexpected fallback')
    if args.dry_run:
        log(f"Azure attempt reservation ${seals_reservations(dict(seal, timePlans=[plan]))[0]:.4f}")
        return 0
    az = Az(Path(local['azureCli']))
    az.pin()
    return one_attempt(az, local, Ledger(local), seal, info['go'], directory, plan, args.approved, args.region,
                       inputs, args.session, args.draw, args.phase, args.fallback_of, args.context, dispatch)


def standalone(command, directory):
    import gate
    local = gate.local_config('azure')
    path = Path(local['azureLock'])
    with path.open('a') as stream:
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            if command == 'cost':
                return daily_cost(directory, local)
            raise Refusal('Azure lock held; live go not interrupted') from None
        owned = stream.fileno() == 9
        if not owned:
            os.dup2(stream.fileno(), 9, inheritable=False)
        try:
            if command == 'cost':
                return daily_cost(directory, local)
            az = Az(Path(local['azureCli']))
            az.pin()
            out = gate.REPO / 'benchmarks/out'
            if command == 'initialize-ledger':
                ledger_path = Path(local['azureLedger'])
                if os.path.lexists(ledger_path) or any(out.glob('gate-*/time/runs/chrysalis-gate-*')):
                    raise Refusal('Azure ledger or historical attempts exist; restore the original ledger')
                proof(az, local['azureBaseline'])
                atomic_json(ledger_path, dict(openingBalanceUsd=float(OPENING_BALANCE), attempts={}, lastDailyCheck=None))
                return 0
            ledger = Ledger(local)
            recover(az, ledger, local['azureBaseline'], out, coordinator=True)
            daily(az, ledger, local['azureBaseline'])
            log('Azure recovery complete; exact baseline restored')
            return 0
        finally:
            if not owned:
                os.close(9)


def main(argv):
    if len(argv) == 2 and argv[0] == '--watchdog':
        watchdog(Path(argv[1]))
        return 0
    if argv in (['recover'], ['initialize-ledger']) or len(argv) == 2 and argv[0] == 'cost':
        return standalone(argv[0], Path(argv[1]) if len(argv) == 2 else None)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dry-run', action='store_true')
    parser.add_argument('--approved', required=True)
    parser.add_argument('seal', type=Path)
    parser.add_argument('--context', nargs=2, required=True)
    parser.add_argument('--session', type=int, required=True)
    parser.add_argument('--draw', type=int, required=True)
    parser.add_argument('--phase', choices=('first', 'repeat'), required=True)
    parser.add_argument('--region', choices=REGIONS, required=True)
    parser.add_argument('--fallback-of')
    return launch(parser.parse_args(argv))


if __name__ == '__main__':
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(2))
    os.umask(0o077)
    try:
        raise SystemExit(main(sys.argv[1:]))
    except Refusal as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(4) from None
    except (GuardFailure, AzureError, HTTPException, KeyError, OSError, RuntimeError, TarError, TypeError, ValueError) as exc:
        print(f'Azure launcher failed: {_exc_class(exc)}', file=sys.stderr)
        raise SystemExit(2) from None
