using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// The console's first-run setup: the most powerful account the platform has, created from a page
/// anyone can reach. So it must stay shut unless the key is right, a key is configured at all, and
/// nobody has an account yet.
/// </summary>
public sealed class PlatformSetupServiceTests
{
    private const string Key = "the-setup-key";
    private const string Password = "a long enough setup passphrase";

    private readonly IPlatformStaffRepository staff = Substitute.For<IPlatformStaffRepository>();
    private readonly IPlatformRoleRepository roles = Substitute.For<IPlatformRoleRepository>();
    private readonly IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task AWrongKey_IsRefused()
    {
        ForbiddenException refused = await Should.ThrowAsync<ForbiddenException>(
            () => Service(Key).CreateFirstAdministratorAsync("guess", "First Admin", "first@gggshr.example", Password));

        refused.ErrorCode.ShouldBe("SETUP_KEY_INVALID");
        staff.DidNotReceive().Add(Arg.Any<PlatformStaffMember>());
    }

    [Fact]
    public async Task NoConfiguredKey_KeepsSetupShutEvenForAnEmptyGuess()
    {
        (await Service(null).IsOpenAsync()).ShouldBeFalse();

        await Should.ThrowAsync<ForbiddenException>(
            () => Service(null).CreateFirstAdministratorAsync(string.Empty, "First Admin", "first@gggshr.example", Password));
    }

    [Fact]
    public async Task OnceAnyStaffExist_SetupIsClosed()
    {
        staff.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);

        (await Service(Key).IsOpenAsync()).ShouldBeFalse();

        DomainRuleException closed = await Should.ThrowAsync<DomainRuleException>(
            () => Service(Key).CreateFirstAdministratorAsync(Key, "First Admin", "first@gggshr.example", Password));
        closed.ErrorCode.ShouldBe("SETUP_CLOSED");
    }

    [Fact]
    public async Task TheRightKey_CreatesAnActiveStaffAdministrator()
    {
        PlatformRole administrator = PlatformRole.Create("Platform Staff Administrator", null, isSystem: true, templateCode: PlatformRoleTemplates.StaffAdministratorCode);
        roles.GetSystemRolesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, PlatformRole> { [PlatformRoleTemplates.StaffAdministratorCode] = administrator });

        (await Service(Key).IsOpenAsync()).ShouldBeTrue();
        await Service(Key).CreateFirstAdministratorAsync(Key, "First Admin", "first@gggshr.example", Password);

        staff.Received(1).Add(Arg.Is<PlatformStaffMember>(member => member.Status == PlatformStaffStatus.Active && member.Email == "first@gggshr.example"));
        roles.Received(1).AddGrant(Arg.Is<PlatformRoleAssignment>(grant => grant.PlatformRoleId == administrator.Id));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private PlatformSetupService Service(string? key)
    {
        IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("hashed");
        return new PlatformSetupService(
            staff,
            roles,
            hasher,
            Options.Create(new PlatformBootstrapOptions { SetupKey = key }),
            unitOfWork,
            TimeProvider.System,
            NullLogger<PlatformSetupService>.Instance);
    }
}
