using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Identity.Application.PlatformStaff;
using Gggs.Identity.Domain.PlatformStaff;
using Gggs.Identity.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Identity.Api.Endpoints;

public sealed record InvitePlatformStaffBody
{
    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public IReadOnlyList<Guid> RoleIds { get; init; } = [];
}

public sealed record InvitePlatformStaffResponse(Guid PlatformStaffId, string InvitationToken, DateTime ExpiresDate);

/// <summary>
/// Completing a platform staff invitation.
/// <para>
/// Carries no tenant, unlike the equivalent for a tenant user: platform staff belong to none, so
/// the lookup needs nothing else to narrow it beyond the id and the token.
/// </para>
/// </summary>
public sealed record CompletePlatformStaffInvitationBody(Guid PlatformStaffId, string Token, string NewPassword);

public sealed record ChangePlatformStaffPasswordBody(string CurrentPassword, string NewPassword);

public sealed record SetPlatformStaffRolesBody(IReadOnlyList<Guid> RoleIds);

public sealed record PlatformStaffListItem(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    DateTime? LastLoginDate);

/// <summary>
/// Managing our own staff.
/// <para>
/// The mirror of <see cref="UserEndpoints"/>, kept as its own module rather than folded into it:
/// the caller here is never a tenant, the permission codes are all <c>platform.*</c>, and mixing the
/// two would make the one endpoint that must never accidentally accept a tenant's request harder to
/// see at a glance.
/// </para>
/// </summary>
public static class PlatformStaffEndpoints
{
    public static IEndpointRouteBuilder MapPlatformStaffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/platform-staff").WithTags("PlatformStaff");

        group.MapGet("/", ListAsync).WithName("ListPlatformStaff").RequirePermission(Permissions.Platform.StaffView);
        group.MapPost("/invite", InviteAsync).WithName("InvitePlatformStaff").RequirePermission(Permissions.Platform.StaffInvite);
        group.MapPost("/{platformStaffId:guid}/disable", DisableAsync).WithName("DisablePlatformStaff").RequirePermission(Permissions.Platform.StaffManage);
        group.MapPost("/{platformStaffId:guid}/unlock", UnlockAsync).WithName("UnlockPlatformStaff").RequirePermission(Permissions.Platform.StaffManage);
        group.MapPut("/{platformStaffId:guid}/roles", SetRolesAsync).WithName("SetPlatformStaffRoles").RequirePermission(Permissions.Platform.StaffManage);

        // No permission beyond being signed in, exactly as for a tenant user: someone whose roles
        // were just taken away must still be able to change the password they signed in with.
        group.MapPost("/me/password", ChangeOwnPasswordAsync)
            .WithName("ChangeOwnPlatformStaffPassword")
            .RequireAuthorization();

        // Anonymous: the invitation token is the credential, and the invitee has no session yet.
        group.MapPost("/complete-invitation", CompleteInvitationAsync)
            .WithName("CompletePlatformStaffInvitation")
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<Ok<PagedResult<PlatformStaffListItem>>> ListAsync(
        [AsParameters] PageQuery page,
        IdentityDbContext database,
        CancellationToken cancellationToken)
    {
        PageRequest request = page.ToPageRequest();

        IQueryable<PlatformStaffMember> query = database.PlatformStaff.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.Trim();
            query = query.Where(member => member.DisplayName.Contains(term) || member.Email.Contains(term));
        }

        long? total = request.IncludeTotal ? await query.LongCountAsync(cancellationToken) : null;

        List<PlatformStaffListItem> items = await query
            .OrderBy(member => member.DisplayName)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(member => new PlatformStaffListItem(
                member.Id,
                member.Email,
                member.DisplayName,
                member.Status.ToString(),
                member.LastLoginDate))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResult.From(items, request, total));
    }

    private static async Task<Created<InvitePlatformStaffResponse>> InviteAsync(
        InvitePlatformStaffBody body,
        PlatformStaffManagementService staff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        InvitePlatformStaffResult result = await staff.InviteAsync(
            body.Email,
            body.DisplayName,
            body.RoleIds,
            cancellationToken);

        return TypedResults.Created(
            $"/api/v1/platform-staff/{result.PlatformStaffId}",
            new InvitePlatformStaffResponse(result.PlatformStaffId, result.InvitationToken, result.ExpiresDate));
    }

    private static async Task<NoContent> CompleteInvitationAsync(
        CompletePlatformStaffInvitationBody body,
        PlatformStaffManagementService staff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        await staff.CompleteInvitationAsync(body.PlatformStaffId, body.Token, body.NewPassword, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ChangeOwnPasswordAsync(
        ChangePlatformStaffPasswordBody body,
        PlatformStaffManagementService staff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        await staff.ChangeOwnPasswordAsync(body.CurrentPassword, body.NewPassword, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DisableAsync(
        Guid platformStaffId,
        PlatformStaffManagementService staff,
        CancellationToken cancellationToken)
    {
        await staff.DisableAsync(platformStaffId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> UnlockAsync(
        Guid platformStaffId,
        PlatformStaffManagementService staff,
        CancellationToken cancellationToken)
    {
        await staff.UnlockAsync(platformStaffId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> SetRolesAsync(
        Guid platformStaffId,
        SetPlatformStaffRolesBody body,
        PlatformStaffManagementService staff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        await staff.SetRolesAsync(platformStaffId, body.RoleIds, cancellationToken);
        return TypedResults.NoContent();
    }
}
