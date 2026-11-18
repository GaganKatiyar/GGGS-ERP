using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Endpoints;

/// <summary>The organisation's password rules as Studio > Password policy shows and saves them.</summary>
public sealed record PasswordPolicyView(
    int MinimumLength,
    int LowestMinimumLength,
    bool RequireComplexity,
    bool RejectBreachedPasswords,
    int HistoryCount,
    int MaximumAgeDays,
    int LockoutThreshold,
    int LockoutMinutes,
    bool IsDefault)
{
    public static PasswordPolicyView From(PasswordPolicy policy, bool isDefault) => new(
        policy.MinimumLength,
        PasswordPolicy.LowestMinimumLength,
        policy.RequireComplexity,
        policy.RejectBreachedPasswords,
        policy.HistoryCount,
        policy.MaximumAgeDays,
        policy.LockoutThreshold,
        (int)policy.LockoutDuration.TotalMinutes,
        isDefault);
}

public sealed record PasswordPolicyBody(
    int MinimumLength,
    bool RequireComplexity,
    bool RejectBreachedPasswords,
    int HistoryCount,
    int MaximumAgeDays,
    int LockoutThreshold,
    int LockoutMinutes);

/// <summary>What a set-password form needs to tell someone before they try: nothing else about the policy.</summary>
public sealed record PasswordRules(int MinimumLength, bool RequireComplexity);

/// <summary>
/// Studio > Password policy, and the rules the set-password pages show. Saving takes effect at once for
/// every new password; existing passwords are not forced to change.
/// </summary>
public static class PasswordPolicyEndpoints
{
    public static IEndpointRouteBuilder MapPasswordPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/v1/admin/password-policy", GetAsync)
            .WithName("GetPasswordPolicy").WithTags("Administration").RequirePermission(Permissions.Admin.SettingView);
        endpoints.MapPut("/api/v1/admin/password-policy", SaveAsync)
            .WithName("SavePasswordPolicy").WithTags("Administration").RequirePermission(Permissions.Admin.SettingManage);

        // The signed-in person's own rules, for the change-password panel.
        endpoints.MapGet("/api/v1/users/me/password-rules", async (IPasswordPolicyRepository policies, CancellationToken ct) => TypedResults.Ok(Rules(await policies.GetAsync(ct))))
            .WithName("MyPasswordRules").WithTags("Users").RequireAuthorization();

        // Anonymous, for the invitation and reset pages. An unknown organisation gets the defaults, so this
        // says nothing about which organisations exist.
        endpoints.MapGet("/api/v1/auth/password-rules", RulesForTenantAsync)
            .WithName("PasswordRules").WithTags("Authentication").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.SignIn);

        return endpoints;
    }

    private static async Task<Ok<PasswordPolicyView>> GetAsync(AppDbContext database, CancellationToken cancellationToken)
    {
        PasswordPolicy? saved = await database.PasswordPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(PasswordPolicyView.From(saved ?? PasswordPolicy.CreateDefault(), saved is null));
    }

    private static async Task<Ok<PasswordPolicyView>> SaveAsync(PasswordPolicyBody body, AppDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        PasswordPolicy? policy = await database.PasswordPolicies.FirstOrDefaultAsync(cancellationToken);
        if (policy is null)
        {
            policy = PasswordPolicy.CreateDefault();
            database.PasswordPolicies.Add(policy);
        }

        // Session lengths are not on this page: they stay as they are.
        policy.Update(
            body.MinimumLength,
            body.RequireComplexity,
            body.RejectBreachedPasswords,
            body.HistoryCount,
            body.MaximumAgeDays,
            body.LockoutThreshold,
            TimeSpan.FromMinutes(body.LockoutMinutes),
            policy.IdleTimeout,
            policy.AbsoluteSessionLifetime);
        await database.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(PasswordPolicyView.From(policy, isDefault: false));
    }

    private static async Task<Ok<PasswordRules>> RulesForTenantAsync(
        string? tenantCode,
        TenantDiscoveryService discovery,
        ITenantContextSetter tenantSetter,
        IPasswordPolicyRepository policies,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(tenantCode)
            && await discovery.DiscoverAsync(tenantCode, email: string.Empty, cancellationToken) is { Tenant.IsOperational: true } discovered)
        {
            tenantSetter.SetTenant(discovered.Tenant);
            return TypedResults.Ok(Rules(await policies.GetAsync(cancellationToken)));
        }

        return TypedResults.Ok(Rules(PasswordPolicy.CreateDefault()));
    }

    private static PasswordRules Rules(PasswordPolicy policy) => new(policy.MinimumLength, policy.RequireComplexity);
}
