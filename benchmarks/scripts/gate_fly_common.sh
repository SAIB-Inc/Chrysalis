#!/usr/bin/env bash
# Shared by the launcher and its independent-session watchdog.
set +x
set -euo pipefail
mapfile -t SETTINGS < <(python3 - "$CHRYSALIS_GATE_LOCAL" <<'PYLOCAL'
import json,sys
x=json.load(open(sys.argv[1]))
for key in ('flyTokenFile','flyLock','flyOrg'):
    value=x[key]
    if not isinstance(value,str) or not value or '\n' in value: raise ValueError('Invalid local Fly setting')
    print(value)
PYLOCAL
)
[[ ${#SETTINGS[@]} == 3 ]] || { echo 'Invalid local Fly settings'; exit 2; }
TOKEN_FILE="${SETTINGS[0]}"; LOCK="${SETTINGS[1]}"; ORG="${SETTINGS[2]}"
ACTIVE_FLY_PID=''

kill_active_fly() {
  if [[ -n "$ACTIVE_FLY_PID" ]]; then
    kill -TERM -- "-$ACTIVE_FLY_PID" 2>/dev/null || kill -TERM "$ACTIVE_FLY_PID" 2>/dev/null || true
    kill -KILL -- "-$ACTIVE_FLY_PID" 2>/dev/null || true
    wait "$ACTIVE_FLY_PID" 2>/dev/null || true
    ACTIVE_FLY_PID=''
  fi
}

fly_call() {
  local absolute="$1"; shift
  [[ -e /proc/self/fd/9 ]] && flock --nonblock 9 || { echo 'Fly call without held lock' >&2; return 2; }
  [[ -r "$TOKEN_FILE" && -s "$TOKEN_FILE" ]] || { echo 'Fly token file missing or empty; no stored-login fallback' >&2; return 2; }
  local seconds=8 remaining
  # Recovery remains possible after the VM's absolute stop. Each API call is
  # bounded; only destruction/absence proof may continue beyond the guard.
  if [[ "$absolute" != cleanup && "$absolute" != context ]]; then
    if [[ "$CREATED_UNIX" == 0 ]]; then seconds=60
    else seconds=$((CREATED_UNIX + absolute - $(date +%s))); fi
    (( seconds > 0 )) || { echo "App-relative deadline expired: $absolute" >&2; return 124; }
  fi
  (
    set +x
    # Never inherit another Fly token, API host, organization or stored-login directory.
    for name in "${!FLY_@}"; do unset "$name"; done
    export FLY_NO_UPDATE_CHECK=1 FLY_NO_ANALYTICS=1 BASH_ENV=/dev/null ENV=/dev/null
    FLY_API_TOKEN="$(cat "$TOKEN_FILE")" exec timeout --signal=TERM --kill-after=2 "$seconds" bash -c '
      set +x
      [[ ${FLY_API_TOKEN:-} == *[![:space:]]* ]] || { echo "Fly token is empty; refusing CLI" >&2; exit 2; }
      exec fly "$@"
    ' fly-command "$@" 9>&-
  ) &
  ACTIVE_FLY_PID=$!
  local result=0
  wait "$ACTIVE_FLY_PID" || result=$?
  ACTIVE_FLY_PID=''
  return "$result"
}

destroy_run() {
  local proof="$1" deadline="${2:-$((CREATED_UNIX + GUARD))}" attempt=0 backoff=1
  while :; do
    attempt=$((attempt + 1))
    fly_call cleanup apps destroy "$APP" --yes >> "$NOTE/cleanup.log" 2>&1 || true
    if fly_call cleanup apps list --org "$ORG" --json > "$proof.list" 2> "$proof.stderr" &&
       python3 "$HERE/gate_fly_guard.py" cleanup "$NOTE/apps-before.json" "$proof.list" "$APP" > "$proof"; then
      return 0
    fi
    printf 'Cleanup proof attempt %s failed for our app\n' "$attempt" >&2
    if (( deadline > 0 && $(date +%s) >= deadline )); then break; fi
    sleep "$backoff"
    (( backoff >= 8 )) || backoff=$((backoff * 2))
  done
  printf 'CLEANUP FAILED: %s may remain; watchdog retains the lock and cleanup ownership; coordinator action required\n' "$APP" >&2
  printf '{"status":"FAILED","ourApp":"%s","attempts":%s,"cleanupOutstanding":true}\n' "$APP" "$attempt" > "$NOTE/cleanup-failed.json"
  return 1
}
