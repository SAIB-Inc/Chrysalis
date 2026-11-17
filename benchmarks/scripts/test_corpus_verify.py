"""Offline controls for the independent verifier. A pinned preprod corpus is required."""
import contextlib
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


def frame(protocol, payload):
    return struct.pack('>IHH', 0, protocol | 0x8000, len(payload)) + payload


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

    def rewrite_frames(self, root, transform):
        path = root / 'server-to-client.bin'
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
        capture = json.loads((root / 'capture.json').read_text())
        capture.update(status='ok', networkMagic=764824073, relay='backbone.cardano.iog.io',
                       startedUtc='2026-01-03T00:06:00Z', completedUtc='2026-01-03T00:07:00Z',
                       anchorLookupUtc='2026-01-03T00:05:00Z')
        (root / 'capture.json').write_text(json.dumps(capture))
        (root / 'declaration.md').write_text(
            '- Declaration time: 2026-01-03T00:04:42Z\n'
            '- T0 = 2026-01-01T00:00:00Z (placeholder weekday)\n'
            '- Mainnet slot at T0: ' + str(self.range['Blocks'][0]['Slot']) + '.\n')
        if deep:
            def deepen(frames):
                for f in frames:
                    if f['protocol'] != 2:
                        yield f['raw']
                        continue
                    values = list(verifier.decode(f['payload']).children)
                    tip_bytes = encoded_array([values[-1].children[0].raw, argument(0, self.result['summary']['tips'][-1]['blockNumber'] + 2160)])
                    yield frame(2, encoded_array([values[0].raw, values[1].raw, tip_bytes]))
            self.rewrite_frames(root, deepen)
        second = root.parent / 'second'
        second.mkdir()
        frames = verifier.parse_frames((root / 'server-to-client.bin').read_bytes())
        (second / 'server-to-client.bin').write_bytes(b''.join(f['raw'] for f in frames if f['protocol'] != 3))
        outbound = verifier.parse_frames((root / 'client-to-server.bin').read_bytes())
        (second / 'client-to-server.bin').write_bytes(b''.join(f['raw'] for f in outbound if f['protocol'] != 3 or f['payload'] == b'\x81\x01'))
        capture.update(relay='backbone.mainnet.cardanofoundation.org', blocks=[], rawBytes=0)
        (second / 'capture.json').write_text(json.dumps(capture))
        return second

    def test_mainnet_second_operator_shallow_tip_and_relay_mismatch(self):
        for mutation in ['none', 'shallow', 'same-operator', 'header']:
            with self.subTest(mutation=mutation), self.copy() as root:
                second = self.mainnet_sources(root, deep=mutation != 'shallow')
                if mutation == 'same-operator':
                    capture = json.loads((second / 'capture.json').read_text())
                    capture['relay'] = 'backbone.cardano.iog.io'
                    (second / 'capture.json').write_text(json.dumps(capture))
                elif mutation == 'header':
                    def change(frames):
                        frames[2]['raw'] = frames[3]['raw']
                        return [f['raw'] for f in frames]
                    self.rewrite_frames(second, change)
                if mutation == 'none':
                    result = verifier.verify(root, relay_b=second)
                    self.assertGreaterEqual(result['summary']['depth'], 2160)
                    self.assertEqual(result['payload']['declaration.md'], (root / 'declaration.md').read_bytes())
                    self.assertEqual(result['source']['declaration']['firstSlot'], self.range['Blocks'][0]['Slot'])
                    self.assertNotEqual(result['source']['relayA']['operator'], result['source']['relayB']['operator'])
                else:
                    with self.assertRaises(ValueError):
                        verifier.verify(root, relay_b=second)

    def test_declaration_boundary_mutations_fail(self):
        for first_slot in [self.range['Anchor']['Slot'], self.range['Blocks'][0]['Slot'] + 1]:
            with self.subTest(first_slot=first_slot), self.copy() as root:
                second = self.mainnet_sources(root)
                path = root / 'declaration.md'
                path.write_text(path.read_text().replace(str(self.range['Blocks'][0]['Slot']), str(first_slot)))
                with self.assertRaisesRegex(ValueError, 'declaration boundary'):
                    verifier.verify(root, relay_b=second)

    def test_declaration_must_precede_first_fetch(self):
        with self.copy() as root:
            second = self.mainnet_sources(root)
            path = root / 'declaration.md'
            path.write_text(path.read_text().replace('2026-01-03T00:04:42Z', '2026-01-04T00:04:42Z')
                            .replace('2026-01-01T00:00:00Z', '2026-01-02T00:00:00Z'))
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
