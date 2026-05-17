# Phase 10: Integration Testing

**Status:** Not Started
**Depends On:** Phase 9 (Audit & Compliance)
**Verification:** `dotnet test AgenticWorkforce.slnx` — all tests green, coverage report generated

---

## Objective

Build comprehensive end-to-end tests that exercise the full platform stack: API → Repository → Database → Agent Runtime → Workflow Engine → Audit Pipeline. These tests prove the system works as a whole before deployment. No mocks — real PostgreSQL (Testcontainers), real Redis, real agent execution (via StubChatClient).

---

## Test Architecture

```
tests/
├── AgenticWorkforce.Domain.Tests.Unit/          (pure domain logic, no I/O)
├── AgenticWorkforce.Api.Tests.Unit/             (handler logic, validation)
└── AgenticWorkforce.Api.Tests.Integration/      (full stack with Testcontainers)
    ├── ApiWebApplicationFactory.cs              (shared factory)
    ├── TestAuthHandler.cs                       (fake JWT for test users)
    ├── Fixtures/
    │   ├── TestUsers.cs                         (predefined test identities)
    │   └── TestData.cs                          (seed helpers)
    ├── Features/
    │   ├── Projects/ProjectLifecycleTests.cs
    │   ├── Tasks/TaskStateMachineTests.cs
    │   ├── Tasks/SegregationOfDutiesTests.cs
    │   ├── Sessions/SessionLifecycleTests.cs
    │   ├── Auth/AuthorizationTests.cs
    │   ├── Auth/BolaPreventionTests.cs
    │   ├── Workflows/WorkflowExecutionTests.cs
    │   ├── Knowledge/LearningLifecycleTests.cs
    │   └── Admin/AdminEndpointTests.cs
    ├── Agents/
    │   ├── AgentExecutionTests.cs
    │   ├── BudgetEnforcementTests.cs
    │   └── VerificationPipelineTests.cs
    ├── Audit/
    │   ├── AuditPipelineEndToEndTests.cs
    │   └── HashChainIntegrityTests.cs
    └── Performance/
        └── EndpointLatencyTests.cs
```

---

## 1. Shared Test Infrastructure

### TestAuthHandler (Fake JWT for all roles)

```csharp
// Allows tests to authenticate as any user/role without real Entra ID
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";
    public static string CurrentUserId { get; set; } = TestUsers.Owner.Id.ToString();
    public static string[] CurrentRoles { get; set; } = [Roles.Owner];

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new("oid", CurrentUserId),
            new(ClaimTypes.Email, $"{CurrentUserId}@test.com"),
            new("name", "Test User"),
        };
        claims.AddRange(CurrentRoles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

### TestUsers

```csharp
public static class TestUsers
{
    public static readonly TestUser PlatformAdmin = new(Guid.Parse("00000000-0000-0000-0000-000000000001"),
        "admin@test.com", [Roles.PlatformAdmin]);
    public static readonly TestUser Owner = new(Guid.Parse("00000000-0000-0000-0000-000000000002"),
        "owner@test.com", [Roles.Owner]);
    public static readonly TestUser Operator = new(Guid.Parse("00000000-0000-0000-0000-000000000003"),
        "operator@test.com", [Roles.Operator]);
    public static readonly TestUser Reviewer = new(Guid.Parse("00000000-0000-0000-0000-000000000004"),
        "reviewer@test.com", [Roles.Reviewer]);
    public static readonly TestUser Viewer = new(Guid.Parse("00000000-0000-0000-0000-000000000005"),
        "viewer@test.com", [Roles.Viewer]);
    public static readonly TestUser Outsider = new(Guid.Parse("00000000-0000-0000-0000-000000000099"),
        "outsider@test.com", [Roles.Viewer]);

    public record TestUser(Guid Id, string Email, string[] Roles);
}
```

### Enhanced ApiWebApplicationFactory

```csharp
public class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16").Build();
    private readonly RedisContainer _redis = new RedisBuilder().Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Replace DB
            ReplaceDbContext(services);
            // Replace Redis
            ReplaceRedis(services);
            // Replace auth with test scheme
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
            // Seed test users
            SeedTestUsers(services);
        });
    }

    public HttpClient CreateAuthenticatedClient(TestUsers.TestUser user)
    {
        TestAuthHandler.CurrentUserId = user.Id.ToString();
        TestAuthHandler.CurrentRoles = user.Roles;
        return CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
```

---

## 2. End-to-End Flow Tests

### ProjectLifecycleTests

```csharp
public class ProjectLifecycleTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task FullLifecycle_CreateToArchive_AllStatesValid()
    {
        var client = factory.CreateAuthenticatedClient(TestUsers.Owner);

        // Create
        var create = await client.PostAsJsonAsync("/api/v1/projects",
            new { Name = "Test Project", Objective = "Test objective" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = await create.Content.ReadFromJsonAsync<ProjectResponse>();

        // Get
        var get = await client.GetAsync($"/api/v1/projects/{project!.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        // Pause
        var pause = await client.PostAsync($"/api/v1/projects/{project.Id}/pause", null);
        pause.StatusCode.Should().Be(HttpStatusCode.OK);

        // Resume
        var resume = await client.PostAsync($"/api/v1/projects/{project.Id}/resume", null);
        resume.StatusCode.Should().Be(HttpStatusCode.OK);

        // Archive
        var archive = await client.PostAsync($"/api/v1/projects/{project.Id}/archive", null);
        archive.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify final state
        var final = await client.GetFromJsonAsync<ProjectResponse>($"/api/v1/projects/{project.Id}");
        final!.Status.Should().Be("Archived");
    }
}
```

### TaskStateMachineTests

```csharp
public class TaskStateMachineTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task TaskLifecycle_ProposedToCompleted_ValidTransitions()
    {
        // Setup: create project, add reviewer as member
        var ownerClient = factory.CreateAuthenticatedClient(TestUsers.Owner);
        var reviewerClient = factory.CreateAuthenticatedClient(TestUsers.Reviewer);
        var projectId = await CreateProjectWithMembers(ownerClient);

        // Create task (Proposed)
        var task = await CreateTask(ownerClient, projectId, "Scan for vulnerabilities");
        task.Status.Should().Be("Proposed");

        // Approve (by reviewer — not creator)
        var approve = await reviewerClient.PostAsync(
            $"/api/v1/projects/{projectId}/tasks/{task.Id}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify state is Approved
        var approved = await ownerClient.GetFromJsonAsync<TaskResponse>(
            $"/api/v1/projects/{projectId}/tasks/{task.Id}");
        approved!.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task InvalidTransition_CompletedToApproved_Returns400()
    {
        // Setup: task in Completed state
        // Try to approve → should fail
    }

    [Fact]
    public async Task RetryFailedTask_MovesToApproved()
    {
        // Setup: task in Failed state
        // Retry → status becomes Approved
    }
}
```

### SegregationOfDutiesTests

```csharp
public class SegregationOfDutiesTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Creator_CannotApproveOwnTask_Returns403()
    {
        var client = factory.CreateAuthenticatedClient(TestUsers.Owner);
        var projectId = await CreateProject(client);
        var task = await CreateTask(client, projectId, "Test task");

        // Owner created it, owner tries to approve it
        var approve = await client.PostAsync(
            $"/api/v1/projects/{projectId}/tasks/{task.Id}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DifferentUser_CanApproveTask_Returns200()
    {
        var ownerClient = factory.CreateAuthenticatedClient(TestUsers.Owner);
        var reviewerClient = factory.CreateAuthenticatedClient(TestUsers.Reviewer);
        var projectId = await CreateProjectWithMember(ownerClient, TestUsers.Reviewer);
        var task = await CreateTask(ownerClient, projectId, "Test task");

        var approve = await reviewerClient.PostAsync(
            $"/api/v1/projects/{projectId}/tasks/{task.Id}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

### BolaPreventionTests

```csharp
public class BolaPreventionTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task NonMember_CannotAccessProject_Returns403()
    {
        var ownerClient = factory.CreateAuthenticatedClient(TestUsers.Owner);
        var outsiderClient = factory.CreateAuthenticatedClient(TestUsers.Outsider);

        var projectId = await CreateProject(ownerClient);

        var get = await outsiderClient.GetAsync($"/api/v1/projects/{projectId}");
        get.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_CannotCreateTask_Returns403()
    {
        var ownerClient = factory.CreateAuthenticatedClient(TestUsers.Owner);
        var viewerClient = factory.CreateAuthenticatedClient(TestUsers.Viewer);

        var projectId = await CreateProjectWithMember(ownerClient, TestUsers.Viewer);

        var create = await viewerClient.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/tasks",
            new { Objective = "Test", Type = "AgentTask" });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

---

## 3. Agent Execution Tests

### AgentExecutionTests

```csharp
public class AgentExecutionTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task RunAdHoc_ExecutesAgentAndRecordsResult()
    {
        var client = factory.CreateAuthenticatedClient(TestUsers.Operator);
        var projectId = await CreateProjectWithSeedTeam(client);

        // Run ad-hoc task
        var run = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/executions/run",
            new { AgentName = "system.verifier", Objective = "Verify test output" });
        run.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await run.Content.ReadFromJsonAsync<RunResponse>();

        // Wait briefly for background processing
        await Task.Delay(2000);

        // Verify task was created and completed (via stub)
        var task = await client.GetFromJsonAsync<TaskResponse>(
            $"/api/v1/projects/{projectId}/tasks/{result!.TaskId}");
        task!.Status.Should().BeOneOf("Completed", "Running");
    }
}
```

### BudgetEnforcementTests

```csharp
public class BudgetEnforcementTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task ExceedBudget_AgentExecutionHalts()
    {
        // Setup: project with $0.01 budget ceiling
        // Execute agent (stub returns usage that exceeds budget)
        // Verify: task status = Failed, error contains "AGENT_BUDGET_EXCEEDED"
    }

    [Fact]
    public async Task BudgetWarningAt80Percent_EventPublished()
    {
        // Setup: project with $1.00 budget, spend $0.85
        // Verify: BudgetWarning event published via SignalR
    }
}
```

---

## 4. Workflow Tests

### WorkflowExecutionTests

```csharp
public class WorkflowExecutionTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task LinearWorkflow_StartToEnd_AllTasksCompleted()
    {
        var client = factory.CreateAuthenticatedClient(TestUsers.Operator);
        var projectId = await CreateProjectWithSeedTeam(client);

        // Create workflow: Start → AgentTask → End
        var workflow = await CreateLinearWorkflow(client, projectId);

        // Run workflow
        var run = await client.PostAsync(
            $"/api/v1/projects/{projectId}/workflows/{workflow.Id}/run", null);
        run.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait for completion
        await WaitForWorkflowCompletion(client, projectId, timeout: TimeSpan.FromSeconds(30));

        // Verify tasks created
        var tasks = await client.GetFromJsonAsync<PagedResult<TaskResponse>>(
            $"/api/v1/projects/{projectId}/tasks?source=Workflow");
        tasks!.Items.Should().HaveCountGreaterThan(0);
        tasks.Items.Should().OnlyContain(t => t.Status == "Completed");
    }

    [Fact]
    public async Task WorkflowWithHumanGate_PausesAndResumesOnApproval()
    {
        var operatorClient = factory.CreateAuthenticatedClient(TestUsers.Operator);
        var reviewerClient = factory.CreateAuthenticatedClient(TestUsers.Reviewer);
        var projectId = await CreateProjectWithMembers(operatorClient);

        // Create workflow: Start → AgentTask → HumanDecision → End
        var workflow = await CreateWorkflowWithHumanGate(operatorClient, projectId);

        // Run workflow
        await operatorClient.PostAsync(
            $"/api/v1/projects/{projectId}/workflows/{workflow.Id}/run", null);

        // Wait for human input request
        await WaitForPendingInput(operatorClient, projectId, timeout: TimeSpan.FromSeconds(15));

        // Verify pending request exists
        var pending = await reviewerClient.GetFromJsonAsync<List<HumanInputResponse>>(
            $"/api/v1/projects/{projectId}/human-input/pending");
        pending.Should().HaveCount(1);

        // Respond (decision is the queryable enum; response is free-text justification)
        var respond = await reviewerClient.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/human-input/{pending![0].Id}/respond",
            new { Decision = "Approved", Response = "Looks good — proceed" });
        respond.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify workflow completes
        await WaitForWorkflowCompletion(operatorClient, projectId, timeout: TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task RejectedHumanGate_FailsWorkflow_AndRecordsDecision()
    {
        // Same setup as above, but reviewer responds with Decision = "Rejected".
        // Asserts: HumanInputRequest.Decision == Rejected in DB,
        //          WorkflowRun.Status == Failed,
        //          decision is queryable via GET /projects/{id}/human-input?decision=Rejected
    }
}
```

---

## 5. Audit Tests

### AuditPipelineEndToEndTests

```csharp
public class AuditPipelineEndToEndTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task AgentExecution_CreatesAuditRecordsWithHashChain()
    {
        var client = factory.CreateAuthenticatedClient(TestUsers.Operator);
        var projectId = await CreateProjectWithSeedTeam(client);

        // Execute agent
        await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/executions/run",
            new { AgentName = "system.verifier", Objective = "Test" });

        // Wait for audit drain
        await Task.Delay(3000);

        // Verify LlmCall records exist
        var costs = await client.GetFromJsonAsync<CostSummaryResponse>(
            $"/api/v1/projects/{projectId}/costs/summary");
        costs!.TotalUsd.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HashChainIntegrity_VerifiesCleanChain()
    {
        var adminClient = factory.CreateAuthenticatedClient(TestUsers.PlatformAdmin);
        // After some agent execution...
        var projectId = await SetupProjectWithExecution(adminClient);

        var verify = await adminClient.PostAsync(
            $"/api/v1/admin/audit/verify/{projectId}", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await verify.Content.ReadFromJsonAsync<AuditVerifyResponse>();
        result!.ChainValid.Should().BeTrue();
    }
}
```

---

## 6. Performance Baseline

### EndpointLatencyTests

```csharp
public class EndpointLatencyTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task ListProjects_Under50msP95()
    {
        var client = factory.CreateAuthenticatedClient(TestUsers.Owner);
        await SeedProjects(client, count: 20);

        var latencies = new List<long>();
        for (int i = 0; i < 100; i++)
        {
            var sw = Stopwatch.StartNew();
            await client.GetAsync("/api/v1/projects");
            latencies.Add(sw.ElapsedMilliseconds);
        }

        var p95 = latencies.OrderBy(l => l).ElementAt(94);
        p95.Should().BeLessThan(50, "GET /projects p95 should be under 50ms");
    }

    [Fact]
    public async Task GetTask_Under30msP95()
    {
        // Similar pattern for task retrieval
    }
}
```

---

## 7. Test Naming Convention

Per AGENTS.md: `{Method}_{Scenario}_{ExpectedOutcome}`

Examples:
- `CreateProject_ValidRequest_Returns201`
- `ApproveTask_ByCreator_Returns403`
- `ListProjects_NonMember_ReturnsEmpty`
- `RunWorkflow_WithHumanGate_PausesAtDecision`
- `ExceedBudget_DuringExecution_HaltsWithError`

---

## File Summary

### Files to CREATE (~20 files)

```
tests/AgenticWorkforce.Api.Tests.Integration/TestAuthHandler.cs
tests/AgenticWorkforce.Api.Tests.Integration/Fixtures/TestUsers.cs
tests/AgenticWorkforce.Api.Tests.Integration/Fixtures/TestData.cs
tests/AgenticWorkforce.Api.Tests.Integration/Fixtures/TestHelpers.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Projects/ProjectLifecycleTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Tasks/TaskStateMachineTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Tasks/SegregationOfDutiesTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Auth/AuthorizationTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Auth/BolaPreventionTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Sessions/SessionLifecycleTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Workflows/WorkflowExecutionTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Knowledge/LearningLifecycleTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Admin/AdminEndpointTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Agents/AgentExecutionTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Agents/BudgetEnforcementTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Agents/VerificationPipelineTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Audit/AuditPipelineEndToEndTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Audit/HashChainIntegrityTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Performance/EndpointLatencyTests.cs
```

### Files to MODIFY

```
tests/AgenticWorkforce.Api.Tests.Integration/ApiWebApplicationFactory.cs — Add Redis, test auth, seeding
tests/AgenticWorkforce.Api.Tests.Integration/AgenticWorkforce.Api.Tests.Integration.csproj — Add Testcontainers.Redis
Directory.Packages.props — Add Testcontainers.Redis
```

### Package Additions

```xml
<PackageVersion Include="Testcontainers.Redis" Version="4.5.0" />
```

---

## Verification Criteria

1. `dotnet test AgenticWorkforce.slnx` — ALL tests pass (0 failures)
2. Tests use real PostgreSQL + real Redis via Testcontainers
3. No mocks anywhere — real implementations, real DI, real middleware
4. Auth tests cover every role combination (PlatformAdmin, Owner, Operator, Reviewer, Viewer, Outsider)
5. BOLA tests prove user A cannot access user B's project
6. Segregation of duties test proves creator != approver enforcement
7. Workflow test proves human gate pauses and resumes correctly
8. Audit test proves hash chain integrity after agent execution
9. Performance test establishes p95 baselines for core endpoints
10. Test report shows >80% code coverage on Api and Domain projects

---

## Goal Command

```
/goal Integration test suite complete: 20 test files covering full platform lifecycle. Tests use real PostgreSQL + Redis via Testcontainers, fake auth via TestAuthHandler, no mocks. Covers: project CRUD lifecycle, task state machine (all valid/invalid transitions), segregation of duties, BOLA prevention, workflow execution with human gates, agent execution via stub, budget enforcement, audit hash chain integrity, p95 latency baselines. Verify: dotnet test AgenticWorkforce.slnx exits 0 with all tests passing. Stop after 30 turns.
```
