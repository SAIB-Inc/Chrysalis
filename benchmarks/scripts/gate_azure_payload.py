"""Pre-upload provenance check; the archive's own manifest is never authority."""
import hashlib
import json
from pathlib import PurePosixPath
import re
import tarfile

from gate_azure_guest import MAX_BLOB_BYTES, SENSITIVE_PATH, SENSITIVE
from gate_time import require


BUILD_MARKER = 'container-build.json'
DEPS_FILES = ('ReplayClient.deps.json', 'Chrysalis.Benchmarks.deps.json')
CAPSTONE = 'runtimes/linux-x64/native/libcapstone.so'
# MX's reviewed Linux/amd64 image and SDK archive checksums. The build runner
# itself stays unapproved until both tooling seats review its frozen digest.
BUILD_RUNNER_SHA256 = None
BUILD_IMAGE_DIGEST = 'sha256:36811ba1de0179e92a9537453e4ec8eb70b1af8dd926a9802c17a252f2c506ce'
BUILD_SDK_SHA512 = {
    '11.0.100': '1bec9cca2ec689b1e80245b84f2b53c9441d1aa6daccf2ceb27490768bffa95954fa585bd275def31069d874d543d9ba9e6591fe9ade2dbd9f34ec5c0fd0050c',
    '10.0.301': 'cfbeec3a3a1d3ad3e168e37a77c4cc26c23125acd84a86d014047da3ecffce4c368a9acac4d7c950a047fa3d98989ce8aea69f8e5842cb6d330e8911e1c335a7',
}


def validate_build_marker(marker, expected=None):
    approved = dict(schemaVersion=1, mode='build', runnerSha256=BUILD_RUNNER_SHA256,
                    imageDigest=BUILD_IMAGE_DIGEST, sdkSha512=BUILD_SDK_SHA512)
    if (BUILD_RUNNER_SHA256 is None or not isinstance(marker, dict) or type(marker.get('schemaVersion')) is not int or
            marker != approved or expected is not None and marker != expected):
        raise ValueError('Missing or invalid Linux/amd64 container build marker')
    return marker


def validate_deps(value, tfm, name, *, require_capstone=True):
    """Require the active target's native assets for this Linux-x64 executable."""
    bad = f'{name} does not resolve Linux-x64 native assets'
    require(isinstance(value, dict) and isinstance(value.get('runtimeTarget'), dict) and
            isinstance(value.get('targets'), dict) and isinstance(value.get('libraries'), dict), bad)
    require(tfm in ('net10.0', 'net11.0'), bad)
    target = value['runtimeTarget'].get('name')
    framework = '.NETCoreApp,Version=v' + tfm.removeprefix('net')
    require(target in (framework, framework + '/linux-x64') and
            isinstance(value['targets'].get(target), dict) and value['targets'][target], bad)
    graph = value['targets'][target]
    capstone, selected = set(), set()
    for library, assets in graph.items():
        require(library in value['libraries'] and isinstance(assets, dict), bad)
        native = set()
        for kind in ('native', 'runtimeTargets'):
            entries = assets.get(kind, {})
            require(isinstance(entries, dict), bad)
            for path, entry in entries.items():
                require(isinstance(entry, dict), bad)
                if kind == 'runtimeTargets':
                    require(entry.get('assetType') in ('runtime', 'native', 'resources'), bad)
                    if entry['assetType'] != 'native':
                        continue
                    require(entry.get('rid') == 'linux-x64', bad)
                require(re.fullmatch(r'(?:runtimes/linux-x64/native/)?[A-Za-z0-9_.+-]+\.so(?:\.[0-9]+)*', path) is not None and
                        (path.startswith('runtimes/linux-x64/native/') or target.endswith('/linux-x64')), bad)
                native.add(path)
        if library.lower().startswith('gee.external.capstone/'):
            require(native == {CAPSTONE}, bad)
            capstone.add(library)
        selected.update(native)
    require(len(capstone) <= 1 and (not require_capstone or capstone), bad)
    return selected

def verify(bundle, expected, *, manifest=None, marker=None, inspect=()):
    def need(value):
        if not value:
            raise ValueError('Azure archive member/provenance check failed')

    need(0 < bundle.stat().st_size <= MAX_BLOB_BYTES)
    names = set(expected) | ({manifest} if manifest else set())
    dirs = {p.as_posix() for name in names for p in PurePosixPath(name).parents if p.as_posix() != '.'}
    seen, written, expanded, objects = set(), set(), 0, {}
    with tarfile.open(bundle, 'r:gz') as stream:
        for entry in stream:
            name = entry.name
            parts = PurePosixPath(name).parts
            need(name not in seen and not SENSITIVE_PATH.search(name) and not entry.mode & 0o7000
                 and name == PurePosixPath(name).as_posix() and not name.startswith('/') and '..' not in parts)
            seen.add(name)
            if entry.isdir():
                need(name in dirs)
                continue
            expanded += entry.size
            need(entry.isreg() and name in names and 0 <= entry.size <= 160_000_000 and expanded <= 550_000_000)
            contents = stream.extractfile(entry).read()
            need(not SENSITIVE.search(contents) if name.rsplit('.', 1)[-1] in ('json', 'py', 'sh') else True)
            if name == BUILD_MARKER and marker is not None:
                validate_build_marker(json.loads(contents), marker)
            if name == manifest:
                need(json.loads(contents) == expected)
            else:
                need(hashlib.sha256(contents).hexdigest() == expected[name])
            if name in inspect:
                objects[name] = json.loads(contents)
            written.add(name)
    need(written == names)
    return objects


def allowed_members(plan, worker, seal, deps):
    from gate_bundle import AZURE_GUEST_FILES
    from gate_schedule import build_id
    inputs = plan['inputSha256']
    # Read-only manifests of the mirrors are independently checked during import.
    expected = {name: digest for name, digest in seal['payloadManifests'].items() if name in inputs}
    if any(not name.startswith(('dotnet/', 'python/', 'benchmarks/', 'corpora/')) for name in expected):
        raise ValueError('Unrecognized Azure mirror manifest member')
    specs = [side for row in seal['rows'] for side in row.get('sides', {}).values()]
    specs.extend(worker.get('builds', {}).values())
    client_dlls = {f'clients/{build_id(spec)}/{name}': digest for spec in specs if 'dllSha256s' in spec
                   for name, digest in spec['dllSha256s'].items()}
    # A repeat is a sealed subset but retains the original measured assemblies.
    expected.update({name: digest for name, digest in client_dlls.items() if name in inputs})
    for build in worker.get('builds', {}).values():
        root = PurePosixPath(build['client']).parent
        if root.as_posix() != f'clients/{build_id(build)}' or any(
                inputs.get((root / name).as_posix()) != digest for name, digest in build['dllSha256s'].items()):
            raise ValueError('Sealed worker build closure differs')
    permitted = {'prelude.json', BUILD_MARKER} | {'scripts/' + name for name in AZURE_GUEST_FILES}
    roots = {f'clients/{build_id(spec)}': spec for spec in specs if 'dllSha256s' in spec and 'tfm' in spec}
    native = {f'{file.parent}/{asset}' for name, graph in deps.items()
              if (file := PurePosixPath(name)).parent.as_posix() in roots
              for asset in validate_deps(graph, roots[file.parent.as_posix()]['tfm'], name)}
    for name, digest in inputs.items():
        if name in expected:
            continue
        file = PurePosixPath(name)
        if (name in permitted or name in native or str(file.parent) in roots and
                (file.name in DEPS_FILES or file.name.endswith('.runtimeconfig.json'))):
            if name.startswith('scripts/') and digest != seal['files'].get('input/azure/' + file.name):
                raise ValueError('Azure worker script differs from launcher snapshot')
            expected[name] = digest
    if inputs != expected or not native <= inputs.keys():
        raise ValueError('Azure input paths or digests differ from the sealed inputs')


def verify_inputs(directory, seal, plan, info):
    from gate_bundle import sha
    bundle = info['bundle']
    worker = info['plan']
    marker = validate_build_marker(seal.get('buildMarker'))
    inputs = plan['inputSha256']
    if BUILD_MARKER not in inputs:
        raise ValueError('Missing container build marker in sealed payload members')
    # Hash the archive against the seal before trusting any deps metadata.
    body = json.dumps(worker, indent=2, allow_nan=False) + '\n'
    expected = dict(inputs, **{'plan.json': hashlib.sha256(body.encode()).hexdigest()})
    deps = verify(bundle, expected, manifest='bundle-manifest.json', marker=marker,
                  inspect={name for name in inputs if PurePosixPath(name).name in DEPS_FILES})
    allowed_members(plan, worker, seal, deps)
    if sha(bundle) != info['sha256']:
        raise ValueError('Azure bundle changed after verification')
    result = {'payload': bundle}
    if plan.get('suite'):
        suite = directory / 'input/suite.tar.gz'
        digest = seal['files'].get('input/suite.tar.gz')
        if sha(suite) != digest:
            raise ValueError('Azure suite changed before verification')
        members = seal['suite']['members']
        if BUILD_MARKER not in members:
            raise ValueError('Missing container build marker in sealed suite members')
        verify(suite, members, marker=marker)
        if sha(suite) != digest:
            raise ValueError('Azure suite changed after verification')
        result['suite'] = suite
    return result
