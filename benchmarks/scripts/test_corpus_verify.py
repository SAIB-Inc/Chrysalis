"""Offline controls for the independent verifier. A pinned preprod corpus is required."""
import contextlib
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import struct
import tempfile
import unittest

import corpus_verify as verifier


def argument(major, value):
    if value < 24:
        return bytes([major << 5 | value])
    for width, additional in [(1, 24), (2, 25), (4, 26), (8, 27)]:
        if value < 1 << (width * 8):
            return bytes([major << 5 | additional]) + value.to_bytes(width, 'big')
    raise ValueError('Integer too large')


def encoded_array(items):
    return argument(4, len(items)) + b''.join(items)


def encoded_bytes(data):
    return argument(2, len(data)) + data


def frame(protocol, payload, server=True):
    return struct.pack('>IHH', 0, protocol | (0x8000 if server else 0), len(payload)) + payload


class CborTests(unittest.TestCase):
    def test_rfc_8949_appendix_a(self):
        # Published encodings, including every major type and each float width.
        vectors = '''00 01 0a 17 1818 1819 1864 1903e8 1a000f4240 1b000000e8d4a51000
            1bffffffffffffffff 3bffffffffffffffff c249010000000000000000 c349010000000000000000
            20 29 3863 3903e7 f90000 f98000 f93c00 fb3ff199999999999a f93e00
            f97bff fa47c35000 fa7f7fffff fb7e37e43c8800759c f90001 f90400 f9c400
            fbc010666666666666 f97c00 f97e00 f9fc00 fa7f800000 fa7fc00000 faff800000
            fb7ff0000000000000 fb7ff8000000000000 fbfff0000000000000 f4 f5 f6 f7 f0 f818
            f8ff c074323031332d30332d32315432303a30343a30305a c11a514b67b0
            c1fb41d452d9ec200000 d74401020304 d818456449455446 d82076687474703a2f2f7777772e6578616d706c652e636f6d
            40 4401020304 60 6161 6449455446 62225c 62c3bc 63e6b0b4 64f0908591
            80 83010203 8301820203820405 98190102030405060708090a0b0c0d0e0f101112131415161718181819
            a0 a201020304 a26161016162820203 826161a161626163 a56161614161626142616361436164614461656145
            5f42010243030405ff 7f657374726561646d696e67ff 9fff 9f018202039f0405ffff
            9f01820203820405ff 83018202039f0405ff 83019f0203ff820405 bf61610161629f0203ffff
            826161bf61626163ff bf6346756ef563416d7421ff'''.split()
        for text in vectors:
            with self.subTest(vector=text):
                data = bytes.fromhex(text)
                self.assertEqual(verifier.decode(data).raw, data)
        self.assertEqual(verifier.decode(bytes.fromhex('3903e7')).value, -1000)
        self.assertEqual(verifier.decode(bytes.fromhex('f93e00')).value, 1.5)
        self.assertTrue(math.isnan(verifier.decode(bytes.fromhex('f97e00')).value))

    def test_conway_encodings_keep_spans_and_non_scalar_keys(self):
        for text in ['5f5840' + '00' * 64 + '4101ff', '7f6161626263ff', 'd90102820102',
                     'a1810001', 'a1058184000000820102', 'a105a18200008200820102',
                     '85008080a080', '85008080a100d90103a080', '850081a080a08100']:
            data = bytes.fromhex(text)
            self.assertEqual(verifier.decode(data).raw, data)
        item = verifier.decode(bytes.fromhex('a1810001'))
        self.assertEqual(item.children[0].major, 4)
        self.assertEqual(item.children[0].raw, bytes.fromhex('8100'))
        self.assertEqual(verifier.decode(bytes.fromhex('7f6161626263ff')).value, 'abc')

    def test_truncations_reserved_types_and_bad_breaks_fail(self):
        for text in ['', '18', '1c', 'ff', '9f01', 'bf01ff', '5f6161ff', '7f5fff', '4301', '0000', 'faff']:
            with self.subTest(vector=text), self.assertRaises((ValueError, UnicodeError)):
                verifier.decode(bytes.fromhex(text))

    def test_script_counts_both_redeemer_forms_and_empty_block(self):
        def parts(witness, body='a0'):
            return [None, verifier.decode(bytes.fromhex('81' + body)), verifier.decode(bytes.fromhex('81' + witness)), None, None]
        for witness in ['a1058184000000820102', 'a105a18200008200820102']:
            self.assertEqual(verifier.script_counts(parts(witness, 'a112d901028180')), (1, 1, 1))
        self.assertEqual(verifier.script_counts([None, verifier.decode(b'\x80'), verifier.decode(b'\x80'), None, None]), (0, 0, 0))


class DeclarationTests(unittest.TestCase):
    declaration = ('- Declaration time: 2026-11-16T23:04:42Z\n'
                   '- T0 = 2026-11-14T23:00:00Z\n'
                   '- Mainnet slot at T0: 203130909.\n')

    def test_matching_time_and_slot(self):
        self.assertEqual(verifier.read_declaration(self.declaration)['firstSlot'], 203130909)

    def test_wrong_slot(self):
        with self.assertRaisesRegex(ValueError, 'slot.*T0'):
            verifier.read_declaration(self.declaration.replace('203130909', '203130910'))

    def test_t0_one_hour_off(self):
        with self.assertRaisesRegex(ValueError, '48-hour, hour-rounded T0'):
            verifier.read_declaration(self.declaration.replace('23:00:00Z', '22:00:00Z'))

    def test_t0_rule_even_with_matching_shifted_slot(self):
        with self.assertRaisesRegex(ValueError, '48-hour, hour-rounded T0'):
            verifier.read_declaration(self.declaration.replace('23:00:00Z', '22:00:00Z')
                                      .replace('203130909', '203127309'))

    def test_real_dotnet_timestamp(self):
        for fraction, microseconds in [('9', 900000), ('94', 940000), ('946', 946000),
                                       ('9468', 946800), ('94686', 946860),
                                       ('946863', 946863), ('9468635', 946863)]:
            with self.subTest(fraction=fraction):
                actual = verifier.utc_time('2026-11-17T01:06:44.' + fraction + '+00:00')
                self.assertEqual(actual, dt.datetime(2026, 11, 17, 1, 6, 44, microseconds, tzinfo=dt.timezone.utc))
                with self.assertRaisesRegex(ValueError, 'must be UTC'):
                    verifier.utc_time('2026-11-17T02:06:44.' + fraction + '+01:00')


class CorpusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        configured = os.environ.get('CHRYSALIS_CORPORA')
        if not configured or not Path(configured).is_absolute():
            raise RuntimeError('Set CHRYSALIS_CORPORA to the absolute pinned-corpus root; no skip path')
        cls.source = Path(configured) / 'preprod-p1-n2n-recording-a'
        if not (cls.source / 'sha256.json').is_file():
            raise RuntimeError('The pinned preprod corpus is required; no skip path')
        cls.range = json.loads((cls.source / 'range.json').read_text())
        cls.result = verifier.verify(cls.source, local_node=True)
        cls.temporary_root = Path(__file__).resolve().parents[1] / 'out/corpus-verify-tests'
        cls.temporary_root.mkdir(parents=True, exist_ok=True)

    @contextlib.contextmanager
    def copy(self):
        with tempfile.TemporaryDirectory(dir=self.temporary_root) as temporary:
            root = Path(temporary) / 'input'
            shutil.copytree(self.source, root)
            for path in [root, *root.rglob("*")]:
                path.chmod(0o700 if path.is_dir() else 0o600)
            yield root

    def repin(self, root):
        if not (root / 'sha256.json').exists():
            return
        index = json.loads((root / 'sha256.json').read_text())
        for name in index:
            if (root / name).exists():
                index[name] = verifier.digest((root / name).read_bytes())
        (root / 'sha256.json').write_text(json.dumps(index))

    def rewrite_frames(self, root, transform, direction='server-to-client.bin'):
        path = root / direction
        path.write_bytes(b''.join(transform(verifier.parse_frames(path.read_bytes()))))
        self.repin(root)

    def rewrite_fetch(self, root, transform):
        def rewrite(frames):
            fetch = [f for f in frames if f['protocol'] == 3]
            new = transform(b''.join(f['payload'] for f in fetch))
            offset = 0
            for f in frames:
                if f['protocol'] != 3:
                    yield f['raw']
                    continue
                length = len(f['payload']) if f is not fetch[-1] else len(new) - offset
                yield frame(3, new[offset:offset + length])
                offset += length
            self.assertEqual(offset, len(new))
        self.rewrite_frames(root, rewrite)

    def test_preprod_end_to_end_identity_groups_and_input_unchanged(self):
        with tempfile.TemporaryDirectory(dir=self.temporary_root) as temporary:
            temporary = Path(temporary)
            before = {p.name: verifier.digest(p.read_bytes()) for p in self.source.iterdir() if p.is_file()}
            out = temporary / self.source.name
            path = temporary / 'manifest.json'
            report = verifier.write_verified(self.result, self.source, None, out, path, True)
            self.assertEqual(before, {p.name: verifier.digest(p.read_bytes()) for p in self.source.iterdir() if p.is_file()})
            manifest = json.loads(path.read_text())
            index = json.loads((out / 'sha256.json').read_text())
            self.assertNotIn('sha256.json', index)
            self.assertNotIn('corpusId', manifest)
            self.assertEqual(report['corpusId'], hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertEqual(manifest['index']['sha256'], verifier.digest((out / 'sha256.json').read_bytes()))
            for name, hash_text in index.items():
                self.assertEqual(verifier.digest((out / name).read_bytes()), hash_text)
            self.assertEqual(set(index), {'server-to-client.bin', 'client-to-server.bin', 'capture.json', 'range.json', 'corpus.json'})
            self.assertEqual((manifest['blocks'], manifest['rawBytes']), (1000, 4040750))
            self.assertEqual(manifest['tcpSendMode'], 'captured-chunks')
            self.assertIn('depth not checked', manifest['source']['verification'])
            self.assertEqual(manifest['tools']['verifier']['sha256'], verifier.digest(Path(verifier.__file__).read_bytes()))
            self.assertNotIn('repositoryCommit', manifest['tools']['verifier'])
        corpus = self.result['corpus']
        groups = corpus['bodies']
        self.assertEqual(len(corpus['headers']), 1000)
        self.assertEqual((len(groups), sum(not group for group in groups)), (1002, 383))
        frames = [f for f in verifier.parse_frames(self.result['payload']['server-to-client.bin']) if f['protocol'] == 3]
        self.assertEqual([part for group in groups for part in group],
                         [{'offset': f['offset'], 'length': len(f['raw'])} for f in frames])
        self.assertEqual(len(groups[0]), 1)  # StartBatch alone.
        self.assertFalse(groups[-1])  # BatchDone shares the last frame, already supplied.
        self.assertFalse(groups[-2])
        self.assertGreater(max(len(group) for group in groups), 1)  # A split message.
        self.assertEqual(self.result['summary']['blockMessagesSpanningFrames'], 178)
        self.assertEqual(self.result['summary']['depth'], 1053)

    def test_each_body_component_mutation_fails(self):
        for component in range(1, 5):
            with self.subTest(component=component), self.copy() as root:
                def mutate(data):
                    first = verifier.read(data).end
                    item = verifier.read(data, first)
                    block = verifier.embedded(item.children[1])
                    parts = list(block.children[1].children)
                    target = parts[component]
                    # Valid alternate container encoding, preserving semantics but changing its bytes.
                    changed = bytes([0x9f if target.major == 4 else 0xbf]) + b''.join(c.raw for c in target.children) + b'\xff'
                    encoded_parts = [p.raw if i != component else changed for i, p in enumerate(parts)]
                    body = encoded_array([block.children[0].raw, encoded_array(encoded_parts)])
                    message = encoded_array([b'\x04', b'\xd8\x18' + encoded_bytes(body)])
                    return data[:first] + message + data[item.end:]
                self.rewrite_fetch(root, mutate)
                with self.assertRaisesRegex(ValueError, 'Block body (size|hash) differs'):
                    verifier.verify(root, local_node=True)

    def test_body_hash_mutation_at_same_length_fails(self):
        with self.copy() as root:
            def mutate(data):
                first = verifier.read(data).end
                item = verifier.read(data, first)
                block = verifier.embedded(item.children[1])
                part = block.children[1].children[1]
                changed = bytearray(block.raw)
                # First unsigned transaction-body key is one byte; change it, not CBOR structure.
                key = verifier.fields(part.children[0])
                self.assertTrue(key)
                key_item = part.children[0].children[0]
                changed[key_item.start - block.start] ^= 1
                return data[:first] + encoded_array([b'\x04', b'\xd8\x18' + encoded_bytes(changed)]) + data[item.end:]
            self.rewrite_fetch(root, mutate)
            with self.assertRaisesRegex(ValueError, 'Block body hash differs'):
                verifier.verify(root, local_node=True)

    def test_swapped_headers_and_dropped_frame_fail(self):
        for mutation in ['swap', 'drop']:
            with self.subTest(mutation=mutation), self.copy() as root:
                def mutate(frames):
                    indices = [i for i, f in enumerate(frames) if f['protocol'] == 2]
                    raws = [f['raw'] for f in frames]
                    if mutation == 'swap':
                        a, b = indices[2:4]
                        raws[a], raws[b] = raws[b], raws[a]
                    else:
                        raws.pop(indices[2])
                    return raws
                self.rewrite_frames(root, mutate)
                with self.assertRaises(ValueError):
                    verifier.verify(root, local_node=True)

    def test_range_point_and_capture_digest_fail(self):
        for mutation in ['point', 'digest']:
            with self.subTest(mutation=mutation), self.copy() as root:
                capture = json.loads((root / 'capture.json').read_text())
                if mutation == 'point':
                    fixed_range = json.loads((root / 'range.json').read_text())
                    fixed_range['Blocks'][0]['Slot'] += 1
                    (root / 'range.json').write_text(json.dumps(fixed_range))
                    capture['rangeSha256'] = verifier.digest((root / 'range.json').read_bytes())
                else:
                    capture['blocks'][0]['sha256'] = '00' * 32
                (root / 'capture.json').write_text(json.dumps(capture))
                self.repin(root)
                with self.assertRaisesRegex(ValueError, 'range point|Capture block'):
                    verifier.verify(root, local_node=True)

    def mainnet_sources(self, root, deep=True):
        fixed_range = json.loads((root / 'range.json').read_text())
        t0 = dt.datetime.fromtimestamp(1596059091 + fixed_range['Blocks'][0]['Slot'] - 4492800,
                                       dt.timezone.utc).replace(minute=0, second=0, microsecond=0)
        boundary = 4492800 + int(t0.timestamp()) - 1596059091
        fixed_range['Anchor']['Slot'] = boundary - 1
        (root / 'range.json').write_text(json.dumps(fixed_range))
        declared = t0 + dt.timedelta(hours=48, minutes=4, seconds=42)
        capture = json.loads((root / 'capture.json').read_text())
        capture.update(status='ok', networkMagic=764824073, relay='backbone.cardano.iog.io',
                       startedUtc=(declared + dt.timedelta(minutes=2)).isoformat(),
                       completedUtc=(declared + dt.timedelta(minutes=3)).isoformat(),
                       anchorLookupUtc=(declared + dt.timedelta(seconds=1)).isoformat(timespec='microseconds').replace('+00:00', '5+00:00'),
                       rangeSha256=verifier.digest((root / 'range.json').read_bytes()))
        (root / 'capture.json').write_text(json.dumps(capture))
        (root / 'declaration.md').write_text(
            '- Declaration time: ' + declared.isoformat() + '\n'
            '- T0 = ' + t0.isoformat() + ' (placeholder weekday)\n'
            '- Mainnet slot at T0: ' + str(boundary) + '.\n')
        def mainnet_frames(frames):
            chain_index = 0
            for f in frames:
                if f['protocol'] == 0:
                    values = list(verifier.decode(f['payload']).children)
                    data = values[2].children
                    yield frame(0, encoded_array([values[0].raw, values[1].raw,
                                encoded_array([argument(0, 764824073), *[v.raw for v in data[1:]]])]))
                elif f['protocol'] == 2:
                    values = list(verifier.decode(f['payload']).children)
                    if chain_index < 2:
                        values[1] = verifier.decode(encoded_array([argument(0, boundary - 1),
                                                                  encoded_bytes(bytes.fromhex(fixed_range['Anchor']['Hash']))]))
                    tip_bytes = values[-1].raw
                    if deep:
                        tip_bytes = encoded_array([values[-1].children[0].raw,
                                    argument(0, self.result['summary']['tips'][-1]['blockNumber'] + 2160)])
                    yield frame(2, encoded_array([values[0].raw, values[1].raw, tip_bytes]))
                    chain_index += 1
                else:
                    yield f['raw']
        self.rewrite_frames(root, mainnet_frames)
        def request(frames):
            changed = False
            for f in frames:
                if f['protocol'] == 2 and not changed:
                    changed = True
                    anchor = encoded_array([argument(0, boundary - 1), encoded_bytes(bytes.fromhex(fixed_range['Anchor']['Hash']))])
                    yield frame(2, encoded_array([b'\x04', encoded_array([anchor])]), server=False)
                else:
                    yield f['raw']
            yield frame(8, b'\x81\x02', server=False)
        self.rewrite_frames(root, request, 'client-to-server.bin')
        second = root.parent / 'second'
        second.mkdir()
        frames = verifier.parse_frames((root / 'server-to-client.bin').read_bytes())
        (second / 'server-to-client.bin').write_bytes(b''.join(f['raw'] for f in frames if f['protocol'] != 3))
        outbound = verifier.parse_frames((root / 'client-to-server.bin').read_bytes())
        (second / 'client-to-server.bin').write_bytes(b''.join(f['raw'] for f in outbound if f['protocol'] != 3 or f['payload'] == b'\x81\x01'))
        capture.update(relay='backbone.mainnet.cardanofoundation.org', blocks=[], rawBytes=0)
        (second / 'capture.json').write_text(json.dumps(capture))
        return second

    def change_last_header(self, root, field=None, value=None, variant=6):
        frames = verifier.parse_frames((root / 'server-to-client.bin').read_bytes())
        last = [f for f in frames if f['protocol'] == 2][-1]
        reply = verifier.decode(last['payload'])
        header = verifier.embedded(reply.children[1].children[1])
        body, signature = header.children
        fields = [v.raw for v in body.children]
        if field is None:
            changed = bytearray(signature.raw)
            changed[-1] ^= 1
            signature_bytes = bytes(changed)
        else:
            fields[field] = value
            signature_bytes = signature.raw
        changed_header = encoded_array([encoded_array(fields), signature_bytes])
        wrapped = encoded_array([argument(0, variant), b'\xd8\x18' + encoded_bytes(changed_header)])
        last['raw'] = frame(2, encoded_array([reply.children[0].raw, wrapped, reply.children[2].raw]))
        fixed_range = json.loads((root / 'range.json').read_text())
        fixed_range['Blocks'][-1].update(Hash=verifier.blake(changed_header).hex(),
                                         Slot=verifier.header(verifier.decode(changed_header))['Slot'])
        self.rewrite_frames(root, lambda _: [f['raw'] for f in frames])
        return fixed_range

    def test_each_chain_guard_isolated(self):
        for mutation, expected in [('previous', 'Broken header previous-hash chain'),
                                   ('slot', 'Header block number or slot'),
                                   ('height', 'Header block number or slot'),
                                   ('variant', 'Expected Conway header variant 6')]:
            with self.subTest(mutation=mutation), self.copy() as root:
                if mutation == 'previous':
                    fixed_range = self.change_last_header(root, 2, encoded_bytes(bytes(32)))
                elif mutation == 'slot':
                    fixed_range = self.change_last_header(root, 1, argument(0, self.range['Blocks'][-2]['Slot']))
                elif mutation == 'height':
                    frames = verifier.parse_frames((root / 'server-to-client.bin').read_bytes())
                    last = [f for f in frames if f['protocol'] == 2][-1]
                    height = verifier.header(verifier.embedded(verifier.decode(last['payload']).children[1].children[1]))['blockNumber']
                    fixed_range = self.change_last_header(root, 0, argument(0, height + 1))
                else:
                    fixed_range = self.change_last_header(root, variant=5)
                frames = [f for f in verifier.parse_frames((root / 'server-to-client.bin').read_bytes()) if f['protocol'] == 2]
                with self.assertRaisesRegex(ValueError, expected):
                    verifier.chain(frames, fixed_range)

    def test_block_era_and_header_guards_isolated(self):
        for mutation, expected in [('era', 'Expected Conway block era 7'),
                                   ('header', 'Block header differs from chain-sync header')]:
            with self.subTest(mutation=mutation), self.copy() as root:
                changed = {}
                def mutate(data):
                    items, offset = [], 0
                    while offset < len(data):
                        item = verifier.read(data, offset)
                        items.append(item)
                        offset = item.end
                    message = items[-2]
                    block = verifier.embedded(message.children[1])
                    parts = [v.raw for v in block.children[1].children]
                    if mutation == 'header':
                        alternate = bytearray(parts[0])
                        alternate[-1] ^= 1
                        parts[0] = bytes(alternate)
                    raw = encoded_array([argument(0, 6 if mutation == 'era' else 7), encoded_array(parts)])
                    changed.update(raw=raw)
                    return data[:message.start] + encoded_array([b'\x04', b'\xd8\x18' + encoded_bytes(raw)]) + data[message.end:]
                self.rewrite_fetch(root, mutate)
                fixed_range = json.loads((root / 'range.json').read_text())
                fixed_range['Blocks'][-1]['Sha256'] = verifier.digest(changed['raw'])
                (root / 'range.json').write_text(json.dumps(fixed_range))
                capture = json.loads((root / 'capture.json').read_text())
                capture['blocks'][-1] = {'bytes': len(changed['raw']), 'sha256': verifier.digest(changed['raw'])}
                capture['rangeSha256'] = verifier.digest((root / 'range.json').read_bytes())
                (root / 'capture.json').write_text(json.dumps(capture))
                self.repin(root)
                with self.assertRaisesRegex(ValueError, expected):
                    verifier.verify(root, local_node=True)

    def test_range_file_and_per_block_digest_guards_isolated(self):
        for mutation, expected in [('range-file', 'Range file hash differs from capture'),
                                   ('block', 'Block digest differs from range digest')]:
            with self.subTest(mutation=mutation), self.copy() as root:
                capture = json.loads((root / 'capture.json').read_text())
                if mutation == 'block':
                    fixed_range = json.loads((root / 'range.json').read_text())
                    fixed_range['Blocks'][-1]['Sha256'] = '00' * 32
                    (root / 'range.json').write_text(json.dumps(fixed_range))
                    capture['rangeSha256'] = verifier.digest((root / 'range.json').read_bytes())
                else:
                    capture['rangeSha256'] = '00' * 32
                (root / 'capture.json').write_text(json.dumps(capture))
                self.repin(root)
                with self.assertRaisesRegex(ValueError, expected):
                    verifier.verify(root, local_node=True)

    def test_handshake_and_each_protocol_done_on_both_relays(self):
        for source in ['A', 'B']:
            for mutation in ['accept', 'version', 'magic', 2, 3, 8]:
                with self.subTest(source=source, mutation=mutation), self.copy() as root:
                    second = self.mainnet_sources(root)
                    target = root if source == 'A' else second
                    direction = 'server-to-client.bin' if isinstance(mutation, str) else 'client-to-server.bin'
                    def mutate(frames):
                        selected = [f for f in frames if f['protocol'] == (0 if isinstance(mutation, str) else mutation)][-1]
                        if isinstance(mutation, str):
                            values = list(verifier.decode(selected['payload']).children)
                            data = [v.raw for v in values[2].children]
                            data[0] = argument(0, 764824074 if mutation == 'magic' else 764824073)
                            payload = encoded_array([argument(0, 2 if mutation == 'accept' else 1),
                                                      argument(0, 15 if mutation == 'version' else 14), encoded_array(data)])
                        else:
                            payload = b'\x81\x00'
                        selected['raw'] = frame(selected['protocol'], payload, server=isinstance(mutation, str))
                        return [f['raw'] for f in frames]
                    self.rewrite_frames(target, mutate, direction)
                    with self.assertRaisesRegex(ValueError, 'handshake' if isinstance(mutation, str) else 'MsgDone'):
                        verifier.verify(root, relay_b=second)

    def test_mainnet_second_operator_shallow_tip_and_relay_mismatch(self):
        for mutation in ['none', 'shallow', 'same-operator', 'header']:
            with self.subTest(mutation=mutation), self.copy() as root:
                second = self.mainnet_sources(root, deep=mutation != 'shallow')
                if mutation == 'same-operator':
                    capture = json.loads((second / 'capture.json').read_text())
                    capture['relay'] = 'backbone.cardano.iog.io'
                    (second / 'capture.json').write_text(json.dumps(capture))
                elif mutation == 'header':
                    (second / 'range.json').write_bytes((root / 'range.json').read_bytes())
                    changed_range = self.change_last_header(second)
                    (second / 'range.json').write_text(json.dumps(changed_range))
                if mutation == 'none':
                    result = verifier.verify(root, relay_b=second)
                    self.assertGreaterEqual(result['summary']['depth'], 2160)
                    self.assertEqual(result['payload']['declaration.md'], (root / 'declaration.md').read_bytes())
                    self.assertLessEqual(result['source']['declaration']['firstSlot'], self.range['Blocks'][0]['Slot'])
                    self.assertNotEqual(result['source']['relayA']['operator'], result['source']['relayB']['operator'])
                else:
                    expected = {'shallow': '2,160 blocks deep', 'same-operator': 'different operator', 'header': 'Header differs from range point'}[mutation]
                    with self.assertRaisesRegex(ValueError, expected):
                        verifier.verify(root, relay_b=second)

    def test_declaration_boundary_mutations_fail(self):
        for hours in [-1, 1]:
            with self.subTest(hours=hours), self.copy() as root:
                second = self.mainnet_sources(root)
                path = root / 'declaration.md'
                declaration = verifier.read_declaration(path.read_text())
                declared = dt.datetime.fromisoformat(declaration['declaredUtc']) + dt.timedelta(hours=hours)
                t0 = dt.datetime.fromisoformat(declaration['t0Utc']) + dt.timedelta(hours=hours)
                path.write_text('- Declaration time: ' + declared.isoformat() + '\n'
                                '- T0 = ' + t0.isoformat() + '\n'
                                '- Mainnet slot at T0: ' + str(declaration['firstSlot'] + hours * 3600) + '.\n')
                with self.assertRaisesRegex(ValueError, 'declaration boundary'):
                    verifier.verify(root, relay_b=second)

    def test_declaration_must_precede_first_fetch(self):
        with self.copy() as root:
            second = self.mainnet_sources(root)
            capture = json.loads((root / 'capture.json').read_text())
            capture['anchorLookupUtc'] = verifier.read_declaration((root / 'declaration.md').read_text())['declaredUtc']
            (root / 'capture.json').write_text(json.dumps(capture))
            with self.assertRaisesRegex(ValueError, 'declaration.*first fetch'):
                verifier.verify(root, relay_b=second)

    def test_changed_pinned_input_and_unsafe_output_fail(self):
        with self.copy() as root:
            path = root / 'client-to-server.bin'
            path.write_bytes(path.read_bytes() + b'\x00')
            with self.assertRaisesRegex(ValueError, 'Pinned input hash differs'):
                verifier.verify(root, local_node=True)
        with self.assertRaisesRegex(ValueError, 'inside an input'):
            verifier.write_verified(self.result, self.source, None, self.source / 'new', self.source.parent / 'manifest.json', True)
        with self.assertRaisesRegex(ValueError, 'inside an input'):
            verifier.write_verified(self.result, self.source, None, self.source, self.source.parent / 'manifest.json', True)


if __name__ == '__main__':
    unittest.main()
