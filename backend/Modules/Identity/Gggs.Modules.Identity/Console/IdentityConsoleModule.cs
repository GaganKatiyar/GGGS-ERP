using System.Security.Claims;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Endpoints;
using Gggs.Modules.Identity.Infrastructure;
using Gggs.Modules.Identity.Sessions;
using Gggs.Modules.Platform.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Console;

public sealed record ConsoleLoginBody(string Email, string Password);

public sealed record SetupState(bool Open);

public sealed record SetupBody(string SetupKey, string DisplayName, string Email, string Password);

public sealed record InvitePlatformStaffBody
{
    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public IReadOnlyList<Guid> RoleIds { get; init; } = [];
}

/// <summary>
/// A new staff member's one-time link. Shown once in the console, to the person who invited them,
/// to pass on; the console never emails platform staff.
/// </summary>
public sealed record InvitePlatformStaffResponse(Guid PlatformStaffId, string InvitationToken, DateTime ExpiresDate);

public sealed record CompletePlatformStaffInvitationBody(Guid PlatformStaffId, string Token, string NewPassword);

public sealed record ChangePlatformStaffPasswordBody(string CurrentPassword, string NewPassword);

public sealed record SetPlatformStaffRolesBody(IReadOnlyList<Guid> RoleIds);

public sealed record StaffRoleRef(Guid Id, string Name);

public sealed record PlatformStaffRow(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    DateTime? LastLoginDate,
    DateTime? InvitationExpiresDate,
    bool LockedOut,
    IReadOnlyList<StaffRoleRef> Roles);

public sealed record PlatformRoleRow(Guid Id, string Code, string Name, string? Description, IReadOnlyList<string> Permissions, int Holders);

public sealed record OrganisationAdministrator(
    Guid UserId,
    string Email,
    string DisplayName,
    string Status,
    DateTime? LastLoginDate,
    DateTime? InvitationExpiresDate);

public sealed record ResendAdministratorBody(string Email);

public sealed record AddAdministratorBody(string Email, string? DisplayName);

/// <summary>When the invitation emailed to an organisation administrator stops working. The token itself is never returned.</summary>
public sealed record AdministratorInvitationSent(Guid UserId, string Email, DateTime ExpiresDate);

/// <summary>
/// The platform console's side of Identity: staff sign-in, first-run setup, platform staff and their
/// roles, and the administrators of each organisation.
/// <para>
/// A module of its own so only the console server (Gggs.PlatformApi) lists it. The HR application's
/// API therefore no longer offers platform staff management or the administrator bootstrap at all,
/// and its <c>/bff/login</c> is not the one used here: this one accepts platform staff only.
/// </para>
/// </summary>
public sealed class IdentityConsoleModule : IModule
{
    public string Name => "identity-console";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<PlatformSetupService>();

        services.AddOptions<PlatformBootstrapOptions>()
            .Bind(context.Configuration.GetSection(PlatformBootstrapOptions.SectionName));

        // The console server keeps the platform roles in step with the code as well as the worker,
        // so a fresh environment's setup page has its administrator role to grant.
        services.AddHostedService<PlatformRoleSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapSession(endpoints);
        MapSetup(endpoints);
        MapStaff(endpoints);
        MapAdministrators(endpoints);
    }

    private static void MapSession(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/bff").WithTags("Console session");

        group.MapPost("/login", async Task<Ok<BffLoginResponse>> (
                ConsoleLoginBody body,
                HttpContext context,
                PasswordSignInService signIn,
                BffSessionStore sessions,
                CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);

                if (context.Features.Get<BffSessionFeature>() is { } existing)
                {
                    await sessions.RemoveAsync(existing.SessionKey, ct);
                }

                PasswordSignInOutcome outcome = await signIn.SignInStaffAsync(
                    body.Email, body.Password, SessionAuthenticationHandler.SchemeName, ct);

                return TypedResults.Ok(await BrowserSessionEndpoints.StartSessionAsync(outcome, context, sessions, ct));
            })
            .WithName("ConsoleLogin").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.SignIn);

        group.MapPost("/logout", async (HttpContext context, BffSessionStore sessions, CancellationToken ct) =>
            {
                if (context.Features.Get<BffSessionFeature>() is { } session)
                {
                    await sessions.RemoveAsync(session.SessionKey, ct);
                }

                SessionCookie.Clear(context);
                return TypedResults.NoContent();
            })
            .WithName("ConsoleLogout").AllowAnonymous();

        group.MapGet("/user", async Task<Results<Ok<SessionResponse>, UnauthorizedHttpResult>> (
                ClaimsPrincipal principal,
                ITenantContext tenantContext,
                ITenantRegistry registry,
                IPermissionProvider permissions,
                CancellationToken ct) =>
            {
                if (principal.Identity?.IsAuthenticated != true || !IsPlatformStaff(principal))
                {
                    return TypedResults.Unauthorized();
                }

                return TypedResults.Ok(await MeEndpoints.BuildSessionAsync(principal, tenantContext, registry, permissions, ct));
            })
            .WithName("ConsoleUser").AllowAnonymous();
    }

    private static void MapSetup(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/platform/setup").WithTags("Console setup");

        group.MapGet("/", async (PlatformSetupService setup, CancellationToken ct) => TypedResults.Ok(new SetupState(await setup.IsOpenAsync(ct))))
            .WithName("ConsoleSetupState").AllowAnonymous();

        group.MapPost("/", async (SetupBody body, PlatformSetupService setup, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                await setup.CreateFirstAdministratorAsync(body.SetupKey, body.DisplayName, body.Email, body.Password, ct);
                return TypedResults.NoContent();
            })
            .WithName("ConsoleSetup").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.SignIn);
    }

    private static void MapStaff(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/platform/staff").WithTags("Platform staff");

        group.MapGet("/", ListStaffAsync).WithName("ConsoleStaff").RequirePermission(Permissions.Platform.StaffView);
        group.MapGet("/roles", ListRolesAsync).WithName("ConsoleStaffRoles").RequirePermission(Permissions.Platform.StaffView);

        group.MapPost("/invite", async Task<Created<InvitePlatformStaffResponse>> (
                InvitePlatformStaffBody body,
                PlatformStaffManagementService staff,
                IPlatformAuditLog audit,
                CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                audit.Record("staff.invited", $"Invited {body.DisplayName} ({body.Email}) to the platform team.");
                InvitePlatformStaffResult result = await staff.InviteAsync(body.Email, body.DisplayName, body.RoleIds, ct);
                return TypedResults.Created(
                    $"/api/v1/platform/staff/{result.PlatformStaffId}",
                    new InvitePlatformStaffResponse(result.PlatformStaffId, result.InvitationToken, result.ExpiresDate));
            })
            .WithName("ConsoleInviteStaff").RequirePermission(Permissions.Platform.StaffInvite);

        group.MapPost("/{platformStaffId:guid}/disable", async (Guid platformStaffId, PlatformStaffManagementService staff, IPlatformAuditLog audit, AppDbContext db, CancellationToken ct) =>
            {
                audit.Record("staff.disabled", $"Disabled {await StaffNameAsync(db, platformStaffId, ct)}.");
                await staff.DisableAsync(platformStaffId, ct);
                return TypedResults.NoContent();
            })
            .WithName("ConsoleDisableStaff").RequirePermission(Permissions.Platform.StaffManage);

        group.MapPost("/{platformStaffId:guid}/unlock", async (Guid platformStaffId, PlatformStaffManagementService staff, IPlatformAuditLog audit, AppDbContext db, CancellationToken ct) =>
            {
                audit.Record("staff.unlocked", $"Unlocked {await StaffNameAsync(db, platformStaffId, ct)}.");
                await staff.UnlockAsync(platformStaffId, ct);
                return TypedResults.NoContent();
            })
            .WithName("ConsoleUnlockStaff").RequirePermission(Permissions.Platform.StaffManage);

        group.MapPut("/{platformStaffId:guid}/roles", async (Guid platformStaffId, SetPlatformStaffRolesBody body, PlatformStaffManagementService staff, IPlatformAuditLog audit, AppDbContext db, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                List<string> names = await db.PlatformRoles.AsNoTracking()
                    .Where(role => body.RoleIds.Contains(role.Id))
                    .Select(role => role.Name)
                    .ToListAsync(ct);
                audit.Record(
                    "staff.roles-changed",
                    $"Set the roles of {await StaffNameAsync(db, platformStaffId, ct)} to {(names.Count == 0 ? "none" : string.Join(", ", names))}.");
                await staff.SetRolesAsync(platformStaffId, body.RoleIds, ct);
                return TypedResults.NoContent();
            })
            .WithName("ConsoleSetStaffRoles").RequirePermission(Permissions.Platform.StaffManage);

        // No permission beyond being signed in: someone whose roles were just taken away must still
        // be able to change the password they signed in with.
        group.MapPost("/me/password", async (
                ChangePlatformStaffPasswordBody body,
                PlatformStaffManagementService staff,
                ICurrentUser currentUser,
                BffSessionStore sessions,
                HttpContext context,
                CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                await staff.ChangeOwnPasswordAsync(body.CurrentPassword, body.NewPassword, ct);
                await sessions.EndAllForUserAsync(currentUser.UserId, context.Features.Get<BffSessionFeature>(), ct);
                return TypedResults.NoContent();
            })
            .WithName("ConsoleChangeOwnPassword").RequireAuthorization();

        // Anonymous: the invitation token is the credential, and the invitee has no session yet.
        group.MapPost("/complete-invitation", async (CompletePlatformStaffInvitationBody body, PlatformStaffManagementService staff, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                await staff.CompleteInvitationAsync(body.PlatformStaffId, body.Token, body.NewPassword, ct);
                return TypedResults.NoContent();
            })
            .WithName("ConsoleCompleteStaffInvitation").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.SignIn);
    }

    private static void MapAdministrators(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/platform/organisations/{tenantId:guid}/administrators")
            .WithTags("Organisation administrators");

        group.MapGet("/", async (Guid tenantId, ITenantScopes tenants, CancellationToken ct) =>
                TypedResults.Ok(await tenants.RunAsync(tenantId, provider => AdministratorsAsync(provider, ct), ct)))
            .WithName("ConsoleOrganisationAdministrators").RequirePermission(Permissions.Platform.TenantView);

        group.MapPost("/resend", async (Guid tenantId, ResendAdministratorBody body, ITenantScopes tenants, ITenantRegistry registry, IPlatformAuditLog audit, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                TenantInfo tenant = await TenantAsync(registry, tenantId, ct);
                AdministratorInvitation sent = await tenants.RunAsync(
                    tenantId,
                    provider => provider.GetRequiredService<TenantBootstrapService>().IssueAdministratorInvitationAsync(body.Email, ct),
                    ct);

                audit.Record("organisation.admin-invitation-resent", $"Re-sent the administrator invitation to {body.Email}.", tenantId, tenant.Code);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Ok(new AdministratorInvitationSent(sent.UserId, body.Email, sent.ExpiresDate));
            })
            .WithName("ConsoleResendAdministratorInvitation").RequirePermission(Permissions.Platform.TenantManage);

        group.MapPost("/", async (Guid tenantId, AddAdministratorBody body, ITenantScopes tenants, ITenantRegistry registry, IPlatformAuditLog audit, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                ArgumentNullException.ThrowIfNull(body);
                TenantInfo tenant = await TenantAsync(registry, tenantId, ct);
                AdministratorInvitation sent = await tenants.RunAsync(
                    tenantId,
                    provider => provider.GetRequiredService<TenantBootstrapService>().AddAdministratorAsync(body.Email, body.DisplayName, ct),
                    ct);

                audit.Record("organisation.admin-added", $"Invited {body.Email} as an administrator.", tenantId, tenant.Code);
                await unitOfWork.SaveChangesAsync(ct);
                return TypedResults.Ok(new AdministratorInvitationSent(sent.UserId, body.Email, sent.ExpiresDate));
            })
            .WithName("ConsoleAddAdministrator").RequirePermission(Permissions.Platform.TenantManage);
    }

    private static async Task<Ok<List<PlatformStaffRow>>> ListStaffAsync(AppDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        List<PlatformStaffMember> members = await db.PlatformStaff.AsNoTracking().OrderBy(member => member.DisplayName).ToListAsync(ct);
        List<PlatformRoleAssignment> grants = await db.PlatformRoleAssignments.AsNoTracking().ToListAsync(ct);
        Dictionary<Guid, string> roleNames = await db.PlatformRoles.AsNoTracking().ToDictionaryAsync(role => role.Id, role => role.Name, ct);

        return TypedResults.Ok(members.Select(member => new PlatformStaffRow(
                member.Id,
                member.Email,
                member.DisplayName,
                member.Status.ToString(),
                member.LastLoginDate,
                member.Status == PlatformStaffStatus.Invited ? member.InvitationTokenExpiresDate : null,
                member.LockoutEndDate > now,
                [.. grants.Where(grant => grant.PlatformStaffId == member.Id && roleNames.ContainsKey(grant.PlatformRoleId))
                    .Select(grant => new StaffRoleRef(grant.PlatformRoleId, roleNames[grant.PlatformRoleId]))
                    .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)]))
            .ToList());
    }

    private static async Task<Ok<List<PlatformRoleRow>>> ListRolesAsync(AppDbContext db, CancellationToken ct)
    {
        List<PlatformRole> roles = await db.PlatformRoles.AsNoTracking().ToListAsync(ct);
        List<PlatformRoleAssignment> grants = await db.PlatformRoleAssignments.AsNoTracking().ToListAsync(ct);
        List<string> order = [.. PlatformRoleTemplates.Definitions.Select(template => template.Code)];

        return TypedResults.Ok(roles
            .OrderBy(role => role.TemplateCode is { } code && order.Contains(code) ? order.IndexOf(code) : order.Count)
            .ThenBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
            .Select(role => new PlatformRoleRow(
                role.Id,
                role.TemplateCode ?? string.Empty,
                role.Name,
                role.Description,
                [.. role.PermissionCodes.Order(StringComparer.Ordinal)],
                grants.Count(grant => grant.PlatformRoleId == role.Id)))
            .ToList());
    }

    private static async Task<List<OrganisationAdministrator>> AdministratorsAsync(IServiceProvider provider, CancellationToken ct)
    {
        AppDbContext db = provider.GetRequiredService<AppDbContext>();
        DateOnly today = DateOnly.FromDateTime(provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);

        List<Guid> adminRoleIds = await db.Roles.AsNoTracking()
            .Where(role => role.TemplateCode == RoleTemplates.TenantAdministratorTemplateCode)
            .Select(role => role.Id)
            .ToListAsync(ct);

        List<Guid> userIds = await db.UserRoles.AsNoTracking()
            .Where(grant => adminRoleIds.Contains(grant.RoleId) && grant.ValidFrom <= today && (grant.ValidTo == null || grant.ValidTo >= today))
            .Select(grant => grant.UserId)
            .Distinct()
            .ToListAsync(ct);

        return await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .OrderBy(user => user.DisplayName)
            .Select(user => new OrganisationAdministrator(
                user.Id,
                user.Email,
                user.DisplayName,
                user.Status.ToString(),
                user.LastLoginDate,
                user.Status == UserStatus.Invited ? user.InvitationTokenExpiresDate : null))
            .ToListAsync(ct);
    }

    private static async Task<TenantInfo> TenantAsync(ITenantRegistry registry, Guid tenantId, CancellationToken ct)
        => await registry.GetAsync(tenantId, ct) ?? throw new NotFoundException("Organisation", tenantId);

    private static async Task<string> StaffNameAsync(AppDbContext db, Guid platformStaffId, CancellationToken ct)
        => await db.PlatformStaff.AsNoTracking()
            .Where(member => member.Id == platformStaffId)
            .Select(member => member.DisplayName)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(PlatformStaffMember), platformStaffId);

    private static bool IsPlatformStaff(ClaimsPrincipal principal)
        => string.Equals(principal.FindFirstValue(GggsClaims.UserType), GggsClaims.UserTypes.Platform, StringComparison.Ordinal);
}
