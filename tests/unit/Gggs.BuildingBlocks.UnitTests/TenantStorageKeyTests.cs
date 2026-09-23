using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.MultiTenancy;
using Shouldly;

namespace Gggs.BuildingBlocks.UnitTests;

/// <summary>
/// Storage keys are the one place where a caller-supplied string becomes a path. Everything that
/// could turn that into another tenant's file - traversal, an absolute path, a drive letter, a
/// forged prefix - is rejected here, so neither adapter has to get it right twice.
/// </summary>
public sealed class TenantStorageKeyTests
{
    private static readonly Guid AcmeTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GlobexTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Build_PrefixesTheKeyWithTheCurrentTenantsContainer()
    {
        string key = TenantStorageKey.Build(ContextFor(AcmeTenantId), "employee/abc/doc/v1/passport.pdf");

        key.ShouldBe("t-11111111111111111111111111111111/employee/abc/doc/v1/passport.pdf");
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("employee/../../t-22222222222222222222222222222222/salary.pdf")]
    [InlineData("C:/Windows/System32/config/sam")]
    [InlineData("employee//doc.pdf")]
    [InlineData("employee/doc.pdf:alternate")]
    [InlineData("employee/\u0000doc.pdf")]
    [InlineData("employee/<script>.pdf")]
    public void Sanitize_RejectsUnsafeKeys(string candidate)
    {
        Should.Throw<ArgumentException>(() => TenantStorageKey.Sanitize(candidate));
    }

    [Fact]
    public void Sanitize_NormalizesBackslashesAndTrimsSeparators()
    {
        TenantStorageKey.Sanitize(@"\employee\abc\doc.pdf\").ShouldBe("employee/abc/doc.pdf");
    }

    [Fact]
    public void Resolve_RejectsAKeyBelongingToAnotherTenant()
    {
        string acmeKey = TenantStorageKey.Build(ContextFor(AcmeTenantId), "employee/abc/contract.pdf");

        // Globex holding Acme's key - from a stale link, a guessed id, or a bug - still gets nothing.
        Should.Throw<TenantMismatchException>(() => TenantStorageKey.Resolve(ContextFor(GlobexTenantId), acmeKey));
    }

    [Fact]
    public void Resolve_ReturnsTheContainerAndRelativeKeyForTheOwningTenant()
    {
        string key = TenantStorageKey.Build(ContextFor(AcmeTenantId), "employee/abc/contract.pdf");

        (string container, string relativeKey) = TenantStorageKey.Resolve(ContextFor(AcmeTenantId), key);

        container.ShouldBe("t-11111111111111111111111111111111");
        relativeKey.ShouldBe("employee/abc/contract.pdf");
    }

    [Fact]
    public void Resolve_RejectsAKeyWithNoTenantPrefix()
    {
        Should.Throw<ArgumentException>(() => TenantStorageKey.Resolve(ContextFor(AcmeTenantId), "contract.pdf"));
    }

    [Fact]
    public void ContainerFor_IsStableAndTenantSpecific()
    {
        TenantStorageKey.ContainerFor(AcmeTenantId).ShouldBe("t-11111111111111111111111111111111");
        TenantStorageKey.ContainerFor(AcmeTenantId).ShouldNotBe(TenantStorageKey.ContainerFor(GlobexTenantId));
    }

    private static ITenantContext ContextFor(Guid tenantId)
    {
        var context = new TenantContext();
        context.SetTenant(tenantId);
        return context;
    }
}
