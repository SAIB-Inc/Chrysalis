#!/usr/bin/env python3
"""Offline capture with the pinned aarch64-darwin cardano-cli (stdlib only).

A folder supplies cases.py and may add cases_<MR>.py modules, each with SINCE,
CASE_IDS and capture(ctx, selected). Use ctx.run
(for approved offline commands), ctx.write / ctx.input (for outputs and their
provenance), and ctx.case (to record a case with its commands and rows). Pass
since explicitly for each row. A later module never edits an earlier one. Scratch
files and throwaway keys belong in tmp/. Only --only may append to a manifest;
--into selects that manifest and --out must always be fresh.
"""
import argparse
import datetime as dt
import getpass
import hashlib
import json
import mmap
from pathlib import Path, PurePosixPath
import runpy
import shutil
import socket
import subprocess
import sys
import tempfile
from dataclasses import dataclass

SOURCE = "24c05829e854f9b2f56dcb9422bff5ff3ff60f77"
REPO = "https://github.com/IntersectMBO/cardano-cli"
TAG = "cardano-cli-11.7.0.0"
PLATFORM = "aarch64-darwin"
ARCHIVE = f"{TAG}-{PLATFORM}.tar.gz"
BINARY = f"cardano-cli-{PLATFORM}"
ARCHIVES = {PLATFORM: "8dfe304a13b59721a4045e88059b303f92849ee69bd879bae3556a212dae2203",
            "x86_64-linux": "08c8dc4efec91bbd0802906d6e8da7ce90394c1dc6f303bc2889702a3825f12b"}
BINARY_SHA = "ae5f44c134b7062319634706c776738f06b3d7ff28ff705fa2e3835c74e43867"
VERSION = "cardano-cli 11.7.0.0 - darwin-aarch64 - ghc-9.12\ngit rev " + "0" * 40
LIBRARIES = {"plutus-core": "1.71.0.0", "plutus-ledger-api": "1.71.0.0", "cardano-api": "11.12.0.0",
             "cardano-ledger-conway": "1.24.0.1", "cardano-ledger-core": "1.23.0.0"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def file_sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def compact(value):
    return json.dumps(value, separators=(",", ":"), ensure_ascii=False)


def safe(path):
    p = PurePosixPath(path)
    require(not p.is_absolute() and p.parts and all(x not in ("..", ".git") for x in p.parts)
            and p.as_posix() == path and "\\" not in path, f"unsafe relative path: {path}")
    return p


def preflight(tools, target, home):
    require(file_sha(tools / ARCHIVE) == ARCHIVES[PLATFORM], "wrong archive sha256")
    require(file_sha(tools / BINARY) == BINARY_SHA, "wrong binary sha256")
    shutil.copyfile(tools / BINARY, target)
    target.chmod(0o700)
    require(file_sha(target) == BINARY_SHA, "wrong staged binary sha256")
    with target.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        for name, version in LIBRARIES.items():
            pin = f"{name}-{version}".encode("ascii")
            require(binary.find(pin) >= 0, f"missing library identifier {name}-{version}")
    version = subprocess.check_output([target, "--version"], env=home, text=True).strip()
    require(version == VERSION, "wrong CLI version output")
    return {"name": "cardano-cli", "version": "11.7.0.0", "versionOutput": version, "platform": PLATFORM,
            "archiveUrl": f"{REPO}/releases/download/{TAG}/{ARCHIVE}", "archiveSha256": ARCHIVES[PLATFORM],
            "publishedArchiveSha256": ARCHIVES, "binarySha256": BINARY_SHA, "sourceCommit": SOURCE,
            "licence": "Apache-2.0", "libraries": LIBRARIES}


class Capture:
    def __init__(self, root, into, env):
        self.root, self.into, self.env = root, into, env
        self.files, self.cases, self.commands, self.linked = {}, {}, [], {}

    def run(self, *argv):
        args = ["conway", *map(str, argv)]
        # Do not permit an online transaction build, submission or query even in another folder's table.
        offline = {("transaction", cmd) for cmd in ("build-raw", "calculate-min-fee", "calculate-min-required-utxo",
                   "calculate-plutus-script-cost", "sign", "policyid", "hash-script-data", "txid")}
        offline.update({("address", "key-gen"), ("address", "build"), ("address", "key-hash"), ("stake-address", "key-gen"),
                        ("stake-address", "build"), ("stake-address", "registration-and-delegation-certificate"),
                        ("node", "key-gen"), ("node", "key-gen-VRF"), ("stake-pool", "registration-certificate")})
        require(tuple(argv[:2]) in offline or tuple(argv[:3]) in {("governance", "drep", "key-gen"),
                ("governance", "drep", "registration-certificate"), ("governance", "action", "create-info")},
                f"command not on offline allowlist: {argv[:2]}")
        require(argv[:2] != ("transaction", "calculate-plutus-script-cost") or argv[2] == "offline",
                "evaluation must be offline")
        require(not {"--check-anchor-data", "--check-drep-metadata-hash", "--check-metadata-hash", "--validate-relays"}
                .intersection(args), "network-backed checking not permitted")
        self.commands.append(args)
        result = subprocess.run(["./tmp/cardano-cli", *args], cwd=self.root, env=self.env, text=True,
                                capture_output=True, timeout=60, check=False)
        if result.returncode or result.stderr:
            raise ValueError(f"{args}: {result.stderr or result.stdout}")
        return result.stdout.strip()

    def read(self, path):
        return (self.root / safe(path)).read_bytes()

    def write(self, path, data):
        dest = self.root / safe(path)
        dest.parent.mkdir(exist_ok=True, parents=True)
        dest.write_bytes(data if isinstance(data, bytes) else (compact(data) + "\n").encode())
        return path

    def input(self, path, role, source="generated by capture.py", licence="MIT", *, since, data=None, **metadata):
        require(path not in self.files, f"duplicate file: {path}")
        data = self.read(path) if data is None else data
        self.files[path] = dict(path=path, role=role, source=source, licence=licence,
                                bytes=len(data), sha256=sha(data), since=since, **metadata)
        return path

    def link(self, path, original, role, **metadata):
        self.linked[path] = original
        self.write(path, original.read_bytes())
        return self.input(path, role, linked=True, **metadata)

    def case(self, id, about, rows, witness_reasons=(), *, since, build_case=None):
        require(id not in self.cases and rows, f"duplicate or empty case: {id}")
        self.cases[id] = dict(id=id, since=since, about=about, witnessReasons=witness_reasons,
                              commands=self.commands, rows=rows)
        if build_case is not None:
            self.cases[id]["buildCase"] = build_case
        self.commands = []
        print(f"{id}: {len(rows)} row(s)", flush=True)


@dataclass
class Term:
    raw: bytes
    major: int
    number: int | None
    children: tuple

    def key(self, number):
        require(self.major == 5, "expected a CBOR map")
        return next(v for k, v in zip(self.children[::2], self.children[1::2]) if k.number == number)


def cbor(raw):
    """Keep spans from CLI CBOR; do not normalize lengths, key order or tags."""
    pos = 0

    def item():
        nonlocal pos
        start = pos
        major, info = divmod(raw[pos], 32)
        pos += 1
        require(info not in (28, 29, 30), "reserved CBOR argument")
        n = info if info < 24 else None
        if 24 <= info <= 27:
            width = 1 << (info - 24)
            n = int.from_bytes(raw[pos:pos + width], "big")
            pos += width
        children = ()
        if major in (4, 5, 6) or (major in (2, 3) and n is None):
            if n is None:
                entries = []
                while raw[pos] != 255:
                    entries.append(item())
                pos += 1
                children = tuple(entries)
            else:
                children = tuple(item() for _ in range(1 if major == 6 else n * (2 if major == 5 else 1)))
        elif major in (2, 3):
            pos += n
        require(pos <= len(raw), "truncated CBOR")
        return Term(raw[start:pos], major, n, children)

    value = item()
    require(pos == len(raw), "trailing CBOR bytes")
    return value


def manifest_row(row):
    return "    " + compact(row)


def manifest_text(meta, files, cases):
    header = "".join(f"  {compact(k)}:{compact(v)},\n" for k, v in meta.items())
    return ("{\n" + header + '  "files": [\n' + ",\n".join(files)
            + '\n  ],\n  "cases": [\n' + ",\n".join(cases) + "\n  ]\n}\n")


def old_lines(text, name, key):
    marker = f'  "{name}": [\n'
    end = '\n  ],\n' if name == "files" else '\n  ]\n'
    block = text.split(marker, 1)[1].split(end, 1)[0]
    pairs = [(json.loads(line)[key], line) for line in block.split(",\n") if line]
    result = dict(pairs)
    require(len(result) == len(pairs), f"duplicate {key} in existing manifest")
    return result


def existing_payload(ctx, path, row):
    original = ctx.into / safe(path)
    if row.get("linked") and not original.is_file():
        # A linked fixture is present in build output but lives elsewhere in the repository.
        repo = next((parent for parent in ctx.into.parents if (parent / ".git").exists()), None)
        require(repo is not None, f"cannot resolve linked fixture: {path}")
        source = safe(row["source"]["path"])
        require(source.parts[0] == "src", f"linked fixture must be in repository src/: {path}")
        original = (repo / source).resolve()
        require(original.is_relative_to(repo / "src"), f"linked fixture escapes repository src/: {path}")
    data = original.read_bytes()
    require(len(data) == row["bytes"] and sha(data) == row["sha256"], f"existing payload changed: {path}")
    return data


def extend_manifest(text, ctx):
    old_files = old_lines(text, "files", "path")
    old_cases = old_lines(text, "cases", "id")
    original = json.loads(text)
    require(original["tool"]["sourceCommit"] == SOURCE, "existing manifest uses a different source pin")
    for path, line in old_files.items():
        data = existing_payload(ctx, path, json.loads(line))
        require(ctx.read(path) == data, f"capture changed staged payload: {path}")
    require(old_cases.keys().isdisjoint(ctx.cases), "changed existing case")
    require(old_files.keys().isdisjoint(ctx.files), "changed existing file row")

    def insert(old, new):
        # Add rows before the old final row so every old line retains its comma (or its absence).
        lines = list(old.values())
        additions = [manifest_row(row) for row in new.values()]
        return lines[:-1] + additions + lines[-1:] if lines else additions

    return manifest_text({k: v for k, v in original.items() if k not in {"files", "cases"}},
                         insert(old_files, ctx.files), insert(old_cases, ctx.cases))


def load_modules(into):
    sys.dont_write_bytecode = True
    modules = []
    known = set()
    for path in [into / "cases.py", *sorted(into.glob("cases_*.py"))]:
        if not path.is_file():
            continue
        table = runpy.run_path(str(path))
        ids = set(table["CASE_IDS"])
        require(ids and ids.isdisjoint(known), f"empty or duplicate case table: {path.name}")
        since = table["SINCE"]
        require(since and path.name in {"cases.py", f"cases_{since}.py"}, f"invalid case module name: {path.name}")
        known.update(ids)
        modules.append((path, since, ids, table.get("capture")))
    return known, modules


def privacy(root):
    denied = {str(Path.home()), getpass.getuser(), socket.gethostname(), "/" + "Users/", "/" + "home/", "Signing" + "Key"}
    for path in root.rglob("*"):
        if path.is_file():
            data = path.read_bytes().lower()
            for token in denied - {""}:
                require(token.lower().encode() not in data, f"private marker in {path.relative_to(root)}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tools", type=Path, required=True, help=f"directory holding {ARCHIVE} and {BINARY}")
    parser.add_argument("--out", type=Path, required=True, help="fresh output directory")
    parser.add_argument("--into", type=Path, default=Path(__file__).resolve().parent, help="case module and append destination")
    parser.add_argument("--only", nargs="+")
    args = parser.parse_args()
    into, out = args.into.resolve(), args.out.resolve()
    require(not out.exists() and not out.is_relative_to(into), "capture output must be fresh and outside the destination")
    manifest = into / "manifest.json"
    require(not manifest.exists() or args.only, "existing manifest can only be extended with --only")
    previous = manifest.read_bytes().decode("utf-8") if manifest.exists() else None
    old_paths = old_lines(previous, "files", "path") if previous else {}
    with tempfile.TemporaryDirectory(prefix="t1-capture-") as temporary:
        root = Path(temporary)
        tmp = root / "tmp"
        tmp.mkdir(mode=0o700)
        env = {"HOME": str(tmp), "TMPDIR": str(tmp), "PATH": "/usr/bin:/bin", "LC_ALL": "C"}
        ctx = Capture(root, into, env)
        for path, line in old_paths.items():
            ctx.write(path, existing_payload(ctx, path, json.loads(line)))
        known, modules = load_modules(into)
        selected = set(args.only or known)
        require(selected and selected <= known, "selected case IDs are unknown or empty")
        if previous:
            require(selected.isdisjoint(old_lines(previous, "cases", "id")), "--only refuses an existing case")
        tool = preflight(args.tools, tmp / "cardano-cli", env)
        source_dir = Path(__file__).resolve().parent
        if into == source_dir and not previous:
            owner = modules[0][1]
            for name in ("capture.py", "test_capture.py", "conftest.py"):
                path = source_dir / name
                if path.is_file():
                    ctx.input(ctx.write(name, path.read_bytes()), "control", "Chrysalis repository source", since=owner)
        for path, since, ids, capture_selected in modules:
            chosen = selected & ids
            if chosen:
                files_before = set(ctx.files)
                if path.name not in old_paths:
                    ctx.input(ctx.write(path.name, path.read_bytes()), "control", "Chrysalis repository source", since=since)
                require(callable(capture_selected), f"missing capture function: {path.name}")
                capture_selected(ctx, chosen)
                require(all(id in ctx.cases and ctx.cases[id]["since"] == since for id in chosen),
                        f"case module did not emit its selected cases with since={since}: {path.name}")
                for name, row in ctx.files.items():
                    if name not in files_before:
                        require(row["since"] == since, f"{path.name}: {name} has since={row['since']}, expected {since}")
        require(set(ctx.cases) == selected, "case table emitted missing or extra cases")
        shutil.rmtree(tmp)  # All generated signing keys, including the throwaway Byron key, stay here.
        if previous:
            text = extend_manifest(previous, ctx)
        else:
            meta = dict(tool=tool, offline=True, node=None, networkMagic=None, tipSlot=None,
                        capturedUtc=dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"))
            text = manifest_text(meta, map(manifest_row, ctx.files.values()), map(manifest_row, ctx.cases.values()))
        ctx.write("manifest.json", text.encode())
        privacy(root)
        out.mkdir(parents=True)
        for path in [*ctx.files, "manifest.json"]:
            dest = out / safe(path)
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(ctx.read(path))
        if args.only:
            for path in ctx.files.keys() - ctx.linked.keys():
                dest = into / safe(path)
                if dest.is_file() and ctx.files[path]["role"] == "control" and dest.read_bytes() == ctx.read(path):
                    continue
                require(not dest.exists(), f"refusing unmanifested existing file: {path}")
            for path in ctx.files.keys() - ctx.linked.keys():
                dest = into / safe(path)
                dest.parent.mkdir(parents=True, exist_ok=True)
                if not dest.exists():
                    with dest.open("xb") as stream:
                        stream.write(ctx.read(path))
            with tempfile.NamedTemporaryFile(dir=into, delete=False) as staged:
                staged.write(text.encode())
            Path(staged.name).replace(manifest)
        print(f"Captured {len(ctx.cases)} cases, {sum(len(c['rows']) for c in ctx.cases.values())} rows", flush=True)


if __name__ == "__main__":
    main()
