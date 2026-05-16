#!/usr/bin/env bash
# ┌──────────────────────────────────────────────────────────────────────────┐
# │  CQI — C# Code Quality Index                                             │
# │  7 dimensions → single 0–100 score                                       │
# │                                                                           │
# │  Bands: Poor 0–30 | Acceptable 31–54 | Adequate 55–64 |                 │
# │         Good 65–79 | Excellent 80–100                                    │
# │                                                                           │
# │  Usage:                                                                   │
# │    ./scripts/code-quality.sh AgenticWorkforce.slnx                            │
# │    ./scripts/code-quality.sh AgenticWorkforce.slnx --json                     │
# │    ./scripts/code-quality.sh AgenticWorkforce.slnx --no-coverage              │
# │    ./scripts/code-quality.sh AgenticWorkforce.slnx --no-semgrep               │
# │                                                                           │
# │  One-time install (macOS/Linux):                                          │
# │    brew install pmd semgrep                                               │
# │    dotnet tool install -g dotnet-reportgenerator-globaltool               │
# │    dotnet tool install -g Roslynator.DotNet.Cli                           │
# │    dotnet tool install -g Microsoft.CST.DevSkim.CLI                      │
# └──────────────────────────────────────────────────────────────────────────┘
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

# ── Args ──────────────────────────────────────────────────────────────────
SLN="${1:?usage: code-quality.sh <Solution.sln|slnx>}"
JSON_OUT=false; SKIP_COV=false; SKIP_SEMGREP=false
for arg in "${@:2}"; do
  case "$arg" in
    --json)        JSON_OUT=true  ;;
    --no-coverage) SKIP_COV=true  ;;
    --no-semgrep)  SKIP_SEMGREP=true ;;
  esac
done

# ── Colours ───────────────────────────────────────────────────────────────
GREEN=$'\e[32m'; YELLOW=$'\e[33m'; RED=$'\e[31m'
BOLD=$'\e[1m'; DIM=$'\e[2m'; RESET=$'\e[0m'

# ── Output dir ────────────────────────────────────────────────────────────
OUT=$(mktemp -d -t cqi-XXXX)
trap 'rm -rf "$OUT"' EXIT
export CQI_OUT="$OUT"   # picked up by Directory.Build.props

# ── Tool detection ────────────────────────────────────────────────────────
has()         { command -v "$1" &>/dev/null; }
HAS_PMD=false;        has pmd               && HAS_PMD=true
HAS_SEMGREP=false;    has semgrep           && HAS_SEMGREP=true
HAS_DEVSKIM=false;    has devskim           && HAS_DEVSKIM=true
HAS_ROSLYNATOR=false; has roslynator        && HAS_ROSLYNATOR=true
HAS_REPORTGEN=false;  has reportgenerator   && HAS_REPORTGEN=true

$JSON_OUT || printf '%b' "${BOLD}CQI — collecting data${RESET}  "

# ─────────────────────────────────────────────────────────────────────────
#  DATA COLLECTION (parallel where safe)
# ─────────────────────────────────────────────────────────────────────────

_build() {
  dotnet build "$SLN" -c Release /t:Build\;Metrics \
    --nologo /clp:Summary > "$OUT/build.log" 2>&1
}

_vulns() {
  dotnet list "$SLN" package --vulnerable --include-transitive \
    --format json --output-version 1 > "$OUT/vuln.json" 2>/dev/null \
    || echo '{"version":1,"sources":[]}' > "$OUT/vuln.json"
}

_cpd() {
  if $HAS_PMD; then
    pmd cpd --language cs --minimum-tokens 100 \
      --dir src --format xml > "$OUT/cpd.xml" 2>/dev/null || true
  else
    echo "<pmd-cpd/>" > "$OUT/cpd.xml"
  fi
}

_devskim() {
  if $HAS_DEVSKIM; then
    devskim analyze --source-code . -f sarif \
      -o "$OUT/devskim.sarif" > /dev/null 2>&1 || true
  fi
  [ -f "$OUT/devskim.sarif" ] || \
    printf '{"version":"2.1.0","runs":[{"results":[]}]}' > "$OUT/devskim.sarif"
}

_semgrep() {
  if ! $SKIP_SEMGREP && $HAS_SEMGREP; then
    semgrep --config=p/csharp --json \
      --output "$OUT/semgrep.json" . > /dev/null 2>&1 || true
  fi
  [ -f "$OUT/semgrep.json" ] || echo '{"results":[]}' > "$OUT/semgrep.json"
}

_coverage() {
  $SKIP_COV && return
  dotnet test "$SLN" --collect:"XPlat Code Coverage" \
    --results-directory "$OUT/cov" --no-build -c Release \
    --verbosity quiet > /dev/null 2>&1 || true
  if $HAS_REPORTGEN; then
    local xml
    xml=$(find "$OUT/cov" -name "coverage.cobertura.xml" 2>/dev/null | head -1)
    if [ -n "$xml" ]; then
      reportgenerator -reports:"$OUT/cov/**/coverage.cobertura.xml" \
        -targetdir:"$OUT/cov-report" \
        -reporttypes:"Cobertura" > /dev/null 2>&1 || true
    fi
  fi
}

_format() {
  dotnet format "$SLN" --verify-no-changes \
    --report "$OUT/fmt.json" --severity warn > /dev/null 2>&1 || true
  [ -f "$OUT/fmt.json" ] || echo '[]' > "$OUT/fmt.json"
}

_roslynator() {
  if $HAS_ROSLYNATOR; then
    roslynator list-symbols "$SLN" --visibility public \
      --output-format xml > "$OUT/public-api.xml" 2>/dev/null || true
  fi
  [ -f "$OUT/public-api.xml" ] || echo "<Symbols/>" > "$OUT/public-api.xml"
}

# Launch independent collectors in parallel
_build    & PID_BUILD=$!
_vulns    & PID_VULN=$!
_cpd      & PID_CPD=$!
_devskim  & PID_DSK=$!
_semgrep  & PID_SG=$!

wait $PID_BUILD $PID_VULN $PID_CPD $PID_DSK $PID_SG

_coverage     # sequential (requires build to finish)
_format       # sequential (needs solution)
_roslynator   # sequential (needs build)

$JSON_OUT || printf '%b\n' "${DIM}done${RESET}"

# ─────────────────────────────────────────────────────────────────────────
#  SCORING ENGINE (Python3 — always available)
# ─────────────────────────────────────────────────────────────────────────

RESULT=$(python3 - "$OUT" "$ROOT" <<'PYEOF'
import sys, os, json, glob, re, math
import xml.etree.ElementTree as ET

OUT, ROOT = sys.argv[1], sys.argv[2]

# ── Helpers ───────────────────────────────────────────────────────────────

def clamp(v, lo=0, hi=100):
    return max(lo, min(hi, float(v)))

def band(v):
    if v >= 80: return "EXCELLENT"
    if v >= 65: return "GOOD"
    if v >= 55: return "ADEQUATE"
    if v >= 31: return "ACCEPTABLE"
    return "POOR"

def safe_mean(lst, default=50):
    return sum(lst) / len(lst) if lst else default

def piecewise(v, breakpoints):
    """breakpoints: [(threshold, score), ...] sorted descending by threshold."""
    for threshold, score in breakpoints:
        if v >= threshold:
            return score
    return 0

# ── Load Metrics XML ──────────────────────────────────────────────────────

def load_metrics():
    types, members = [], []
    for f in glob.glob(f"{OUT}/*.Metrics.xml"):
        try:
            tree = ET.parse(f)
            for nt in tree.getroot().iter('NamedType'):
                m_el = nt.find('Metrics')
                if m_el is None: continue
                m = {e.get('Name'): int(e.get('Value', 0)) for e in m_el}
                m['_name'] = nt.get('Name', '')
                m['_file'] = f
                types.append(m)
                for mem in nt.iter('Member'):
                    mm_el = mem.find('Metrics')
                    if mm_el is None: continue
                    mm = {e.get('Name'): int(e.get('Value', 0)) for e in mm_el}
                    mm['_name'] = mem.get('Name', '')
                    mm['_is_test'] = 'Test' in f or 'test' in f
                    members.append(mm)
        except Exception:
            pass
    return types, members

# ── Load SARIF results ────────────────────────────────────────────────────

def load_sarif(exclude_pattern=None):
    results = []
    for f in glob.glob(f"{OUT}/*.sarif"):
        if exclude_pattern and exclude_pattern in os.path.basename(f):
            continue
        try:
            data = json.load(open(f))
            for run in data.get('runs', []):
                results.extend(run.get('results', []))
        except Exception:
            pass
    return results

def count_rules(results, prefixes):
    return sum(1 for r in results
               if any(r.get('ruleId', '').startswith(p) for p in prefixes))

def count_rule_ids(results, ids):
    return sum(1 for r in results if r.get('ruleId', '') in ids)

# ── KLOC from types ───────────────────────────────────────────────────────

def get_kloc(types):
    total = sum(t.get('SourceLines', 0) for t in types)
    return max(1.0, total / 1000.0)

# ─────────────────────────────────────────────────────────────────────────
#  LOAD ALL DATA
# ─────────────────────────────────────────────────────────────────────────

types, members = load_metrics()
sarif           = load_sarif(exclude_pattern='devskim')
kloc            = get_kloc(types)

# ─────────────────────────────────────────────────────────────────────────
#  DIMENSION 1 — MAINTAINABILITY (weight 0.20)
# ─────────────────────────────────────────────────────────────────────────

def score_maintainability():
    scores = []

    # 1a. Weighted mean Maintainability Index (MI 0–100, green ≥ 20)
    total_lines = sum(t.get('SourceLines', 0) for t in types)
    if total_lines > 0:
        w_mi = sum(t.get('MaintainabilityIndex', 50) * t.get('SourceLines', 1)
                   for t in types) / total_lines
        scores.append(clamp(w_mi))
    else:
        scores.append(50.0)

    # 1b. % methods with CC ≤ 10 (McCabe ideal)
    ccs = [m.get('CyclomaticComplexity', 1) for m in members
           if m.get('CyclomaticComplexity', 0) > 0]
    if ccs:
        scores.append(100.0 * sum(1 for cc in ccs if cc <= 10) / len(ccs))
    else:
        scores.append(50.0)

    # 1c. Penalty for CC > 25 (CA1502 threshold)
    if ccs:
        pct_gt25 = 100.0 * sum(1 for cc in ccs if cc > 25) / len(ccs)
        scores.append(clamp(100 - 4 * pct_gt25))
    else:
        scores.append(50.0)

    # 1d. Mean method SourceLines ≤ 30
    m_lines = [m.get('SourceLines', 0) for m in members
               if m.get('SourceLines', 0) > 0]
    if m_lines:
        mean_sl = safe_mean(m_lines)
        scores.append(100.0 if mean_sl <= 30 else clamp(100 - 2 * (mean_sl - 30)))
    else:
        scores.append(50.0)

    # 1e. S3776 cognitive complexity violations per KLOC (default threshold 15)
    s3776 = count_rules(sarif, ['S3776'])
    scores.append(clamp(100 - 5 * s3776 / kloc))

    return round(safe_mean(scores))

# ─────────────────────────────────────────────────────────────────────────
#  DIMENSION 2 — SECURITY (weight 0.20)
# ─────────────────────────────────────────────────────────────────────────

def score_security():
    # 2a. Vulnerable NuGet packages
    try:
        vdata = json.load(open(f"{OUT}/vuln.json"))
        crit_high = med = low = 0
        for src in vdata.get('sources', []):
            for proj in src.get('Projects', []):
                for fw in proj.get('Frameworks', []):
                    pkgs = fw.get('TopLevelPackages', []) + fw.get('TransitivePackages', [])
                    for pkg in pkgs:
                        for v in pkg.get('Vulnerabilities', []):
                            sev = v.get('Severity', '').lower()
                            if sev in ('critical', 'high'): crit_high += 1
                            elif sev in ('moderate', 'medium'): med += 1
                            elif sev == 'low': low += 1
        if crit_high == 0:   s_vuln = 100.0
        elif crit_high == 1: s_vuln = 30.0
        else:                s_vuln = 0.0
        s_vuln = clamp(s_vuln - 15 * med - 5 * low)
    except Exception:
        s_vuln = 50.0

    # 2b. CA security rules (CA2xxx, CA3xxx, CA5xxx)
    ca_sec = count_rules(sarif, ['CA2', 'CA3', 'CA5'])
    s_ca = clamp(100 - 10 * ca_sec / kloc)

    # 2c. SonarAnalyzer known security rules
    sonar_sec = {
        'S2068','S2076','S2078','S2091','S2115','S2255','S2631','S3649',
        'S3884','S4432','S4792','S4818','S5042','S5144','S5145','S5766','S5973'
    }
    s_sonar = clamp(100 - 10 * count_rule_ids(sarif, sonar_sec) / kloc)

    # 2d. DevSkim
    try:
        dsdata = json.load(open(f"{OUT}/devskim.sarif"))
        ds_count = sum(len(run.get('results', [])) for run in dsdata.get('runs', []))
        s_devskim = clamp(100 - 8 * ds_count)
    except Exception:
        s_devskim = 50.0

    # 2e. Semgrep p/csharp errors
    try:
        sgdata = json.load(open(f"{OUT}/semgrep.json"))
        sg_err = sum(1 for r in sgdata.get('results', [])
                     if r.get('extra', {}).get('severity', '').upper() == 'ERROR')
        s_semgrep = clamp(100 - 8 * sg_err)
    except Exception:
        s_semgrep = 50.0

    return round(
        0.30 * s_vuln  +
        0.25 * s_ca    +
        0.20 * s_sonar +
        0.15 * s_devskim +
        0.10 * s_semgrep
    )

# ─────────────────────────────────────────────────────────────────────────
#  DIMENSION 3 — MODULARITY (weight 0.10)
# ─────────────────────────────────────────────────────────────────────────

def score_modularity():
    scores = []

    # 3a. Mean ClassCoupling per type (NDepend warns > 9)
    couplings = [t.get('ClassCoupling', 0) for t in types
                 if t.get('ClassCoupling', 0) > 0]
    if couplings:
        mean_cc = safe_mean(couplings)
        s = 100.0 if mean_cc <= 9 else clamp(100 - 2 * (mean_cc - 9))
        scores.append(s)
    else:
        scores.append(50.0)

    # 3b. Mean DepthOfInheritance (ideal ≤ 3)
    dois = [t.get('DepthOfInheritance', 0) for t in types
            if t.get('DepthOfInheritance', 0) > 0]
    if dois:
        mean_doi = safe_mean(dois)
        s = 100.0 if mean_doi <= 3 else clamp(100 - 25 * (mean_doi - 3))
        scores.append(s)
    else:
        scores.append(50.0)

    # 3c. Project dependency depth (max chain from .csproj references)
    def get_proj_depth():
        refs = {}
        for csproj in glob.glob(f"{ROOT}/src/**/*.csproj", recursive=True):
            try:
                tree = ET.parse(csproj)
                name = os.path.basename(csproj).replace('.csproj', '')
                refs[name] = []
                for pr in tree.getroot().iter('ProjectReference'):
                    inc = pr.get('Include', '')
                    dep = os.path.basename(inc).replace('.csproj', '')
                    refs[name].append(dep)
            except Exception:
                pass
        def depth(name, seen=None):
            seen = seen or set()
            if name in seen or name not in refs: return 0
            seen.add(name)
            deps = refs[name]
            return 1 + max((depth(d, seen.copy()) for d in deps), default=0)
        depths = [depth(n) for n in refs]
        return max(depths) if depths else 0

    max_depth = get_proj_depth()
    s = 100.0 if max_depth <= 4 else clamp(100 - 20 * (max_depth - 4))
    scores.append(s)

    # 3d. Abstraction ratio proxy (interface + abstract types / total types)
    #     We use Metrics XML type names as a proxy
    total_types = len(types)
    if total_types > 0:
        # Heuristic: types with low SourceLines + high MI tend to be interfaces
        # Better: grep source for 'interface ' and 'abstract class '
        abstract_count = 0
        for f in glob.glob(f"{ROOT}/src/**/*.cs", recursive=True):
            try:
                with open(f) as fh:
                    content = fh.read()
                abstract_count += len(re.findall(
                    r'\b(interface|abstract\s+class)\s+\w', content))
            except Exception:
                pass
        ratio = abstract_count / max(total_types, 1)
        # Ideal: D = |A + I − 1| close to 0; use ratio as A proxy
        d_dist = abs(ratio - 0.5)  # simplified; 0 = perfectly on main sequence
        scores.append(clamp(100 - 100 * d_dist))
    else:
        scores.append(50.0)

    return round(safe_mean(scores))

# ─────────────────────────────────────────────────────────────────────────
#  DIMENSION 4 — TESTABILITY (weight 0.15)
# ─────────────────────────────────────────────────────────────────────────

def score_testability():
    cov_breakpoints = [(85, 100), (75, 80), (60, 60), (40, 30), (0, 0)]

    # 4a/b/c. Coverage from Cobertura
    line_cov = branch_cov = method_cov = None
    cob_files = glob.glob(f"{OUT}/cov-report/Cobertura.xml") + \
                glob.glob(f"{OUT}/cov/**/coverage.cobertura.xml", recursive=True)
    for cf in cob_files:
        try:
            root = ET.parse(cf).getroot()
            line_cov   = float(root.get('line-rate',   0)) * 100
            branch_cov = float(root.get('branch-rate', 0)) * 100
            # method coverage: sum covered/total across all methods
            total_m = covered_m = 0
            for method in root.iter('method'):
                total_m += 1
                if float(method.get('line-rate', 0)) > 0:
                    covered_m += 1
            method_cov = (100.0 * covered_m / total_m) if total_m else line_cov
            break
        except Exception:
            pass

    s_line   = piecewise(line_cov or 0,   cov_breakpoints)
    s_branch = piecewise(branch_cov or 0, cov_breakpoints)
    s_method = piecewise(method_cov or 0, cov_breakpoints)

    # 4d. Test-to-code ratio (test SourceLines / non-test SourceLines)
    test_lines = sum(m.get('SourceLines', 0) for m in members if m.get('_is_test'))
    code_lines = sum(m.get('SourceLines', 0) for m in members if not m.get('_is_test'))
    if code_lines > 0:
        ratio = test_lines / code_lines
        s_ratio = piecewise(ratio, [(0.5, 100), (0.3, 70), (0.15, 40), (0, 0)])
    else:
        s_ratio = 50.0

    if line_cov is None:
        # coverage not collected — partial score from test ratio only
        return round(s_ratio * 0.5 + 50 * 0.5)

    return round(
        0.40 * s_branch +
        0.30 * s_line   +
        0.20 * s_method +
        0.10 * s_ratio
    )

# ─────────────────────────────────────────────────────────────────────────
#  DIMENSION 5 — ROBUSTNESS (weight 0.10)
# ─────────────────────────────────────────────────────────────────────────

def score_robustness():
    csproj_files = glob.glob(f"{ROOT}/src/**/*.csproj", recursive=True)
    total_proj = len(csproj_files)
    nullable_count = twae_count = 0

    for f in csproj_files:
        try:
            with open(f) as fh: content = fh.read()
            if re.search(r'<Nullable>\s*enable\s*</Nullable>', content, re.I):
                nullable_count += 1
            if re.search(r'<TreatWarningsAsErrors>\s*true\s*</TreatWarningsAsErrors>',
                         content, re.I):
                twae_count += 1
        except Exception:
            pass

    # Also check Directory.Build.props
    for dbp in glob.glob(f"{ROOT}/Directory.Build.props"):
        try:
            with open(dbp) as fh: content = fh.read()
            if re.search(r'<Nullable>\s*enable\s*</Nullable>', content, re.I):
                nullable_count = total_proj  # applies to all
            if re.search(r'<TreatWarningsAsErrors>\s*true\s*</TreatWarningsAsErrors>',
                         content, re.I):
                twae_count = total_proj
        except Exception:
            pass

    s_nullable = (100.0 * nullable_count / total_proj) if total_proj else 50.0
    s_twae     = (100.0 * twae_count     / total_proj) if total_proj else 50.0

    # Build warnings per KLOC
    warn_count = 0
    try:
        with open(f"{OUT}/build.log") as fh:
            for line in fh:
                if re.search(r'\bwarning\b', line, re.I) and \
                   not re.search(r'(^Build|Summary|Succeeded|Failed)', line):
                    warn_count += 1
    except Exception:
        pass
    s_warns = clamp(100 - 5 * warn_count / kloc)

    # Empty catch / swallowed exceptions (CA1031, S2486, S108)
    swallow = count_rule_ids(sarif, {'CA1031', 'S2486', 'S108'})
    s_catch = clamp(100 - 5 * swallow / kloc)

    return round(safe_mean([s_nullable, s_twae, s_warns, s_catch]))

# ─────────────────────────────────────────────────────────────────────────
#  DIMENSION 6 — ELEGANCE (weight 0.10)
# ─────────────────────────────────────────────────────────────────────────

def score_elegance():
    scores = []

    # 6a. dotnet format violations per KLOC
    try:
        fmt_data = json.load(open(f"{OUT}/fmt.json"))
        # fmt.json is an array of document entries, each with a list of changes
        violations = sum(
            len(doc.get('FileChanges', doc.get('changes', [])))
            for doc in (fmt_data if isinstance(fmt_data, list) else [])
        )
        scores.append(clamp(100 - 0.5 * violations / kloc))
    except Exception:
        scores.append(50.0)

    # 6b. CPD duplication % (Sonar "Sonar way" gate: > 3% on new code)
    try:
        cpd_tree = ET.parse(f"{OUT}/cpd.xml").getroot()
        dup_lines = sum(int(d.get('lines', 0)) for d in cpd_tree.iter('duplication'))
        total_src_lines = sum(t.get('SourceLines', 0) for t in types) or 1
        dup_pct = 100.0 * dup_lines / total_src_lines
        scores.append(piecewise(
            100 - dup_pct,
            [(97, 100), (95, 70), (90, 40), (0, 0)]
        ))
    except Exception:
        scores.append(50.0)

    # 6c. Mean method SourceLines (concision)
    m_lines = [m.get('SourceLines', 0) for m in members if m.get('SourceLines', 0) > 0]
    if m_lines:
        mean_ml = safe_mean(m_lines)
        scores.append(piecewise(mean_ml, []) if False else
                      100.0 if mean_ml <= 20 else
                      70.0  if mean_ml <= 40 else 30.0)
    else:
        scores.append(50.0)

    # 6d. IDE-rule violations per KLOC (IDExxxx)
    ide_count = count_rules(sarif, ['IDE'])
    scores.append(clamp(100 - 2 * ide_count / kloc))

    return round(safe_mean(scores))

# ─────────────────────────────────────────────────────────────────────────
#  DIMENSION 7 — REUSABILITY (weight 0.15)
# ─────────────────────────────────────────────────────────────────────────

def score_reusability():
    scores = []

    # 7a. Interface / abstraction ratio (ideal 10–40%)
    total_types_count = len(types)
    abstract_types = 0
    for f in glob.glob(f"{ROOT}/src/**/*.cs", recursive=True):
        try:
            with open(f) as fh:
                content = fh.read()
            abstract_types += len(re.findall(
                r'\b(interface|abstract\s+class)\s+\w', content))
        except Exception:
            pass
    if total_types_count > 0:
        ratio = abstract_types / total_types_count
        if 0.10 <= ratio <= 0.40:
            s = 100.0
        elif ratio < 0.10:
            s = clamp(100 - (0.10 - ratio) * 500)
        else:
            s = clamp(100 - (ratio - 0.40) * 200)
        scores.append(s)
    else:
        scores.append(50.0)

    # 7b. Public vs internal surface (favour internal; ideal public ≤ 30%)
    pub_count = int_count = 0
    for f in glob.glob(f"{ROOT}/src/**/*.cs", recursive=True):
        try:
            with open(f) as fh:
                content = fh.read()
            pub_count += len(re.findall(r'\bpublic\s+(class|interface|enum|struct|record)\b', content))
            int_count += len(re.findall(r'\binternal\s+(class|interface|enum|struct|record)\b', content))
        except Exception:
            pass
    total_pub_int = pub_count + int_count
    if total_pub_int > 0:
        pub_ratio = pub_count / total_pub_int
        scores.append(clamp(100 - max(0, pub_ratio - 0.30) * 200))
    else:
        scores.append(50.0)

    # 7c. Mean ClassCoupling (lower = more reusable)
    couplings = [t.get('ClassCoupling', 0) for t in types if t.get('ClassCoupling', 0) > 0]
    if couplings:
        mean_c = safe_mean(couplings)
        scores.append(100.0 if mean_c <= 9 else clamp(100 - 2 * (mean_c - 9)))
    else:
        scores.append(50.0)

    # 7d. Bonus: any *.Abstractions or *.Contracts project
    abst_projects = glob.glob(f"{ROOT}/src/**/*Abstractions*.csproj", recursive=True) + \
                    glob.glob(f"{ROOT}/src/**/*Contracts*.csproj", recursive=True)
    if abst_projects:
        scores.append(min(100.0, safe_mean(scores) + 10))

    return round(safe_mean(scores))

# ─────────────────────────────────────────────────────────────────────────
#  COMPUTE
# ─────────────────────────────────────────────────────────────────────────

d = {
    'Maintainability': score_maintainability(),
    'Security':        score_security(),
    'Testability':     score_testability(),
    'Reusability':     score_reusability(),
    'Modularity':      score_modularity(),
    'Robustness':      score_robustness(),
    'Elegance':        score_elegance(),
}

cqi = round(
    0.20 * d['Maintainability'] +
    0.20 * d['Security']        +
    0.15 * d['Testability']     +
    0.15 * d['Reusability']     +
    0.10 * d['Modularity']      +
    0.10 * d['Robustness']      +
    0.10 * d['Elegance']
, 1)

out = {
    'cqi':        cqi,
    'band':       band(cqi),
    'kloc':       round(kloc, 2),
    'dimensions': d,
}

print(json.dumps(out))
PYEOF
)

# ─────────────────────────────────────────────────────────────────────────
#  OUTPUT
# ─────────────────────────────────────────────────────────────────────────

if $JSON_OUT; then
  echo "$RESULT"
  exit 0
fi

# Human-readable output
CQI=$(echo   "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['cqi'])")
BAND=$(echo  "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['band'])")
KLOC=$(echo  "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['kloc'])")
MAINT=$(echo "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['dimensions']['Maintainability'])")
SEC=$(echo   "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['dimensions']['Security'])")
TEST=$(echo  "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['dimensions']['Testability'])")
REUSE=$(echo "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['dimensions']['Reusability'])")
MOD=$(echo   "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['dimensions']['Modularity'])")
ROB=$(echo   "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['dimensions']['Robustness'])")
ELEG=$(echo  "$RESULT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['dimensions']['Elegance'])")

score_colour() {
  local v=$1
  if   [ "$v" -ge 80 ]; then printf '%b' "$GREEN"
  elif [ "$v" -ge 65 ]; then printf '%b' "$GREEN"
  elif [ "$v" -ge 55 ]; then printf '%b' "$YELLOW"
  elif [ "$v" -ge 31 ]; then printf '%b' "$YELLOW"
  else                       printf '%b' "$RED"
  fi
}

band_colour() {
  case "$1" in
    EXCELLENT|GOOD) printf '%b' "$GREEN" ;;
    ADEQUATE|ACCEPTABLE) printf '%b' "$YELLOW" ;;
    *) printf '%b' "$RED" ;;
  esac
}

bar() {
  local v=$1 width=30
  local filled=$(( v * width / 100 ))
  local empty=$(( width - filled ))
  printf '%b' "$(score_colour "$v")"
  printf '%0.s█' $(seq 1 $filled)
  printf '%b' "$DIM"
  printf '%0.s░' $(seq 1 $empty)
  printf '%b' "$RESET"
}

echo ""
printf '%b┌──────────────────────────────────────────┐%b\n' "$BOLD" "$RESET"
printf '%b│  C# Code Quality Index                   │%b\n' "$BOLD" "$RESET"
printf '%b└──────────────────────────────────────────┘%b\n' "$BOLD" "$RESET"
echo ""
printf "  %-16s  %b%s%b  %b(%s)%b   %bKLOC: %s%b\n" \
  "CQI" "$(band_colour "$BAND")$BOLD" "$CQI" "$RESET" \
  "$(band_colour "$BAND")" "$BAND" "$RESET" \
  "$DIM" "$KLOC" "$RESET"
echo ""
printf "  %-18s  %3s  %s\n" "Dimension"  " # " "Score"
printf "  %-18s  %3s  %s\n" "---------" "---" "-----"

print_dim() {
  local label=$1 score=$2 weight=$3
  printf "  %-18s  %b%3d%b  %s  %b×%s%b\n" \
    "$label" "$(score_colour "$score")" "$score" "$RESET" \
    "$(bar "$score")" "$DIM" "$weight" "$RESET"
}

print_dim "Maintainability" "$MAINT" "0.20"
print_dim "Security"        "$SEC"   "0.20"
print_dim "Testability"     "$TEST"  "0.15"
print_dim "Reusability"     "$REUSE" "0.15"
print_dim "Modularity"      "$MOD"   "0.10"
print_dim "Robustness"      "$ROB"   "0.10"
print_dim "Elegance"        "$ELEG"  "0.10"

echo ""
printf "  %bBands:%b  Poor 0–30  Acceptable 31–54  " "$DIM" "$RESET"
printf "Adequate 55–64  Good 65–79  Excellent 80–100%b\n" "$RESET"
echo ""
