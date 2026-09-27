using Gggs.BuildingBlocks.Application;
using Gggs.Identity.Application.PlatformStaff;
using Gggs.Identity.Application.Users;
using Gggs.Identity.Domain.PlatformStaff;
using Gggs.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gggs.Identity.Api.Infrastructure;

/// <summary>Names the migration that must be applied before either seeder below can run.</summary>
internal static class PlatformStaffMigration
{
    public const string Name = "AddPlatformStaff";
}

/// <summary>
/// Keeps the two seeded platform roles in sync with <see cref="PlatformRoleTemplates"/> on every
/// startup.
/// <para>
/// Unlike tenant role seeding, this tops permissions back up as well as adding what is missing.
/// There is no tenant here who might have deliberately narrowed a role - these definitions belong
/// to the platform itself, and drift between the code and the database is a deploy that has not
/// finished, not a decision to respect.
/// </para>
/// </summary>
public sealed class PlatformRoleSeeder(
    IServiceProvider serviceProvider,
    ILogger<PlatformRoleSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        if (!await database.Database.CanConnectAsync(cancellationToken))
        {
            logger.LogError("The identity database is unreachable; platform role seeding was skipped.");
            return;
        }

        IEnumerable<string> applied = await database.Database.GetAppliedMigrationsAsync(cancellationToken);

        if (!applied.Any(migration => migration.EndsWith(PlatformStaffMigration.Name, StringComparison.Ordinal)))
        {
            logger.LogError(
                "The platform staff tables are missing; role seeding was skipped. Run the database " +
                "migrations and restart this service.");
            return;
        }

        var roles = scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        IReadOnlyDictionary<string, PlatformRole> existing = await roles.GetSystemRolesAsync(cancellationToken);
        int created = 0;
        int updated = 0;

        foreach (PlatformRoleTemplate template in PlatformRoleTemplates.Definitions)
        {
            if (existing.TryGetValue(template.Code, out PlatformRole? role))
            {
                List<string> obsolete =
                [
                    .. role.PermissionCodes.Except(template.Permissions, StringComparer.OrdinalIgnoreCase)
                ];

                foreach (string code in obsolete)
                {
                    role.Revoke(code);
                }

                foreach (string code in template.Permissions)
                {
                    role.Grant(code);
                }

                if (obsolete.Count > 0 || role.PermissionCodes.Count != template.Permissions.Count)
                {
                    updated++;
                }

                continue;
            }

            role = PlatformRole.Create(template.Name, template.Description, isSystem: true, templateCode: template.Code);

            foreach (string code in template.Permissions)
            {
                role.Grant(code);
            }

            roles.Add(role);
            created++;
        }

        if (created > 0 || updated > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Platform roles seeded: {Created} created, {Updated} updated.", created, updated);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Where the first platform administrator's address comes from.</summary>
public sealed class PlatformBootstrapOptions
{
    public const string SectionName = "Gggs:Identity:PlatformBootstrap";

    /// <summary>
    /// Left empty in every environment after the first one is bootstrapped. Once an account exists
    /// with this address, this setting has nothing left to do and can stay or be removed.
    /// </summary>
    public string? AdministratorEmail { get; set; }
}

/// <summary>
/// Solves the one genuine circularity in the whole authorization model: only platform staff can
/// invite platform staff, and on a brand-new deployment there is no platform staff.
/// <para>
/// Unlike the tenant administrator bootstrap - which an existing platform operator triggers through
/// an API once a tenant is provisioned - there is no "existing platform operator" to press that
/// button here. So this runs from configuration instead of a request, exactly once per environment,
/// and hands the invitation to an operator through the log rather than a database column: the token
/// is never written anywhere else, and it never crosses an HTTP boundary that a network position
/// could intercept.
/// </para>
/// </summary>
public sealed class PlatformStaffBootstrapSeeder(
    IServiceProvider serviceProvider,
    IOptions<PlatformBootstrapOptions> options,
    TimeProvider timeProvider,
    ILogger<PlatformStaffBootstrapSeeder> logger) : IHostedService
{
    /// <summary>
    /// A week, not the ordinary three days. This one has to be read out of a log by a person doing
    /// an initial deployment, not delivered by email the moment it is issued.
    /// </summary>
    private static readonly TimeSpan BootstrapInvitationLifetime = TimeSpan.FromDays(7);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string? email = options.Value.AdministratorEmail;

        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        if (!await database.Database.CanConnectAsync(cancellationToken))
        {
            logger.LogError("The identity database is unreachable; the platform bootstrap account was not created.");
            return;
        }

        var staff = scope.ServiceProvider.GetRequiredService<IPlatformStaffRepository>();
        string normalized = PlatformStaffMember.NormalizeEmailForLookup(email);

        if (await staff.FindByEmailAsync(normalized, cancellationToken) is not null)
        {
            // Already bootstrapped, on this or an earlier start. The setting is left in place by
            // whoever deploys this, and idempotency here is what makes that safe.
            return;
        }

        var roles = scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>();
        IReadOnlyDictionary<string, PlatformRole> systemRoles = await roles.GetSystemRolesAsync(cancellationToken);

        if (!systemRoles.TryGetValue(PlatformRoleTemplates.StaffAdministratorCode, out PlatformRole? administratorRole))
        {
            // PlatformRoleSeeder must run first; both are registered in that order and this is the
            // symptom if something changes that order.
            logger.LogError(
                "Cannot create the first platform administrator: the {RoleCode} role does not exist yet.",
                PlatformRoleTemplates.StaffAdministratorCode);
            return;
        }

        var tokenGenerator = scope.ServiceProvider.GetRequiredService<IInvitationTokenGenerator>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        PlatformStaffMember member = PlatformStaffMember.Invite(email, DisplayNameFor(email), now);

        (string token, string tokenHash) = tokenGenerator.Generate();
        DateTime expiresDate = now.Add(BootstrapInvitationLifetime);
        member.SetInvitationToken(tokenHash, expiresDate);

        staff.Add(member);
        roles.AddGrant(PlatformRoleAssignment.Grant(member.Id, administratorRole.Id));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Critical, not Information: this is the single most powerful credential the platform has,
        // and the only record of it that will ever exist is this line in whatever reads this
        // service's stdout. It is deliberately never written to a database column, an API response
        // or anywhere a second party could read it after the fact.
        logger.LogCritical(
            "Created the first platform administrator account for {Email}. " +
            "Invitation token: {InvitationToken} (expires {ExpiresDate:u}). " +
            "Complete it at POST /api/v1/platform-staff/complete-invitation with platformStaffId {PlatformStaffId}. " +
            "This token is not stored anywhere else and will not be shown again; if it is lost, remove the " +
            "PlatformStaff row for this address and restart this service to have a new one issued.",
            email,
            token,
            expiresDate,
            member.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string DisplayNameFor(string email)
    {
        int at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? email[..at] : email;
    }
}
