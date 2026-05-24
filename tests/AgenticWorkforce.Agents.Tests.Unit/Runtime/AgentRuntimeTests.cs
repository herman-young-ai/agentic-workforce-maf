using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Runtime;

/// <summary>
/// Exercises the surface of <see cref="AgentRuntime"/> using hand-rolled fakes
/// (permitted by the testing standards for interfaces whose only production
/// implementation depends on infrastructure). The stub agent factory short-
/// circuits to a canned response so we can assert the runtime's projection of
/// the MAF response onto <see cref="AgentExecutionResult"/> without standing
/// up a real chat client pipeline.
/// </summary>
public class AgentRuntimeTests
{
    private static AgentCatalog Catalog(string? version = "1.0.0") => new()
    {
        Id = Guid.NewGuid(),
        AgentName = "test.agent",
        AgentType = "system",
        AgentVersion = version,
        Enabled = true
    };

    private static Project Project(Guid id) => new()
    {
        Id = id,
        Name = "P",
        Objective = "O"
    };

    private static IOptions<AgentRuntimeOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new AgentRuntimeOptions
        {
            DefaultProvider = "stub",
            DefaultModel = "stub-model"
        });

    private static AgentRuntime BuildSut(
        AgentCatalog? agent = null,
        Project? project = null,
        IAgentFactory? factory = null,
        IModelPricingService? pricing = null,
        AgentRuntimeOptions? opts = null)
    {
        agent ??= Catalog();
        project ??= Project(Guid.NewGuid());
        return new AgentRuntime(
            catalog: new FakeCatalog(agent),
            projects: new FakeProjects(project),
            projectAgents: new FakeProjectAgents(),
            factory: factory ?? new StubFactory(),
            pricing: pricing ?? new FakePricing(),
            clock: TimeProvider.System,
            options: opts is null ? Options() : Microsoft.Extensions.Options.Options.Create(opts),
            logger: NullLogger<AgentRuntime>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_MissingAgent_ThrowsNotFound()
    {
        var sut = new AgentRuntime(
            catalog: new FakeCatalog(null),
            projects: new FakeProjects(Project(Guid.NewGuid())),
            projectAgents: new FakeProjectAgents(),
            factory: new StubFactory(),
            pricing: new FakePricing(),
            clock: TimeProvider.System,
            options: Options(),
            logger: NullLogger<AgentRuntime>.Instance);

        var act = async () => await sut.ExecuteAsync(new AgentExecutionRequest(
            ProjectId: Guid.NewGuid(),
            TaskId: Guid.NewGuid(),
            AgentName: "missing.agent",
            Objective: "x"));

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("*missing.agent*");
    }

    [Fact]
    public async Task ExecuteAsync_CatalogMissingVersion_ThrowsInvalidState()
    {
        var sut = BuildSut(agent: Catalog(version: null));

        var act = async () => await sut.ExecuteAsync(new AgentExecutionRequest(
            ProjectId: Guid.NewGuid(), TaskId: Guid.NewGuid(),
            AgentName: "test.agent", Objective: "x"));

        await act.Should().ThrowAsync<InvalidStateException>().WithMessage("*AgentVersion*");
    }

    [Fact]
    public async Task ExecuteAsync_MissingProject_ThrowsNotFound()
    {
        var sut = new AgentRuntime(
            catalog: new FakeCatalog(Catalog()),
            projects: new FakeProjects(null),
            projectAgents: new FakeProjectAgents(),
            factory: new StubFactory(),
            pricing: new FakePricing(),
            clock: TimeProvider.System,
            options: Options(),
            logger: NullLogger<AgentRuntime>.Instance);

        var act = async () => await sut.ExecuteAsync(new AgentExecutionRequest(
            ProjectId: Guid.NewGuid(), TaskId: Guid.NewGuid(),
            AgentName: "test.agent", Objective: "x"));

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("*Project*");
    }

    [Fact]
    public async Task ExecuteAsync_NegativeTimeout_ThrowsValidation()
    {
        var sut = BuildSut();

        var act = async () => await sut.ExecuteAsync(new AgentExecutionRequest(
            ProjectId: Guid.NewGuid(), TaskId: Guid.NewGuid(),
            AgentName: "test.agent", Objective: "x",
            Timeout: TimeSpan.Zero));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*Timeout*positive*");
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_ProjectsUsageOntoResult()
    {
        var pricing = new FakePricing(perCall: 0.0042m);
        var factory = new StubFactory(usageInput: 100, usageOutput: 50);
        var sut = BuildSut(pricing: pricing, factory: factory);

        var result = await sut.ExecuteAsync(new AgentExecutionRequest(
            ProjectId: Guid.NewGuid(), TaskId: Guid.NewGuid(),
            AgentName: "test.agent", Objective: "do x"));

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("stub");
        result.InputTokens.Should().Be(100);
        result.OutputTokens.Should().Be(50);
        result.CostUsd.Should().Be(0.0042m);
    }

    // ---------- fakes ----------

    private sealed class FakeCatalog(AgentCatalog? entry) : IAgentCatalogRepository
    {
        public Task<AgentCatalog?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(entry);
        public Task<AgentCatalog?> GetByNameAsync(string n, CancellationToken ct = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<AgentCatalog>> ListEnabledAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentCatalog>> ListAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AgentCatalog> AddAsync(AgentCatalog a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AgentCatalog a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeProjects(Project? p) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(p);
        public Task<PagedResult<Project>> ListByMemberPagedAsync(Guid u, ProjectStatus? s, PagedQuery q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string n, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Project> CreateWithOwnerAsync(Project pr, Guid o, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Project pr, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeProjectAgents : IProjectAgentRepository
    {
        public Task<ProjectAgent?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ProjectAgent?>(null);
        public Task<IReadOnlyList<ProjectAgent>> ListByProjectAsync(Guid p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectAgent>>(Array.Empty<ProjectAgent>());
        public Task<ProjectAgent> AddAsync(ProjectAgent a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(ProjectAgent a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SeededProjectAgent>> SeedFromCatalogAsync(Guid p, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakePricing(decimal perCall = 0m) : IModelPricingService
    {
        public Task<decimal> EstimateInputCostAsync(string m, int i, CancellationToken ct = default) => Task.FromResult(perCall);
        public Task<decimal> CalculateCostAsync(string m, long i, long o, long cr, long cc, CancellationToken ct = default) => Task.FromResult(perCall);
    }

    private sealed class StubFactory(long usageInput = 100, long usageOutput = 50) : IAgentFactory
    {
        public Task<AIAgent> CreateAsync(
            AgentCatalog catalog, Project project, ProjectAgent? projectAgent,
            AgentExecutionContext context, CancellationToken ct = default)
        {
            var chatClient = new StubResponseClient(usageInput, usageOutput);
            return Task.FromResult<AIAgent>(new ChatClientAgent(chatClient, instructions: "test"));
        }
    }

    private sealed class StubResponseClient(long inTok, long outTok) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            var resp = new ChatResponse(new ChatMessage(ChatRole.Assistant, "stub reply"))
            {
                ModelId = options?.ModelId ?? "stub-model",
                Usage = new UsageDetails { InputTokenCount = inTok, OutputTokenCount = outTok }
            };
            return Task.FromResult(resp);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "stub reply");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
