#!/usr/bin/env python3
"""Verify recorded N2N bytes independently; copy only verified payload to a new corpus."""
import argparse
import bisect
import collections
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess

from n2n_replay_server import parse_frames



OPERATORS = {'backbone.cardano.iog.io': 'IOG',
             'backbone.mainnet.cardanofoundation.org': 'Cardano Foundation',
             'backbone.mainnet.emurgornd.com': 'EMURGO'}

def require(condition, message):
    if not condition:
        raise ValueError(message)


class Item:
    """A CBOR item with its original byte span, never a re-encoding."""
    def __init__(self, data, start, end, major, value=None, children=()):
        self.data, self.start, self.end = data, start, end
        self.major, self.value, self.children = major, value, children

    @property
    def raw(self):
        return self.data[self.start:self.end]


def read(data, start=0):
    require(start < len(data), "Truncated CBOR item")
    head = data[start]
    major, additional = head >> 5, head & 31
    offset = start + 1
    value = additional
    if additional in (24, 25, 26, 27):
        width = 1 << (additional - 24)
        require(offset + width <= len(data), "Truncated CBOR argument")
        value = int.from_bytes(data[offset:offset + width], 'big')
        offset += width
    else:
        require(additional < 28 or additional == 31, "Reserved CBOR additional information")
    indefinite = additional == 31
    require(not indefinite or major in (2, 3, 4, 5), "Unexpected CBOR break or indefinite scalar")
    children = []
    if major in (0, 1):
        value = value if major == 0 else -1 - value
    elif major in (2, 3):
        if indefinite:
            parts = []
            while True:
                require(offset < len(data), "Unclosed indefinite string")
                if data[offset] == 255:
                    offset += 1
                    break
                require(data[offset] >> 5 == major and data[offset] & 31 != 31, "Invalid indefinite string chunk")
                child = read(data, offset)
                parts.append(child.value)
                children.append(child)
                offset = child.end
            value = (b'' if major == 2 else '').join(parts)
        else:
            require(offset + value <= len(data), "Truncated CBOR string")
            value, offset = data[offset:offset + value], offset + value
            if major == 3:
                value = value.decode('utf-8')
    elif major in (4, 5):
        remaining = None if indefinite else value * (2 if major == 5 else 1)
        while remaining is None or remaining > 0:
            require(offset < len(data), "Truncated CBOR container")
            if indefinite and data[offset] == 255:
                offset += 1
                break
            child = read(data, offset)
            children.append(child)
            offset = child.end
            if remaining is not None:
                remaining -= 1
        require(major != 5 or len(children) % 2 == 0, "Odd indefinite CBOR map")
        value = None
    elif major == 6:
        child = read(data, offset)
        children, offset = [child], child.end
    elif major == 7:
        if additional in (25, 26, 27):
            value = struct.unpack({25: '>e', 26: '>f', 27: '>d'}[additional], data[start + 1:offset])[0]
    return Item(data, start, offset, major, value, tuple(children))


def decode(data):
    item = read(data)
    require(item.end == len(data), "Trailing CBOR bytes")
    return item


def array(item, length=None):
    require(item.major == 4 and (length is None or len(item.children) == length), "Unexpected CBOR array shape")
    return item.children


def uint(item):
    require(item.major == 0, "Expected CBOR unsigned integer")
    return item.value


def byte_string(item):
    require(item.major == 2, "Expected CBOR byte string")
    return item.value


def embedded(item):
    if item.major == 6:
        require(item.value == 24, "Expected CBOR tag 24")
        item = item.children[0]
    return decode(byte_string(item))


def untag(item):
    while item.major == 6:
        item = item.children[0]
    return item


def fields(item):
    item = untag(item)
    require(item.major == 5, "Expected CBOR map")
    pairs = list(zip(item.children[::2], item.children[1::2]))
    require(all(key.major == 0 for key, _ in pairs), "Expected unsigned map keys")
    require(len({key.value for key, _ in pairs}) == len(pairs), "Duplicate unsigned map key")
    return {key.value: value for key, value in pairs}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def blake(data):
    return hashlib.blake2b(data, digest_size=32).digest()


def messages(frames):
    data = b''.join(frame['payload'] for frame in frames)
    result, offset = [], 0
    while offset < len(data):
        item = read(data, offset)
        result.append(item)
        offset = item.end
    return result


def point(item):
    values = array(item, 2)
    slot, hash_bytes = uint(values[0]), byte_string(values[1])
    require(len(hash_bytes) == 32, "Point hash must have 32 bytes")
    return {'Slot': slot, 'Hash': hash_bytes.hex()}


def same_point(left, right):
    return left['Slot'] == right['Slot'] and left['Hash'] == right['Hash']


def tip(item):
    values = array(item, 2)
    return dict(point(values[0]), blockNumber=uint(values[1]))


def header(item):
    body, _ = array(item, 2)
    values = array(body, 10)
    previous, body_hash = byte_string(values[2]), byte_string(values[7])
    require(len(previous) == len(body_hash) == 32, "Header hashes must have 32 bytes")
    version = array(values[9], 2)
    return {'Slot': uint(values[1]), 'Hash': blake(item.raw).hex(), 'blockNumber': uint(values[0]),
            'previous': previous.hex(), 'bodySize': uint(values[6]), 'bodyHash': body_hash,
            'version': (uint(version[0]), uint(version[1])), 'raw': item.raw}


def chain(frames, fixed_range):
    require(len(frames) == 1002, "Expected 1,002 chain-sync reply frames")
    replies = [decode(frame['payload']) for frame in frames]
    require([uint(array(reply)[0]) for reply in replies] == [5, 3] + [2] * 1000,
            "Expected IntersectFound, RollBackward, then 1,000 RollForward")
    anchor = fixed_range['Anchor']
    tips, headers = [], []
    for reply in replies:
        values = array(reply, 3)
        tips.append(tip(values[2]))
    for reply in replies[:2]:
        require(same_point(point(reply.children[1]), anchor), "Intersection or rollback differs from range anchor")
    previous, height, slot = anchor['Hash'], None, anchor['Slot']
    for reply, expected in zip(replies[2:], fixed_range['Blocks']):
        variant, wrapped = array(reply.children[1], 2)
        require(uint(variant) == 6, "Expected Conway header variant 6")
        value = header(embedded(wrapped))
        require(value['previous'] == previous, "Broken header previous-hash chain")
        require(value['Slot'] > slot and (height is None or value['blockNumber'] == height + 1),
                "Header block number or slot does not advance consecutively")
        require(same_point(value, expected), "Header differs from range point")
        previous, height, slot = value['Hash'], value['blockNumber'], value['Slot']
        headers.append(value)
    return headers, tips


def conversation(inbound, outbound, fixed_range, bodies=True):
    incoming, outgoing = parse_frames(inbound), parse_frames(outbound)
    require(all(frame['mode'] for frame in incoming) and all(not frame['mode'] for frame in outgoing),
            "Unexpected mux direction")
    require(all(frame['protocol'] in (0, 2, 3, 8) for frame in incoming + outgoing), "Unexpected recorded mini-protocol")
    by_protocol = lambda frames, protocol: [frame for frame in frames if frame['protocol'] == protocol]
    require(len(by_protocol(incoming, 0)) == len(by_protocol(outgoing, 0)) == 1, "Missing recorded handshake")
    headers, tips = chain(by_protocol(incoming, 2), fixed_range)
    requests = [decode(frame['payload']) for frame in by_protocol(outgoing, 2)]
    require(len(requests) == 1003, "Expected intersection, 1,001 NextRequest and MsgDone")
    first = array(requests[0], 2)
    require(uint(first[0]) == 4 and len(array(first[1])) == 1
            and same_point(point(first[1].children[0]), fixed_range['Anchor']), "Wrong FindIntersect request")
    require(all(len(array(item, 1)) == 1 and uint(item.children[0]) == 0 for item in requests[1:-1]),
            "Wrong RequestNext message")
    require(uint(array(requests[-1], 1)[0]) == 7, "Missing chain-sync MsgDone")
    fetch_requests = [decode(frame['payload']) for frame in by_protocol(outgoing, 3)]
    require(len(fetch_requests) == (2 if bodies else 1), "Wrong block-fetch request count")
    require(uint(array(fetch_requests[-1], 1)[0]) == 1, "Missing block-fetch MsgDone")
    if bodies:
        request = array(fetch_requests[0], 3)
        require(uint(request[0]) == 0 and same_point(point(request[1]), fixed_range['Blocks'][0])
                and same_point(point(request[2]), fixed_range['Blocks'][-1]), "Wrong block-fetch range")
    else:
        require(not by_protocol(incoming, 3), "Headers-only source contains block-fetch replies")
    return headers, tips, by_protocol(incoming, 2), by_protocol(incoming, 3)


def frame_groups(frames, items):
    """Assign every frame once, through the frame containing each message's last byte."""
    ends, total = [], 0
    for frame in frames:
        total += len(frame['payload'])
        ends.append(total)
    groups, cursor, spanning = [], 0, []
    for item in items:
        first = bisect.bisect_right(ends, item.start)
        last = bisect.bisect_left(ends, item.end)
        require(last < len(frames), "Message extends beyond its frames")
        spanning.append(first != last)
        groups.append([{'offset': f['offset'], 'length': len(f['raw'])} for f in frames[cursor:last + 1]])
        cursor = max(cursor, last + 1)
    require(cursor == len(frames), "Unassigned trailing block-fetch frame")
    return groups, spanning


def script_counts(parts):
    bodies, witnesses = array(parts[1]), array(parts[2])
    require(len(bodies) == len(witnesses), "Transaction body and witness counts differ")
    redeemers = references = 0
    for body, witness in zip(bodies, witnesses):
        body_fields, witness_fields = fields(body), fields(witness)
        if 18 in body_fields:
            references += bool(array(untag(body_fields[18])))
        if 5 in witness_fields:
            value = untag(witness_fields[5])
            require(value.major in (4, 5), "Redeemers must use list or map form")
            redeemers += bool(value.children)
    return len(bodies), redeemers, references


def verify_blocks(frames, headers, fixed_range, capture):
    items = messages(frames)
    require(len(items) == 1002, "Expected StartBatch, 1,000 MsgBlock and BatchDone")
    require([uint(array(item)[0]) for item in items] == [2] + [4] * 1000 + [5], "Wrong block-fetch message sequence")
    require(len(array(items[0], 1)) == len(array(items[-1], 1)) == 1, "Malformed batch envelope")
    require(len(capture['blocks']) == 1000, "Wrong capture block count")
    raw_bytes = transactions = redeemers = references = scripted_blocks = 0
    for item, expected_header, expected_point, recorded in zip(items[1:-1], headers, fixed_range['Blocks'], capture['blocks']):
        block = embedded(array(item, 2)[1])
        era, content = array(block, 2)
        require(uint(era) == 7, "Expected Conway block era 7")
        parts = array(content, 5)
        require(parts[0].raw == expected_header['raw'], "Block header differs from chain-sync header")
        require(sum(len(part.raw) for part in parts[1:]) == expected_header['bodySize'], "Block body size differs from header")
        require(blake(b''.join(blake(part.raw) for part in parts[1:])) == expected_header['bodyHash'], "Block body hash differs from header")
        require(digest(block.raw) == expected_point['Sha256'], "Block digest differs from range digest")
        require(recorded['bytes'] == len(block.raw) and recorded['sha256'] == digest(block.raw), "Capture block bytes or digest differ")
        count, scripts, refs = script_counts(parts)
        transactions += count
        redeemers += scripts
        references += refs
        scripted_blocks += scripts > 0
        raw_bytes += len(block.raw)
    require(capture['rawBytes'] == raw_bytes, "Capture raw byte count differs")
    groups, spanning = frame_groups(frames, items)
    return groups, {'blocks': 1000, 'rawBytes': raw_bytes, 'transactions': transactions,
                    'transactionsWithRedeemers': redeemers, 'blocksWithRedeemers': scripted_blocks,
                    'transactionsWithReferenceInputs': references, 'messagesSpanningFrames': sum(spanning),
                    'blockMessagesSpanningFrames': sum(spanning[1:-1])}


def tip_series(tips):
    indices = {0, len(tips) - 1}
    indices.update(i for i in range(1, len(tips))
                   if tips[i]['Slot'] < tips[i - 1]['Slot'] or tips[i]['blockNumber'] < tips[i - 1]['blockNumber'])
    return [dict(reply=i, **tips[i]) for i in sorted(indices)]


def read_source(root, range_path=None, pinned=False):
    root = Path(root)
    payload = {name: (root / name).read_bytes() for name in ('server-to-client.bin', 'client-to-server.bin', 'capture.json')}
    capture = json.loads(payload['capture.json'])
    if pinned:
        index = json.loads((root / 'sha256.json').read_text())
        for name, data in payload.items():
            require(index.get(name) == digest(data), 'Pinned input hash differs: ' + name)
    payload['range.json'] = Path(range_path or root / 'range.json').read_bytes()
    require(capture['rangeSha256'] == digest(payload['range.json']), "Range file hash differs from capture")
    fixed_range = json.loads(payload['range.json'])
    require(len(fixed_range['Blocks']) == 1000, "Expected 1,000 range points")
    for value in [fixed_range['Anchor'], *fixed_range['Blocks']]:
        require(isinstance(value['Slot'], int) and value['Slot'] >= 0 and len(bytes.fromhex(value['Hash'])) == 32,
                "Malformed range point")
    return payload, capture, fixed_range



def utc_time(value):
    instant = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
    require(instant.utcoffset() == dt.timedelta(0), "Declaration and fetch timestamps must be UTC")
    return instant


def read_declaration(text):
    lines = text.splitlines()
    def value(prefix):
        values = [line[len(prefix):].strip() for line in lines if line.startswith(prefix)]
        require(len(values) == 1, "Missing or repeated declaration field")
        return values[0]
    declared = utc_time(value('- Declaration time: '))
    t0 = utc_time(value('- T0 = ').split(' ')[0])
    first_slot = int(value('- Mainnet slot at T0: ').split('.')[0])
    expected = (declared - dt.timedelta(hours=48)).replace(minute=0, second=0, microsecond=0)
    require(t0 == expected and first_slot >= 0, "The declaration must fix the 48-hour, hour-rounded T0")
    return {'declaredUtc': declared.isoformat(), 't0Utc': t0.isoformat(), 'firstSlot': first_slot}

def verify(input_dir, range_path=None, relay_b=None, local_node=False):
    require(bool(relay_b) != bool(local_node), "Choose a second relay or --local-node")
    payload, capture, fixed_range = read_source(input_dir, range_path, pinned=local_node)
    headers, tips, chain_frames, fetch_frames = conversation(payload['server-to-client.bin'], payload['client-to-server.bin'], fixed_range)
    groups, summary = verify_blocks(fetch_frames, headers, fixed_range, capture)
    summary.update({'blocksOver12288Bytes': sum(h['bodySize'] > 12288 for h in headers),
                    'blocksOver65535Bytes': sum(h['bodySize'] > 65535 for h in headers),
                    'bodyBytes': sum(h['bodySize'] for h in headers), 'blockFetchFrames': len(fetch_frames),
                    'bodyGroups': len(groups), 'emptyBodyGroups': sum(not group for group in groups),
                    'headerFrames': len(chain_frames) - 2,
                    'headerProtocolVersions': [{'major': major, 'minor': minor, 'headers': count}
                                               for (major, minor), count in sorted(collections.Counter(h['version'] for h in headers).items())],
                    'tips': tip_series(tips), 'depth': tips[-1]['blockNumber'] - headers[-1]['blockNumber']})
    for numerator, denominator, label in [('transactionsWithRedeemers', 'transactions', 'transactionRedeemerShare'),
                                         ('transactionsWithReferenceInputs', 'transactions', 'referenceInputShare'),
                                         ('blocksWithRedeemers', 'blocks', 'blockRedeemerShare')]:
        summary[label] = summary[numerator] / summary[denominator] if summary[denominator] else None
    source = {'kind': 'local node 11.1.3', 'verification': 'No second operator; depth not checked'}
    if not local_node:
        raw_declaration = (Path(input_dir) / 'declaration.md').read_bytes()
        declaration = read_declaration(raw_declaration.decode('utf-8'))
        require(fixed_range['Anchor']['Slot'] < declaration['firstSlot'] <= fixed_range['Blocks'][0]['Slot'],
                "The declaration boundary must bracket the anchor and first block")
        require(utc_time(declaration['declaredUtc']) < utc_time(capture['anchorLookupUtc']) <= utc_time(capture['startedUtc']),
                "The declaration must precede the first fetch")
        payload['declaration.md'] = raw_declaration
        require(capture.get('status') == 'ok' and capture.get('networkMagic') == 764824073, "Mainnet capture did not succeed")
        require(summary['depth'] >= 2160, "Mainnet range is fewer than 2,160 blocks deep")
        second_payload, second_capture, second_range = read_source(relay_b, Path(input_dir) / 'range.json')
        require(second_capture.get('status') == 'ok' and second_capture.get('networkMagic') == 764824073, "Second relay did not succeed")
        require(capture.get('relay') in OPERATORS and second_capture.get('relay') in OPERATORS
                and OPERATORS[capture['relay']] != OPERATORS[second_capture['relay']], "Second source must be a different operator")
        second_headers, second_tips, _, _ = conversation(second_payload['server-to-client.bin'], second_payload['client-to-server.bin'], second_range, bodies=False)
        require([h['raw'] for h in headers] == [h['raw'] for h in second_headers], "Second relay headers differ")
        for name in ('server-to-client.bin', 'client-to-server.bin', 'capture.json'):
            payload['relay-b/' + name] = second_payload[name]
        summary['relayBTips'] = tip_series(second_tips)
        source = {'kind': 'public bootstrap relays', 'declaration': declaration, 'relayA': dict(operator=OPERATORS[capture['relay']], **{key: capture[key] for key in ('relay', 'startedUtc', 'completedUtc')}),
                  'relayB': dict(operator=OPERATORS[second_capture['relay']], **{key: second_capture[key] for key in ('relay', 'startedUtc', 'completedUtc')})}
    corpus = {'format': 'recorded-frames-v1',
              'headers': [{'offset': frame['offset'], 'length': len(frame['raw'])} for frame in chain_frames[2:]],
              'bodies': groups}
    return {'payload': payload, 'range': fixed_range, 'summary': summary, 'source': source, 'corpus': corpus,
            'captureTools': capture.get('tools', {'repositoryCommit': 'd679f36', 'sdk': '11.0.100',
                                                'note': 'preprod capture predates corpus tooling; built with candidate, not 7c45d47'})}


def write_verified(result, input_dir, relay_b, out, manifest_path, local_node):
    out, manifest_path = Path(out).resolve(), Path(manifest_path).resolve()
    sources = [Path(input_dir).resolve()] + ([Path(relay_b).resolve()] if relay_b else [])
    require(not any(out.is_relative_to(source) or manifest_path.is_relative_to(source) for source in sources),
            "Output and manifest must not be inside an input source")
    require(not out.exists(), "Output directory must not exist")
    require(not manifest_path.exists(), "Manifest must not exist")
    require(not manifest_path.is_relative_to(out), "Manifest must be outside the corpus")
    name = 'preprod-p1-n2n-recording-a' if local_node else 'mainnet-' + str(result['range']['Anchor']['Slot'])
    require(out.name == name, "Output directory name must be " + name)
    out.mkdir(parents=True)
    for name_in_corpus, data in result['payload'].items():
        destination = out / name_in_corpus
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
        require(digest(destination.read_bytes()) == digest(data), "Copied payload hash differs")
    json_bytes = lambda value: (json.dumps(value, indent=2, ensure_ascii=True) + '\n').encode()
    (out / 'corpus.json').write_bytes(json_bytes(result['corpus']))
    index = {str(path.relative_to(out)): digest(path.read_bytes()) for path in sorted(out.rglob('*')) if path.is_file()}
    (out / 'sha256.json').write_bytes(json_bytes(index))
    commit = subprocess.check_output(['git', '-C', str(Path(__file__).resolve().parent), 'rev-parse', 'HEAD'], text=True,
                                     env=dict(os.environ, GIT_OPTIONAL_LOCKS='0')).strip()
    manifest = {'name': name, 'network': 'preprod' if local_node else 'mainnet', 'networkMagic': 1 if local_node else 764824073,
                'anchor': result['range']['Anchor'], 'last': result['range']['Blocks'][-1],
                'blocks': result['summary']['blocks'], 'rawBytes': result['summary']['rawBytes'],
                'tcpSendMode': 'captured-chunks' if local_node else 'mux-frames',
                'index': {'file': 'sha256.json', 'sha256': digest((out / 'sha256.json').read_bytes())},
                'summary': result['summary'], 'source': result['source'],
                'tools': {'capture': result['captureTools'], 'verifier': {'repositoryCommit': commit,
                          'sha256': digest(Path(__file__).read_bytes()), 'implementation': 'Python standard library'},
                          'verifiedUtc': dt.datetime.now(dt.timezone.utc).isoformat()},
                'licence': [
                    'Cardano ' + ('preprod' if local_node else 'mainnet') + ' blockchain data, public. No licence is granted or asserted by the capturer.',
                    'Blocks hold transactions and metadata written by third parties, which may include personal data or copyrighted text. The corpus is kept out of the repository, used only for local and Fly benchmarks, and never printed or redistributed.',
                    'Source relays: public bootstrap peers run by IOG and the Cardano Foundation (EMURGO as fallback). No terms of use for them were found (searched 17 Nov 2026: developers.cardano.org topology pages, docs.cardano.org P2P page, Cardano Foundation policies).'
                    if not local_node else 'Source: local preprod node 11.1.3; no second operator.',
                    "Redistribution needs the owner's decision."]}
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_bytes(json_bytes(manifest))
    return {'corpusId': digest(manifest_path.read_bytes()), 'manifest': manifest, 'verification': 'PASS'}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--range', type=Path)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--relay-b', type=Path)
    source.add_argument('--local-node', action='store_true')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        result = verify(args.input, args.range, args.relay_b, args.local_node)
        report = write_verified(result, args.input, args.relay_b, args.out, args.manifest, args.local_node)
    except (ValueError, KeyError, TypeError, OSError, RecursionError, struct.error):
        # Paths and CBOR error strings can contain third-party or local data.
        parser.exit(1, 'Corpus verification failed; input left unchanged.\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
