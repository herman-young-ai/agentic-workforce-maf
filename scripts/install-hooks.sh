#!/usr/bin/env bash
# Install git hooks and optional CQI tooling.
# Run once after cloning: ./scripts/install-hooks.sh

set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
HOOKS_DIR="$ROOT/.githooks"
GIT_HOOKS_DIR="$ROOT/.git/hooks"

if [ ! -d "$HOOKS_DIR" ]; then
  echo "No .githooks/ directory found at $HOOKS_DIR"
  exit 1
fi

echo "Installing hooks from .githooks/ ..."

for hook in "$HOOKS_DIR"/*; do
  name="$(basename "$hook")"
  target="$GIT_HOOKS_DIR/$name"
  cp "$hook" "$target"
  chmod +x "$target"
  echo "  + $name"
done

echo "Done. Hooks installed."
echo ""
echo "Note (Windows): hooks are bash scripts — run this script from Git Bash or WSL."
echo ""
echo "Required: install gitleaks for secret scanning (pre-commit will warn if missing)"
echo "  macOS/Linux:  brew install gitleaks"
echo "  Windows:      winget install gitleaks   (or: choco install gitleaks / scoop install gitleaks)"
echo ""
echo "Optional: install CQI tools for ./scripts/code-quality.sh"
echo "  brew install pmd semgrep"
echo "  dotnet tool install -g dotnet-reportgenerator-globaltool"
echo "  dotnet tool install -g Roslynator.DotNet.Cli"
echo "  dotnet tool install -g Microsoft.CST.DevSkim.CLI"
