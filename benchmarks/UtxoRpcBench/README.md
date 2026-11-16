# UTxO RPC benchmark

Run in Release with `dotnet run --project benchmarks/UtxoRpcBench -c Release -- --filter '*' --artifacts <directory>`.
The ShortRun job uses InProcessEmitToolchain because BenchmarkDotNet 0.15.8's
out-of-process SDK validation does not recognize net11.0. MemoryDiagnoser and
the Markdown and full JSON exporters stay enabled.

There are no live calls. Two captures are embedded from the test project's golden
folder without a second copy. Setup repeats search items in order to 100 and 1,000;
the recorded input remains unchanged. Both search cases call the shipped output
decoder for every item; full parsing reuses one projection without an extra wrapper.
Lean and parameters use the shipped contextual sequence deserializers; the array accessor throws.

## Shared x64 reference

2026-11-16; MR 4 uncommitted atop `e445d68`. Timings are noisy, not a timing gate.
BenchmarkDotNet 0.15.8; Linux Ubuntu 24.04.5 LTS (Noble Numbat).
AMD Ryzen 9 9900X3D 4.40GHz; 1 CPU, 24 logical and 12 physical cores.
.NET SDK 11.0.100; .NET 11.0.0 (11.0.0, 11.0.26.53018), X64 RyuJIT x86-64-v4.
GC: Concurrent Workstation. ShortRun: 1 launch, 3 warmups, 3 measured iterations.
Run 1 load (1-minute): 11.09 before, 10.78 after; run 2: 10.78 before, 9.67 after.
Load brackets include the wait for a shared-machine slot.

| Method | Input | Mean (us, noisy) | Run 1 B/op (two-run range if varied) |
|---|---|---:|---:|
| SearchUtxosLean | Recorded | 1.517 | 10,840 |
| SearchUtxosFull | Recorded | 7.086 | 42,120 |
| SearchUtxosLean | 100 | 77.747 | 536,176 |
| SearchUtxosFull | 100 | 453.301 | 2,092,730 |
| SearchUtxosLean | 1000 | 1,216.741 | 5,356,590 (5,356,587–5,356,590) |
| SearchUtxosFull | 1000 | 7,781.975 | 20,920,762 |
| ReadParams | Recorded | 52.015 | 80,352 (80,352–80,353) |

Both runs completed all seven workloads; lean allocated less than full on every input.
The 1 and 3 B/op differences come from per-iteration extra allocations (0 to about
5.3 KB), divided by the operation count chosen from timings. At equal operation
counts the total bytes were identical. Lean/1000 is 5,356,584 B/call + 3,312 B/iteration
(512 versus 1,024 operations); ReadParams is 80,352 B/call + 4,992 B/iteration
(16,384 versus 8,192 operations). See each run's
`UtxoRpcSearchBenchmarks-report-full.json` and `UtxoRpcParameterBenchmarks-report-full.json`
(`Memory.TotalOperations`) and the corresponding benchmark logs' `// GC:` totals.

The benchmark is gated on bytes allocated per call, not timings.
Its baseline is the first run on the project's MacBook Pro measuring machine at the next alpha after merge.
Until those measuring runs take it in, changes to the provider rerun this benchmark
and compare x64 B/op with this reference within a tolerance of 5,400 divided by the
run's `Memory.TotalOperations` in the full JSON, or compare at a fixed invocation
count on both machines.
