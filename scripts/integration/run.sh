#!/usr/bin/env bash
# Environment-only endpoints and credentials; no configuration files are sourced.
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
unset CHRYSALIS_IT_FUNDED
filter='Category=Integration&Known!=B2&Known!=B3&Known!=B4&Known!=F2'
case "${1:-}" in
  --include-known) filter='Category=Integration'; shift ;;
  --funded) export CHRYSALIS_IT_FUNDED=1; filter='Category=Integration&FullyQualifiedName~FundedIntegrationTests'; shift ;;
  --help)
    printf '%s\n' 'Usage: run.sh [--include-known|--funded] [dotnet test options]' \
      'Default excludes only known-defect traits. Missing environment produces real skips.' \
      'See src/Chrysalis.IntegrationTests/README.md before configuring credentials.'
    exit 0 ;;
esac
export NUGET_PACKAGES="${NUGET_PACKAGES-$HOME/.nuget/packages}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd -- "$root"
launcher=()
if command -v heavy >/dev/null 2>&1; then
  launcher=(heavy)
fi
exec ${launcher[@]+"${launcher[@]}"} dotnet test src/Chrysalis.IntegrationTests/Chrysalis.IntegrationTests.csproj -m:8 --filter "$filter" "$@"
