# ReplayClient

Runs the existing benchmark harness's fixed TCP N2N or offline N2N/N2C replay, recording
per-pass time, allocations, GC/JIT observations, and loaded assemblies in JSON.

Build with `heavy dotnet build benchmarks/ReplayClient/ReplayClient.csproj -c Release -warnaserror -m:8`.
Use the shared work-local NuGet cache and the repository's SDK.

Command: `ReplayClient tcp|offline|offline-n2c <output.json> <warmup> <measured> [--observed]`.
Both counts must be positive decimal integers; their sum must be below Int32.MaxValue.
For a local smoke check, use 1 warm-up and 25 measured passes. PC times are not measurements.

TCP uses the recorded preprod replay server at 127.0.0.2:3001 and `P1_RANGE`
pointing to its verified 1,000-block `range.json`. The server takes `--capture`.
Offline uses the original 30-block fixtures, repeated 34 times per pass;
`CHRYSALIS_BENCHMARK_DATA` can name their fixture root. A later change adds an optional manifest argument.

No process environment is recorded. Runtime, AppContext and GC fields are retained.
Credential-bearing variable names are refused, case-insensitively: names containing
TOKEN, SECRET, PASSWORD or API_KEY, or ending in _KEY. No values are printed.
The client runs on its own. Opt-in `--observed` emits acknowledged measured-window
marks outside timed passes; a missing answer exits 3 within a 30-second wait,
without writing a result.
