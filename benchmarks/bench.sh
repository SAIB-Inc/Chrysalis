#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "========================================="
echo " BenchmarkDotNet — Deserialization"
echo "========================================="
echo

echo "=== Chrysalis New (codegen) ==="
dotnet run --project "$REPO_ROOT/benchmarks/ChrysalisNew" -c Release
echo

echo "=== Chrysalis Old (Dahomey) ==="
dotnet run --project "$REPO_ROOT/benchmarks/ChrysalisOld" -c Release
echo

echo "========================================="
echo " BenchmarkDotNet complete."
echo "========================================="
