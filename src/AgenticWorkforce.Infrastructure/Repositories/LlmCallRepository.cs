using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class LlmCallRepository(AppDbContext db) : ILlmCallRepository
{
    public async Task AddBatchAsync(IReadOnlyList<LlmCall> calls, CancellationToken ct = default)
    {
        if (calls.Count == 0) return;
        db.LlmCalls.AddRange(calls);
        await db.SaveChangesAsync(ct);
    }
}
