#!/bin/bash
# Refresh generated serializer code when source generator changes.
# Source generators use incremental caching that can get stale.
# This removes obj directories and rebuilds with EmitCompilerGeneratedFiles.
set -e

echo "Cleaning obj directories..."
find src -name obj -type d -exec rm -rf {} + 2>/dev/null || true

echo "Rebuilding with EmitCompilerGeneratedFiles..."
dotnet build -p:EmitCompilerGeneratedFiles=true -v q

echo "Done. Generated files are in src/*/obj/Debug/net10.0/generated/"
