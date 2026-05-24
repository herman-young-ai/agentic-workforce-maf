# Testing Standards

## Frameworks

| Tool | Purpose |
|------|---------|
| xUnit 2.9+ | Test framework |
| FluentAssertions 8.x | Readable assertions |
| Testcontainers.PostgreSql | Real PostgreSQL for integration tests |
| coverlet | Code coverage collection |

**No mocking frameworks.** No Moq, NSubstitute, or FakeItEasy. Tests use real implementations.

**Hand-rolled in-test fakes are permitted in unit tests** for interfaces whose only production implementation depends on infrastructure that the unit-test layer doesn't own (e.g. `IBudgetService` for a middleware test, `ILearningRepository` for an assembler test). The fake must be defined as a `private sealed class` inside the test file, implement the interface verbatim (no behavioural compromises), and throw `NotSupportedException` for members the test doesn't exercise. This is *not* a substitute for integration coverage — anything that touches PostgreSQL, Redis, or HTTP stays in integration tests with Testcontainers. Rationale: the rule above bans mocking *frameworks* (records-of-calls, dynamic stubs, "verify" assertions). It does not ban writing a small, explicit test double when the seam being tested genuinely sits above the infrastructure boundary.

## Test Types

### Unit Tests (`*.Tests.Unit`)

Test a single class with real dependencies where possible.
For classes requiring infrastructure (DbContext, Redis), defer to integration tests.

```csharp
public class ProjectStatusTransitionTests
{
    [Fact]
    public void Complete_WhenDraft_ThrowsInvalidStateException()
    {
        var project = new Project { Status = ProjectStatus.Draft };

        var act = () => project.Complete();

        act.Should().Throw<InvalidStateException>()
            .WithMessage("*cannot complete*Draft*");
    }
}
```

### Integration Tests (`*.Tests.Integration`)

Use `ApiWebApplicationFactory` with real PostgreSQL via Testcontainers.

```csharp
public class ProjectEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProjectEndpointTests(ApiWebApplicationFactory factory)
    {
        factory.StartAsync().GetAwaiter().GetResult();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateProject_WithValidRequest_Returns201()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/projects", new
        {
            name = "Test Project",
            description = "Integration test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

## Naming Convention

```
{Method}_{Scenario}_{ExpectedOutcome}
```

Examples:
- `GetById_WhenNotFound_ThrowsNotFoundException`
- `CreateProject_WithDuplicateName_Returns409`
- `ApproveTask_AsViewer_Returns403`
- `ExecuteAgent_WhenBudgetExhausted_ThrowsBudgetExceededException`

## What to Always Test

### Authentication (100% coverage)

- Valid token returns 200
- Expired token returns 401
- Wrong audience returns 401
- Missing token returns 401
- Valid API key returns 200
- Expired API key returns 401

### Authorization (100% coverage)

- Each role at each endpoint (Viewer, Operator, Reviewer, Owner, PlatformAdmin)
- Project-scoped: non-member returns 403
- Project-scoped: Viewer cannot approve tasks
- Agent role can access agent-specific endpoints

### Business Rules

- State transition validation (e.g., Draft → Active is allowed, Completed → Draft is not)
- Budget enforcement (agent halts when budget exhausted)
- Approval gates (task requiring approval cannot be auto-completed)

### Error Paths

- Not found returns 404 with `RES_NOT_FOUND` code
- Duplicate returns 409 with `RES_ALREADY_EXISTS` code
- Validation failure returns 422 with `VAL_*` code
- Rate limit returns 429 with `RATE_LIMITED` code

## What NOT to Test

- Private methods (test via public API)
- EF Core configuration (trust the framework)
- Framework middleware (trust ASP.NET Core)
- Exact log messages (fragile)

## Anti-Patterns (Forbidden)

- `Assert.True(result != null)` — use `result.Should().NotBeNull()`
- `Thread.Sleep` in tests — use async patterns
- Shared mutable state between tests — each test is independent
- Testing implementation details (internal method calls, private fields)
- InMemory EF Core — use Testcontainers with real PostgreSQL
