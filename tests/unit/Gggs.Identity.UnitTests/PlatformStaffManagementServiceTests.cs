using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// The last-administrator safety rule in <see cref="PlatformStaffManagementService"/>, exercised
/// against fakes rather than a database.
/// <para>
/// This is not a rule an integration test against the shared identity database can pin reliably:
/// <c>platform.staff.manage</c> is held by whichever accounts every test in that fixture has
/// seeded, and "is this the only one" depends on what else happened to run first. Here, what
/// <see cref="IPlatformRoleRepository.GetStaffIdsWithPermissionAsync"/> returns is exactly what the
/// test says it returns, so the rule is checked in isolation from everything else that touches the
/// table it reads.
/// </para>
/// </summary>
public sealed class PlatformStaffManagementServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DisablingTheOnlyAccountThatCanManageStaff_IsRefused()
    {
        Guid onlyManagerId = Guid.NewGuid();
        Guid actorId = Guid.NewGuid();

        IPlatformStaffRepository staff = FakeStaffRepositoryReturning(PlatformStaffMember.Invite(
            "only-manager@gggshr.example", "Only Manager", Now));

        IPlatformRoleRepository roles = Substitute.For<IPlatformRoleRepository>();
        roles.GetStaffIdsWithPermissionAsync(Permissions.Platform.StaffManage, Arg.Any<CancellationToken>())
            .Returns([onlyManagerId]);

        // Acted on by someone else entirely, so this is unambiguously the last-administrator rule
        // and not the separate CANNOT_DISABLE_SELF check.
        PlatformStaffManagementService service = BuildService(staff, roles, actorId);

        DomainRuleException exception = await Should.ThrowAsync<DomainRuleException>(
            () => service.DisableAsync(onlyManagerId));

        exception.ErrorCode.ShouldBe("LAST_PLATFORM_ADMINISTRATOR");
    }

    [Fact]
    public async Task DisablingOneOfTwoAccountsThatCanManageStaff_Succeeds()
    {
        Guid targetId = Guid.NewGuid();
        Guid otherManagerId = Guid.NewGuid();
        Guid actorId = Guid.NewGuid();

        IPlatformStaffRepository staff = FakeStaffRepositoryReturning(PlatformStaffMember.Invite(
            "target@gggshr.example", "Target", Now));

        IPlatformRoleRepository roles = Substitute.For<IPlatformRoleRepository>();
        roles.GetStaffIdsWithPermissionAsync(Permissions.Platform.StaffManage, Arg.Any<CancellationToken>())
            .Returns([targetId, otherManagerId]);

        PlatformStaffManagementService service = BuildService(staff, roles, actorId);

        // Must not throw: with two managers, removing one still leaves someone who can grant access.
        await service.DisableAsync(targetId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemovingAllRolesFromTheOnlyAccountThatCanManageStaff_IsRefused()
    {
        Guid onlyManagerId = Guid.NewGuid();
        Guid actorId = Guid.NewGuid();

        IPlatformStaffRepository staff = FakeStaffRepositoryReturning(PlatformStaffMember.Invite(
            "only-manager@gggshr.example", "Only Manager", Now));

        IPlatformRoleRepository roles = Substitute.For<IPlatformRoleRepository>();
        roles.GetStaffIdsWithPermissionAsync(Permissions.Platform.StaffManage, Arg.Any<CancellationToken>())
            .Returns([onlyManagerId]);
        roles.GetGrantsForStaffAsync(onlyManagerId, Arg.Any<CancellationToken>())
            .Returns([PlatformRoleAssignment.Grant(onlyManagerId, Guid.NewGuid())]);

        PlatformStaffManagementService service = BuildService(staff, roles, actorId);

        DomainRuleException exception = await Should.ThrowAsync<DomainRuleException>(
            () => service.SetRolesAsync(onlyManagerId, []));

        exception.ErrorCode.ShouldBe("LAST_PLATFORM_ADMINISTRATOR");
    }

    [Fact]
    public async Task AnAdministratorCannotDisableTheirOwnAccount()
    {
        Guid selfId = Guid.NewGuid();

        IPlatformStaffRepository staff = FakeStaffRepositoryReturning(PlatformStaffMember.Invite(
            "self@gggshr.example", "Self", Now));

        IPlatformRoleRepository roles = Substitute.For<IPlatformRoleRepository>();

        // Acting on themselves, so this must be refused before the last-administrator check ever
        // runs - the roles repository is left unconfigured, and a call to it here would fail the
        // test the way an unexpected call should.
        PlatformStaffManagementService service = BuildService(staff, roles, actorId: selfId);

        DomainRuleException exception = await Should.ThrowAsync<DomainRuleException>(
            () => service.DisableAsync(selfId));

        exception.ErrorCode.ShouldBe("CANNOT_DISABLE_SELF");
        roles.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A repository that returns the given member for whatever id is asked for - these tests only ever ask for one.</summary>
    private static IPlatformStaffRepository FakeStaffRepositoryReturning(PlatformStaffMember member)
    {
        IPlatformStaffRepository staff = Substitute.For<IPlatformStaffRepository>();
        staff.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(member);
        return staff;
    }

    private static PlatformStaffManagementService BuildService(
        IPlatformStaffRepository staff,
        IPlatformRoleRepository roles,
        Guid actorId)
    {
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(actorId);

        return new PlatformStaffManagementService(
            staff,
            roles,
            Substitute.For<IPasswordHasher>(),
            Substitute.For<IInvitationTokenGenerator>(),
            currentUser,
            Substitute.For<IUnitOfWork>(),
            TimeProvider.System,
            NullLogger<PlatformStaffManagementService>.Instance);
    }
}
