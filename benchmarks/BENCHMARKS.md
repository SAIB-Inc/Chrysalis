# ChainSync Benchmark Results

## Environment

- **CPU:** AMD Ryzen 9 9900X3D
- **Runtime:** .NET 10, Rust (release), Go 1.25
- **Node:** Cardano Preview testnet (local)
- **Network magic:** 2
- **Block range:** Conway-era blocks (slot ~103M for N2C, from origin for N2N)

## N2C (Unix Socket) — 10,000 blocks

All implementations use sequential request-response ChainSync over a local Unix socket. Blocks are full block bodies (~2 KB avg).

| Implementation | With Deserialization | Network Only (no deser) |
|---|---|---|
| **Pallas (Rust)** | 3,097 blk/s | 3,280 blk/s |
| **Chrysalis (.NET)** | 2,747 blk/s | 2,977 blk/s |
| **Gouroboros (Go)** | 2,735 blk/s | — |

- Chrysalis achieves **89% of Rust** on N2C with full deserialization
- Deserialization overhead is ~8% for both Chrysalis and Pallas
- Gouroboros does not expose a no-deser mode

## N2N (TCP) — 30 seconds from origin

N2N ChainSync delivers headers only; full blocks require a separate BlockFetch request. Pipelining sends multiple requests before waiting for responses, dramatically reducing round-trip overhead.

### Headers Only (apples-to-apples)

| Implementation | Mode | blk/s | Notes |
|---|---|---|---|
| **Chrysalis (.NET)** | Pipelined (depth 100) headers only | **~40,000** | Header parsing + hash computation |
| **Gouroboros (Go)** | Pipelined (depth 100) headers only | ~14,500 | Header parsing via callbacks |

- **Chrysalis is 2.8x faster than Gouroboros** on raw header throughput
- Both implementations use the same pipeline depth (100) and parse headers to extract slot/hash

### Full Block Download

| Implementation | Mode | blk/s | Notes |
|---|---|---|---|
| **Chrysalis (.NET)** | Pipelined (depth 100) + BlockFetch + deser | **~9,500** | Full blocks downloaded and deserialized |
| **Pallas (Rust)** | Sequential + BlockFetch + deser | ~722 | No pipelining support |

- **Chrysalis is 13x faster than Pallas** on N2N with full block download
- Gouroboros cannot run concurrent ChainSync + BlockFetch due to its callback architecture (node message queue overflow)
- Pallas does not implement ChainSync pipelining, resulting in one round-trip per block

## Key Takeaways

1. **N2C is bottlenecked by the node**, not the client — all three implementations converge around 2,700–3,300 blk/s
2. **Pipelining is the dominant factor for N2N** — it eliminates per-block round-trip latency
3. **Chrysalis is 2.8x faster than Go on headers** — .NET's async pipeline and zero-copy parsing outperform Go's goroutine-based callbacks
4. **Chrysalis's batch burst pattern** (send N requests → drain N responses → BlockFetch the batch) achieves ~9,500 blk/s with full block deserialization
5. **Deserialization cost is negligible** relative to network I/O (~8% overhead on N2C)

## Reproducing

```bash
# Build prerequisites
cd benchmarks/PallasChainSyncBench && cargo build --release
cd benchmarks/GouroborosChainSyncBench && go build

# Run full suite
./benchmarks/chainsync.sh
```

Configure via environment variables: `SOCKET`, `TCP_HOST`, `TCP_PORT`, `SLOT`, `HASH`, `BLOCKS`, `MAGIC`, `PIPELINE`, `N2N_TIMEOUT`.
