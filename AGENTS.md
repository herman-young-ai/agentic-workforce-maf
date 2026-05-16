# AGENTS.md — AI Coding Assistant Instructions

## Project: Agentic Workforce Platform

Production platform for Investec (dual-regulated bank — FCA/PRA UK, SARB/PA SA).
C# / .NET 10 / ASP.NET Core / MAF / PostgreSQL + pgvector / Redis / Azure Container Apps.

## Solution Structure

```
AgenticWorkforce.slnx
├── src/
│   ├── AgenticWorkforce.AppHost/          .NET Aspire orchestrator
│   ├── AgenticWorkforce.ServiceDefaults/  Shared OTel, health, service discovery
│   ├── AgenticWorkforce.Api/              ASP.NET Core BFF
│   ├── AgenticWorkforce.Worker/           Background worker (Durable Task, agents)
│   ├── AgenticWorkforce.Agents/           MAF agent wrappers, tools, prompts
│   ├── AgenticWorkforce.Domain/           Entities, enums, interfaces, exceptions
│   └── AgenticWorkforce.Infrastructure/   EF Core, Redis, Azure SDK
├── tests/
│   ├── AgenticWorkforce.Api.Tests.Unit/
│   ├── AgenticWorkforce.Api.Tests.Integration/
│   └── AgenticWorkforce.Domain.Tests.Unit/
├── infra/                                 Bicep IaC (VNet, PostgreSQL, Redis, Container Apps)
└── scripts/                               CQI, codemap, rules, hooks
```

## Dependency Graph (one-way, no cycles)

```
AppHost → Api, Worker             (Aspire orchestration)
Api → Domain, Infrastructure      (BFF — endpoints, auth, middleware)
Worker → Domain, Infrastructure, Agents  (Durable Task, agent execution)
Agents → Domain                   (MAF wrappers, tools)
Infrastructure → Domain           (EF Core, Redis implementations)
Domain → (nothing)                (pure — entities, interfaces, exceptions)
```

## Architecture: Vertical Slice (NOT Classic Layering)

Features are organized as self-contained slices, NOT as Controller → Service → Repository layers.

```
Api/Features/Projects/
├── CreateProject.cs      (endpoint + handler + request/response DTOs)
├── GetProject.cs
├── ListProjects.cs
└── UpdateProject.cs
```

Cross-cutting concerns live in `Api/Core/` (auth, exceptions, middleware, pagination, observability).

## Critical Rules

### C# Code

- **Async all the way** — `async Task<T>` with `CancellationToken ct` on every async method
- **DateTime UTC only** — `DateTime.UtcNow`, never `DateTimeOffset`, never `DateTime.Now`
- **Structured logging** — `logger.LogInformation("Created {ProjectId}", id)` — never string interpolation
- **AppException hierarchy** — throw typed exceptions from `AgenticWorkforce.Domain.Exceptions`, never raw `Exception`
- **Fail fast** — no silent fallbacks, no hidden defaults, no swallowed exceptions
- **No hardcoded values** — configuration via `IOptions<T>`, `IConfiguration`, or appsettings
- **No mocks** — tests use real implementations (Testcontainers for PostgreSQL, not InMemory)
- **Nullable enabled** — `string?` for nullable, `string` for non-nullable — no `!` operator except navigation properties
- **Files ≤500 lines** — hard limit 1000 lines
- **One public type per file** — except tightly coupled pairs (entity + configuration)

### Data Layer

- **EF Core via Infrastructure project only** — Domain has no EF Core references
- **Interfaces in Domain** — implementations in Infrastructure
- **All entities extend EntityBase** — UUID PK, CreatedAt, UpdatedAt
- **jsonb for flexible payloads** — `string? Settings` with `HasColumnType("jsonb")`
- **Enums stored as strings** — `.HasConversion<string>()` in configuration
- **Optimistic concurrency** — via PostgreSQL `xmin` system column
- **Soft delete** — `IsActive = false`, never hard delete (Principle 13: Retract, don't delete)

### Authentication & Authorization

- **[Authorize] by default** — every endpoint; [AllowAnonymous] needs justification
- **Entra ID JWT + API Key** — dual auth scheme (ADR-007)
- **Hierarchical roles** — Viewer < Operator < Reviewer < Owner < PlatformAdmin
- **Project-scoped authorization** — check ProjectMember role before accessing project resources
- **BOLA prevention** — always verify resource ownership

### Error Handling

- **Error codes** — machine-readable, format `{CATEGORY}_{NOUN}_{STATE}` (see ErrorCodes.cs)
- **ProblemDetails RFC 9457** — all errors returned as ProblemDetails with `code` + `traceId`
- **GlobalExceptionHandler** — single place for exception → HTTP response mapping
- **Never expose internal errors** — 500s get generic message; details in logs only

### Agent Execution

- **Container-first (Principle 22)** — all tools touching network/filesystem run in ACA Dynamic Sessions
- **Platform tools only** — `project.*` queries run in-process; everything else is sandboxed
- **Budget enforcement** — BudgetEnforcingChatClient checks before every LLM call
- **Audit every LLM call** — AuditingChatClient in IChatClient pipeline; no silent drops
- **Human gate on learnings** — agent-extracted learnings default to `Pending`

### Testing

- **No mocks** — use real services, Testcontainers for PostgreSQL
- **xUnit + FluentAssertions** — `act.Should().ThrowAsync<NotFoundException>()`
- **Test naming** — `{Method}_{Scenario}_{ExpectedOutcome}`
- **100% on auth/authz** — every role combination, every error path
- **Integration tests** — `ApiWebApplicationFactory` with real PostgreSQL (pgvector/pgvector:pg16)

## Naming Conventions

| Construct | Convention | Example |
|-----------|-----------|---------|
| Classes, records | PascalCase | `ProjectService`, `CreateProjectRequest` |
| Interfaces | `I` prefix | `IProjectRepository`, `IAgentRuntime` |
| Async methods | `Async` suffix | `CreateProjectAsync` |
| Private fields | `_camelCase` | `_projectRepository` |
| Local variables | camelCase | `projectId` |
| Constants | PascalCase | `DefaultPageSize` |
| Enums | PascalCase (singular) | `TaskStatus.Running` |
| DB tables | PascalCase (plural) | `Projects`, `AgenticTasks` |
| JSON fields | camelCase | `projectId`, `createdAt` |
| Error codes | UPPER_SNAKE | `AUTH_TOKEN_EXPIRED` |
| Agent names | dot.separated | `security.reviewer`, `project.director` |

## Commands

```bash
# Build
dotnet build AgenticWorkforce.slnx

# Test
dotnet test AgenticWorkforce.slnx

# Run via Aspire
dotnet run --project src/AgenticWorkforce.AppHost

# EF Core migration
dotnet ef migrations add <Name> --project src/AgenticWorkforce.Infrastructure --startup-project src/AgenticWorkforce.Api

# Codemap
./scripts/codemap.sh

# Code quality
./scripts/code-quality.sh AgenticWorkforce.slnx

# Install hooks
./scripts/install-hooks.sh
```

## Do NOT

- Import EF Core or Npgsql in Domain or Agents projects
- Use `DateTime.Now` or `DateTimeOffset` anywhere
- Use `Moq` or any mocking framework
- Use `InMemoryDatabase` for tests
- Create Controller → Service → Repository layers (use vertical slices)
- Hardcode connection strings, API keys, or any secrets
- Swallow exceptions or create silent fallbacks
- Skip `CancellationToken` on async methods
- Use string interpolation in log messages
- Create files over 1000 lines
