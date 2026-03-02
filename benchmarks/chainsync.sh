#!/usr/bin/env bash
set -euo pipefail

SOCKET="${SOCKET:-/home/rawriclark/CardanoPreviewFixed/CardanoPreview/ipc/node.socket}"
SLOT="${SLOT:-103560749}"
HASH="${HASH:-d80f54dd22aace85cdb4b57e775e8e892289f23fe6e1767e8c752ec02d014cb8}"
BLOCKS="${BLOCKS:-10000}"
MAGIC="${MAGIC:-2}"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PALLAS_BIN="$REPO_ROOT/benchmarks/PallasChainSyncBench/target/release/pallas-chainsync-bench"

echo "========================================="
echo " ChainSync Benchmark Suite"
echo "========================================="
echo "  Socket:  $SOCKET"
echo "  Slot:    $SLOT"
echo "  Hash:    ${HASH:0:16}..."
echo "  Blocks:  $BLOCKS"
echo "  Magic:   $MAGIC"
echo "========================================="
echo

# 1. Chrysalis N2C + deser
echo "=== 1/6: Chrysalis N2C + deser ==="
dotnet run --project "$REPO_ROOT/benchmarks/ChainSyncBench" -c Release -- \
  --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC"
echo

# 2. Chrysalis N2C no-deser
echo "=== 2/6: Chrysalis N2C no-deser ==="
dotnet run --project "$REPO_ROOT/benchmarks/ChainSyncBench" -c Release -- \
  --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC" --no-deser
echo

# 3. Chrysalis OLD N2C + deser
echo "=== 3/6: Chrysalis OLD N2C + deser ==="
dotnet run --project "$REPO_ROOT/benchmarks/ChainSyncBenchOld" -c Release -- \
  --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC"
echo

# 4. Chrysalis OLD N2C no-deser
echo "=== 4/6: Chrysalis OLD N2C no-deser ==="
dotnet run --project "$REPO_ROOT/benchmarks/ChainSyncBenchOld" -c Release -- \
  --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC" --no-deser
echo

# 5. Pallas N2C + deser
echo "=== 5/6: Pallas N2C + deser ==="
"$PALLAS_BIN" --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC"
echo

# 6. Pallas N2C no-deser
echo "=== 6/6: Pallas N2C no-deser ==="
"$PALLAS_BIN" --socket "$SOCKET" --slot "$SLOT" --hash "$HASH" --blocks "$BLOCKS" --magic "$MAGIC" --no-deser
echo

echo "========================================="
echo " ChainSync benchmarks complete."
echo "========================================="
