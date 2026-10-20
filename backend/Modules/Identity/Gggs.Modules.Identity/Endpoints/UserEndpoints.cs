using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Endpoints;

public sealed record InviteUserBody
{
    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public Guid? EmployeeId { get; init; }

    public IReadOnlyList<Guid> RoleIds { get; init; } = [];
}

/// <summary>
/// What an invitation returns.
/// <para>
/// The token comes back to the caller because the Notification service does not exist yet, so an
/// administrator has to pass the link on. Once notifications land this becomes a message sent to
/// the user's address and the token stops crossing this boundary at all - a token that reaches
/// anyone but the invitee is a way into their account.
/// </para>
/// </summary>
public sealed record InviteUserResponse(Guid UserId, string InvitationToken, DateTime ExpiresDate);

/// <summary>
/// Completing an invitation.
/// <para>
/// Carries the tenant because it runs anonymously, and users are tenant-owned: without it the
/// lookup is filtered to no tenant and finds nothing. The invitation link already points at the
/// customer's own address, so the code is there to put in it.
/// </para>
/// </summary>
public sealed record CompleteInvitationBody(string TenantCode, Guid UserId, string Token, string NewPassword);

public sealed record ChangePasswordBody(string CurrentPassword, string NewPassword);

public sealed record SetRolesBody(IReadOnlyList<Guid> RoleIds);

public sealed record UserListItem(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    string UserType,
    Guid? EmployeeId,
    DateTime? LastLoginDate,
    bool MfaEnabled);

/// <summary>Managing the people who can sign in.</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/users").WithTags("Users");

        group.MapGet("/", ListAsync).WithName("ListUsers").RequirePermission(Permissions.Admin.UserView);
        group.MapPost("/invite", InviteAsync).WithName("InviteUser").RequirePermission(Permissions.Admin.UserInvite);
        group.MapPost("/{userId:guid}/disable", DisableAsync).WithName("DisableUser").RequirePermission(Permissions.Admin.UserLock);
        group.MapPost("/{userId:guid}/enable", EnableAsync).WithName("EnableUser").RequirePermission(Permissions.Admin.UserLock);
        group.MapPost("/{userId:guid}/unlock", UnlockAsync).WithName("UnlockUser").RequirePermission(Permissions.Admin.UserLock);
        group.MapGet("/{userId:guid}/roles", GetRolesAsync).WithName("GetUserRoles").RequirePermission(Permissions.Admin.UserView);
        group.MapPut("/{userId:guid}/roles", SetRolesAsync).WithName("SetUserRoles").RequirePermission(Permissions.Admin.UserManage);

        // Changing your own password needs no permission beyond being signed in - everyone must be
        // able to do it, including a user whose roles were just taken away.
        group.MapPost("/me/password", ChangeOwnPasswordAsync)
            .WithName("ChangeOwnPassword")
            .RequireAuthorization();

        // Anonymous: the invitation token is the credential, and the invitee has no account yet.
        endpoints.MapPost("/api/v1/auth/complete-invitation", CompleteInvitationAsync)
            .WithName("CompleteInvitation")
            .WithTags("Authentication")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.SignIn);

        return endpoints;
    }

    private static async Task<Ok<PagedResult<UserListItem>>> ListAsync(
        [AsParameters] PageQuery page,
        [FromQuery] string? status,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        PageRequest request = page.ToPageRequest();

        IQueryable<User> query = database.Users.AsNoTracking();

        if (Enum.TryParse(status, ignoreCase: true, out UserStatus parsedStatus))
        {
            query = query.Where(user => user.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.Trim();
            query = query.Where(user => user.DisplayName.Contains(term) || user.Email.Contains(term));
        }

        long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

        List<UserListItem> items = await query
            .OrderBy(user => user.DisplayName)
            .Skip(request.Skip)
            .Take(request.PageSize)

            // Note what is absent: no password hash, no security stamp, no invitation token. A list
            // endpoint is exactly where those leak into a log or a browser cache.
            .Select(user => new UserListItem(
                user.Id,
                user.Email,
                user.DisplayName,
                user.Status.ToString(),
                user.UserType.ToString(),
                user.EmployeeId,
                user.LastLoginDate,
                user.MfaEnabled))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResult.From(items, request, total));
    }

    private static async Task<Created<InviteUserResponse>> InviteAsync(
        InviteUserBody body,
        UserManagementService users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        InviteUserResult result = await users.InviteAsync(
            body.Email,
            body.DisplayName,

            // Platform accounts are never created through a tenant's own console.
            UserType.TenantUser,
            body.EmployeeId,
            body.RoleIds,
            cancellationToken);

        return TypedResults.Created(
            $"/api/v1/users/{result.UserId}",
            new InviteUserResponse(result.UserId, result.InvitationToken, result.ExpiresDate));
    }

    private static async Task<NoContent> CompleteInvitationAsync(
        CompleteInvitationBody body,
        UserManagementService users,
        TenantDiscoveryService discovery,
        ITenantContextSetter tenantSetter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Same shape as sign-in: any anonymous endpoint in a tenant-owned world has to establish
        // the tenant before it can look anything up.
        TenantDiscoveryResult? discovered = await discovery.DiscoverAsync(
            body.TenantCode, email: string.Empty, cancellationToken);

        if (discovered is null || !discovered.Tenant.IsOperational)
        {
            // Indistinguishable from a bad token, so this cannot be used to probe which customers
            // exist.
            throw new DomainRuleException("INVITATION_INVALID", "That invitation is no longer valid.");
        }

        tenantSetter.SetTenant(discovered.Tenant);

        await users.CompleteInvitationAsync(body.UserId, body.Token, body.NewPassword, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ChangeOwnPasswordAsync(
        ChangePasswordBody body,
        UserManagementService users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        await users.ChangeOwnPasswordAsync(body.CurrentPassword, body.NewPassword, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DisableAsync(
        Guid userId,
        UserManagementService users,
        CancellationToken cancellationToken)
    {
        await users.DisableAsync(userId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> EnableAsync(
        Guid userId,
        UserManagementService users,
        CancellationToken cancellationToken)
    {
        await users.EnableAsync(userId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> UnlockAsync(
        Guid userId,
        UserManagementService users,
        CancellationToken cancellationToken)
    {
        await users.UnlockAsync(userId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> SetRolesAsync(
        Guid userId,
        SetRolesBody body,
        UserManagementService users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        await users.SetRolesAsync(userId, body.RoleIds, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The role ids a "set roles" screen needs to know which boxes are already checked.
    /// <para>
    /// <see cref="SetRolesAsync"/> replaces a user's roles wholesale - there is no "add one role"
    /// endpoint - so an editor that does not first know what is already granted would clear every
    /// existing grant the moment it saved anything.
    /// </para>
    /// </summary>
    private static async Task<Ok<IReadOnlyList<Guid>>> GetRolesAsync(
        Guid userId,
        IRoleRepository roles,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        IReadOnlyList<UserRole> grants = await roles.GetGrantsForUserAsync(userId, cancellationToken);

        return TypedResults.Ok<IReadOnlyList<Guid>>(
            [.. grants.Where(grant => grant.IsActiveOn(today)).Select(grant => grant.RoleId)]);
    }
}
