#!/usr/bin/env python3
"""Produce portable JSON and Markdown without confusing BDN's 99.9% CI with 95%."""
import json
import math
from pathlib import Path
import statistics
import sys

from provenance import accepted_artifacts, rejected_artifacts, case_names


def beta_fraction(a, b, x):
    """Continued fraction for regularized incomplete beta (Lentz iteration)."""
    qab, qap, qam = a + b, a + 1, a - 1
    c = 1.0
    d = 1 - qab * x / qap
    d = 1 / max(abs(d), 1e-300) * (1 if d >= 0 else -1)
    h = d
    for m in range(1, 301):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1 + aa * d
        if abs(d) < 1e-300:
            d = 1e-300
        c = 1 + aa / c
        if abs(c) < 1e-300:
            c = 1e-300
        d = 1 / d
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1 + aa * d
        if abs(d) < 1e-300:
            d = 1e-300
        c = 1 + aa / c
        if abs(c) < 1e-300:
            c = 1e-300
        d = 1 / d
        delta = d * c
        h *= delta
        if abs(delta - 1) < 2e-14:
            return h
    raise ArithmeticError("Incomplete beta did not converge")


def regularized_beta(a, b, x):
    if x <= 0:
        return 0.0
    if x >= 1:
        return 1.0
    weight = math.exp(math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b)
                      + a * math.log(x) + b * math.log1p(-x))
    if x < (a + 1) / (a + b + 2):
        return weight * beta_fraction(a, b, x) / a
    return 1 - weight * beta_fraction(b, a, 1 - x) / b


def t95(df):
    if df < 1:
        raise ValueError("Student CI needs at least two observations")
    low, high = 0.0, 1.0
    def tail(t):
        return 0.5 * regularized_beta(df / 2, 0.5, df / (df + t * t))
    while tail(high) > 0.025:
        high *= 2
    for _ in range(90):
        mid = (low + high) / 2
        if tail(mid) > 0.025:
            low = mid
        else:
            high = mid
    return (low + high) / 2


def confidence95(values):
    if len(values) < 2:
        return None
    mean = statistics.mean(values)
    margin = t95(len(values) - 1) * statistics.stdev(values) / math.sqrt(len(values))
    return [mean - margin, mean + margin]


def bdn_case(item):
    stats = item.get("Statistics")
    if not stats:
        return {"name": item.get("FullName", "unknown"), "status": "REJECTED",
                "reason": "BenchmarkDotNet statistics are unavailable: workload failed or was not measured"}
    # Result measurements already have overhead removed and are normalized by operation count.
    values = [m["Nanoseconds"] / m["Operations"] for m in item.get("Measurements", [])
              if m.get("IterationMode") == "Workload" and m.get("IterationStage") == "Result" and m["Operations"]]
    return {"name": item.get("FullName", item.get("DisplayInfo")), "meanNs": stats["Mean"],
            "confidence95Ns": confidence95(values), "samples": len(values), "iterationMeanNs": values,
            "allocatedBytesPerOperation": item.get("Memory", {}).get("BytesAllocatedPerOperation"),
            "method": "BenchmarkDotNet MemoryDiagnoser, all threads; Student t 95% CI on BDN result iterations",
            "status": "measured" if values else "REJECTED",
            **({} if values else {"reason": "BenchmarkDotNet has no workload result iterations"})}


def reproduction(audit, measured):
    results = []
    for name, expected in audit["rows"].items():
        samples = measured.get(name, [])
        values = [sample["bytes"] for sample in samples]
        status = "not measured"
        if values:
            within = min(values) >= expected["minimum"] - 0.005 and max(values) <= expected["maximum"] + 0.005
            status = "reproduced" if within else "difference: needs explanation (not a gate verdict)"
        results.append({"name": name, "audit": expected, "minimumBytes": min(values) if values else None,
                        "maximumBytes": max(values) if values else None, "samples": samples, "status": status})
    return results


def load_gate_input(root):
    """Fail-closed completeness guard, not a timing gate or P2 policy."""
    result = json.loads((Path(root) / "results.json").read_text())
    if result.get("status") != "COMPLETED" or not result.get("gateInputEligible", False):
        raise ValueError(f"Not a complete gate input: {result.get('status', 'UNVERIFIED')}; {result.get('failure') or result.get('gateInputReason', '')}")
    if not {"baseline", "candidate"}.issubset(result.get("sides", {})):
        raise ValueError("Not a comparison: gate input requires both baseline and candidate")
    return result


def micro_cases(root, manifest, rejected, failed):
    by_side = {side: [] for side in manifest["sides"]}
    if failed:
        return by_side
    for side, rows in by_side.items():
        for file in accepted_artifacts(root, manifest, side, "micro"):
            for item in json.loads(file.read_text()).get("Benchmarks", []):
                rows.append(dict(bdn_case(item), rawFile=str(file.relative_to(root))))
    names = {row["name"] for rows in by_side.values() for row in rows}
    names.update(name for entry in rejected if entry.get("kind") == "micro" for name in entry.get("caseNames", []))
    for side, rows in by_side.items():
        present = {row["name"] for row in rows}
        for name in sorted(names - present):
            reasons = [entry["reason"] for entry in rejected if entry.get("side") == side and entry.get("kind") == "micro"
                       and name in entry.get("caseNames", [])]
            rows.append({"name": name, "status": "REJECTED" if reasons else "MISSING",
                         "reason": "; ".join(reasons) if reasons else "case present on another side but no accepted measurement on this side"})
    return by_side


def coverage_rows(root, manifest, rejected, micro, failed):
    """R6 coverage for all measurement families; missing samples never become zero."""
    coverage = []
    for kind in ["micro", "direct", "network", "replay"]:
        by_side = {side: {} for side in manifest["sides"]}
        if not failed:
            for side, names in by_side.items():
                if kind == "micro":
                    for row in micro[side]:
                        names.setdefault(row["name"], []).append(row)
                else:
                    for file in accepted_artifacts(root, manifest, side, kind):
                        for name in case_names(kind, json.loads(file.read_text())):
                            names.setdefault(name, []).append({"status": "measured"})
        universe = {name for names in by_side.values() for name in names}
        universe.update(name for entry in rejected if entry.get("kind") == kind for name in entry.get("caseNames", []))
        for side, names in by_side.items():
            for name in sorted(universe):
                rows = names.get(name, [])
                reasons = [entry["reason"] for entry in rejected if entry.get("side") == side and entry.get("kind") == kind
                           and name in entry.get("caseNames", [])]
                bad = [row for row in rows if row["status"] != "measured"]
                status = "REJECTED" if reasons or any(row["status"] == "REJECTED" for row in rows) else "MISSING" if not rows or all(row["status"] == "MISSING" for row in rows) else "measured"
                reason = "; ".join([row.get("reason", row["status"]) for row in bad] + reasons)
                if not rows and not reasons:
                    reason = "case present on another side but no accepted measurement on this side"
                coverage.append({"side": side, "kind": kind, "name": name, "status": status, "reason": reason})
    return coverage


def summarize(root):
    manifest = json.loads((root / "manifest.json").read_text())
    audit = json.loads((Path(__file__).parents[1] / "Chrysalis.Benchmarks/audit-allocations.json").read_text())
    failed = bool(manifest.get("failure")) or manifest.get("status") == "FAILED"
    rejected = rejected_artifacts(root, manifest)
    micro = micro_cases(root, manifest, rejected, failed)
    coverage = coverage_rows(root, manifest, rejected, micro, failed)
    issues = [row for row in coverage if row["status"] != "measured"]
    state = "FAILED" if failed else manifest.get("status", "UNVERIFIED")
    comparison = {"baseline", "candidate"}.issubset(manifest["sides"])
    if state == "COMPLETED" and not comparison:
        state = "NOT_A_COMPARISON"
    if state == "COMPLETED" and (rejected or issues or manifest.get("measure") in ("all", "micro", "direct", "network", "replay") and not coverage):
        state = "INCOMPLETE"
    eligible = comparison and state == "COMPLETED" and bool(coverage) and not rejected and not issues
    reason = "Completeness only; no timing gate evaluated" if eligible else "Failed, unverified, missing, rejected or no measurement cases; diagnostic evidence only"
    if not comparison and not failed:
        reason = "Comparison requires both baseline and candidate; missing: " + ", ".join(sorted({"baseline", "candidate"} - manifest["sides"].keys()))
    all_results = dict(manifest, status=state, gateInputEligible=eligible,
                       gateInputReason=reason,
                       rejectedArtifacts=rejected, caseCoverage=coverage, runs=[], benchmarks={}, reproduction={})
    def artifacts(side, kind):
        return [] if failed else accepted_artifacts(root, manifest, side, kind)
    lines = [f"# {state} — Chrysalis side-by-side benchmark results", "",
             f"**Failure:** {manifest.get('failure') or 'Invocation marked FAILED without a recorded failure reason'}" if failed else f"Status: **{state}**. Gate input eligible: **{eligible}** (completeness only; no timing gate verdict).", "", f"Mode: **{manifest['mode']}**. Smoke is development evidence, never a timing gate.",
             "", f"Gate input reason: {reason}.",
             "", f"Machine: `{manifest['machine']['hostname']}`, {manifest['machine']['cpu']}, {manifest['machine']['logicalCpus']} logical CPUs; {manifest['machine'].get('architecture', 'unknown architecture')}; {manifest['machine']['os']}.",
             "", "Metric artifacts must match this invocation ID, side commit, harness hash and content hash; unregistered/stale artifacts are rejected and listed below. Failed invocation artifacts are diagnostic only and never published as measurements.",
             "", "Time intervals below are **95%**, not BenchmarkDotNet's default displayed 99.9%. Allocation methods are kept separate.",
             "", f"Whole path: {manifest['wholePathStatus']}.", ""]
    if rejected:
        lines += ["## Rejected artifacts", "", "| Side | Kind | Artifact | Reason |", "|---|---|---|---|"]
        for entry in rejected:
            reason = entry["reason"].replace("|", "\\|").replace("\n", " ")
            lines.append(f"| {entry.get('side', 'unknown')} | {entry.get('kind', 'unknown')} | `{entry.get('path', 'unknown')}` | {reason} |")
        lines += [""]
    if failed:
        lines += ["Invocation failed: no normal measurement or gate input is exported. Registered raw artifacts are retained only for diagnosis.", ""]
    if coverage:
        lines += ["## Case coverage across sides", "", "| Family | Case | Side | Status | Reason |", "|---|---|---|---|---|"]
        for row in coverage:
            reason = row["reason"].replace("|", "\\|").replace("\n", " ")
            lines.append(f"| {row['kind']} | {row['name']} | {row['side']} | {row['status']} | {reason} |")
        lines += [""]
    for run in sorted(root.rglob("*.run.json")):
        data = json.loads(run.read_text())
        if data.get("invocationId") != manifest.get("invocationId") or not manifest.get("invocationId"):
            continue
        data["file"] = str(run.relative_to(root))
        all_results["runs"].append(data)
    for side, info in manifest["sides"].items():
        lines += [f"## {side}", "", f"Commit `{info['commit']}`; dirty={info['dirty']}; build SDK {info['sdk']}; target {info.get('environment', {}).get('targetFramework', 'unknown')}; runtime {info.get('environment', {}).get('runtime', 'unknown')}.",
                  "", f"Working-tree content id: `{info.get('treeId', 'unrecorded')}`; BenchmarkDotNet {info.get('benchmarkDotNetVersion', info.get('environment', {}).get('benchmarkDotNet', 'unknown'))}.",
                  "", f"GC/runtime settings: `{json.dumps(info.get('environment', {}).get('gc', {}), sort_keys=True)}`.", ""]
        if info.get("attributionOnly"):
            lines += ["**ATTRIBUTION ONLY:** identical SDK10-built baseline assemblies hosted on runtime11; not the original-runtime baseline or a gate result.",
                      f"Harness assembly SHA256: `{info.get('assemblySha256', 'unknown')}`. SDK assembly hashes are recorded in results.json.", ""]
        lines += ["### Microbenchmarks", "", "| Case | Status | Mean ns/op | 95% CI ns/op | Allocated B/op | n | Reason |", "|---|---|---:|---:|---:|---:|---|"]
        cases = micro[side]
        for case in cases:
            ci = case.get("confidence95Ns")
            lines.append(f"| {case['name']} | {case['status']} | {case.get('meanNs', '—')} | {f'[{ci[0]:.3f}, {ci[1]:.3f}]' if ci else 'undefined'} | {case.get('allocatedBytesPerOperation', '—')} | {case.get('samples', 0)} | {case.get('reason', '')} |")
        all_results["benchmarks"][side] = cases
        measured = {}
        direct = []
        for direct_path in artifacts(side, "direct"):
            direct.extend(json.loads(direct_path.read_text()))
        measured.update({row["name"]: row["samples"] for row in direct})
        all_results.setdefault("direct", {})[side] = direct
        if direct:
            lines += ["", "### Direct allocation controls", "", "| Case | Warm-up ms | Sample B/op min–max | Temperature |", "|---|---:|---:|---|"]
            for row in direct:
                allocations = [sample["bytes"] for sample in row["samples"]]
                temperature = "; ".join(sorted({sample.get("temperature", "unlabelled") for sample in row["samples"]}))
                lines.append(f"| {row['name']} | {row.get('warmupMilliseconds', 'unlabelled')} | {min(allocations):.3f}–{max(allocations):.3f} | {temperature} |")
        rows = reproduction(audit, measured)
        all_results["reproduction"][side] = rows
        lines += ["", "### Audit allocation reproduction (thread-local, five batches)", "",
                  "Direct samples retain their explicit warm-up/temperature labels; cold/transitional batches are not regression verdicts and no steady state is certified here.", "",
                  "Candidate differences are reported, not compared to the audit's runtime-11 roll-forward as a replacement baseline.", "",
                  "| Case | Audit .NET 10 B/op | This run B/op min–max | Result |", "|---|---:|---:|---|"]
        for row in rows:
            expected = row["audit"]
            actual = "not measured" if row["minimumBytes"] is None else f"{row['minimumBytes']:.2f}–{row['maximumBytes']:.2f}"
            lines.append(f"| {row['name']} | {expected['minimum']:.2f}–{expected['maximum']:.2f} | {actual} | {row['status']} |")
        network = []
        for file in artifacts(side, "network"):
            if file.name.startswith(("N2C_", "N2N_")):
                item = json.loads(file.read_text())
                item["rawFile"] = str(file.relative_to(root))
                network.append(item)
        all_results.setdefault("network", {})[side] = network
        grouped = []
        for name in sorted({item["name"] for item in network}):
            samples = [item for item in network if item["name"] == name]
            times = [item["ns"] for item in samples]
            allocations = [item["bytes"] for item in samples]
            grouped.append({"name": name, "processes": len(samples), "meanNsPerBlock": statistics.mean(times),
                            "confidence95NsPerBlock": confidence95(times),
                            "meanBytesPerBlock": statistics.mean(allocations),
                            "confidence95BytesPerBlock": confidence95(allocations),
                            "minimumBytesPerBlock": min(allocations), "maximumBytesPerBlock": max(allocations),
                            "meanBlocksPerSecond": statistics.mean(item["blocksPerSecond"] for item in samples),
                            "method": "independent fixed-work processes; Student t 95% CI on process means; no gate verdict"})
        all_results.setdefault("networkSummary", {})[side] = grouped
        lines += ["", "### Fixed-work whole paths", "", "| Case | Blocks | Blocks/s | ns/block | Process-wide B/block |", "|---|---:|---:|---:|---:|"]
        for item in network:
            lines.append(f"| {item['name']} | {item['blocks']} | {item['blocksPerSecond']:.3f} | {item['ns']:.3f} | {item['bytes']:.3f} |")
        if grouped:
            lines += ["", "#### Whole-path process means and 95% intervals", "",
                      "| Case | Processes | Mean ns/block [95% CI] | Mean B/block [95% CI] |", "|---|---:|---:|---:|"]
            for item in grouped:
                tc, ac = item["confidence95NsPerBlock"], item["confidence95BytesPerBlock"]
                time_text = f"[{tc[0]:.3f}, {tc[1]:.3f}]" if tc else "undefined"
                allocation_text = f"[{ac[0]:.3f}, {ac[1]:.3f}]" if ac else "undefined"
                lines.append(f"| {item['name']} | {item['processes']} | {item['meanNsPerBlock']:.3f} {time_text} | {item['meanBytesPerBlock']:.3f} {allocation_text} |")
        replay = []
        for file in artifacts(side, "replay"):
            payload = json.loads(file.read_text())
            for row in payload.get("rows", []):
                replay.append(dict(row, rawFile=str(file.relative_to(root)), environment=payload.get("environment", {})))
        all_results.setdefault("offlineReplay", {})[side] = replay
        if replay:
            lines += ["", "### Offline receive-component replay (attribution only, NOT live whole path)", "",
                      "Excludes socket, request sending, node and BlockFetch agency; includes common memory-pipe producer overhead. Five batches within one process, not five independent processes.", "",
                      "| Case | Batches | Process-wide B/block min–max |", "|---|---:|---:|"]
            for row in replay:
                allocations = [sample["bytes"] for sample in row["samples"]]
                lines.append(f"| {row['name']} | {len(allocations)} | {min(allocations):.3f}–{max(allocations):.3f} |")
        wire_files = artifacts(side, "wire")
        if wire_files:
            all_results.setdefault("wire", {})[side] = [row for wire in wire_files for row in json.loads(wire.read_text())]
            lines += ["", "### Captured query/rejection placeholder", "", "No baseline: path did not work at 7c45d47. No invented zero-byte/time result.", ""]
    lines += ["", "## Actual load at the start and end of every heavy invocation", "", "| Run | Start UTC | Load1 start | End UTC | Load1 end | Exit |", "|---|---|---:|---|---:|---:|"]
    for run in all_results["runs"]:
        lines.append(f"| {run['file']} | {run['start']['utc']} | {run['start']['load1']:.2f} | {run['end']['utc']} | {run['end']['load1']:.2f} | {run['exitCode']} |")
    (root / "results.json").write_text(json.dumps(all_results, indent=2, allow_nan=False) + "\n")
    (root / "summary.md").write_text("\n".join(lines) + "\n")
    return all_results


if __name__ == "__main__":
    result = summarize(Path(sys.argv[1]).resolve())
    sys.exit(0 if result["status"] == "COMPLETED" else 1)
