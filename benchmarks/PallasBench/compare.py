#!/usr/bin/env python3
"""Matched G8b comparisons. Pair 3: replay peer, not live-node throughput."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import xml.etree.ElementTree as ET
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from corpus_verify import decode
from gate_stats import paired_ratio


def item_count(item):
    # Tags/containers count, break markers do not. String chunks are one value.
    return 1 + (sum(item_count(child) for child in item.children)
                if item.major in (4, 5, 6) else 0)


def corpus_plan(root):
    manifest = json.loads((root / "sha256.json").read_text())
    checks, blocks = [], []
    entries = json.loads((root / "fixtures/manifest.json").read_text())
    for entry in entries:
        name = "fixtures/" + entry["path"]
        data = (root / name).read_bytes()
        actual = hashlib.sha256(data).hexdigest()
        if actual != manifest[name] or actual != entry["sha256"]:
            raise ValueError("Fixture hash mismatch: " + name)
        checks.append({key: entry[key] for key in ("slot", "hash", "transactions", "sha256")})
        blocks.append(data)
    fixed = json.loads((root / "range.json").read_text())
    if len(blocks) != 30 or checks[0]["slot"] != fixed["Anchor"]["Slot"]:
        raise ValueError("Expected 30 fixtures including the G8 anchor")
    for check, point in zip(checks[1:], fixed["Blocks"][:29]):
        if any(check[left] != point[right] for left, right in
               (("slot", "Slot"), ("hash", "Hash"), ("sha256", "Sha256"))):
            raise ValueError("Replay is not the first 29 pinned successors")
    return {"checks": checks, "blocks": blocks, "anchor": fixed["Anchor"],
            "items": sum(item_count(decode(data)) for data in blocks)}


def validate_pair(left, right, plan):
    if left["mode"] != right["mode"] or left["mode"] not in ("typed", "walk", "replay"):
        raise ValueError("Different/unknown work")
    replay = left["mode"] == "replay"
    for row in (left, right):
        if row["blocks"] != (29 if replay else 30):
            raise ValueError("Wrong block count")
        if row["iterations"] != left["iterations"] or row["iterations"] < 1:
            raise ValueError("Different iteration count")
        if row["checks"] != plan["checks"][1 if replay else 0:]:
            raise ValueError("Decoded point, transaction count or digest mismatch")
        if row["mode"] == "walk" and row["items"] != plan["items"]:
            raise ValueError("Different CBOR data-item count")
        if not math.isfinite(row["elapsed_ns"]) or row["elapsed_ns"] < 1_000_000_000:
            raise ValueError("Every timed window must be at least one second")

    compilation_during_window(left.get("jit"), left["elapsed_ns"])


# Information-only policy: at most 0.1% JIT time and 1,000 compiled IL bytes.
COMPILATION_TIME_DENOMINATOR = 1000
COMPILED_IL_LIMIT = 1000


def compilation_during_window(jit, elapsed_ns):
    required = {"compiledMethods", "ilBytes", "compilationTicks"}
    if not isinstance(jit, dict) or set(jit.get("start", {})) != required or set(jit.get("end", {})) != required:
        raise ValueError("Missing/incomplete JIT counters")
    delta = {key: jit["end"][key] - jit["start"][key] for key in required}
    if any(value < 0 for value in delta.values()):
        raise ValueError("JIT counters decreased during timing")
    # A TimeSpan tick is 100 ns. Cross-multiply so equality is accepted exactly.
    compilation_ns = delta["compilationTicks"] * 100
    if compilation_ns * COMPILATION_TIME_DENOMINATOR > elapsed_ns or delta["ilBytes"] > COMPILED_IL_LIMIT:
        raise ValueError("Compilation exceeds the information-only timing/IL limit")
    return dict(delta, percent=compilation_ns * 100 / elapsed_ns)


def process_timeout_seconds(timeouts):
    return sum(timeouts[name] for name in ("warmupCapSeconds", "measuredWindowSeconds", "marginSeconds"))


def run(command, timeout=30):
    # Bootstrap/version tools have a separate short bound; samples pass their derived budget.
    result = subprocess.run(command, text=True, capture_output=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"Comparison process failed ({result.returncode}): {result.stderr}")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pairs", type=int, default=5)
    parser.add_argument("--iterations", type=int, help="Override typed/walk defaults (6000/20000)")
    parser.add_argument("--replay-iterations", type=int, default=1000)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.pairs < 2 or (args.iterations is not None and args.iterations < 1) or args.replay_iterations < 1:
        parser.error("At least two pairs and positive iterations required")
    here = Path(__file__).resolve().parent
    root = here.parent / "Chrysalis.Benchmarks"
    plan = corpus_plan(root)
    args.out.mkdir(parents=True, exist_ok=False)
    commands = {
        "chrysalis": ["dotnet", str(here / "chrysalis/bin/Release/net11.0/Comparison.dll")],
        "rust": [str(here / "target/release/pallas-bench")],
    }
    timeouts = json.loads(run(commands["chrysalis"] + [str(root), "check", "1", "unused"]).stdout)["timeouts"]
    process_timeout = process_timeout_seconds(timeouts)
    # A short socket path lives in the caller's own output directory, not /tmp.
    socket_path = Path(os.path.relpath(args.out.resolve() / "peer.sock"))
    if len(os.fsencode(socket_path)) >= 104:
        parser.error("Use a shorter --out path to fit the Unix-socket address")
    ready = args.out / "peer-ready.json"
    summary = args.out / "peer-summary.json"
    connections = args.pairs * 2
    peer_command = [sys.executable, "-B", str(here / "replay_peer.py"), str(root),
                    str(socket_path), str(ready), str(summary), str(connections)]
    started = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    load_start = os.getloadavg()
    peer = None
    expected_ranges = []
    with (args.out / "peer.log").open("w") as log:
        try:
            results = {}
            for mode in ("typed", "walk", "replay"):
                if mode == "replay":
                    # Start only when needed: long micro runs must not exhaust accept timeout.
                    peer = subprocess.Popen(peer_command, stdout=log, stderr=log)
                    deadline = time.monotonic() + 15
                    while not ready.exists():
                        if peer.poll() is not None or time.monotonic() > deadline:
                            raise RuntimeError("Replay peer did not become ready")
                        time.sleep(0.02)
                rows = []
                iterations = args.replay_iterations if mode == "replay" else args.iterations or {"typed": 6000, "walk": 20000}[mode]
                for pair in range(args.pairs):
                    row = {}
                    order = ("chrysalis", "rust") if pair % 2 == 0 else ("rust", "chrysalis")
                    for side in order:
                        command = commands[side] + [str(root), mode, str(iterations), str(socket_path)]
                        output = run(command, timeout=process_timeout).stdout
                        (args.out / f"{mode}-{pair}-{side}.json").write_text(output)
                        row[side] = json.loads(output)
                        if mode == "replay":
                            expected_ranges.append(iterations + row[side]["warmupIterations"])
                    validate_pair(row["chrysalis"], row["rust"], plan)
                    rows.append(row)
                # paired_ratio(baseline=Rust, comparison=Chrysalis) returns Chrysalis/Rust.
                ratio = paired_ratio([r["rust"]["elapsed_ns"] for r in rows],
                                     [r["chrysalis"]["elapsed_ns"] for r in rows])
                results[mode] = {
                    "blocksPerIteration": 29 if mode == "replay" else 30,
                    "iterationsPerProcess": iterations,
                    "itemsPerIteration": plan["items"] if mode == "walk" else None,
                    "validatedChecksSha256": hashlib.sha256(json.dumps(
                        plan["checks"][1 if mode == "replay" else 0:], sort_keys=True).encode()).hexdigest(),
                    "chrysalisElapsedNs": [r["chrysalis"]["elapsed_ns"] for r in rows],
                    "rustElapsedNs": [r["rust"]["elapsed_ns"] for r in rows],
                    "chrysalisJit": [r["chrysalis"]["jit"] for r in rows],
                    "chrysalisWarmupIterations": [r["chrysalis"]["warmupIterations"] for r in rows],
                    "ratioChrysalisOverRust": ratio,
                }
            peer.wait(timeout=10)
            if peer.returncode != 0:
                raise RuntimeError("Replay peer failed; see peer.log")
        finally:
            if peer is not None and peer.poll() is None:
                peer.terminate()
                peer.wait(timeout=10)
            socket_path.unlink(missing_ok=True)
            ready.unlink(missing_ok=True)
    load_end = os.getloadavg()
    peer_rows = json.loads(summary.read_text())
    if len(peer_rows) != connections or [row["ranges"] for row in peer_rows] != expected_ranges:
        raise ValueError("Peer did not complete every warmup/measured range")
    lock = (here / "Cargo.lock").read_text()
    def locked_version(name):
        versions = re.findall(r'\[\[package\]\]\nname = "' + re.escape(name) + r'"\nversion = "([^"]+)"', lock)
        return max(versions, key=lambda v: tuple(int(x) for x in v.split(".")))
    packages = ET.parse(here.parents[1] / "Directory.Packages.props")
    saib_version = packages.find(".//PackageVersion[@Include='SAIB.Cbor']").attrib["Version"]
    report = {"startedUtc": started, "platform": platform.system(), "machine": platform.machine(),
              "cpu": next((line.split(":", 1)[1].strip() for line in Path("/proc/cpuinfo").read_text().splitlines() if line.startswith("model name")), platform.machine()),
              "loadavgStart": load_start, "loadavgEnd": load_end,
              "python": platform.python_version(), "dotnetSdk": run(["dotnet", "--version"]).stdout.strip(),
              "dotnetRuntime": rows[0]["chrysalis"]["runtime"], "serverGC": rows[0]["chrysalis"]["serverGC"],
              "rustc": run(["rustc", "--version"]).stdout.strip(),
              "pallas": locked_version("pallas"), "minicbor": locked_version("minicbor"), "saibCbor": saib_version,
              "runtimeControls": {name: os.environ.get(name, "default") for name in (
                  "DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_gcServer")},
              "compilationLimits": {"timeFraction": 1 / COMPILATION_TIME_DENOMINATOR, "ilBytes": COMPILED_IL_LIMIT},
              "cargoLockSha256": hashlib.sha256((here / "Cargo.lock").read_bytes()).hexdigest(),
              "corpusManifestSha256": hashlib.sha256((root / "sha256.json").read_bytes()).hexdigest(),
              "pair3": "replay peer, not live-node throughput", "results": results,
              "peerConnections": len(peer_rows), "completedRangesPerConnection": expected_ranges,
              "rustRuntime": "tokio default multi-thread", "networkMagic": 1}
    (args.out / "comparison.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({mode: row["ratioChrysalisOverRust"] for mode, row in results.items()}, indent=2))


if __name__ == "__main__":
    main()
