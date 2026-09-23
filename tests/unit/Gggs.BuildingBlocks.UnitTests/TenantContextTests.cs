using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Shouldly;

namespace Gggs.BuildingBlocks.UnitTests;

public sealed class TenantContextTests
{
    [Fact]
    public void ReadingTheTenantBeforeItIsResolved_Throws()
    {
        var context = new TenantContext();

        context.IsResolved.ShouldBeFalse();

        // Failing loudly is the point: a silent Guid.Empty would quietly query "tenant zero".
        Should.Throw<TenantNotResolvedException>(() => context.TenantId);
    }

    [Fact]
    public void PlatformScope_EndsWhenDisposed()
    {
        var context = new TenantContext();
        context.SetTenant(Guid.NewGuid());

        using (context.BeginPlatformScope("tenant-provisioning"))
        {
            context.IsPlatformScope.ShouldBeTrue();
        }

        context.IsPlatformScope.ShouldBeFalse();
    }

    [Fact]
    public void PlatformScope_RequiresAReason()
    {
        var context = new TenantContext();

        // Every bypass of tenant isolation has to be explainable in the audit trail.
        Should.Throw<ArgumentException>(() => context.BeginPlatformScope(" "));
    }

    [Fact]
    public void SuspendedTenant_IsNotOperational()
    {
        TenantInfo tenant = CreateTenant() with { Status = TenantStatus.Suspended };

        tenant.IsOperational.ShouldBeFalse();
    }

    private static TenantInfo CreateTenant() => new()
    {
        Id = Guid.NewGuid(),
        Code = "acme",
        Name = "Acme",
        Status = TenantStatus.Active,
        IsolationTier = TenantIsolationTier.Pooled,
        DataRegion = "centralindia",
        CountryCode = "IN",
        DefaultTimeZone = "Asia/Kolkata",
        DefaultCurrency = "INR",
        DefaultLocale = "en-IN",
    };
}

public sealed class RowLevelSecurityTests
{
    [Fact]
    public void Policy_AddsFilterAndBlockPredicatesForEveryTable()
    {
        string sql = RowLevelSecurity.CreatePolicySql(
        [
            new RowLevelSecurity.TenantTable("emp", "Employee"),
            new RowLevelSecurity.TenantTable("org", "Department"),
        ]);

        // The filter hides other tenants' rows; the block predicates stop writes from landing in,
        // or being moved to, another tenant. Missing either one leaves a hole.
        sql.ShouldContain("ADD FILTER PREDICATE security.fn_TenantAccessPredicate([TenantId]) ON [emp].[Employee]");
        sql.ShouldContain("ON [emp].[Employee] AFTER INSERT");
        sql.ShouldContain("ON [emp].[Employee] AFTER UPDATE");
        sql.ShouldContain("ON [org].[Department] AFTER UPDATE");
        sql.ShouldContain("STATE = ON");
    }

    [Fact]
    public void PredicateFunction_ReadsSessionContextAndAllowsOnlyTheBypassRole()
    {
        string sql = RowLevelSecurity.CreatePredicateFunctionSql();

        sql.ShouldContain("SESSION_CONTEXT(N'TenantId')");
        sql.ShouldContain("IS_ROLEMEMBER(N'rls_bypass')");
        sql.ShouldContain("WITH SCHEMABINDING");
    }

    [Fact]
    public void Policy_RequiresAtLeastOneTable()
    {
        Should.Throw<ArgumentException>(() => RowLevelSecurity.CreatePolicySql([]));
    }
}
