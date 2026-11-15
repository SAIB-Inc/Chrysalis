"""Regression controls for result semantics; run via heavy python3 -m unittest."""
import math
from pathlib import Path
import sys
import unittest
import json
import tempfile
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parent))
from summarize import bdn_case, confidence95, reproduction, t95, summarize
from run_benchmarks import log_json, load1, machine_info, validate_output, initialize_output
import run_benchmarks
from provenance import accepted_artifacts, register_artifact


class ResultTests(unittest.TestCase):
    def test_heavy_queue_text_does_not_corrupt_environment_json(self):
        self.assertEqual(log_json('heavy: starting after 20s\n{\n  "runtime": "10.0.9"\n}\n'), {"runtime": "10.0.9"})

    def test_macos_load_uses_bsd_sysctl(self):
        with patch.object(run_benchmarks.sys, "platform", "darwin"), patch.object(run_benchmarks.subprocess, "check_output", return_value="{ 1.23 2.34 3.45 }\n") as call:
            self.assertEqual(load1(), 1.23)
            call.assert_called_once_with(["sysctl", "-n", "vm.loadavg"], text=True)

    def test_macos_identity_records_cpu_cores_and_arch(self):
        with patch.object(run_benchmarks.sys, "platform", "darwin"), patch.object(run_benchmarks, "command_output", side_effect=["Apple M4 Pro", "12"]), patch.object(run_benchmarks.platform, "machine", return_value="arm64"):
            machine = machine_info()
            self.assertEqual(machine["cpu"], "Apple M4 Pro")
            self.assertEqual(machine["physicalCores"], 12)
            self.assertEqual(machine["architecture"], "arm64")

    def test_sdk_staging_cannot_write_outside_repository(self):
        with self.assertRaises(ValueError):
            validate_output(run_benchmarks.REPO.parent / "outside-results")
        self.assertEqual(validate_output(run_benchmarks.REPO / "benchmarks/out/test"), run_benchmarks.REPO / "benchmarks/out/test")

    def test_nonempty_output_is_refused_without_mutating_old_results(self):
        parent = run_benchmarks.REPO / "benchmarks/out/test-temporary"
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=parent) as temporary:
            output = Path(temporary)
            old = output / "direct.json"
            old.write_text("old result")
            with self.assertRaises(FileExistsError):
                initialize_output(output)
            self.assertEqual(old.read_text(), "old result")

    def test_validate_only_run_cannot_publish_stale_measurements(self):
        parent = run_benchmarks.REPO / "benchmarks/out/test-temporary"
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=parent) as temporary:
            root = Path(temporary)
            side = root / "baseline"
            side.mkdir()
            manifest = {"schemaVersion": 2, "invocationId": "new-invocation", "mode": "smoke",
                        "measure": "validate", "filter": "*", "machine": {"hostname": "test", "cpu": "test",
                        "logicalCpus": 1, "os": "test", "architecture": "test"}, "wholePathStatus": "not measured",
                        "sides": {"baseline": {"commit": "new-commit", "harnessSha256": "new-hash", "sdk": "10.0.301", "dirty": False}},
                        "artifacts": []}
            (root / "manifest.json").write_text(json.dumps(manifest))
            (side / "direct.json").write_text(json.dumps([{"name": "CoreBench.GeneratedUnionRead", "samples": [{"bytes": 88}]}]))
            reports = side / "micro-00/bdn/results"
            reports.mkdir(parents=True)
            (reports / "Example-report-full.json").write_text(json.dumps({"Benchmarks": [{"FullName": "stale.case", "Statistics": {"Mean": 1}, "Memory": {"BytesAllocatedPerOperation": 0}}]}))
            network = side / "network-00"
            network.mkdir()
            (network / "N2C_full_blocks.json").write_text(json.dumps({"name": "stale-network", "blocks": 1000,
                "blocksPerSecond": 100, "ns": 1, "bytes": 1}))
            result = summarize(root)
            self.assertFalse(any(row["status"] == "measured" for row in result["benchmarks"]["baseline"]))
            self.assertTrue(all(row["status"] == "REJECTED" for row in result["benchmarks"]["baseline"]))
            self.assertEqual(result["network"]["baseline"], [])
            self.assertFalse(any(row["status"] == "reproduced" for row in result["reproduction"]["baseline"]))

    def test_artifact_requires_matching_invocation_commit_harness_and_bytes(self):
        parent = run_benchmarks.REPO / "benchmarks/out/test-temporary"
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=parent) as temporary:
            root = Path(temporary)
            data = root / "direct.json"
            data.write_text("[]")
            manifest = {"schemaVersion": 2, "invocationId": "current", "artifacts": [],
                        "sides": {"baseline": {"commit": "commit", "harnessSha256": "harness"}}}
            entry = register_artifact(root, manifest, "baseline", "direct", data)
            self.assertEqual(accepted_artifacts(root, manifest, "baseline", "direct"), [data])
            for key in ["invocationId", "commit", "harnessSha256"]:
                original = entry[key]
                entry[key] = "stale"
                self.assertEqual(accepted_artifacts(root, manifest, "baseline", "direct"), [], key)
                entry[key] = original
            data.write_text("modified")
            self.assertEqual(accepted_artifacts(root, manifest, "baseline", "direct"), [])

    def test_registered_replay_is_rendered_as_attribution_not_network(self):
        parent = run_benchmarks.REPO / "benchmarks/out/test-temporary"
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=parent) as temporary:
            root = Path(temporary)
            side = "baseline-runtime11"
            manifest = {"schemaVersion": 2, "invocationId": "current", "mode": "smoke",
                        "machine": {"hostname": "test", "cpu": "test", "logicalCpus": 1, "os": "test"},
                        "wholePathStatus": "not measured", "sides": {side: {"commit": "baseline", "dirty": False,
                        "sdk": "10.0.301", "harnessSha256": "harness", "attributionOnly": True,
                        "environment": {"runtime": "11.0.0", "targetFramework": ".NETCoreApp,Version=v10.0"}}}, "artifacts": []}
            replay = root / "offline-replay.json"
            replay.write_text(json.dumps({"rows": [{"name": "N2C_receive_replay", "samples": [{"bytes": 123}]}]}))
            register_artifact(root, manifest, side, "replay", replay)
            (root / "manifest.json").write_text(json.dumps(manifest))
            result = summarize(root)
            self.assertEqual(result["network"][side], [])
            self.assertEqual(result["offlineReplay"][side][0]["samples"][0]["bytes"], 123)
            text = (root / "summary.md").read_text()
            self.assertIn("ATTRIBUTION ONLY", text)
            self.assertIn("NOT live whole path", text)
            self.assertIn("target .NETCoreApp,Version=v10.0", text)
            replay.write_text("modified")
            self.assertEqual(summarize(root)["offlineReplay"][side], [])

    def test_student_critical_values(self):
        for df, expected in [(1, 12.706204736), (2, 4.30265273), (4, 2.776445105), (9, 2.262157163), (30, 2.042272456)]:
            self.assertAlmostEqual(t95(df), expected, places=7)

    def test_one_observation_has_no_interval(self):
        self.assertIsNone(confidence95([12.0]))

    def test_constant_observations(self):
        self.assertEqual(confidence95([2.0, 2.0, 2.0]), [2.0, 2.0])

    def test_bdn_uses_normalized_result_iterations_only(self):
        raw = {"FullName": "Example.Read", "Statistics": {"Mean": 20},
               "Measurements": [{"IterationMode": "Workload", "IterationStage": "Result", "Nanoseconds": n * 4, "Operations": 4}
                                for n in [10, 20, 30]] +
                               [{"IterationMode": "Overhead", "IterationStage": "Result", "Nanoseconds": 1000, "Operations": 1}],
               "Memory": {"BytesAllocatedPerOperation": 0}}
        result = bdn_case(raw)
        self.assertEqual(result["samples"], 3)
        self.assertEqual(result["meanNs"], 20)
        self.assertEqual(result["allocatedBytesPerOperation"], 0)
        self.assertAlmostEqual(result["confidence95Ns"][1], 20 + t95(2) * 10 / math.sqrt(3))

    def test_missing_allocation_is_not_zero(self):
        self.assertIsNone(bdn_case({"FullName": "x", "Statistics": {"Mean": 1}})["allocatedBytesPerOperation"])

    def test_reproduction_keeps_entire_batch_range(self):
        audit = {"rows": {"x": {"minimum": 0, "maximum": 0, "kind": "exact"}}}
        self.assertEqual(reproduction(audit, {})[0]["status"], "not measured")
        result = reproduction(audit, {"x": [{"bytes": 0}, {"bytes": 8}]})[0]
        self.assertTrue(result["status"].startswith("difference"))
        self.assertEqual(result["maximumBytes"], 8)


if __name__ == "__main__":
    unittest.main()
