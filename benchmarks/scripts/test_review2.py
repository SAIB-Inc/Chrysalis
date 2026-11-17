"""Narrow review-two regressions; execute only through heavy."""
from contextlib import contextmanager
import json
import io
import tarfile
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

sys.path.insert(0, str(Path(__file__).parent))
import run_benchmarks as runner
import summarize as renderer
from provenance import register_artifact


@contextmanager
def temporary_root():
    parent = runner.REPO / "benchmarks/out/test-temporary"
    parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=parent) as directory:
        yield Path(directory)


def manifest():
    return {"schemaVersion": 2, "invocationId": "review2", "status": "COMPLETED", "mode": "smoke",
            "measure": "micro", "filter": "*", "machine": {"hostname": "test", "cpu": "test",
            "logicalCpus": 1, "os": "test"}, "wholePathStatus": "not measured: offline unit proof",
            "sides": {side: {"commit": side + "-commit", "dirty": side == "candidate", "sdk": sdk,
            "treeId": side + "-exact-tree", "harnessSha256": "same-harness", "benchmarkDotNetVersion": "0.15.8",
            "environment": {"runtime": runtime, "benchmarkDotNet": "0.15.8.0"}}
            for side, sdk, runtime in [("baseline", "10.0.301", "10.0.9"), ("candidate", "11.0.100", "11.0.0")]},
            "artifacts": []}


def report(root, state, side, name="Example.Read"):
    path = root / side / "micro-00/bdn/results/Example-report-full.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"Benchmarks": [{"FullName": name, "Statistics": {"Mean": 10},
                    "Measurements": [{"IterationMode": "Workload", "IterationStage": "Result", "Operations": 1,
                    "Nanoseconds": 10}], "Memory": {"BytesAllocatedPerOperation": 4}}]}))
    register_artifact(root, state, side, "micro", path)
    return path


def render(root, state):
    (root / "manifest.json").write_text(json.dumps(state))
    return renderer.summarize(root)


class ReviewTwoTests(unittest.TestCase):
    def test_failed_invocation_is_loud_and_has_no_gate_measurements(self):
        with temporary_root() as root:
            state = manifest()
            state.pop("status")
            state["failure"] = "NoSuchCase*: benchmark process exited 1"
            report(root, state, "baseline")
            result = render(root, state)
            self.assertEqual(result.get("status"), "FAILED")
            self.assertFalse(result.get("gateInputEligible", True))
            self.assertEqual(result["benchmarks"]["baseline"], [])
            text = (root / "summary.md").read_text()
            self.assertIn("FAILED", text.splitlines()[0])
            self.assertIn(state["failure"], text)
            self.assertTrue(callable(getattr(renderer, "load_gate_input", None)), "Missing fail-closed result reader")
            with self.assertRaisesRegex(ValueError, "FAILED"):
                renderer.load_gate_input(root)

    def test_complete_matching_cases_pass_the_completeness_reader(self):
        with temporary_root() as root:
            state = manifest()
            for side in state["sides"]:
                report(root, state, side)
            result = render(root, state)
            self.assertEqual(result["status"], "COMPLETED")
            self.assertTrue(result["gateInputEligible"])
            self.assertEqual(renderer.load_gate_input(root)["invocationId"], state["invocationId"])

    def test_main_records_failure_before_rendering_summary(self):
        with temporary_root() as parent:
            output = parent / "invocation"
            argv = ["run_benchmarks.py", "--side", "candidate", "--measure", "micro", "--mode", "smoke",
                    "--filter", "NoSuchCase*", "--output", str(output)]
            def old_external_summary(*arguments, **keywords):
                renderer.summarize(output)
                return 0
            with patch.object(sys, "argv", argv), patch.object(runner, "perform", side_effect=RuntimeError("NoSuchCase* exited 1")), \
                    patch.object(runner.subprocess, "call", side_effect=old_external_summary):
                with self.assertRaisesRegex(RuntimeError, "NoSuchCase"):
                    runner.main()
            stored = json.loads((output / "manifest.json").read_text())
            result = json.loads((output / "results.json").read_text())
            self.assertEqual(stored.get("status"), "FAILED")
            self.assertEqual(result.get("status"), "FAILED")
            self.assertIn("NoSuchCase* exited 1", (output / "summary.md").read_text())

    def test_case_missing_on_peer_has_a_missing_row(self):
        with temporary_root() as root:
            state = manifest()
            report(root, state, "baseline", "Example.BaselineOnly")
            report(root, state, "candidate", "Example.CandidateOnly")
            result = render(root, state)
            peer = next((row for row in result["benchmarks"]["candidate"] if row["name"] == "Example.BaselineOnly"), None)
            self.assertIsNotNone(peer, "Peer case silently disappeared instead of a MISSING row")
            self.assertEqual(peer["status"], "MISSING")
            coverage = next(row for row in result["caseCoverage"] if row["side"] == "candidate" and row["name"] == "Example.BaselineOnly")
            self.assertEqual(coverage["status"], "MISSING", "Coverage matrix must not relabel an absent case as REJECTED")
            self.assertEqual(result.get("status"), "INCOMPLETE")
            self.assertFalse(result.get("gateInputEligible", True))
            self.assertIn("MISSING", (root / "summary.md").read_text())

    def test_rejected_artifact_has_a_reason_and_rejected_case_row(self):
        with temporary_root() as root:
            state = manifest()
            report(root, state, "baseline")
            rejected = report(root, state, "candidate")
            rejected.write_text(rejected.read_text() + " ")
            result = render(root, state)
            peer = next((row for row in result["benchmarks"]["candidate"] if row["name"] == "Example.Read"), None)
            self.assertIsNotNone(peer, "Rejected peer case silently disappeared")
            self.assertEqual(peer["status"], "REJECTED")
            self.assertIn("content SHA256", peer["reason"])
            self.assertTrue(result.get("rejectedArtifacts"))
            self.assertIn(str(rejected.relative_to(root)), (root / "summary.md").read_text())
            self.assertFalse(result.get("gateInputEligible", True))

    def test_bdn_statistics_failure_is_rejected_with_reason(self):
        result = renderer.bdn_case({"FullName": "Example.Bad", "Statistics": None})
        self.assertEqual(result["status"], "REJECTED")
        self.assertIn("statistics", result.get("reason", "").lower())

    def test_source_tree_and_bdn_version_are_visible_per_side(self):
        with temporary_root() as root:
            state = manifest()
            result = render(root, state)
            text = (root / "summary.md").read_text()
            for side, info in state["sides"].items():
                self.assertEqual(result["sides"][side]["treeId"], info["treeId"])
                self.assertIn(info["treeId"], text)
            self.assertEqual(text.count("BenchmarkDotNet 0.15.8"), 2)

    def test_content_id_tracks_untracked_source_without_changing_real_index(self):
        function = getattr(runner, "working_tree_id", None)
        self.assertTrue(callable(function), "Missing exact working-tree content id")
        repo = runner.REPO
        index = Path(runner.command_output("git", "-C", str(repo), "rev-parse", "--git-path", "index"))
        if not index.is_absolute():
            index = repo / index
        before = index.read_bytes()
        path = repo / "benchmarks/scripts" / ("review2-content-control-" + uuid.uuid4().hex + ".txt")
        original = function(repo)
        try:
            path.write_text("one\n")
            first = function(repo)
            path.write_text("two\n")
            second = function(repo)
            self.assertNotEqual(original, first)
            self.assertNotEqual(first, second)
        finally:
            path.unlink(missing_ok=True)
        self.assertEqual(function(repo), original)
        self.assertEqual(index.read_bytes(), before, "The real index was changed")

    def test_clean_baseline_content_id_is_its_head_tree(self):
        function = getattr(runner, "working_tree_id", None)
        self.assertTrue(callable(function), "Missing baseline working-tree content id")
        # A clean HEAD export is enough; no sibling checkout is required on the Mac.
        with temporary_root() as repo:
            data = runner.subprocess.check_output(["git", "-C", str(runner.REPO), "archive", "HEAD"])
            with tarfile.open(fileobj=io.BytesIO(data)) as archive:
                for member in archive:
                    destination = repo / member.name
                    self.assertTrue(destination.resolve().is_relative_to(repo.resolve()))
                    if member.isdir():
                        destination.mkdir(parents=True, exist_ok=True)
                    else:
                        self.assertTrue(member.isfile())
                        destination.parent.mkdir(parents=True, exist_ok=True)
                        destination.write_bytes(archive.extractfile(member).read())
                        destination.chmod(member.mode)
            git_dir = runner.command_output("git", "-C", str(runner.REPO), "rev-parse", "--absolute-git-dir")
            (repo / ".git").write_text("gitdir: " + git_dir + "\n")
            self.assertEqual(function(repo), runner.command_output("git", "-C", str(runner.REPO), "rev-parse", "HEAD^{tree}"))

    def test_resolved_bdn_package_is_read_from_assets(self):
        function = getattr(runner, "resolved_benchmarkdotnet_version", None)
        self.assertTrue(callable(function), "Resolved BenchmarkDotNet package is not checked")
        with temporary_root() as root:
            (root / "obj").mkdir()
            (root / "obj/project.assets.json").write_text(json.dumps({"libraries": {"BenchmarkDotNet/0.15.8": {}}}))
            self.assertEqual(function(root), "0.15.8")

    def test_bdn_mismatch_is_refused_before_any_measurement(self):
        with temporary_root() as output:
            state = manifest()
            state["sides"] = {}
            labels = []
            args = SimpleNamespace(runtime11_version="11.0.0", measure="direct", mode="smoke", filter="*", pairs=None,
                                   skip_network=True, n2n_container=False, node_already_running=True)
            def stage(side, source, destination, snapshot, *, sdk, tfm):
                self.assertEqual((sdk, tfm), ("10.0.301", "net10.0") if side == "baseline" else ("11.0.100", "net11.0"))
                directory = destination / "build" / side
                directory.mkdir(parents=True)
                binary = directory / "bin/Chrysalis.Benchmarks.dll"
                binary.parent.mkdir()
                binary.write_bytes(side.encode())
                (directory / "obj").mkdir()
                version = "0.15.8" if side == "baseline" else "0.15.7"
                (directory / "obj/project.assets.json").write_text(json.dumps({"libraries": {"BenchmarkDotNet/" + version: {}}}))
                return directory, binary, "10.0.301" if side == "baseline" else "11.0.100"
            def run(command, destination, label, env=None, cwd=None):
                labels.append(label)
                destination.mkdir(parents=True, exist_ok=True)
                side = destination.name
                if label == "sdk":
                    (destination / "sdk.log").write_text("10.0.301" if side == "baseline" else "11.0.100")
                elif label == "environment":
                    (destination / "environment.log").write_text(json.dumps({"runtime": "10.0.9" if side == "baseline" else "11.0.0",
                                                                            "benchmarkDotNet": "0.15.8.0" if side == "baseline" else "0.15.7.0"}))
                elif label == "direct":
                    (destination / "direct.json").write_text("[]")
            def git_output(*arguments):
                return "" if "status" in arguments else "source-commit"
            with patch.object(runner, "stage", side_effect=stage), patch.object(runner, "run", side_effect=run), \
                    patch.object(runner, "working_tree_id", return_value="exact-tree", create=True), \
                    patch.object(runner, "command_output", side_effect=git_output), patch.object(runner.subprocess, "call", return_value=0):
                with self.assertRaisesRegex(RuntimeError, "BenchmarkDotNet.*0.15.8.*0.15.7"):
                    runner.perform(args, output, state, {"baseline": output, "candidate": output}, ["baseline", "candidate"])
            self.assertFalse(set(labels) & {"validate", "direct", "benchmarks", "n2c", "n2n", "node-start"})

    def test_n2n_container_command_never_pulls(self):
        function = getattr(runner, "n2n_container_command", None)
        self.assertTrue(callable(function), "N2N command has no local-only constructor")
        work = runner.REPO.parent
        command = function("example/runtime:11", work / "binary.dll", work / "results", work / "range.json")
        self.assertIn("--pull=never", command)
        self.assertIn("container:chrysalis-preprod-node", command)
        self.assertNotIn("pull", command)

    def test_missing_local_image_fails_clearly_without_node_or_pull(self):
        function = getattr(runner, "require_local_runtime_image", None)
        self.assertTrue(callable(function), "Missing local runtime image is not preflighted")
        with temporary_root() as root, patch.object(runner, "run", side_effect=RuntimeError("inspect exited 1")) as run:
            with self.assertRaisesRegex(RuntimeError, "local-only.*never pulled"):
                function("99.99.99", root)
            self.assertEqual(run.call_args.args[0][:3], ["docker", "image", "inspect"])


if __name__ == "__main__":
    unittest.main()
