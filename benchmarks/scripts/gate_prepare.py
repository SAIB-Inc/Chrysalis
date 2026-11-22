"""Linux build export and PC seal import. Only the build guest queries client metadata; noise prepares also run one uncounted micro rehearsal."""
import copy
import gzip
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
from posixpath import normpath
import re
import shlex
import shutil
import subprocess
import sys
import tarfile
import time
from types import SimpleNamespace
import zipfile
import zlib

import gate
import gate_bundle as bundle
import gate_prelude as prelude
import gate_schedule as schedules
import gate_session as session
import gate_time as timing
from gate_azure_guest import results_limits
from gate_azure_payload import CAPSTONE, DEPS_FILES, validate_deps
from gate_time import require

KINDS = ('noise', 'controls', 'batch-leg-1', 'batch-leg-2', 'bisect', 'rc')
FIXTURES = 'benchmarks/Chrysalis.Benchmarks'
INPUT_PREFIXES = ('dotnet/', 'python/', 'corpora/', 'benchmarks/')
ORDERING_WORK = [1020, 9338100]  # MR5a/red-counter.json, STEP7-SPLIT
SHA = re.compile(r'[a-f0-9]{64}')
CONTAINER_ROOT = Path('/gate')
CONTAINER_OUTPUT = 'benchmarks/out/container-prepared'
MARKER_FILE = 'container-build.json'
ENTRYPOINTS = {'dotnet/dotnet', 'python/bin/python3.12'}
MIRRORS = {'dotnet': ('linuxDotnetRoot', 'linuxDotnetManifestSha256'),
           'python': ('flyPythonRoot', 'flyPythonManifestSha256')}
MIRROR_DIR = 'input/mirrors'
OID = re.compile(r'[a-f0-9]{40}')
BATCH_MODES = {'batch-leg-1': 'change', 'batch-leg-2': 'release'}
REQUEST_KEYS = {'kind', 'mode', 'baseline', 'candidate', 'sealKind', 'toolingCommit', 'toolingTree'}

PINNED_DEPS_SHA256 = 'b40ab590a233465de7bd0a81dc0f524b5447a8f6a35227dc467904fe70a41f20'
PINNED_CAPSTONE_SHA256 = 'e85e1ac723954c77843a89899e31853f7f92b6ce9fbbcb095c198b392eee6abb'
REHEARSAL_WALL_SECONDS = 45 * 60
REHEARSAL_FILES = re.compile(r'rehearsal/(?:acceptance\.json|(?:pass|fail)/(?:results\.(?:tar\.gz|sha256)|verdict\.json)|fail/diagnostic-only\.json)')


def validate_build_marker(marker):
    # AZ1 owns the pinned trust anchors used both here and by the launcher.
    from gate_azure_payload import validate_build_marker as validate
    validate(marker)


def relative(name):
    path = PurePosixPath(name)
    require(name != '.' and path.as_posix() == name and not path.is_absolute() and '..' not in path.parts and
            all(re.fullmatch(r'[A-Za-z0-9_.+@-]+', part) for part in path.parts), 'Noncanonical prepared path')
    return Path(name)


def checked_json(data):
    def unique(pairs):
        result = dict(pairs)
        require(len(result) == len(pairs), 'Duplicate JSON member')
        return result
    value = json.loads(data, object_pairs_hook=unique)
    gate.encoded(value)  # Reject nonfinite numbers.
    return value


def json_file(path):
    return checked_json(path.read_bytes())


def files(root):
    require(root.is_dir() and not root.is_symlink(), 'Missing prepared directory or symbolic link')
    entries = sorted(root.rglob('*'))
    require(all(not path.is_symlink() and (path.is_file() or path.is_dir()) for path in entries),
            'Prepared directory contains a symbolic link or special file')
    return {relative(path.relative_to(root).as_posix()).as_posix(): bundle.sha(path) for path in entries if path.is_file()}


def checked_input(root, name, expected):
    path = root / relative(name)
    require(path.resolve().is_relative_to(root.resolve()) and path.is_file() and SHA.fullmatch(expected) and
            bundle.sha(path) == expected, 'Input manifest member differs')
    return path


def inputs(local, destination=None, *, mirror_in=None, mirror_out=None, tree=None):
    """Verify public manifests; optionally mirror their members into a bundle tree."""
    manifests, payload = {}, {}

    def manifest(root, path, key):
        full = root / path
        content = json_file(full)
        manifests[key] = bundle.sha(full)
        return content

    def add(root, name, target, expected):
        source = checked_input(root, name, expected)
        payload[relative(target).as_posix()] = expected
        if destination is not None:
            out = destination / target
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, out)

    for prefix, (root_key, pin_key) in MIRRORS.items():
        name = f'{MIRROR_DIR}/{prefix}.json'
        if mirror_in is not None:
            digest = local.get(pin_key)
            require(matches(SHA, digest), f'Missing or invalid local setting: {pin_key}')
            body = (mirror_in / name).read_bytes()
            require(hashlib.sha256(body).hexdigest() == digest, f'Runtime mirror manifest differs from {pin_key}')
        else:
            root = Path(local[root_key])
            body = (root / 'sha256.json').read_bytes()
            digest = hashlib.sha256(body).hexdigest()
        members = checked_json(body)
        require(isinstance(members, dict) and all(matches(SHA, value) for value in members.values()),
                f'Invalid runtime mirror inventory: {pin_key}')
        manifests[prefix] = digest
        if mirror_out is not None:
            path = mirror_out / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(body)
        for member, expected in members.items():
            target = relative(f'{prefix}/{member}').as_posix()
            if mirror_in is None:
                add(root, member, target, expected)
            else:
                payload[target] = expected
        require(f'{prefix}/' + ('dotnet' if prefix == 'dotnet' else 'bin/python3.12') in payload,
                'Missing bundled runtime entrypoint')
    pin = Path(local['pinnedBaseline'])
    gate.check_pinned(pin)
    manifests['pinnedClosure'] = bundle.sha(pin / 'closure.json')
    corpus = Path(local['corporaRoot'])
    capture = manifest(corpus, 'capture/sha256.json', 'capture')
    # Retain the existing derived payload-only manifest, never incidental capture logs.
    capture = {name: capture[name] for name in ('capture.json', 'server-to-client.bin', 'client-to-server.bin')}
    for name, expected in capture.items():
        add(corpus, 'capture/' + name, 'corpora/capture/' + name, expected)
    data = bundle.gate_encoded(capture)
    payload['corpora/capture/sha256.json'] = hashlib.sha256(data).hexdigest()
    if destination is not None:
        (destination / 'corpora/capture/sha256.json').write_bytes(data)
    manifests['range'] = bundle.sha(corpus / 'range.json')
    add(corpus, 'range.json', 'corpora/range.json', manifests['range'])
    fixture_name = f'{FIXTURES}/sha256.json'
    fixture = tooling_bytes(fixture_name, tree)
    manifests['fixtures'] = hashlib.sha256(fixture).hexdigest()
    fixture_members = {fixture_name: manifests['fixtures']}
    for name, expected in checked_json(fixture).items():
        name = relative(normpath(f'{FIXTURES}/{name}')).as_posix()
        require(name.startswith('benchmarks/'), 'Fixture path escapes benchmarks')
        fixture_members[name] = expected
    for name, expected in fixture_members.items():
        if tree is None:
            require((gate.REPO / name).resolve().is_relative_to(gate.REPO / 'benchmarks'), 'Fixture path escapes benchmarks')
            add(gate.REPO, name, name, expected)
        else:
            actual = manifests['fixtures'] if name == fixture_name else hashlib.sha256(tooling_bytes(name, tree)).hexdigest()
            require(actual == expected, 'Pinned tooling fixture differs from its manifest')
            payload[name] = expected
    return manifests, payload


def archive_members(path, *, objects=None, executable=None):
    """Use expanded member digests, not tar metadata or the tar's own manifest."""
    with tarfile.open(path) as archive:
        result = {}
        for item in archive:
            name = relative(item.name).as_posix()
            require(item.isfile() and name not in result, 'Archive has a nonregular or duplicate member')
            if executable is not None:
                executable[name] = bool(item.mode & 0o100)
            with archive.extractfile(item) as contents:
                if objects is not None and name in objects:
                    body = contents.read()
                    objects[name] = checked_json(body)
                    result[name] = hashlib.sha256(body).hexdigest()
                else:
                    result[name] = hashlib.file_digest(contents, 'sha256').hexdigest()
        return result


def pack(root, output, members, generated, *, expected=None, entrypoints=()):
    require(all(name in members and (root / name).stat().st_mode & 0o100 for name in entrypoints),
            'Missing or non-executable bundled entrypoint')
    hashes = {name: bundle.sha(root / name) for name in members}
    require(expected is None or hashes == expected, 'Staged member digest differs before archive packing')
    hashes.update({name: hashlib.sha256(value).hexdigest() for name, value in generated.items()})
    generated = dict(generated, **{'bundle-manifest.json': bundle.gate_encoded(hashes)})
    # Normalize both tar and gzip headers; keep only the executable bit from source.
    import io
    with output.open('xb') as stream, gzip.GzipFile(fileobj=stream, filename='', mode='wb', mtime=0) as compressed, \
            tarfile.open(fileobj=compressed, mode='w') as archive:
        for name in sorted(set(hashes) | {'bundle-manifest.json'}):
            item = tarfile.TarInfo(name)
            if name in generated:
                item.size = len(generated[name])
                archive.addfile(item, io.BytesIO(generated[name]))
            else:
                path = root / name
                item.size = path.stat().st_size
                if path.stat().st_mode & 0o100:
                    item.mode = 0o755
                with path.open('rb') as contents:
                    archive.addfile(item, contents)


def pack_session(base, dest, members, plan):
    pack(base, dest, sorted(members), {'plan.json': bundle.gate_encoded(schedules.worker_plan(plan)),
         'prelude.json': bundle.gate_encoded(prelude.recipe(plan))}, expected=plan['inputSha256'], entrypoints=ENTRYPOINTS)


def rehearsal_plans(plan, *, gen0=None):
    micros = [row['id'] for row in plan['rows'] if row.get('replay') == 'micro']
    require(bool(micros) and (gen0 is None or type(gen0) is int and gen0 > 0), 'Invalid rehearsal plan or gen0 budget')
    fail_row = 'time/micro-hashing' if 'time/micro-hashing' in micros else micros[0]
    variants = {}
    for case in ('fail', 'pass'):
        variant = copy.deepcopy(plan)
        for key in ('provider', 'region'):
            variant.pop(key, None)
        variant['probe']['limit'] = 1.0
        if gen0 is not None:
            variant['probe']['gen0BudgetBytes'] = gen0
        for row in variant['rows']:
            row.update(aPairs=1, bPairs=0, chargeSeconds=3 if case == 'fail' and row['id'] == fail_row else 300)
        variants[case] = schedules.prepare(variant)
    return variants


def rehearse_case(case, base, members, plan, scratch):
    import gate_azure as azure, gate_azure_guest as guest
    scratch.mkdir(parents=True, exist_ok=False)
    payload, archive = scratch / 'payload.tar.gz', scratch / 'results.tar.gz'
    work, temp, note = (scratch / name for name in ('work', 'temp', 'note'))
    for directory in (work, temp, note):
        directory.mkdir(mode=0o700)
    pack_session(base, payload, members, plan)
    old_work, old_temp = guest.WORK, guest.TEMP
    guest.WORK, guest.TEMP = work, temp
    try:
        limits = guest.plan_results_limits(payload)
        guest._save_limits(limits)
        with tarfile.open(payload) as stream:
            stream.extractall(work, filter='data')
        # The payload's python3.12 needs glibc 2.38 (Azure's Ubuntu 24.04); this Debian 12 guest has 2.36.
        text = guest.REMOTE_SESSION
        require(text.splitlines().count('cd /opt/chrysalis') == 1, 'Unexpected remote session line')
        for old, new, count in (('\ncd /opt/chrysalis\n', f'\ncd {shlex.quote(str(work))}\n', 1),
                                ('PYTHONHOME="$PWD/python" ', '', 1), ('export LD_LIBRARY_PATH="$PWD/python/lib"\n', '', 1),
                                ('"$PWD/python/bin/python3.12"', shlex.quote(sys.executable), 2)):
            require(text.count(old) == count, 'Unexpected remote session line')
            text = text.replace(old, new)
        script = temp / 'remote-session.sh'
        script.write_text(text)
        try:
            proc = subprocess.run(['/bin/bash', str(script)], cwd=work, capture_output=True,
                                  env={'PATH': '/usr/bin:/bin', 'P2_MACHINE_CREATED_UNIX': str(int(time.time()))},
                                  timeout=min(plan['measurementCutoffSeconds'], REHEARSAL_WALL_SECONDS))
        except subprocess.TimeoutExpired as error:
            raise ValueError(f'rehearsal {case} exceeded its wall limit') from error
        partial = guest.archive_results(archive, False, limits=limits)
    finally:
        guest.WORK, guest.TEMP = old_work, old_temp
    code, err = None, None
    try:
        code = azure.probe_result(archive, plan, note)
    except (ValueError, RuntimeError, KeyError, tarfile.TarError, OSError, EOFError, zlib.error, AttributeError) as error:
        err = str(error)
    diag = note / 'diagnostic-only.json'
    return dict(archive=archive, exitCode=proc.returncode, partial=partial, archiveSha256=bundle.sha(archive),
                importerCode=code, importerError=err, diagnosticPath=diag if diag.is_file() else None,
                probeRejected=(note / 'probe-rejected.json').is_file())


def rehearsal_checks(case, archive, plan, outcome):
    import gate_azure_guest as guest
    worker, recipe = schedules.worker_plan(plan), prelude.recipe(plan)
    sha_ok = archive.is_file() and outcome.get('archiveSha256') == bundle.sha(archive)
    read_err = (ValueError, OSError, tarfile.TarError, EOFError, zlib.error)
    try:
        full = sha_ok and outcome.get('partial') is False and guest.check_results(archive, limits=results_limits(worker)) is False
    except read_err:
        full = False
    data, log_text, applied_read, pop_ok, diag = {}, '', None, False, None
    try:
        with tarfile.open(archive, 'r:gz') as stream:
            names = set(stream.getnames())
            def read_member(member, parse, default=None):
                try:
                    handle = stream.extractfile(member) if member in names else None
                    return default if handle is None else parse(handle.read())
                except read_err:
                    return default
            data = {k: read_member(m, checked_json) for k, m in (
                ('status', 'results/session-status.json'), ('summary', 'results/guest-summary.json'),
                ('applied', 'prelude/applied.json'), ('members', 'results/members.json'),
                ('probe', 'results/probe.json'), ('marker', 'results/recovery-diagnostic.json'))}
            log_text = read_member('results/session.log', lambda b: b.decode('utf-8', errors='replace'), '')
            if case == 'pass':
                try:
                    applied_read = prelude.read(stream, plan)
                    if isinstance(data.get('members'), list):
                        prelude.verify_population(plan, data['members'], applied_read)
                        pop_ok = True
                except (*read_err, KeyError, TypeError):
                    pass
    except read_err:
        pass
    try:
        diag = json_file(Path(outcome['diagnosticPath'])) if outcome.get('diagnosticPath') else None
    except (ValueError, OSError):
        diag = None
    status, summary, applied, members_json = (data.get(k) for k in ('status', 'summary', 'applied', 'members'))
    is_status, want_rc, lines = isinstance(status, dict), (0 if case == 'pass' else 2), log_text.splitlines()
    p, s = timing.STAGE_PREFIX, timing.STAGE
    fail_rows = [n for n, r in recipe['rows'].items() if r['chargeSeconds'] <= 3]
    log_prelude = (bool(recipe['rows']) and all(any(l.startswith(f"{p}{s['prelude']}{n} {r} ") for l in lines)
                   for n in recipe['rows'] for r in ('baseline', 'candidate')) if case == 'pass' else
                   any(l.startswith(f"{p}{s['prelude']}{n} {r} ") for n in fail_rows for r in ('baseline', 'candidate') for l in lines))
    checks = dict(
        archive_full=full, status_present=is_status, probe_kept=is_status and status.get('probe') == 'kept',
        exit_code=is_status and status.get('exitCode') == want_rc and outcome.get('exitCode') == want_rc,
        log_nonempty=bool(log_text.strip()), log_start=any(l.startswith(p + s['start']) for l in lines),
        log_probe=any(l.startswith(p + s['probe'] + 'kept cv ') for l in lines), log_prelude=log_prelude,
        log_end=any(l.startswith(f"{p}{s['end']}{want_rc} seconds ") for l in lines),
        summary_fields=(isinstance(summary, dict) and all(type(summary.get(k)) is int and summary[k] >= 0 for k in guest.LOG_COUNTS)
                        and summary.get('logsIncluded', 0) >= 1 and all(type(summary.get(k)) is int and summary[k] > 0
                        for k in ('expandedBytes', 'maxCompressedBytes', 'maxExpandedBytes'))),
    )
    if case == 'fail':
        checks.update(fail_reason=is_status and status.get('reason') == prelude.CHARGE_EXCEEDED,
                      log_stopped=(p + s['stopped'] + prelude.CHARGE_EXCEEDED) in lines,
                      importer_inconclusive=(outcome.get('importerCode') is None and 'INCONCLUSIVE' in (outcome.get('importerError') or '')
                                             and isinstance(diag, dict) and diag.get('verdict') == 'INCONCLUSIVE'))
    else:
        checks.update(completed=is_status and status.get('completedAllDeclaredRounds') is True,
                      applied_rows=isinstance(applied, dict) and set(applied) == set(recipe['rows']),
                      members_count=isinstance(members_json, list) and len(members_json) == len(worker['members']),
                      importer_ok=outcome.get('importerCode') == 0 and outcome.get('importerError') is None,
                      prelude_read=applied_read is not None, prelude_population=pop_ok)
    artefacts = [n for n, ok in (('archive', full), ('sha256', sha_ok), ('status', is_status), ('log', bool(log_text.strip())),
                 ('summary', isinstance(summary, dict)), ('applied', isinstance(applied, dict)),
                 ('members', isinstance(members_json, list)), ('diagnostic', isinstance(diag, dict))) if ok]
    stages = [k for l in lines if l.startswith(p) for k, v in s.items() if k not in ('stopped', 'end') and l[len(p):].startswith(v)]
    errors = [n for l in lines if (n := l.split(':', 1)[0].rsplit('.', 1)[-1]).isascii() and n.isidentifier() and n.endswith(('Error', 'Exception'))]
    marker = data['marker'].get('reason') if isinstance(data.get('marker'), dict) else None
    worker_diag = (f"exit:{ec if type(ec := outcome.get('exitCode')) is int else 'none'},stage:{stages[-1] if stages else 'none'},"
                   f"marker:{marker if isinstance(marker, str) and marker in guest.REFUSAL_REASONS else 'none'},error:{errors[-1] if errors else 'none'}")
    return dict(checks=checks, status=status or {}, artefacts=artefacts, probe=data.get('probe') or {}, worker=worker_diag)


def rehearse(base, members, plan, scratch, dest):
    gen0_sealed = gen0_used = plan['probe']['gen0BudgetBytes']
    gen0_overridden, plans = False, rehearsal_plans(plan)
    def verdict_line(case, ok, reason, artefacts=(), worker='none'):
        line = (f"rehearsal case={case} verdict={'PASS' if ok else 'FAIL'} reason={reason} "
                f"artefacts={','.join(artefacts) or 'none'} worker={worker} "
                f"gen0={gen0_used} overridden={str(gen0_overridden).lower()} counted=false")
        print(line, flush=True)
        return line
    def run_case(case, tag):
        try:
            return rehearse_case(case, base, members, plans[case], scratch / tag)
        except Exception as error:
            verdict_line(case, False, 'wall-limit' if isinstance(error, ValueError) and 'exceeded its wall limit' in str(error) else f'error-{type(error).__name__}')
            raise
    outcome = run_case('fail', 'fail-1')
    info = rehearsal_checks('fail', outcome['archive'], plans['fail'], outcome)
    probe = info.get('probe') or {}
    obs = (probe.get('gen0BudgetBytes') or {}).get('observed')
    if (probe.get('decision') == 'rejected' and probe.get('reasons') == ['gen0-budget-mismatch']
            and isinstance(obs, list) and len(obs) == 1 and type(obs[0]) is int and obs[0] > 0):
        if not (outcome.get('importerCode') == 5 and outcome.get('probeRejected') is True):
            verdict_line('fail', False, 'probe-importer-mismatch', info['artefacts'], info.get('worker', 'none'))
            raise ValueError('Rehearsal rejected probe did not pass importer check')
        gen0_used, gen0_overridden = obs[0], True
        plans = rehearsal_plans(plan, gen0=gen0_used)
        outcome = run_case('fail', 'fail-2')
        info = rehearsal_checks('fail', outcome['archive'], plans['fail'], outcome)
    meta = dict(counted=False, platform='pc', gen0Sealed=gen0_sealed, gen0Used=gen0_used, gen0Overridden=gen0_overridden)
    def finalize(case, cur, cur_info):
        failed = [k for k, ok in cur_info['checks'].items() if not ok]
        reason = ('failed:' + ','.join(failed)) if failed else ('prelude-charge-exceeded' if case == 'fail' else 'ok')
        line = verdict_line(case, not failed, reason, cur_info['artefacts'], cur_info.get('worker', 'none'))
        require(not failed, f'Rehearsal failed ({case}): {", ".join(failed)}')
        st = {k: cur_info['status'][k] for k in ('completedAllDeclaredRounds', 'exitCode', 'probe', 'dry') if k in cur_info['status']}
        if cur_info['status'].get('reason') in (None, prelude.CHARGE_EXCEEDED):
            st['reason'] = cur_info['status']['reason']
        return cur, dict(case=case, verdict='PASS', reasonCode=reason, exitCode=cur['exitCode'], status=st, archiveSha256=cur['archiveSha256'],
                         importer=dict(exitCode=cur['importerCode'], inconclusive=bool(cur['importerError'])), artefacts=cur_info['artefacts'],
                         checks=cur_info['checks'], summaryLine=line, **meta)
    results = {'fail': finalize('fail', outcome, info)}
    passed = run_case('pass', 'pass')
    results['pass'] = finalize('pass', passed, rehearsal_checks('pass', passed['archive'], plans['pass'], passed))
    for case, (cur, verdict) in results.items():
        case_dir = dest / case
        case_dir.mkdir(parents=True, exist_ok=False)
        shutil.copy2(cur['archive'], case_dir / 'results.tar.gz')
        (case_dir / 'results.sha256').write_text(cur['archiveSha256'] + '\n')
        session.save(case_dir / 'verdict.json', verdict)
        if case == 'fail':
            shutil.copy2(cur['diagnosticPath'], case_dir / 'diagnostic-only.json')
    session.save(dest / 'acceptance.json', dict(verdict='PASS', archives={c: results[c][1]['archiveSha256'] for c in ('fail', 'pass')},
                 lines=[results[c][1]['summaryLine'] for c in ('fail', 'pass')], **meta))


def guest_files():
    return bundle.AZURE_GUEST_FILES


def host_files():
    return bundle.HOST_FILES['azure']


def replay_job(replay, filter):
    return dict(kind='time', replay=replay, dotnet='dotnet/dotnet', server='scripts/n2n_replay_server.py',
                capture='corpora/capture', filter=filter, microInvocations=bundle.MICRO_PROBE_INVOCATIONS,
                environment=dict(CHRYSALIS_CORPORA='corpora', CHRYSALIS_BENCHMARK_DATA=FIXTURES,
                                 P1_RANGE='corpora/range.json', DOTNET_TieredPGO='1'))


def plan_rows(rows, config):
    # The historical flat Fly records are not Azure records.
    machine = timing.provider_for({'provider': 'azure'})['machine']
    noise = config['time']['noise'].get(machine, {})
    selected = bundle.time_rows(rows, dict(config, time=dict(config['time'], noise={})))
    for row in selected:
        row.update(provider='azure', region=row.get('region', next(iter(timing.PROVIDERS['azure']['regions']))))
        row['noise'] = noise.get(row['id'], noise.get(row['replay']))
        require(row['noise'] is None or timing.noise_band(row['noise'], 'azure') is not None, 'Noise record provenance or band mismatch')
        for spec in row['sides'].values():
            path = f'clients/{schedules.build_id(spec)}'
            spec.update(client=path + '/ReplayClient.dll', harness=path + '/Chrysalis.Benchmarks.dll')
        row['job'] = replay_job(row['replay'], row.get('microFilter', row['id']))
    return selected


def time_plans(seal):
    config, mode = seal['gated'], seal['mode']
    selected = plan_rows(seal['rows'], config)
    if seal['noiseRecords']:
        selected = [dict(row, aPairs=max(16, row.get('aPairs', 0)), bPairs=0,
                         sides=dict(baseline=row['sides']['baseline'], candidate=row['sides']['baseline']))
                    for row in selected if row['noise'] is None]
    groups = [[row] for row in selected] if seal['sessionPerRow'] else [selected]
    plans = [plan for group in groups for plan in timing.size_sessions(
        group, 'change' if seal['noiseRecords'] else mode, option=config['time']['option'],
        probe=timing.validate_probe_rule(config['time'].get('probe')), prelude=True)]
    proven = set()
    for plan in plans:
        if seal['noiseRecords']:
            plan.update(design='noise-band', repeatAllowed=False)
        reference = next((row for row in plan['rows'] if row.get('rowClass', 'gated') == 'gated'), None)
        require(reference is not None, 'Missing gated baseline for probe')
        build = schedules.build_id(reference['sides']['baseline'])
        plan.update(mode=mode, ruleVersion=gate.stats.RULE_VERSION, probeJob=replay_job('offline', '*'),
                    orderingProof=ORDERING_WORK if seal['orderingProof'] and build not in proven else None)
        results_limits(plan, strict=True)
        proven.add(build)
    return plans


def closure_names(plan):
    result = {}
    for row in plan['rows']:
        for spec in row['sides'].values():
            path = f'clients/{schedules.build_id(spec)}'
            result.update({f'{path}/{name}': value for name, value in spec['dllSha256s'].items()})
    return result


def static_inputs(plan, payload):
    use_corpus = any(row.get('replay') == 'tcp' for row in plan['rows'])
    runtimes = {spec['runtime'] for row in plan['rows'] for spec in row['sides'].values()}
    return {name: value for name, value in payload.items()
            if (not name.startswith('corpora/') or use_corpus) and
            (not name.startswith(('dotnet/host/fxr/', 'dotnet/shared/Microsoft.NETCore.App/')) or
             name.split('/')[3] in runtimes)}


def client_metadata(name):
    return name in DEPS_FILES or name.endswith('.runtimeconfig.json')


def copy_staged_closure(source, destination, tfm, dll_hashes):
    """Copy only sealed DLLs, executable metadata, and selected native members."""
    flat = set(dll_hashes) | {file.name for file in source.iterdir() if file.is_file() and client_metadata(file.name)}
    native = {asset for name in DEPS_FILES for asset in validate_deps(json_file(source / name), tfm, name)}
    native_hashes = {}
    for name in sorted(native):
        asset = source / name
        require(asset.is_file() and asset.resolve() == asset, f'Missing staged native asset: {name}')
        native_hashes[name] = bundle.sha(asset)
    require(tfm != 'net10.0' or native_hashes.get(CAPSTONE) == PINNED_CAPSTONE_SHA256,
            'Pinned native Capstone digest differs')
    bundle.copy_files(source, destination, files=sorted(flat | native))
    require(all(bundle.sha(destination / name) == sha for name, sha in native_hashes.items()), 'Staged native asset digest differs')
    require(gate.closure_hashes(destination) == dll_hashes, 'Staged every-DLL identity differs')
    return flat | native


def canonical_rows(baseline, candidate, rows):
    return gate.canonical(baseline, candidate) and all(row['sides']['candidate']['commit'] == candidate['commit'] for row in rows)


def azure_config(config):
    # Explicit prepare uses Azure; the checked-in default for legacy run stays Fly.
    provider = timing.PROVIDERS['azure']
    config['time']['platform'] = dict(provider='azure', size=provider['machine'], regions=list(provider['regions']),
                                      image=provider['image'])
    return config


def micro_probes(side, selector):
    sdk = gate.staged_sdk_root(side['commit'])
    client = Path(side['client'])
    try:
        names = json.loads(subprocess.check_output(
            [sdk / 'dotnet', '--fx-version', side['runtime'], client, 'micro-probes', selector],
            cwd=client.parent, env=dict(session.environment({}), DOTNET_ROOT=str(sdk)),
            stderr=subprocess.PIPE, text=True, timeout=30))
    except subprocess.CalledProcessError as error:
        raise ValueError(error.stderr.strip()) from error
    require(isinstance(names, list) and names and all(isinstance(name, str) and '.' in name for name in names)
            and len(set(names)) == len(names), 'Invalid micro probe inventory')
    return names


def verify_micro_probes(args, rows, config, staged, baseline):
    label = 'pinned baseline' if baseline['commit'] == gate.PIN else 'baseline'
    for row in plan_rows(rows, config):
        if 'microFilter' not in row:
            continue
        selector = row['microFilter']
        right = staged[row.get('candidate', args.candidate)]
        try:
            left_names, right_names = (micro_probes(side, selector) for side in (baseline, right))
        except ValueError as error:
            raise ValueError(f'{row["id"]}: cannot pair {selector!r} against {label}: {error}') from error
        require(left_names and left_names == right_names,
                f'{row["id"]}: {label} has different micro methods for {selector!r}: {left_names} != {right_names}')


def build(args, output, local, *, marker, phase):
    config = azure_config(json_file(gate.GATE / 'gated.json'))
    audit = json_file(gate.REPO / FIXTURES / 'audit-allocations.json')
    rows = gate.select_rows(args, config, audit)
    require(all(row['kind'] in ('time', 'replay') for row in rows), 'Azure prepares timing and replay rows only')
    require(args.seal_kind in KINDS and (args.seal_kind == 'noise') == config['_noiseRecords'],
            'Noise seal kind must match the plan noiseRecords flag')
    require(args.seal_kind not in BATCH_MODES or (args.mode == BATCH_MODES[args.seal_kind] and config['_batchAreas']),
            'Batch seal requires its mode and named areas')
    root = output.with_name('.' + output.name + '-stage')
    require(not output.exists() and not output.is_symlink() and root.resolve() == root,
            'Choose a fresh container preparation directory')
    state = root / 'restore.json'
    expected = dict(buildMarker=marker, local=local, rows=rows, config=config,
                    baseline=args.baseline, candidate=args.candidate, mode=args.mode,
                    sealKind=args.seal_kind, orderingProof=args.ordering_proof)
    if phase == 'restore':
        root.mkdir(parents=True, exist_ok=False)
        gate.stage_rows(args, root, rows, local, phase='restore')
        session.save(state, expected)
        return root
    require(phase == 'build' and state.is_file() and not state.is_symlink() and
            bundle.gate_encoded(json_file(state)) == bundle.gate_encoded(expected),
            'Restore phase missing or preparation request changed')
    staged, baseline, candidate = gate.stage_rows(args, root, rows, local, phase='build')
    verify_micro_probes(args, rows, config, staged, baseline)
    output.mkdir(parents=True, exist_ok=False)
    # Infer replay before tying rows to their corpus. Stage aliases may be absolute guest paths.
    for row in rows:
        if 'candidate' in row:
            row['candidate'] = row['sides']['candidate']['commit']
    seal = portable_plan(args.mode, args.seal_kind, args.ordering_proof, rows, baseline, candidate, config, audit, local, marker)
    seal['timePlans'] = []
    base = root / 'payload'
    session.save(base / MARKER_FILE, marker)
    manifests, payload = inputs(local, base, mirror_out=output)
    scripts = gate.REPO / 'benchmarks/scripts'
    bundle.copy_files(scripts, output / 'input/azure', files=host_files())
    bundle.copy_files(scripts, base / 'scripts', files=guest_files())
    builds = [gate.public_side(side) for side in staged.values()]
    session.save(output / 'build/sides.json', builds)
    closures = {}
    for side in staged.values():
        relative_root = f'clients/{schedules.build_id(side)}'
        members = copy_staged_closure(Path(side['client']).parent, base / relative_root, side['tfm'], side['dllSha256s'])
        closures[relative_root] = {f'{relative_root}/{name}' for name in members}
    plans = time_plans(seal)
    require(not seal['noiseRecords'] or plans, 'No missing noise records for selected Azure rows')
    for number, plan in enumerate(plans):
        roots = {str(Path(name).parent) for name in closure_names(plan)}
        members = {MARKER_FILE, *static_inputs(plan, payload), *(f'scripts/{name}' for name in guest_files()),
                   *(name for folder in roots for name in closures[folder])}
        plan['inputSha256'] = {name: bundle.sha(base / name) for name in sorted(members)}
        schedules.prepare(plan)
        pack_session(base, output / f'input/session-{number}.tar.gz', members, plan)
        if seal['noiseRecords'] and any(row.get('replay') == 'micro' for row in plan['rows']):
            rehearse(base, members, plan, root / f'rehearsal-{number}', output / 'rehearsal')
        seal['timePlans'].append(plan)
    session.save(output / 'plan.json', seal)
    session.save(output / 'prepared.json', dict(schemaVersion=1, buildMarker=marker, manifests=manifests,
                                                builds=builds, files=files(output)))
    return output


def checked_local(*, guest_root=None):
    name = os.environ.get('CHRYSALIS_GATE_LOCAL')
    require(name is not None, 'Set CHRYSALIS_GATE_LOCAL for this host')
    path = Path(name).resolve()
    require(guest_root is None or path.is_relative_to(guest_root), 'Build settings must be inside the guest root')
    local = json_file(path)
    launcher_keys = ('azureCli', 'azureLock', 'azureBaseline', 'azureLedger')
    keys = ('corporaRoot', 'pinnedBaseline')
    if guest_root is None:
        keys += launcher_keys
    else:
        keys += tuple(root for root, _ in MIRRORS.values())
        require(set(local) == set(keys), 'Container settings must contain public inputs only')
    require(all(isinstance(local.get(key), str) and Path(local[key]).is_absolute() and Path(local[key]).resolve() == Path(local[key])
                and (guest_root is None or Path(local[key]).is_relative_to(guest_root)) for key in keys),
            'Missing or noncanonical local paths for this host')
    return path, local


def portable_builds(data):
    keys = set(prelude.IDENTITY) | {'sdk', 'tfm'}
    require(isinstance(data, list) and data and all(
        isinstance(side, dict) and set(side) in (keys, keys | {'previousAlpha'}) and
        all(matches(OID, side[key]) for key in ('commit', 'treeId')) and
        all(matches(r'[0-9]+(?:\.[0-9]+){1,2}', side[key]) for key in ('sdk', 'runtime')) and
        matches(r'net[0-9]+\.[0-9]+', side['tfm']) and
        type(side.get('previousAlpha', False)) is bool and isinstance(side['dllSha256s'], dict) and
        all(relative(name).name == name and name.endswith('.dll') and matches(SHA, value)
            for name, value in side['dllSha256s'].items()) for side in data), 'Invalid portable build records')


def restore_pinned_native(client_directory, harness_deps, pinned, nuget_source):
    """Restore the legacy native entries stripped by MSBuild's direct DLL references."""
    original = pinned / 'Chrysalis.Benchmarks.deps.json'
    require(original.is_file() and original.resolve() == original and bundle.sha(original) == PINNED_DEPS_SHA256,
            'Archived pinned deps digest differs')
    source = json_file(original)
    require(validate_deps(source, 'net10.0', original.name) == {CAPSTONE}, 'Pinned native graph changed')
    native_graph = source['targets'][source['runtimeTarget']['name']]
    package = next(name for name in native_graph if name.lower().startswith('gee.external.capstone/'))
    targets = native_graph[package]['runtimeTargets']
    package_path = source['libraries'][package]['path']
    nupkg = nuget_source / package_path / (package.replace('/', '.').lower() + '.nupkg')
    try:
        with zipfile.ZipFile(nupkg) as archive:
            body = archive.read(CAPSTONE)
    except (OSError, KeyError, zipfile.BadZipFile) as error:
        raise ValueError('Cannot read archived pinned Linux-x64 Capstone asset') from error
    require(hashlib.sha256(body).hexdigest() == PINNED_CAPSTONE_SHA256, 'Pinned native Capstone digest differs')
    for name in DEPS_FILES:
        path = client_directory / name
        # Direct references do not copy the newly built harness deps to the client.
        rebuilt = json_file(harness_deps if name == harness_deps.name else path)
        active = rebuilt['targets'][rebuilt['runtimeTarget']['name']]
        entries = [record for key, record in active.items() if key.lower().startswith('gee.external.capstone/')]
        require(len(entries) == 1 and 'runtimeTargets' not in entries[0], f'Unexpected legacy Capstone package in {name}')
        # The managed references and binaries remain as built; restore only native resolution.
        entries[0]['runtimeTargets'] = targets
        validate_deps(rebuilt, 'net10.0', name)
        session.save(path, rebuilt)
    destination = client_directory / CAPSTONE
    require(destination.resolve() == destination, 'Unexpected legacy native asset path')
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(body)


def native_members(objects, deps_tfms, *, require_capstone):
    return {str(PurePosixPath(name).parent / native): tfm for name, tfm in deps_tfms.items()
            for native in validate_deps(objects[name], tfm, name, require_capstone=require_capstone)}


def verify_payload(path, marker, deps_tfms, *, expected_hashes=None, metadata=(), require_capstone=True, entrypoints=()):
    """Check the marker, native graph, and selected native member hashes against the bound inventory."""
    objects = dict.fromkeys((*metadata, MARKER_FILE, *deps_tfms))
    modes = {}
    expanded = archive_members(path, objects=objects, executable=modes)
    require(all(modes.get(name) for name in entrypoints), 'Missing or non-executable bundled entrypoint')
    require(MARKER_FILE in expanded and objects[MARKER_FILE] == marker, 'Missing or different archive build marker')
    for name in deps_tfms:
        require(name in expanded, f'Missing packed {name}')
    for name, tfm in native_members(objects, deps_tfms, require_capstone=require_capstone).items():
        require(name in expanded, f'Missing packed native asset: {name}')
        require(expected_hashes is not None and expanded[name] == expected_hashes.get(name), f'Native asset digest differs: {name}')
        if tfm == 'net10.0' and require_capstone and (name == CAPSTONE or name.endswith('/' + CAPSTONE)):
            require(expanded[name] == PINNED_CAPSTONE_SHA256, 'Pinned native Capstone digest differs')
    return expanded, objects


def deps_paths(plan):
    return {f'clients/{schedules.build_id(spec)}/{name}': spec['tfm'] for row in plan['rows']
            for spec in row['sides'].values() for name in DEPS_FILES}


def script_hashes(prefix, names, tree):
    return {f'{prefix}/{name}': hashlib.sha256(git_blob(tree, f'benchmarks/scripts/{name}')).hexdigest() for name in names}


def verify_host_scripts(files, tree):
    require({name: sha for name, sha in files.items() if name.startswith('input/azure/')} ==
            script_hashes('input/azure', host_files(), tree), 'Prepared host script changed')


def verify_timing(output, seal, payload, *, host_hashes):
    marker = seal.get('buildMarker')
    validate_build_marker(marker)
    expected = time_plans(seal)
    sealed_files = seal.get('files')
    require(len(expected) == len(seal['timePlans']) and (sealed_files is None or
                {name for name in sealed_files if re.fullmatch(r'input/session-[0-9]+\.tar\.gz', name)} ==
                {f'input/session-{number}.tar.gz' for number in range(len(expected))}), 'Prepared session packing changed')
    guest = {f'scripts/{name}': host_hashes.get(f'input/azure/{name}') for name in guest_files()} if expected else {}
    for number, (plan, actual) in enumerate(zip(expected, seal['timePlans'])):
        path = output / f'input/session-{number}.tar.gz'
        metadata = ('plan.json', 'prelude.json', 'bundle-manifest.json')
        deps = deps_paths(plan)
        expanded, objects = verify_payload(path, marker, deps, expected_hashes=actual['inputSha256'], metadata=metadata, entrypoints=ENTRYPOINTS)
        require(set(metadata) <= expanded.keys(), 'Missing session archive plan')
        hashes = {name: sha for name, sha in expanded.items() if name not in metadata}
        static = static_inputs(plan, payload)
        require({name: sha for name, sha in hashes.items() if name.startswith(INPUT_PREFIXES)} == static and
                {name: sha for name, sha in hashes.items() if name.startswith('clients/') and name.endswith('.dll')} == closure_names(plan),
                'Packed input differs from verified provenance')
        require({name: sha for name, sha in hashes.items() if name.startswith('scripts/')} == guest, 'Packed guest script changed')
        roots = {str(Path(name).parent) for name in closure_names(plan)}
        native = native_members(objects, deps, require_capstone=True)
        require(all(name == MARKER_FILE or name in native or name.startswith((*INPUT_PREFIXES, 'scripts/')) or
                    (str(Path(name).parent) in roots and
                     (client_metadata(Path(name).name) or name in closure_names(plan))) for name in hashes), 'Unrecognized packed input')
        plan['inputSha256'] = hashes
        schedules.prepare(plan)
        require(plan == actual and objects['plan.json'] == schedules.worker_plan(plan) and
                objects['prelude.json'] == prelude.recipe(plan) and
                objects['bundle-manifest.json'] == {name: sha for name, sha in expanded.items() if name != 'bundle-manifest.json'},
                'Prepared timing plan differs from sealed inputs')
        results_limits(objects['plan.json'], strict=True)


def matches(pattern, value):
    return isinstance(value, str) and re.fullmatch(pattern, value) is not None


def immutable_git(*args):
    try:
        return gate.git('--no-replace-objects', *args, cwd=gate.REPO)
    except (OSError, subprocess.CalledProcessError) as error:
        raise ValueError('Completion tooling or build Git object is unresolvable or unreachable') from error


def git_blob(tree, name):
    try:
        return subprocess.check_output(['git', '--no-replace-objects', '-C', str(gate.REPO), 'cat-file', 'blob', f'{tree}:{name}'],
                                       env=gate.runner.git_environment(base=session.environment({})))
    except (OSError, subprocess.CalledProcessError) as error:
        raise ValueError(f'Completion tooling file is unavailable: {name}') from error


def tooling_bytes(name, tree):
    return (gate.REPO / name).read_bytes() if tree is None else git_blob(tree, name)


def tooling_bands(tree):
    folder = 'benchmarks/gate/bands/'
    entries = immutable_git('ls-tree', '-z', tree, '--', folder).split('\0')
    sources = []
    for entry in filter(None, entries):
        metadata, name = entry.split('\t', 1)
        if name.endswith('.json'):
            require(metadata.startswith(('100644 blob ', '100755 blob ')) and Path(name).parent.as_posix() == folder.rstrip('/'),
                    'Invalid frozen band in tooling tree')
            sources.append((Path(name).name, git_blob(tree, relative(name).as_posix())))
    return sources


def commit_header(commit):
    headers = immutable_git('cat-file', 'commit', commit).split('\n\n', 1)[0].splitlines()
    require(headers and headers[0].startswith('tree ') and matches(OID, headers[0][5:]), 'Invalid completion commit header')
    return headers[0][5:], headers


def completion(source, fetched, local):
    # mbp-run fetches each worktree into a unique run directory before unpacking the container folder.
    root = fetched.absolute()
    require(root == root.resolve(), 'Completion directory must be fetched under work/mbp-results without symlinks')
    parts = source.relative_to(root).parts if source.is_relative_to(root) else ()
    token = re.fullmatch(r'mbp-container-([a-f0-9]{32})', parts[2]) if len(parts) == 4 else None
    require(token and parts[3] == 'prepared' and matches(r'[A-Za-z0-9][A-Za-z0-9._-]*', parts[0]) and
            matches(r'[a-f0-9]{32}', parts[1]) and source == source.resolve(),
            'Completion directory must be fetched under work/mbp-results/<worktree>/<run-id>')
    parent = source.parent
    receipt, inventory = (parent / name for name in ('completion.json', 'build-files.json'))
    require(all(path.is_file() and not path.is_symlink() for path in (receipt, inventory)) and
            {path.name for path in parent.iterdir()} == {'completion.json', 'build-files.json', 'prepared'},
            'Missing or extra completion handoff files')
    data = json_file(receipt)
    require(isinstance(data, dict) and data.keys() == {'schemaVersion', 'token', 'buildMarker', 'request', 'requestSha256',
            'previousAlpha', 'preparedSha256', 'filesManifestSha256'} and type(data['schemaVersion']) is int and
            data['schemaVersion'] == 1 and data['token'] == token[1], 'Invalid completion receipt or token')
    request = data['request']
    require(isinstance(request, dict) and REQUEST_KEYS <= request.keys() <= REQUEST_KEYS | {'only', 'plan', 'planSha256', 'orderingProof'}
            and request['kind'] == 'prepare' and request['mode'] in ('change', 'release') and request['sealKind'] in KINDS
            and all(matches(OID, request[key]) for key in ('baseline', 'candidate', 'toolingCommit', 'toolingTree'))
            and ('only' not in request or isinstance(request['only'], str) and request['only'] and 'plan' not in request)
            and ('plan' in request) == ('planSha256' in request) and
            ('plan' not in request or isinstance(request['plan'], str) and matches(SHA, request['planSha256']))
            and type(request.get('orderingProof', False)) is bool, 'Invalid completion request')
    canonical = json.dumps(request, sort_keys=True, separators=(',', ':'), ensure_ascii=True, allow_nan=False).encode('utf-8')
    require(data['requestSha256'] == hashlib.sha256(canonical).hexdigest(), 'Completion request digest differs')
    validate_build_marker(data['buildMarker'])
    commit, tree = local.get('azureToolingCommit'), local.get('azureToolingTree')
    require(matches(OID, commit) and matches(OID, tree) and (commit, tree) == (request['toolingCommit'], request['toolingTree']),
            'Completion tooling pair differs from the independently approved local pair')
    actual_tree, headers = commit_header(commit)
    require(actual_tree == tree and sum(line.startswith('gpgsig ') for line in headers) == 1,
            'Completion tooling must be a signed commit of the approved tree')
    immutable_git('merge-base', '--is-ancestor', commit, 'refs/heads/release/2.0.0-alpha')
    actual = files(source)
    manifest = inventory.read_bytes()
    require(data['filesManifestSha256'] == hashlib.sha256(manifest).hexdigest() and checked_json(manifest) == actual and
            'prepared.json' in actual and data['preparedSha256'] == actual['prepared.json'],
            'Completion files missing, changed or extra')
    # Empty directories are not represented in the manifest.
    allowed_dirs = {p for name in actual for p in (source / name).parents if p != source and p.is_relative_to(source)}
    require(all(not path.is_dir() or path in allowed_dirs for path in source.rglob('*')), 'Extra completion directory')
    return data, actual


def build_for_row(row, baseline, candidate):
    if row.get('window') == 'cold' and baseline['commit'] == gate.PIN and baseline['runtime'] == '10.0.9' and candidate['runtime'] == '11.0.0':
        baseline = gate.cold_companion(baseline, candidate)
    return dict(baseline=baseline, candidate=candidate)


def bind_builds(rows, builds):
    """Bind every declared row to its ordered primary/alternate immutable build records."""
    require(rows and builds, 'Missing completion rows or builds')
    by_commit = {build['commit']: build for build in builds}
    require(len(by_commit) == len(builds), 'Ambiguous completion build records')
    baseline, candidate = builds[0], builds[min(1, len(builds) - 1)]
    require(set(by_commit) == {baseline['commit'], candidate['commit'], *(row.get('candidate', candidate['commit']) for row in rows)},
            'Stale or extra completion builds')
    for row in rows:
        right = by_commit[row.get('candidate', candidate['commit'])]
        require(row.get('sides') == build_for_row(row, baseline, right), 'Row build differs from completion records')
    return baseline, candidate


def verify_policy(seal, builds, receipt, local, *, fixtures_sha256=None):
    request = receipt['request']
    mode, kind = request['mode'], request['sealKind']
    require(seal.get('mode') == mode and seal.get('kind') == kind, 'Completion kind or mode differs from portable plan')
    tree = request['toolingTree']
    config = azure_config(checked_json(git_blob(tree, 'benchmarks/gate/gated.json')))
    audit = checked_json(git_blob(tree, f'{FIXTURES}/audit-allocations.json'))
    args = SimpleNamespace(mode=mode, plan=None, only=request.get('only'), baseline=request['baseline'], candidate=request['candidate'])
    entries = None
    if 'plan' in request:
        path = relative(request['plan'])
        require(path.suffix == '.json', 'Completion plan must be a repository-relative JSON file')
        body = git_blob(tree, path.as_posix())
        require(hashlib.sha256(body).hexdigest() == request['planSha256'], 'Completion plan digest differs')
        entries = checked_json(body)
        require(isinstance(entries, (dict, list)), 'Invalid completion selection plan')
    band_sources = tooling_bands(tree)
    rows = gate.select_rows(args, config, audit, plan_entries=entries, band_sources=band_sources)
    noise, areas = config['_noiseRecords'], config['_batchAreas']
    require(all(row['kind'] in ('time', 'replay') for row in rows) and (kind == 'noise') == noise and
            (kind not in BATCH_MODES or mode == BATCH_MODES[kind] and areas), 'Completion row, noise or batch policy differs')
    try:
        previous = gate.previous_alpha(args.candidate, receipt=True)
    except (OSError, subprocess.CalledProcessError) as error:
        raise ValueError('Completion preceding alpha is unresolvable') from error
    require(receipt['previousAlpha'] == previous, 'Completion previous-alpha selection differs')
    def resolve(value):
        require(isinstance(value, str) and value and value != '.' and not value.startswith('-'), 'Invalid completion alternate build')
        commit = immutable_git('rev-parse', '--verify', value + '^{commit}')
        require(matches(OID, commit), 'Invalid resolved completion build')
        return commit
    for row in rows:
        if 'candidate' in row:
            row['candidate'] = resolve(row['candidate'])
    ids = list(dict.fromkeys([args.baseline, args.candidate, *[row['candidate'] for row in rows if 'candidate' in row]]))
    require([build['commit'] for build in builds] == ids, 'Completion build selection differs')
    for build in builds:
        commit = build['commit']
        sdk, runtime, tfm = ('10.0.301', '10.0.9', 'net10.0') if commit == gate.PIN else ('11.0.100', '11.0.0', 'net11.0')
        require(build['treeId'] == commit_header(commit)[0] and (build['sdk'], build['runtime'], build['tfm']) == (sdk, runtime, tfm) and
                ('previousAlpha' in build) == (commit == args.baseline) and
                build.get('previousAlpha', False) == bool(previous and previous['commit'] == commit and commit == args.baseline),
                'Completion immutable build metadata differs')
    baseline, candidate = builds[0], builds[ids.index(args.candidate)]
    by_commit = dict(zip(ids, builds))
    for row in rows:
        row['sides'] = build_for_row(row, baseline, by_commit[row.get('candidate', args.candidate)])
    policy = portable_plan(mode, kind, request.get('orderingProof', False), rows, baseline, candidate, config, audit, local,
                           receipt['buildMarker'], band_sources=band_sources, fixtures_sha256=fixtures_sha256)
    require(gate.encoded({key: value for key, value in seal.items() if key != 'timePlans'}) == gate.encoded(policy),
            'Completion portable plan differs from the independently derived policy')


def portable_plan(mode, kind, ordering_proof, rows, baseline, candidate, config, audit, local, marker, *,
                  band_sources=None, fixtures_sha256=None):
    # Both hosts derive this policy from independently read inputs, never from the portable claim.
    replays = {row['id']: row['replay'] for row in bundle.time_rows(rows, dict(config, time=dict(config['time'], noise={})))}
    for row in rows:
        if row['id'] in replays:
            row['replay'] = replays[row['id']]
        row['corpusId'] = gate.corpus_identity(row, local, fixtures_sha256=fixtures_sha256)
    bands = gate.seal_bands(rows, band_sources=band_sources)
    return dict(schemaVersion=1, provider='azure', mode=mode, kind=kind, ruleVersion=gate.stats.RULE_VERSION,
                rows=rows, bands=bands, buildMarker=marker, canonical=canonical_rows(baseline, candidate, rows), timeScopeDecided=True,
                gated={key: value for key, value in config.items() if not key.startswith('_')},
                releaseSet=[row['id'] for row in gate.catalogue(config, audit) if row.get('rowClass') != 'recorded-only'],
                sessionPerRow=config['_sessionPerRow'], noiseRecords=config['_noiseRecords'],
                orderingProof=ordering_proof or config['_noiseRecords'], batchAreas=config['_batchAreas'])


def import_prepared(source, *, fetched, settings, local, check_cli, check_budget):
    source = source.absolute()
    receipt, actual = completion(source, fetched, local)
    prepared = json_file(source / 'prepared.json')
    require(bundle.sha(source / 'prepared.json') == receipt['preparedSha256'] and isinstance(prepared, dict),
            'Completion prepared manifest changed')
    validate_build_marker(prepared.get('buildMarker'))
    require(set(prepared) == {'schemaVersion', 'buildMarker', 'manifests', 'builds', 'files'} and type(prepared['schemaVersion']) is int and
            prepared['schemaVersion'] == 1 and prepared['buildMarker'] == receipt['buildMarker'],
            'Unsupported prepared manifest')
    portable_builds(prepared['builds'])
    expected = prepared['files']
    require(isinstance(expected, dict), 'Invalid prepared files')
    for name, value in expected.items():
        require(relative(name).as_posix() == name and matches(SHA, value), 'Invalid prepared file digest')
    require(set(expected) >= {'plan.json', 'build/sides.json', *(f'{MIRROR_DIR}/{prefix}.json' for prefix in MIRRORS),
                              *(f'input/azure/{name}' for name in host_files())} and
            all(re.fullmatch(r'(plan\.json|build/sides\.json|suite(?:-expected)?\.json|input/(?:azure/[^/]+|mirrors/(?:dotnet|python)\.json|session-[0-9]+\.tar\.gz|suite\.tar\.gz))', name)
                or REHEARSAL_FILES.fullmatch(name) for name in expected), 'Unrecognized or missing prepared file')
    actual.pop('prepared.json')
    require(actual == expected, 'Prepared file missing, changed or extra')
    expected = {name: value for name, value in expected.items() if not name.startswith('rehearsal/')}
    tree = receipt['request']['toolingTree']
    manifests, payload = inputs(local, mirror_in=source, tree=tree)
    require(manifests == prepared['manifests'], 'Prepared input manifests differ from PC inputs')
    require(all(manifests[prefix] == expected[f'{MIRROR_DIR}/{prefix}.json'] for prefix in MIRRORS),
            'Prepared runtime mirror inventory changed during import')
    require(json_file(source / 'build/sides.json') == prepared['builds'], 'Prepared build records differ')
    verify_host_scripts(expected, tree)
    output = gate.new_output()
    try:
        bundle.copy_files(source, output, files=expected)
        require(files(output) == expected, 'Prepared file changed during import')
        seal = json_file(output / 'plan.json')
        require(isinstance(seal, dict) and isinstance(seal.get('timePlans'), list), 'Invalid completion portable plan')
        require(seal.get('buildMarker') == prepared['buildMarker'], 'Portable plan build marker differs from handoff')
        require(seal['provider'] == 'azure' and seal['kind'] in KINDS and not
                {'files', 'platform', 'payloadManifests', 'programStopUsd', 'lineageCapUsd', 'retryOf'} & seal.keys(), 'Prepared plan carries PC authority')
        require({name for name in expected if re.fullmatch(r'input/session-[0-9]+\.tar\.gz', name)} ==
                {f'input/session-{number}.tar.gz' for number in range(len(seal['timePlans']))}, 'Unexpected prepared session archive')
        verify_policy(seal, prepared['builds'], receipt, local, fixtures_sha256=manifests['fixtures'])
        verify_timing(output, seal, payload, host_hashes=expected)
        if 'input/suite.tar.gz' in expected:
            verify_payload(output / 'input/suite.tar.gz', seal['buildMarker'], {})
            raise ValueError('PC suite closure verification requires the AZ3 importer')
        require(not {'suite.json', 'suite-expected.json'} & expected.keys(), 'Missing suite archive')
        require(json_file(settings) == local, 'Local settings changed during import')
        local_file = output / 'input/azure-local.json'
        local_file.touch(mode=0o600, exist_ok=False)
        local_file.write_bytes(settings.read_bytes())
        require(json_file(local_file) == local, 'Local settings changed during import')
        provider = timing.PROVIDERS['azure']
        version = check_cli(Path(local['azureCli']))
        require(re.fullmatch(r'\d+\.\d+\.\d+', version) is not None, 'Malformed Azure CLI version')
        seal.update(files=dict(expected, **{'input/azure-local.json': bundle.sha(local_file)}), payloadManifests=payload,
                    programStopUsd=40,
                    platform=dict(size=provider['machine'], regions=list(provider['regions']), image=provider['image'],
                                  azCliVersion=version))
        if seal['kind'] == 'controls':
            seal['lineageCapUsd'] = 6
        seal_id = hashlib.sha256(bundle.gate_encoded(seal)).hexdigest()
        bundle.checked_azure_local(seal)
        if seal['timePlans']:
            check_budget(local, seal, seal_id)
        require(gate.write_seal(output, seal) == seal_id, 'Seal serialization differed from budget binding')
    except BaseException:
        shutil.rmtree(output)
        raise
    return output


def command(args):
    require(hasattr(bundle, 'AZURE_GUEST_FILES'), 'Azure prepare requires the Azure provider seam')
    if args.build_only:
        root = CONTAINER_ROOT
        phase = os.environ.get('CHRYSALIS_CONTAINER_BUILD_PHASE')
        marker_path = root / 'build-marker.json'
        env = {'CHRYSALIS_CONTAINER_BUILD_MARKER': marker_path, 'CHRYSALIS_GATE_LOCAL': root / 'settings.json',
               'DOTNET_ROOT': root / 'sdk/11.0.100', 'CHRYSALIS_DOTNET10_ROOT': root / 'sdk/10.0.301',
               'NUGET_PACKAGES': root / 'packages', 'CHRYSALIS_NUGET_SOURCE': root / 'nuget'}
        require(sys.platform == 'linux' and os.uname().machine == 'x86_64' and 'microsoft' not in os.uname().release.lower()
                and not os.environ.get('WSL_DISTRO_NAME') and gate.REPO == Path.cwd() == root / 'project'
                and phase in ('restore', 'build') and all(os.environ.get(key) == str(path) for key, path in env.items()),
                'Build Azure payload through mbp-container build (Linux amd64 guest)')
        require(args.mode and args.baseline and args.candidate and args.output == CONTAINER_OUTPUT,
                'Build requires --mode, --baseline, --candidate and the container output path')
        if args.plan:
            relative(str(args.plan))
            require((gate.REPO / args.plan).resolve().is_relative_to(gate.REPO), 'Build plan must be inside the guest checkout')
        marker = json_file(marker_path)
        validate_build_marker(marker)
        _, local = checked_local(guest_root=root)
        return build(args, gate.REPO / CONTAINER_OUTPUT, local, marker=marker, phase=phase)
    require(sys.platform == 'linux' and os.environ.get('WSL_DISTRO_NAME'), 'Import Azure payload on the WSL launcher host')
    require(not any((args.mode, args.baseline, args.candidate, args.plan, args.only, args.output, args.seal_kind,
                     args.ordering_proof)), 'Import takes only --from; it cannot change the prepared plan')
    from gate_azure import Az, check_seal_budget
    settings, local = checked_local()
    return import_prepared(args.from_prepared, fetched=gate.REPO.parent / 'mbp-results', settings=settings, local=local,
                           check_cli=lambda path: Az(path).version(), check_budget=check_seal_budget)
