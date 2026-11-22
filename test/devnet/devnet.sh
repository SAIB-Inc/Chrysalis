#!/usr/bin/env bash
set -euo pipefail

pids=()
d=""
host=""

stop_daemons() {
  [ "${#pids[@]}" -eq 0 ] && return 0
  local pid
  for pid in "${pids[@]}"; do
    kill -TERM "$pid" 2>/dev/null || true
  done
  local end=$((SECONDS + 10))
  while [ "$SECONDS" -lt "$end" ]; do
    local alive=0
    for pid in "${pids[@]}"; do
      if kill -0 "$pid" 2>/dev/null; then
        alive=1
        break
      fi
    done
    [ "$alive" -eq 0 ] && break
    sleep 0.2
  done
  for pid in "${pids[@]}"; do
    kill -KILL "$pid" 2>/dev/null || true
  done
  for pid in "${pids[@]}"; do
    wait "$pid" 2>/dev/null || true
  done
  pids=()
}

fail_missing() {
  printf '%s\n' 'Devnet: key check missing' >&3
  return 1
}

check_lines() {
  local f=$1 want=$2 all=0 non=0
  [ -f "$f" ] || return 1
  all=$(wc -l <"$f")
  non=$(grep -c '[^[:space:]]' "$f" || true)
  [ "$all" -eq "$want" ] && [ "$non" -eq "$want" ]
}

hand_out() {
  [ -n "$d" ] && [ -d "$d/results" ] || return 0
  local n=0 p=0
  [ -f "$d/data/utxo-keys/utxo1/utxo.skey" ] && n=$((n + 1))
  [ -f "$d/wallet.skey" ] && n=$((n + 1))
  [ -f "$d/data/pools-keys/pool1/cold.vkey" ] && p=1
  check_lines "$d/keys" "$n" &&
    check_lines "$d/addresses" $((2 * n)) &&
    check_lines "$d/vkeys" $((2 * n)) &&
    check_lines "$d/pools" $((2 * p)) || { fail_missing; return 1; }
  if [ "$n" -gt 0 ] || [ "$p" -gt 0 ]; then
    {
      sed 's/.*/s|&|devnet-vkey|gI/' "$d/vkeys"
      sed 's/.*/s|&|devnet-address|gI/' "$d/addresses"
      sed 's/.*/s|&|devnet-pool|gI/' "$d/pools"
    } >"$d/replace.sed" &&
      find "$d/results" -type f ! -name '*.cbor' -exec sed -i -f "$d/replace.sed" {} + &&
      { [ ! -f "$d/console.log" ] || sed -i -f "$d/replace.sed" "$d/console.log"; } &&
      cat "$d/keys" "$d/addresses" "$d/vkeys" "$d/pools" >"$d/patterns" || { fail_missing; return 1; }
    local targets=("$d/results")
    [ -f "$d/console.log" ] && targets+=("$d/console.log")
    local g_rc=0
    grep -rqiFf "$d/patterns" "${targets[@]}" || g_rc=$?
    if [ "$g_rc" -eq 0 ]; then
      printf '%s\n' 'Devnet: key or address text found' >&3
      return 1
    elif [ "$g_rc" -ne 1 ]; then
      fail_missing
      return 1
    fi
  fi
  mkdir -p "$host" && cp -R "$d/results/." "$host/" || { fail_missing; return 1; }
  printf '%s\n' 'Devnet: key and address text absent' >&3
  if [ -f "$d/console.log" ]; then
    cat "$d/console.log" >&3
  fi
}

teardown() {
  local status=$?
  set +eu
  stop_daemons
  hand_out || { [ "$status" -eq 0 ] && status=1; }
  exit "$status"
}

trap teardown EXIT
trap 'exit 130' INT TERM

if [ "$#" -lt 5 ] || [ -z "$1" ] || [ ! -d "$2" ] || [ -z "$3" ] || [ "$4" != "--" ]; then
  printf '%s\n' 'Usage: devnet.sh <tools-dir> <ram-dir> <results-dir> -- <command...>' >&2
  exit 2
fi

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
tools=$1
d=$2
host=$3
shift 4

out="$d/results/devnet-output"
umask 077
node="$tools/cardano-node/cardano-node"
cli="$tools/cardano-node/cardano-cli"
oracle="$tools/cardano-cli/cardano-cli"
ogmios="$tools/ogmios/ogmios"
kupo="$tools/kupo/kupo"
magic=$(sed -n 's/.*"networkMagic": *\([0-9][0-9]*\).*/\1/p' "$here/shelley-spec.json")
[ -n "$magic" ]
exec 3>&1
mkdir -p "$out/logs"
: >"$d/keys"
: >"$d/addresses"
: >"$d/vkeys"
: >"$d/pools"
exec >>"$out/logs/setup.log" 2>&1

wait_for() {
  local secs=$1 step=$2 end
  shift 2
  end=$((SECONDS + secs))
  while :; do
    if "$@" >/dev/null 2>&1; then
      return 0
    fi
    if [ "$SECONDS" -ge "$end" ]; then
      printf 'Devnet: %s not ready\n' "$step" >&3
      exit 1
    fi
    sleep 0.5
  done
}

start=$(date -u -d '+5 seconds' +%Y-%m-%dT%H:%M:%SZ)
"$cli" latest genesis create-testnet-data \
  --spec-shelley "$here/shelley-spec.json" \
  --genesis-keys 1 --pools 1 --stake-delegators 1 --utxo-keys 1 \
  --total-supply 2000000000000000 --delegated-supply 1000000000000000 \
  --start-time "$start" --out-dir "$d/data" >/dev/null
sed -n 's/.*"cborHex": *"58[28]0\([0-9a-fA-F]\{64\}\).*/\1/p' "$d/data/utxo-keys/utxo1/utxo.skey" >>"$d/keys"
sed -n 's/.*"cborHex": *"\([0-9a-fA-F]\{68,\}\)".*/\1/p' "$d/data/utxo-keys/utxo1/utxo.vkey" >>"$d/vkeys"
sed -n 's/.*"cborHex": *"58[24]0\([0-9a-fA-F]\{64\}\).*/\1/p' "$d/data/utxo-keys/utxo1/utxo.vkey" >>"$d/vkeys"
pool_hex=$("$cli" latest stake-pool id --cold-verification-key-file "$d/data/pools-keys/pool1/cold.vkey" --output-format hex)
pool_bech32=$("$cli" latest stake-pool id --cold-verification-key-file "$d/data/pools-keys/pool1/cold.vkey" --output-format bech32)
printf '%s\n' "$pool_hex" "$pool_bech32" >>"$d/pools"
cp "$here/config.json" "$here/topology.json" "$d/data/"
source=$("$cli" latest address build --payment-verification-key-file "$d/data/utxo-keys/utxo1/utxo.vkey" --testnet-magic "$magic")
source_hash=$("$cli" latest address key-hash --payment-verification-key-file "$d/data/utxo-keys/utxo1/utxo.vkey")
printf '%s\n' "$source" "$source_hash" >>"$d/addresses"

(while :; do grep MemAvailable /proc/meminfo; sleep 5; done) >"$out/logs/meminfo.txt" 2>&1 3>&- &
pids+=("$!")
"$node" run \
  --config "$d/data/config.json" \
  --topology "$d/data/topology.json" \
  --database-path "$d/db" \
  --socket-path "$d/node.socket" \
  --host-addr 127.0.0.1 --port 3001 \
  --shelley-kes-key "$d/data/pools-keys/pool1/kes.skey" \
  --shelley-vrf-key "$d/data/pools-keys/pool1/vrf.skey" \
  --shelley-operational-certificate "$d/data/pools-keys/pool1/opcert.cert" \
  >"$out/logs/node.log" 2>&1 3>&- &
pids+=("$!")

node_has_tip() { timeout 10 "$cli" latest query tip --testnet-magic "$magic" --socket-path "$d/node.socket" | grep -Fq '"hash"'; }
wait_for 30 socket test -S "$d/node.socket"
wait_for 60 tip node_has_tip

"$cli" latest address key-gen --extended-key --verification-key-file "$d/wallet.vkey" --signing-key-file "$d/wallet.skey" >/dev/null
sed -n 's/.*"cborHex": *"58[68]0\([0-9a-fA-F]\{128\}\).*/\1/p' "$d/wallet.skey" >>"$d/keys"
sed -n 's/.*"cborHex": *"\([0-9a-fA-F]\{68,\}\)".*/\1/p' "$d/wallet.vkey" >>"$d/vkeys"
sed -n 's/.*"cborHex": *"58[24]0\([0-9a-fA-F]\{64\}\).*/\1/p' "$d/wallet.vkey" >>"$d/vkeys"
addr=$("$cli" latest address build --payment-verification-key-file "$d/wallet.vkey" --testnet-magic "$magic")
wallet_hash=$("$cli" latest address key-hash --payment-verification-key-file "$d/wallet.vkey")
printf '%s\n' "$addr" "$wallet_hash" >>"$d/addresses"
cbor=$(sed -n 's/.*"cborHex": *"\([0-9a-fA-F]*\)".*/\1/p' "$d/wallet.skey")
case "${cbor:0:4}" in
  5880) [ "${#cbor}" -eq 260 ]; key_hex="${cbor:4:128}"; cc_hex="${cbor:196:64}" ;;
  5860) [ "${#cbor}" -eq 196 ]; key_hex="${cbor:4:128}"; cc_hex="${cbor:132:64}" ;;
  *) exit 1 ;;
esac
printf '{"Key":"%s","Chaincode":"%s"}\n' "$key_hex" "$cc_hex" >"$d/wallet.json"

txin=$("$cli" latest genesis initial-txin --verification-key-file "$d/data/utxo-keys/utxo1/utxo.vkey" --testnet-magic "$magic")
"$cli" latest transaction build \
  --tx-in "$txin" \
  --tx-out "$addr+1000000000" \
  --tx-out "$addr+5000000" \
  --change-address "$source" \
  --testnet-magic "$magic" --socket-path "$d/node.socket" \
  --out-file "$d/setup.raw" >/dev/null
"$cli" latest transaction sign \
  --tx-body-file "$d/setup.raw" \
  --signing-key-file "$d/data/utxo-keys/utxo1/utxo.skey" \
  --testnet-magic "$magic" \
  --out-file "$d/setup.signed" >/dev/null
"$cli" latest transaction submit \
  --tx-file "$d/setup.signed" \
  --testnet-magic "$magic" --socket-path "$d/node.socket" >/dev/null
setup=$("$cli" latest transaction txid --tx-file "$d/setup.signed" --output-text)

"$ogmios" --node-socket "$d/node.socket" --node-config "$d/data/config.json" --host 127.0.0.1 --port 1337 \
  >"$out/logs/ogmios.log" 2>&1 3>&- &
pids+=("$!")
"$kupo" --node-socket "$d/node.socket" --node-config "$d/data/config.json" --host 127.0.0.1 --port 1442 \
  --since origin --match '*' --in-memory \
  >"$out/logs/kupo.log" 2>&1 3>&- &
pids+=("$!")

setup_ready() { timeout 10 "$cli" latest query utxo --tx-in "$setup#1" --testnet-magic "$magic" --socket-path "$d/node.socket" --output-json | grep -Fq "$setup#1"; }
ogmios_ready() { curl -fsS --max-time 2 http://127.0.0.1:1337/health | grep -Eq '"connectionStatus"[[:space:]]*:[[:space:]]*"connected"'; }
kupo_ready() { curl -fsS --max-time 2 -H 'Accept: application/json' http://127.0.0.1:1442/health | grep -Eq '"connection_status"[[:space:]]*:[[:space:]]*"connected"'; }
wait_for 60 setup setup_ready
wait_for 30 ogmios ogmios_ready
wait_for 30 kupo kupo_ready

export CHRYSALIS_NODE_SOCKET="$d/node.socket"
export CHRYSALIS_NETWORK_MAGIC="$magic"
node_version=$("$node" --version | awk 'NR==1 {print $2}')
[ -n "$node_version" ]
export CHRYSALIS_NODE_VERSION="$node_version"
export CHRYSALIS_CARDANO_CLI="$oracle"
export CHRYSALIS_INTEGRATION_OUTPUT="$out"
export CHRYSALIS_N2N_HOST=127.0.0.1
export CHRYSALIS_N2N_PORT=3001
export CHRYSALIS_QUERY_ADDRESS="$addr"
export CHRYSALIS_WALLET_ADDRESS="$addr"
export CHRYSALIS_WALLET_KEY_FILE="$d/wallet.json"
export CHRYSALIS_IT_COLLATERAL_TXIN="$setup#1"
export CHRYSALIS_OGMIOS_URL=http://127.0.0.1:1337
export CHRYSALIS_KUPO_URL=http://127.0.0.1:1442

printf '%s\n' 'Devnet: ready' >&3
rc=0
"$@" >"$d/console.log" 2>&1 3>&- || rc=$?
exit "$rc"
