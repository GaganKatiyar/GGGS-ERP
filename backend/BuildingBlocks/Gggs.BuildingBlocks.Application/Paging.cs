namespace Gggs.BuildingBlocks.Application;

/// <summary>
/// Paging, sorting and search parameters shared by every list endpoint. Feature-specific filters
/// are added by deriving from this record.
/// </summary>
public record PageRequest
{
    /// <summary>Hard ceiling, so no caller can ask for a whole tenant in one response.</summary>
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 25;

    private readonly int _page = 1;
    private readonly int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value,
        };
    }

    /// <summary>Comma-separated fields, '-' for descending, for example <c>-dateOfJoining,displayName</c>.
    /// Field names are mapped through a per-query allowlist, never concatenated into SQL.</summary>
    public string? Sort { get; init; }

    /// <summary>Free-text search term applied to the fields that query declares as searchable.</summary>
    public string? Search { get; init; }

    /// <summary>Counting is extra work on large tables, so callers that page endlessly can skip it.</summary>
    public bool IncludeTotal { get; init; } = true;

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>One page of results plus what the UI needs to render pagination.</summary>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    /// <summary>Null when the caller asked not to count.</summary>
    public long? TotalCount { get; init; }

    public int? TotalPages => TotalCount is null ? null : (int)Math.Ceiling(TotalCount.Value / (double)PageSize);

    public bool HasNextPage => TotalPages is null ? Items.Count == PageSize : Page < TotalPages;
}

/// <summary>Factories for <see cref="PagedResult{T}"/>, written so the item type is inferred.</summary>
public static class PagedResult
{
    public static PagedResult<T> Empty<T>(PageRequest request) => new()
    {
        Items = [],
        Page = request.Page,
        PageSize = request.PageSize,
        TotalCount = request.IncludeTotal ? 0 : null,
    };

    public static PagedResult<T> From<T>(IReadOnlyList<T> items, PageRequest request, long? totalCount) => new()
    {
        Items = items,
        Page = request.Page,
        PageSize = request.PageSize,
        TotalCount = totalCount,
    };
}

/// <summary>
/// Cursor paging for high-volume feeds - attendance punches, audit entries, notifications - where
/// OFFSET gets slower the deeper you page and rows arrive while the user is paging.
/// </summary>
public sealed record CursorResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Opaque continuation token; null when the end has been reached.</summary>
    public string? NextCursor { get; init; }
}
