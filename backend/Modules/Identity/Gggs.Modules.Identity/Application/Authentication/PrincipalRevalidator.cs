using System.Globalization;
using System.Security.Claims;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Domain.Users;

namespace Gggs.Modules.Identity.Application.Authentication;

/// <summary>
/// Rebuilds a signed-in principal from the account record, or refuses to.
/// <para>
/// Used wherever an existing sign-in is renewed: a refresh-token exchange, and the periodic
/// revalidation of a browser session. In both cases the claims come from the account as it is
/// <em>now</em>, never copied from the old principal - an account disabled, locked or stripped of a
/// role five minutes ago must not keep working because its old principal said so. A tenant
/// suspended in the meantime ends the sign-in too.
/// </para>
/// <para>
/// What is carried forward is only the session id and the original authentication time, so an
/// absolute session limit and a step-up check still measure from when the password was typed.
/// </para>
/// </summary>
public sealed class PrincipalRevalidator(
    ITenantContextSetter tenantSetter,
    IUserRepository users,
    IPlatformStaffRepository platformStaff,
    ITenantRegistry registry,
    TimeProvider timeProvider,
    ILogger<PrincipalRevalidator> logger)
{
    /// <summary>The rebuilt principal, or null when the sign-in must end.</summary>
    public async Task<ClaimsPrincipal?> RebuildAsync(
        ClaimsPrincipal previous,
        string authenticationScheme,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);

        string? subject = previous.FindFirstValue("sub") ?? previous.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out Guid subjectId))
        {
            return null;
        }

        bool isPlatformStaff = string.Equals(
            previous.FindFirstValue(GggsClaims.UserType),
            GggsClaims.UserTypes.Platform,
            StringComparison.Ordinal);

        (Guid sessionId, DateTimeOffset authenticatedAt) = CarryForwardSession(previous);

        return isPlatformStaff
            ? await RebuildPlatformStaffAsync(subjectId, sessionId, authenticatedAt, authenticationScheme, cancellationToken)
            : await RebuildTenantUserAsync(previous, subjectId, sessionId, authenticatedAt, authenticationScheme, cancellationToken);
    }

    private async Task<ClaimsPrincipal?> RebuildTenantUserAsync(
        ClaimsPrincipal previous,
        Guid userId,
        Guid sessionId,
        DateTimeOffset authenticatedAt,
        string authenticationScheme,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(previous.FindFirstValue(GggsClaims.TenantId), out Guid tenantId))
        {
            return null;
        }

        TenantInfo? tenant = await registry.GetAsync(tenantId, cancellationToken);

        if (tenant is null || !tenant.IsOperational)
        {
            logger.LogInformation("Sign-in for user {UserId} ended: the tenant is not operational.", userId);
            return null;
        }

        tenantSetter.SetTenant(tenant);

        User? user = await users.GetAsync(userId, cancellationToken);

        if (user is null || !user.CanSignIn)
        {
            logger.LogInformation("Sign-in for user {UserId} ended: the account can no longer sign in.", userId);
            return null;
        }

        return UserPrincipalFactory.Create(
            user,
            tenantId,
            sessionId,
            authenticatedAt,
            mfaSatisfied: previous.HasClaim("amr", UserPrincipalFactory.AuthenticationMethods.MultiFactor),
            authenticationScheme);
    }

    private async Task<ClaimsPrincipal?> RebuildPlatformStaffAsync(
        Guid platformStaffId,
        Guid sessionId,
        DateTimeOffset authenticatedAt,
        string authenticationScheme,
        CancellationToken cancellationToken)
    {
        PlatformStaffMember? member = await platformStaff.GetAsync(platformStaffId, cancellationToken);

        if (member is null || !member.CanSignIn)
        {
            logger.LogInformation(
                "Platform sign-in for {PlatformStaffId} ended: the account can no longer sign in.",
                platformStaffId);
            return null;
        }

        return PlatformStaffPrincipalFactory.Create(member, sessionId, authenticatedAt, authenticationScheme);
    }

    private (Guid SessionId, DateTimeOffset AuthenticatedAt) CarryForwardSession(ClaimsPrincipal previous)
    {
        Guid sessionId = Guid.TryParse(previous.FindFirstValue(GggsClaims.SessionId), out Guid existing)
            ? existing
            : Guid.CreateVersion7();

        DateTimeOffset authenticatedAt = long.TryParse(
                previous.FindFirstValue("auth_time"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : timeProvider.GetUtcNow();

        return (sessionId, authenticatedAt);
    }
}
