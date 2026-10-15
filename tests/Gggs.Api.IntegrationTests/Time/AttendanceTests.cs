using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;
using static Gggs.Api.IntegrationTests.TimeApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class AttendanceTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADay_IsJudgedByItsFirstAndLastPunch_AgainstTheShift()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        DateOnly fullDay = Previous(DayOfWeek.Wednesday, daysBack: 7);
        DateOnly shortDay = fullDay.AddDays(1);

        await PunchAtAsync(factory, org.Requester, (fullDay, new TimeOnly(9, 5)), (fullDay, new TimeOnly(13, 0)), (fullDay, new TimeOnly(18, 10)));
        await PunchAtAsync(factory, org.Requester, (shortDay, new TimeOnly(10, 0)), (shortDay, new TimeOnly(15, 30)));

        using HttpClient employee = await EmployeeAsync(factory, org.Requester);

        JsonElement present = await MyDayAsync(employee, fullDay);
        present.GetProperty("status").GetString().ShouldBe("Present");
        present.GetProperty("lateMinutes").GetInt32().ShouldBe(0, "five minutes late is within the grace");
        present.GetProperty("workedMinutes").GetInt32().ShouldBe(485, "nine hours five minutes less the hour's break");
        present.GetProperty("firstIn").GetString().ShouldBe("09:05:00");
        present.GetProperty("punchCount").GetInt32().ShouldBe(3);

        JsonElement half = await MyDayAsync(employee, shortDay);
        half.GetProperty("status").GetString().ShouldBe("HalfDay");
        half.GetProperty("lateMinutes").GetInt32().ShouldBe(45);
        half.GetProperty("earlyLeaveMinutes").GetInt32().ShouldBe(150);

        JsonElement absent = await MyDayAsync(employee, fullDay.AddDays(-7));
        absent.GetProperty("status").GetString().ShouldBe("Absent");
        (await MyDayAsync(employee, Previous(DayOfWeek.Sunday))).GetProperty("status").GetString().ShouldBe("WeeklyOff");
    }

    [Fact]
    public async Task APunchFromTheWeb_IsRecorded_ButNotTwiceInAMinute()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        using HttpClient employee = await EmployeeAsync(factory, org.Requester);

        HttpResponseMessage punched = await employee.PostAsync("/api/v1/me/attendance/punch", null, Ct);
        punched.StatusCode.ShouldBe(HttpStatusCode.OK, await punched.Content.ReadAsStringAsync(Ct));
        (await punched.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("punches").GetArrayLength().ShouldBe(1);

        HttpResponseMessage again = await employee.PostAsync("/api/v1/me/attendance/punch", null, Ct);
        (await CoreHrApi.ErrorCodeAsync(again)).ShouldBe("ATTENDANCE_PUNCH_TOO_SOON");
    }

    [Fact]
    public async Task ARegularization_AddsTheMissingPunches_OnceApproved()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        using HttpClient employee = await EmployeeAsync(factory, org.Requester);
        DateOnly forgotten = Previous(DayOfWeek.Tuesday, daysBack: 5);

        (await MyDayAsync(employee, forgotten)).GetProperty("status").GetString().ShouldBe("Absent");

        HttpResponseMessage requested = await employee.PostAsJsonAsync("/api/v1/me/attendance/regularizations", new
        {
            Date = Iso(forgotten),
            CheckIn = "09:00",
            CheckOut = "18:00",
            Reason = "Card reader was down",
        }, Ct);
        requested.StatusCode.ShouldBe(HttpStatusCode.OK, await requested.Content.ReadAsStringAsync(Ct));
        (await requested.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("Approved", "no workflow is defined, so it applies at once");

        JsonElement day = await MyDayAsync(employee, forgotten);
        day.GetProperty("status").GetString().ShouldBe("Present");
        day.GetProperty("isRegularized").GetBoolean().ShouldBeTrue();

        HttpResponseMessage future = await employee.PostAsJsonAsync("/api/v1/me/attendance/regularizations", new
        {
            Date = Iso(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3)),
            CheckIn = "09:00",
            Reason = "x",
        }, Ct);
        (await CoreHrApi.ErrorCodeAsync(future)).ShouldBe("REGULARIZATION_DATE");
    }

    [Fact]
    public async Task ADeviceImport_MatchesPeopleByCode_AndIgnoresWhatItAlreadyHas()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);
        string code = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{org.Requester}", Ct)).GetProperty("employeeCode").GetString()!;
        string device = CoreHrApi.Unique("DEV");

        (await admin.PostAsJsonAsync("/api/v1/attendance/devices", new { Code = device, Name = "Front door" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        DateOnly day = Previous(DayOfWeek.Thursday, daysBack: 4);
        object batch = new
        {
            DeviceCode = device,
            Punches = new object[]
            {
                new { EmployeeCode = code, PunchedAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(5.5)) },
                new { EmployeeCode = code, PunchedAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(18, 30)), TimeSpan.FromHours(5.5)) },
                new { EmployeeCode = "NOBODY-1", PunchedAt = DateTimeOffset.UtcNow.AddDays(-4) },
            },
        };

        JsonElement first = await (await admin.PostAsJsonAsync("/api/v1/attendance/punches/import", batch, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        first.GetProperty("imported").GetInt32().ShouldBe(2);
        first.GetProperty("unknownEmployeeCodes").EnumerateArray().Select(value => value.GetString()).ShouldBe(["NOBODY-1"]);

        JsonElement resent = await (await admin.PostAsJsonAsync("/api/v1/attendance/punches/import", batch, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        resent.GetProperty("imported").GetInt32().ShouldBe(0);
        resent.GetProperty("duplicates").GetInt32().ShouldBe(2);

        JsonElement[] days = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/attendance/days?from={Iso(day)}&to={Iso(day)}&employeeId={org.Requester}", Ct))!;
        days.Single().GetProperty("status").GetString().ShouldBe("Present");
    }

    [Fact]
    public async Task ANightShift_SpansMidnight_AndSwapsTradeTheDay()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await OrgAsync(admin);

        // Weekends off, like the built-in standard day: whichever shift a tenant creates first becomes its default.
        await CoreHrApi.CreateAsync(admin, "/api/v1/shifts", new
        {
            Code = CoreHrApi.Unique("DAY"),
            Name = "Day shift",
            StartTime = "09:00",
            EndTime = "18:00",
            BreakMinutes = 60,
            GraceMinutes = 15,
            HalfDayMinutes = 240,
            FullDayMinutes = 480,
            WeeklyOff = new[] { "Saturday", "Sunday" },
        });
        Guid night = await CoreHrApi.CreateAsync(admin, "/api/v1/shifts", new
        {
            Code = CoreHrApi.Unique("NIGHT"),
            Name = "Night shift",
            StartTime = "22:00",
            EndTime = "07:00",
            BreakMinutes = 60,
            GraceMinutes = 15,
            HalfDayMinutes = 240,
            FullDayMinutes = 420,
            WeeklyOff = new[] { "Saturday", "Sunday" },
        });

        DateOnly from = Previous(DayOfWeek.Monday, daysBack: 14);
        HttpResponseMessage assigned = await admin.PostAsJsonAsync("/api/v1/shifts/assignments", new { EmployeeIds = new[] { org.Requester }, ShiftId = night, EffectiveFrom = Iso(from) }, Ct);
        assigned.StatusCode.ShouldBe(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync(Ct));

        DateOnly tuesday = from.AddDays(1);
        await PunchAtAsync(factory, org.Requester, (tuesday, new TimeOnly(22, 5)), (tuesday.AddDays(1), new TimeOnly(7, 0)));

        using HttpClient employee = await EmployeeAsync(factory, org.Requester);
        JsonElement day = await MyDayAsync(employee, tuesday);
        day.GetProperty("shiftName").GetString().ShouldBe("Night shift");
        day.GetProperty("status").GetString().ShouldBe("Present");
        day.GetProperty("workedMinutes").GetInt32().ShouldBe(475);

        DateOnly swapDay = Next(DayOfWeek.Wednesday, daysAhead: 7);
        HttpResponseMessage swapped = await employee.PostAsJsonAsync("/api/v1/me/shift-swaps", new { CounterpartEmployeeId = org.Finance, Date = Iso(swapDay), Reason = "Family event" }, Ct);
        swapped.StatusCode.ShouldBe(HttpStatusCode.OK, await swapped.Content.ReadAsStringAsync(Ct));
        (await swapped.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("Approved");

        JsonElement[] roster = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/shifts/roster?from={Iso(swapDay)}&to={Iso(swapDay)}&departmentId={org.Department}", Ct))!;
        string ShiftOf(Guid employeeId) => roster.Single(row => row.GetProperty("employeeId").GetGuid() == employeeId).GetProperty("days")[0].GetProperty("shiftName").GetString()!;
        ShiftOf(org.Requester).ShouldNotBe("Night shift");
        ShiftOf(org.Finance).ShouldBe("Night shift");
    }
}
