using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Platform.Domain.Console;

/// <summary>
/// One thing platform staff did in the console: who, to which organisation, what, and why. Append-only.
/// <para>
/// Platform-owned, unlike the per-tenant audit log: these actions happen above any one customer, and
/// the customer's own administrators must not be able to read or prune them.
/// </para>
/// </summary>
public sealed class PlatformAuditEntry : AggregateRoot, IPlatformOwned
{
    private PlatformAuditEntry()
    {
        StaffName = null!;
        Action = null!;
        Summary = null!;
    }

    public DateTime OccurredDate { get; private set; }

    public Guid StaffId { get; private set; }

    public string StaffName { get; private set; }

    public Guid? TenantId { get; private set; }

    public string? TenantCode { get; private set; }

    /// <summary>Stable code, e.g. <c>organisation.suspended</c>.</summary>
    public string Action { get; private set; }

    /// <summary>What happened, in words, e.g. "Plan changed: Core → Professional".</summary>
    public string Summary { get; private set; }

    public string? Reason { get; private set; }

    public static PlatformAuditEntry Record(DateTime now, Guid staffId, string staffName, Guid? tenantId, string? tenantCode, string action, string summary, string? reason) => new()
    {
        Id = SequentialGuid.Create(),
        OccurredDate = now,
        StaffId = staffId,
        StaffName = Guard.MaxLength(string.IsNullOrWhiteSpace(staffName) ? "Platform staff" : staffName.Trim(), 200),
        TenantId = tenantId,
        TenantCode = tenantCode,
        Action = Guard.MaxLength(Guard.NotNullOrWhiteSpace(action), 64),
        Summary = Guard.MaxLength(Guard.NotNullOrWhiteSpace(summary), 500),
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.MaxLength(reason.Trim(), 500),
    };
}

/// <summary>
/// One organisation's figures for the console, measured inside the tenant by a background job. Only
/// counts cross the tenant boundary - never names or records - which is why the console can show them.
/// </summary>
public sealed class TenantUsage : AggregateRoot, IPlatformOwned
{
    private TenantUsage()
    {
    }

    public Guid TenantId { get; private set; }

    public DateTime MeasuredDate { get; private set; }

    public int Employees { get; private set; }

    public int Users { get; private set; }

    public int SignedInToday { get; private set; }

    public DateTime? LastSignIn { get; private set; }

    public int EmailsSentToday { get; private set; }

    public int EmailsFailedToday { get; private set; }

    public int EmailsLoggedToday { get; private set; }

    public string? EmailChannel { get; private set; }

    public string? SignIn { get; private set; }

    public static TenantUsage For(Guid tenantId) => new() { Id = SequentialGuid.Create(), TenantId = Guard.NotEmpty(tenantId) };

    public void Update(DateTime now, int employees, int users, int signedInToday, DateTime? lastSignIn, int emailsSent, int emailsFailed, int emailsLogged, string? emailChannel, string? signIn)
    {
        SignIn = string.IsNullOrWhiteSpace(signIn) ? null : Guard.MaxLength(signIn.Trim(), 200);
        MeasuredDate = now;
        Employees = Math.Max(0, employees);
        Users = Math.Max(0, users);
        SignedInToday = Math.Max(0, signedInToday);
        LastSignIn = lastSignIn;
        EmailsSentToday = Math.Max(0, emailsSent);
        EmailsFailedToday = Math.Max(0, emailsFailed);
        EmailsLoggedToday = Math.Max(0, emailsLogged);
        EmailChannel = string.IsNullOrWhiteSpace(emailChannel) ? null : Guard.MaxLength(emailChannel.Trim(), 200);
    }
}
