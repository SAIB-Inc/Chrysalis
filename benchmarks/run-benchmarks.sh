#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
work_dir="$(cd "$script_dir/../.." && pwd)"
export NUGET_PACKAGES="$work_dir/.nuget/packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
exec python3 "$script_dir/scripts/run_benchmarks.py" "$@"
