"""Offline runner opt-in/portability checks; fake launchers never invoke dotnet."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


RUNNER = Path(__file__).with_name("run.sh")


class RunnerOptInTests(unittest.TestCase):
    def invoke(self, arguments, inherited_opt_in, *, with_heavy=True, packages=None):
        with tempfile.TemporaryDirectory(prefix="chrysalis-runner-safety-") as directory:
            heavy = Path(directory) / ("heavy" if with_heavy else "dotnet")
            heavy.write_text(
                f"#!{sys.executable}\nimport json, os, sys\n"
                "print(json.dumps({'opt_in': os.environ.get('CHRYSALIS_IT_FUNDED'), 'arguments': sys.argv[1:], 'packages': os.environ.get('NUGET_PACKAGES'), 'launcher': os.path.basename(sys.argv[0])}))\n"
            )
            heavy.chmod(0o755)
            environment = {
                "PATH": directory + os.pathsep + os.defpath,
                "HOME": directory,
                "CHRYSALIS_WALLET_KEY_FILE": "synthetic-wallet-path-never-opened",
                "CHRYSALIS_IT_FUNDED": inherited_opt_in,
            }
            if packages is not None:
                environment["NUGET_PACKAGES"] = packages
            output = subprocess.check_output([str(RUNNER), *arguments], env=environment, text=True)
            return json.loads(output)

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


if __name__ == "__main__":
    unittest.main()
