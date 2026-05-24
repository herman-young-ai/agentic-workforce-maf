using AgenticWorkforce.Agents.Context;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Context;

public class ContextAssemblerTests
{
    [Fact]
    public async Task BuildAsync_AlwaysIncludesPcd_AndTaskDefinition()
    {
        var sut = new ContextAssembler(
            new FakePcd("PCD body"),
            new FakeLearnings(Array.Empty<ProjectLearning>()),
            new FakeTokenCounter());

        var packet = await sut.BuildAsync(
            projectId: Guid.NewGuid(),
            taskDefinition: "Find the thing",
            domainTags: null,
            modelId: "stub-model");

        packet.Messages.Should().HaveCountGreaterThanOrEqualTo(2);
        packet.Messages.Should().Contain(m => m.Text!.Contains("PCD body"));
        packet.Messages.Should().Contain(m => m.Text!.Contains("Find the thing"));
        packet.LayersIncluded.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task BuildAsync_OmitsLearnings_WhenBudgetTight()
    {
        // PCD spends ~95k of the 100k budget; learning reserve is 10k -> learnings skipped.
        var largePcd = new string('x', 95_000);
        var sut = new ContextAssembler(
            new FakePcd(largePcd),
            new FakeLearnings(new[]
            {
                new ProjectLearning { Title = "L", Body = "b", Status = LearningStatus.Active, Confidence = 0.9m }
            }),
            new FakeTokenCounter()); // 1 char = 1 token

        var packet = await sut.BuildAsync(
            projectId: Guid.NewGuid(),
            taskDefinition: null,
            domainTags: null,
            modelId: "stub-model");

        packet.Messages.Should().NotContain(m => m.Text!.Contains("## Learnings"));
    }

    [Fact]
    public async Task BuildAsync_IncludesLearnings_WhenBudgetAllows()
    {
        var sut = new ContextAssembler(
            new FakePcd("small pcd"),
            new FakeLearnings(new[]
            {
                new ProjectLearning { Title = "Use feature flags", Body = "Always", Status = LearningStatus.Active, Confidence = 0.95m }
            }),
            new FakeTokenCounter());

        var packet = await sut.BuildAsync(
            projectId: Guid.NewGuid(),
            taskDefinition: "do x",
            domainTags: null,
            modelId: "stub-model");

        packet.Messages.Should().Contain(m => m.Text!.Contains("Use feature flags"));
    }

    private sealed class FakePcd(string body) : IProjectContextService
    {
        public Task<ProjectContext> GetAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult(new ProjectContext { ContextData = body, ContextVersion = 1, SizeCharacters = body.Length, SizeTokens = body.Length });

        public Task<IReadOnlyList<ContextChange>> GetHistoryAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AddPrincipleAsync(Guid projectId, string principle, Guid addedById, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AddGuardrailAsync(Guid projectId, string guardrail, Guid addedById, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemovePrincipleAsync(Guid projectId, string principleId, Guid removedById, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveGuardrailAsync(Guid projectId, string guardrailId, Guid removedById, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeLearnings(IEnumerable<ProjectLearning> items) : ILearningRepository
    {
        public Task<PagedResult<ProjectLearning>> ListByProjectPagedAsync(Guid p, PagedQuery q, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<ProjectLearning>(items.ToList(), items.Count(), q.Page, q.PageSize));

        public Task<ProjectLearning?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ProjectLearning?>(null);
        public Task<ProjectLearning> AddAsync(ProjectLearning l, CancellationToken ct = default) => Task.FromResult(l);
        public Task UpdateAsync(ProjectLearning l, CancellationToken ct = default) => Task.CompletedTask;
        public Task RetractAsync(Guid id, string by, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task SupersedeAsync(Guid oldId, ProjectLearning replacement, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestPromotionAsync(Guid id, Guid by, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApprovePromotionAsync(Guid id, Guid by, CancellationToken ct = default) => Task.CompletedTask;
        public Task RejectPromotionAsync(Guid id, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PagedResult<ProjectLearning>> ListPendingPromotionsPagedAsync(PagedQuery q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<ProjectLearning>> ListApprovedAcrossProjectsPagedAsync(PagedQuery q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LearningMatch>> SearchByEmbeddingAsync(Guid p, float[] e, int l, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LearningMatch>> FindSimilarAsync(Guid id, int l, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeTokenCounter : ITokenCounter
    {
        public Task<int> CountAsync(string text, string modelId, CancellationToken ct = default) => Task.FromResult(text.Length);
    }
}
