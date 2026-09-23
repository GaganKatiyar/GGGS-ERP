using System.Reflection;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Sample.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Shouldly;
using TestResult = NetArchTest.Rules.TestResult;

namespace Gggs.Architecture.Tests;

/// <summary>
/// Rules that keep tenant isolation from eroding as the codebase grows.
/// <para>
/// Reviews catch most of this, but not on the Friday afternoon when someone adds a table in a
/// hurry. These run on every pull request instead.
/// </para>
/// </summary>
public sealed class IsolationRuleTests
{
    private static readonly Assembly SampleService = typeof(SampleDbContext).Assembly;

    /// <summary>
    /// Infrastructure tables that deliberately are not tenant-owned. Each entry is a decision, not
    /// an oversight, so adding one is a visible change in a pull request.
    /// </summary>
    private static readonly Dictionary<Type, string> TenantOwnershipExemptions = new()
    {
        [typeof(OutboxMessage)] =
            "The relay reads it across tenants in a platform scope; a query filter or RLS policy " +
            "would leave it seeing an empty table and publishing nothing.",
        [typeof(InboxMessage)] =
            "Deduplication happens before a tenant context exists for the incoming message.",
    };

    [Fact]
    public void EveryBusinessEntity_IsTenantOwned()
    {
        // A table without a TenantId cannot be isolated by the query filter or by row-level
        // security, so entities are tenant-owned unless they are deliberately exempt.
        List<string> offenders =
        [
            .. DiscoverMappedEntityTypes()
                .Where(type => !typeof(ITenantOwned).IsAssignableFrom(type))
                .Where(type => !TenantOwnershipExemptions.ContainsKey(type))
                .Select(type => type.FullName!)
        ];

        offenders.ShouldBeEmpty(
            $"these entities are missing TenantId: {string.Join(", ", offenders)}. " +
            "If that is deliberate, add it to TenantOwnershipExemptions with the reason.");
    }

    [Fact]
    public void ExemptInfrastructureTables_StillRecordTheirTenant()
    {
        // Being outside the filter is not the same as losing the tenant: an outbox row still has to
        // say which tenant its message belongs to, or consumers could not restore the context.
        foreach (Type exempt in TenantOwnershipExemptions.Keys)
        {
            exempt.GetProperty("TenantId").ShouldNotBeNull(
                $"{exempt.Name} is exempt from tenant ownership but must still carry a TenantId column.");
        }
    }

    [Fact]
    public void EveryTenantOwnedEntity_AlsoCarriesAuditAndSoftDeleteColumns()
    {
        List<string> offenders =
        [
            .. DiscoverMappedEntityTypes()
                .Where(type => typeof(ITenantOwned).IsAssignableFrom(type))
                .Where(type => !typeof(IAuditable).IsAssignableFrom(type) || !typeof(ISoftDeletable).IsAssignableFrom(type))
                .Select(type => type.FullName!)
        ];

        offenders.ShouldBeEmpty($"these entities cannot answer 'who changed this': {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ServiceDbContexts_DeriveFromTheSharedBaseContext()
    {
        // Deriving straight from DbContext would silently skip the tenant filter, the audit
        // interceptors and the clustered key layout.
        TestResult result = Types.InAssembly(SampleService)
            .That()
            .Inherit(typeof(DbContext))
            .Should()
            .Inherit(typeof(GggsDbContext))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"contexts bypassing GggsDbContext: {Describe(result)}");
    }

    [Fact]
    public void DomainLayer_DoesNotDependOnPersistenceOrHttp()
    {
        TestResult result = Types.InAssembly(typeof(Entity).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "System.Data")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"domain types must stay free of infrastructure: {Describe(result)}");
    }

    [Fact]
    public void RequestDtos_NeverCarryATenantId()
    {
        // The tenant comes from the token. A TenantId on a request DTO is an invitation to trust
        // the client, which is the one thing the isolation design never does.
        List<string> offenders =
        [
            .. SampleService.GetTypes()
                .Where(type => type.Name.EndsWith("Request", StringComparison.Ordinal))
                .Where(type => type.GetProperties().Any(property =>
                    property.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase)))
                .Select(type => type.FullName!)
        ];

        offenders.ShouldBeEmpty($"request DTOs exposing TenantId: {string.Join(", ", offenders)}");
    }

    /// <summary>Entity types the sample context actually maps, read from the EF model itself.</summary>
    private static IEnumerable<Type> DiscoverMappedEntityTypes()
    {
        using SampleDbContext context = TestDbContextFactory.CreateForModelInspection();

        return context.Model.GetEntityTypes()
            .Where(entityType => !entityType.IsOwned())
            .Select(entityType => entityType.ClrType)
            .Where(type => type is not null)
            .Distinct();
    }

    private static string Describe(TestResult result)
        => result.FailingTypeNames is null ? "none" : string.Join(", ", result.FailingTypeNames);
}
