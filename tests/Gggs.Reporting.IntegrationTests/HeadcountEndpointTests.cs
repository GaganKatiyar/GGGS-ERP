using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Reporting.Api.Contracts;
using Gggs.Reporting.Application.Headcount;
using Gggs.Reporting.Domain.Headcount;
using Gggs.Reporting.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Reporting.IntegrationTests;

[Collection(nameof(ReportingApiCollection))]
public sealed class HeadcountEndpointTests(ReportingApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();

    [Fact]
    public async Task BuildingTheSnapshot_CountsActiveEmployeesAndJoinersAndLeavers()
    {
        DateOnly snapshotDate = new(2026, 6, 15);

        ReportingDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            // Still employed, joined before the snapshot date: counted in headcount, not joiners.
            db.EmployeeRosterFacts.Add(EmployeeRosterFact.Create(
                Guid.NewGuid(), "EMP-001", "Active", LegalEntityId, BranchId, DepartmentId, new DateOnly(2025, 1, 1), version: 1));

            // Joined exactly on the snapshot date: counted in both headcount and joiners.
            EmployeeRosterFact joiner = EmployeeRosterFact.Create(
                Guid.NewGuid(), "EMP-002", "Active", LegalEntityId, BranchId, DepartmentId, snapshotDate, version: 1);
            db.EmployeeRosterFacts.Add(joiner);

            // Exited exactly on the snapshot date: counted in leavers, not headcount (ExitDate <= snapshotDate excludes them).
            EmployeeRosterFact leaver = EmployeeRosterFact.Create(
                Guid.NewGuid(), "EMP-003", "Active", LegalEntityId, BranchId, DepartmentId, new DateOnly(2024, 1, 1), version: 1);
            leaver.MarkExited(snapshotDate, version: 2);
            db.EmployeeRosterFacts.Add(leaver);

            // Hasn't joined yet as of the snapshot date: excluded entirely.
            db.EmployeeRosterFacts.Add(EmployeeRosterFact.Create(
                Guid.NewGuid(), "EMP-004", "Active", LegalEntityId, BranchId, DepartmentId, snapshotDate.AddDays(1), version: 1));

            await db.SaveChangesAsync();
        }

        using IServiceScope buildScope = factory.Services.CreateScope();
        var tenantContext = buildScope.ServiceProvider.GetRequiredService<Gggs.BuildingBlocks.MultiTenancy.TenantContext>();
        tenantContext.SetTenant(ReportingApiFactory.SeededTenantId);
        HeadcountSnapshotBuilder builder = buildScope.ServiceProvider.GetRequiredService<HeadcountSnapshotBuilder>();
        await builder.BuildForTenantAsync(ReportingApiFactory.SeededTenantId, snapshotDate);

        HttpClient client = factory.CreateClientFor(Permissions.Report.View);
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/reporting/headcount-snapshots?fromDate={snapshotDate:yyyy-MM-dd}&toDate={snapshotDate:yyyy-MM-dd}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        IReadOnlyList<HeadcountDailySnapshotResponse>? rows = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<HeadcountDailySnapshotResponse>>(JsonOptions);

        rows.ShouldNotBeNull();
        HeadcountDailySnapshotResponse row = rows.ShouldHaveSingleItem();
        row.Headcount.ShouldBe(2);
        row.Joiners.ShouldBe(1);
        row.Leavers.ShouldBe(1);
    }

    [Fact]
    public async Task ListingSnapshots_WithoutThePermission_IsForbidden()
    {
        HttpClient client = factory.CreateClientFor();

        HttpResponseMessage response = await client.GetAsync("/api/v1/reporting/headcount-snapshots");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        JsonConfiguration.Apply(options);
        return options;
    }
}
