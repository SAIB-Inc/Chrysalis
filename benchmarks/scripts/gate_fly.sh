#!/usr/bin/env bash
# Future launch only after a separate explicit GO; never run in preparation.
set +x
set -euo pipefail
[[ $# == 3 && "$1" == --approved ]] || { echo 'Usage: gate_fly.sh --approved OWN_APP seal.json'; exit 2; }
APP="$2"; READY="$(realpath "$3")"; HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
[[ "$APP" =~ ^chrysalis-gate-[0-9]{8}t[0-9]{6}-[a-f0-9]{8}$ ]] || { echo 'Refusing non-run app'; exit 2; }
source "$HERE/gate_fly_common.sh"
if [[ -e /proc/self/fd/9 ]]; then
  [[ "$(readlink /proc/self/fd/9)" == "$LOCK" ]] || { echo 'Wrong inherited lock descriptor'; exit 2; }
else
  exec 9>"$LOCK"
fi
flock --nonblock 9 || { echo 'Another Fly session holds the lock'; exit 75; }
VERIFIED_INPUTS="$(python3 "$HERE/gate_fly_guard.py" sealed "$HERE" "$READY")"
mapfile -t SEALED_VALUES < <(python3 -c 'import json,sys; d=json.loads(sys.argv[1]); [print(d[k]) for k in ("bundle","sha256","machineCommand","guard","cutoff","version","image","region")]' "$VERIFIED_INPUTS")
[[ ${#SEALED_VALUES[@]} == 8 ]] || { echo 'Invalid verified launch inputs'; exit 2; }
BUNDLE="${SEALED_VALUES[0]}"; BUNDLE_SHA="${SEALED_VALUES[1]}"; VM_COMMAND="${SEALED_VALUES[2]}"
GUARD="${SEALED_VALUES[3]}"; CUTOFF="${SEALED_VALUES[4]}"; VERSION="${SEALED_VALUES[5]}"; BASE="${SEALED_VALUES[6]}"
REGION="${SEALED_VALUES[7]}"
CREATED_UNIX=0
[[ "$(env -i HOME="$HOME" PATH="$PATH" FLY_NO_UPDATE_CHECK=1 FLY_NO_ANALYTICS=1 timeout 10 fly version </dev/null 9>&-)" == "$VERSION" ]] || { echo 'flyctl version changed since seal'; exit 2; }
NOTE="$(dirname "$READY")/time/runs/$APP"
[[ ! -e "$NOTE" ]] || { echo 'Run folder already exists'; exit 2; }
if [[ -n "${GATE_FIRST_GO:-}" ]]; then
  [[ -r "$TOKEN_FILE" && -s "$TOKEN_FILE" ]] || { echo 'Fly token file missing or empty; no stored-login fallback' >&2; exit 2; }
  python3 "$HERE/gate_fly_guard.py" lock-inode "$LOCK"
  mkdir -p "$(dirname "$READY")/time"
  python3 "$HERE/gate_fly_guard.py" consume-go "$READY" "$GATE_FIRST_GO"
fi
exec </dev/null # Do not pass the local retry dispatch descriptor to provider children.
mkdir -p "$NOTE"
CREATED_UNIX=0; CAN_CLEAN=0; APP_CREATE_ATTEMPTED=0; WATCHDOG=''; MACHINE=''; RESULTS_MAY_EXIST=0; COPIED=0

stop_watchdog() {
  if [[ -n "$WATCHDOG" ]]; then
    if ! python3 "$HERE/gate_fly_guard.py" watchdog-pid "$WATCHDOG" "$HERE/gate_fly_watchdog.sh" "$APP" "$NOTE" "$CREATED_UNIX" "$HERE" "$GUARD"; then
      echo 'Watchdog PID is gone or no longer our exact watchdog; not signalling it' >&2
      WATCHDOG=''
      return 0
    fi
    kill -TERM "$WATCHDOG" 2>/dev/null || true
    for ((attempt=0; attempt<40; attempt++)); do
      [[ -r "/proc/$WATCHDOG/stat" ]] || break
      read -r _ _ state _ < "/proc/$WATCHDOG/stat" || break
      [[ "$state" != Z ]] || break
      sleep .05
    done
    WATCHDOG=''
  fi
}
recover_partial() {
  [[ "$RESULTS_MAY_EXIST" == 1 && "$COPIED" == 0 && -n "$MACHINE" ]] || return 0
  if (( $(date +%s) >= CREATED_UNIX + CUTOFF + 200 )); then
    echo 'Partial-result copy deadline expired; diagnostic data unavailable' >&2
    return 1
  fi
  python3 "$HERE/gate_fly_guard.py" clean-run-folder "$NOTE" || return 1
  # This is attempted on every failed/aborted session, before app destruction.
  fly_call "$((CUTOFF + 150))" machine exec "$MACHINE" 'bash /opt/chrysalis/archive-results.sh diagnostic-only' --app "$APP" --timeout 20 > "$NOTE/partial-archive.log" 2>&1 || true
  if fly_call "$((CUTOFF + 200))" ssh sftp get --app "$APP" --machine "$MACHINE" /tmp/chrysalis-results.tar.gz "$NOTE/results.tar.gz" > "$NOTE/download.log" 2>&1; then
    (( $(date +%s) <= CREATED_UNIX + CUTOFF + 200 )) || return 1
    python3 "$HERE/gate_fly_guard.py" clean-run-folder "$NOTE" || return 1
    COPIED=1
    printf '{"diagnosticOnly":true,"reason":"failed, aborted or incomplete session"}\n' > "$NOTE/diagnostic-only.json"
    date -u '+%Y-%m-%dT%H:%M:%SZ' > "$NOTE/results-copied-utc.txt"
    return 0
  fi
  echo 'Partial-result download failed; destroying our app without further delay' >&2
  return 1
}
cleanup() {
  local result=$? cleaned=1
  trap - EXIT
  trap '' INT TERM HUP
  set +e
  kill_active_fly
  recover_partial || result=1
  if [[ "$CAN_CLEAN" == 1 && "$APP_CREATE_ATTEMPTED" == 1 ]]; then
    fly_call cleanup machine list --app "$APP" --json > "$NOTE/machines-before-cleanup.json" 2> "$NOTE/machines-before-cleanup.stderr"
    fly_call cleanup volumes list --app "$APP" --json > "$NOTE/volumes-before-cleanup.json" 2> "$NOTE/volumes-before-cleanup.stderr"
    if ! destroy_run "$NOTE/cleanup-proof.json"; then result=1; cleaned=0; fi
  fi
  if [[ "$cleaned" == 0 ]] && ! python3 "$HERE/gate_fly_guard.py" watchdog-pid "$WATCHDOG" "$HERE/gate_fly_watchdog.sh" "$APP" "$NOTE" "$CREATED_UNIX" "$HERE" "$GUARD"; then
    echo "CLEANUP FAILED: $APP; no live watchdog handoff, retaining this process and lock until proof" >&2
    if destroy_run "$NOTE/cleanup-proof.json" 0; then cleaned=1; fi
  fi
  # Only proven absence permits cancelling the watchdog/releasing ownership.
  if [[ "$cleaned" == 1 ]]; then stop_watchdog; fi
  python3 "$HERE/gate_fly_guard.py" lock-holders "$LOCK" "$$" "$PPID" "${WATCHDOG:-0}" > "$NOTE/lock-holders.json" || result=1
  python3 "$HERE/gate_fly_guard.py" clean-run-folder "$NOTE" || result=1
  python3 - "$CREATED_UNIX" "$(date +%s)" "$GUARD" "$HERE" "$cleaned" "$REGION" > "$NOTE/duration-cost.json" <<'PY'
import json,sys
sys.path.insert(0,sys.argv[4])
from gate_time import price, compute_price
start,end,guard=map(int,sys.argv[1:4]);s=max(0,end-start) if start else 0
cleaned=sys.argv[5]=='1';region=sys.argv[6]
json.dump(dict(appGuardStartUnix=start,endedUnix=end if cleaned else None,cleanupOutstanding=not cleaned,conservativeSeconds=s if cleaned else None,computeEstimateUsd=compute_price(s,region) if cleaned else None,totalEstimateWithReserveUsd=price(s,region) if cleaned else None,sealedLimitUsd=price(guard,region),withinSealedGuard=s<=guard if cleaned else None),sys.stdout,indent=2)
PY
  exit "$result"
}
abort() { kill_active_fly; exit "$1"; }
trap cleanup EXIT
trap 'abort 130' INT
trap 'abort 143' TERM
trap 'abort 129' HUP
python3 - "$BUNDLE" "$NOTE" "$BASE" <<'PYPLAN'
import json,sys,tarfile
from pathlib import Path
with tarfile.open(sys.argv[1]) as stream:
    Path(sys.argv[2], 'verified-plan.json').write_bytes(stream.extractfile('plan.json').read())
Path(sys.argv[2], 'image.txt').write_text(sys.argv[3])
PYPLAN
check_machine() {
  if ! python3 "$HERE/gate_fly_guard.py" lock-inode "$LOCK" ||
     ! python3 "$HERE/gate_fly_guard.py" machine "$NOTE" "$MACHINE" "$VM_COMMAND"; then
    printf '{"invalid":"session invalid: machine identity or lock changed"}\n' > "$NOTE/session-invalid.json"
    return 1
  fi
}
neighbours() {
  local phase="$1" apps names name raw counts available=1 performance=0 shared=0 unknown=0
  if ! apps="$(fly_call context apps list --org "$ORG" --json 2>/dev/null)" ||
     ! names="$(printf '%s' "$apps" | python3 "$HERE/gate_fly_guard.py" neighbour-apps - "$APP" 2>/dev/null)"; then
    available=0; names=''
  fi
  while IFS= read -r name; do
    [[ -n "$name" ]] || continue
    if raw="$(fly_call context machine list --app "$name" --json 2>/dev/null)" &&
       counts="$(printf '%s' "$raw" | python3 "$HERE/gate_fly_guard.py" neighbours 2>/dev/null)"; then
      read -r p s u < <(python3 -c 'import json,sys;x=json.loads(sys.argv[1]);print(x.get("performance",0),x.get("shared",0),x.get("unknown",0))' "$counts")
      performance=$((performance + p)); shared=$((shared + s)); unknown=$((unknown + u))
    else available=0; fi
  done <<< "$names"
  python3 - "$available" "$performance" "$shared" "$unknown" > "$NOTE/neighbours-$phase.json" <<'PYCONTEXT'
import json,sys
available,p,s,u=map(int,sys.argv[1:])
json.dump(dict(available=bool(available),performance=p,shared=s,unknown=u,informationOnly=True),sys.stdout)
PYCONTEXT
  return 0
}
fly_call 420 apps list --org "$ORG" --json > "$NOTE/apps-before.json"
python3 "$HERE/gate_fly_guard.py" apps "$NOTE/apps-before.json" "$APP"
neighbours start
CAN_CLEAN=1
CREATED_UNIX="$(date +%s)"
printf '%s\n' "$CREATED_UNIX" > "$NOTE/app-guard-start-unix.txt"
# A double-forked independent session retains fd9 if the launcher's process group dies.
setsid --fork bash "$HERE/gate_fly_watchdog.sh" "$APP" "$NOTE" "$CREATED_UNIX" "$HERE" "$GUARD" </dev/null > "$NOTE/watchdog.log" 2>&1
limit=$(( $(date +%s) + 5 ))
until [[ -s "$NOTE/watchdog.pid" ]]; do
  (( $(date +%s) < limit )) || { echo 'Independent watchdog did not become ready'; exit 1; }
  sleep .05
done
WATCHDOG="$(cat "$NOTE/watchdog.pid")"
APP_CREATE_ATTEMPTED=1
fly_call 420 apps create "$APP" --org "$ORG" --json > "$NOTE/app-created.json"

fly_call 420 machine run "$BASE" --app "$APP" --org "$ORG" --region "$REGION" --vm-size performance-8x --rootfs-size 20 --restart no --rm --detach --env "P2_MACHINE_CREATED_UNIX=$CREATED_UNIX" -- /bin/bash -c "$VM_COMMAND" > "$NOTE/machine-run.log" 2>&1
fly_call 420 machine list --app "$APP" --json > "$NOTE/machines-created.json"
MACHINE="$(python3 "$HERE/gate_fly_guard.py" machine-id "$NOTE/machines-created.json")"
fly_call 420 machine wait "$MACHINE" --app "$APP" --state started --wait-timeout 5m > "$NOTE/machine-ready.log" 2>&1
fly_call 420 machine list --app "$APP" --json > "$NOTE/machines-ready.json"
fly_call 420 ips list --app "$APP" --json > "$NOTE/ips.json"
fly_call 420 volumes list --app "$APP" --json > "$NOTE/volumes.json"
fly_call 420 secrets list --app "$APP" --json > "$NOTE/secrets.json"
check_machine
python3 "$HERE/gate_fly_guard.py" clean-run-folder "$NOTE"
fly_call 420 ssh sftp put --app "$APP" --machine "$MACHINE" "$BUNDLE" /tmp/chrysalis-input.tar.gz > "$NOTE/upload.log" 2>&1
fly_call 420 machine exec "$MACHINE" "bash -lc \"echo $BUNDLE_SHA  /tmp/chrysalis-input.tar.gz | sha256sum -c - && mkdir -p /opt/chrysalis && tar -xzf /tmp/chrysalis-input.tar.gz -C /opt/chrysalis && rm /tmp/chrysalis-input.tar.gz\"" --app "$APP" --timeout 60 > "$NOTE/unpack.log" 2>&1
grep -F '/tmp/chrysalis-input.tar.gz: OK' "$NOTE/unpack.log" > "$NOTE/upload-verified.txt"
python3 "$HERE/gate_fly_guard.py" clean-run-folder "$NOTE"
fly_call 420 ssh sftp put --app "$APP" --machine "$MACHINE" "$NOTE/machine.json" /opt/chrysalis/machine.json > "$NOTE/metadata-upload.log" 2>&1
RESULTS_MAY_EXIST=1
SESSION_STATUS=0
fly_call "$CUTOFF" ssh console --app "$APP" --machine "$MACHINE" -C 'bash /opt/chrysalis/remote-session.sh' > "$NOTE/remote-session.log" 2>&1 || SESSION_STATUS=$?
fly_call "$((CUTOFF + 100))" machine list --app "$APP" --json > "$NOTE/machines-ready.json"
fly_call "$((CUTOFF + 100))" ips list --app "$APP" --json > "$NOTE/ips.json"
fly_call "$((CUTOFF + 100))" volumes list --app "$APP" --json > "$NOTE/volumes.json"
fly_call "$((CUTOFF + 100))" secrets list --app "$APP" --json > "$NOTE/secrets.json"
check_machine
python3 "$HERE/gate_fly_guard.py" clean-run-folder "$NOTE"
if [[ "$SESSION_STATUS" == 0 ]] && fly_call "$((CUTOFF + 200))" ssh sftp get --app "$APP" --machine "$MACHINE" /tmp/chrysalis-results.tar.gz "$NOTE/results.tar.gz" > "$NOTE/download.log" 2>&1; then
  python3 "$HERE/gate_fly_guard.py" clean-run-folder "$NOTE"
  fly_call "$((CUTOFF + 200))" ssh sftp get --app "$APP" --machine "$MACHINE" /tmp/chrysalis-results.sha256 "$NOTE/results.sha256" > "$NOTE/download-hash.log" 2>&1
  python3 - "$NOTE" <<'PYHASH'
import hashlib,sys
from pathlib import Path
p=Path(sys.argv[1])
if hashlib.sha256((p/'results.tar.gz').read_bytes()).hexdigest() != (p/'results.sha256').read_text().split()[0]:
    raise ValueError('Result archive hash differs')
PYHASH
  COPIED=1
  (( $(date +%s) <= CREATED_UNIX + CUTOFF + 200 )) || { echo 'Results missed sealed copy deadline'; exit 1; }
  date -u '+%Y-%m-%dT%H:%M:%SZ' > "$NOTE/results-copied-utc.txt"
else
  recover_partial || true
  exit 1
fi
# Fail/incomplete evidence is diagnostic only, even if SSH and download succeeded.
python3 "$HERE/gate_fly_guard.py" result "$NOTE/results.tar.gz" "$NOTE"

# Context cannot delay or invalidate the paid result copy.
neighbours before-fetch

if [[ -f "$NOTE/probe-rejected.json" ]]; then exit 5; fi
