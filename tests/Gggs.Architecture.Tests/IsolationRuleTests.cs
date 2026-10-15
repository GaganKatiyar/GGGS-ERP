using System.Reflection;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Composition;
using Gggs.Data;
using Gggs.Modules.Platform.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetArchTest.Rules;
using Shouldly;
using TestResult = NetArchTest.Rules.TestResult;

namespace Gggs.Architecture.Tests;

/// <summary>
/// Rules that keep tenant isolation from eroding as modules are added.
/// <para>
/// Reviews catch most of this, but not on the Friday afternoon when someone adds a table in a
/// hurry. These run on every pull request against the real model the application builds.
/// </para>
/// </summary>
public sealed class IsolationRuleTests
{
    /// <summary>
    /// Mapped types that are neither tenant- nor platform-owned. Each entry is a decision, so adding
    /// one is a visible change in a pull request.
    /// </summary>
    private static readonly Dictionary<Type, string> OwnershipExemptions = new()
    {
        [typeof(OutboxMessage)] =
            "The dispatcher reads it across tenants in a platform scope; each row carries its tenant.",
        [typeof(InboxMessage)] =
            "Deduplication bookkeeping keyed by message id; each row carries its tenant.",
    };

    /// <summary>Tenant-owned tables that deliberately have no audit or soft-delete columns.</summary>
    private static readonly Dictionary<Type, string> AuditColumnExemptions = new()
    {
        [typeof(AuditLog)] =
            "The audit log is append-only: it is the record of who changed what, and is never edited or deleted.",
    };

    [Fact]
    public void EveryMappedEntity_DeclaresWhoOwnsIt()
    {
        // A table is either a tenant's (filtered and RLS-protected) or the platform's (the registry,
        // plans, platform staff). Anything else is a table nobody decided about.
        List<string> offenders =
        [
            .. MappedEntityTypes()
                .Where(type => !typeof(ITenantOwned).IsAssignableFrom(type))
                .Where(type => !typeof(IPlatformOwned).IsAssignableFrom(type))
                .Where(type => !OwnershipExemptions.ContainsKey(type))
                .Where(type => type.Namespace?.StartsWith("OpenIddict", StringComparison.Ordinal) != true)
                .Select(type => type.FullName!)
        ];

        offenders.ShouldBeEmpty(
            $"these entities are neither ITenantOwned nor IPlatformOwned: {string.Join(", ", offenders)}. " +
            "If that is deliberate, add it to OwnershipExemptions with the reason.");
    }

    [Fact]
    public void ExemptInfrastructureTables_StillRecordTheirTenant()
    {
        foreach (Type exempt in OwnershipExemptions.Keys)
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
            .. MappedEntityTypes()
                .Where(type => typeof(ITenantOwned).IsAssignableFrom(type))
                .Where(type => !AuditColumnExemptions.ContainsKey(type))
                .Where(type => !typeof(IAuditable).IsAssignableFrom(type) || !typeof(ISoftDeletable).IsAssignableFrom(type))
                .Select(type => type.FullName!)
        ];

        offenders.ShouldBeEmpty($"these entities cannot answer 'who changed this': {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryTenantOwnedTable_IsMarkedForRowLevelSecurity()
    {
        // The RLS policy is generated from this annotation; a tenant-owned table without it would
        // be left without the database backstop.
        using AppDbContext context = TestDbContextFactory.CreateForModelInspection();

        List<string> offenders =
        [
            .. context.Model.GetEntityTypes()
                .Where(entityType => !entityType.IsOwned() && typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                .Where(entityType => entityType.FindAnnotation(RowLevelSecurity.TenantOwnedAnnotation)?.Value is not true)
                .Select(entityType => entityType.DisplayName())
        ];

        offenders.ShouldBeEmpty($"tenant-owned tables missing the RLS annotation: {string.Join(", ", offenders)}");
        RowLevelSecurity.TenantTablesOf(context.Model).ShouldNotBeEmpty();
    }

    [Fact]
    public void NoTable_LandsInTheDefaultSchema()
    {
        // Every module owns a schema; a table in dbo is one that forgot to say whose it is.
        using AppDbContext context = TestDbContextFactory.CreateForModelInspection();

        List<string> offenders =
        [
            .. context.Model.GetEntityTypes()
                .Where(entityType => !entityType.IsOwned() && entityType.GetTableName() is not null)
                .Where(entityType => (entityType.GetSchema() ?? AppDbContext.FallbackSchema) == AppDbContext.FallbackSchema)
                .Select(entityType => entityType.DisplayName())
        ];

        offenders.ShouldBeEmpty($"tables with no module schema: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheModel_HasNoChangesMissingAMigration()
    {
        // A model change without a migration means the database the application expects is not the
        // one any environment will have - and the RLS policy will not have been re-synced either.
        using AppDbContext context = TestDbContextFactory.CreateForModelInspection();

        context.Database.HasPendingModelChanges().ShouldBeFalse(
            "the model has changed since the last migration; add one with dotnet ef migrations add " +
            "(and call SyncTenantRowLevelSecurity if tenant-owned tables changed).");
    }

    [Fact]
    public void Modules_NeverDeclareTheirOwnDbContext()
    {
        // One database, one context (ADR-12). A module-local context would split the unit of work
        // and escape the shared conventions.
        foreach (Assembly module in ModuleCatalog.ModelAssemblies)
        {
            Types.InAssembly(module).That().Inherit(typeof(DbContext)).GetTypes().ShouldBeEmpty(
                $"{module.GetName().Name} declares its own DbContext; use AppDbContext and an IModuleModel.");
        }
    }

    [Fact]
    public void DomainLayer_DoesNotDependOnPersistenceOrHttp()
    {
        TestResult buildingBlock = Types.InAssembly(typeof(Entity).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "System.Data")
            .GetResult();

        buildingBlock.IsSuccessful.ShouldBeTrue($"domain primitives must stay free of infrastructure: {Describe(buildingBlock)}");

        foreach (Assembly module in ModuleCatalog.ModelAssemblies)
        {
            TestResult result = Types.InAssembly(module)
                .That()
                .ResideInNamespaceContaining(".Domain")
                .Should()
                .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Gggs.Data")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"{module.GetName().Name}: domain types must stay free of infrastructure: {Describe(result)}");
        }
    }

    [Fact]
    public void RequestDtos_NeverCarryATenantId()
    {
        // The tenant comes from the credential. A TenantId on a request DTO is an invitation to trust
        // the client, which is the one thing the isolation design never does. Domain entities are
        // not DTOs - a leave request is tenant-owned by design, its TenantId set by the interceptor -
        // so only types that are not entities are checked.
        List<string> offenders =
        [
            .. ModuleCatalog.ModelAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.Name.EndsWith("Request", StringComparison.Ordinal))
                .Where(type => !typeof(Entity).IsAssignableFrom(type))
                .Where(type => type.GetProperties().Any(property =>
                    property.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase)))
                .Select(type => type.FullName!)
        ];

        offenders.ShouldBeEmpty($"request DTOs exposing TenantId: {string.Join(", ", offenders)}");
    }

    private static IEnumerable<Type> MappedEntityTypes()
    {
        using AppDbContext context = TestDbContextFactory.CreateForModelInspection();

        return
        [
            .. context.Model.GetEntityTypes()
                .Where(entityType => !entityType.IsOwned())
                .Select(entityType => entityType.ClrType)
                .Distinct(),
        ];
    }

    private static string Describe(TestResult result)
        => result.FailingTypeNames is null ? "none" : string.Join(", ", result.FailingTypeNames);
}
