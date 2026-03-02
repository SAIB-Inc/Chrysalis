#!/usr/bin/env bash
set -euo pipefail

SOCKET="${SOCKET:-/home/rawriclark/CardanoPreviewFixed/CardanoPreview/ipc/node.socket}"
TCP_HOST="${TCP_HOST:-127.0.0.1}"
TCP_PORT="${TCP_PORT:-3001}"
SLOT="${SLOT:-103560749}"
HASH="${HASH:-d80f54dd22aace85cdb4b57e775e8e892289f23fe6e1767e8c752ec02d014cb8}"
BLOCKS="${BLOCKS:-10000}"
MAGIC="${MAGIC:-2}"
N2N_TIMEOUT="${N2N_TIMEOUT:-30}"
PIPELINE="${PIPELINE:-100}"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PALLAS_BIN="$REPO_ROOT/benchmarks/PallasChainSyncBench/target/release/pallas-chainsync-bench"
GOUROBOROS_BIN="$REPO_ROOT/benchmarks/GouroborosChainSyncBench/GouroborosChainSyncBench"

echo "========================================="
echo " ChainSync Benchmark Suite"
echo "========================================="
echo "  Socket:    $SOCKET"
echo "  TCP:       $TCP_HOST:$TCP_PORT"
echo "  Slot:      $SLOT"
echo "  Hash:      ${HASH:0:16}..."
echo "  Blocks:    $BLOCKS"
echo "  Magic:     $MAGIC"
echo "  Pipeline:  $PIPELINE"
echo "  N2N timeout: ${N2N_TIMEOUT}s"
echo "========================================="
echo

echo "========================================="
echo " N2C (Unix Socket) — $BLOCKS blocks"
echo "========================================="
echo

# 1. Chrysalis N2C + deser
echo "=== 1/9: Chrysalis N2C + deser ==="
dotnet run --project "$REPO_ROOT/benchmarks/ChainSyncBench" -c Release -- \
  --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC"
echo

# 2. Chrysalis N2C no-deser
echo "=== 2/9: Chrysalis N2C no-deser ==="
dotnet run --project "$REPO_ROOT/benchmarks/ChainSyncBench" -c Release -- \
  --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC" --no-deser
echo

# 3. Pallas N2C + deser
echo "=== 3/9: Pallas N2C + deser ==="
"$PALLAS_BIN" --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC"
echo

# 4. Pallas N2C no-deser
echo "=== 4/9: Pallas N2C no-deser ==="
"$PALLAS_BIN" --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC" --no-deser
echo

# 5. Gouroboros N2C
echo "=== 5/9: Gouroboros N2C ==="
"$GOUROBOROS_BIN" --socket "$SOCKET" --magic "$MAGIC" --blocks "$BLOCKS"
echo

echo "========================================="
echo " N2N (TCP) — from origin, ${N2N_TIMEOUT}s each"
echo "========================================="
echo

# 6. Chrysalis N2N pipelined + deser
echo "=== 6/9: Chrysalis N2N pipelined + deser (pipeline $PIPELINE) ==="
timeout "$N2N_TIMEOUT" dotnet run --project "$REPO_ROOT/src/Chrysalis.Network.Cli" -c Release -- \
  chainsync --tcp-host "$TCP_HOST" --tcp-port "$TCP_PORT" --magic "$MAGIC" --pipeline "$PIPELINE" || true
echo

# 7. Chrysalis N2N pipelined headers only (no BlockFetch)
echo "=== 7/9: Chrysalis N2N pipelined headers only (pipeline $PIPELINE) ==="
timeout "$N2N_TIMEOUT" dotnet run --project "$REPO_ROOT/src/Chrysalis.Network.Cli" -c Release -- \
  chainsync --tcp-host "$TCP_HOST" --tcp-port "$TCP_PORT" --magic "$MAGIC" --pipeline "$PIPELINE" --headers-only || true
echo

# 8. Pallas N2N sequential + deser
echo "=== 8/9: Pallas N2N sequential + deser ==="
timeout "$N2N_TIMEOUT" "$PALLAS_BIN" --tcp-host "$TCP_HOST" --tcp-port "$TCP_PORT" --blocks "$BLOCKS" --magic "$MAGIC" || true
echo

# 9. Gouroboros N2N pipelined headers only
echo "=== 9/9: Gouroboros N2N pipelined headers (pipeline 100) ==="
timeout "$N2N_TIMEOUT" "$GOUROBOROS_BIN" --tcp-host "$TCP_HOST" --tcp-port "$TCP_PORT" --magic "$MAGIC" --blocks "$BLOCKS" || true
echo

echo "========================================="
echo " Benchmark suite complete."
echo "========================================="
