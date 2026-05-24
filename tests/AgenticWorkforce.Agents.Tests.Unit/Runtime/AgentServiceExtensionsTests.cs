using AgenticWorkforce.Agents;
using AgenticWorkforce.Agents.Context;
using AgenticWorkforce.Agents.Prompts;
using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Agents.Tools;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Queries;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Runtime;

/// <summary>
/// Phase 6 plan §Verification §3 — Worker DI must resolve <see cref="IAgentRuntime"/>,
/// <see cref="IAgentFactory"/>, <see cref="IChatClientFactory"/>, the prompt
/// assembler, the tool registry, and the context-assembly stack. Full Worker
/// resolution depends on PostgreSQL (Infrastructure), so this test stubs the
/// Infrastructure-side ports and verifies that <see cref="AgentServiceExtensions.AddAgentServices"/>
/// wires the Agents side completely.
/// </summary>
public class AgentServiceExtensionsTests
{
    private static IServiceProvider BuildContainer()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentRuntime:DefaultProvider"] = "stub",
            ["AgentRuntime:DefaultModel"]    = "stub-model"
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Stub Infrastructure-side ports so the Agents container can build.
        services.AddSingleton<IAgentCatalogRepository, StubCatalog>();
        services.AddSingleton<IProjectRepository, StubProjects>();
        services.AddSingleton<IProjectAgentRepository, StubProjectAgents>();
        services.AddSingleton<IBudgetService, StubBudget>();
        services.AddSingleton<IModelPricingService, StubPricing>();
        services.AddSingleton<ITokenCounter, StubTokenCounterImpl>();
        services.AddSingleton<IProjectContextService, StubPcd>();
        services.AddSingleton<ILearningRepository, StubLearnings>();
        services.AddSingleton<ILlmCallRepository, StubLlmCalls>();

        services.AddAgentServices(cfg);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Theory]
    [InlineData(typeof(IAgentRuntime))]
    [InlineData(typeof(IAgentFactory))]
    [InlineData(typeof(IChatClientFactory))]
    [InlineData(typeof(IPromptAssembler))]
    [InlineData(typeof(IToolRegistry))]
    [InlineData(typeof(IContextAssembler))]
    [InlineData(typeof(IProjectContextProviderFactory))]
    public void ResolveAgentSideService_Succeeds(Type serviceType)
    {
        using var root = (ServiceProvider)BuildContainer();
        using var scope = root.CreateScope();

        var instance = scope.ServiceProvider.GetService(serviceType);

        instance.Should().NotBeNull(
            $"AddAgentServices must register {serviceType.Name} for the Worker pipeline to construct.");
    }

    // ---------- minimal stub impls (registrations only — never invoked) ----------

    private sealed class StubCatalog : IAgentCatalogRepository
    {
        public Task<AgentCatalog?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AgentCatalog?>(null);
        public Task<AgentCatalog?> GetByNameAsync(string n, CancellationToken ct = default) => Task.FromResult<AgentCatalog?>(null);
        public Task<IReadOnlyList<AgentCatalog>> ListEnabledAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentCatalog>> ListAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AgentCatalog> AddAsync(AgentCatalog a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AgentCatalog a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubProjects : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Project?>(null);
        public Task<PagedResult<Project>> ListByMemberPagedAsync(Guid u, ProjectStatus? s, PagedQuery q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string n, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Project> CreateWithOwnerAsync(Project p, Guid o, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Project p, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubProjectAgents : IProjectAgentRepository
    {
        public Task<ProjectAgent?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ProjectAgent?>(null);
        public Task<IReadOnlyList<ProjectAgent>> ListByProjectAsync(Guid p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectAgent>>(Array.Empty<ProjectAgent>());
        public Task<ProjectAgent> AddAsync(ProjectAgent a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(ProjectAgent a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SeededProjectAgent>> SeedFromCatalogAsync(Guid p, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubBudget : IBudgetService
    {
        public Task<bool> CanSpendAsync(Guid p, Guid? s, decimal c, CancellationToken ct = default) => Task.FromResult(true);
        public Task RecordSpendAsync(Guid p, Guid? s, Guid? t, decimal c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<BudgetStatus> GetStatusAsync(Guid p, CancellationToken ct = default) => Task.FromResult(new BudgetStatus(100, 0, 100, false));
    }

    private sealed class StubPricing : IModelPricingService
    {
        public Task<decimal> EstimateInputCostAsync(string m, int i, CancellationToken ct = default) => Task.FromResult(0m);
        public Task<decimal> CalculateCostAsync(string m, long i, long o, long cr, long cc, CancellationToken ct = default) => Task.FromResult(0m);
    }

    private sealed class StubTokenCounterImpl : ITokenCounter
    {
        public Task<int> CountAsync(string t, string m, CancellationToken ct = default) => Task.FromResult(t.Length);
    }

    private sealed class StubPcd : IProjectContextService
    {
        public Task<ProjectContext> GetAsync(Guid p, CancellationToken ct = default) =>
            Task.FromResult(new ProjectContext { ContextData = string.Empty, ContextVersion = 1, SizeCharacters = 0, SizeTokens = 0 });

        public Task<IReadOnlyList<ContextChange>> GetHistoryAsync(Guid p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AddPrincipleAsync(Guid p, string s, Guid u, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AddGuardrailAsync(Guid p, string s, Guid u, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemovePrincipleAsync(Guid p, string id, Guid u, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveGuardrailAsync(Guid p, string id, Guid u, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubLearnings : ILearningRepository
    {
        public Task<PagedResult<ProjectLearning>> ListByProjectPagedAsync(Guid p, PagedQuery q, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<ProjectLearning>(Array.Empty<ProjectLearning>(), 0, q.Page, q.PageSize));

        public Task<ProjectLearning?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ProjectLearning?>(null);
        public Task<ProjectLearning> AddAsync(ProjectLearning l, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(ProjectLearning l, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RetractAsync(Guid id, string by, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SupersedeAsync(Guid oldId, ProjectLearning replacement, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RequestPromotionAsync(Guid id, Guid by, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ApprovePromotionAsync(Guid id, Guid by, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RejectPromotionAsync(Guid id, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<ProjectLearning>> ListPendingPromotionsPagedAsync(PagedQuery q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<ProjectLearning>> ListApprovedAcrossProjectsPagedAsync(PagedQuery q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LearningMatch>> SearchByEmbeddingAsync(Guid p, float[] e, int l, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LearningMatch>> FindSimilarAsync(Guid id, int l, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubLlmCalls : ILlmCallRepository
    {
        public Task AddBatchAsync(IReadOnlyList<LlmCall> calls, CancellationToken ct = default) => Task.CompletedTask;
    }
}
