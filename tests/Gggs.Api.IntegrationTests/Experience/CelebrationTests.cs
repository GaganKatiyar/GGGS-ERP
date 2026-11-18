using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Notification.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// HR Portal greetings: birthdays and work anniversaries are listed, wished by hand once per occasion
/// with an optional personal note, and both the employee and their manager hear about it.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CelebrationTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_birthday_is_wished_once_with_a_note_to_the_employee_and_their_manager()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid person = await CoreHrApi.HireAsync(admin, department, manager);
        DateOnly today = await TodayAsync(admin);

        // 28 years back keeps a 29 February birthday on 29 February.
        await SetDatesAsync(person, dateOfBirth: today.AddYears(-28), joiningDate: null);

        JsonElement row = await RowAsync(admin, "Birthday", today, person);
        row.GetProperty("canSend").GetBoolean().ShouldBeTrue();
        row.TryGetProperty("wishedAt", out _).ShouldBeFalse("nobody has wished them yet, and nulls are left out");
        row.GetProperty("managerName").GetString().ShouldNotBeNullOrWhiteSpace();
        row.GetProperty("years").GetInt32().ShouldBe(0, "a birthday never gives away anyone's age");

        const string Note = "Cake in the pantry at four!";
        JsonElement sent = await OkAsync(await admin.PostAsJsonAsync("/api/v1/engagement/celebrations/send", new
        {
            Kind = "Birthday",
            Items = new[] { new { EmployeeId = person, Date = Iso(today) } },
            Note,
        }, Ct));
        sent.GetProperty("sent").GetInt32().ShouldBe(1);

        JsonElement wished = await RowAsync(admin, "Birthday", today, person);
        wished.GetProperty("canSend").GetBoolean().ShouldBeFalse();
        wished.GetProperty("wishedAt").ValueKind.ShouldBe(JsonValueKind.String);
        wished.GetProperty("wishedBy").GetString().ShouldNotBeNullOrWhiteSpace();
        wished.GetProperty("note").GetString().ShouldBe(Note);

        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            AppDbContext db = provider.GetRequiredService<AppDbContext>();
            InAppNotification wish = await db.Set<InAppNotification>().SingleAsync(row => row.RecipientEmployeeId == person, Ct);
            wish.Title.ShouldContain("Happy birthday");
            wish.Body.ShouldContain(Note);
            InAppNotification heads = await db.Set<InAppNotification>().SingleAsync(row => row.RecipientEmployeeId == manager, Ct);
            heads.Title.ShouldContain("birthday");
            heads.Body.ShouldNotContain(Note, Case.Insensitive, "the personal note is for the employee");
        });

        // A second send for the same day is skipped, with a reason, and nobody is notified twice.
        JsonElement again = await OkAsync(await admin.PostAsJsonAsync("/api/v1/engagement/celebrations/send", new
        {
            Kind = "Birthday",
            Items = new[] { new { EmployeeId = person, Date = Iso(today) } },
        }, Ct));
        again.GetProperty("sent").GetInt32().ShouldBe(0);
        again.GetProperty("skipped").EnumerateArray().ShouldHaveSingleItem().GetProperty("reason").GetString()!.ShouldContain("already wished");

        // A day that is not their birthday is refused rather than wished.
        JsonElement wrong = await OkAsync(await admin.PostAsJsonAsync("/api/v1/engagement/celebrations/send", new
        {
            Kind = "Birthday",
            Items = new[] { new { EmployeeId = person, Date = Iso(today.AddDays(1)) } },
        }, Ct));
        wrong.GetProperty("sent").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_work_anniversary_counts_the_years_and_a_future_one_cannot_be_wished_early()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid manager = await CoreHrApi.HireAsync(admin, department);
        Guid person = await CoreHrApi.HireAsync(admin, department, manager);
        Guid upcoming = await CoreHrApi.HireAsync(admin, department, manager);
        DateOnly today = await TodayAsync(admin);

        await SetDatesAsync(person, dateOfBirth: null, joiningDate: today.AddYears(-4));
        await SetDatesAsync(upcoming, dateOfBirth: null, joiningDate: today.AddDays(3).AddYears(-2));

        JsonElement row = await RowAsync(admin, "WorkAnniversary", today, person);
        row.GetProperty("years").GetInt32().ShouldBe(4);

        JsonElement[] week = await ListAsync(admin, "WorkAnniversary", today, today.AddDays(6));
        JsonElement soon = week.Single(item => item.GetProperty("employeeId").GetGuid() == upcoming);
        soon.GetProperty("years").GetInt32().ShouldBe(2);
        soon.GetProperty("canSend").GetBoolean().ShouldBeFalse("a wish never goes out before the day");

        JsonElement sent = await OkAsync(await admin.PostAsJsonAsync("/api/v1/engagement/celebrations/send", new
        {
            Kind = "WorkAnniversary",
            Items = new[] { new { EmployeeId = person, Date = Iso(today) }, new { EmployeeId = upcoming, Date = Iso(today.AddDays(3)) } },
        }, Ct));
        sent.GetProperty("sent").GetInt32().ShouldBe(1);
        sent.GetProperty("skipped").EnumerateArray().ShouldHaveSingleItem().GetProperty("employeeId").GetGuid().ShouldBe(upcoming);

        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            AppDbContext db = provider.GetRequiredService<AppDbContext>();
            (await db.Set<InAppNotification>().SingleAsync(row => row.RecipientEmployeeId == person, Ct)).Title.ShouldContain("4-year");
            (await db.Set<InAppNotification>().CountAsync(row => row.RecipientEmployeeId == manager, Ct)).ShouldBe(1);
            (await db.Set<InAppNotification>().AnyAsync(row => row.RecipientEmployeeId == upcoming, Ct)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Greetings_are_for_those_who_manage_engagement_only()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        using HttpClient self = await factory.CreateScopedUserClientAsync(Email("greet"), person, (Permissions.Self.ProfileView, DataScope.Own));

        (await self.GetAsync("/api/v1/engagement/celebrations?kind=Birthday", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await self.PostAsJsonAsync("/api/v1/engagement/celebrations/send", new { Kind = "Birthday", Items = new[] { new { EmployeeId = person, Date = Iso(DateOnly.FromDateTime(DateTime.UtcNow)) } } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<DateOnly> TodayAsync(HttpClient admin)
        => DateOnly.Parse((await OkAsync(await admin.GetAsync("/api/v1/engagement/celebrations?kind=Birthday", Ct))).GetProperty("today").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement[]> ListAsync(HttpClient admin, string kind, DateOnly from, DateOnly to)
        => [.. (await OkAsync(await admin.GetAsync($"/api/v1/engagement/celebrations?kind={kind}&from={Iso(from)}&to={Iso(to)}", Ct))).GetProperty("rows").EnumerateArray()];

    private static async Task<JsonElement> RowAsync(HttpClient admin, string kind, DateOnly day, Guid employee)
        => (await ListAsync(admin, kind, day, day)).Single(row => row.GetProperty("employeeId").GetGuid() == employee);

    private Task SetDatesAsync(Guid employee, DateOnly? dateOfBirth, DateOnly? joiningDate)
        => factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            AppDbContext db = provider.GetRequiredService<AppDbContext>();
            if (dateOfBirth is { } born)
            {
                await db.Database.ExecuteSqlAsync($"UPDATE [emp].[Employee] SET [DateOfBirth] = {born} WHERE [Id] = {employee}", Ct);
            }

            if (joiningDate is { } joined)
            {
                await db.Database.ExecuteSqlAsync($"UPDATE [emp].[Employee] SET [JoiningDate] = {joined} WHERE [Id] = {employee}", Ct);
            }
        });
}
