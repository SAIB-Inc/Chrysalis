#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "========================================"
echo "  Chrysalis Old (NuGet 1.1.0-alpha)"
echo "========================================"
cd "$SCRIPT_DIR/ChrysalisOld"
dotnet run -c Release

echo ""
echo "========================================"
echo "  Chrysalis New (ReadOnlyMemory<byte>)"
echo "========================================"
cd "$SCRIPT_DIR/ChrysalisNew"
dotnet run -c Release

echo ""
echo "========================================"
echo "  Pallas (Rust 1.0.0-alpha.4)"
echo "========================================"
cd "$SCRIPT_DIR/PallasBench"
cargo bench
