#!/usr/bin/env bash
# Run all machine-checkable rules from docs/04-rules/*.jsonl
# Usage: ./scripts/check-rules.sh
#        ./scripts/check-rules.sh --errors-only

set -uo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

ERRORS_ONLY=false
[[ "${1:-}" == "--errors-only" ]] && ERRORS_ONLY=true

PASS=0
FAIL=0
SKIP=0

run_check() {
  local id="$1" severity="$2" description="$3" check="$4" expect="$5"

  # Skip review-only rules
  if [[ "$check" == "review" ]]; then
    [[ "$ERRORS_ONLY" == "false" ]] && echo "  [SKIP] $id — manual review required"
    ((SKIP++))
    return
  fi

  local output
  output=$(eval "$check" 2>/dev/null || true)

  local ok=false
  case "$expect" in
    "zero_results")   [[ -z "$output" ]] && ok=true ;;
    "at_least_one")   [[ -n "$output" ]] && ok=true ;;
    *)                [[ "$output" == "$expect" ]] && ok=true ;;
  esac

  if $ok; then
    [[ "$ERRORS_ONLY" == "false" ]] && echo "  [PASS] $id"
    ((PASS++))
  else
    local colour="\033[0;31m"   # red for error
    [[ "$severity" == "warning" ]] && colour="\033[0;33m"
    [[ "$severity" == "info" ]]    && colour="\033[0;34m"
    echo -e "  ${colour}[FAIL][$severity]\033[0m $id — $description"
    ((FAIL++))
  fi
}

echo "Running rules checks..."
echo ""

for rule_file in "$ROOT"/docs/004-rules/*.jsonl; do
  [ -e "$rule_file" ] || { echo "No rule files found in docs/004-rules/"; exit 0; }
  echo "$(basename "$rule_file")"
  while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    id=$(echo "$line"          | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['id'])")
    severity=$(echo "$line"    | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['severity'])")
    description=$(echo "$line" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['description'])")
    check=$(echo "$line"       | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['check'])")
    expect=$(echo "$line"      | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['expect'])")
    run_check "$id" "$severity" "$description" "$check" "$expect"
  done < "$rule_file"
  echo ""
done

echo "Results: $PASS passed, $FAIL failed, $SKIP skipped"
[[ $FAIL -gt 0 ]] && exit 1 || exit 0
