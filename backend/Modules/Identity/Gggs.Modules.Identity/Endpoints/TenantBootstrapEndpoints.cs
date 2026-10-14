using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Identity.Application.Provisioning;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gggs.Modules.Identity.Endpoints;

/// <summary>Which pending administrator the invitation is for.</summary>
public sealed record IssueAdministratorInvitationBody(string Email);

/// <summary>
/// What the operator hands to the customer: the same shape an ordinary invitation returns, and it
/// leaves this service the same way - once, in a response, never in a log.
/// </summary>
public sealed record AdministratorInvitationResponse(Guid UserId, string InvitationToken, DateTime ExpiresDate);

/// <summary>
/// Opening a new tenant for the first time.
/// <para>
/// Separate from the user endpoints because the caller is different in kind: platform staff acting
/// on a tenant they provisioned, not a tenant administrator acting inside their own organization.
/// Keeping it apart makes the exception visible rather than buried among the ordinary user routes.
/// </para>
/// </summary>
public static class TenantBootstrapEndpoints
{
    public static IEndpointRouteBuilder MapTenantBootstrapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/api/v1/tenants/{tenantId:guid}/administrator-invitation", IssueAsync)
            .WithName("IssueAdministratorInvitation")
            .WithTags("Provisioning")
            .WithDescription(
                "Issues the first administrator's invitation for a newly provisioned tenant. " +
                "Platform staff only, and only for an account nobody has signed into yet.")
            .RequirePermission(Permissions.Platform.TenantManage);

        return endpoints;
    }

    private static async Task<Ok<AdministratorInvitationResponse>> IssueAsync(
        Guid tenantId,
        IssueAdministratorInvitationBody body,
        TenantBootstrapService bootstrap,
        ITenantRegistry registry,
        ITenantContextSetter tenantSetter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        // The tenant comes from the route here rather than from a claim, which is the one shape the
        // isolation rules allow: the caller is platform staff, so their token has no tenant to read,
        // and the permission that let them in is a platform permission no tenant user can hold.
        TenantInfo? tenant = await registry.GetAsync(tenantId, cancellationToken);

        if (tenant is null)
        {
            throw new NotFoundException("Tenant", tenantId);
        }

        tenantSetter.SetTenant(tenant);

        AdministratorInvitation invitation = await bootstrap.IssueAdministratorInvitationAsync(
            body.Email,
            cancellationToken);

        return TypedResults.Ok(new AdministratorInvitationResponse(
            invitation.UserId,
            invitation.InvitationToken,
            invitation.ExpiresDate));
    }
}
