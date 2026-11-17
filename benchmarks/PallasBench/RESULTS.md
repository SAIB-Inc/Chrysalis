# Comparison results

Informational only; no pass mark. Larger Chrysalis/Rust ratios mean Chrysalis took longer.

One counted PC session on 17 November 2026, starting 2026-11-17T16:28:09Z.
PC loadavg (1 / 5 / 15 min): start 12.25 / 15.63 / 18.37; end 18.32 / 15.23 / 15.91.
Linux / x86_64, AMD Ryzen 9 9900X3D 12-Core Processor.
Five independent process pairs per workload, alternating AB/BA order. Raw samples and
versions are in `comparison.json`; this table is derived from those samples.

| Matched work | Blocks/iteration | Iterations/process | Chrysalis, µs/block | Rust, µs/block | Chrysalis/Rust | 95% interval |
|---|---:|---:|---:|---:|---:|---:|
| Every-field Chrysalis / typed Pallas decode | 30 | 6000 | 220.89 | 20.04 | 11.07× | 10.09–12.05× |
| SAIB.Cbor reader / minicbor walk | 30 | 20000 | 11.54 | 6.88 | 1.68× | 1.59–1.76× |
| N2C replay peer, not live-node throughput | 29 | 1000 | 199.58 | 227.32 | 0.88× | 0.77–0.99× |

Ratios/intervals use G8’s existing arithmetic mean of paired process ratios and
two-sided Student-t 95% interval, not the ratio of the displayed mean times.
The interval describes this noisy shared-PC session, not a cross-machine claim.

## Matched work

- Decode: all 30 pinned Conway blocks, 164 transactions; Chrysalis reads its 129
  concrete schema properties through G8’s linked every-field visitor, while Pallas
  eagerly decodes its typed block. Rust’s timed result is black-boxed and dropped.
- Reader: the same 30 block byte strings; both readers and the independent Python
  oracle count exactly 21,051 logical CBOR items per iteration, with complete input
  consumption. No ledger objects are constructed. See `README.md` for tag/string rules.
- N2C: both actual clients negotiate v16, intersect at slot 139055739, and own the
  exact same 29 successors through slot 139056433 (159 transactions per range).
  All 10 peer connections completed their recorded variable warmups plus
  1000 measured ranges each (57181 complete ranges in total).
  Exact bytes, SHA-256, slot/header hash and transaction counts are checked outside
  timing after every range. Only complete ranges/frames may end with clean EOF;
  no arbitrary flush sleep or additional timed hand-off was added.

## Limits and substitution

Pair (3) is a **replay peer, not live-node throughput**. Point 62 named a live node;
with coordinator approval this substitutes a reproducible out-of-process Unix-socket
replay so both sides do matched work without a node being the limiting factor.
No live node or Docker was started. The stored 30-block corpus includes the anchor,
so there are exactly 29 stored successors, not 30; this count correction was approved.
The Python peer, scheduler, socket and per-block ownership costs remain in this
result. It must not be presented as the live-node ratio or the twofold goal’s verdict.

Pallas 1.4.0; direct minicbor 2.3.1; SAIB.Cbor 2.0.0;
.NET SDK 11.0.100 / .NET 11.0.0, workstation GC;
rustc 1.99.1 (58cf8f9c3 2026-10-13), release profile; Python 3.12.3. Full transitive Rust versions/checksums
are pinned in `Cargo.lock`. Official crates were downloaded once to the update’s
benchmark-only cache; no toolchain upgrade or system-wide installation was needed.
Rust uses Tokio’s default multi-thread runtime, matching the existing Pallas N2C
comparator (D13), not a current-thread runtime. Replay uses preprod magic 1.
The N2C ratio depends on the Pallas client’s Tokio runtime: its current-thread client took 142.01 µs/block in the superseded session versus 227.32 µs/block for default multi-thread now, while Rust’s typed row stayed level, so 0.88× must not be read as ‘Chrysalis’s N2C is faster than Pallas’.
Earlier fixed-one-second-warmup figures are superseded: unsettled JIT. They and
functional red/smoke runs are retained in review evidence, not used in this table.
No previous figures were substituted; only the accepted five-pair session supplies this table.

## JIT and window qualification

C# repeats the actual operation/driver until the compiled-method count stays fixed
for 10 s, after at least 20 s warming; the cap is 120 s.
A cap failure produces an error and no counted sample. Tiering and PGO are not disabled.
Each accepted process window is at least 1 s. Every C# start/end snapshot records
JitInfo’s total compiled-method count, compiled IL bytes and compilation time ticks.
The approved information-only rule accepts at most 0.1% recorded JIT time and
1000 compiled IL bytes per window; exceeding either limit rejects the pair.
Equality is accepted. These are recorded compilation deltas, not a claim of zero JIT
or a direct measurement of all its timing effects. No separate Tier1 counter is claimed.
For replay the counters conservatively bracket the entire range sequence, including
its untimed checks/intersections; elapsed time sums only the receive/ownership timers.

| Work / process | Methods compiled | IL bytes compiled | Compilation ticks (100 ns) | JIT time / window, % |
|---|---:|---:|---:|---:|
| typed / 1 | 2 | 242 | 8917 | 0.002218 |
| typed / 2 | 2 | 242 | 10610 | 0.002551 |
| typed / 3 | 2 | 242 | 8022 | 0.002169 |
| typed / 4 | 2 | 242 | 7916 | 0.002136 |
| typed / 5 | 2 | 242 | 8797 | 0.002048 |
| walk / 1 | 0 | 0 | 0 | 0.000000 |
| walk / 2 | 0 | 0 | 0 | 0.000000 |
| walk / 3 | 0 | 0 | 0 | 0.000000 |
| walk / 4 | 0 | 0 | 0 | 0.000000 |
| walk / 5 | 0 | 0 | 0 | 0.000000 |
| replay / 1 | 4 | 309 | 6250 | 0.011855 |
| replay / 2 | 3 | 301 | 4993 | 0.009277 |
| replay / 3 | 3 | 301 | 5546 | 0.010499 |
| replay / 4 | 3 | 301 | 8590 | 0.013222 |
| replay / 5 | 10 | 504 | 28632 | 0.044009 |

The typed row retains G8’s unchanged opaque visitor, including its pre-existing
tag/checksum defect; the separate helper repair is not part of X1.
