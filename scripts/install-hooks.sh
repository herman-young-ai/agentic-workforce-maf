#!/usr/bin/env bash
# Install AgenticWorkforce git hooks via core.hooksPath.
# Idempotent: safe to re-run. No file copying — edits to .githooks/ apply immediately.
# Run once per clone: ./scripts/install-hooks.sh

set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
HOOKS_DIR="$ROOT/.githooks"

# -- 1. Verify the hooks directory exists and has executable hooks --
if [ ! -d "$HOOKS_DIR" ]; then
  echo "ERROR: $HOOKS_DIR does not exist."
  echo "Hooks must live in .githooks/ at the repo root."
  exit 1
fi

shopt -s nullglob
hook_files=("$HOOKS_DIR"/*)
shopt -u nullglob
if [ "${#hook_files[@]}" -eq 0 ]; then
  echo "ERROR: $HOOKS_DIR is empty."
  exit 1
fi

# Make sure every hook is executable (git silently skips non-executable hooks)
for h in "${hook_files[@]}"; do
  if [ ! -x "$h" ]; then
    echo "Marking $(basename "$h") executable"
    chmod +x "$h"
  fi
done

# -- 2. Point this clone at .githooks/ --
# Using core.hooksPath instead of copying into .git/hooks/ means edits to
# .githooks/pre-commit propagate immediately without re-running this script.
git -C "$ROOT" config core.hooksPath .githooks
echo "git core.hooksPath = .githooks"

# -- 3. Verify required runtime dependencies --
missing=()
need() {
  if ! command -v "$1" &>/dev/null; then
    missing+=("$1 ($2)")
  fi
}
need gitleaks  "secret scanning — brew install gitleaks"
need dotnet    ".NET SDK 10 — https://dot.net/download"
need python3   "Python 3 — system package or brew install python@3"

if [ "${#missing[@]}" -gt 0 ]; then
  echo ""
  echo "ERROR: required tools missing — pre-commit will fail until installed:"
  for m in "${missing[@]}"; do echo "  - $m"; done
  exit 1
fi

# -- 4. Smoke-test the hooks --
for h in "${hook_files[@]}"; do
  name="$(basename "$h")"
  if ! head -1 "$h" | grep -q '^#!'; then
    echo "WARNING: $name has no shebang line — git may not execute it correctly"
  fi
done

# -- 5. Confirm activation --
configured="$(git -C "$ROOT" config --get core.hooksPath || true)"
if [ "$configured" != ".githooks" ]; then
  echo "ERROR: failed to set core.hooksPath (got: '$configured')"
  exit 1
fi

echo ""
echo "Hooks installed and active. Installed hooks:"
for h in "${hook_files[@]}"; do
  echo "  - $(basename "$h")"
done
echo ""
echo "Optional: install CQI tools for richer ./scripts/code-quality.sh output"
echo "  brew install pmd semgrep"
echo "  dotnet tool install -g dotnet-reportgenerator-globaltool"
echo "  dotnet tool install -g Roslynator.DotNet.Cli"
echo "  dotnet tool install -g Microsoft.CST.DevSkim.CLI"
