using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Gggs.Modules.Platform.Domain.Tenants;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Gggs.TenantIsolation.Tests;

/// <summary>
/// The tenant registry against a real database.
/// <para>
/// Every request in every service resolves its tenant through this, so a mistake here is not a
/// feature bug - it decides whether a suspended customer can still sign in, and which modules
/// anyone sees.
/// </para>
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class PlatformRegistryTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task AnActiveTenantWithASubscription_ResolvesWithItsPlanFeatures()
    {
        (Tenant tenant, _) = await fixture.SeedTenantAsync("resolve-ok", activate: true, withSubscription: true);

        TenantInfo? resolved = await fixture.CreateRegistry().GetAsync(tenant.Id, TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
        resolved.Code.ShouldBe("resolve-ok");
        resolved.Status.ShouldBe(TenantStatus.Active);
        resolved.IsOperational.ShouldBeTrue();
        resolved.EnabledFeatures.ShouldContain("Payroll");
        resolved.HasFeature("payroll").ShouldBeTrue("feature checks are case-insensitive");
    }

    [Fact]
    public async Task ATenantStillProvisioning_IsNotOperational()
    {
        // Signing in mid-provisioning would show a half-built system: no roles, no policies.
        (Tenant tenant, _) = await fixture.SeedTenantAsync("resolve-provisioning", activate: false, withSubscription: true);

        TenantInfo? resolved = await fixture.CreateRegistry().GetAsync(tenant.Id, TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
        resolved.Status.ShouldBe(TenantStatus.Provisioning);
        resolved.IsOperational.ShouldBeFalse();
    }

    [Fact]
    public async Task ATenantWithNoLiveSubscription_GetsNoFeatures()
    {
        // Failing closed is the only safe default: entitlement that cannot be established must not
        // resolve to "everything".
        (Tenant tenant, _) = await fixture.SeedTenantAsync("resolve-nosub", activate: true, withSubscription: false);

        TenantInfo? resolved = await fixture.CreateRegistry().GetAsync(tenant.Id, TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
        resolved.EnabledFeatures.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownTenantResolvesToNothing()
    {
        TenantInfo? resolved = await fixture.CreateRegistry()
            .GetAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task ATenantCanBeResolvedByItsCode()
    {
        // The login path starts from a code or an email domain, before any token exists.
        await fixture.SeedTenantAsync("resolve-bycode", activate: true, withSubscription: true);

        TenantInfo? resolved = await fixture.CreateRegistry()
            .GetByCodeAsync("RESOLVE-ByCode", TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
        resolved.Code.ShouldBe("resolve-bycode");
    }

    [Fact]
    public async Task TenantCodesAreUniqueAcrossTheWholePlatform()
    {
        await fixture.SeedTenantAsync("resolve-dupe", activate: true, withSubscription: false);

        // Two customers sharing a code would make login discovery ambiguous and let one tenant's
        // links resolve to the other.
        await Should.ThrowAsync<DbUpdateException>(async () =>
            await fixture.SeedTenantAsync("resolve-dupe", activate: true, withSubscription: false));
    }

    [Fact]
    public async Task ASuspendedTenantIsReportedAsSuspended()
    {
        (Tenant tenant, _) = await fixture.SeedTenantAsync("resolve-suspended", activate: true, withSubscription: true);

        await using (AppDbContext context = fixture.CreateContext())
        {
            Tenant tracked = await context.Set<Tenant>().SingleAsync(
                candidate => candidate.Id == tenant.Id,
                TestContext.Current.CancellationToken);

            tracked.Suspend("Non-payment", DateTime.UtcNow);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        TenantInfo? resolved = await fixture.CreateRegistry().GetAsync(tenant.Id, TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
        resolved.Status.ShouldBe(TenantStatus.Suspended);
        resolved.IsOperational.ShouldBeFalse();
    }

    [Fact]
    public async Task ADedicatedTenantReportsItsTier()
    {
        // The connection resolver reads this to decide which database to open.
        (Tenant tenant, _) = await fixture.SeedTenantAsync(
            "resolve-dedicated", activate: true, withSubscription: true, tier: TenantIsolationTier.Dedicated);

        TenantInfo? resolved = await fixture.CreateRegistry().GetAsync(tenant.Id, TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
        resolved.IsolationTier.ShouldBe(TenantIsolationTier.Dedicated);
    }

    [Fact]
    public async Task TenantSettingsAreIsolatedBetweenTenants()
    {
        // TenantSetting is the one tenant-owned table in this database, so it must behave like
        // every other: filtered by EF and blocked by row-level security.
        (Tenant acme, _) = await fixture.SeedTenantAsync("settings-acme", activate: true, withSubscription: false);
        (Tenant globex, _) = await fixture.SeedTenantAsync("settings-globex", activate: true, withSubscription: false);

        await using (AppDbContext acmeContext = fixture.CreateContext(acme.Id))
        {
            acmeContext.Set<TenantSetting>().Add(TenantSetting.Create("leave", "WorkingDays", "[1,2,3,4,5]"));
            await acmeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AppDbContext globexContext = fixture.CreateContext(globex.Id);
        List<TenantSetting> visible = await globexContext.Set<TenantSetting>()
            .ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldBeEmpty();
    }
}
