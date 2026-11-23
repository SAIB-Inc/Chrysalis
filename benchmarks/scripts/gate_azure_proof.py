#!/usr/bin/env python3
"""Read-only Azure subscription proof; only raw ARM REST, never an SDK command."""
import hashlib
import json
import math
from datetime import datetime, timedelta, timezone
import os
from pathlib import Path
import re
import subprocess
import time
from urllib.parse import urlsplit

ARM = 'https://management.azure.com'
GUID = re.compile(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}', re.I)
GROUP = re.compile(r'chrysalis-gate-[0-9]{8}t[0-9]{6}-[a-f0-9]{8}')
TRIAL_GROUP = re.compile(r'chrysalis-probe-[0-9a-f]{16}-[1-4]')
TRIAL_CAP = 0.9725
TRIAL_LAST_DAY = 20261119
ONCE = (0,)
READ_WAITS = (2, 5)
TRANSIENT_READ_ERRORS = ('timeout', 'TooManyRequests', 'ServiceUnavailable', 'InternalServerError', 'RetryableError')
# REST uses the raw azure.cli.core.util.send_raw_request path instead of an Azure
# Resource Management SDK client, so its provider auto-registration policy is
# absent. The command allowlist below is the enforcement, not a config guess.
AZ_ENV = dict(os.environ, AZURE_LOGGING_ENABLE_LOG_FILE='false', AZURE_CORE_COLLECT_TELEMETRY='false',
              AZURE_EXTENSION_USE_DYNAMIC_INSTALL='no', AZURE_CORE_ONLY_SHOW_ERRORS='true')


def storage_account(group):
    return 'chry' + hashlib.sha256(group.encode('ascii')).hexdigest()[:16]


class AzureError(RuntimeError):
    pass


def _call_label(args):
    if args[:1] != ['rest'] or '--method' not in args or '--url' not in args:
        return ' '.join(args[:2])
    method = args[args.index('--method') + 1]
    path = urlsplit(args[args.index('--url') + 1]).path
    if match := re.search(r'/runCommands/(init|kick|status-(?:0[0-9]|1[0-8])|push-[12])$', path):
        return f'{method} runCommands {match[1]}'
    tail = path.split('/providers/', 1)[1].split('/') if '/providers/' in path else path.strip('/').split('/')[2:]
    kind = (tail[1] if len(tail) > 1 else 'providers') if '/providers/' in path else (tail[0] if tail else 'arm')
    return f'{method} {kind if re.fullmatch(r"[A-Za-z]+", kind) else "arm"}'


class Az:
    def __init__(self, executable):
        self.executable = executable
        self.subscription = None
        self.last_call = None

    def _call(self, args, *, timeout=15, fence_fd=None, stdin_body=None):
        # account show accesses only the CLI's local selected subscription. ARM
        # commands MUST use raw REST; no fallback or SDK operation is permitted.
        if args not in (['account', 'show', '--query', 'id'], ['version']) and args[:1] != ['rest']:
            raise AzureError('SDK/provider auto-registration command path forbidden')
        label = self.last_call = _call_label(args)
        command = args + ['--only-show-errors', '-o', 'json']
        if self.subscription and args[:1] == ['rest']:
            command += ['--subscription', self.subscription]
        try:
            p = subprocess.run([self.executable, *command], capture_output=True, stdin=subprocess.DEVNULL if stdin_body is None else None, input=stdin_body,
                               env=AZ_ENV, timeout=timeout, check=False, pass_fds=() if fence_fd is None else (fence_fd,))
        except subprocess.TimeoutExpired:
            print(f'Azure call {label} timed out', flush=True)
            raise AzureError('timeout') from None
        if p.returncode:
            for code in ('MissingSubscriptionRegistration', 'NoRegisteredProviderFound', 'SkuNotAvailable',
                         'AllocationFailed', 'OverconstrainedAllocationRequest', 'ZonalAllocationFailed',
                         'ResourceGroupNotFound', 'ResourceNotFound', 'PrincipalNotFound', 'RoleAssignmentExists',
                         'AuthorizationFailed', 'LinkedAuthorizationFailed', 'Conflict', 'AnotherOperationInProgress',
                         'TooManyRequests', 'RetryableError', 'InternalServerError', 'ServiceUnavailable'):
                if re.search(rb'\b' + code.encode() + rb'\b', p.stderr + p.stdout, re.I):
                    raise AzureError(code)
            if re.search(rb'\btoo many requests\b', p.stderr + p.stdout, re.I):
                raise AzureError('TooManyRequests')
            # az rest surfaces status as text; Azure can return empty entity bodies.
            for status, key in ((404, 'ResourceNotFound'), (409, 'Conflict'), (429, 'TooManyRequests'), (503, 'ServiceUnavailable')):
                if str(status).encode() in p.stderr and re.search(rb'(?i)status|reason|client error|server error', p.stderr):
                    raise AzureError(key)
            raise AzureError(f'{args[0]} command failed (exit {p.returncode}; details suppressed)')
        try:
            return json.loads(p.stdout) if p.stdout.strip() else None
        except ValueError:
            raise AzureError('Azure response not JSON') from None

    def pin(self):
        self.subscription = self._call(['account', 'show', '--query', 'id'])
        if not isinstance(self.subscription, str) or not GUID.fullmatch(self.subscription):
            raise AzureError('No selected Azure subscription')
        return self.subscription

    def version(self):
        value = self._call(['version'])
        version = value.get('azure-cli') if isinstance(value, dict) else None
        if not isinstance(version, str) or not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', version):
            raise AzureError('Azure CLI version unavailable')
        return version

    def rest(self, method, path, *, body=None, query=None, timeout=15, fence_fd=None, stdin_body=None, waits=(), until=None):
        url = urlsplit(ARM + path)
        if url.netloc != 'management.azure.com' or not path.startswith(f'/subscriptions/{self.subscription}/') or \
                not GUID.fullmatch(self.subscription or '') or '\\' in url.path or '/..' in url.path or '%' in url.path:
            raise AzureError('Refusing ARM path outside pinned subscription')
        if method not in ('get','put','delete'):
            cost = url.path.lower()==f'/subscriptions/{self.subscription}/providers/microsoft.costmanagement/query'.lower()
            key_path = re.fullmatch(rf'/subscriptions/{self.subscription}/resourceGroups/({GROUP.pattern})/providers/Microsoft.Storage/storageAccounts/([a-z0-9]+)/listKeys', url.path)
            scoped_keys = key_path and storage_account(key_path[1]) == key_path[2]
            if method!='post' or not (cost or scoped_keys):
                raise AzureError('Refusing unauthorized POST; provider registration prohibited')
        if url.path.rstrip('/').rsplit('/', 1)[-1].lower() in ('register', 'unregister'):
            raise AzureError('Refusing provider registration path')
        if method in ('put', 'delete'):
            if waits:
                raise AzureError('Refusing retry on ARM mutation')
            resource = url.path.split('/', 5)
            if len(resource) < 5 or resource[3] != 'resourceGroups' or not GROUP.fullmatch(resource[4]):
                raise AzureError('ARM mutation outside an owned run group')
            if method == 'delete' and len(resource) != 5:
                raise AzureError('Only whole run groups may be deleted')
        args = ['rest', '--method', method, '--url', ARM + path]
        if stdin_body is not None:
            if body or (method != 'put' and not (method == 'post' and cost)):
                raise AzureError('Unexpected piped ARM body')
            args += ['--body','@-']
        elif body:
            args += ['--body','@'+str(body)]
        if query:
            args += ['--query', query]
        for wait in (*waits, None):
            if until is not None and time.time() >= until:
                raise AzureError('timeout')
            call_timeout = timeout if until is None else min(timeout, max(1.0, until - time.time()))
            try:
                return self._call(args, timeout=call_timeout, fence_fd=fence_fd, stdin_body=stdin_body)
            except AzureError as exc:
                if wait is None or str(exc) not in TRANSIENT_READ_ERRORS or (until is not None and time.time() + wait >= until):
                    raise
                if wait:
                    time.sleep(wait)


def fingerprint(*values):
    return hashlib.sha256('\0'.join(v.lower() for v in values).encode()).hexdigest()


WATCHER = 'microsoft.network/networkwatchers'
COUNTS = {'disks': ('Compute','disks','2024-03-02'), 'snapshots': ('Compute','snapshots','2024-03-02'), 'storageAccounts':('Storage','storageAccounts','2023-05-01'),
          **{k: ('Network', r, '2024-05-01') for k,r in {'publicIPs':'publicIPAddresses','NICs':'networkInterfaces',
                                                        'NSGs':'networkSecurityGroups'}.items()}}


def inventory(az, *, live=(), until=None):
    scope = '/subscriptions/' + az.subscription

    def listing(path, selector):
        page = az.rest('get', scope + path, query='{items:value[].' + selector + ',paginated:nextLink!=`null`}', waits=READ_WAITS, until=until)
        if page.get('paginated') is not False or not isinstance(page.get('items'), list):
            raise AzureError('Inventory response incomplete')
        return page['items']

    def group_name(resource):
        match = re.match(re.escape(scope) + r'/resourceGroups/([^/]+)(?:/|$)', resource['id'], re.I)
        return match[1].lower() if match else ''

    groups = [g for g in listing('/resourceGroups?api-version=2021-04-01', '{id:id,location:location}') if group_name(g) not in live]
    items = [r for r in listing('/resources?api-version=2021-04-01', '{id:id,type:type,location:location}') if group_name(r) not in live]
    counts = {}
    for resource, (provider, kind, api) in COUNTS.items():
        path = f'{scope}/providers/Microsoft.{provider}/{kind}?api-version={api}'
        if live:
            # The typed endpoints may expose a soon-to-disappear resource before /resources does.
            counts[resource] = sum(group_name(r) not in live for r in listing(path[len(scope):], '{id:id}'))
        else:
            page = az.rest('get', path, query='{count:length(value),paginated:nextLink!=`null`}', waits=READ_WAITS, until=until)
            if page.get('paginated') is not False or type(page.get('count')) is not int:
                raise AzureError('Typed inventory response incomplete')
            counts[resource] = page['count']
    watchers = [r for r in items if r['type'].lower() == WATCHER and r['location'] == 'southeastasia']
    group_ok = len(groups) == 1 and groups[0]['location'] == 'southeastasia'
    if not (group_ok and len(items) == len(watchers) == 1 and all(n == 0 for n in counts.values())
            and group_name(groups[0]) and group_name(groups[0]) == group_name(items[0])):
        raise AzureError('Orphan group or resource: exact baseline shape not restored')
    return dict(groupCount=1, resourceCount=1, soleGroupInSoutheastAsia=True, southeastAsiaWatchers=1,
                leftovers=counts, identitySha256=fingerprint(az.subscription, groups[0]['id'], items[0]['id'])), group_name(groups[0])


def run_role_count(az, groups, *, until=None):
    scope = '/subscriptions/' + az.subscription
    if any(not GROUP.fullmatch(group) for group in groups):
        raise AzureError('Invalid run group for role proof')
    page = az.rest('get', scope + '/providers/Microsoft.Authorization/roleAssignments?api-version=2022-04-01',
                   query='{scopes:value[].properties.scope,expected:length(value),paginated:nextLink!=`null`}', waits=READ_WAITS, until=until)
    scopes = page['scopes']
    if page.get('paginated') is not False or page['expected'] != len(scopes) or any(not isinstance(x, str) for x in scopes):
        raise AzureError('Role inventory incomplete')
    roots = [f'{scope}/resourceGroups/{group}'.lower() for group in groups]
    return sum(any(s.lower() == root or s.lower().startswith(root + '/') for root in roots) for s in scopes)


def proof(az, baseline, groups=(), *, live=(), until=None):
    current, baseline_group = inventory(az, live=live, until=until)
    original = json.loads(Path(baseline).read_text())
    original.get('leftovers', {}).setdefault('storageAccounts', 0)
    roles = run_role_count(az, [group for group in groups if group not in live], until=until)
    if current != original or roles:
        raise AzureError('Exact baseline identity differs or run roles remain')
    summary = {key: value for key, value in current.items() if key != 'identitySha256'}
    return dict(summary, restored=True, ourAppAbsent=True, remainingRunRoleAssignments=roles), baseline_group


def expected_posted(seconds):
    minutes = math.ceil(seconds / 60)
    started_hours = max(1, math.ceil(seconds / 3600))
    return round(.484 * minutes / 60 + (.005 + .0033) * started_hours + .01, 4)


def cost_readback(az, baseline_group, attempts, first, now):
    start = datetime.fromtimestamp(first, timezone.utc).date()
    end = datetime.fromtimestamp(now, timezone.utc).date() + timedelta(days=1)
    payload = dict(type='ActualCost', timeframe='Custom', timePeriod={'from': f'{start}T00:00:00Z', 'to': f'{end}T00:00:00Z'},
        dataset=dict(granularity='Daily', aggregation={'totalCost': dict(name='PreTaxCost', function='Sum')},
                     grouping=[dict(type='Dimension', name='ResourceGroup')]))
    response = az.rest('post', f'/subscriptions/{az.subscription}/providers/Microsoft.CostManagement/query?api-version=2025-03-01',
                       stdin_body=json.dumps(payload).encode(),
                       query='properties.{columns:columns[].name,rows:rows,paginated:nextLink!=`null`}', waits=(60, 120))
    columns = response.get('columns', []) if isinstance(response, dict) else []
    if (not columns or len(columns) != 4 or set(columns) != {'PreTaxCost', 'UsageDate', 'ResourceGroup', 'Currency'}
            or response.get('paginated') is not False or not isinstance(response.get('rows'), list)):
        raise AzureError('ActualCost response unsupported or incomplete')
    posted = dict.fromkeys(attempts, 0.)
    other_count, other_total, trial_count, trial_total, unexpected = 0, 0., 0, 0., False
    for row in response['rows']:
        if not isinstance(row, list) or len(row) != len(columns):
            raise AzureError('ActualCost row incomplete')
        x = dict(zip(columns, row))
        group, cost = x['ResourceGroup'], x['PreTaxCost']
        day = x['UsageDate']
        if (group is not None and not isinstance(group, str) or type(cost) not in (int, float) or not math.isfinite(cost)
                or x['Currency'] != 'USD' or type(day) is not int
                or not int(start.strftime('%Y%m%d')) <= day < int(end.strftime('%Y%m%d'))):
            raise AzureError('ActualCost row unsupported')
        group = (group or '').lower()
        if group in posted:
            posted[group] += cost
        elif TRIAL_GROUP.fullmatch(group) and day <= TRIAL_LAST_DAY:
            trial_count += 1
            trial_total += cost
        else:
            other_count += 1
            other_total += cost
            unexpected |= group != baseline_group
    stops = ['charge outside run groups and exact baseline'] if unexpected or trial_total > TRIAL_CAP else []
    for group, cost in posted.items():
        entry = attempts[group]
        settled = entry['state'] == 'settled'
        if cost > entry['booked' if settled else 'reservation']:
            stops.append('posted exceeds booking')
        if settled and cost > entry['expectedPosted'] * 1.20:
            stops.append('posted exceeds expected cost by more than 20%')
    return dict(posted=posted, otherRows=other_count, otherUsd=other_total,
                trialRows=trial_count, trialUsd=trial_total, stops=sorted(set(stops)))
