#!/usr/bin/env bash
# Regenerate .codemap/map.md using the Roslyn-based codemap tool.
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
dotnet run --project "$ROOT/scripts/codemap" -- --output "$ROOT/.codemap/map.md"
