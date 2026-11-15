"""Review-two re-check: comparison-only inputs, read-only git and ignored output."""
import io
import json
import os
from pathlib import Path
import shutil
import sys
import unittest
from unittest.mock import patch
import uuid

sys.path.insert(0, str(Path(__file__).parent))
import run_benchmarks as runner
import summarize as renderer
from test_review2 import temporary_root, manifest, report, render


class ReviewThreeTests(unittest.TestCase):
    def test_single_side_or_attribution_pair_is_not_a_comparison(self):
        for sides in [("baseline",), ("candidate",), ("baseline", "baseline-runtime11")]:
            with self.subTest(sides=sides), temporary_root() as root:
                state = manifest()
                if "baseline-runtime11" in sides:
                    state["sides"]["baseline-runtime11"] = dict(state["sides"]["baseline"], attributionOnly=True)
                state["sides"] = {side: state["sides"][side] for side in sides}
                for side in sides:
                    report(root, state, side)
                result = render(root, state)
                self.assertFalse(result["gateInputEligible"], "A one-sided run was accepted as a comparison")
                self.assertEqual(result["status"], "NOT_A_COMPARISON")
                self.assertIn("baseline and candidate", result["gateInputReason"])
                self.assertIn("baseline and candidate", (root / "summary.md").read_text())
                with self.assertRaisesRegex(ValueError, "baseline and candidate"):
                    renderer.load_gate_input(root)

    def test_gate_reader_rechecks_sides_of_a_copied_result(self):
        with temporary_root() as root:
            state = manifest()
            for side in state["sides"]:
                report(root, state, side)
            result = render(root, state)
            self.assertTrue(result["gateInputEligible"])
            result["sides"].pop("candidate")
            (root / "results.json").write_text(json.dumps(result))
            with self.assertRaisesRegex(ValueError, "baseline and candidate"):
                renderer.load_gate_input(root)

    def test_every_git_command_output_disables_optional_locks(self):
        with patch.dict(os.environ, {"GIT_OPTIONAL_LOCKS": "1"}):
            for command in [("git", "status", "--porcelain"), ("git", "rev-parse", "HEAD"), ("/usr/bin/git", "status", "--porcelain")]:
                with self.subTest(command=command), patch.object(runner.subprocess, "check_output", return_value="ok\n") as call:
                    self.assertEqual(runner.command_output(*command), "ok")
                    self.assertEqual(call.call_args.kwargs.get("env", {}).get("GIT_OPTIONAL_LOCKS"), "0",
                                     "Git metadata commands can still refresh the real index")

    def test_nonignored_output_is_rejected_before_creation_or_build(self):
        output = runner.REPO / "benchmarks" / ("review3-unignored-output-" + uuid.uuid4().hex)
        self.assertFalse(output.exists())
        errors = io.StringIO()
        argv = ["run_benchmarks.py", "--side", "candidate", "--measure", "validate", "--mode", "smoke", "--output", str(output)]
        try:
            with patch.object(sys, "argv", argv), patch.object(sys, "stderr", errors), patch.object(runner, "perform") as perform:
                with self.assertRaises(SystemExit) as failure:
                    runner.main()
                self.assertEqual(failure.exception.code, 2)
                perform.assert_not_called()
            self.assertFalse(output.exists(), "Output validation already wrote into the source tree")
            self.assertIn("--output", errors.getvalue())
            self.assertIn("not ignored", errors.getvalue())
            self.assertIn("source tree", errors.getvalue())
        finally:
            # The red control creates a previously nonexistent, test-owned folder.
            if output.exists():
                shutil.rmtree(output)


if __name__ == "__main__":
    unittest.main()
