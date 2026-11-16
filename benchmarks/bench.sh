#!/usr/bin/env bash
# The shared corpus replaces the removed ChrysalisOld/Dahomey comparison.
set -euo pipefail
exec "$(dirname "${BASH_SOURCE[0]}")/run-benchmarks.sh" "$@"
