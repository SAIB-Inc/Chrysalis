"""Usage: python3 GenerateX30WireForms.py <corpora-directory> [--check]; no network."""
import argparse
from collections.abc import Mapping
from functools import cache
from hashlib import sha256
from importlib.metadata import version
from io import BytesIO
import json
from pathlib import Path
import sys

import cbor2._decoder as oracle

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[2] / 'benchmarks/scripts'))
from corpus_verify import embedded, messages, parse_frames, read, tip

ORACLE = {'repository': 'agronholm/cbor2', 'version': '5.8.0', 'path': 'cbor2/_decoder.py', 'license': 'MIT',
          'sha256': 'cd13bfdd6cef5ce69d734738d608632241bb0b8e63a1e9ef56e7fb2b4bc40923'}
CAPTURES = {
    'mainnet-203130896': ('01007e6529e417bd252f91031fdf6f81afd8df300e744a8694754c11cf307e44', 764824073),
    'preprod-p1-n2n-recording-a': ('25825d614d8536a366777df653268026bd543e5737f8ef774dffd57d53957772', 1),
}


def digest(data):
    return sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


@cache
def read_capture(corpora, capture):
    folder = corpora / capture
    path = folder / 'server-to-client.bin'
    data = path.read_bytes()
    pin, magic = CAPTURES[capture]
    require(digest(data) == pin, f'Capture SHA-256 changed: {capture}')
    points = json.loads((folder / 'range.json').read_text())['Blocks']
    frames = parse_frames(data)
    sync = messages([frame for frame in frames if frame['protocol'] == 2])
    tools = json.loads((folder / 'capture.json').read_text()).get('tools', {})
    tool_pin = {key: tools[key] for key in ('repositoryCommit', 'harnessSha256', 'harnessAssemblySha256') if key in tools}
    provenance = dict(capture=capture, path=path.name, sha256=pin, networkMagic=magic,
                      source='Public Cardano chain data via recorded node-to-node BlockFetch',
                      captureTool=tool_pin or 'not recorded', nodeVersion='not recorded',
                      tipSlots=dict(firstRecordedChainSyncReply=tip(sync[0].children[-1])['Slot'],
                                    lastRecordedChainSyncReply=tip(sync[-1].children[-1])['Slot']))
    replies = messages([frame for frame in frames if frame['protocol'] == 3])
    require(len(replies) == len(points) + 2 and len(points) == 1000, f'Unexpected block count: {capture}')
    blocks = {point['Slot']: (embedded(reply.children[1]).raw, point['Sha256'])
              for point, reply in zip(points, replies[1:-1], strict=True)}
    return provenance, blocks


def chain_slice(corpora, network, slot, offset):
    capture = next(name for name in CAPTURES if name.startswith(network))
    _, blocks = read_capture(corpora, capture)
    block, block_sha = blocks[slot]
    require(digest(block) == block_sha, f'Block SHA-256 changed: {capture} slot {slot}')
    data = read(block, offset).raw
    return data, dict(capture=capture, slot=slot, offset=offset, length=len(data),
                      blockSha256=block_sha, sliceSha256=digest(data))


def decode(data):
    stream = BytesIO(data)
    value = oracle.CBORDecoder(stream).decode()
    require(not stream.read(1), 'Trailing bytes in oracle fixture')
    return value


def semantic(value):
    if isinstance(value, bytes):
        return {'bytes': value.hex().upper()}
    if isinstance(value, (list, tuple)):
        return [semantic(item) for item in value]
    if isinstance(value, Mapping):
        return {'map': [[semantic(key), semantic(item)] for key, item in value.items()]}
    return value


def fixtures(corpora):
    # Select the covered child, not unrelated siblings; decode the entire root before selecting.
    def real(name, network, slot, offset, target=lambda x: x):
        data, source = chain_slice(corpora, network, slot, offset)
        return row(name, data, target, {'chainSlice': source})

    def built(name, hex_prefix, hex_suffix='', basis=None, target=lambda x: x):
        original, source = chain_slice(corpora, *basis) if basis else (b'', None)
        return row(name, bytes.fromhex(hex_prefix) + original + bytes.fromhex(hex_suffix), target,
                   {'handBuilt': True, **({'chainSlice': source} if source else {})})

    def row(name, data, target, source):
        return dict(id=name, hex=data.hex().upper(), sha256=digest(data), expected=semantic(target(decode(data))), **source)

    def first(x):
        return x[0]

    def last(x):
        return x[-1]

    def map_value(x):
        return next(iter(x.values()))

    def tag_value(x):
        return x.value

    byte_basis = ('preprod', 139056374, 19519)
    yield real('DefArrayRoot', 'mainnet', 203139970, 15034)
    yield real('DefArrayList', 'mainnet', 203139970, 15033, first)
    yield real('DefArrayMap', 'mainnet', 203134789, 60351, map_value)
    yield real('DefArrayTag', 'mainnet', 203130971, 38271, tag_value)
    yield real('IndefArrayRoot', 'preprod', 139060371, 4813)
    yield real('IndefArrayList', 'preprod', 139060371, 4812, first)
    yield real('IndefArrayMap', 'mainnet', 203146167, 7684, lambda x: next(iter(x)))
    yield real('IndefArrayTag', 'preprod', 139061133, 3276, tag_value)
    yield real('DefMapRoot', 'mainnet', 203131002, 27936)
    yield real('DefMapList', 'mainnet', 203131495, 16528, last)
    yield real('DefMapMap', 'mainnet', 203131002, 27934, map_value)
    yield real('DefMapTag', 'mainnet', 203131002, 27931, tag_value)
    yield real('IndefMapRoot', 'mainnet', 203134887, 3615)
    yield real('IndefMapList', 'mainnet', 203134887, 3609, last)
    yield real('IndefMapMap', 'mainnet', 203149504, 3573, lambda x: list(x.values())[-1])
    yield built('IndefMapTag', 'D9EA60', basis=('mainnet', 203134887, 3615), target=tag_value)
    yield real('BytesRoot', *byte_basis)
    yield real('BytesList', 'preprod', 139056374, 19518, first)
    yield built('BytesMap', 'A1190100', basis=byte_basis, target=map_value)
    yield built('BytesTag', 'D9EA60', basis=byte_basis, target=tag_value)
    # Tag 24 wraps the bytes of a CBOR array [1, 2] split across two chunks.
    yield built('BytesEncoded', 'D8185F4182420102FF', target=tag_value)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('corpora', type=Path)
    parser.add_argument('--check', action='store_true', help='compare without modifying the manifest')
    args = parser.parse_args()
    require(version('cbor2') == ORACLE['version'] and digest(Path(oracle.__file__).read_bytes()) == ORACLE['sha256'],
            'Wrong cbor2 version or decoder SHA-256')
    captures = [read_capture(args.corpora, name)[0] for name in CAPTURES]
    rows = list(fixtures(args.corpora))
    manifest = ('{\n  "oracle": ' + json.dumps(ORACLE) + ',\n  "captures": ' + json.dumps(captures) +
                ',\n  "fixtures": [\n    ' + ',\n    '.join(json.dumps(row) for row in rows) + '\n  ]\n}\n')
    path = HERE / 'X30WireForms.json'
    if args.check:
        require(path.read_text() == manifest, 'Wire fixtures differ from pinned captures or oracle')
    else:
        path.write_text(manifest)
    print(f'{len(rows)} vectors verified against cbor2 and the original slices against pinned captures')
