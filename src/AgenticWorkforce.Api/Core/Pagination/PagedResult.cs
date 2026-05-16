using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Core.Pagination;

/// <summary>
/// Standard pagination response. Adopted verbatim from SecurityBff reference.
/// </summary>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public record PagedQuery(int Page = 1, int PageSize = 20)
{
    public int Page { get; init; } = Math.Max(1, Page);
    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, 100);
    public int Skip => (Page - 1) * PageSize;
}

public static class QueryableExtensions
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
