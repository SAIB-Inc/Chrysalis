#!/usr/bin/env python3
"""Build one immutable harness source against two source trees, then measure identically."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import time
import tempfile
import uuid

from provenance import register_artifact
from summarize import summarize

HERE = Path(__file__).resolve()
REPO = HERE.parents[2]
HARNESS = REPO / "benchmarks/Chrysalis.Benchmarks"
DOTNET = Path(os.environ.get("DOTNET_ROOT", str(Path.home() / ".dotnet"))) / "dotnet"
if not DOTNET.exists():
    DOTNET = Path(shutil.which("dotnet") or str(DOTNET)).resolve()
DOTNET_ROOT = DOTNET.resolve().parent


def load1():
    if sys.platform == "darwin":
        # BSD/macOS reports e.g. "{ 1.23 2.34 3.45 }".
        return float(subprocess.check_output(["sysctl", "-n", "vm.loadavg"], text=True).strip(" {}\n").split()[0])
    load_file = Path("/proc/loadavg")
    return float(load_file.read_text().split()[0]) if load_file.exists() else os.getloadavg()[0]


def machine_info():
    if sys.platform == "darwin":
        cpu = command_output("sysctl", "-n", "machdep.cpu.brand_string")
        physical = int(command_output("sysctl", "-n", "hw.physicalcpu"))
    elif Path("/proc/cpuinfo").exists():
        text = Path("/proc/cpuinfo").read_text()
        cpu = next((line.split(":", 1)[1].strip() for line in text.splitlines() if line.startswith("model name")), "unknown")
        identities = set()
        for item in text.split("\n\n"):
            fields = dict(line.split(":", 1) for line in item.splitlines() if ":" in line)
            normalized = {key.strip(): value.strip() for key, value in fields.items()}
            if "physical id" in normalized and "core id" in normalized:
                identities.add((normalized["physical id"], normalized["core id"]))
        physical = len(identities) or None
    else:
        cpu, physical = platform.processor() or "unknown", None
    return {"hostname": platform.node(), "cpu": cpu, "physicalCores": physical,
            "logicalCpus": os.cpu_count(), "architecture": platform.machine(), "os": platform.platform()}


def stamp():
    return {"utc": dt.datetime.now(dt.timezone.utc).isoformat(), "load1": load1()}


def execute_internal(arguments):
    """Executed *inside* heavy: load excludes time spent waiting for a slot."""
    destination, cwd, *command = arguments
    start = stamp()
    code = subprocess.call(command, cwd=None if cwd == "-" else cwd)
    end = stamp()
    Path(destination).write_text(json.dumps({"command": command, "start": start, "end": end,
                                           "exitCode": code, "cwd": cwd,
                                           "invocationId": os.environ.get("CHRYSALIS_BENCHMARK_INVOCATION_ID")}, indent=2) + "\n")
    return code


def log_json(text):
    start = text.find("{")
    if start < 0:
        raise ValueError("No JSON object in command log")
    return json.JSONDecoder().raw_decode(text[start:])[0]


def git_environment(**overrides):
    env = dict(os.environ, **overrides)
    env["GIT_OPTIONAL_LOCKS"] = "0"
    return env


def command_output(*arguments):
    options = {"text": True}
    if arguments and Path(str(arguments[0])).name == "git":
        options["env"] = git_environment()
    return subprocess.check_output(arguments, **options).strip()


def run(command, output, label, env=None, cwd=None):
    output.mkdir(parents=True, exist_ok=True)
    metadata = output / (label + ".run.json")
    log = output / (label + ".log")
    full = ["heavy", sys.executable, str(HERE), "_exec", str(metadata), str(cwd) if cwd is not None else "-", *map(str, command)]
    print("+", label, flush=True)
    with log.open("w") as stream:
        code = subprocess.call(full, env=env, stdout=stream, stderr=subprocess.STDOUT)
    if code:
        print(log.read_text()[-10000:], file=sys.stderr)
        raise RuntimeError(f"{label} exited {code}; retained {log}")
    return metadata


def working_tree_id(source):
    """Git content tree of tracked + unignored working files; never use the real index."""
    temporary = REPO / "benchmarks/out"
    temporary.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="source-index-", dir=temporary) as directory:
        env = git_environment(GIT_INDEX_FILE=str(Path(directory) / "index"))
        git = ["git", "-C", str(source)]
        subprocess.check_call([*git, "read-tree", "HEAD"], env=env)
        subprocess.check_call([*git, "add", "--all", "--", "."], env=env)
        return subprocess.check_output([*git, "write-tree"], env=env, text=True).strip()


def resolved_benchmarkdotnet_version(directory):
    assets = json.loads((directory / "obj/project.assets.json").read_text())
    versions = {name.split("/", 1)[1] for name in assets.get("libraries", {})
                if name.split("/", 1)[0].lower() == "benchmarkdotnet"}
    if len(versions) != 1:
        raise RuntimeError(f"Expected one resolved BenchmarkDotNet package in {directory}; found {sorted(versions)}")
    return versions.pop()


def require_same_benchmarkdotnet(sides):
    packages = {side: info.get("benchmarkDotNetVersion") for side, info in sides.items()}
    assemblies = {side: info.get("environment", {}).get("benchmarkDotNet") for side, info in sides.items()}
    if None in packages.values() or len(set(packages.values())) != 1:
        raise RuntimeError("Resolved BenchmarkDotNet versions differ; refusing measurements: " +
                           ", ".join(f"{side}={version}" for side, version in packages.items()))
    if None in assemblies.values() or len(set(assemblies.values())) != 1:
        raise RuntimeError("Loaded BenchmarkDotNet assembly versions differ; refusing measurements: " +
                           ", ".join(f"{side}={version}" for side, version in assemblies.items()))


def require_local_runtime_image(runtime, output):
    image = f"mcr.microsoft.com/dotnet/runtime:{runtime}"
    try:
        run(["docker", "image", "inspect", image], output, "runtime-image")
    except RuntimeError as exception:
        raise RuntimeError(f"Required local runtime image {image} is missing or unavailable; local-only runs never pull and images are never pulled by this runner.") from exception
    text = (output / "runtime-image.log").read_text()
    info = json.JSONDecoder().raw_decode(text[text.index("["):])[0][0]
    return {"tag": image, "id": info["Id"], "digests": info.get("RepoDigests")}


def n2n_container_command(image, binary, result, range_path):
    work = REPO.parent.resolve()
    def mounted(path):
        return "/work/" + str(Path(path).resolve().relative_to(work))
    return ["docker", "run", "--pull=never", "--rm", "--network", "container:chrysalis-preprod-node",
            "--user", f"{os.getuid()}:{os.getgid()}", "-v", f"{work}:/work",
            "-e", f"CHRYSALIS_BENCHMARK_DATA={mounted(HARNESS)}",
            "-e", f"CHRYSALIS_BENCHMARK_RESULTS={mounted(result)}",
            "-e", "DOTNET_ROLL_FORWARD=LatestPatch", image,
            "dotnet", mounted(binary), "network", "n2n", "127.0.0.1", mounted(range_path)]


def stage(side, source, output, snapshot=None):
    sdk = "10.0.301" if side == "baseline" else "11.0.100"
    tfm = "net10.0" if side == "baseline" else "net11.0"
    directory = output / "build" / side
    directory.mkdir(parents=True, exist_ok=True)
    files = snapshot if snapshot is not None else {file.name: file.read_bytes() for file in HARNESS.glob("*.cs")}
    for name, content in files.items():
        (directory / name).write_bytes(content)
    project = (HARNESS / "Chrysalis.Benchmarks.csproj").read_text().replace("net11.0", tfm)
    project = project.replace("$(MSBuildThisFileDirectory)../..", str(source))
    (directory / "Chrysalis.Benchmarks.csproj").write_text(project)
    (directory / "global.json").write_text(json.dumps({"sdk": {"version": sdk, "rollForward": "disable"}}))
    (directory / "Directory.Build.props").write_text(
        '<Project><Import Project="' + str(source / "Directory.Build.props") + '" /></Project>\n')
    shutil.copyfile(source / "Directory.Packages.props", directory / "Directory.Packages.props")
    return directory, directory / "bin/Release" / tfm / "Chrysalis.Benchmarks.dll", sdk


def validate_output(output):
    output = output.resolve()
    if not output.is_relative_to(REPO):
        raise ValueError("--output must be inside the repository: staged SDK selectors and build artifacts may not be written outside it")
    ignored = subprocess.run(["git", "-C", str(REPO), "check-ignore", "--quiet", "--no-index", "--", str(output) + "/"],
                             env=git_environment(), capture_output=True, text=True)
    if ignored.returncode == 1:
        raise ValueError(f"--output location {output} is not ignored by git; build artifacts there would change the source tree. Choose an ignored directory such as benchmarks/out/<new-run>.")
    if ignored.returncode:
        raise ValueError(f"Cannot check --output ignore rules: {ignored.stderr.strip()}")
    return output


def initialize_output(output):
    output = validate_output(output)
    if output.exists() and any(output.iterdir()):
        raise FileExistsError(f"Refusing non-empty --output directory: {output}. Choose a fresh invocation directory.")
    output.mkdir(parents=True, exist_ok=True)
    # An exclusive claim also prevents two simultaneous invocations from sharing
    # a directory that happened to be empty at both initial checks.
    with (output / ".invocation.lock").open("x") as claim:
        claim.write(f"pid={os.getpid()}\n")
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=["smoke", "full"], default="full")
    parser.add_argument("--include-runtime11-attribution", action="store_true", help="Also run the identical SDK10 baseline DLLs on .NET11, attribution only; offline smoke/direct modes.")
    parser.add_argument("--runtime11-version", default="11.0.0")
    parser.add_argument("--direct-warmup-ms", type=int, default=1000)
    parser.add_argument("--filter", default="*")
    parser.add_argument("--measure", choices=["all", "micro", "direct", "network", "validate", "wire", "replay"], default="all")
    parser.add_argument("--baseline", type=Path, default=REPO.parent / "Chrysalis-7c45d47")
    parser.add_argument("--candidate", type=Path, default=REPO)
    parser.add_argument("--side", choices=["both", "baseline", "candidate"], default="both")
    parser.add_argument("--pairs", type=int, default=None)
    parser.add_argument("--output", type=Path, default=None)
    parser.add_argument("--socket", type=Path, default=REPO.parents[1] / "node/ipc/node.socket")
    parser.add_argument("--range", type=Path, default=HARNESS / "range.json")
    parser.add_argument("--skip-network", action="store_true")
    parser.add_argument("--n2n-container", action="store_true")
    parser.add_argument("--node-already-running", action="store_true")
    args = parser.parse_args()
    if args.direct_warmup_ms < 0:
        parser.error("--direct-warmup-ms must be nonnegative")
    if args.include_runtime11_attribution and (args.mode != "smoke" or args.measure == "network" or args.measure == "all" and not args.skip_network):
        parser.error("Runtime11 attribution is offline only and uses smoke/direct modes; add --skip-network.")
    if args.pairs is not None and args.pairs < 1:
        parser.error("--pairs must be positive")
    output = (args.output or REPO / "benchmarks/out" / dt.datetime.now().strftime("%Y%m%dT%H%M%S")).resolve()
    try:
        output = validate_output(output)
    except ValueError as exception:
        parser.error(str(exception))
    try:
        output = initialize_output(output)
    except FileExistsError as exception:
        parser.error(str(exception))
    sides = ["baseline", "candidate"] if args.side == "both" else [args.side]
    sources = {"baseline": args.baseline.resolve(), "candidate": args.candidate.resolve()}
    if args.include_runtime11_attribution:
        if "baseline" not in sides:
            parser.error("Runtime attribution requires the original baseline side")
        sides.insert(sides.index("baseline") + 1, "baseline-runtime11")
        sources["baseline-runtime11"] = sources["baseline"]
    if "baseline" in sides and not command_output("git", "-C", str(sources["baseline"]), "rev-parse", "HEAD").startswith("7c45d47"):
        parser.error("The G8 original-runtime baseline must be 7c45d47.")
    os.environ.update(NUGET_PACKAGES=str(REPO.parent / ".nuget/packages"),
                      DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_ROLL_FORWARD="LatestPatch",
                      DOTNET_DbgEnableMiniDump="0", COMPlus_DbgEnableMiniDump="0",
                      CHRYSALIS_DIRECT_WARMUP_MS=str(args.direct_warmup_ms))
    invocation = str(uuid.uuid4())
    os.environ["CHRYSALIS_BENCHMARK_INVOCATION_ID"] = invocation
    state = {"schemaVersion": 2, "invocationId": invocation, "artifacts": [], "mode": args.mode, "filter": args.filter, "measure": args.measure,
             "machine": machine_info(), "sides": {}, "wholePathStatus": "pending", "status": "RUNNING"}
    try:
        perform(args, output, state, sources, sides)
        state["status"] = "COMPLETED"
    except BaseException as exception:
        state["failure"] = f"{type(exception).__name__}: {exception}"
        state["status"] = "FAILED"
        if state["wholePathStatus"] == "pending":
            state["wholePathStatus"] = "not measured: run failed before whole-path completion"
        raise
    finally:
        (output / "manifest.json").write_text(json.dumps(state, indent=2) + "\n")
        result = summarize(output)
        state["status"] = result["status"]
        (output / "manifest.json").write_text(json.dumps(state, indent=2) + "\n")
        if state.get("failure"):
            print(f"FAILED: {state['failure']}", file=sys.stderr)
        print(f"{state['status']}: {output / 'summary.md'}", flush=True)
    if result["status"] != "COMPLETED":
        raise RuntimeError(f"Invocation is {result['status']}; no gate input: {result['gateInputReason']}")


def app_command(side, binary, *arguments, runtime11="11.0.0"):
    if side == "baseline-runtime11":
        return [DOTNET, "--fx-version", runtime11, binary, *arguments]
    return [DOTNET, binary, *arguments]


def side_environment(side, result):
    return dict(os.environ, CHRYSALIS_BENCHMARK_DATA=str(HARNESS), CHRYSALIS_BENCHMARK_RESULTS=str(result),
                DOTNET_ROLL_FORWARD="LatestMajor" if side == "baseline-runtime11" else "LatestPatch")


def perform(args, output, state, sources, sides):
    # Capture once, before any SDK work, so even an edit while heavy queues a
    # build cannot make the two sides compile different harness source.
    trees = {side: working_tree_id(sources[side]) for side in sides if side != "baseline-runtime11"}
    snapshot = {file.name: file.read_bytes() for file in HARNESS.glob("*.cs")}
    harness_hash = hashlib.sha256(b"".join(snapshot[name] for name in sorted(snapshot))).hexdigest()
    binaries = {}
    for side in sides:
        if side == "baseline-runtime11":
            binary = binaries["baseline"]
            directory = output / "build/baseline"
            info = dict(state["sides"]["baseline"], attributionOnly=True,
                        attribution="IDENTICAL SDK10-built baseline assemblies on runtime11; never the original-runtime gate baseline",
                        runtimeOverride=args.runtime11_version)
            state["sides"][side] = info
            binaries[side] = binary
        else:
            directory, binary, sdk = stage(side, sources[side], output, snapshot)
            binaries[side] = binary
            info = {"source": str(sources[side]), "commit": command_output("git", "-C", str(sources[side]), "rev-parse", "HEAD"),
                    "dirty": bool(command_output("git", "-C", str(sources[side]), "status", "--porcelain")), "sdk": sdk,
                    "harnessSha256": harness_hash, "treeId": trees[side]}
            state["sides"][side] = info
            run([DOTNET, "--version"], output / side, "sdk", cwd=directory)
            selected_sdk = (output / side / "sdk.log").read_text().strip().splitlines()[-1]
            if selected_sdk != sdk:
                raise RuntimeError(f"{side}: expected SDK {sdk}, actually selected {selected_sdk}")
            info["actualSdk"] = selected_sdk
            if side == "baseline" and info["dirty"]:
                raise RuntimeError("The pinned baseline source tree has uncommitted changes")
            cli = [DOTNET, DOTNET_ROOT / "sdk" / sdk / "dotnet.dll"] if side == "baseline" else [DOTNET]
            run([*cli, "build", directory / "Chrysalis.Benchmarks.csproj", "-c", "Release", "-m:8", "--nologo"],
                output / side, "build", cwd=directory)
            info["benchmarkDotNetVersion"] = resolved_benchmarkdotnet_version(directory)
            info["assemblySha256"] = hashlib.sha256(binary.read_bytes()).hexdigest()
            info["sdkAssemblySha256s"] = {file.name: hashlib.sha256(file.read_bytes()).hexdigest() for file in binary.parent.glob("Chrysalis*.dll")}
        environment = side_environment(side, output / side)
        run(app_command(side, binary, "environment", runtime11=args.runtime11_version), output / side, "environment", environment)
        info["environment"] = log_json((output / side / "environment.log").read_text())
        expected_major = 11 if side != "baseline" else 10
        if int(info["environment"]["runtime"].split(".")[0]) != expected_major:
            raise RuntimeError(f"{side} ran on the wrong runtime: {info['environment']['runtime']}")
        if side == "baseline-runtime11":
            actual_hashes = {file.name: hashlib.sha256(file.read_bytes()).hexdigest() for file in binary.parent.glob("Chrysalis*.dll")}
            if actual_hashes != info["sdkAssemblySha256s"]:
                raise RuntimeError("The attribution run did not use the identical baseline SDK assemblies")
    require_same_benchmarkdotnet(state["sides"])
    for side, tree in trees.items():
        if working_tree_id(sources[side]) != tree:
            raise RuntimeError(f"{side}: working tree changed during the build; refusing measurements")
    if args.measure in ("all", "network") and not args.skip_network and sys.platform == "linux" and args.n2n_container:
        for side in sides:
            state["sides"][side]["runtimeContainerImage"] = require_local_runtime_image(
                state["sides"][side]["environment"]["runtime"], output / side)
    (output / "manifest.json").write_text(json.dumps(state, indent=2) + "\n")
    for side in sides:
        env = side_environment(side, output / side)
        if args.measure in ("all", "validate", "micro"):
            run(app_command(side, binaries[side], "validate", runtime11=args.runtime11_version), output / side, "validate", env)
            register_artifact(output, state, side, "coverage", output / side / "field-coverage.json")
        if args.measure in ("all", "wire"):
            run(app_command(side, binaries[side], "wire", runtime11=args.runtime11_version), output / side, "wire", env)
            register_artifact(output, state, side, "wire", output / side / "wire.json")
        if args.measure == "replay":
            run(app_command(side, binaries[side], "replay", runtime11=args.runtime11_version), output / side, "replay", env)
            register_artifact(output, state, side, "replay", output / side / "offline-replay.json")
        if args.measure in ("all", "direct"):
            run(app_command(side, binaries[side], "direct", args.filter, runtime11=args.runtime11_version), output / side, "direct", env)
            register_artifact(output, state, side, "direct", output / side / "direct.json")
    if args.measure in ("all", "micro"):
        pairs = args.pairs or 1
        for pair in range(pairs):
            for side in (sides if pair % 2 == 0 else sides[::-1]):
                result = output / side / f"micro-{pair:02}"
                env = side_environment(side, result)
                run(app_command(side, binaries[side], args.mode, "--filter", args.filter, runtime11=args.runtime11_version), result, "benchmarks", env, cwd=output / "build" / ("baseline" if side == "baseline-runtime11" else side))
                for report in result.rglob("*-report-full.json"):
                    register_artifact(output, state, side, "micro", report)
    if args.measure in ("all", "network") and not args.skip_network and sys.platform == "linux":
        started = False
        try:
            if not args.node_already_running:
                run(["docker", "start", "chrysalis-preprod-node"], output, "node-start")
                started = True
            deadline = time.monotonic() + 180
            attempt = 0
            while True:
                attempt += 1
                ready_label = f"node-ready-{attempt:02}"
                # Readiness probes are also inside heavy; never touch the node's database.
                try:
                    run(["cardano-cli", "query", "tip", "--testnet-magic", "1", "--socket-path", args.socket], output, ready_label)
                    tip = log_json((output / (ready_label + ".log")).read_text())
                    if tip.get("syncProgress") == "100.00":
                        break
                except RuntimeError:
                    pass
                if time.monotonic() >= deadline:
                    raise RuntimeError("Direct WSL Unix socket not usable/synced; no proxy will be added.")
                time.sleep(5)
            if not args.range.exists():
                side = sides[0]
                env = side_environment(side, output / side)
                run([DOTNET, binaries[side], "capture-range", args.socket, "1000", args.range], output, "capture-range", env)
            shutil.copyfile(args.range, output / "range.json")
            state["rangeSha256"] = hashlib.sha256(args.range.read_bytes()).hexdigest()
            protocols = ["n2c", "n2n"] if args.n2n_container else ["n2c"]
            for protocol in protocols:
                for pair in range(args.pairs or 5):
                    for side in (sides if pair % 2 == 0 else sides[::-1]):
                        result = output / side / f"network-{pair:02}"
                        env = side_environment(side, result)
                        if protocol == "n2c":
                            run([DOTNET, binaries[side], "network", "n2c", args.socket, args.range], result, "n2c", env)
                            register_artifact(output, state, side, "network", result / "N2C_full_blocks.json")
                        else:
                            image = state["sides"][side]["runtimeContainerImage"]["tag"]
                            run(n2n_container_command(image, binaries[side], result, args.range), result, "n2n")
                            register_artifact(output, state, side, "network", result / "N2N_headers_and_block_fetch.json")
            state["wholePathStatus"] = "N2C measured; " + ("N2N measured" if args.n2n_container else "N2N not measured: request --n2n-container")
        finally:
            if started:
                run(["docker", "stop", "-t", "180", "chrysalis-preprod-node"], output, "node-stop")
    else:
        state["wholePathStatus"] = "not measured: whole-path node cases are Main-PC/Linux-only" if sys.platform != "linux" else "not measured: explicitly excluded by run options"


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "_exec":
        sys.exit(execute_internal(sys.argv[2:]))
    main()
