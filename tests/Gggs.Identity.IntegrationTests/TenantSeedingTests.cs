using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Application.Authentication;
using Gggs.Identity.Application.Provisioning;
using Gggs.Identity.Application.Users;
using Gggs.Identity.Domain.Roles;
using Gggs.Identity.Domain.Users;
using Gggs.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Gggs.Identity.IntegrationTests;

/// <summary>
/// What a tenant gets the moment Platform says it exists.
/// <para>
/// Run against the real database rather than with fakes, because the parts most likely to be wrong
/// are the ones only SQL Server can show: the tenant query filter deciding what "already seeded"
/// means, and the unique index on role name deciding what a second run collides with.
/// </para>
/// <para>
/// Each test seeds its own tenant id rather than sharing one, because seeding is idempotent by
/// design: a test that ran second against a shared tenant would find nothing left to create and
/// pass for the wrong reason. Every assertion is also a cross-tenant assertion by accident - the
/// fixture's own tenant already has a Tenant Administrator role, and seeding must neither see it
/// nor be satisfied by it.
/// </para>
/// </summary>
[Collection(nameof(IdentityApiCollection))]
public sealed class TenantSeedingTests(IdentityApiFactory factory)
{
    [Fact]
    public async Task Seeding_CreatesEveryTemplateRoleAndAnAdministratorWhoCannotYetSignIn()
    {
        using IServiceScope scope = CreateTenantScope(Guid.NewGuid());

        TenantSeedingResult result = await SeedingService(scope).SeedAsync(
            "founder@example.test",
            TestContext.Current.CancellationToken);

        result.RolesCreated.ShouldBe(RoleTemplates.Definitions.Count);
        result.AdministratorUserId.ShouldNotBeNull();

        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        List<Role> roles = await database.Roles.AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);

        roles.Select(role => role.TemplateCode)
            .ShouldBe(RoleTemplates.Definitions.Select(template => template.Code), ignoreOrder: true);

        // System roles: a tenant that deleted "Employee" would have no way to grant self-service
        // to anyone again.
        roles.ShouldAllBe(role => role.IsSystem);

        Role administratorRole = roles.Single(role =>
            role.TemplateCode == RoleTemplates.TenantAdministratorTemplateCode);

        administratorRole.Permissions
            .Select(permission => permission.PermissionCode)
            .ShouldContain(Permissions.Admin.RoleManage);

        User administrator = await database.Users.AsNoTracking()
            .SingleAsync(user => user.Id == result.AdministratorUserId, TestContext.Current.CancellationToken);

        administrator.Status.ShouldBe(UserStatus.Invited);

        // No credential and no outstanding token. Seeding cannot deliver an invitation, and a token
        // created here would be a live credential its owner never received.
        administrator.PasswordHash.ShouldBeNull();
        administrator.InvitationTokenHash.ShouldBeNull();
    }

    [Fact]
    public async Task SeedingTwice_AddsNothingAndLeavesEditedPermissionsAlone()
    {
        Guid tenantId = Guid.NewGuid();

        using (IServiceScope first = CreateTenantScope(tenantId))
        {
            await SeedingService(first).SeedAsync("first@example.test", TestContext.Current.CancellationToken);
        }

        // An administrator decides the Employee role should not be able to upload documents.
        using (IServiceScope edit = CreateTenantScope(tenantId))
        {
            IdentityDbContext database = edit.ServiceProvider.GetRequiredService<IdentityDbContext>();

            Role employee = await database.Roles.SingleAsync(
                role => role.TemplateCode == RoleTemplates.EmployeeTemplateCode,
                TestContext.Current.CancellationToken);

            employee.Revoke(Permissions.Document.Upload);
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using IServiceScope second = CreateTenantScope(tenantId);

        TenantSeedingResult repeat = await SeedingService(second).SeedAsync(
            "first@example.test",
            TestContext.Current.CancellationToken);

        repeat.RolesCreated.ShouldBe(0);
        repeat.RolesAlreadyPresent.ShouldBe(RoleTemplates.Definitions.Count);

        IdentityDbContext check = second.ServiceProvider.GetRequiredService<IdentityDbContext>();

        (await check.Roles.CountAsync(TestContext.Current.CancellationToken))
            .ShouldBe(RoleTemplates.Definitions.Count);

        (await check.Users.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);

        Role employeeAfter = await check.Roles.AsNoTracking().SingleAsync(
            role => role.TemplateCode == RoleTemplates.EmployeeTemplateCode,
            TestContext.Current.CancellationToken);

        // The point of the test: a redelivered provisioning message must not quietly restore a
        // permission the customer took away.
        employeeAfter.Permissions
            .Select(permission => permission.PermissionCode)
            .ShouldNotContain(Permissions.Document.Upload);
    }

    [Fact]
    public async Task BootstrapInvitation_LetsTheFirstAdministratorSetAPasswordAndSignIn()
    {
        using IServiceScope scope = CreateTenantScope(IdentityApiFactory.OtherTenantId);

        TenantSeedingResult seeded = await SeedingService(scope).SeedAsync(
            "opener@othertestco.example",
            TestContext.Current.CancellationToken);

        AdministratorInvitation invitation = await BootstrapService(scope)
            .IssueAdministratorInvitationAsync("opener@othertestco.example", TestContext.Current.CancellationToken);

        invitation.UserId.ShouldBe(seeded.AdministratorUserId!.Value);
        invitation.InvitationToken.ShouldNotBeNullOrWhiteSpace();

        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage completion = await client.PostAsJsonAsync(
            "/api/v1/auth/complete-invitation",
            new
            {
                TenantCode = IdentityApiFactory.OtherTenantCode,
                UserId = invitation.UserId,
                Token = invitation.InvitationToken,
                NewPassword = "the first administrator passphrase",
            },
            TestContext.Current.CancellationToken);

        completion.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage signIn = await client.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new
            {
                Email = "opener@othertestco.example",
                Password = "the first administrator passphrase",
                TenantCode = IdentityApiFactory.OtherTenantCode,
            },
            TestContext.Current.CancellationToken);

        signIn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BootstrapInvitation_IsRefusedOnceTheAccountHasBeenUsed()
    {
        using IServiceScope scope = CreateTenantScope(Guid.NewGuid());

        await SeedingService(scope).SeedAsync("used@example.test", TestContext.Current.CancellationToken);

        TenantBootstrapService bootstrap = BootstrapService(scope);

        AdministratorInvitation first = await bootstrap.IssueAdministratorInvitationAsync(
            "used@example.test",
            TestContext.Current.CancellationToken);

        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        User administrator = await database.Users.SingleAsync(
            user => user.Id == first.UserId,
            TestContext.Current.CancellationToken);

        administrator.CompleteInvitation(hasher.Hash("a chosen passphrase"), DateTime.UtcNow);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Once the customer holds the account, platform staff can no longer mint a way into it.
        DomainRuleException refused = await Should.ThrowAsync<DomainRuleException>(
            () => bootstrap.IssueAdministratorInvitationAsync(
                "used@example.test",
                TestContext.Current.CancellationToken));

        refused.ErrorCode.ShouldBe("BOOTSTRAP_NOT_AVAILABLE");
    }

    [Fact]
    public async Task BootstrapInvitation_IsRefusedForSomeoneWhoIsNotTheAdministrator()
    {
        Guid tenantId = Guid.NewGuid();

        using IServiceScope scope = CreateTenantScope(tenantId);

        await SeedingService(scope).SeedAsync("boss@example.test", TestContext.Current.CancellationToken);

        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // An ordinary invited user in the same tenant: invited, no password, but no admin role.
        User other = User.Invite("someone@example.test", "Someone", UserType.TenantUser, null, DateTime.UtcNow);
        database.Users.Add(other);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        DomainRuleException refused = await Should.ThrowAsync<DomainRuleException>(
            () => BootstrapService(scope).IssueAdministratorInvitationAsync(
                "someone@example.test",
                TestContext.Current.CancellationToken));

        refused.ErrorCode.ShouldBe("BOOTSTRAP_NOT_AVAILABLE");
    }

    private IServiceScope CreateTenantScope(Guid tenantId)
    {
        IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        return scope;
    }

    /// <summary>
    /// Built by hand rather than resolved: seeding runs in the worker process, so the API host does
    /// not register it. Its dependencies are the host's real ones.
    /// </summary>
    private static TenantSeedingService SeedingService(IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<IRoleRepository>(),
        scope.ServiceProvider.GetRequiredService<IUserRepository>(),
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
        TimeProvider.System,
        NullLogger<TenantSeedingService>.Instance);

    private static TenantBootstrapService BootstrapService(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<TenantBootstrapService>();
}
