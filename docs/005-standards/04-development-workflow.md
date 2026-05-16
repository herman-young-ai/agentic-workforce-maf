# Development Workflow

## Branching Strategy

| Branch | Purpose |
|--------|---------|
| `main` | Production-ready, protected, PR-required |
| `develop` | Integration branch |
| `feature/{ticket}-{desc}` | Feature branches (from `develop`) |
| `hotfix/{ticket}-{desc}` | Production fixes (from `main`, merge to both) |

## Commit Messages

```
[TYPE] Short description (max 72 characters)

Optional longer explanation.
- Specific changes
- Fixes #123

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
```

Types: `[FEAT]`, `[FIX]`, `[REFACTOR]`, `[DOCS]`, `[TEST]`, `[CHORE]`, `[SECURITY]`

## Pre-Commit Hook

Installed via `./scripts/install-hooks.sh`. Runs on every commit:

1. **gitleaks** — secret scanning on staged files
2. **dotnet build** — must compile (includes Roslyn security analyzers)
3. **dotnet test** — must pass
4. **CQI score** — must be ≥ 70 (fast mode: no coverage, no semgrep)

## Pull Request Process

1. Create branch from `develop`
2. Atomic commits (one logical change per commit)
3. Open PR against `develop`
4. CI runs: build, test, NuGet audit, Bicep validation, CQI
5. Code review — ≥ 1 approval
6. All checks pass — no exceptions
7. Squash-merge or rebase-merge

## Adding a Feature (Checklist)

- [ ] Create vertical slice in `Api/Features/{Resource}/`
- [ ] Define request/response DTOs in the slice file
- [ ] Add domain interface if new repository needed
- [ ] Implement repository in Infrastructure
- [ ] Register in DI (`Program.cs`)
- [ ] Add EF Core migration if schema changed: `dotnet ef migrations add <Name> --project src/AgenticWorkforce.Infrastructure --startup-project src/AgenticWorkforce.Api`
- [ ] Add error codes to `ErrorCodes.cs` and `docs/005-standards/02-error-codes.md`
- [ ] Write integration tests
- [ ] `./scripts/check-rules.sh` passes
- [ ] CQI score ≥ 70

## Adding an Agent (Checklist)

- [ ] Create seed YAML in `Agents/Seeds/{agent-name}.yaml`
- [ ] Register tools in tool registry
- [ ] Write system prompt
- [ ] Add catalog seed entry
- [ ] Test agent execution with real LLM calls
- [ ] Verify audit trail captures all LLM calls and tool invocations

## Local Development

```bash
# Prerequisites: .NET 10 SDK, Node.js 22+, Docker

# 1. Start via Aspire (PostgreSQL + Redis auto-provisioned)
dotnet run --project src/AgenticWorkforce.AppHost

# 2. API available at https://localhost:5001/swagger
# 3. Aspire dashboard at https://localhost:15888

# 4. Or run standalone with Docker PostgreSQL + Redis
docker run -d --name pg -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=agenticworkforce_dev -p 5432:5432 pgvector/pgvector:pg16
docker run -d --name redis -p 6379:6379 redis:7-alpine
dotnet run --project src/AgenticWorkforce.Api

# 5. Run tests
dotnet test AgenticWorkforce.slnx

# 6. Install hooks (one-time)
./scripts/install-hooks.sh
```

## Versioning

- SemVer: `MAJOR.MINOR.PATCH`
- Patch: bug fixes, dependency updates
- Minor: new features, new API endpoints
- Major: breaking API changes
- During development (pre-v1): breaking changes are free
