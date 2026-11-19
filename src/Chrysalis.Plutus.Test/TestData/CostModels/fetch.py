#!/usr/bin/env python3
"""Copy and verify pinned V2 and V2b fixtures, or emit the production tables from local upstream clones."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parent
PLUTUS = "1.70.0.0"


def git(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args])


def verify(repos, rows):
    for row in rows:
        if row["repository"] == "G1":  # already in this repository; linked at build time
            data = (ROOT.parents[3] / row["upstreamPath"]).read_bytes()
        else:
            repo = repos[row["repository"]]
            ref = f"{row['tag']}:{row['upstreamPath']}"
            if git(repo, "rev-parse", f"refs/tags/{row['tag']}^{{commit}}").decode().strip() != row["commit"]:
                raise ValueError(f"wrong commit: {ref}")
            if git(repo, "rev-parse", ref).decode().strip() != row["blob"]:
                raise ValueError(f"wrong blob: {ref}")
            data = git(repo, "show", ref)
        if len(data) != row["bytes"] or hashlib.sha256(data).hexdigest() != row["sha256"]:
            raise ValueError(f"wrong bytes: {row['path']}")
        if row["repository"] != "G1":
            path = ROOT / row["path"]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)


def emit(plutus, rows):
    def table(name, type_name, values, per_line):
        print(f"    internal static readonly ImmutableArray<{type_name}> {name} =\n    [")
        for i in range(0, len(values), per_line):
            print("        " + ", ".join(values[i:i + per_line]) + ",")
        print("    ];")

    for lang in ("V1", "V2", "V3"):
        path = next(row["upstreamPath"] for row in rows if row["path"] == f"plutus-{PLUTUS}/{lang}/ParamName.hs")
        source = git(plutus, "show", f"{PLUTUS}:{path}").decode().split("data ParamName", 1)[1].split("Batch 7", 1)[0]
        names = [s[0].lower() + s[1:].replace("'", "-") for s in re.findall(r"(?m)^[ \t]*[=|]?[ \t]*([A-Z][\w']*)", source)]
        table(lang, "string", [json.dumps(name) for name in names], 3)

    def leaves(node, prefix=""):
        for key, value in node.items():
            if isinstance(value, dict):
                yield from leaves(value, prefix + key + "-")
            elif type(value) is int:
                yield prefix + key, value

    params = {}
    for file in ("builtinCostModelC.json", "cekMachineCostsC.json"):
        params.update(leaves(json.loads(git(plutus, "show", f"{PLUTUS}:plutus-core/cost-model/data/{file}"))))
    table("VariantC", "long", [str(params[name]) for name in names], 10)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plutus", required=True)
    parser.add_argument("--plutigo")
    parser.add_argument("--emit", action="store_true")
    args = parser.parse_args()
    rows = json.loads((ROOT / "manifest.json").read_bytes())
    if args.emit:
        emit(args.plutus, rows)
    elif args.plutigo:
        verify({"IntersectMBO/plutus": args.plutus, "blinklabs-io/plutigo": args.plutigo}, rows)
    else:
        parser.error("--plutigo required in verify mode")
