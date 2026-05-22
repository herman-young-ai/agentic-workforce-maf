using AgenticWorkforce.Domain.Pagination;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Data;

/// <summary>
/// EF Core query extensions used by repository implementations to push paging
/// to SQL (Skip/Take/Count). Lives in Infrastructure because it depends on EF
/// Core; never call from Api or Domain.
/// </summary>
internal static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PagedQuery paging,
        CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(paging.Skip).Take(paging.PageSize).ToListAsync(ct);
        return new PagedResult<T>(items, paging.Page, paging.PageSize, total);
    }
}
