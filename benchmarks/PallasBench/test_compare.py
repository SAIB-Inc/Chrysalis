"""Fail closed on unequal work, unsettled JIT, or incomplete replay ranges."""
import copy
import unittest
from pathlib import Path

from compare import corpus_plan, validate_pair, process_timeout_seconds, run

ROOT = Path(__file__).resolve().parents[1] / "Chrysalis.Benchmarks"


class MatchedWorkTests(unittest.TestCase):
    def setUp(self):
        self.plan = corpus_plan(ROOT)
        counters = {"compiledMethods": 50, "ilBytes": 500, "compilationTicks": 100}
        self.result = {
            "mode": "walk", "blocks": 30, "iterations": 2,
            "items": self.plan["items"], "checks": self.plan["checks"],
            "elapsed_ns": 2_000_000_000,
            "jit": {"start": counters, "end": dict(counters)},
        }

    def test_walk_counts_match_independent_cbor_oracle(self):
        validate_pair(self.result, copy.deepcopy(self.result), self.plan)

    def test_rejects_dropped_items_blocks_and_wrong_digest(self):
        for field, value in (("items", 0), ("blocks", 29), ("iterations", 1)):
            with self.subTest(field=field):
                changed = dict(self.result, **{field: value})
                with self.assertRaises(ValueError):
                    validate_pair(self.result, changed, self.plan)
        changed = copy.deepcopy(self.result)
        changed["checks"][0]["sha256"] = "00" * 32
        with self.assertRaises(ValueError):
            validate_pair(self.result, changed, self.plan)

    def test_rejects_equal_but_incorrect_work_on_both_sides(self):
        for field, value in (("items", 1), ("checks", []), ("elapsed_ns", 0)):
            changed = dict(self.result, **{field: value})
            with self.subTest(field=field), self.assertRaises(ValueError):
                validate_pair(changed, changed, self.plan)

    def test_replay_is_29_successors_not_anchor_repeated(self):
        result = dict(self.result, mode="replay", blocks=29, iterations=1,
                      checks=self.plan["checks"][1:])
        validate_pair(result, copy.deepcopy(result), self.plan)
        changed = dict(result, checks=self.plan["checks"][:29])
        with self.assertRaises(ValueError):
            validate_pair(changed, changed, self.plan)

    def test_accepts_small_recorded_compilation(self):
        changed = copy.deepcopy(self.result)
        changed["jit"]["end"]["compiledMethods"] += 2
        changed["jit"]["end"]["ilBytes"] += 242
        changed["jit"]["end"]["compilationTicks"] += 10_000
        validate_pair(changed, self.result, self.plan)

    def test_rejects_missing_or_decreasing_compilation_counters(self):
        changed = dict(self.result)
        del changed["jit"]
        with self.assertRaises(ValueError):
            validate_pair(changed, self.result, self.plan)
        changed = copy.deepcopy(self.result)
        changed["jit"]["end"]["compiledMethods"] -= 1
        with self.assertRaises(ValueError):
            validate_pair(changed, self.result, self.plan)

    def test_compilation_time_boundary_is_strictly_above_one_tenth_percent(self):
        changed = copy.deepcopy(self.result)
        # 20,000 TimeSpan ticks = 2 ms = 0.1% of this 2-second window.
        changed["jit"]["end"]["compilationTicks"] += 20_000
        validate_pair(changed, self.result, self.plan)
        changed["jit"]["end"]["compilationTicks"] += 1
        with self.assertRaises(ValueError):
            validate_pair(changed, self.result, self.plan)

    def test_compiled_il_boundary_is_strictly_above_one_thousand_bytes(self):
        changed = copy.deepcopy(self.result)
        changed["jit"]["end"]["ilBytes"] += 1000
        validate_pair(changed, self.result, self.plan)
        changed["jit"]["end"]["ilBytes"] += 1
        with self.assertRaises(ValueError):
            validate_pair(changed, self.result, self.plan)

    def test_rejects_a_timed_window_shorter_than_one_second(self):
        for side in (0, 1):
            rows = [copy.deepcopy(self.result), copy.deepcopy(self.result)]
            rows[side]["elapsed_ns"] = 999_999_999
            with self.subTest(side=side), self.assertRaises(ValueError):
                validate_pair(*rows, self.plan)


class ProcessBudgetTests(unittest.TestCase):
    def test_process_budget_includes_cap_measured_window_and_margin(self):
        from types import SimpleNamespace
        from unittest.mock import patch
        limits = {"warmupCapSeconds": 120, "measuredWindowSeconds": 120, "marginSeconds": 30}
        self.assertEqual(process_timeout_seconds(limits), 270)
        limits.update(warmupCapSeconds=60, measuredWindowSeconds=60)
        self.assertEqual(process_timeout_seconds(limits), 150)
        with patch("compare.subprocess.run", return_value=SimpleNamespace(returncode=0, stdout="", stderr="")) as child:
            run(["test"], timeout=process_timeout_seconds(limits))
        self.assertEqual(child.call_args.kwargs["timeout"], 150)


class RendererTests(unittest.TestCase):
    def test_repository_renderer_uses_recorded_samples_load_and_jit(self):
        import json
        import subprocess
        import sys
        import tempfile
        out = Path(__file__).resolve().parent / "out"
        out.mkdir(exist_ok=True)
        counters = {"compiledMethods": 50, "ilBytes": 500, "compilationTicks": 100}
        row = {"blocksPerIteration": 30, "iterationsPerProcess": 1,
               "chrysalisElapsedNs": [2_000_000_000], "rustElapsedNs": [1_000_000_000],
               "chrysalisJit": [{"start": counters, "end": dict(counters, compiledMethods=52, ilBytes=742, compilationTicks=10100),
                                  "minimumSeconds": 10, "quietSeconds": 2, "capSeconds": 60}],
               "ratioChrysalisOverRust": {"mean": 2, "confidence95": [2, 2]}}
        data = {"startedUtc": "2026-11-17T00:00:00Z", "platform": "test", "machine": "test", "cpu": "test",
                "loadavgStart": [1, 2, 3], "loadavgEnd": [4, 5, 6], "peerConnections": 2,
                "completedRangesPerConnection": [2, 2], "compilationLimits": {"timeFraction": .001, "ilBytes": 1000}, "results": {mode: row for mode in ("typed", "walk", "replay")},
                "pallas": "test", "minicbor": "test", "saibCbor": "test", "dotnetSdk": "test", "dotnetRuntime": "test",
                "serverGC": False, "rustc": "test", "python": "test"}
        with tempfile.TemporaryDirectory(dir=out) as temporary:
            root = Path(temporary)
            (root / "comparison.json").write_text(json.dumps(data))
            subprocess.run([sys.executable, "-B", str(Path(__file__).with_name("render-results.py")), temporary], check=True, timeout=5)
            text = (root / "RESULTS.md").read_text()
            self.assertIn("1.00 / 2.00 / 3.00; end 4.00 / 5.00 / 6.00", text)
            self.assertIn("| typed / 1 | 2 | 242 | 10000 | 0.050000 |", text)
            self.assertIn("Chrysalis/Rust", text)
            self.assertIn("replay peer, not live-node throughput", text)
            self.assertIn("142.01 µs/block", text)
            self.assertIn("must not be read as", text)
            self.assertEqual(text.count("functional red/smoke runs are retained"), 1)


class ReplayPeerTests(unittest.TestCase):
    def test_real_mux_handshake_intersect_and_two_complete_ranges(self):
        self.check_ranges("done")

    def test_clean_eof_after_full_range_needs_no_arbitrary_flush_sleep(self):
        self.check_ranges("eof")

    def test_early_eof_fails_before_the_first_range_completes(self):
        self.check_ranges("eof", incomplete=True)

    def test_early_done_fails_before_the_first_range_completes(self):
        self.check_ranges("done", incomplete=True)

    def test_reintersect_mid_range_fails_immediately(self):
        self.check_ranges("intersect", incomplete=True)

    def check_ranges(self, ending, incomplete=False):
        import socket
        import struct
        import threading
        from replay_peer import encode, prepare, serve
        from n2n_replay_server import receive_exact
        from corpus_verify import array, decode, uint, tip

        plan = corpus_plan(ROOT)
        anchor, _, _ = prepare(plan)
        errors, results = [], []
        client, server = socket.socketpair()
        with client, server:
            client.settimeout(3)

            def worker():
                try:
                    results.append(serve(server, plan))
                except Exception as error:
                    errors.append(error)

            thread = threading.Thread(target=worker, daemon=True)
            thread.start()

            def send(protocol, payload):
                client.sendall(struct.pack(">IHH", 0, protocol, len(payload)) + payload)

            def exchange(protocol, payload):
                send(protocol, payload)
                body = bytearray()
                while True:
                    header = receive_exact(client, 8)
                    _, response_protocol, length = struct.unpack(">IHH", header)
                    self.assertEqual(response_protocol, protocol | 0x8000)
                    body.extend(receive_exact(client, length))
                    try:
                        return decode(body)
                    except ValueError as error:
                        if not any(word in str(error) for word in ("Truncated", "Unclosed")):
                            raise

            try:
                offer = b"\x82\x00\xa1" + encode(32784) + encode([1, False])
                self.assertEqual(uint(array(exchange(0, offer))[0]), 1)
                for _ in range(1 if incomplete else 2):
                    self.assertEqual(uint(array(exchange(5, encode([4, [anchor]])))[0]), 5)
                    for expected in plan["blocks"][1:2 if incomplete else None]:
                        message = array(exchange(5, encode([0])), 3)
                        self.assertEqual(uint(message[0]), 2)
                        self.assertEqual(message[1].children[0].value, expected)
                        self.assertEqual(tip(message[2])["Slot"], plan["checks"][-1]["slot"])
                if ending == "done":
                    send(5, encode([7]))
                elif ending == "intersect":
                    send(5, encode([4, [anchor]]))
                else:
                    client.shutdown(socket.SHUT_WR)
                thread.join(1)
                self.assertFalse(thread.is_alive(), "Peer accepted an incomplete range")
                if incomplete:
                    self.assertEqual(results, [])
                    self.assertEqual(len(errors), 1)
                    self.assertIsInstance(errors[0], ValueError)
                    expected = {"done": "Out-of-sequence", "eof": "before completing",
                                "intersect": "incomplete range"}[ending]
                    self.assertIn(expected, str(errors[0]))
                else:
                    self.assertEqual(errors, [])
                    self.assertEqual(results, [{"ranges": 2, "blocksPerRange": 29, "version": 16,
                                              "termination": "Done" if ending == "done" else "EOF after complete range"}])
            finally:
                try:
                    client.shutdown(socket.SHUT_RDWR)
                except OSError:
                    pass
                thread.join(3)


if __name__ == "__main__":
    unittest.main()
