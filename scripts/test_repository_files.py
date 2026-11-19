"""Checks on tracked and unignored files, including a dirty worktree."""

import csv
import hashlib
from itertools import groupby
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
DATA = Path("src/Chrysalis.Codec.Blueprint.Test/data")
MANIFEST_HEADER = [
    "file", "repository", "tag", "commit", "path at that commit",
    "licence", "sha256", "derived from",
]
RECORDING_MANIFEST_HEADER = [
    "file", "method", "path", "status", "content-type", "sha256", "origin",
]
UTF8_BOM = b"\xef\xbb\xbf"
HEADER_PREFIX = "// Contains code adapted from "
HEADER_SUFFIX = "; changed for Chrysalis. See THIRD-PARTY-NOTICES.md."
GITHUB_REPO_RE = re.compile(r"https://github\.com/[^/\s]+/([A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*)")
ORIGINS = (
    ("blaze", "Blaze (Apache-2.0)"),
    ("plutuz", "plutuz (MIT), Copyright (c) 2026 UTxO Company"),
    ("aiken", "Aiken (Apache-2.0)"),
    ("noble-curves", "noble-curves (MIT), Copyright (c) 2022 Paul Miller (https://paulmillr.com)"),
    ("Chaos.NaCl", "Chaos.NaCl (public domain)"),
    ("cardanosharp-wallet", "CardanoSharp.Wallet (MIT), Copyright (c) 2021 CardanoSharp"),
    ("bips", "the BIP-39 English word list (MIT)"),
    ("cardano-multiplatform-lib", "cardano-multiplatform-lib (MIT), Copyright (c) 2022 dcSpark"),
    ("plutus", "Plutus (Apache-2.0)"),
)
ORIGIN_MAP = dict(ORIGINS)
ORIGIN_ORDER = {key: index for index, (key, _) in enumerate(ORIGINS)}
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
REMOVED_NAME = bytes.fromhex("77697a617264").decode()
REMOVED_HASHES = {
    "83305427f2ab1356bd47b12a8886aedb3a8cd7adf844804e7ca6bbcbaa9fe856",
    "634de5f1b161d7e2092a7bac5c4378c160ac89e692075a73378487ec752fbd12",
    "413dc03cc9587546d5631b4f5a19b237fd882f2031c216420c1957fa696a1330",
}
REMOVED_PROGRAMS = {
    "df7dcb95733e08abaf5dc4b48e1edd6025dd530c60f83ae84bb771124c8ad73a",
    "74654c8c1f5d283b9023c45408b8020b59cd86f4fcef257c30e1fb109b3ec494",
    "accd1e0d6bade108f07db0f1e85fe7b9daafc879aba4774930c7c410b2ca40ab",
    "e5bbcc83b826841f778156a46a5f165aac36c4d0d7c68c6e193271aa42f41853",
}
WORD_LIST = Path("src/Chrysalis.Wallet/Words/English.cs")
WALLET_TEST = Path("src/Chrysalis.Wallet.Test")
PHRASE_LENGTHS = (12, 15, 18, 21, 24)
ALLOWED_PHRASES = {
    # digest: (path, count, label, whether the label is in the adjacent manifest)
    "c557eec878dfd852ba3f88087c4f350f09c55537ab5e549c3cd14320ec3cef38": (
        WALLET_TEST / "TestVectors.cs", 1, "BIP-39 test vector", False),
    "533f2e1742956eeed3591f2a1c89c8b5fe77f984919e4131313802db75e26983": (
        WALLET_TEST / "CIPs/CIP8Tests.cs", 1, "CardanoSharp/cardanosharp-wallet `5089079d`", False),
    "16b99389dd0544e520f7fa7ff558d6d5326ada33a01f8ea20a4b0f31fb7ddadc": (
        WALLET_TEST / "TestData/CIPs/icarus.json", 2, "CIP-0003/Icarus.md", True),
}


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


def read_text_or_bytes(path):
    data = path.read_bytes()
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        return data.decode("utf-16", errors="replace")
    if b"\0" in data[:8192]:
        return data
    return data.decode("utf-8-sig", errors="replace")


def unwrap_byte_strings(data):
    # Strip only definite-length CBOR byte strings that span the entire input.
    while data and data[0] >> 5 == 2:
        size = data[0] & 31
        header = 1
        if size >= 24:
            if size > 27:
                break
            header += 1 << (size - 24)
            size = int.from_bytes(data[1:header], "big")
        if header + size != len(data):
            break
        data = data[header:]
    return data


def removed_material(root, name, hashes, programs):
    for path in sorted(candidate_files(root)):
        if name in path.as_posix().lower():
            yield f"{path}: name in path"
        data = read_text_or_bytes(root / path)
        if isinstance(data, bytes):
            if hashlib.sha256(unwrap_byte_strings(data)).hexdigest() in programs:
                yield f"{path}: script payload"
            continue
        for number, line in enumerate(data.splitlines(), 1):
            line = line.lower()
            if name in line:
                yield f"{path}:{number}: name"
            for token in re.findall(r"[0-9a-f]{56,}", line):
                if len(token) == 56 and hashlib.sha256(token.encode()).hexdigest() in hashes:
                    yield f"{path}:{number}: script hash"
                if len(token) >= 112 and len(token) % 2 == 0:
                    raw = unwrap_byte_strings(bytes.fromhex(token))
                    if hashlib.sha256(raw).hexdigest() in programs:
                        yield f"{path}:{number}: script payload"


def valid_checksum(words, indices):
    bits = 0
    for word in words:
        bits = (bits << 11) | indices[word]
    size = len(words) // 3
    entropy = (bits >> size).to_bytes(size * 4)
    return bits & ((1 << size) - 1) == hashlib.sha256(entropy).digest()[0] >> (8 - size)


def phrase_windows(words):
    for i in range(len(words) - PHRASE_LENGTHS[0] + 1):
        for size in PHRASE_LENGTHS:
            if i + size > len(words):
                break
            yield i, words[i:i + size]


def contains_phrase(words, indices):
    return any(valid_checksum(window, indices) for _, window in phrase_windows(words))


def manifest_has_label(path, label):
    try:
        entries = json.loads(path.with_name("manifest.json").read_text(encoding="utf-8-sig"))
        return any(entry.get("path") == path.name and label in entry.get("source", "")
                   for entry in entries)
    except (OSError, ValueError, AttributeError, TypeError):
        return False


def recovery_phrases(root, indices, allowed):
    counts = dict.fromkeys(allowed, 0)
    minimum = PHRASE_LENGTHS[0]
    for path in sorted(candidate_files(root)):
        if path == WORD_LIST:
            continue
        data = read_text_or_bytes(root / path)
        if isinstance(data, bytes):
            continue
        lines = data.splitlines()
        tokens = ((word, number) for number, line in enumerate(lines, 1)
                  for word in re.findall(r"[a-z]+", line.lower()))
        rules = {digest: rule for digest, rule in allowed.items() if rule[0] == path}
        for known, run in groupby(tokens, key=lambda token: token[0] in indices):
            if not known:
                continue
            entries = list(run)
            if len(entries) < minimum:
                continue
            words, numbers = zip(*entries)
            start = 0
            # Subtract allowed occurrences before checking the remaining parts for valid windows.
            if rules:
                for i, window in phrase_windows(words):
                    if i < start:
                        continue
                    digest = hashlib.sha256(" ".join(window).encode()).hexdigest()
                    if digest not in rules:
                        continue
                    if contains_phrase(words[start:i], indices):
                        yield f"{path}:{numbers[start]}"
                    counts[digest] += 1
                    _, _, label, in_manifest = rules[digest]
                    number = numbers[i]
                    labelled = (manifest_has_label(root / path, label) if in_manifest else
                                any(label in line for line in lines[max(0, number - 4):number - 1]))
                    if not labelled:
                        yield f"{path}:{number}: allowed phrase without its label"
                    start = i + len(window)
            if contains_phrase(words[start:], indices):
                yield f"{path}:{numbers[start]}"
    for digest, (path, count, _, _) in allowed.items():
        if counts[digest] != count:
            yield f"{path}: allowed phrase count {counts[digest]}, expected {count}"



def origin_header(keys):
    parts = [ORIGIN_MAP[key] for key in sorted(set(keys), key=ORIGIN_ORDER.__getitem__)]
    if len(parts) == 1:
        joined = parts[0]
    elif len(parts) == 2:
        joined = (", and " if "," in parts[0] else " and ").join(parts)
    else:
        joined = ", ".join(parts[:-1]) + ", and " + parts[-1]
    return f"{HEADER_PREFIX}{joined}{HEADER_SUFFIX}"


def first_two_lines(path):
    with path.open("rb") as stream:
        raw1 = stream.readline()
        raw2 = stream.readline()
    if raw1.startswith(UTF8_BOM):
        raw1 = raw1[len(UTF8_BOM):]
    if raw1.endswith(b"\r\n"):
        line1_bytes, lf_ok = raw1[:-2], False
    elif raw1.endswith(b"\n"):
        line1_bytes, lf_ok = raw1[:-1], True
    else:
        line1_bytes, lf_ok = raw1, False
    line1 = line1_bytes.decode("utf-8", errors="replace")
    line2_bom = raw2.startswith(UTF8_BOM)
    line2 = raw2.rstrip(b"\r\n").decode("utf-8", errors="replace")
    return line1, lf_ok, line2_bom, line2


def manifest_origins(root):
    files = list(candidate_files(root))
    cs_files = {path for path in files if path.suffix == ".cs"}
    manifests = sorted(path for path in files if path.parts[:1] == ("src",) and path.match("*manifest.tsv"))
    origins = {}
    issues = set()
    for manifest in manifests:
        with (root / manifest).open(encoding="utf-8", newline="") as stream:
            rows = list(csv.reader(stream, delimiter="\t"))
        if rows and rows[0] and rows[0][0].startswith("#"):
            table = [row for row in rows if row and not row[0].startswith("#")]
            if (not table or table[0] != RECORDING_MANIFEST_HEADER or len(table) < 2
                    or any(len(row) != len(RECORDING_MANIFEST_HEADER) or row[0].endswith(".cs")
                           for row in table[1:])):
                issues.add(manifest.as_posix())
            continue
        if not rows or rows[0] != MANIFEST_HEADER or len(rows) < 2:
            issues.add(manifest.as_posix())
            continue
        for row in rows[1:]:
            if len(row) != len(MANIFEST_HEADER):
                issues.add(manifest.as_posix())
                continue
            cell = row[0]
            if "#" in cell or not cell.strip().lower().endswith(".cs"):
                continue
            if cell != cell.strip() or not cell.endswith(".cs"):
                issues.add(manifest.as_posix())
                continue
            rel_part = Path(row[0])
            if rel_part.is_absolute() or ".." in rel_part.parts:
                issues.add(manifest.as_posix())
                continue
            target = manifest.parent / rel_part
            repo_match = GITHUB_REPO_RE.fullmatch(row[1].rstrip("/"))
            keys = [repo_match.group(1)] if repo_match else [row[1]]
            keys.extend(GITHUB_REPO_RE.findall(row[7]))
            if target not in cs_files or any(key not in ORIGIN_MAP for key in keys):
                issues.add(target.as_posix())
                continue
            origins.setdefault(target, set()).update(keys)
    return origins, cs_files, issues


def ported_header_issues(root):
    origins, cs_files, issues = manifest_origins(root)
    for path, keys in origins.items():
        line1, lf_ok, line2_bom, line2 = first_two_lines(root / path)
        if not lf_ok or line1 != origin_header(keys) or line2_bom or line2.startswith(HEADER_PREFIX):
            issues.add(path.as_posix())
    for path in cs_files - origins.keys():
        line1, _, _, line2 = first_two_lines(root / path)
        if line1.lstrip("\ufeff").startswith(HEADER_PREFIX) or line2.lstrip("\ufeff").startswith(HEADER_PREFIX):
            issues.add(path.as_posix())
    return sorted(issues)

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
                self.assertEqual(next(rows, None), MANIFEST_HEADER, str(manifest))
                self.assertTrue((entries := list(rows)), f"{manifest}: empty manifest")
            for row in entries:
                self.assertEqual(len(row), 8, f"{manifest}: malformed row")
                path = DATA / row[0]
                self.assertIn(path, fixtures, f"{manifest}: not a blueprint fixture: {path}")
                self.assertNotIn(path, listed, f"{path}: duplicate manifest row")
                listed.add(path)
                self.assertEqual(sha256(ROOT / path), row[6], f"{path}: SHA-256 mismatch")
        missing = fixtures - listed
        if missing:
            self.fail("\n".join(f"{path}: missing manifest row" for path in sorted(missing)))
        licence = DATA / "LICENSE-aiken"
        self.assertIn(licence, data_files)
        self.assertEqual(sha256(ROOT / licence),
                         "5dcae70ec2e7d4166d33f0546e8e6327e41e8dddf924de16db554085973e1a26")

    def test_removed_material_stays_removed(self):
        found = list(removed_material(ROOT, REMOVED_NAME, REMOVED_HASHES, REMOVED_PROGRAMS))
        if found:
            self.fail("\n".join(found))

    def test_removed_material_check_sees_synthetic_material(self):
        token = os.urandom(28).hex().upper()
        raw = b"\0" + os.urandom(127)
        inner = b"\x58" + bytes([len(raw)]) + raw
        envelope = b"\x58" + bytes([len(inner)]) + inner
        root = self.temp_repo({
            "NeBuLa/raw": b"\0",
            "text": b"\nThis says NEBULA.\n",
            "utf16": "\nThis says Nebula.\n".encode("utf-16"),
            "token": f"\n{token}\n".encode(),
            "envelope": json.dumps({"cborHex": envelope.hex()}).encode(),
            "binary": raw,
            "clean": b"an unrelated file",
        })
        self.assertCountEqual(removed_material(root, "nebula", {
            hashlib.sha256(token.lower().encode()).hexdigest(),
        }, {hashlib.sha256(raw).hexdigest()}), [
            "NeBuLa/raw: name in path", "text:2: name", "utf16:2: name", "token:2: script hash",
            "envelope:1: script payload", "binary: script payload",
        ])

    def bip39_indices(self):
        words = re.findall(r'"([a-z]+)"', (ROOT / WORD_LIST).read_text(encoding="utf-8-sig"))
        indices = {word: index for index, word in enumerate(words)}
        self.assertTrue(len(words) == len(indices) == 2048, f"{WORD_LIST}: invalid word list")
        return indices

    def test_no_unlisted_recovery_phrases(self):
        found = list(recovery_phrases(ROOT, self.bip39_indices(), ALLOWED_PHRASES))
        if found:
            self.fail("\n".join(found))

    def test_phrase_check_sees_generated_phrases(self):
        indices = self.bip39_indices()
        words = list(indices)

        def phrase(size, damage=0):
            entropy = os.urandom(size * 4)
            checksum = hashlib.sha256(entropy).digest()[0] >> (8 - size)
            bits = (int.from_bytes(entropy, "big") << size) | (checksum ^ damage)
            return [words[(bits >> shift) & 2047] for shift in range(33 * size - 11, -1, -11)]

        p1, p2, p3, p4 = (phrase(size) for size in (4, 8, 4, 4))
        p1, p3, p4 = (" ".join(parts) for parts in (p1, p3, p4))
        root = self.temp_repo({
            "p1": f"\n{p1}\n".encode(), "utf16": p1.encode("utf-16"),
            "p2": (" ".join(p2[:12]) + "\n" + " ".join(p2[12:])).encode(),
            "invalid": " ".join(phrase(4, damage=1)).encode(),
            "a": f"# VECTOR\n{p3}\n# VECTOR\n{p3}\n".encode(),
            "b": p4.encode(), "c": p3.encode(),
        })
        allowed = {hashlib.sha256(value.encode()).hexdigest(): (Path(path), 1, "VECTOR", False)
                   for value, path in ((p3, "a"), (p4, "b"))}
        self.assertCountEqual(recovery_phrases(root, indices, allowed), [
            "p1:2", "utf16:1", "p2:1", "a: allowed phrase count 2, expected 1",
            "b:1: allowed phrase without its label", "c:1",
        ])

    def test_ported_files_carry_their_header(self):
        found = ported_header_issues(ROOT)
        if found:
            self.fail("\n".join(found))

    def test_header_check_catches_missing_wrong_extra_and_bad_manifests(self):
        hdr = "\t".join(MANIFEST_HEADER)
        def row(name, repo, derived=""):
            return f"{name}\t{repo}\t-\t0\tp\tMIT\t0\t{derived}"
        blaze_hdr = origin_header(["blaze"])
        root = self.temp_repo({
            "src/P/a-manifest.tsv": (
                f"{hdr}\n"
                f"{row('Ok.cs', 'https://github.com/o/blaze')}\n"
                f"{row('Multi.cs', 'https://github.com/o/blaze', 'https://github.com/o/plutuz/blob/0/a')}\n"
                f"{row('Bom.cs', 'https://github.com/o/cardanosharp-wallet', 'https://github.com/o/bips/blob/0/w')}\n"
                f"{row('BomAfter.cs', 'https://github.com/o/blaze')}\n"
                f"{row('Crlf.cs', 'https://github.com/o/blaze')}\n"
                f"{row('Dup.cs', 'https://github.com/o/blaze')}\n"
                f"{row('Missing.cs', 'https://github.com/o/plutus')}\n"
                f"{row('BadKey.cs', 'https://github.com/o/unknown')}\n"
            ).encode(),
            "src/P/b-manifest.tsv": f"{hdr}\n{row('Multi.cs', 'https://github.com/o/aiken')}\n".encode(),
            "src/P/empty-manifest.tsv": f"{hdr}\n".encode(),
            "src/P/ws-manifest.tsv": f"{hdr}\n{row('Ws.cs ', 'https://github.com/o/blaze')}\n".encode(),
            "src/P/upper-manifest.tsv": f"{hdr}\n{row('Upper.CS', 'https://github.com/o/blaze')}\n".encode(),
            "src/P/rec-manifest.tsv": (
                "# utc: 0\n" + "\t".join(RECORDING_MANIFEST_HEADER) + "\n1.json\tGET\tp\t200\tj\t0\tr\n"
            ).encode(),
            "src/P/Ok.cs": f"{blaze_hdr}\nclass A {{}}\n".encode(),
            "src/P/Multi.cs": f"{origin_header(['blaze', 'plutuz', 'aiken'])}\nclass B {{}}\n".encode(),
            "src/P/Bom.cs": b"\xef\xbb\xbf" + f"{origin_header(['cardanosharp-wallet', 'bips'])}\nclass C {{}}\n".encode(),
            "src/P/BomAfter.cs": f"{blaze_hdr}\n".encode() + b"\xef\xbb\xbfclass G {}\n",
            "src/P/Crlf.cs": f"{blaze_hdr}\r\nclass H {{}}\n".encode(),
            "src/P/Dup.cs": f"{blaze_hdr}\n{blaze_hdr}\nclass I {{}}\n".encode(),
            "src/P/Ws.cs": b"class J {}\n",
            "src/P/Upper.CS": b"class K {}\n",
            "src/P/Missing.cs": b"class D {}\n",
            "src/P/BadKey.cs": b"class E {}\n",
            "src/P/Extra.cs": f"{HEADER_PREFIX}Blaze (Apache-2.0){HEADER_SUFFIX}\nclass F {{}}\n".encode(),
        })
        self.assertEqual(ported_header_issues(root), [
            "src/P/BadKey.cs", "src/P/BomAfter.cs", "src/P/Crlf.cs", "src/P/Dup.cs",
            "src/P/Extra.cs", "src/P/Missing.cs", "src/P/empty-manifest.tsv",
            "src/P/upper-manifest.tsv", "src/P/ws-manifest.tsv",
        ])
