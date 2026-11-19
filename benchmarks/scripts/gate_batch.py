"""Batch review and carry-forward use sealed rules, not changed archive hashes."""
import fnmatch
import math
import re

import gate
import gate_prepare as prepared
import gate_schedule as schedules
from gate_time import require

BATCH = prepared.BATCH_MODES
BUILD_RANGE = {'commit', 'treeId', 'dllSha256s', 'previousAlpha'}
PC_FIELDS = {'platform', 'payloadManifests', 'files', 'programStopUsd', 'lineageCapUsd'}


def by_id(rows):
    result = {row['id']: row for row in rows}
    require(len(result) == len(rows), 'Duplicate sealed row')
    return result


def steady_build(side):
    return {key: value for key, value in side.items() if key not in BUILD_RANGE}


def steady_row(row):
    return dict({key: value for key, value in row.items() if key not in ('candidate', 'sides')},
                sides={role: steady_build(side) for role, side in row['sides'].items()})


def session_products(seal, directory):
    """Use row and side to compare auxiliary client files when opaque build IDs change."""
    stable, products = {}, {}
    for number, plan in enumerate(seal['timePlans']):
        executable = {}
        members = prepared.archive_members(directory / f'input/session-{number}.tar.gz', executable=executable)
        dlls = prepared.closure_names(plan)
        require(all(members.get(name) == sha for name, sha in dlls.items()) and
                {name for name in members if name.startswith('clients/') and name.endswith('.dll')} == set(dlls),
                'Unsealed DLL in session')
        for row in plan['rows']:
            for role, side in row['sides'].items():
                prefix = f'clients/{schedules.build_id(side)}/'
                value = {name.removeprefix(prefix): (sha, executable[name]) for name, sha in members.items()
                         if name.startswith(prefix) and name not in dlls}
                key = (row['id'], role)
                require(products.setdefault(key, value) == value, 'Inconsistent auxiliary build files')
        for name, sha in members.items():
            if not name.startswith('clients/') and name not in ('plan.json', 'prelude.json', 'bundle-manifest.json'):
                value = (sha, executable[name])
                require(stable.setdefault(name, value) == value, 'Inconsistent static session member')
    return stable, products


def compare_suite(old_dir, new_dir, old, new):
    """AZ3 may expose exact suite build members in suite.json's buildSha256s."""
    name = 'input/suite.tar.gz'
    require((name in old['files']) == (name in new['files']), 'Suite archive added or removed')
    if name not in new['files']:
        return
    modes = [{}, {}]
    previous, current = [prepared.archive_members(directory / name, executable=executable)
                         for directory, executable in zip((old_dir, new_dir), modes)]
    require(modes[0] == modes[1], 'Suite executable modes changed')
    metadata = [prepared.json_file(directory / 'suite.json') for directory in (old_dir, new_dir)]
    def without_builds(value):
        return {key: item for key, item in value.items() if key != 'buildSha256s'}
    require(without_builds(metadata[0]) == without_builds(metadata[1]) and previous.keys() == current.keys(), 'Suite recipe or member set changed')
    for members, plan in zip((previous, current), metadata):
        hashes = plan.get('buildSha256s', {})
        require(all(member.startswith('suite/build/') and members.get(member) == sha for member, sha in hashes.items()),
                'Invalid sealed suite build members')
    require(metadata[0].get('buildSha256s', {}).keys() == metadata[1].get('buildSha256s', {}).keys() and
            all(previous[member] == sha for member, sha in current.items() if member not in metadata[1].get('buildSha256s', {})),
            'Non-build suite member changed')


def seal_diff(reviewed, new, approvals):
    before, after = gate.read_seal(reviewed), gate.read_seal(new)
    seal_id = gate.digest(reviewed / 'seal.json')
    verdicts = [prepared.json_file(path) for path in approvals]
    require(len(verdicts) == 2 and {verdict.get('seat') for verdict in verdicts} == {'astra', 'opus'} and
            all(verdict == dict(seat=verdict['seat'], verdict='APPROVED', sealId=seal_id) for verdict in verdicts),
            'Reference needs two independent approvals for its exact seal ID')
    for seal in (before, after):
        require(seal.get('provider') == 'azure' and seal.get('mode') == BATCH.get(seal.get('kind')) and
                seal.get('kind') in BATCH and not {'retryOf'} & seal.keys() and not seal.get('noiseRecords'),
                'One-seat comparison requires a batch-leg seal')
        batch_areas(seal)
        require(isinstance(seal.get('rows'), list) and seal['rows'], 'Empty batch rows')
    derived = {'rows', 'timePlans', 'bands', 'canonical', 'files', 'batchAreas'}
    require({key: value for key, value in before.items() if key not in derived} ==
            {key: value for key, value in after.items() if key not in derived}, 'Batch seal policy or unrecognized field changed')
    old_rows, new_rows = by_id(before['rows']), by_id(after['rows'])
    require(all(name in old_rows and steady_row(old_rows[name]) == steady_row(row) for name, row in new_rows.items()),
            'New row or byte-equal row policy changed')
    require(all(key in before['bands'] and before['bands'][key] == value for key, value in after['bands'].items()) and
            after['bands'].keys() == {row['bandKey'] for row in new_rows.values() if 'bandKey' in row}, 'New or changed noise band record')
    builds = []
    for directory, seal in ((reviewed, before), (new, after)):
        mandatory = {'plan.json', 'build/sides.json', 'input/azure-local.json',
                     *(f'{prepared.MIRROR_DIR}/{prefix}.json' for prefix in prepared.MIRRORS),
                     *(f'input/azure/{name}' for name in prepared.host_files())}
        require(mandatory <= seal['files'].keys(), 'Missing mandatory batch transport file')
        require(prepared.json_file(directory / 'plan.json') == {key: value for key, value in seal.items() if key not in PC_FIELDS},
                'Portable plan differs from seal')
        sides = prepared.json_file(directory / 'build/sides.json')
        prepared.portable_builds(sides)
        baseline, candidate = prepared.bind_builds(seal['rows'], sides)
        require(type(seal.get('canonical')) is bool and
                seal['canonical'] == prepared.canonical_rows(baseline, candidate, seal['rows']),
                'Canonical batch status differs from its builds')
        builds.append(sides)
        prepared.verify_timing(directory, seal, seal['payloadManifests'], host_hashes=seal['files'])
    stable1, products1 = session_products(before, reviewed)
    stable2, products2 = session_products(after, new)
    require(all(stable1.get(name) == sha for name, sha in stable2.items()) and
            all(products1.get(name) == value for name, value in products2.items()), 'Non-build session member changed')
    compare_suite(reviewed, new, before, after)
    exempt = {'plan.json', 'build/sides.json'}
    if 'input/suite.tar.gz' in after['files']:
        exempt |= {'input/suite.tar.gz', 'suite.json'}
    fixed = lambda seal: {name: sha for name, sha in seal['files'].items()
                          if name not in exempt and not re.fullmatch(r'input/session-[0-9]+\.tar\.gz', name)}
    require(fixed(before) == fixed(after), 'Non-archive sealed input changed')
    require([steady_build(side) for side in builds[0]] == [steady_build(side) for side in builds[1]], 'Non-range build metadata changed')
    return True


def batch_areas(seal):
    areas, policy = seal.get('batchAreas'), seal['gated']['areas']
    require(isinstance(areas, list) and areas and all(isinstance(name, str) and name in policy for name in areas) and
            areas == sorted(set(areas)), 'Invalid sealed batch areas')
    return areas


def carry_rows(seal, results):
    require(seal['kind'] == 'batch-leg-1' and seal['mode'] == 'change', 'Carry requires a batch leg 1 change report')
    areas = batch_areas(seal)
    patterns = {scope: [pattern for area in areas for pattern in seal['gated']['areas'][area].get(scope, [])]
                for scope in ('bytes', 'time')}
    records = []
    for row in seal['rows']:
        name = row['id']
        scope = 'time' if row['kind'] == 'time' else 'bytes'
        if row.get('rowClass') == 'recorded-only' or any(fnmatch.fnmatchcase(name, pattern) for pattern in patterns[scope]):
            continue
        result = results.get(name, dict(status='INCONCLUSIVE', reason='No reported result'))
        status = result['status']
        if status == 'PASS':
            continue
        if row['kind'] == 'time':
            interval, limit = result.get('confidence95'), 1.05
            reason = 'interval crosses the rule'
        else:
            interval = result.get('confidence95DifferenceBytes')
            band = seal['bands'].get(row.get('bandKey'))
            limit = band['record']['absoluteNoiseBandBytes'] if band else None
            reason = 'Interval crosses limit'
        crosses = (status == 'INCONCLUSIVE' and reason in result['reason'] and limit is not None and
                   isinstance(interval, (list, tuple)) and len(interval) == 2 and
                   all(type(value) in (int, float) and math.isfinite(value) for value in interval) and
                   interval[0] <= limit < interval[1])
        records.append(dict(id=name, classification='moved' if status == 'FAIL' or crosses else 'unresolved',
                            reason=result['reason'], destination='leg-2'))
    return records


def carry(directory):
    seal = gate.read_seal(directory)
    return carry_rows(seal, gate.report(directory)['rows'])
