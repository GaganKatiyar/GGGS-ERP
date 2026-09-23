using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gggs.BuildingBlocks.Security;

/// <summary>Tunables for step-up authentication on sensitive actions.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Gggs:Security";

    /// <summary>How recently the user must have completed MFA to use a sensitive permission.</summary>
    public TimeSpan StepUpWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Turned off only for local development and integration tests.</summary>
    public bool EnforceStepUp { get; set; } = true;
}

/// <summary>
/// Decides whether the caller holds the permissions an endpoint declares, and whether sensitive
/// ones have a recent enough MFA challenge behind them.
/// </summary>
public sealed class PermissionAuthorizationHandler(
    IPermissionProvider permissionProvider,
    IOptions<SecurityOptions> options,
    TimeProvider timeProvider,
    ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    private readonly SecurityOptions _options = options.Value;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        foreach (string code in requirement.Permissions)
        {
            if (!PermissionCatalog.Exists(code))
            {
                // A typo in an attribute must fail closed, loudly, rather than let everyone through.
                logger.LogError("Endpoint requires permission '{Permission}', which is not in the catalog.", code);
                context.Fail(new AuthorizationFailureReason(this, AuthorizationFailureReasons.UnknownPermission));
                return;
            }
        }

        PermissionSet permissions = await permissionProvider.GetCurrentAsync();

        bool satisfied = requirement.Mode == PermissionMode.All
            ? requirement.Permissions.All(permissions.Has)
            : requirement.Permissions.Any(permissions.Has);

        if (!satisfied)
        {
            context.Fail(new AuthorizationFailureReason(this, AuthorizationFailureReasons.MissingPermission));
            return;
        }

        string[] sensitiveInPlay = [.. requirement.Permissions.Where(p => permissions.Has(p) && PermissionCatalog.IsSensitive(p))];
        if (sensitiveInPlay.Length > 0 && !HasRecentAuthentication(context.User))
        {
            logger.LogInformation(
                "Step-up authentication required for sensitive permission(s) {Permissions}.",
                sensitiveInPlay);
            context.Fail(new AuthorizationFailureReason(this, AuthorizationFailureReasons.StepUpRequired));
            return;
        }

        context.Succeed(requirement);
    }

    /// <summary>
    /// Sensitive actions need a fresh authentication, not just a valid token: an unattended laptop
    /// should not be able to export bank details hours after login.
    /// </summary>
    private bool HasRecentAuthentication(ClaimsPrincipal user)
    {
        if (!_options.EnforceStepUp)
        {
            return true;
        }

        string? authTime = user.FindFirstValue("auth_time");
        if (!long.TryParse(authTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out long authTimeSeconds))
        {
            return false;
        }

        DateTimeOffset authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds);
        return timeProvider.GetUtcNow() - authenticatedAt <= _options.StepUpWindow;
    }
}
