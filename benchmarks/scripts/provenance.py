"""Invocation-bound metric artifacts with explicit rejection diagnostics."""
import hashlib
import json
from pathlib import Path


def artifact_identity(manifest, side):
    info = manifest["sides"][side]
    identity = {"invocationId": manifest["invocationId"], "side": side,
                "commit": info["commit"], "harnessSha256": info["harnessSha256"]}
    if "treeId" in info:
        identity["treeId"] = info["treeId"]
    return identity


def case_names(kind, payload):
    if kind == "micro":
        return sorted({row.get("FullName", row.get("DisplayInfo", "unknown")) for row in payload.get("Benchmarks", [])})
    rows = payload.get("rows", []) if kind == "replay" else [payload] if kind == "network" else payload if kind == "direct" else []
    return sorted({row["name"] for row in rows if isinstance(row, dict) and isinstance(row.get("name"), str)})


def register_artifact(root, manifest, side, kind, path):
    root, path = Path(root).resolve(), Path(path).resolve()
    relative = path.relative_to(root)
    entry = dict(artifact_identity(manifest, side), kind=kind, path=relative.as_posix(), complete=True,
                 sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                 caseNames=case_names(kind, json.loads(path.read_text())))
    manifest.setdefault("artifacts", []).append(entry)
    return entry


def artifact_problem(root, manifest, entry):
    side = entry.get("side")
    if manifest.get("schemaVersion", 0) < 2 or not manifest.get("invocationId"):
        return "no current invocation identity (legacy/unverified manifest)"
    if side not in manifest.get("sides", {}):
        return "side is not in this invocation"
    for key, value in artifact_identity(manifest, side).items():
        if entry.get(key) != value:
            return f"{key} does not match this invocation"
    if not entry.get("complete"):
        return "producer did not complete successfully"
    path = (root / entry.get("path", "")).resolve()
    if not path.is_relative_to(root):
        return "artifact path is outside the invocation directory"
    if not path.is_file():
        return "artifact file is missing"
    try:
        if hashlib.sha256(path.read_bytes()).hexdigest() != entry.get("sha256"):
            return "artifact content SHA256 does not match its registered fingerprint"
        json.loads(path.read_text())
    except (OSError, ValueError) as exception:
        return f"artifact JSON is unreadable: {exception}"
    return None


def accepted_artifacts(root, manifest, side, kind):
    """Return only successful producer artifacts bound to this exact invocation and side."""
    root = Path(root).resolve()
    return sorted({(root / entry["path"]).resolve() for entry in manifest.get("artifacts", [])
                   if entry.get("side") == side and entry.get("kind") == kind and not artifact_problem(root, manifest, entry)})


def metric_kind(path):
    if path.name.endswith("-report-full.json"):
        return "micro"
    if path.name.startswith(("N2C_", "N2N_")) and path.suffix == ".json":
        return "network"
    return {"direct.json": "direct", "offline-replay.json": "replay", "wire.json": "wire",
            "field-coverage.json": "coverage"}.get(path.name)


def rejected_artifacts(root, manifest):
    """Name every rejected or unregistered metric file; names are never measurements."""
    root = Path(root).resolve()
    rejected, registered = [], set()
    for entry in manifest.get("artifacts", []):
        registered.add(entry.get("path"))
        reason = artifact_problem(root, manifest, entry)
        if reason:
            rejected.append(dict(entry, reason=reason))
    for path in sorted(root.rglob("*.json")):
        relative = path.relative_to(root)
        kind = metric_kind(path)
        if not kind or relative.as_posix() in registered or "build" in relative.parts:
            continue
        side = relative.parts[0] if relative.parts[0] in manifest.get("sides", {}) else "unknown"
        names = []
        if path.resolve().is_relative_to(root):
            try:
                names = case_names(kind, json.loads(path.read_text()))
            except (OSError, ValueError, TypeError, AttributeError):
                pass
        rejected.append({"path": relative.as_posix(), "side": side, "kind": kind, "caseNames": names,
                         "reason": "unregistered artifact: not a successful producer output for this invocation"})
    return rejected
