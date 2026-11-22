"""Offline runner opt-in/portability checks; fake launchers never invoke dotnet."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


RUNNER = Path(__file__).with_name("run.sh")
DEVNET = Path(__file__).resolve().parents[2] / "test" / "devnet" / "devnet.sh"


class RunnerOptInTests(unittest.TestCase):
    def invoke(self, arguments, inherited_opt_in, *, with_heavy=True, packages=None, with_wallet=True, magic=None):
        with tempfile.TemporaryDirectory(prefix="chrysalis-runner-safety-") as directory:
            heavy = Path(directory) / ("heavy" if with_heavy else "dotnet")
            heavy.write_text(
                f"#!{sys.executable}\nimport json, os, sys\n"
                "print(json.dumps({'opt_in': os.environ.get('CHRYSALIS_IT_FUNDED'), 'arguments': sys.argv[1:], 'packages': os.environ.get('NUGET_PACKAGES'), 'launcher': os.path.basename(sys.argv[0]), 'dijkstra': os.environ.get('CHRYSALIS_DIJKSTRA_PROBES')}))\n"
            )
            heavy.chmod(0o755)
            environment = {
                "PATH": directory + os.pathsep + os.defpath,
                "HOME": directory,
                "CHRYSALIS_WALLET_KEY_FILE": "synthetic-wallet-path-never-opened",
                "CHRYSALIS_IT_FUNDED": inherited_opt_in,
            }
            if not with_wallet:
                environment.pop("CHRYSALIS_WALLET_KEY_FILE")
            if packages is not None:
                environment["NUGET_PACKAGES"] = packages
            if magic is not None:
                environment["CHRYSALIS_NETWORK_MAGIC"] = str(magic)
            output = subprocess.check_output([str(RUNNER), *arguments], env=environment, text=True)
            return json.loads(output)

    def test_dijkstra_sync_clears_inherited_funding_without_wallet(self):
        result = self.invoke(["--dijkstra"], "1", with_wallet=False)
        self.assertIsNone(result["opt_in"])
        self.assertEqual("1", result["dijkstra"])
        self.assertIn("Category=Integration&FullyQualifiedName~NodeToNodeSyncKeepsEveryBlockAcrossTheFork", result["arguments"])

    def test_dijkstra_funding_needs_its_explicit_flag(self):
        result = self.invoke(["--dijkstra", "--funded"], "0")
        self.assertEqual("1", result["opt_in"])
        self.assertIn("Category=Integration&FullyQualifiedName~ConwaySelfTransferKeepsTheDijkstraNodesAnswer", result["arguments"])

    def test_dijkstra_funded_flag_order_is_equivalent(self):
        forward = self.invoke(["--dijkstra", "--funded", "--no-build"], "0")
        reverse = self.invoke(["--funded", "--dijkstra", "--no-build"], "0")
        self.assertEqual(forward["arguments"], reverse["arguments"])
        self.assertEqual(forward["dijkstra"], reverse["dijkstra"])

    def test_dijkstra_uses_the_portable_launcher_and_cache(self):
        result = self.invoke(["--dijkstra"], "1", with_heavy=False, with_wallet=False)
        self.assertEqual("dotnet", result["launcher"])
        self.assertIsNone(result["opt_in"])
        self.assertTrue(result["packages"].endswith("/.nuget/packages"))

    def test_empty_launcher_uses_legacy_nounset_safe_expansion(self):
        # Current CI Bash cannot reproduce the pre-4.4 nounset-array bug. Keep
        # its portable expansion explicit, alongside the runtime fallback test.
        source = RUNNER.read_text()
        self.assertNotIn('exec "${launcher[@]}"', source)
        self.assertIn('${launcher[@]+"${launcher[@]}"}', source)

    def test_preserves_preconfigured_package_cache(self):
        result = self.invoke([], "0", packages="/synthetic/preconfigured/cache")
        self.assertEqual("/synthetic/preconfigured/cache", result["packages"])
        self.assertEqual("heavy", result["launcher"])

    def test_runs_dotnet_directly_when_heavy_is_absent(self):
        result = self.invoke([], "0", with_heavy=False)
        self.assertEqual("dotnet", result["launcher"])
        self.assertEqual("test", result["arguments"][0])

    def test_default_cache_uses_portable_home_location(self):
        result = self.invoke([], "0")
        self.assertTrue(result["packages"].endswith("/.nuget/packages"))
        self.assertIn("chrysalis-runner-safety-", result["packages"])

    def test_default_clears_inherited_funding_opt_in(self):
        self.assertIsNone(self.invoke([], "1")["opt_in"])

    def test_include_known_clears_inherited_funding_opt_in(self):
        self.assertIsNone(self.invoke(["--include-known"], "1")["opt_in"])

    def test_funded_sets_explicit_opt_in_for_this_invocation_only(self):
        result = self.invoke(["--funded"], "0")
        self.assertEqual("1", result["opt_in"])
        self.assertIn("Category=Integration&FullyQualifiedName~FundedIntegrationTests", result["arguments"])

    def test_devnet_opts_in_and_excludes_what_the_devnet_cannot_serve(self):
        result = self.invoke(["--devnet", "--no-build"], "0", magic="42")
        self.assertEqual("1", result["opt_in"])
        self.assertIsNone(result["dijkstra"])
        self.assertIn(
            "Category=Integration&Known!=F2&FullyQualifiedName!~DijkstraIntegrationTests&FullyQualifiedName!~BlockfrostPreprodParametersMatchCli",
            result["arguments"],
        )
        self.assertIn("--no-build", result["arguments"])

    def test_devnet_refuses_another_network(self):
        for magic in ("1", None):
            with tempfile.TemporaryDirectory(prefix="chrysalis-runner-safety-") as directory:
                marker = Path(directory) / "ran"
                heavy = Path(directory) / "heavy"
                heavy.write_text(f"#!{sys.executable}\nfrom pathlib import Path\nPath({str(marker)!r}).write_text('ran')\n")
                heavy.chmod(0o755)
                env = {"PATH": directory + os.pathsep + os.defpath, "HOME": directory}
                if magic is not None:
                    env["CHRYSALIS_NETWORK_MAGIC"] = magic
                result = subprocess.run([str(RUNNER), "--devnet"], env=env, text=True, capture_output=True)
                self.assertEqual(2, result.returncode)
                self.assertIn("run.sh --devnet: CHRYSALIS_NETWORK_MAGIC is not the devnet's", result.stderr)
                self.assertFalse(marker.exists())

    def test_devnet_sh_keeps_a_failure_status(self):
        result = subprocess.run(["bash", str(DEVNET)], text=True, capture_output=True)
        self.assertEqual(2, result.returncode)
        self.assertIn("Usage:", result.stderr)
        self.assertNotIn("Devnet: ready", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
