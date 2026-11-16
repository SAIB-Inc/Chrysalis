"""Opaque captured mux bytes over a real external TCP replay server."""
import importlib
import struct
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).parent))
try:
    replay = importlib.import_module("n2n_replay_server")
except ModuleNotFoundError:
    replay = None


class N2NReplayTests(unittest.TestCase):
    def implementation(self):
        self.assertIsNotNone(replay, "Recorded N2N TCP replay server has not been implemented")
        return replay

    def test_mux_stream_is_exact_and_truncation_fails(self):
        module = self.implementation()
        raw = struct.pack(">IHH", 3, 0x8002, 3) + b"abc"
        frames = module.parse_frames(raw)
        self.assertEqual(frames[0]["raw"], raw)
        self.assertEqual(frames[0]["protocol"], 2)
        self.assertEqual(frames[0]["payload"], b"abc")
        for broken in [raw[:4], raw[:-1]]:
            with self.assertRaises(ValueError):
                module.parse_frames(broken)

    def test_session_resets_identical_recorded_bytes_for_each_range(self):
        module = self.implementation()
        plan = {"handshakeRequest": b"hello", "handshakeResponses": [b"accepted"], "intersectRequest": b"intersect",
                "intersectResponse": b"found", "nextRequest": b"next", "headerResponses": [b"rollback", b"h1", b"h2"],
                "chainDone": b"chain-done", "fetchRequest": b"range", "fetchDone": b"fetch-done", "fetchChunks": [b"b1", b"b2", b"done"]}
        session = module.ReplaySession(plan)
        self.assertEqual(session.respond(0, b"hello"), [b"accepted"])
        for _ in range(2):
            self.assertEqual(session.respond(2, b"intersect"), [b"found"])
            self.assertEqual([session.respond(2, b"next")[0] for _ in range(3)], [b"rollback", b"h1", b"h2"])
            self.assertEqual(session.respond(3, b"range"), [b"b1", b"b2", b"done"])
        self.assertEqual(session.completed_ranges, 2)
        with self.assertRaises(ValueError):
            session.respond(2, b"unexpected")

    def test_captured_chunk_intersections_preserve_all_fetch_bytes(self):
        module = self.implementation()
        data = b"prefixABCDEFGHIJsuffix"
        chunks = module.captured_chunks(data, [(0, 9), (9, 4), (13, len(data) - 13)], 6, 16)
        self.assertEqual(b"".join(chunks), b"ABCDEFGHIJ")
        self.assertEqual([len(chunk) for chunk in chunks], [3, 4, 3])


if __name__ == "__main__":
    unittest.main()
