using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Attendance.Domain;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>Helpers for the Time tests: people with self-service time permissions, dates and leave types.</summary>
internal static class TimeApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An employee using self-service: leave, attendance, regularization, swaps, and their own approvals.</summary>
    public static Task<HttpClient> EmployeeAsync(PeopleOsApiFactory factory, Guid employeeId)
        => factory.CreateScopedUserClientAsync(
            PlatformServicesApi.Email("time"),
            employeeId,
            (Permissions.Self.LeaveView, DataScope.Own),
            (Permissions.Self.LeaveApply, DataScope.Own),
            (Permissions.Self.AttendanceView, DataScope.Own),
            (Permissions.Self.AttendancePunch, DataScope.Own),
            (Permissions.Attendance.RegularizationCreate, DataScope.Own),
            (Permissions.Self.ShiftSwapRequest, DataScope.Own),
            (Permissions.Workflow.TaskView, DataScope.Own),
            (Permissions.Workflow.TaskAct, DataScope.Own));

    /// <summary>The first <paramref name="day"/> at least <paramref name="daysAhead"/> days from today.</summary>
    public static DateOnly Next(DayOfWeek day, int daysAhead = 7)
    {
        DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);
        while (date.DayOfWeek != day)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    /// <summary>The last <paramref name="day"/> at least <paramref name="daysBack"/> days ago.</summary>
    public static DateOnly Previous(DayOfWeek day, int daysBack = 3)
    {
        DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysBack);
        while (date.DayOfWeek != day)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static async Task<Guid> LeaveTypeAsync(HttpClient admin, string code)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/leave/types", Ct))!
            .First(row => row.GetProperty("code").GetString() == code)
            .GetProperty("id").GetGuid();

    public static async Task GrantAsync(HttpClient admin, Guid employeeId, Guid leaveTypeId, decimal days)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/leave/adjustments", new { EmployeeId = employeeId, LeaveTypeId = leaveTypeId, Days = days, Note = "Test grant" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    public static async Task<JsonElement> BalanceAsync(HttpClient employee, string code)
        => (await employee.GetFromJsonAsync<JsonElement[]>("/api/v1/me/leave/balances", Ct))!
            .First(row => row.GetProperty("code").GetString() == code);

    public static object Application(Guid typeId, DateOnly start, DateOnly end, bool startsSecondHalf = false, bool endsFirstHalf = false, Guid? employeeId = null)
        => new { LeaveTypeId = typeId, StartDate = Iso(start), EndDate = Iso(end), StartsSecondHalf = startsSecondHalf, EndsFirstHalf = endsFirstHalf, Reason = "Test", EmployeeId = employeeId };

    /// <summary>Writes punches straight into the store at tenant-local times, as a device would have.</summary>
    public static Task PunchAtAsync(PeopleOsApiFactory factory, Guid employeeId, params (DateOnly Date, TimeOnly Time)[] punches)
        => factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services =>
        {
            ITenantClock clock = services.GetRequiredService<ITenantClock>();
            AppDbContext database = services.GetRequiredService<AppDbContext>();
            foreach ((DateOnly date, TimeOnly time) in punches)
            {
                database.Set<AttendancePunch>().Add(AttendancePunch.Record(employeeId, await clock.ToUtcAsync(date, time, Ct), PunchSource.Device));
            }

            await database.SaveChangesAsync(Ct);
        });

    public static async Task<JsonElement> MyDayAsync(HttpClient employee, DateOnly date)
    {
        JsonElement[] days = (await employee.GetFromJsonAsync<JsonElement[]>($"/api/v1/me/attendance?from={Iso(date)}&to={Iso(date)}", Ct))!;
        days.Length.ShouldBe(1);
        return days[0];
    }
}
