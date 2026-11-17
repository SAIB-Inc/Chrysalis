#!/usr/bin/env python3
"""Out-of-process N2C replay peer, not live-node throughput. No Docker/node use."""
import json
from pathlib import Path
import socket
import struct
import sys

from compare import corpus_plan
from corpus_verify import array, decode, fields, uint, byte_string
from n2n_replay_server import receive_exact


def encode(value):
    if isinstance(value, bool):
        return b"\xf5" if value else b"\xf4"
    if isinstance(value, int):
        if value < 24:
            return bytes([value])
        for width, additional in ((1, 24), (2, 25), (4, 26), (8, 27)):
            if value < 1 << (width * 8):
                return bytes([additional]) + value.to_bytes(width, "big")
        raise ValueError("CBOR integer overflow")
    if isinstance(value, bytes):
        head = encode(len(value))
        return bytes([head[0] | 0x40]) + head[1:] + value
    if isinstance(value, (list, tuple)):
        head = encode(len(value))
        return bytes([head[0] | 0x80]) + head[1:] + b"".join(encode(x) for x in value)
    raise ValueError("Unsupported replay CBOR value")


def mux(protocol, payload):
    return b"".join(struct.pack(">IHH", 0, protocol | 0x8000, len(payload[i:i + 16384]))
                    + payload[i:i + 16384] for i in range(0, len(payload), 16384))


def prepare(plan):
    anchor = [plan["anchor"]["Slot"], bytes.fromhex(plan["anchor"]["Hash"])]
    last = plan["checks"][-1]
    tip = [[last["slot"], bytes.fromhex(last["hash"])], 30]
    rolls = []
    for data in plan["blocks"][1:]:
        # [MsgRollForward, tag24(bytes(BlockWithEra)), Tip]
        payload = b"\x83\x02\xd8\x18" + encode(data) + encode(tip)
        rolls.append(mux(5, payload))
    return anchor, mux(5, encode([5, anchor, tip])), rolls


def serve(connection, plan):
    anchor, intersection, rolls = prepare(plan)
    index, ranges, handshaken, intersected = 0, 0, False, False
    connection.settimeout(120)
    while True:
        header = receive_exact(connection, 8, eof_ok=True)
        if header is None:
            if index != len(rolls) or ranges == 0:
                raise ValueError("Client closed before completing the replay range")
            return {"ranges": ranges, "blocksPerRange": len(rolls), "version": 16, "termination": "EOF after complete range"}
        _, protocol, size = struct.unpack(">IHH", header)
        if protocol & 0x8000:
            raise ValueError("Client sent responder-direction frame")
        payload = receive_exact(connection, size)
        message = array(decode(payload))
        kind = uint(message[0])
        if protocol == 0 and kind == 0 and not handshaken:
            offered = fields(message[1])
            # Negotiate the same N2C version (16) for both clients.
            version = 32784
            if version not in offered:
                raise ValueError("Client does not offer N2C v16")
            data = array(offered[version], 2)
            if uint(data[0]) != 1 or data[1].major != 7 or data[1].value != 20:
                raise ValueError("Expected preprod magic=1, query=false")
            connection.sendall(mux(0, encode([1, version, [1, False]])))
            handshaken = True
        elif protocol == 5 and handshaken:
            if kind == 4:
                points = array(message[1], 1)
                point = array(points[0], 2)
                if [uint(point[0]), byte_string(point[1])] != anchor or index not in (0, len(rolls)):
                    raise ValueError("Wrong anchor or incomplete range")
                index, intersected = 0, True
                connection.sendall(intersection)
            elif kind == 0 and intersected and index < len(rolls):
                connection.sendall(rolls[index])
                index += 1
                if index == len(rolls):
                    ranges += 1
            elif kind == 7 and index == len(rolls):
                return {"ranges": ranges, "blocksPerRange": len(rolls), "version": 16, "termination": "Done"}
            else:
                raise ValueError("Out-of-sequence N2C request")
        else:
            raise ValueError("Unexpected replay protocol/request")


def main():
    root, path, ready, summary = map(Path, sys.argv[1:5])
    plan = corpus_plan(root)
    rows = []
    try:
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as listener:
            listener.bind(str(path))
            listener.listen(1)
            listener.settimeout(180)
            ready.write_text("{}\n")
            for _ in range(int(sys.argv[5])):
                connection, _ = listener.accept()
                with connection:
                    rows.append(serve(connection, plan))
    finally:
        summary.write_text(json.dumps(rows, indent=2) + "\n")
        ready.unlink(missing_ok=True)
        path.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
