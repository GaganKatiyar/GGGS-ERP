using System.Net;
using System.Net.Http.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Platform.Api.Contracts;
using Gggs.Platform.Domain.Audit;
using Gggs.Platform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Platform.IntegrationTests;

/// <summary>
/// <c>/api/v1/audit/logs</c> over HTTP, with the real query filter and authorization pipeline in
/// place.
/// <para>
/// Rows are seeded directly through a platform-scoped context rather than by driving a real message
/// through a broker: no broker runs in this suite (see <c>Gggs.Platform.Worker</c>'s own remarks on
/// why), so this exercises everything downstream of the consumer - which is also everything a tenant
/// admin's request actually touches.
/// </para>
/// </summary>
[Collection(nameof(PlatformApiCollection))]
public sealed class AuditEndpointTests(PlatformApiFactory factory)
{
    private static readonly Guid OtherTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static Guid TenantId => PlatformApiFactory.SeededTenantId;

    [Fact]
    public async Task ListingLogs_WithoutCredentials_IsRefused()
    {
        using HttpClient client = factory.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/audit/logs",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListingLogs_WithoutTheAuditPermission_IsRefused()
    {
        using HttpClient client = factory.CreateClientFor(TenantId, Permissions.Admin.SettingView);

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/audit/logs",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListingLogs_ReturnsOnlyTheCallersTenant()
    {
        string entityId = $"probe-{Guid.CreateVersion7():N}";
        await SeedAsync(TenantId, entityId, "Created", newValuesJson: "{\"Code\":\"AUD-1\"}");
        await SeedAsync(OtherTenantId, entityId, "Created", newValuesJson: "{\"Code\":\"OTHER\"}");

        using HttpClient client = factory.CreateClientFor(TenantId, Permissions.Audit.LogView);

        PagedResult<AuditLogItem>? page = await client.GetFromJsonAsync<PagedResult<AuditLogItem>>(
            $"/api/v1/audit/logs?entityId={entityId}",
            TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        AuditLogItem item = page.Items.ShouldHaveSingleItem();
        item.EntityId.ShouldBe(entityId);
        item.NewValuesJson.ShouldBe("{\"Code\":\"AUD-1\"}");
    }

    [Fact]
    public async Task ListingLogs_FiltersByEntityTypeAndAction()
    {
        string entityId = $"probe-{Guid.CreateVersion7():N}";
        await SeedAsync(TenantId, entityId, "Created", entityType: "LegalEntity");
        await SeedAsync(TenantId, entityId, "Modified", entityType: "LegalEntity");
        await SeedAsync(TenantId, entityId, "Created", entityType: "Department");

        using HttpClient client = factory.CreateClientFor(TenantId, Permissions.Audit.LogView);

        PagedResult<AuditLogItem>? page = await client.GetFromJsonAsync<PagedResult<AuditLogItem>>(
            $"/api/v1/audit/logs?entityId={entityId}&entityType=LegalEntity&action=Modified",
            TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        AuditLogItem item = page.Items.ShouldHaveSingleItem();
        item.EntityType.ShouldBe("LegalEntity");
        item.Action.ShouldBe("Modified");
    }

    [Fact]
    public async Task GettingOneLog_FromAnotherTenant_ReturnsNotFoundRatherThanTheRow()
    {
        Guid otherTenantsLogId = await SeedAsync(OtherTenantId, $"probe-{Guid.CreateVersion7():N}", "Created");

        using HttpClient client = factory.CreateClientFor(TenantId, Permissions.Audit.LogView);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/audit/logs/{otherTenantsLogId}",
            TestContext.Current.CancellationToken);

        // Not 403: existence itself is not something a caller in another tenant learns.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<Guid> SeedAsync(
        Guid tenantId,
        string entityId,
        string action,
        string entityType = "LegalEntity",
        string? newValuesJson = "{}")
    {
        using IServiceScope scope = factory.Services.CreateScope();

        // Platform scope, not a resolved tenant: seeding two different tenants' rows in one call
        // needs to bypass the same-tenant check that a real request would (correctly) be held to.
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        using IDisposable platformScope = tenantContext.BeginPlatformScope("audit-endpoint-test-seed");

        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        AuditLog auditLog = AuditLog.Record(
            tenantId,
            Guid.CreateVersion7(),
            DateTime.UtcNow,
            userId: Guid.CreateVersion7(),
            service: "corehr",
            action: action,
            entityType: entityType,
            entityId: entityId,
            oldValuesJson: null,
            newValuesJson: newValuesJson,
            changedFieldsCsv: action == "Modified" ? "Name" : null,
            correlationId: "test-seed");

        db.AuditLogs.Add(auditLog);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return auditLog.Id;
    }
}
