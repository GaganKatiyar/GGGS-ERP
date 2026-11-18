namespace Gggs.Modules.Platform.Contracts;

/// <summary>
/// One tenant's figures for the platform console, filled in by the modules that own them. A module
/// leaves what it does not know as null.
/// </summary>
public sealed class TenantUsageReading
{
    /// <summary>People currently employed (not exited).</summary>
    public int? Employees { get; set; }

    /// <summary>Accounts that can sign in (invited or active), API clients excluded.</summary>
    public int? Users { get; set; }

    public int? SignedInToday { get; set; }

    public DateTime? LastSignIn { get; set; }

    public int? EmailsSentToday { get; set; }

    public int? EmailsFailedToday { get; set; }

    public int? EmailsLoggedToday { get; set; }

    /// <summary>What the organisation's email goes through, e.g. "Own connection: GGGS Gmail" or "Platform mail server".</summary>
    public string? EmailChannel { get; set; }

    /// <summary>How their people sign in, e.g. "Password, minimum 6 characters" or "Microsoft SSO (required)".</summary>
    public string? SignIn { get; set; }
}

/// <summary>
/// A module's contribution to <see cref="TenantUsageReading"/>. Runs inside the tenant (a background job
/// fans out per tenant), so it reads its own tables as usual: row-level security stays in force, and the
/// platform console only ever sees the counts.
/// </summary>
public interface ITenantUsageProbe
{
    Task ReadAsync(TenantUsageReading reading, DateTime todayStartUtc, CancellationToken cancellationToken = default);
}
