# C# Coding Standards

## Naming

| Construct | Convention | Example |
|-----------|-----------|---------|
| Classes, records, interfaces | PascalCase | `ProjectService`, `IProjectRepository` |
| Interfaces | `I` prefix | `ICurrentUserAccessor` |
| Methods | PascalCase | `CreateProjectAsync` |
| Async methods | `Async` suffix | `GetByIdAsync` |
| Properties | PascalCase | `DisplayName` |
| Private fields | `_camelCase` | `_projectRepository` |
| Local variables | camelCase | `projectId` |
| Constants | PascalCase | `DefaultPageSize` |
| Enums | PascalCase, singular | `TaskStatus` (not `TaskStatuses`) |
| Error codes | UPPER_SNAKE_CASE | `AUTH_TOKEN_EXPIRED` |

## File Organisation

- One public type per file (exceptions: tightly coupled pairs like entity + configuration)
- Namespaces match folder structure: `AgenticWorkforce.Api.Core.Auth`
- Files ≤500 lines average, hard limit 1000 lines
- No `// TODO` without a linked issue number

## Language Features (C# 14 / .NET 10)

- **Records** for DTOs and immutable data: `public record CreateProjectRequest(...)`
- **Primary constructors** for DI: `public class ProjectHandler(IProjectRepository repo)`
- **Pattern matching** over if-else chains: `status switch { ... }`
- **Collection expressions**: `IReadOnlyList<string> Roles { get; init; } = []`
- **File-scoped namespaces**: `namespace AgenticWorkforce.Domain.Entities;`
- **Raw string literals** for multi-line: `"""..."""`
- **Required members** for mandatory properties: `public required string Name { get; init; }`

## Async

- `async Task<T>` with `CancellationToken ct` on every async method
- Never `.Result` or `.Wait()` — async all the way
- Use `ValueTask<T>` only when measured profiling shows benefit
- `ConfigureAwait(false)` is unnecessary in ASP.NET Core — do not add it

## DateTime

- **Always `DateTime.UtcNow`** — never `DateTime.Now`, never `DateTimeOffset`
- Store as `TIMESTAMPTZ` in PostgreSQL (Npgsql maps `DateTime` with `Kind=Utc` correctly)
- Return as ISO 8601 UTC in JSON: `"2026-05-15T10:30:00Z"`

## Error Handling

- Throw typed exceptions from `AgenticWorkforce.Domain.Exceptions` hierarchy
- Never `throw new Exception(...)` — always `throw new NotFoundException(...)` etc.
- Never catch-and-swallow: `catch (Exception) { }` is forbidden
- Let `GlobalExceptionHandler` map exceptions to ProblemDetails
- Fail fast on missing configuration at startup: `?? throw new InvalidOperationException("...")`

## Logging

- **Structured only**: `logger.LogInformation("Created project {ProjectId}", id)`
- **Never string interpolation**: `logger.LogInformation($"Created {id}")` is forbidden
- **Log at boundaries**: entry/exit of public API methods, external service calls
- **Never log secrets**: tokens, passwords, API keys, connection strings
- **PII masking**: Serilog.Enrichers.Sensitive handles emails and IBANs automatically

## Configuration

- `IOptions<T>` / `IOptionsSnapshot<T>` for strongly-typed settings
- Never `IConfiguration["key"]` in business logic — bind to a typed class
- No hardcoded values — even timeouts and retry counts come from configuration
- Secrets in Azure Key Vault (prod) or `appsettings.Development.json` (dev)

## Dependency Injection

- Register in `Program.cs` or dedicated extension methods
- Scoped for request-bound services (repositories, current user)
- Singleton for stateless services (factories, configuration wrappers)
- Never resolve from `IServiceProvider` manually — use constructor injection

## EF Core

- All EF Core code in `AgenticWorkforce.Infrastructure` — never in Domain or Api
- `IEntityTypeConfiguration<T>` for fluent configuration (not data annotations)
- `.HasConversion<string>()` for all enums
- `.HasColumnType("jsonb")` for flexible JSON payloads
- `.HasQueryFilter()` for soft-delete entities
- Never call `SaveChangesAsync` outside repositories
- Always use parameterised queries — never `FromSqlRaw` with concatenated input

## Anti-Patterns (Forbidden)

- Controller → Service → Repository layering (use vertical slices)
- `Moq` or any mocking framework (use real implementations)
- `InMemoryDatabase` for tests (use Testcontainers)
- `dynamic` keyword
- `Thread.Sleep` in production code
- `Process.Start` with user input
- `Type.GetType` with user input
- String concatenation in SQL queries
- `Console.WriteLine` instead of `ILogger`
