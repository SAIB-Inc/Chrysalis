"""Regression tests for the offline capture infrastructure; no cardano-cli needed."""
import json
import os
from pathlib import Path
import subprocess
import sys

import pytest

import capture
from cases import Fixtures, G1, Shape, native_length

RECIPE = 'SINCE = "T1"\nCASE_IDS = ["OLD"]\nPAYMENT = 2_000_000\n'


def record(path, data, role, since):
    return dict(path=path, role=role, source="synthetic test", licence="MIT",
                bytes=len(data), sha256=capture.sha(data), since=since)


@pytest.fixture
def corpus(tmp_path):
    into = tmp_path / "corpus"
    into.mkdir()
    old_case = dict(id="OLD", since="T1", rows=[{"tx": "old.tx"}])
    paths = {"cases.py": (RECIPE.encode(), "control"), "old.tx": (b"old payload", "tx")}
    for path, (data, _) in paths.items():
        (into / path).write_bytes(data)
    manifest = capture.manifest_text({"tool": {"sourceCommit": capture.SOURCE}},
                                    [capture.manifest_row(record(k, *v, "T1")) for k, v in paths.items()],
                                    [capture.manifest_row(old_case)])
    (into / "manifest.json").write_text(manifest, encoding="utf-8")
    return into, manifest


@pytest.mark.parametrize("path", ["cases.py", "old.tx"])
def test_append_rejects_rehashed_existing_files(corpus, tmp_path, path):
    into, manifest = corpus
    changed = (RECIPE.replace("2_000_000", "3_000_000").encode() if path == "cases.py" else b"replacement payload")
    draft = capture.Capture(tmp_path / "draft", into, {})
    for name in ("cases.py", "old.tx"):
        draft.write(name, (into / name).read_bytes())
    (into / path).write_bytes(changed)
    draft.files[path] = record(path, changed, "control" if path == "cases.py" else "tx", "T1")
    draft.case("NEW", "New case", [{"tx": "new.tx"}], since="T2")
    with pytest.raises(ValueError, match=f"existing payload changed: {path}"):
        capture.extend_manifest(manifest, draft)


def test_append_loads_new_module_without_rewriting_old_rows(corpus, tmp_path, monkeypatch):
    into, before = corpus
    module = into / "cases_T2.py"
    module.write_text('''SINCE = "T2"
CASE_IDS = ["NEW"]

def capture(ctx, selected):
    assert selected == {"NEW"}
    # Existing payloads may be used as input but must not be emitted again.
    assert ctx.read("old.tx") == b"old payload"
    ctx.input(ctx.write("new.tx", b"new payload"), "tx", since=SINCE)
    ctx.case("NEW", "New case", [{"tx": "new.tx"}], since=SINCE)
''', encoding="utf-8")
    output = tmp_path / "out"
    monkeypatch.setattr(sys, "argv", ["capture.py", "--tools", str(tmp_path), "--out", str(output),
                                      "--into", str(into), "--only", "NEW"])
    monkeypatch.setattr(capture, "preflight", lambda *_: {"sourceCommit": capture.SOURCE})
    capture.main()
    after = (into / "manifest.json").read_text(encoding="utf-8")
    for line in before.splitlines():
        if line.startswith("    {"):
            assert line in after.splitlines()  # Including original indentation and comma.
    for section, key in (("files", "path"), ("cases", "id")):
        original = capture.old_lines(before, section, key)
        added = capture.old_lines(after, section, key)
        assert {name: added[name] for name in original} == original
    files = {row["path"]: row for row in json.loads(after)["files"]}
    assert files["cases_T2.py"]["sha256"] == capture.sha(module.read_bytes())
    assert files["cases_T2.py"]["since"] == files["new.tx"]["since"] == "T2"
    assert (into / "cases.py").read_text(encoding="utf-8") == RECIPE
    assert (into / "old.tx").read_bytes() == b"old payload"
    assert (into / "new.tx").read_bytes() == b"new payload"
    assert not (output / "old.tx").exists()


def test_case_module_import_does_not_create_bytecode(tmp_path):
    recipe = tmp_path / "recipe"
    recipe.mkdir()
    script = recipe / "capture.py"
    script.write_bytes(Path(capture.__file__).read_bytes())
    (recipe / "cases.py").write_text('''from capture import SOURCE
print("recipe read")
SINCE = "T1"
CASE_IDS = ["OLD"]
''', encoding="utf-8")
    # The recipe must load before the absent tool fails; deliberately do not pass -B.
    env = {k: v for k, v in os.environ.items() if k not in {"PYTHONDONTWRITEBYTECODE", "PYTHONPYCACHEPREFIX"}}
    result = subprocess.run([sys.executable, script, "--tools", tmp_path / "absent", "--out", tmp_path / "out"],
                            env=env, capture_output=True, text=True, timeout=20, check=False)
    assert result.returncode != 0 and "recipe read" in result.stdout
    assert "FileNotFoundError" in result.stderr
    assert not (recipe / "__pycache__").exists()


def test_native_script_recipe_returns_only_cli_description():
    assert native_length(9) == {"type": "all", "scripts": [
        {"type": "after", "slot": 0}, {"type": "after", "slot": 0},
    ]}


def test_reused_fixture_helper_uses_calling_modules_since(tmp_path):
    ctx = capture.Capture(tmp_path, tmp_path, {})
    ctx.write(G1, {})
    fixture = Fixtures(ctx, since="T2")
    path = fixture.fund("NEW", Shape({}))
    assert ctx.files[path]["since"] == "T2"


def test_append_rejects_a_file_with_the_wrong_since(corpus, tmp_path, monkeypatch):
    into, before = corpus
    (into / "cases_T2.py").write_text('''SINCE = "T2"
CASE_IDS = ["NEW"]

def capture(ctx, selected):
    ctx.input(ctx.write("new.tx", b"new payload"), "tx", since="T1")
    ctx.case("NEW", "New case", [{"tx": "new.tx"}], since=SINCE)
''', encoding="utf-8")
    output = tmp_path / "out"
    monkeypatch.setattr(sys, "argv", ["capture.py", "--tools", str(tmp_path), "--out", str(output),
                                      "--into", str(into), "--only", "NEW"])
    monkeypatch.setattr(capture, "preflight", lambda *_: {"sourceCommit": capture.SOURCE})
    with pytest.raises(ValueError, match=r"cases_T2\.py: new\.tx has since=T1, expected T2"):
        capture.main()
    assert (into / "manifest.json").read_text(encoding="utf-8") == before
    assert not output.exists()
