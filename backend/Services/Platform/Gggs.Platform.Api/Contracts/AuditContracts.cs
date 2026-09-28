namespace Gggs.Platform.Api.Contracts;

/// <summary>One row of the audit log, as returned to an admin or auditor.</summary>
public sealed record AuditLogItem(
    Guid Id,
    DateTime OccurredDate,
    Guid? UserId,
    string Service,
    string Action,
    string EntityType,
    string EntityId,
    string? OldValuesJson,
    string? NewValuesJson,
    string? ChangedFieldsCsv,
    string? CorrelationId);

/// <summary>
/// Filters for <c>GET /api/v1/audit/logs</c>, layered on <see cref="Gggs.BuildingBlocks.Web.PageQuery"/>.
/// <para>
/// Free-text <c>search</c> is deliberately not supported here: the log has no single text column
/// worth scanning, and a query without at least one of these filters over a large tenant's history
/// would be an expensive accident waiting to happen. <see cref="EntityType"/> and
/// <see cref="EntityId"/> together give the common "history of this one record" view; the rest give
/// the "what happened in this window" view.
/// </para>
/// </summary>
public sealed record AuditLogQuery
{
    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? Action { get; init; }

    public string? Service { get; init; }

    public Guid? UserId { get; init; }

    public DateTime? FromDate { get; init; }

    public DateTime? ToDate { get; init; }
}
