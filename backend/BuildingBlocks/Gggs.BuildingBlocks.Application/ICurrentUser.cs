namespace Gggs.BuildingBlocks.Application;

/// <summary>Who is making the current request, message or job run.</summary>
public interface ICurrentUser
{
    /// <summary>Identity user id, or the well-known system user for background work.</summary>
    Guid UserId { get; }

    /// <summary>
    /// The employee this user is, when they are one. Platform staff, integration clients and
    /// service accounts have no employee record, which is why this is nullable.
    /// </summary>
    Guid? EmployeeId { get; }

    /// <summary>Identity session id, used for session-level revocation and audit.</summary>
    Guid? SessionId { get; }

    bool IsAuthenticated { get; }

    /// <summary>True for platform staff acting through a support grant; recorded on every audit row.</summary>
    Guid? ImpersonatorUserId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    /// <summary>Correlation id shared by the request, its events, its jobs and its audit records.</summary>
    string? CorrelationId { get; }
}

/// <summary>Well-known identities used when no human is behind an operation.</summary>
public static class SystemUsers
{
    /// <summary>Background jobs, accruals, retention and other scheduled work.</summary>
    public static readonly Guid System = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Data written by migrations and seeding.</summary>
    public static readonly Guid Migration = new("00000000-0000-0000-0000-000000000002");

    /// <summary>Inbound integrations (biometric devices, SCIM, connectors).</summary>
    public static readonly Guid Integration = new("00000000-0000-0000-0000-000000000003");
}
