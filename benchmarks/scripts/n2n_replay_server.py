#!/usr/bin/env python3
"""Replay verified public N2N mux bytes, opaque CBOR, over an external TCP socket."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import socket
import struct


def parse_frames(data):
    frames, offset = [], 0
    while offset < len(data):
        if len(data) - offset < 8:
            raise ValueError("Truncated mux header")
        timestamp, protocol, length = struct.unpack_from(">IHH", data, offset)
        end = offset + 8 + length
        if end > len(data):
            raise ValueError("Truncated mux payload")
        frames.append({"offset": offset, "protocol": protocol & 0x7fff, "mode": bool(protocol & 0x8000),
                       "timestamp": timestamp, "raw": data[offset:end], "payload": data[offset + 8:end]})
        offset = end
    return frames


def captured_chunks(data, reads, start, end):
    chunks = [data[max(start, offset):min(end, offset + length)] for offset, length in reads
              if offset < end and offset + length > start]
    if b"".join(chunks) != data[start:end]:
        raise ValueError("Captured TCP read chunks do not cover the complete requested bytes")
    return chunks


def load_plan(root, mode):
    root = Path(root)
    fingerprints = json.loads((root / "sha256.json").read_text())
    for name, expected in fingerprints.items():
        path = (root / name).resolve()
        if not path.is_relative_to(root.resolve()) or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError("Capture integrity failed: " + name)
    capture = json.loads((root / "capture.json").read_text())
    if len(capture["blocks"]) != 1000 or capture["rawBytes"] != 4040750:
        raise ValueError("Not the approved1000-block fixed range")
    inbound = (root / "server-to-client.bin").read_bytes()
    outbound = (root / "client-to-server.bin").read_bytes()
    received = parse_frames(inbound)
    sent = parse_frames(outbound)
    receive_by_protocol = {protocol: [frame for frame in received if frame["protocol"] == protocol] for protocol in [0, 2, 3]}
    send_by_protocol = {protocol: [frame for frame in sent if frame["protocol"] == protocol] for protocol in [0, 2, 3]}
    chain = send_by_protocol[2]
    next_request, count = collections.Counter(frame["payload"] for frame in chain).most_common(1)[0]
    if count != 1001 or len(receive_by_protocol[2]) != 1002 or len(send_by_protocol[3]) != 2:
        raise ValueError("Recorded request/response conversation is not the expected fixed range")
    fetch_frames = receive_by_protocol[3]
    start = fetch_frames[0]["offset"]
    end = fetch_frames[-1]["offset"] + len(fetch_frames[-1]["raw"])
    if b"".join(frame["raw"] for frame in fetch_frames) != inbound[start:end]:
        raise ValueError("BlockFetch capture is interleaved; explicit protocol scheduling required")
    reads = [(event["offset"], event["length"]) for event in capture["events"] if event["inbound"]]
    chunks = captured_chunks(inbound, reads, start, end) if mode == "captured-chunks" else [frame["raw"] for frame in fetch_frames]
    return {"handshakeRequest": send_by_protocol[0][0]["payload"], "handshakeResponses": [frame["raw"] for frame in receive_by_protocol[0]],
            "intersectRequest": chain[0]["payload"], "intersectResponse": receive_by_protocol[2][0]["raw"],
            "nextRequest": next_request, "headerResponses": [frame["raw"] for frame in receive_by_protocol[2][1:]],
            "chainDone": chain[-1]["payload"], "fetchRequest": send_by_protocol[3][0]["payload"],
            "fetchDone": send_by_protocol[3][-1]["payload"], "fetchChunks": chunks,
            "rangeSha256": capture["rangeSha256"], "sendMode": mode}


class ReplaySession:
    def __init__(self, plan):
        self.plan = plan
        self.header_index = 0
        self.completed_ranges = 0
        self.intersections = 0
        self.header_responses = 0
        self.done_protocols = set()

    def respond(self, protocol, payload):
        plan = self.plan
        if protocol == 0 and payload == plan["handshakeRequest"]:
            return plan["handshakeResponses"]
        if protocol == 2:
            if payload == plan["intersectRequest"]:
                if self.header_index not in [0, len(plan["headerResponses"])]:
                    raise ValueError("Intersection interrupted a partial recorded range")
                self.header_index = 0
                self.intersections += 1
                return [plan["intersectResponse"]]
            if payload == plan["nextRequest"]:
                if self.header_index >= len(plan["headerResponses"]):
                    raise ValueError("Too many header requests")
                response = plan["headerResponses"][self.header_index]
                self.header_index += 1
                self.header_responses += 1
                return [response]
            if payload == plan["chainDone"]:
                self.done_protocols.add(2)
                return []
        if protocol == 3:
            if payload == plan["fetchRequest"]:
                if self.header_index != len(plan["headerResponses"]):
                    raise ValueError("BlockFetch before all recorded headers")
                self.completed_ranges += 1
                return plan["fetchChunks"]
            if payload == plan["fetchDone"]:
                self.done_protocols.add(3)
                return []
        raise ValueError(f"Unrecorded client request on protocol{protocol}: {payload[:32].hex()}")


def receive_exact(connection, count, *, eof_ok=False):
    result = bytearray()
    while len(result) < count:
        chunk = connection.recv(count - len(result))
        if not chunk:
            if eof_ok and not result:
                return None
            raise ValueError("Client closed with a partial mux frame")
        result.extend(chunk)
    return bytes(result)


def serve_connection(connection, plan):
    session = ReplaySession(plan)
    connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    connection.settimeout(120)
    wire_received = wire_sent = requests = 0
    while True:
        header = receive_exact(connection, 8, eof_ok=True)
        if header is None:
            break
        _, protocol, length = struct.unpack(">IHH", header)
        payload = receive_exact(connection, length) if length else b""
        requests += 1
        wire_received += 8 + length
        for response in session.respond(protocol & 0x7fff, payload):
            connection.sendall(response)
            wire_sent += len(response)
        if session.done_protocols == {2, 3}:
            break
    if session.completed_ranges < 2:
        raise ValueError(f"Client completed only{session.completed_ranges} ranges; warmup + measured work required")
    return {"completedRanges": session.completed_ranges, "intersections": session.intersections,
            "headerResponses": session.header_responses, "requests": requests, "wireReceived": wire_received, "wireSent": wire_sent}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--capture", required=True, type=Path)
    parser.add_argument("--ready", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    parser.add_argument("--bind", default="127.0.0.2")
    parser.add_argument("--port", type=int, default=3001)
    parser.add_argument("--connections", type=int, required=True)
    parser.add_argument("--send-mode", choices=["captured-chunks", "mux-frames"], default="captured-chunks")
    args = parser.parse_args()
    plan = load_plan(args.capture, args.send_mode)
    rows = []
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
            listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            listener.bind((args.bind, args.port))
            listener.listen(4)
            listener.settimeout(600)
            args.ready.write_text(json.dumps({"bind": args.bind, "port": args.port, "rangeSha256": plan["rangeSha256"], "mode": args.send_mode}) + "\n")
            for index in range(args.connections):
                connection, _ = listener.accept()
                with connection:
                    row = serve_connection(connection, plan)
                    rows.append(dict(connection=index, **row))
                args.summary.write_text(json.dumps(rows, indent=2) + "\n")
    finally:
        args.summary.write_text(json.dumps(rows, indent=2) + "\n")
        args.ready.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
