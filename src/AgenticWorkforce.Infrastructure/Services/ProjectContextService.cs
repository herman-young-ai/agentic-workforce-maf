using System.Text.Json;
using System.Text.Json.Nodes;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Mutates the PCD's JSON document with versioned change history. Each
/// mutation reads the current document, applies the change, re-serialises,
/// increments <c>ContextVersion</c>, and records a <see cref="ContextChange"/>
/// row — all in a single transaction so the change log can never diverge
/// from the current state.
/// </summary>
internal sealed class ProjectContextService(
    AppDbContext db,
    IProjectContextRepository repo) : IProjectContextService
{
    private const string PrinciplesPath = "principles";
    private const string GuardrailsPath = "guardrails";

    public async Task<ProjectContext> GetAsync(Guid projectId, CancellationToken ct = default)
        => await repo.GetAsync(projectId, ct)
        ?? await repo.EnsureCreatedAsync(projectId, ct);

    public Task<IReadOnlyList<ContextChange>> GetHistoryAsync(Guid projectId, CancellationToken ct = default)
        => repo.GetHistoryAsync(projectId, ct);

    public Task<string> AddPrincipleAsync(
        Guid projectId, string principle, Guid addedById, CancellationToken ct = default)
        => AddItemAsync(projectId, PrinciplesPath, principle, addedById, ct);

    public Task<string> AddGuardrailAsync(
        Guid projectId, string guardrail, Guid addedById, CancellationToken ct = default)
        => AddItemAsync(projectId, GuardrailsPath, guardrail, addedById, ct);

    public Task<bool> RemovePrincipleAsync(
        Guid projectId, string principleId, Guid removedById, CancellationToken ct = default)
        => RemoveItemAsync(projectId, PrinciplesPath, principleId, removedById, ct);

    public Task<bool> RemoveGuardrailAsync(
        Guid projectId, string guardrailId, Guid removedById, CancellationToken ct = default)
        => RemoveItemAsync(projectId, GuardrailsPath, guardrailId, removedById, ct);

    private async Task<string> AddItemAsync(
        Guid projectId, string sectionPath, string value, Guid addedById, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{sectionPath} value cannot be empty.");

        var context = await db.ProjectContexts
            .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectContext", projectId);

        var root = JsonNode.Parse(context.ContextData) as JsonObject ?? new JsonObject();
        if (root[sectionPath] is not JsonArray section)
        {
            section = new JsonArray();
            root[sectionPath] = section;
        }

        var itemId = Guid.NewGuid().ToString();
        section.Add(new JsonObject { ["id"] = itemId, ["text"] = value });

        var newData = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var oldVersion = context.ContextVersion;

        context.ContextData    = newData;
        context.ContextVersion = oldVersion + 1;
        context.SizeCharacters = newData.Length;
        // Rough token estimate: 4 chars per token. Real counter wires in Phase 6.
        context.SizeTokens     = newData.Length / 4;

        db.ContextChanges.Add(new ContextChange
        {
            ProjectId      = projectId,
            ContextId      = context.Id,
            ContextVersion = context.ContextVersion,
            ChangeType     = ChangeType.Add,
            Path           = $"{sectionPath}.{itemId}",
            OldValue       = null,
            NewValue       = JsonSerializer.Serialize(new { id = itemId, text = value }),
            Reason         = $"Added by user {addedById:N}"
        });

        await db.SaveChangesAsync(ct);
        return itemId;
    }

    private async Task<bool> RemoveItemAsync(
        Guid projectId, string sectionPath, string itemId, Guid removedById, CancellationToken ct)
    {
        var context = await db.ProjectContexts
            .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectContext", projectId);

        var root = JsonNode.Parse(context.ContextData) as JsonObject;
        if (root?[sectionPath] is not JsonArray section) return false;

        JsonObject? target = null;
        for (var i = 0; i < section.Count; i++)
        {
            if (section[i] is JsonObject item && item["id"]?.ToString() == itemId)
            {
                target = item;
                section.RemoveAt(i);
                break;
            }
        }
        if (target is null) return false;

        var newData = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        context.ContextData    = newData;
        context.ContextVersion += 1;
        context.SizeCharacters = newData.Length;
        context.SizeTokens     = newData.Length / 4;

        db.ContextChanges.Add(new ContextChange
        {
            ProjectId      = projectId,
            ContextId      = context.Id,
            ContextVersion = context.ContextVersion,
            ChangeType     = ChangeType.Remove,
            Path           = $"{sectionPath}.{itemId}",
            OldValue       = target.ToJsonString(),
            NewValue       = null,
            Reason         = $"Removed by user {removedById:N}"
        });

        await db.SaveChangesAsync(ct);
        return true;
    }
}
