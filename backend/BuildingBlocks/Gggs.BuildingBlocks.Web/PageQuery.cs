using Gggs.BuildingBlocks.Application;

namespace Gggs.BuildingBlocks.Web;

/// <summary>
/// Query-string binding for list endpoints: <c>?page=2&amp;pageSize=50&amp;sort=-createdDate</c>.
/// <para>
/// Every property is nullable, and that is the entire point. Binding <see cref="PageRequest"/>
/// directly makes its non-nullable <c>Page</c> a <b>required</b> query parameter, so
/// <c>GET /api/v1/tenants</c> with no query string fails with a 400 - which is exactly how a
/// client calls a list endpoint the first time.
/// </para>
/// <para>
/// Endpoints bind this and call <see cref="ToPageRequest"/>, which applies the defaults and the
/// page-size ceiling.
/// </para>
/// </summary>
public sealed record PageQuery
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }

    /// <summary>Comma-separated fields, '-' for descending. Mapped through a per-query allowlist.</summary>
    public string? Sort { get; init; }

    public string? Search { get; init; }

    /// <summary>Counting costs a second query on large tables, so a caller may skip it.</summary>
    public bool? IncludeTotal { get; init; }

    public PageRequest ToPageRequest() => new()
    {
        // PageRequest clamps these itself: a negative page or an oversized page size is corrected
        // rather than rejected, because a bad paging parameter is not worth failing a request over.
        Page = Page ?? 1,
        PageSize = PageSize ?? PageRequest.DefaultPageSize,
        Sort = Sort,
        Search = Search,
        IncludeTotal = IncludeTotal ?? true,
    };
}
