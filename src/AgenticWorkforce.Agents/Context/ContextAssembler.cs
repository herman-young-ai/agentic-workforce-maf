using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Context;

internal sealed class ContextAssembler(
    IProjectContextService pcdService,
    ILearningRepository learnings,
    ITokenCounter tokens) : IContextAssembler
{
    private const int DefaultBudgetTokens = 100_000;
    private const int LearningsMinReserveTokens = 10_000;
    private const int LearningsPageSize = 20;

    public async Task<ContextPacket> BuildAsync(
        Guid projectId,
        string? taskDefinition,
        string[]? domainTags,
        string modelId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var consumed = 0;
        var layers = 0;
        var messages = new List<ChatMessage>();

        // Priority 0: PCD (never trimmed)
        var pcd = await pcdService.GetAsync(projectId, ct);
        var pcdMsg = $"## Project Context\n\n{pcd.ContextData}";
        messages.Add(new ChatMessage(ChatRole.System, pcdMsg));
        consumed += await tokens.CountAsync(pcdMsg, modelId, ct);
        layers++;

        // Priority 1: Task definition (never trimmed)
        if (taskDefinition is not null)
        {
            var taskMsg = $"## Current Task\n\n{taskDefinition}";
            messages.Add(new ChatMessage(ChatRole.System, taskMsg));
            consumed += await tokens.CountAsync(taskMsg, modelId, ct);
            layers++;
        }

        // Priority 2.5: Active learnings (top by confidence); skipped if budget tight
        var remaining = DefaultBudgetTokens - consumed;
        if (remaining > LearningsMinReserveTokens)
        {
            var page = await learnings.ListByProjectPagedAsync(
                projectId, new PagedQuery(1, LearningsPageSize), ct);

            var active = page.Items
                .Where(l => l.Status == LearningStatus.Active)
                .OrderByDescending(l => l.Confidence)
                .ToList();

            var learningText = FormatLearnings(active, remaining / 4);
            if (learningText.Length > 0)
            {
                var msg = $"## Learnings\n\n{learningText}";
                messages.Add(new ChatMessage(ChatRole.System, msg));
                consumed += await tokens.CountAsync(msg, modelId, ct);
                layers++;
            }
        }

        return new ContextPacket(messages, consumed, layers);
    }

    private static string FormatLearnings(IReadOnlyList<ProjectLearning> active, int charBudget)
    {
        if (active.Count == 0 || charBudget <= 0) return string.Empty;

        var sb = new System.Text.StringBuilder(capacity: Math.Min(charBudget, 4096));
        foreach (var l in active)
        {
            var line = $"- ({l.Confidence:F2}) {l.Title}: {l.Body}\n";
            if (sb.Length + line.Length > charBudget) break;
            sb.Append(line);
        }
        return sb.ToString();
    }
}
