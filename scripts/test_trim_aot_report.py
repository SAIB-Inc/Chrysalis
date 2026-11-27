"""Unit tests for scripts/trim-aot-report.py."""

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().with_name("trim-aot-report.py")


def load_module():
    if not SCRIPT.is_file():
        raise FileNotFoundError(f"Missing target script: {SCRIPT}")
    spec = importlib.util.spec_from_file_location("trim_aot_report", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def make_build_log(stage_root: Path, lines: list[str], *, banner: str = "Build FAILED.", summary_lines: list[str] | None = None) -> str:
    root = stage_root.as_posix().rstrip("/")
    canary_trim = (
        f"{root}/src/Chrysalis.Codec/__TrimAotCanary.g.cs(16,9): error IL2026: "
        f"Using member 'Chrysalis.Codec.__TrimAotCanary.TriggerTrim()' which has 'RequiresUnreferencedCodeAttribute' "
        f"can break functionality when trimming application code. X11_TRIM_CANARY. "
        f"[{root}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
    )
    canary_aot = (
        f"{root}/src/Chrysalis.Codec/__TrimAotCanary.g.cs(17,9): error IL3050: "
        f"Using member 'Chrysalis.Codec.__TrimAotCanary.TriggerAot()' which has 'RequiresDynamicCodeAttribute' "
        f"can require dynamic code generation at runtime. X11_AOT_CANARY. "
        f"[{root}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
    )
    body = [canary_trim, canary_aot, *lines]
    if banner:
        body.append("")
        body.append(banner)
    if summary_lines:
        body.extend(summary_lines)
    return "\n".join(body) + "\n"


def init_repo_tree(repo: Path) -> str:
    subprocess.run(["git", "init", "-q"], cwd=repo, check=True)
    subprocess.run(["git", "add", "."], cwd=repo, check=True)
    return subprocess.check_output(["git", "write-tree"], cwd=repo, text=True).strip()


def make_assets(illink: bool = True) -> dict:
    libs = {}
    if illink:
        libs["Microsoft.NET.ILLink.Tasks/10.0.0"] = {"type": "package"}
    return {
        "version": 3,
        "targets": {"net11.0": dict(libs)},
        "libraries": libs,
    }


class TrimAotReportTests(unittest.TestCase):
    def run_with_logs(
        self,
        base_lines: list[str],
        cand_lines: list[str],
        *,
        base_banner: str = "Build FAILED.",
        cand_banner: str = "Build FAILED.",
        base_summary: list[str] | None = None,
        cand_summary: list[str] | None = None,
        base_include_trim_canary: bool = True,
        base_include_aot_canary: bool = True,
        cand_include_trim_canary: bool = True,
        cand_include_aot_canary: bool = True,
        cand_illink: bool = True,
        base_rc: int = 1,
        cand_rc: int = 1,
    ):
        mod = load_module()
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            repo = tmp_path / "repo"
            repo.mkdir()
            (repo / "src/Chrysalis.Codec").mkdir(parents=True)
            (repo / "src/Chrysalis.Codec/Chrysalis.Codec.csproj").write_text("<Project />", encoding="utf-8")
            (repo / "src/Chrysalis.Codec.CodeGen").mkdir(parents=True)
            (repo / "src/Chrysalis.Codec.CodeGen/Chrysalis.Codec.CodeGen.csproj").write_text("<Project />", encoding="utf-8")
            base_sha = init_repo_tree(repo)

            nuget = tmp_path / "nuget"
            (nuget / "microsoft.net.illink.tasks").mkdir(parents=True)
            json_out = tmp_path / "out/report.json"

            def fake_run(cmd, *, cwd, env, stdout_to_stderr=True):
                stage = Path(cwd)
                side = stage.name  # "base" or "candidate"
                if cmd[:2] == ["dotnet", "restore"] and "Chrysalis.Codec.csproj" in cmd[2]:
                    assets = stage / "src/Chrysalis.Codec/obj/project.assets.json"
                    assets.parent.mkdir(parents=True, exist_ok=True)
                    has_illink = cand_illink if side == "candidate" else True
                    assets.write_text(json.dumps(make_assets(has_illink)), encoding="utf-8")
                    return 0, ""
                if cmd[:2] == ["dotnet", "build"] and "Chrysalis.Codec.csproj" in cmd[2]:
                    if side == "base":
                        lines = list(base_lines)
                        inc_trim, inc_aot = base_include_trim_canary, base_include_aot_canary
                        banner, summary, rc = base_banner, base_summary, base_rc
                    else:
                        lines = list(cand_lines)
                        inc_trim, inc_aot = cand_include_trim_canary, cand_include_aot_canary
                        banner, summary, rc = cand_banner, cand_summary, cand_rc
                    root = stage.as_posix().rstrip("/")
                    built: list[str] = []
                    if inc_trim:
                        built.append(
                            f"{root}/src/Chrysalis.Codec/__TrimAotCanary.g.cs(16,9): error IL2026: "
                            f"Using member 'Chrysalis.Codec.__TrimAotCanary.TriggerTrim()' which has 'RequiresUnreferencedCodeAttribute' "
                            f"can break functionality when trimming application code. X11_TRIM_CANARY. "
                            f"[{root}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
                        )
                    if inc_aot:
                        built.append(
                            f"{root}/src/Chrysalis.Codec/__TrimAotCanary.g.cs(17,9): error IL3050: "
                            f"Using member 'Chrysalis.Codec.__TrimAotCanary.TriggerAot()' which has 'RequiresDynamicCodeAttribute' "
                            f"can require dynamic code generation at runtime. X11_AOT_CANARY. "
                            f"[{root}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
                        )
                    built.extend(line.replace("{ROOT}", root) for line in lines)
                    if banner:
                        built.append("")
                        built.append(banner)
                    if summary:
                        built.extend(summary)
                    return rc, "\n".join(built) + "\n"
                return 0, ""

            return mod.run_report(repo, base_sha, json_path=json_out, nuget_packages=nuget, command_runner=fake_run)

    def test_zero_diff_passes_with_both_canaries_verified_and_stripped(self):
        base_diag = (
            "{ROOT}/src/Chrysalis.Codec/Serialization/GenericSerializationUtil.cs(45,17): error IL2026: "
            "Using member 'Method' which has 'RequiresUnreferencedCodeAttribute'. "
            "[{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        )
        code, report = self.run_with_logs([base_diag], [base_diag])
        self.assertEqual(code, 0)
        self.assertEqual(report["canaries_verified"], {
            "base": {"IL2026": True, "IL3050": True},
            "candidate": {"IL2026": True, "IL3050": True},
        })
        self.assertEqual(report["base_total"], 1)
        self.assertEqual(report["candidate_total"], 1)
        self.assertEqual(report["added"], [])
        self.assertEqual(report["removed"], [])
        self.assertEqual(report["by_code"], {"IL2026": {"base": 1, "candidate": 1}})

    def test_added_warning_fails_with_exact_entry(self):
        base_diag = "{ROOT}/src/Chrysalis.Codec/A.cs(10,5): error IL2026: Trim msg [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        new_diag = "{ROOT}/src/Chrysalis.Codec/B.cs(20,5): error IL2075: Reflect msg [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        code, report = self.run_with_logs([base_diag], [base_diag, new_diag])
        self.assertEqual(code, 1)
        self.assertEqual(len(report["added"]), 1)
        self.assertEqual(report["added"][0]["code"], "IL2075")
        self.assertEqual(report["added"][0]["path"], "src/Chrysalis.Codec/B.cs")
        self.assertEqual(report["added"][0]["count"], 1)

    def test_duplicate_identical_warning_in_same_file_fails(self):
        d1 = "{ROOT}/src/Chrysalis.Codec/A.cs(10,5): error IL2026: Identical msg [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        d2 = "{ROOT}/src/Chrysalis.Codec/A.cs(99,5): error IL2026: Identical msg [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        code, report = self.run_with_logs([d1], [d1, d2])
        self.assertEqual(code, 1)
        self.assertEqual(report["base_total"], 1)
        self.assertEqual(report["candidate_total"], 2)
        self.assertEqual(len(report["added"]), 1)
        self.assertEqual(report["added"][0]["count"], 1)

    def test_removed_warning_passes(self):
        d1 = "{ROOT}/src/Chrysalis.Codec/A.cs(10,5): error IL2026: Msg one [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        d2 = "{ROOT}/src/Chrysalis.Codec/B.cs(12,5): error IL3050: Msg two [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        code, report = self.run_with_logs([d1, d2], [d1])
        self.assertEqual(code, 0)
        self.assertEqual(report["added"], [])
        self.assertEqual(len(report["removed"]), 1)
        self.assertEqual(report["removed"][0]["code"], "IL3050")

    def test_coded_non_il_error_fails_closed(self):
        cs_err = "{ROOT}/src/Chrysalis.Codec/Broken.cs(3,1): error CS0103: The name 'x' does not exist [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        with self.assertRaises(RuntimeError):
            self.run_with_logs([], [cs_err])

    def test_missing_illink_tasks_fails_closed(self):
        with self.assertRaises(RuntimeError):
            self.run_with_logs([], [], cand_illink=False)

    def test_build_failed_summary_block_counts_diagnostics_once(self):
        mod = load_module()
        stage = Path("/tmp/stage-one/base")
        diag = (
            "/tmp/stage-one/base/src/Chrysalis.Codec/Foo.cs(12,3): error IL2026: Msg "
            "[/tmp/stage-one/base/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        )
        log = make_build_log(stage, [diag], banner="Build FAILED.", summary_lines=[diag, diag])
        counts, canaries, saw_succeeded = mod.parse_build_output(log, stage)
        self.assertFalse(saw_succeeded)
        self.assertEqual(canaries, {"IL2026": True, "IL3050": True})
        self.assertEqual(sum(counts.values()), 1)
        self.assertEqual(counts[("IL2026", "src/Chrysalis.Codec/Foo.cs", "Msg [src/Chrysalis.Codec/Chrysalis.Codec.csproj]")], 1)

    def test_path_normalization_strips_staging_root_from_leading_and_trailing_bracket(self):
        d_base = "{ROOT}/src/Chrysalis.Codec/Bar.cs(11,2): error IL2067: Param Msg [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        d_cand = "{ROOT}/src/Chrysalis.Codec/Bar.cs(44,8): error IL2067: Param Msg [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        code, report = self.run_with_logs([d_base], [d_cand])
        self.assertEqual(code, 0)
        self.assertEqual(report["added"], [])
        self.assertEqual(report["removed"], [])

    def test_missing_trim_or_aot_canary_despite_illink_fails_closed(self):
        with self.assertRaises(RuntimeError):
            self.run_with_logs([], [], cand_include_trim_canary=False)
        with self.assertRaises(RuntimeError):
            self.run_with_logs([], [], cand_include_aot_canary=False)
        with self.assertRaises(RuntimeError):
            self.run_with_logs([], [], base_include_trim_canary=False)

    def test_il_diagnostic_followed_by_uncoded_error_or_truncated_build_fails_closed(self):
        mod = load_module()
        stage = Path("/tmp/stage-two/candidate")
        uncoded = make_build_log(stage, ["MSBUILD : error : Unexpected internal failure"], banner="Build FAILED.")
        with self.assertRaises(RuntimeError):
            mod.parse_build_output(uncoded, stage)

        truncated = make_build_log(stage, [], banner="")
        with self.assertRaises(RuntimeError):
            mod.parse_build_output(truncated, stage)

    def test_step4_stderr_error_merged_and_fails_closed(self):
        mod = load_module()
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            # Verify default_command_runner uses stderr=subprocess.STDOUT so stderr errors are parsed and fail closed.
            rc, merged = mod.default_command_runner(
                [sys.executable, "-c", "import sys; sys.stdout.write('ok\\n'); sys.stderr.write('CSC : error : fatal stderr error\\n'); sys.exit(1)"],
                cwd=tmp_path,
                env={},
            )
            self.assertEqual(rc, 1)
            self.assertIn("fatal stderr error", merged)
            log = make_build_log(tmp_path, [merged.strip()], banner="Build FAILED.")
            with self.assertRaises(RuntimeError):
                mod.parse_build_output(log, tmp_path)

    def test_stage_candidate_uses_git_c_and_skips_deleted_and_symlink(self):
        mod = load_module()
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            repo = tmp_path / "repo"
            elsewhere = tmp_path / "elsewhere"
            base_dest = tmp_path / "staged-base"
            cand_dest = tmp_path / "staged-cand"
            elsewhere.mkdir()
            repo.mkdir()
            (repo / "src").mkdir()
            (repo / "src/Keep.cs").write_text("base", encoding="utf-8")
            (repo / "src/Deleted.cs").write_text("will delete", encoding="utf-8")
            base_sha = init_repo_tree(repo)

            (repo / "src/Keep.cs").write_text("modified", encoding="utf-8")
            (repo / "src/Untracked.cs").write_text("added", encoding="utf-8")
            (repo / "src/Deleted.cs").unlink()
            (repo / "src/Link.cs").symlink_to(repo / "src/Keep.cs")

            orig_cwd = Path.cwd()
            try:
                import os
                os.chdir(elsewhere)
                mod.stage_base_tree(repo, base_sha, base_dest)
                mod.stage_candidate_tree(repo, cand_dest)
            finally:
                os.chdir(orig_cwd)

            self.assertEqual((base_dest / "src/Keep.cs").read_text(encoding="utf-8"), "base")
            self.assertTrue((base_dest / "src/Deleted.cs").is_file())
            self.assertEqual((cand_dest / "src/Keep.cs").read_text(encoding="utf-8"), "modified")
            self.assertEqual((cand_dest / "src/Untracked.cs").read_text(encoding="utf-8"), "added")
            self.assertFalse((cand_dest / "src/Deleted.cs").exists())
            self.assertFalse((cand_dest / "src/Link.cs").exists())


    def test_nonzero_exit_with_build_succeeded_banner_fails_closed(self):
        base_diag = "{ROOT}/src/Chrysalis.Codec/A.cs(10,5): error IL2026: Msg [{ROOT}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
        with self.assertRaises(RuntimeError):
            self.run_with_logs([base_diag], [base_diag], cand_rc=1, cand_banner="Build succeeded.")
        with self.assertRaises(RuntimeError):
            self.run_with_logs([base_diag], [base_diag], cand_rc=0, cand_banner="Build FAILED.")

    def test_nuget_path_xml_special_chars_produce_valid_xml(self):
        mod = load_module()
        import xml.etree.ElementTree as ET
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            repo = tmp_path / "repo"
            (repo / "src/Chrysalis.Codec").mkdir(parents=True)
            (repo / "src/Chrysalis.Codec/Chrysalis.Codec.csproj").write_text("<Project />", encoding="utf-8")
            (repo / "src/Chrysalis.Codec.CodeGen").mkdir(parents=True)
            (repo / "src/Chrysalis.Codec.CodeGen/Chrysalis.Codec.CodeGen.csproj").write_text("<Project />", encoding="utf-8")
            base_sha = init_repo_tree(repo)
            nuget = tmp_path / "pkg&cache'dir"
            (nuget / "microsoft.net.illink.tasks").mkdir(parents=True)
            seen_configs: list[str] = []

            def fake_run(cmd, *, cwd, env, stdout_to_stderr=True):
                stage = Path(cwd)
                if cmd[:2] == ["dotnet", "restore"] and "--configfile" in cmd:
                    cfg = Path(cmd[cmd.index("--configfile") + 1]).read_text(encoding="utf-8")
                    seen_configs.append(cfg)
                    assets = stage / "src/Chrysalis.Codec/obj/project.assets.json"
                    assets.parent.mkdir(parents=True, exist_ok=True)
                    assets.write_text(json.dumps(make_assets(True)), encoding="utf-8")
                    return 0, ""
                if cmd[:2] == ["dotnet", "build"] and "Chrysalis.Codec.csproj" in cmd[2]:
                    return 1, make_build_log(stage, [], banner="Build FAILED.")
                return 0, ""

            code, _ = mod.run_report(repo, base_sha, nuget_packages=nuget, command_runner=fake_run)
            self.assertEqual(code, 0)
            self.assertTrue(seen_configs)
            parsed = ET.fromstring(seen_configs[0])
            add_el = parsed.find("./packageSources/add")
            self.assertIsNotNone(add_el)
            self.assertEqual(add_el.attrib["value"], str(nuget))

    def test_normalize_text_strips_leading_dot_slash_only_for_paths(self):
        mod = load_module()
        stage = Path("/tmp/stage-norm")
        self.assertEqual(mod.normalize_text("./src/A.cs", stage, is_path=True), "src/A.cs")
        self.assertEqual(mod.normalize_text("./src/A.cs", stage, is_path=False), "./src/A.cs")
        self.assertEqual(mod.normalize_text(".leading message", stage, is_path=False), ".leading message")
        self.assertEqual(mod.normalize_text("././src/A.cs", stage, is_path=True), "src/A.cs")
        self.assertEqual(
            mod.normalize_text("/tmp/stage-norm/src/Chrysalis.Codec/__TrimAotCanary.g.cs", stage, is_path=True),
            mod.CANARY_REL,
        )
        for bad_path in (
            "../src/Chrysalis.Codec/__TrimAotCanary.g.cs",
            "/src/Chrysalis.Codec/__TrimAotCanary.g.cs",
            "/tmp/stage-norm-other/src/Chrysalis.Codec/__TrimAotCanary.g.cs",
            "/other/tmp/stage-norm/src/Chrysalis.Codec/__TrimAotCanary.g.cs",
            "./../src/Chrysalis.Codec/__TrimAotCanary.g.cs",
        ):
            self.assertNotEqual(mod.normalize_text(bad_path, stage, is_path=True), mod.CANARY_REL)
        msg_stage = Path("/t/candidate")
        self.assertEqual(mod.normalize_text("warn [/t/candidate/Shared/P.csproj]", msg_stage, is_path=False), "warn [Shared/P.csproj]")
        self.assertEqual(mod.normalize_text("warn [/t/candidateShared/P.csproj]", msg_stage, is_path=False), "warn [/t/candidateShared/P.csproj]")
        self.assertNotEqual(mod.normalize_text("warn [/t/candidateShared/P.csproj]", msg_stage, is_path=False), "warn [Shared/P.csproj]")
        self.assertEqual(mod.normalize_text("warn [/other/t/candidate/Shared/P.csproj]", msg_stage, is_path=False), "warn [/other/t/candidate/Shared/P.csproj]")
        self.assertEqual(mod.normalize_text("warn [/t/candidate]", msg_stage, is_path=False), "warn []")
        self.assertEqual(mod.normalize_text(r"warn [/t\candidate\Shared\P.csproj]", msg_stage, is_path=False), "warn [Shared/P.csproj]")

    def test_parse_build_output_distinguishes_in_stage_and_sibling_project_suffixes(self):
        mod = load_module()
        stage = Path("/t/candidate")
        in_stage = "/t/candidate/src/Chrysalis.Codec/A.cs(10,5): error IL2026: Msg [/t/candidate/Shared/P.csproj]"
        sibling = "/t/candidate/src/Chrysalis.Codec/A.cs(10,5): error IL2026: Msg [/t/candidateShared/P.csproj]"
        counts, _, _ = mod.parse_build_output(make_build_log(stage, [in_stage, sibling]), stage)
        self.assertEqual(len(counts), 2)
        self.assertEqual(sum(counts.values()), 2)

    def test_out_of_stage_canary_path_does_not_satisfy_canary_check(self):
        mod = load_module()
        stage = Path("/tmp/stage-canary-bounds")
        root = stage.as_posix().rstrip("/")
        for bad_prefix in ("..", "", "./..", "/tmp/stage-canary-bounds-other"):
            bad_trim = (
                f"{bad_prefix}/src/Chrysalis.Codec/__TrimAotCanary.g.cs(16,9): error IL2026: "
                "Using member 'Chrysalis.Codec.__TrimAotCanary.TriggerTrim()' which has 'RequiresUnreferencedCodeAttribute' "
                "can break functionality when trimming application code. X11_TRIM_CANARY. "
                f"[{root}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
            )
            good_aot = (
                f"{root}/src/Chrysalis.Codec/__TrimAotCanary.g.cs(17,9): error IL3050: "
                "Using member 'Chrysalis.Codec.__TrimAotCanary.TriggerAot()' which has 'RequiresDynamicCodeAttribute' "
                "can require dynamic code generation at runtime. X11_AOT_CANARY. "
                f"[{root}/src/Chrysalis.Codec/Chrysalis.Codec.csproj]"
            )
            log = "\n".join([bad_trim, good_aot, "", "Build FAILED."]) + "\n"
            with self.assertRaisesRegex(RuntimeError, "Missing required trim/AOT execution canary"):
                mod.parse_build_output(log, stage)


if __name__ == "__main__":
    unittest.main()
