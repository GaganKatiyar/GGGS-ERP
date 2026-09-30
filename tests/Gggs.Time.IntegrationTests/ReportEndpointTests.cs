using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Time.Api.Endpoints;
using Gggs.Time.Domain.Attendance;
using Gggs.Time.Domain.Leave;
using Gggs.Time.Infrastructure;
using Shouldly;

namespace Gggs.Time.IntegrationTests;

/// <summary><c>/api/v1/reports/attendance-summary</c> and <c>/api/v1/reports/leave-summary</c> over HTTP.</summary>
[Collection(nameof(TimeApiCollection))]
public sealed class ReportEndpointTests(TimeApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task GetAttendanceSummary_CountsAPresentDayJustSeeded()
    {
        // A date no other test in this shared-database fixture touches (LeaveEndpointTests seeds
        // around "today" via FutureDate helpers - see its own remarks - never a fixed past date),
        // so this status breakdown cannot pick up a day some other test created.
        var date = new DateOnly(2031, 8, 4);

        using (TimeDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle))
        {
            using (scopeHandle)
            {
                AttendanceDay day = AttendanceDay.CreateEmpty(TimeApiFactory.CurrentEmployeeId, date);
                day.ApplyRegularization(
                    date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
                    date.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc));

                db.AttendanceDays.Add(day);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        using HttpClient client = factory.CreateClientFor(Permissions.Report.View);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/reports/attendance-summary?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        AttendanceSummaryReportResponse? report = await response.Content.ReadFromJsonAsync<AttendanceSummaryReportResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        report.ShouldNotBeNull();
        report.TotalDays.ShouldBe(1);
        report.RegularizedDays.ShouldBe(1);
        report.ByStatus.ShouldContain(x => x.Status == AttendanceDayStatus.Present && x.Days == 1);
    }

    [Fact]
    public async Task GetAttendanceSummary_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor(); // no permissions at all

        HttpResponseMessage response = await client.GetAsync(
            "/api/v1/reports/attendance-summary", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetLeaveSummary_SumsTakenAndPendingAcrossBalances()
    {
        // A period year no other test uses, and a leave type this test creates itself, so the sums
        // below are exactly this test's own rows - not the shared database's running totals.
        const int periodYear = 2031;
        Guid leaveTypeId;

        using (TimeDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle))
        {
            using (scopeHandle)
            {
                LeaveType leaveType = LeaveType.Create(
                    $"RPT-{Guid.NewGuid():N}"[..12], "Report Test Leave", isPaid: true, LeaveUnit.Day,
                    genderRestrictionCode: null, isCompOff: false, isLop: false, colorHex: "#123456");
                db.LeaveTypes.Add(leaveType);
                leaveTypeId = leaveType.Id;

                LeaveBalance balance = LeaveBalance.CreateEmpty(TimeApiFactory.CurrentEmployeeId, leaveTypeId, periodYear);
                LeaveLedgerEntry opening = LeaveLedgerEntry.Post(
                    TimeApiFactory.CurrentEmployeeId, leaveTypeId, periodYear, LeaveLedgerTransactionType.Opening, 10m,
                    new DateOnly(periodYear, 1, 1), sourceType: "Test", sourceId: Guid.NewGuid(), remarks: null);
                LeaveLedgerEntry debit = LeaveLedgerEntry.Post(
                    TimeApiFactory.CurrentEmployeeId, leaveTypeId, periodYear, LeaveLedgerTransactionType.Debit, -3m,
                    new DateOnly(periodYear, 6, 1), sourceType: "Test", sourceId: Guid.NewGuid(), remarks: null);

                balance.Recalculate([opening, debit], pendingApprovalDays: 2m);

                db.LeaveLedgerEntries.Add(opening);
                db.LeaveLedgerEntries.Add(debit);
                db.LeaveBalances.Add(balance);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        using HttpClient client = factory.CreateClientFor(Permissions.Report.View);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/reports/leave-summary?periodYear={periodYear}", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        LeaveSummaryReportResponse? report = await response.Content.ReadFromJsonAsync<LeaveSummaryReportResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        report.ShouldNotBeNull();
        report.PeriodYear.ShouldBe(periodYear);
        report.ByType.ShouldContain(x => x.LeaveTypeId == leaveTypeId && x.TakenDays == 3m && x.PendingDays == 2m);
    }
}
