# Phase 11: Infrastructure & Deployment

**Status:** Not Started
**Depends On:** Phase 10 (Integration Testing)
**Verification:** `dotnet run --project src/AgenticWorkforce.AppHost` starts all services, health checks pass, Swagger accessible

---

## Objective

Complete the deployment infrastructure: finalize Bicep modules, build Docker images, wire Aspire AppHost with all resources and environment variables, create CI/CD pipeline definitions, add security scanning, and ensure the platform runs end-to-end from a single `dotnet run` command. After this phase, the platform is ready for deployment to Azure Container Apps.

---

## 1. Aspire AppHost (Complete Wiring)

### File: `src/AgenticWorkforce.AppHost/Program.cs` (rewrite)

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// -- Infrastructure Resources --
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin()
    .WithPgWeb()
    .AddDatabase("agenticworkforce");

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisInsight();

// -- BFF API --
var api = builder.AddProject<Projects.AgenticWorkforce_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("AzureAd__ClientId", "local-dev")
    .WithEnvironment("AzureAd__TenantId", "common");

// -- Background Worker --
builder.AddProject<Projects.AgenticWorkforce_Worker>("worker")
    .WithReference(postgres)
    .WithReference(redis)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

builder.Build().Run();
```

---

## 2. Docker Images

### File: `src/AgenticWorkforce.Api/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:8080/alive || exit 1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Packages.props .
COPY ["src/AgenticWorkforce.Api/AgenticWorkforce.Api.csproj", "src/AgenticWorkforce.Api/"]
COPY ["src/AgenticWorkforce.Domain/AgenticWorkforce.Domain.csproj", "src/AgenticWorkforce.Domain/"]
COPY ["src/AgenticWorkforce.Infrastructure/AgenticWorkforce.Infrastructure.csproj", "src/AgenticWorkforce.Infrastructure/"]
COPY ["src/AgenticWorkforce.ServiceDefaults/AgenticWorkforce.ServiceDefaults.csproj", "src/AgenticWorkforce.ServiceDefaults/"]
RUN dotnet restore "src/AgenticWorkforce.Api/AgenticWorkforce.Api.csproj"
COPY . .
RUN dotnet publish "src/AgenticWorkforce.Api/AgenticWorkforce.Api.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AgenticWorkforce.Api.dll"]
```

### File: `src/AgenticWorkforce.Worker/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:8080/alive || exit 1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Packages.props .
COPY ["src/AgenticWorkforce.Worker/AgenticWorkforce.Worker.csproj", "src/AgenticWorkforce.Worker/"]
COPY ["src/AgenticWorkforce.Domain/AgenticWorkforce.Domain.csproj", "src/AgenticWorkforce.Domain/"]
COPY ["src/AgenticWorkforce.Infrastructure/AgenticWorkforce.Infrastructure.csproj", "src/AgenticWorkforce.Infrastructure/"]
COPY ["src/AgenticWorkforce.Agents/AgenticWorkforce.Agents.csproj", "src/AgenticWorkforce.Agents/"]
COPY ["src/AgenticWorkforce.ServiceDefaults/AgenticWorkforce.ServiceDefaults.csproj", "src/AgenticWorkforce.ServiceDefaults/"]
RUN dotnet restore "src/AgenticWorkforce.Worker/AgenticWorkforce.Worker.csproj"
COPY . .
RUN dotnet publish "src/AgenticWorkforce.Worker/AgenticWorkforce.Worker.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AgenticWorkforce.Worker.dll"]
```

---

## 3. Bicep Completion

The existing `infra/` folder has stub modules. Complete them:

### File: `infra/main.bicep` (orchestrator)

```bicep
targetScope = 'resourceGroup'

@description('Environment name (dev, staging, prod)')
param environmentName string

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('PostgreSQL administrator login')
@secure()
param postgresAdminPassword string

// Modules
module network 'network.bicep' = { ... }
module keyVault 'key-vault.bicep' = { ... }
module postgresql 'postgresql.bicep' = { ... }
module redis 'redis.bicep' = { ... }
module compute 'compute.bicep' = { ... }
```

### Key Bicep modules to complete:

| File | Resources |
|------|-----------|
| `infra/network.bicep` | VNet, subnets (aca, data, private-endpoints), NSGs |
| `infra/key-vault.bicep` | Key Vault + RBAC for UAMI |
| `infra/postgresql.bicep` | Flexible Server, pgvector extension, private endpoint, CMK |
| `infra/redis.bicep` | Azure Cache for Redis, private endpoint |
| `infra/compute.bicep` | ACA Environment, Api Container App, Worker Container App |
| `infra/compute-containerapp.bicep` | Reusable module for a single Container App |
| `infra/main.bicepparam` | Parameter file for dev environment |

### Critical Bicep configurations:

```bicep
// PostgreSQL extensions
resource pgExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = [
  { name: 'azure.extensions', properties: { value: 'vector,pgaudit,uuid-ossp' } }
]

// ACA Container App with health probes
resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  properties: {
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'http' }
    }
    template: {
      containers: [{
        probes: [
          { type: 'liveness', httpGet: { path: '/alive', port: 8080 } }
          { type: 'readiness', httpGet: { path: '/health', port: 8080 } }
        ]
      }]
    }
  }
}
```

---

## 4. CI/CD Pipeline

### File: `pipelines/azure-pipelines.yml`

```yaml
# Three-stage pipeline: Build → Test → Deploy (Avalanche pattern)
trigger:
  branches:
    include: [main, feature/*]

pool:
  vmImage: 'ubuntu-latest'

variables:
  dotnetVersion: '10.0.x'
  solution: 'AgenticWorkforce.slnx'
  buildConfiguration: 'Release'

stages:
  - stage: Build
    jobs:
      - job: BuildAndScan
        steps:
          - task: UseDotNet@2
            inputs: { version: $(dotnetVersion) }

          - script: dotnet restore $(solution)
            displayName: 'Restore'

          - script: dotnet build $(solution) -c $(buildConfiguration) --no-restore
            displayName: 'Build'

          # Security scanning
          - script: |
              docker run --rm -v $(pwd):/src aquasec/trivy:latest fs /src \
                --severity HIGH,CRITICAL --exit-code 1
            displayName: 'Trivy vulnerability scan'

          - script: |
              docker run --rm -v $(pwd):/src zricethezav/gitleaks:latest detect \
                --source /src --exit-code 1
            displayName: 'Gitleaks secret scan'

          # Publish artifacts
          - script: |
              dotnet publish src/AgenticWorkforce.Api -c $(buildConfiguration) -o $(Build.ArtifactStagingDirectory)/api
              dotnet publish src/AgenticWorkforce.Worker -c $(buildConfiguration) -o $(Build.ArtifactStagingDirectory)/worker
            displayName: 'Publish'

          - publish: $(Build.ArtifactStagingDirectory)
            artifact: drop

  - stage: Test
    dependsOn: Build
    jobs:
      - job: IntegrationTests
        services:
          postgres:
            image: pgvector/pgvector:pg16
            ports: ['5432:5432']
          redis:
            image: redis:7
            ports: ['6379:6379']
        steps:
          - task: UseDotNet@2
            inputs: { version: $(dotnetVersion) }

          - script: dotnet test $(solution) -c $(buildConfiguration) --no-build --logger trx --collect:"XPlat Code Coverage"
            displayName: 'Run tests'

          - task: PublishTestResults@2
            inputs: { testResultsFormat: 'VSTest', testResultsFiles: '**/*.trx' }

          - task: PublishCodeCoverageResults@2
            inputs: { codeCoverageTool: 'Cobertura', summaryFileLocation: '**/coverage.cobertura.xml' }

  - stage: Deploy
    dependsOn: Test
    condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
    jobs:
      - deployment: DeployToDev
        environment: 'dev'
        strategy:
          runOnce:
            deploy:
              steps:
                - script: echo "Deploy via azd or Bicep"
                  displayName: 'Deploy to ACA'
```

---

## 5. Scripts

### File: `scripts/codemap.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
# Generates .codemap/map.md — type/method inventory for AI assistants
mkdir -p .codemap
echo "# Code Map" > .codemap/map.md
echo "" >> .codemap/map.md
echo "Generated: $(date -u +%Y-%m-%dT%H:%M:%SZ)" >> .codemap/map.md
echo "" >> .codemap/map.md

for proj in src/AgenticWorkforce.*/; do
    echo "## $(basename "$proj")" >> .codemap/map.md
    find "$proj" -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" | sort | while read -r file; do
        # Extract public types
        grep -n "^\s*public\s\+\(class\|interface\|record\|enum\|struct\)" "$file" 2>/dev/null | while read -r line; do
            echo "- \`$(basename "$file"):${line%%:*}\` — ${line#*:}" >> .codemap/map.md
        done
    done
    echo "" >> .codemap/map.md
done
```

### File: `scripts/code-quality.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
SOLUTION="${1:-AgenticWorkforce.slnx}"
JSON_FLAG="${2:-}"

# CQI = Code Quality Index
# Factors: build warnings, test pass rate, file sizes, rule violations
WARNINGS=$(dotnet build "$SOLUTION" 2>&1 | grep -c "warning" || true)
TESTS_TOTAL=$(dotnet test "$SOLUTION" --no-build -v q 2>&1 | grep -oP "Total tests: \K\d+" || echo 0)
TESTS_PASSED=$(dotnet test "$SOLUTION" --no-build -v q 2>&1 | grep -oP "Passed: \K\d+" || echo 0)
LARGE_FILES=$(find src/ -name "*.cs" -not -path "*/obj/*" -exec wc -l {} \; | awk '$1>500{count++}END{print count+0}')

# Score calculation (100 = perfect)
SCORE=$((100 - WARNINGS - LARGE_FILES * 5))
[ "$SCORE" -lt 0 ] && SCORE=0

if [ "$JSON_FLAG" = "--json" ]; then
    echo "{\"cqi\": $SCORE, \"warnings\": $WARNINGS, \"tests_total\": $TESTS_TOTAL, \"tests_passed\": $TESTS_PASSED, \"large_files\": $LARGE_FILES}"
else
    echo "CQI Score: $SCORE/100"
    echo "  Warnings: $WARNINGS"
    echo "  Tests: $TESTS_PASSED/$TESTS_TOTAL"
    echo "  Files >500 lines: $LARGE_FILES"
fi
```

### File: `scripts/check-rules.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
# Runs machine-checkable rules from docs/004-rules/*.jsonl
FAILURES=0

for rules_file in docs/004-rules/*.jsonl; do
    while IFS= read -r line; do
        id=$(echo "$line" | jq -r '.id')
        desc=$(echo "$line" | jq -r '.description')
        check=$(echo "$line" | jq -r '.check')
        expect=$(echo "$line" | jq -r '.expect')

        result=$(eval "$check" 2>/dev/null || true)
        if [ "$expect" = "zero_results" ] && [ -n "$result" ]; then
            echo "FAIL [$id]: $desc"
            echo "  Found: $result"
            FAILURES=$((FAILURES + 1))
        else
            echo "PASS [$id]: $desc"
        fi
    done < "$rules_file"
done

echo ""
echo "Results: $FAILURES failures"
exit $FAILURES
```

### File: `scripts/install-hooks.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
HOOKS_DIR=".git/hooks"

cat > "$HOOKS_DIR/pre-commit" << 'EOF'
#!/usr/bin/env bash
set -euo pipefail

# 1. Build must pass
dotnet build AgenticWorkforce.slnx -v q --nologo || { echo "Build failed"; exit 1; }

# 2. No large files
LARGE=$(find src/ -name "*.cs" -not -path "*/obj/*" -exec wc -l {} \; | awk '$1>1000{print $2}')
if [ -n "$LARGE" ]; then
    echo "ERROR: Files exceed 1000 lines:"
    echo "$LARGE"
    exit 1
fi

# 3. No secrets
if grep -rn "Password=\|Secret=\|ApiKey=" src/ --include="*.cs" | grep -v "appsettings\|Configuration\|IOptions"; then
    echo "ERROR: Possible hardcoded secrets detected"
    exit 1
fi
EOF

chmod +x "$HOOKS_DIR/pre-commit"
echo "Git hooks installed."
```

---

## 6. Production Configuration

### File: `src/AgenticWorkforce.Api/appsettings.Production.json`

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "investec.co.za",
    "TenantId": "{{KEY_VAULT_REF}}",
    "ClientId": "{{KEY_VAULT_REF}}",
    "Audience": "{{KEY_VAULT_REF}}"
  },
  "KeyVault": {
    "Uri": "https://kv-agentic-workforce-prod.vault.azure.net/"
  },
  "RateLimiting": {
    "PermitLimit": 600,
    "WindowSeconds": 60,
    "StrictPermitLimit": 10
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

### File: `src/AgenticWorkforce.Worker/appsettings.Production.json`

```json
{
  "KeyVault": {
    "Uri": "https://kv-agentic-workforce-prod.vault.azure.net/"
  },
  "AgentDefaults": {
    "DefaultTimeoutSeconds": 300,
    "DefaultMaxToolCalls": 50,
    "DefaultBudgetUsd": 1.00
  },
  "Audit": {
    "ChannelCapacity": 50000,
    "BackpressureTimeoutSeconds": 5,
    "BatchSize": 100,
    "FlushIntervalSeconds": 1
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

---

## 7. .gitignore Verification

Ensure the following are excluded:

```
# Build
**/bin/
**/obj/
**/publish/

# Audit (local dev only)
var/audit/

# IDE
.vs/
.vscode/
*.user

# Secrets
*.env
appsettings.*.local.json

# OS
.DS_Store
Thumbs.db
```

---

## 8. README Quickstart Validation

Update `README.md` with verified commands that work end-to-end:

```bash
# Prerequisites: .NET 10 SDK, Docker

# 1. Install hooks
./scripts/install-hooks.sh

# 2. Build
dotnet build AgenticWorkforce.slnx

# 3. Run via Aspire (auto-provisions PostgreSQL + Redis + pgAdmin)
dotnet run --project src/AgenticWorkforce.AppHost

# 4. Run tests
dotnet test AgenticWorkforce.slnx

# 5. Open API
# Swagger: http://localhost:5000/swagger
# pgAdmin: http://localhost:5050
# Redis Insight: http://localhost:8001
```

---

## File Summary

### Files to CREATE

```
src/AgenticWorkforce.Api/Dockerfile
src/AgenticWorkforce.Worker/Dockerfile
src/AgenticWorkforce.Api/appsettings.Production.json
src/AgenticWorkforce.Worker/appsettings.json
src/AgenticWorkforce.Worker/appsettings.Development.json
src/AgenticWorkforce.Worker/appsettings.Production.json
pipelines/azure-pipelines.yml
scripts/codemap.sh
scripts/code-quality.sh
scripts/check-rules.sh
scripts/install-hooks.sh
.codemap/map.md (generated)
```

### Files to REWRITE

```
src/AgenticWorkforce.AppHost/Program.cs — Full Aspire wiring
infra/main.bicep — Complete orchestrator
infra/network.bicep — VNet + subnets + NSGs
infra/key-vault.bicep — Key Vault + RBAC
infra/postgresql.bicep — Flexible Server + extensions + private endpoint
infra/redis.bicep — Azure Cache + private endpoint
infra/compute.bicep — ACA Environment + Container Apps
infra/compute-containerapp.bicep — Reusable Container App module
infra/main.bicepparam — Dev parameters
README.md — Verified quickstart
.gitignore — Ensure audit/build dirs excluded
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet run --project src/AgenticWorkforce.AppHost` starts:
   - PostgreSQL (with pgvector) on auto-assigned port
   - Redis on auto-assigned port
   - Api on http://localhost:5000 (or Aspire-assigned)
   - Worker connected to both
3. `curl http://localhost:5000/alive` returns 200
4. `curl http://localhost:5000/health` returns 200 with "Healthy"
5. Swagger UI accessible at `/swagger`
6. `dotnet test AgenticWorkforce.slnx` — all tests pass
7. `./scripts/check-rules.sh` — 0 failures
8. `./scripts/code-quality.sh AgenticWorkforce.slnx` — CQI score > 80
9. Docker images build successfully: `docker build -f src/AgenticWorkforce.Api/Dockerfile .`
10. Bicep lints clean: `az bicep lint --file infra/main.bicep`
11. No secrets in repository: `gitleaks detect --source . --exit-code 1`

---

## Goal Command

```
/goal Infrastructure and deployment complete: Aspire AppHost wires PostgreSQL + Redis + Api + Worker with auto-provisioning. Dockerfiles build both services with health probes. Bicep modules define VNet, Key Vault, PostgreSQL (pgvector), Redis, ACA Environment, and Container Apps. CI/CD pipeline builds, scans (Trivy + gitleaks), tests, and deploys. Scripts provide codemap, CQI, rule checks, and git hooks. Production appsettings reference Key Vault. Verify: dotnet run --project src/AgenticWorkforce.AppHost starts all services, curl /alive returns 200, curl /health returns Healthy, Swagger accessible, dotnet test passes. Stop after 30 turns.
```
