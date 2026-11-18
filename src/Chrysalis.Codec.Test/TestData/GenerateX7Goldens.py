"""Verify the X7 wire/CLI manifest, or replay its captures with the two pinned CLI ELFs.

Run replay only with network disabled, e.g. unshare -Urn -- python3 GenerateX7Goldens.py
--cli /path/to/11.7.0.0 --babbage-cli /path/to/10.13.1.0. No node is needed.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, failure):
    if not condition:
        raise SystemExit(failure)


def run(executable, args, directory):
    return subprocess.run([str(executable), *args], cwd=directory, check=False,
                          capture_output=True, text=True, timeout=30,
                          env={"LANG": "C", "LC_ALL": "C", "HOME": str(directory)})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", type=Path)
    parser.add_argument("--babbage-cli", type=Path)
    parser.add_argument("--pallas", type=Path, help="Pallas probe built as a bin target of the pinned package")
    options = parser.parse_args()
    here = Path(__file__).resolve().parent
    record = json.loads((here / "X7CliGoldens.json").read_text())
    require(bool(options.cli) == bool(options.babbage_cli), "Both pinned CLI paths are required for oracle replay")
    paths = {}
    with tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR")) as temp:
        directory = Path(temp)
        for name, executable, pin in (("11.7.0.0", options.cli, record),
                                       ("10.13.1.0", options.babbage_cli, record["fallbackCli"])):
            if executable is not None:
                exe = executable.resolve()
                require(sha(exe.read_bytes()) == pin["elfSha256"], f"CLI {name} ELF hash mismatch")
                version = run(exe, ["--version"], directory)
                require(version.returncode == 0 and version.stdout.strip() == pin["versionOutput"], f"CLI {name} pin mismatch")
                paths[name] = exe

        for row in record["rows"]:
            name = row["id"]
            wire = bytes.fromhex(row["cborHex"])
            require(sha(wire) == row["sha256"], f"{name}: wire checksum mismatch")
            if not paths:
                continue
            args = row["command"][1:]
            envelope = {"type": f"Tx {row['era']}Era", "description": "", "cborHex": wire.hex()}
            (directory / args[-1]).write_text(json.dumps(envelope) + "\n")
            actual = run(paths[row["cli"]], args, directory)
            require(actual.returncode == row["exitCode"], f"{name}: oracle disposition changed")
            if actual.returncode == 0:
                require(json.loads(actual.stdout) == row["stdout"], f"{name}: oracle value changed")
            else:
                require(actual.stderr.strip() == row["stderr"], f"{name}: oracle diagnostic changed")
    pallas = json.loads((here / "X7PallasGoldens.json").read_text())
    block = here / pallas["metadata"]["source"]
    data = {"metadata": bytes.fromhex(block.read_text()), "datum": bytes.fromhex(pallas["datum"]["cborHex"])}
    for kind, wire in data.items():
        require(sha(wire) == pallas[kind]["sha256"], f"{kind}: Pallas input checksum mismatch")
    if options.pallas is not None:
        result = run(options.pallas.resolve(), [str(block)], here)
        expected = ["\t".join([kind, key, value]) for kind in data for key, value in pallas[kind]["pairs"]]
        require(result.returncode == 0 and result.stdout.splitlines() == expected, "Pallas projection changed")
    print(f"Verified {len(record['rows'])} pinned CLI wire rows and {len(data)} Pallas input rows")


if __name__ == "__main__":
    main()
