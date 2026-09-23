using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Platform.Domain.Tenants;
using Gggs.Platform.Domain.Tenants.Events;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>
/// The tenant lifecycle. Every transition here has a consequence for real people - losing access
/// to payroll, or having data destroyed - so the rules are pinned rather than left to the caller.
/// </summary>
public sealed class TenantTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ANewTenant_StartsProvisioningRatherThanActive()
    {
        Tenant tenant = CreateTenant();

        // Active on creation would let someone sign in before roles, policies and the first admin
        // exist, and see a half-built system.
        tenant.Status.ShouldBe(TenantStatus.Provisioning);
        tenant.IsOperational.ShouldBeFalse();
        tenant.DomainEvents.OfType<TenantRegisteredDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void CompletingProvisioning_MakesTheTenantOperational()
    {
        Tenant tenant = CreateTenant();

        tenant.CompleteProvisioning(Now);

        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.IsOperational.ShouldBeTrue();
        tenant.ActivatedDate.ShouldBe(Now);
    }

    [Fact]
    public void ATenantCannotBeActivatedTwice()
    {
        Tenant tenant = CreateTenant();
        tenant.CompleteProvisioning(Now);

        Should.Throw<DomainRuleException>(() => tenant.CompleteProvisioning(Now))
            .ErrorCode.ShouldBe("TENANT_NOT_PROVISIONING");
    }

    [Fact]
    public void SuspensionStopsAccessButKeepsTheTenantRecoverable()
    {
        Tenant tenant = ActiveTenant();

        tenant.Suspend("Invoice unpaid for 60 days", Now);

        tenant.Status.ShouldBe(TenantStatus.Suspended);
        tenant.IsOperational.ShouldBeFalse();
        tenant.SuspensionReason.ShouldBe("Invoice unpaid for 60 days");

        // Nothing about the data changed, so reactivating is immediate.
        tenant.Reactivate(Now);
        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.SuspensionReason.ShouldBeNull();
    }

    [Fact]
    public void SuspensionRequiresAReason()
    {
        // Someone will have to explain to the customer why they were locked out.
        Should.Throw<ArgumentException>(() => ActiveTenant().Suspend("  ", Now));
    }

    [Fact]
    public void OffboardingRequiresAGracePeriodOfAtLeastAWeek()
    {
        // A customer who leaves by mistake, or whose renewal is stuck in procurement, must be
        // recoverable without a database restore.
        Should.Throw<DomainRuleException>(() => ActiveTenant().BeginOffboarding(TimeSpan.FromDays(1), Now))
            .ErrorCode.ShouldBe("TENANT_GRACE_TOO_SHORT");
    }

    [Fact]
    public void DataCannotBeArchivedBeforeTheGracePeriodElapses()
    {
        Tenant tenant = ActiveTenant();
        tenant.BeginOffboarding(TimeSpan.FromDays(30), Now);

        Should.Throw<DomainRuleException>(() => tenant.Archive(Now.AddDays(10)))
            .ErrorCode.ShouldBe("TENANT_GRACE_NOT_ELAPSED");

        tenant.Archive(Now.AddDays(31));
        tenant.Status.ShouldBe(TenantStatus.Archived);
    }

    [Fact]
    public void PurgeIsTerminalAndOnlyFollowsArchiving()
    {
        Tenant tenant = ActiveTenant();

        Should.Throw<DomainRuleException>(() => tenant.MarkPurged(Now))
            .ErrorCode.ShouldBe("TENANT_NOT_ARCHIVED");

        tenant.BeginOffboarding(TimeSpan.FromDays(30), Now);
        tenant.Archive(Now.AddDays(31));
        tenant.MarkPurged(Now.AddDays(31));

        tenant.Status.ShouldBe(TenantStatus.Purged);

        // The row survives so the code can never be handed to another customer: old links and
        // cached references would otherwise resolve to somebody else's tenant.
        tenant.Code.ShouldBe("acme");
    }

    [Theory]
    [InlineData("ab")]                                  // too short
    [InlineData("this-code-is-far-too-long-to-be-a-tenant-code")]
    [InlineData("has space")]
    [InlineData("has_underscore")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("Ünicode")]
    public void InvalidTenantCodes_AreRejected(string code)
    {
        // The code becomes part of a hostname, so anything not safe there cannot be accepted.
        Should.Throw<DomainRuleException>(() => CreateTenant(code));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("WWW")]
    [InlineData("platform")]
    public void ReservedTenantCodes_AreRejected(string code)
    {
        // A tenant claiming "api" or "admin" would collide with our own routes and hostnames.
        Should.Throw<DomainRuleException>(() => CreateTenant(code))
            .ErrorCode.ShouldBe("TENANT_CODE_RESERVED");
    }

    [Fact]
    public void TenantCodesAndCurrenciesAreNormalized()
    {
        Tenant tenant = Tenant.Register(
            "  ACME-Group  ", "Acme", "in", "CentralIndia", "Asia/Kolkata", "inr", "en-IN",
            TenantIsolationTier.Pooled, Now);

        tenant.Code.ShouldBe("acme-group");
        tenant.CountryCode.ShouldBe("IN");
        tenant.DefaultCurrency.ShouldBe("INR");
        tenant.DataRegion.ShouldBe("centralindia");
    }

    [Fact]
    public void ChangingIsolationTier_RaisesAnEventForThePlatformTeam()
    {
        Tenant tenant = ActiveTenant();

        tenant.ChangeIsolationTier(TenantIsolationTier.Dedicated);

        tenant.IsolationTier.ShouldBe(TenantIsolationTier.Dedicated);

        // The record changes here; moving the data is a separate operation the event triggers.
        tenant.DomainEvents.OfType<TenantIsolationTierChangedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void ChangingToTheSameTier_IsANoOp()
    {
        Tenant tenant = ActiveTenant();
        tenant.ClearDomainEvents();

        tenant.ChangeIsolationTier(TenantIsolationTier.Pooled);

        tenant.DomainEvents.ShouldBeEmpty();
    }

    private static Tenant CreateTenant(string code = "acme") => Tenant.Register(
        code, "Acme Manufacturing", "IN", "centralindia", "Asia/Kolkata", "INR", "en-IN",
        TenantIsolationTier.Pooled, Now);

    private static Tenant ActiveTenant()
    {
        Tenant tenant = CreateTenant();
        tenant.CompleteProvisioning(Now);
        return tenant;
    }
}
