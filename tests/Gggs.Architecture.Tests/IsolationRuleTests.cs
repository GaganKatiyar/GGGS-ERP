using System.Reflection;
using Gggs.BuildingBlocks.Domain.Abstractions;
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

    [Fact]
    public void EveryPersistedEntity_IsTenantOwned()
    {
        // A table without a TenantId cannot be isolated by the query filter or by row-level
        // security, so entities are tenant-owned unless they are deliberately platform-wide.
        IEnumerable<Type> entityTypes = DiscoverMappedEntityTypes();

        List<string> offenders =
        [
            .. entityTypes
                .Where(type => !typeof(ITenantOwned).IsAssignableFrom(type))
                .Select(type => type.FullName!)
        ];

        offenders.ShouldBeEmpty(
            $"these entities are missing TenantId: {string.Join(", ", offenders)}. " +
            "Platform-wide reference tables must be declared in the platform database instead.");
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
