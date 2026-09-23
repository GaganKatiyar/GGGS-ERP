using Microsoft.AspNetCore.Authorization;

namespace Gggs.BuildingBlocks.Security;

/// <summary>Whether the caller needs every listed permission or just one of them.</summary>
public enum PermissionMode
{
    Any = 0,
    All = 1,
}

/// <summary>
/// Declares what a caller must hold to reach an endpoint:
/// <c>[RequirePermission(Permissions.Leave.RequestApprove)]</c>.
/// <para>
/// Authorization is deny-by-default: a startup check fails the host if any non-anonymous endpoint
/// is missing this attribute, so "forgot to secure it" cannot reach production.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "gggs.perm:";

    public RequirePermissionAttribute(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Length == 0)
        {
            throw new ArgumentException("At least one permission is required.", nameof(permissions));
        }

        Permissions = permissions;
        Policy = BuildPolicyName(permissions, PermissionMode.Any);
    }

    public string[] Permissions { get; }

    /// <summary>Encodes the requirement into a policy name the policy provider can parse back.</summary>
    public static string BuildPolicyName(IEnumerable<string> permissions, PermissionMode mode)
        => $"{PolicyPrefix}{mode}:{string.Join('|', permissions)}";

    public static bool TryParsePolicyName(string policyName, out string[] permissions, out PermissionMode mode)
    {
        permissions = [];
        mode = PermissionMode.Any;

        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string remainder = policyName[PolicyPrefix.Length..];
        int separator = remainder.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0 || !Enum.TryParse(remainder[..separator], out mode))
        {
            return false;
        }

        permissions = remainder[(separator + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        return permissions.Length > 0;
    }
}

/// <summary>Requires every listed permission, for endpoints that combine two capabilities.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireAllPermissionsAttribute : AuthorizeAttribute
{
    public RequireAllPermissionsAttribute(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        Permissions = permissions;
        Policy = RequirePermissionAttribute.BuildPolicyName(permissions, PermissionMode.All);
    }

    public string[] Permissions { get; }
}

/// <summary>The authorization requirement carrying the parsed permission codes.</summary>
public sealed class PermissionRequirement(string[] permissions, PermissionMode mode) : IAuthorizationRequirement
{
    public string[] Permissions { get; } = permissions;

    public PermissionMode Mode { get; } = mode;
}

/// <summary>Reasons an authorization failure can be reported back, so the API can answer usefully.</summary>
public static class AuthorizationFailureReasons
{
    public const string MissingPermission = "missing_permission";

    /// <summary>Held, but the action needs a recent MFA challenge (§8.3 step-up).</summary>
    public const string StepUpRequired = "insufficient_user_authentication";

    public const string UnknownPermission = "unknown_permission";
}
