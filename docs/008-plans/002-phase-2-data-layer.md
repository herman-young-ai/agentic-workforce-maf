# Phase 2: Data Layer

**Status:** Not Started
**Depends On:** Phase 1 (Domain Alignment)
**Verification:** `dotnet build` exits 0, `dotnet ef migrations list` shows migration, integration test creates DB and runs CRUD

---

## Pre-flight

Complete the checklist in [000-phase-overview.md § Pre-flight for every phase](000-phase-overview.md#pre-flight-for-every-phase):

1. Read `.codemap/map.md` — type/method inventory from the previous phase. Do not recreate anything already present.
2. Read `.codemap/quality.md` — current CQI baseline. Work must not regress the score.
3. Verify the previous phase's exit criteria still hold:
   - `dotnet build AgenticWorkforce.slnx` exits 0
   - `dotnet test AgenticWorkforce.slnx` exits 0

---

## Objective

Implement the full EF Core data layer: DbContext with all entity configurations, initial migration, repository implementations registered in DI, Worker DB registration fixed, and Central Package Management introduced. After this phase, we can create, read, update entities against a real PostgreSQL instance.

---

## 1. DbContext Rewrite

Replace current `AppDbContext` (57 lines) with the full spec from `docs/002-architecture/003-database-schema.md` §5.

### Key changes from current state:

| Concern | Current | Target |
|---------|---------|--------|
| Class name | `AppDbContext` | Keep `AppDbContext` (simpler than renaming everywhere) |
| Enum storage | String conversion (`.HasConversion<string>()`) | PostgreSQL native enums (`HasPostgresEnum<T>()`) |
| Concurrency | None | xmin row version on all non-append-only entities |
| Table names | Default (PascalCase) | Explicit snake_case (`.ToTable("projects")`) |
| Partitioned tables | None | `ProjectEvent` + `LlmCall` excluded from migrations, created via raw SQL |
| Vector columns | `float[]?` | `Vector?` with `HasColumnType("vector(1536)")` + HNSW index |
| Composite keys | None | `TaskDependency`, `ModelPricing`, `LlmCall`, `ProjectEvent` |
| Check constraints | None | `ck_project_learnings_confidence` |
| BRIN indexes | None | `SessionMessage.CreatedAt` |
| Filtered unique indexes | None | Platform workflow templates (project_id IS NULL) |

### File: `src/AgenticWorkforce.Infrastructure/Data/AppDbContext.cs`

Rewrite with:
- All DbSets from spec (§5)
- `OnModelCreating` registers PostgreSQL enums, pgvector extension, and xmin concurrency loop
- Calls `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)` for entity configs

### Configuration files: REWRITE (not delete)

Keep the separate `IEntityTypeConfiguration<T>` pattern but rewrite each file to match the spec exactly (snake_case tables, native enums, HNSW indexes, proper FKs, check constraints). This avoids a 500+ line `OnModelCreating` method (AGENTS.md: files ~500 lines on average).

Reorganize by domain area:

```
src/AgenticWorkforce.Infrastructure/Data/Configurations/
├── ProjectConfigurations.cs      — Project, ProjectContext, ContextChange, ContextMilestone, ProjectIntent, ProjectMember, ProjectAgent
├── TaskConfigurations.cs         — AgenticTask, TaskAttempt, TaskDependency
├── KnowledgeConfigurations.cs    — ProjectLearning, ProjectDecision, MilestoneSummary, ProjectArtifact
├── DocumentConfigurations.cs     — ProjectDocument, DocumentChunk
├── SessionConfigurations.cs      — Session, SessionMessage, SessionChannel
├── WorkflowConfigurations.cs     — WorkflowDefinition, WorkflowRun, WorkflowSchedule, HumanInputRequest
├── EventConfigurations.cs        — ProjectEvent (partitioned, ExcludeFromMigrations)
├── IdentityConfigurations.cs     — User, ApiKey
└── PlatformConfigurations.cs     — AgentCatalog, PromptVersion, LlmCall (partitioned), ModelPricing
```

### Rewrite existing configuration files:

Delete the current 8 files and replace with 9 new files grouped by domain area (listed above). Each file contains multiple related `IEntityTypeConfiguration<T>` classes. The `OnModelCreating` stays lean — only enum registration, pgvector extension, and the xmin concurrency loop. Entity configurations are discovered via `ApplyConfigurationsFromAssembly`.

**Rationale:** Keeps `AppDbContext` under 100 lines while each configuration file stays under 200 lines. Grouping by domain area (not per-entity) balances discoverability with file count.

---

## 2. NpgsqlDataSource Configuration

The spec requires enum mapping at the `NpgsqlDataSource` level (before the `DbContext` is created). This means changing how the connection is built in both Api and Worker.

### Pattern (from spec):

```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
dataSourceBuilder.MapEnum<ProjectStatus>();
dataSourceBuilder.MapEnum<ProjectTier>();
// ... all enums including HumanInputRequestStatus, HumanDecisionType (see DataSourceFactory below)
// ... all enums ...
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(dataSource, npgsql => npgsql.EnableRetryOnFailure(3)));
```

### Files to modify:

- `src/AgenticWorkforce.Api/Program.cs` — replace current DB registration
- `src/AgenticWorkforce.Worker/Program.cs` — replace current DB registration (also add `UseVector()`)

### Extract to shared helper:

Create `src/AgenticWorkforce.Infrastructure/Data/DataSourceFactory.cs`:

```csharp
public static class DataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        builder.MapEnum<ProjectStatus>();
        builder.MapEnum<ProjectTier>();
        builder.MapEnum<ProjectRole>();
        builder.MapEnum<SystemRole>();
        builder.MapEnum<ChangeType>();
        builder.MapEnum<IntentSource>();
        builder.MapEnum<AgentRole>();
        builder.MapEnum<TaskType>();
        builder.MapEnum<TaskStatus>();
        builder.MapEnum<TaskSource>();
        builder.MapEnum<AttemptStatus>();
        builder.MapEnum<FailureTier>();
        builder.MapEnum<LearningKind>();
        builder.MapEnum<LearningStatus>();
        builder.MapEnum<DecisionStatus>();
        builder.MapEnum<ContentFormat>();
        builder.MapEnum<ArtifactType>();
        builder.MapEnum<DocumentType>();
        builder.MapEnum<ExtractionStatus>();
        builder.MapEnum<SessionStatus>();
        builder.MapEnum<MessageRole>();
        builder.MapEnum<WorkflowRunStatus>();
        builder.MapEnum<HumanInputRequestStatus>();
        builder.MapEnum<HumanDecisionType>();      // ← carry-over from Phase 1 ADR-018 fix
        builder.MapEnum<EventSeverity>();
        builder.MapEnum<AgentVisibility>();
        return builder.Build();
    }
}
```

Both Api and Worker call `DataSourceFactory.Create(connectionString)` then pass the result to `UseNpgsql()`.

> **Carry-over from Phase 1:** `HumanDecisionType` was added to `Enums.cs` and registered in `AppDbContext.HasPostgresEnum<>()` during Phase 1, but the `NpgsqlDataSourceBuilder.MapEnum<>()` registration was deferred because no `NpgsqlDataSourceBuilder` existed yet (the scaffold only called `npgsql.UseVector()` on `DbContextOptions`). This phase introduces the `DataSourceFactory` — make sure `MapEnum<HumanDecisionType>()` is in the list alongside the other enum mappings. Without it, Npgsql will fail at runtime the first time a `HumanInputRequest` row with a non-null `Decision` is read or written.

---

## 3. AuditInterceptor Update

Current `AuditInterceptor` stamps `UpdatedAt`. Keep this but ensure it skips `ModelPricing` (no `EntityBase`), `TaskDependency` (no `EntityBase`), and partitioned entities.

No structural changes needed — it already targets `EntityBase` entries only.

---

## 4. Initial EF Core Migration

Generate the initial migration after all entity/config changes are applied:

```bash
dotnet ef migrations add InitialSchema \
  --project src/AgenticWorkforce.Infrastructure \
  --startup-project src/AgenticWorkforce.Api
```

### Additional raw SQL migration for partitioned tables:

Create a second migration `CreatePartitionedTables` with raw SQL for:

```sql
-- ProjectEvent (RANGE partition by created_at month)
CREATE TABLE project_events (
    id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    task_id UUID REFERENCES tasks(id) ON DELETE SET NULL,
    session_id UUID REFERENCES sessions(id) ON DELETE SET NULL,
    event_type TEXT NOT NULL,
    source TEXT,
    data JSONB,
    severity event_severity NOT NULL DEFAULT 'info',
    updated_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

-- LlmCall (RANGE partition by created_at month)
CREATE TABLE llm_calls (
    id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    session_id UUID,
    project_id UUID REFERENCES projects(id) ON DELETE SET NULL,
    task_id UUID,
    agent_name TEXT,
    agent_role TEXT,
    model TEXT NOT NULL,
    provider TEXT NOT NULL,
    input_tokens BIGINT NOT NULL DEFAULT 0,
    output_tokens BIGINT NOT NULL DEFAULT 0,
    cache_read_tokens BIGINT NOT NULL DEFAULT 0,
    cache_creation_tokens BIGINT NOT NULL DEFAULT 0,
    cost_usd NUMERIC(12,6) NOT NULL DEFAULT 0,
    latency_ms INT NOT NULL DEFAULT 0,
    request_id TEXT,
    tool_count INT NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

-- Create initial partitions (current + next month)
-- pg_partman will manage future partitions in production
```

---

## 5. Repository Implementations

Repositories exist for **aggregate roots only** — entities that are the entry point for a bounded context. Child entities and lookup tables are accessed via `AppDbContext` directly in vertical-slice handlers. This avoids over-abstraction while maintaining testability for core operations.

### Aggregate root repositories:

| Repository | Aggregate Root | Child Access |
|------------|---------------|--------------|
| `IProjectRepository` | Project | Members, Agents accessed through Project includes |
| `ITaskRepository` | AgenticTask | Attempts, Dependencies accessed through Task includes |
| `ISessionRepository` | Session | Messages, Channels accessed through Session includes |
| `IWorkflowRepository` | WorkflowDefinition, WorkflowRun | Schedules, HumanInputRequests accessed through includes |

Non-aggregate queries (learnings, decisions, artifacts, events, documents, costs) are handled directly by vertical-slice endpoints using `AppDbContext`. No repository wrapping for simple reads.

### Files to CREATE:

```
src/AgenticWorkforce.Infrastructure/Repositories/ProjectRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/TaskRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/SessionRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/WorkflowRepository.cs
src/AgenticWorkforce.Infrastructure/DependencyInjection.cs
```

### ProjectRepository

```csharp
internal sealed class ProjectRepository(AppDbContext db) : IProjectRepository
{
    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Projects
            .Include(p => p.Members)
            .Include(p => p.Agents)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Project>> ListByMemberAsync(Guid userId, CancellationToken ct = default)
        => await db.Projects
            .Where(p => p.Members.Any(m => m.UserId == userId))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<Project> CreateAsync(Project project, CancellationToken ct = default)
    {
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<Project> UpdateAsync(Project project, CancellationToken ct = default)
    {
        db.Projects.Update(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
        => await db.Projects.AnyAsync(p => p.Name == name, ct);
}
```

### TaskRepository

```csharp
internal sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    public async Task<AgenticTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Tasks
            .Include(t => t.Attempts)
            .Include(t => t.Dependencies)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<AgenticTask>> GetByProjectIdAsync(
        Guid projectId, TaskStatus? status = null, CancellationToken ct = default)
    {
        var query = db.Tasks.Where(t => t.ProjectId == projectId);
        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        return await query.OrderBy(t => t.CreatedAt).ToListAsync(ct);
    }

    public async Task<AgenticTask> CreateAsync(AgenticTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task<AgenticTask> UpdateAsync(AgenticTask task, CancellationToken ct = default)
    {
        db.Tasks.Update(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task<IReadOnlyList<AgenticTask>> GetBoardAsync(Guid projectId, CancellationToken ct = default)
        => await db.Tasks
            .Where(t => t.ProjectId == projectId)
            .Include(t => t.Dependencies)
            .Include(t => t.Dependents)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
}
```

### DependencyInjection.cs (Infrastructure)

```csharp
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        var dataSource = DataSourceFactory.Create(connectionString);

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(dataSource, npgsql => npgsql.EnableRetryOnFailure(3)));

        services.AddScoped<AuditInterceptor>();

        // Aggregate root repositories only
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();

        // Services
        services.AddScoped<IEmbeddingService, StubEmbeddingService>();
        services.AddScoped<IDocumentStore, LocalFileDocumentStore>();

        return services;
    }
}
```

Note: Vertical-slice endpoints that need simple queries (learnings, events, costs, documents) inject `AppDbContext` directly. This is intentional — repositories add no value for read-only paginated queries.

---

## 6. Central Package Management

### File to CREATE: `Directory.Packages.props` (solution root)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <!-- EF Core -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.8" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.8" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.1" />
    <PackageVersion Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />

    <!-- ASP.NET Core / Auth -->
    <PackageVersion Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="1.5.1" />
    <PackageVersion Include="Azure.Identity" Version="1.21.0" />
    <PackageVersion Include="Microsoft.ApplicationInsights.AspNetCore" Version="3.1.1" />
    <PackageVersion Include="Microsoft.Identity.Web" Version="4.9.0" />
    <PackageVersion Include="NetEscapades.AspNetCore.SecurityHeaders" Version="1.3.1" />
    <PackageVersion Include="Swashbuckle.AspNetCore" Version="6.9.0" />

    <!-- Logging -->
    <PackageVersion Include="Serilog.AspNetCore" Version="10.0.0" />
    <PackageVersion Include="Serilog.Enrichers.Sensitive" Version="2.1.0" />
    <PackageVersion Include="Serilog.Formatting.Compact" Version="3.0.0" />
    <PackageVersion Include="Serilog.Sinks.Console" Version="6.1.1" />

    <!-- AI -->
    <PackageVersion Include="Microsoft.Extensions.AI" Version="9.6.0" />
    <PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="9.6.0" />

    <!-- OpenTelemetry -->
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.12.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.12.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.12.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.12.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.12.0" />

    <!-- Testing -->
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.8" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.5.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.0" />
    <PackageVersion Include="FluentAssertions" Version="8.3.0" />
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>
</Project>
```

All `.csproj` files updated to remove `Version=` attributes (use `VersionOverride` only if needed).

---

## 7. Application Settings

### File to CREATE: `src/AgenticWorkforce.Api/appsettings.json`

```json
{
  // Options: AzureAd, KeyVault, ConnectionStrings, Cors, RateLimiting
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "",
    "TenantId": "",
    "ClientId": "",
    "Audience": ""
  },
  "KeyVault": {
    "Uri": ""
  },
  "ConnectionStrings": {
    "agenticworkforce": ""
  },
  "Cors": {
    "AllowedOrigins": []
  },
  "RateLimiting": {
    "PermitLimit": 600,
    "WindowSeconds": 60,
    "StrictPermitLimit": 10
  }
}
```

### File to CREATE: `src/AgenticWorkforce.Api/appsettings.Development.json`

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "localhost",
    "TenantId": "common",
    "ClientId": "local-dev",
    "Audience": "local-dev"
  },
  "ConnectionStrings": {
    "agenticworkforce": "Host=localhost;Database=agenticworkforce;Username=postgres;Password=postgres"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000", "http://localhost:5173"]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  }
}
```

---

## 8. Worker Program.cs Fix

Replace the current skeleton with proper registration:

```csharp
using AgenticWorkforce.Infrastructure;
using AgenticWorkforce.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("agenticworkforce")
    ?? throw new InvalidOperationException("Connection string 'agenticworkforce' is required.");

builder.Services.AddInfrastructure(connectionString);

// Durable Task, Agent Runtime registered in Phase 6+

var host = builder.Build();
await host.RunAsync();
```

---

## 9. Integration Test Update

Update `ApiWebApplicationFactory` to use `DataSourceFactory`:

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseEnvironment("Testing");
    builder.ConfigureServices(services =>
    {
        // Remove existing registrations
        var descriptors = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                     || d.ServiceType.FullName?.Contains("NpgsqlDataSource") == true)
            .ToList();
        foreach (var d in descriptors) services.Remove(d);

        // Register with Testcontainers
        var dataSource = DataSourceFactory.Create(_postgres.GetConnectionString());
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(dataSource, npgsql => npgsql.EnableRetryOnFailure(3)));
    });
}
```

Add a basic smoke test:

```csharp
public class DatabaseSmokeTests : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Database_MigratesAndSeeds_Successfully()
    {
        // Arrange
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Act
        await db.Database.MigrateAsync();

        // Assert
        (await db.Database.CanConnectAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task ProjectRepository_CreateAndRetrieve_Works()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

        var project = new Project { Name = "Test", Objective = "Test objective" };
        await repo.CreateAsync(project);

        var retrieved = await repo.GetByIdAsync(project.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test");
    }
}
```

---

## File Summary

### CREATE (new files)

```
Directory.Packages.props
src/AgenticWorkforce.Infrastructure/Data/DataSourceFactory.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/ProjectConfigurations.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/TaskConfigurations.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/KnowledgeConfigurations.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/DocumentConfigurations.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/SessionConfigurations.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/WorkflowConfigurations.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/EventConfigurations.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/IdentityConfigurations.cs
src/AgenticWorkforce.Infrastructure/Data/Configurations/PlatformConfigurations.cs
src/AgenticWorkforce.Infrastructure/Repositories/ProjectRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/TaskRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/SessionRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/WorkflowRepository.cs
src/AgenticWorkforce.Infrastructure/Services/StubEmbeddingService.cs
src/AgenticWorkforce.Infrastructure/Services/LocalFileDocumentStore.cs
src/AgenticWorkforce.Infrastructure/DependencyInjection.cs
src/AgenticWorkforce.Api/appsettings.json
src/AgenticWorkforce.Api/appsettings.Development.json
src/AgenticWorkforce.Infrastructure/Migrations/ (generated)
tests/AgenticWorkforce.Api.Tests.Integration/DatabaseSmokeTests.cs
```

### REWRITE (replace contents)

```
src/AgenticWorkforce.Infrastructure/Data/AppDbContext.cs
src/AgenticWorkforce.Api/Program.cs (DB registration section only)
src/AgenticWorkforce.Worker/Program.cs
tests/AgenticWorkforce.Api.Tests.Integration/ApiWebApplicationFactory.cs
src/AgenticWorkforce.Infrastructure/AgenticWorkforce.Infrastructure.csproj (remove Version attrs)
src/AgenticWorkforce.Api/AgenticWorkforce.Api.csproj (remove Version attrs)
src/AgenticWorkforce.Agents/AgenticWorkforce.Agents.csproj (remove Version attrs)
```

### DELETE then RECREATE

```
src/AgenticWorkforce.Infrastructure/Data/Configurations/ (delete 8 old files, create 9 new domain-grouped files)
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet ef migrations list --project src/AgenticWorkforce.Infrastructure --startup-project src/AgenticWorkforce.Api` shows `InitialSchema` and `CreatePartitionedTables`
3. `dotnet test AgenticWorkforce.slnx` — `DatabaseSmokeTests` pass (Testcontainers creates real PostgreSQL, migration applies, CRUD works)
4. Configuration files exist in `Data/Configurations/` grouped by domain (9 files, each <200 lines)
5. Both Api and Worker use `DataSourceFactory.Create()` for consistent enum mapping
6. `Directory.Packages.props` exists and all `.csproj` files have no `Version=` attributes on PackageReferences
7. `appsettings.json` documents all required config keys
8. Worker `Program.cs` compiles and references Infrastructure correctly

---

## Phase 1 carry-over: native PG enums actually wired

Phase 1 declared the PG enum *types* via `HasPostgresEnum<T>()` but every enum
column on every entity still landed as `integer` in the generated migration —
Npgsql.EFCore 10 no longer auto-maps CLR enum properties to the corresponding
native enum column type. Phase 2 closed that gap so columns now store the
native enum (e.g. `status project_status NOT NULL`) and parameters are sent as
the native enum at insert time.

The working setup needs **four** coordinated registrations; only the data
source + model-builder pair documented in the Npgsql docs is empirically
insufficient. Full reference in
[`003-database-schema.md` §4.1](../002-architecture/003-database-schema.md#41-wiring-clr-enums-to-native-postgresql-enums).
Single source of truth for the CLR↔PG name map: [`PgEnumRegistry`](../../src/AgenticWorkforce.Infrastructure/Data/PgEnumRegistry.cs).

This is recorded as an implementation note, not an ADR — the architectural
decision (native PG enums) was already taken; this is just the recipe that
makes it work with the current library versions.

---

## Goal Command

```
/goal Data layer is complete: AppDbContext has all entities configured inline with PostgreSQL native enums, xmin concurrency, HNSW vector indexes, and partitioned tables. EF migration generates successfully. Repository implementations exist for Project, Task, Session, Workflow, Knowledge, Event. Directory.Packages.props centralises all package versions. Both Api and Worker use DataSourceFactory for consistent NpgsqlDataSource setup. Verify: dotnet build exits 0, dotnet ef migrations list shows migrations, dotnet test exits 0 with DatabaseSmokeTests passing against Testcontainers PostgreSQL.
```
