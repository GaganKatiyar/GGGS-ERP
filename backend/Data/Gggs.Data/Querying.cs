using System.Linq.Expressions;
using Gggs.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Data;

/// <summary>
/// Sorting and paging for list endpoints, so every listing behaves the same (spec §118): a
/// whitelist of sortable columns, a default order, and a page plus an optional total.
/// </summary>
public static class Querying
{
    /// <summary>
    /// Applies <c>?sort=name</c> or <c>?sort=-name</c> (descending) from a whitelist. An unknown key
    /// falls back to the default rather than erroring, and never reaches the database as a string -
    /// a sort parameter is not a way to inject an expression.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        string? sort,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> sortable,
        string defaultSort)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sortable);

        string requested = string.IsNullOrWhiteSpace(sort) ? defaultSort : sort.Trim();
        bool descending = requested.StartsWith('-');
        string key = requested.TrimStart('-', '+');

        if (!sortable.TryGetValue(key, out Expression<Func<T, object?>>? selector))
        {
            descending = defaultSort.StartsWith('-');
            selector = sortable[defaultSort.TrimStart('-', '+')];
        }

        return descending ? query.OrderByDescending(selector) : query.OrderBy(selector);
    }

    /// <summary>One page of an already-filtered, already-sorted query, projected by <paramref name="select"/>.</summary>
    public static async Task<PagedResult<TItem>> ToPagedResultAsync<T, TItem>(
        this IQueryable<T> query,
        PageRequest request,
        Expression<Func<T, TItem>> select,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

        List<TItem> items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(select)
            .ToListAsync(cancellationToken);

        return PagedResult.From(items, request, total);
    }
}
