# Pallas and minicbor comparison

G8b compares three matched workloads on the shared G8 corpus. `RESULTS.md`
and `comparison.json` contain the measured results; there is no pass mark.
Nothing in this folder is a Chrysalis package dependency.

- `typed`: G8's actual every-field Chrysalis operation, linked rather than
  copied (`ModelGraph` and its opaque-value visitor), against Pallas's eager
  `MultiEraBlock::decode`. Both decode all 30 Conway blocks. This is not a
  comparison against Chrysalis's cheap lazy root construction. Pallas's
  `KeepRaw` decodes its inner typed value while borrowing the original bytes.
- `walk`: SAIB.Cbor `CborReader` against the direct minicbor dependency, visiting
  the same 21,051 logical data items across those 30 blocks, without ledger
  objects. Tags and container headers count; break markers and physical string
  chunks do not. Tag-24 byte strings remain bytes on both sides. An independent
  existing Python CBOR decoder supplies the expected count. Both reject trailing
  bytes. Text is read as raw UTF-8 bytes by SAIB.Cbor, as borrowed validated
  UTF-8 by minicbor; this valid corpus is not a UTF-8 validation benchmark.
- `replay`: **replay peer, not live-node throughput**. Both real N2C clients use
  the same external Python Unix-socket peer, negotiate v16 with network magic 1,
  intersect at slot 139055739 and fetch its 29 stored contiguous successors.
  The anchor is fixture 00, not a thirtieth successor. Each client owns the
  returned bytes; Chrysalis copies its borrowed payload before the next receive,
  Pallas already returns an owned vector. Handshake/intersection, array setup,
  JIT-settled warmup ranges, and checking exact bytes/points/transaction counts/
  SHA-256 digests are outside timing. RequestNext, mux, receive, protocol decode
  and ownership are inside. Both check **every** range before re-intersecting.
  The peer sends identical tag-24 messages in at most 16 KiB mux fragments.

All pairs verify decoded slot/hash/transaction count and SHA-256 against G8's
existing manifests. The runner aborts on unequal work or a failed process/peer;
it never substitutes an older result. Raw outputs and peer counts go in `out/`.
Five independent process pairs alternate AB/BA order. C# repeats its actual driver
for at least 20 s and until the compiled-method count is unchanged for 10 s, with
a 120 s cap; failure to settle produces an error, not a counted sample. Tiering/PGO
remain enabled. Every process window must time at least 1 s, and every C# window
records JitInfo compiled-method, compiled-IL-byte and compilation-time counters at
start/end. The information-only rule rejects a pair if recorded compilation time
exceeds 0.1% of its window or compiled IL exceeds 1,000 bytes; equality is accepted.
All method/IL/time deltas are recorded in the table, not hidden. For replay the counters conservatively
cover the complete measured range sequence including untimed validation. Rust uses
Tokio’s default multi-thread runtime, as the existing Pallas N2C comparator does.
The replay measurement token starts only after warmup, with a limit derived from
its cap (120 s). The runner obtains these limits from the binary’s untimed check
mode: process budget = warmup cap + measured-window limit + 30 s startup/output
margin (270 s). Bootstrap/version tools have a separate 30 s bound.
Fixture load, validation and visitor compilation are untimed. Ratios are Chrysalis/Rust process totals for equal iterations, with
G8's existing paired-ratio Student-t 95% interval. This informational run is not
P2's allocation or Fly timing gate, and shared-machine noise remains a limit.

## Run

Use the update's private NuGet/Cargo caches and `heavy` on the PC. The Rust
crates were downloaded once from crates.io, pinned by `Cargo.lock`; subsequent
builds/tests are offline. No toolchain update, global install, node, Docker,
network capture or paid service is needed. Defaults are 6,000 typed iterations, 20,000 reader iterations and 1,000 replay
ranges per process. Loadavg is recorded at session start/end. Use a new output
directory each run:

```sh
heavy cargo build --manifest-path benchmarks/PallasBench/Cargo.toml --release --offline --locked -j8
heavy dotnet build benchmarks/PallasBench/chrysalis/Comparison.csproj -c Release -warnaserror -m:8
heavy python3 -B benchmarks/PallasBench/compare.py --out benchmarks/PallasBench/out/new-run
python3 -B benchmarks/PallasBench/render-results.py benchmarks/PallasBench/out/new-run
```

`python3 -B -m unittest discover -s benchmarks/PallasBench` tests matched-work
rejection and the real replay handshake/framing/ranges. `cargo test --release
--offline --locked --manifest-path benchmarks/PallasBench/Cargo.toml` tests the
Rust walker. The C# binary's `check` mode (`<fixture-project> check 1 unused`)
runs untimed typed-field and raw-walker golden checks, including tagged children
of indefinite containers. Non-measurement iterations use the Mac build wrapper;
the fix-round PC gate runs only X1’s own build, tests, corpus/golden and scope
checks, reusing the earlier solution-wide evidence instead of repeating it.
