"""Checks on tracked and unignored files, including a dirty worktree."""

import csv
import hashlib
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
DATA = Path("src/Chrysalis.Codec.Blueprint.Test/data")
# LC1w deletes the Wizard fixture and this exception together.
UNMANIFESTED_BLUEPRINTS = {DATA / "plutus.json"}
MAGIC = (
    ("ELF", 0, b"\x7fELF"),
    ("Mach-O", 0, b"\xfe\xed\xfa\xce"),
    ("Mach-O", 0, b"\xce\xfa\xed\xfe"),
    ("Mach-O", 0, b"\xfe\xed\xfa\xcf"),
    ("Mach-O", 0, b"\xcf\xfa\xed\xfe"),
    ("Mach-O", 0, b"\xca\xfe\xba\xbe"),
    ("Mach-O", 0, b"\xbe\xba\xfe\xca"),
    ("Mach-O", 0, b"\xca\xfe\xba\xbf"),
    ("Mach-O", 0, b"\xbf\xba\xfe\xca"),
    ("WebAssembly", 0, b"\x00asm"),
)
PE_OFFSET = 0x3C
PREFIX_SIZE = max(PE_OFFSET + 4, *(offset + len(magic) for _, offset, magic in MAGIC))


def candidate_files(root):
    names = subprocess.check_output(
        ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=root
    )
    for name in filter(None, names.split(b"\0")):
        rel = Path(os.fsdecode(name))
        path = root / rel
        if not path.is_symlink() and path.is_file():
            yield rel


def binary_kind(path):
    with path.open("rb") as stream:
        prefix = stream.read(PREFIX_SIZE)
        for kind, offset, magic in MAGIC:
            if prefix.startswith(magic, offset):
                return kind
        if len(prefix) >= PE_OFFSET + 4 and prefix.startswith(b"MZ"):
            offset = int.from_bytes(prefix[PE_OFFSET : PE_OFFSET + 4], "little")
            if offset >= PE_OFFSET + 4:
                stream.seek(offset)
                if stream.read(4) == b"PE\0\0":
                    return "PE"
    return None


def binaries(root):
    return {path.as_posix(): kind for path in candidate_files(root)
            if (kind := binary_kind(root / path)) is not None}


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


class RepositoryFilesTests(unittest.TestCase):
    def temp_repo(self, files):
        root = Path(self.enterContext(tempfile.TemporaryDirectory()))
        subprocess.run(["git", "init", "-q"], cwd=root, check=True)
        for name, contents in files.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(contents)
        return root

    def test_no_committed_binaries(self):
        found = binaries(ROOT)
        if found:
            self.fail("\n".join(f"{path}: {found[path]}" for path in sorted(found)))

    def test_scan_sees_force_added_ignored_binaries(self):
        pe = b"MZ" + bytes(PE_OFFSET - 2) + (0x80).to_bytes(4, "little") + bytes(0x40)
        root = self.temp_repo({
            ".gitignore": b"out/\n",
            "out/added": b"\x7fELF",
            "out/local": b"\x7fELF",
            "mac": b"\xcf\xfa\xed\xfe",
            "windows": pe + b"PE\0\0",
            "wasm": b"\0asm",
            "readme": b"MZ (not a PE header)",
        })
        subprocess.run(["git", "add", "-f", "--", "out/added"], cwd=root, check=True)
        self.assertEqual(binaries(root), {
            "out/added": "ELF", "mac": "Mach-O", "windows": "PE", "wasm": "WebAssembly",
        })

    def test_blueprint_fixtures_match_their_manifests(self):
        data_files = {path for path in candidate_files(ROOT) if path.parent == DATA}
        fixtures = {path for path in data_files if path.suffix == ".json"}
        manifests = sorted(path for path in data_files if path.match("*manifest.tsv"))
        listed = set()
        for manifest in manifests:
            with (ROOT / manifest).open(encoding="utf-8", newline="") as stream:
                rows = csv.reader(stream, delimiter="\t")
                self.assertEqual(next(rows, None), [
                    "file", "repository", "tag", "commit", "path at that commit",
                    "licence", "sha256", "derived from",
                ], str(manifest))
                self.assertTrue((entries := list(rows)), f"{manifest}: empty manifest")
            for row in entries:
                self.assertEqual(len(row), 8, f"{manifest}: malformed row")
                path = DATA / row[0]
                self.assertIn(path, fixtures, f"{manifest}: not a blueprint fixture: {path}")
                self.assertNotIn(path, listed, f"{path}: duplicate manifest row")
                listed.add(path)
                self.assertEqual(sha256(ROOT / path), row[6], f"{path}: SHA-256 mismatch")
        self.assertLessEqual(UNMANIFESTED_BLUEPRINTS, fixtures, "stale Wizard exception")
        missing = fixtures - listed - UNMANIFESTED_BLUEPRINTS
        if missing:
            self.fail("\n".join(f"{path}: missing manifest row" for path in sorted(missing)))
        licence = DATA / "LICENSE-aiken"
        self.assertIn(licence, data_files)
        self.assertEqual(sha256(ROOT / licence),
                         "5dcae70ec2e7d4166d33f0546e8e6327e41e8dddf924de16db554085973e1a26")
