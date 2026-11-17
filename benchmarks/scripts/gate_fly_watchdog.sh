#!/usr/bin/env bash
set +x
set -euo pipefail
APP="$1"; NOTE="$2"; CREATED_UNIX="$3"; HERE="$4"; GUARD="$5"
source "$HERE/gate_fly_common.sh"
TIMER=''
stop_watchdog() {
  kill_active_fly
  if [[ -n "$TIMER" ]]; then kill "$TIMER" 2>/dev/null || true; wait "$TIMER" 2>/dev/null || true; fi
  exit 0
}
trap stop_watchdog TERM INT HUP
printf '%s\n' "$$" > "$NOTE/watchdog.pid"
# Begin recovery before the VM guard; keep the inherited lock until absence is proved.
remaining=$((CREATED_UNIX + GUARD - 70 - $(date +%s)))
if (( remaining > 0 )); then sleep "$remaining" & TIMER=$!; wait "$TIMER" || true; TIMER=''; fi
if destroy_run "$NOTE/watchdog-proof.json" 0; then
  printf 'Independent watchdog proved our app absent\n' > "$NOTE/watchdog-result.txt"
else
  printf 'Independent watchdog cleanup failed; coordinator action required\n' > "$NOTE/watchdog-result.txt"
  exit 1
fi
