"""Regenerate hand-built wire fixtures offline, or verify the manifest with --check."""
import argparse
from hashlib import sha256
from importlib.metadata import version
from io import BytesIO
import json
from pathlib import Path

import cbor2._decoder as oracle

ORACLE = dict(repository='agronholm/cbor2', version='5.8.0', path='cbor2/_decoder.py', license='MIT',
              sha256='cd13bfdd6cef5ce69d734738d608632241bb0b8e63a1e9ef56e7fb2b4bc40923')


def decode(data):
    stream = BytesIO(data + b'\x00')
    result = oracle.CBORDecoder(stream).decode()
    if stream.tell() != len(data) or stream.read() != b'\x00':
        raise ValueError('Wrong wire consumption or sentinel')
    return result


def semantic(value):
    while isinstance(value, oracle.CBORTag):
        value = value.value
    if isinstance(value, bytes):
        return value.hex().upper()
    if isinstance(value, (tuple, list)):
        return [semantic(item) for item in value]
    return value


def fixtures():
    def row(name, wire, project=lambda value: value, **provenance):
        data = bytes.fromhex(wire)
        return dict(id=name, hex=data.hex().upper(), sha256=sha256(data).hexdigest(),
                    expected=semantic(project(decode(data))), **provenance)

    def entry(value):
        return next(iter(value.items()))

    def magnitude(value):
        return value.to_bytes((value.bit_length() + 7) // 8, 'big')

    chunked = '5F4182420102FF'
    tagged = 'D9EA60' + chunked
    encoded = 'D818' + chunked
    yield row('BareMemory', chunked)
    yield row('TaggedMemory', tagged)
    yield row('TaggedEmptyMemory', 'D9EA605F4040FF')
    yield row('TaggedDefiniteMemory', 'D9EA6043820102', payloadOffset=4)
    yield row('ListMemory', '82' + tagged + 'D9EA605F410340FF')
    yield row('MapMemory', 'A1' + tagged + 'D9EA605F410340FF', entry)
    yield row('EagerEncodedBare', chunked)
    yield row('EagerEncodedNestedTags', 'D9EA60' + encoded)
    yield row('BigUintMagnitude', 'C2' + chunked, magnitude)
    yield row('ScriptBytes', '8201' + tagged, lambda x: x[1])
    yield row('DatumHash', '8200D9EA605F50' + '01' * 16 + '50' + '02' * 16 + 'FF', lambda x: x[1])
    yield row('InlineDatum', '8201' + encoded, lambda x: x[1])
    yield row('ScriptReference', 'A300581D61' + '00' * 28 + '010003' + encoded, lambda x: x[3])
    yield row('ScriptWitnesses', 'A10381' + tagged, lambda x: x[3][0])
    yield row('TaggedDefiniteText', 'D9EA606661C3A9E282AC')  # a, é, €

    # The proposed tag (60000) has no semantic conversion in cbor2. Decode its
    # complete contents; the paired array forms differ only in wire framing.
    yield row('UnknownEraTaggedIndefinite', 'D9EA609F08F6FF', indefinite=True)
    yield row('UnknownEraTaggedDefinite', 'D9EA608208F6', indefinite=False)
    # Minimal Byron block shell; only the kind of the third header member is read.
    yield row('ByronTaggedArrayProof', '82018383F6F6D9EA6080F6F6', lambda x: isinstance(x[1][0][2].value, list))
    yield row('ByronTaggedBytesProof', '82018383F6F6D9EA6040F6F6', lambda x: isinstance(x[1][0][2].value, list))

    # The last child is itself indefinite: dropping all trailing FF bytes would corrupt it.
    ex_units = '9F186418C8FF'
    joined = '840000' + chunked + ex_units
    value = decode(bytes.fromhex(joined))
    for key_indef, value_indef in ((False, False), (True, False), (False, True), (True, True)):
        key = '9F0000FF' if key_indef else '820000'
        datum = chunked + ex_units
        map_value = '9F' + datum + 'FF' if value_indef else '82' + datum
        name = f'Redeemer{int(key_indef)}{int(value_indef)}'
        yield row(name, 'A1' + key + map_value, lambda x: (*entry(x)[0], *entry(x)[1]), joined=joined)
        if decode(bytes.fromhex('A1' + key + map_value)) != {tuple(value[:2]): value[2:]}:
            raise ValueError(f'Joined redeemer changes semantics: {name}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    if version('cbor2') != ORACLE['version'] or sha256(Path(oracle.__file__).read_bytes()).hexdigest() != ORACLE['sha256']:
        raise SystemExit('Wrong cbor2 version or decoder SHA-256')
    rows = list(fixtures())
    manifest = ('{\n  "oracle": ' + json.dumps(ORACLE) + ',\n  "fixtures": [\n    ' +
                ',\n    '.join(json.dumps(row) for row in rows) + '\n  ]\n}\n')
    path = Path(__file__).with_name('X32WireForms.json')
    if args.check:
        if path.read_text() != manifest:
            raise SystemExit('Wire fixtures differ from the pinned oracle')
    else:
        path.write_text(manifest)
    print(f'{len(rows)} wire vectors verified against cbor2')
