using System.Security.Claims;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace Gggs.BuildingBlocks.Security;

/// <summary>Reads the acting user out of the validated token on the current request.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid UserId => ParseGuid(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub"))
        ?? throw new UnauthorizedAccessException("The request has no authenticated user.");

    public Guid? EmployeeId => ParseGuid(User?.FindFirstValue(GggsClaims.EmployeeId));

    public Guid? SessionId => ParseGuid(User?.FindFirstValue(GggsClaims.SessionId));

    public Guid? ImpersonatorUserId => ParseGuid(User?.FindFirstValue(GggsClaims.Impersonator));

    public bool IsPlatformStaff => string.Equals(
        User?.FindFirstValue(GggsClaims.UserType),
        GggsClaims.UserTypes.Platform,
        StringComparison.Ordinal);

    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string? CorrelationId => httpContextAccessor.HttpContext?.TraceIdentifier;

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out Guid parsed) ? parsed : null;
}

/// <summary>
/// The identity used by background jobs, message consumers and seeding, where no person is acting.
/// Audit rows written under it are attributed to the system, never to a real user.
/// </summary>
public sealed class SystemCurrentUser(Guid userId, string? correlationId = null) : ICurrentUser
{
    public Guid UserId { get; } = userId;

    public Guid? EmployeeId => null;

    public Guid? SessionId => null;

    public bool IsAuthenticated => true;

    public Guid? ImpersonatorUserId => null;

    public bool IsPlatformStaff => false;

    public string? IpAddress => null;

    public string? UserAgent => null;

    public string? CorrelationId { get; } = correlationId;
}
