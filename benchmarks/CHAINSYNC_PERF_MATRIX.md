# ChainSync Benchmark Results (Conway, N2C, 10k blocks)

Generated: 2026-03-01 12:21 UTC

## Test Setup

- Socket: `/home/rawriclark/CardanoPreviewFixed/CardanoPreview/ipc/node.socket`
- Magic: `2`
- Start point: slot `105324896`
- Start hash: `32bd9eb0c4af2228c730d4bf51f951e90c2ec74b01011f2e0ae0a71ae7abb24c`
- Samples: `3` sequential runs per target/mode
- Modes:
  - `with-deser`: includes block deserialization
  - `no-deser`: network path only

## How to Inspect Generated CBOR Serializer Code

The source generator in `Chrysalis.Cbor.CodeGen` emits `Read`/`Write` methods for every `[CborSerializable]` type.
To get the latest generated `.g.cs` files:

```bash
# Emit generated files to /tmp/cbor-gen (Debug build writes .g.cs; Release may not persist them)
dotnet build src/Chrysalis.Cbor/Chrysalis.Cbor.csproj -c Debug \
  /p:EmitCompilerGeneratedFiles=true \
  /p:CompilerGeneratedFilesOutputPath=/tmp/cbor-gen

# Generated files land under:
#   /tmp/cbor-gen/Chrysalis.Cbor.CodeGen/Chrysalis.Cbor.CodeGen.CborSerializerCodeGen/

# Example: inspect the ConwayBlock serializer
cat /tmp/cbor-gen/Chrysalis.Cbor.CodeGen/Chrysalis.Cbor.CodeGen.CborSerializerCodeGen/Chrysalis.Cbor.Types.Cardano.Core.ConwayBlock.Serializer.g.cs

# List all types still using try-catch fallback dispatch
grep -l "lastError = ex" /tmp/cbor-gen/Chrysalis.Cbor.CodeGen/Chrysalis.Cbor.CodeGen.CborSerializerCodeGen/*.Serializer.g.cs

# List all types using probe-based dispatch (good)
grep -l "PeekState" /tmp/cbor-gen/Chrysalis.Cbor.CodeGen/Chrysalis.Cbor.CodeGen.CborSerializerCodeGen/*.Serializer.g.cs
```

Note: `src/Chrysalis.Cbor/Chrysalis.Cbor.csproj` sets `<EmitCompilerGeneratedFiles>false</EmitCompilerGeneratedFiles>` by default.
The `/p:` overrides above enable it for a single build without modifying the csproj.

## Quick Read

- In `with-deser`, Chrysalis is still far behind Pallas, but latest serializer work nudged it up to ~44%.
- In `no-deser`, Chrysalis remains much closer to Pallas (about 88%).
- `Chrysalis-OLD` is slower than current Chrysalis in both modes.
- `perf/n2c-benchmark-align-and-network-tuning` improves `no-deser` vs `main`, but regresses `with-deser` slightly.

## Latest Validation Run (Post Generic Read Cache)

Date: `2026-03-01 12:48 UTC`
Target: `perf/n2c-benchmark-align-and-network-tuning`
Change under test: `GenericSerializationUtil` per-`T` static read delegate cache.

Raw CSV:
- Chrysalis: `/tmp/chrysalis_post_generic_cache_20260301_204722.csv`
- Pallas: `/tmp/pallas_post_generic_cache_20260301_204813.csv`

| Target | Mode | Run1 blk/s | Run2 blk/s | Run3 blk/s | Avg blk/s | StdDev | vs Pallas |
|---|---|---:|---:|---:|---:|---:|---:|
| Pallas | with-deser | 2996.1 | 2910.0 | 3083.2 | 2996.4 | 70.7 | 100.0% |
| Chrysalis | with-deser | 1354.7 | 1365.9 | 1356.9 | 1359.2 | 4.8 | 45.4% |
| Pallas | no-deser | 3151.6 | 3061.8 | 3232.4 | 3148.6 | 69.7 | 100.0% |
| Chrysalis | no-deser | 2793.0 | 2704.8 | 2721.9 | 2739.9 | 38.2 | 87.0% |

Takeaway:
- `with-deser` moved slightly up (`1350.3 -> 1359.2`, `+0.7%`).
- `no-deser` moved down in this sample set (`2787.6 -> 2739.9`, `-1.7%`), likely run variance.
- Net: this optimization is low impact on end-to-end ChainSync throughput.

## Latest Snapshot (Current Workspace)

Date: `2026-03-01 12:12-12:21 UTC`
Target: `perf/n2c-benchmark-align-and-network-tuning` after union-reader + network raw-off fast-path updates.

| Target | Mode | Run1 blk/s | Run2 blk/s | Run3 blk/s | Avg blk/s | Min-Max blk/s | StdDev | Avg MB/s | Avg time | vs Pallas |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Pallas (fresh) | with-deser | 3070.1 | 3115.8 | 2976.6 | 3054.2 | 2976.6-3115.8 | 57.9 | 6.94 | 3.30s | 100.0% |
| Chrysalis (current, confirm set) | with-deser | 1347.3 | 1375.5 | 1328.2 | 1350.3 | 1328.2-1375.5 | 19.4 | 3.08 | 7.40s | 44.2% |
| Pallas (fresh) | no-deser | 3263.2 | 3129.5 | 3128.6 | 3173.8 | 3128.6-3263.2 | 63.2 | 7.21 | 3.17s | 100.0% |
| Chrysalis (current, confirm set) | no-deser | 2716.4 | 2854.7 | 2791.8 | 2787.6 | 2716.4-2854.7 | 56.5 | 6.35 | 3.60s | 87.8% |

Notes:
- Immediate first pass after build had one cold `with-deser` outlier (`1016.5 blk/s`), so the table above uses the follow-up confirmation x3 set.
- `with-deser` improved vs previous iteration C, while `no-deser` remained effectively flat.

## Serializer Optimization Iterations (Current Branch)

Date: `2026-03-01 11:58-12:08 UTC`
Target: `perf/n2c-benchmark-align-and-network-tuning`
Samples: `3` runs per mode per iteration.

| Iteration | Main change | with-deser avg blk/s | no-deser avg blk/s |
|---|---|---:|---:|
| A | Generator micro-opts (`fieldsRead` bitset, pre-size list/map, `TryAdd`, reader-path preserve pruning) | 1200.3 | 2793.7 |
| B | `GenericSerializationUtil.Read<T>(reader)` uses cached `Read(CborReader)` delegates before encoded fallback | 1235.3 | 2750.2 |
| C | Union fast path for list-container definite/indefinite tagged/untagged variants (single-pass reader parse, no encoded-value union fallback) | 1323.4 | 2790.8 |
| D | Union `Read(CborReader)` enablement + structural-probe direct reader dispatch + `CborSerializer.DeserializeWithoutRaw` in `ChannelBuffer` | 1350.3 | 2787.6 |

Delta summary:
- D vs C (`with-deser`): `+26.9 blk/s` (`+2.0%`)
- D vs C (`no-deser`): `-3.2 blk/s` (`-0.1%`, effectively flat)
- D vs A (`with-deser`): `+150.0 blk/s` (`+12.5%`)

Relative to fresh Pallas baseline in this report:
- `with-deser`: `1350.3 / 3054.2 = 44.2%`
- `no-deser`: `2787.6 / 3173.8 = 87.8%`

## No-Deser Stability Check (10x, Current Workspace)

Date: `2026-03-01 11:37 UTC`
Target: `perf/n2c-benchmark-align-and-network-tuning`
Mode: `--no-deser` only, `10` sequential runs.

| Metric | Value |
|---|---:|
| Mean blk/s (raw 10x) | 2647.7 |
| Median blk/s | 2777.1 |
| StdDev blk/s (raw 10x) | 403.3 |
| Min-Max blk/s | 1438.6-2815.4 |
| Trimmed mean blk/s (exclude min/max) | 2777.9 |

Raw runs (blk/s):
`2774.1, 2781.0, 2774.1, 2780.1, 1438.6, 2815.4, 2798.2, 2764.1, 2762.0, 2789.8`

Interpretation:
- One severe outlier (`1438.6`) dominates the raw mean.
- Steady-state no-deser throughput is still around `~2.78k blk/s` when the outlier is discounted.

Pallas 10x reference (same session/config):

| Metric | Value |
|---|---:|
| Mean blk/s (raw 10x) | 3136.2 |
| Median blk/s | 3131.8 |
| StdDev blk/s (raw 10x) | 33.5 |
| Min-Max blk/s | 3096.8-3218.0 |
| Trimmed mean blk/s (exclude min/max) | 3130.9 |

Raw runs (blk/s):
`3103.1, 3115.8, 3130.7, 3114.8, 3096.8, 3163.8, 3133.0, 3149.6, 3218.0, 3136.8`

Direct comparison (10x trimmed mean):

| Ratio | Value |
|---|---:|
| Chrysalis / Pallas (trimmed) | 88.7% |

## Results: With Deserialization

| Implementation | Avg blk/s | Avg MB/s | Avg time | vs Pallas |
|---|---:|---:|---:|---:|
| Pallas (reference) | 3024.5 | 6.88 | 3.30s | 100.0% |
| Chrysalis (main) | 1274.6 | 2.90 | 7.83s | 42.1% |
| Chrysalis (perf branch) | 1221.5 | 2.78 | 8.17s | 40.4% |
| Chrysalis-OLD | 1115.5 | 2.54 | 8.97s | 36.9% |

Stability (min-max blk/s):

| Implementation | Min-Max blk/s |
|---|---:|
| Pallas (reference) | 2973.6-3103.8 |
| Chrysalis (main) | 1265.5-1283.2 |
| Chrysalis (perf branch) | 1213.9-1235.2 |
| Chrysalis-OLD | 1088.9-1135.6 |

## Results: No Deserialization

| Implementation | Avg blk/s | Avg MB/s | Avg time | vs Pallas |
|---|---:|---:|---:|---:|
| Pallas (reference) | 3190.1 | 7.25 | 3.17s | 100.0% |
| Chrysalis (perf branch) | 2805.9 | 6.39 | 3.57s | 88.0% |
| Chrysalis (main) | 2752.3 | 6.27 | 3.63s | 86.3% |
| Chrysalis-OLD | 2683.4 | 6.11 | 3.73s | 84.1% |

Stability (min-max blk/s):

| Implementation | Min-Max blk/s |
|---|---:|
| Pallas (reference) | 3145.4-3263.9 |
| Chrysalis (perf branch) | 2789.8-2836.7 |
| Chrysalis (main) | 2731.1-2769.4 |
| Chrysalis-OLD | 2638.8-2728.7 |

## Key Deltas

### Perf Branch vs Main (Current Chrysalis)

| Mode | Delta blk/s | Relative |
|---|---:|---:|
| with-deser | -53.1 | -4.2% |
| no-deser | +53.6 | +1.9% |

### Current Main vs OLD

| Mode | Delta blk/s | Relative |
|---|---:|---:|
| with-deser | +159.1 | +14.3% |
| no-deser | +68.9 | +2.6% |

## Raw Runs (blk/s)

| Target | Mode | Run1 | Run2 | Run3 |
|---|---|---:|---:|---:|
| Chrysalis (main) | with-deser | 1283.2 | 1275.2 | 1265.5 |
| Chrysalis (main) | no-deser | 2769.4 | 2756.5 | 2731.1 |
| Chrysalis (perf branch) | with-deser | 1213.9 | 1235.2 | 1215.5 |
| Chrysalis (perf branch) | no-deser | 2791.1 | 2789.8 | 2836.7 |
| Chrysalis-OLD | with-deser | 1122.0 | 1088.9 | 1135.6 |
| Chrysalis-OLD | no-deser | 2682.6 | 2638.8 | 2728.7 |
| Pallas (reference) | with-deser | 2996.1 | 3103.8 | 2973.6 |
| Pallas (reference) | no-deser | 3145.4 | 3160.9 | 3263.9 |

## Notes

- `ChainSyncBenchOld` parser was updated in this workspace so `--no-deser` is now correctly recognized as a flag.
- Runs were executed sequentially to avoid cross-process contention.
