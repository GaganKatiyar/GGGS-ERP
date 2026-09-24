using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Infrastructure;
using Shouldly;
using DomainTenantStatus = Gggs.BuildingBlocks.Domain.Abstractions.TenantStatus;

namespace Gggs.Identity.UnitTests;

/// <summary>
/// Round-trips every type that gets stored in the distributed cache.
/// <para>
/// This exists because the same mistake has been made twice. A type that cannot be deserialized -
/// an interface-typed collection, a constructor the serializer cannot bind - works perfectly while
/// the entry is served from memory, and fails the moment a second instance reads it from Redis.
/// The symptom is an authorization outage in production and nothing at all in development, so the
/// constraint is pinned here where it is cheap to discover.
/// </para>
/// </summary>
public sealed class CacheSerializationTests
{
    /// <summary>Matches what HybridCache uses by default.</summary>
    private static readonly JsonSerializerOptions Options = JsonSerializerOptions.Default;

    [Fact]
    public void APermissionSnapshotSurvivesTheCache()
    {
        PermissionSnapshot original = new(
            [
                new PermissionGrant { Code = "employee.profile.view", Scope = DataScope.Branch },
                new PermissionGrant { Code = "leave.request.approve", Scope = DataScope.AllReports, ScopeId = Guid.CreateVersion7() },
            ],
            PermissionVersion: 7);

        PermissionSnapshot restored = RoundTrip(original);

        restored.PermissionVersion.ShouldBe(7);
        restored.Grants.Count.ShouldBe(2);
        restored.Grants[0].Code.ShouldBe("employee.profile.view");
        restored.Grants[0].Scope.ShouldBe(DataScope.Branch);
    }

    [Fact]
    public void APermissionSetRebuiltFromASnapshotStillAnswersCorrectly()
    {
        // The behaviour has to survive the round trip, not just the data: case-insensitive lookup
        // is a property of the set, and a comparer does not travel through JSON.
        PermissionSnapshot restored = RoundTrip(new PermissionSnapshot(
            [new PermissionGrant { Code = "Employee.Profile.View", Scope = DataScope.Tenant }],
            1));

        PermissionSet permissions = new(restored.Grants, restored.PermissionVersion);

        permissions.Has("employee.profile.view").ShouldBeTrue();
        permissions.ScopeFor("EMPLOYEE.PROFILE.VIEW").ShouldBe(DataScope.Tenant);
    }

    [Fact]
    public void TenantInfoSurvivesTheCache()
    {
        // Cached on the sign-in path of every service, so a failure here would stop all of them.
        TenantInfo original = new()
        {
            Id = Guid.CreateVersion7(),
            Code = "acme",
            Name = "Acme Manufacturing",
            Status = DomainTenantStatus.Active,
            IsolationTier = TenantIsolationTier.Pooled,
            DataRegion = "centralindia",
            CountryCode = "IN",
            DefaultTimeZone = "Asia/Kolkata",
            DefaultCurrency = "INR",
            DefaultLocale = "en-IN",
            EnabledFeatures = ["CoreHr", "Payroll"],
        };

        TenantInfo restored = RoundTrip(original);

        restored.Code.ShouldBe("acme");
        restored.Status.ShouldBe(DomainTenantStatus.Active);
        restored.EnabledFeatures.ShouldContain("Payroll");

        // Case-insensitivity is implemented by explicit comparison for exactly this reason.
        restored.HasFeature("payroll").ShouldBeTrue();
    }

    private static T RoundTrip<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options)!;
    }
}
