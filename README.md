# Agentic Workforce Platform

Production platform for autonomous AI agent execution in a dual-regulated banking environment (Investec — FCA/PRA UK, SARB/PA SA).

## Stack

- **Runtime:** .NET 10, ASP.NET Core, Durable Task Framework
- **AI:** Microsoft.Extensions.AI (MAF), Claude Sonnet 4.6 + Haiku 4.5 (Azure AI Foundry)
- **Data:** PostgreSQL 16 + pgvector, Redis 7, Azure Event Hubs, Azure Data Explorer
- **Infra:** Azure Container Apps, ACA Dynamic Sessions, .NET Aspire, Bicep IaC
- **CI/CD:** Azure DevOps, Trivy, gitleaks, CQI

## Solution Structure

```
AgenticWorkforce.slnx
├── src/
│   ├── AgenticWorkforce.AppHost/          Aspire orchestrator
│   ├── AgenticWorkforce.ServiceDefaults/  Shared OTel, health, service discovery
│   ├── AgenticWorkforce.Api/              BFF (auth, middleware, endpoints)
│   ├── AgenticWorkforce.Worker/           Background worker (agents, workflows)
│   ├── AgenticWorkforce.Agents/           MAF agent wrappers, tools, prompts
│   ├── AgenticWorkforce.Domain/           Entities, enums, interfaces, exceptions
│   └── AgenticWorkforce.Infrastructure/   EF Core, Redis, Azure SDK
├── tests/
│   ├── AgenticWorkforce.Api.Tests.Unit/
│   ├── AgenticWorkforce.Api.Tests.Integration/
│   └── AgenticWorkforce.Domain.Tests.Unit/
├── infra/                                 Bicep IaC
├── scripts/                               CQI, codemap, rules, hooks
└── docs/                                  Architecture, standards, ADRs
```

## Quick Start

```bash
# Prerequisites: .NET 10 SDK, Docker

# 1. Install hooks
./scripts/install-hooks.sh

# 2. Build
dotnet build AgenticWorkforce.slnx

# 3. Run via Aspire (auto-provisions PostgreSQL + Redis)
dotnet run --project src/AgenticWorkforce.AppHost

# 4. Run tests
dotnet test AgenticWorkforce.slnx
```

## Documentation

| Folder | Contents |
|--------|----------|
| [docs/001-overview/](docs/001-overview/) | Architecture walkthrough |
| [docs/002-architecture/](docs/002-architecture/) | Solution architecture, 17 ADRs, DB schema |
| [docs/003-principles/](docs/003-principles/) | 22 architectural principles |
| [docs/004-rules/](docs/004-rules/) | Machine-checkable rules (`.jsonl`) |
| [docs/005-standards/](docs/005-standards/) | Coding, testing, API, security standards |
| [docs/096-requirements/](docs/096-requirements/) | BRD, TRD, domain model, API surface |

## Key Commands

```bash
./scripts/code-quality.sh AgenticWorkforce.slnx        # CQI score
./scripts/code-quality.sh AgenticWorkforce.slnx --json  # CQI as JSON
./scripts/check-rules.sh                                # Machine-checkable rules
./scripts/codemap.sh                                    # Generate code map
```
