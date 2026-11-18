using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.Data;
using Gggs.Modules.Notification.Application;
using Gggs.Modules.Notification.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// Keeping the Email log small and acting on failures: the keep periods an admin sets, the nightly clean-up
/// that deletes for real, temporary failures tried again, the in-app notice to mail admins, and Resend all.
/// Every email here is in a module of its own, so nothing another test wrote is touched.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class EmailLogUpkeepTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task KeepPeriods_AreSetByAnAdmin_FromTheOfferedChoices()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        try
        {
            JsonElement current = await OkAsync(await admin.GetAsync("/api/v1/notifications/emails/settings", Ct));
            current.GetProperty("keepSentDays").GetInt32().ShouldBe(30);
            current.GetProperty("keepProblemDays").GetInt32().ShouldBe(30);
            current.GetProperty("alertOnFailure").GetBoolean().ShouldBeTrue();

            JsonElement saved = await OkAsync(await admin.PutAsJsonAsync("/api/v1/notifications/emails/settings", new { KeepSentDays = 0, KeepProblemDays = 60, AlertOnFailure = false }, Ct));
            saved.GetProperty("keepSentDays").GetInt32().ShouldBe(0);
            saved.GetProperty("keepProblemDays").GetInt32().ShouldBe(60);
            saved.GetProperty("alertOnFailure").GetBoolean().ShouldBeFalse();

            (await CoreHrApi.ErrorCodeAsync(await admin.PutAsJsonAsync("/api/v1/notifications/emails/settings", new { KeepSentDays = 7, KeepProblemDays = 90, AlertOnFailure = true }, Ct))).ShouldBe("EMAIL_LOG_KEEP");
        }
        finally
        {
            await OkAsync(await admin.PutAsJsonAsync("/api/v1/notifications/emails/settings", new { KeepSentDays = 30, KeepProblemDays = 30, AlertOnFailure = true }, Ct));
        }
    }

    [Fact]
    public async Task TheCleanUp_DeletesOnlyWhatIsPastItsKeepPeriod()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string module = Module();
        DateTime now = DateTime.UtcNow;
        Guid oldSent = Guid.Empty, recentSent = Guid.Empty, oldFailed = Guid.Empty, retrying = Guid.Empty, queued = Guid.Empty;

        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            AppDbContext database = provider.GetRequiredService<AppDbContext>();
            oldSent = Add(database, module, email => email.MarkSent(now.AddDays(-40)));
            recentSent = Add(database, module, email => email.MarkSent(now.AddDays(-1)));
            oldFailed = Add(database, module, email => email.MarkFailed("535 authentication failed", now: now));
            retrying = Add(database, module, email => email.MarkFailed("The operation has timed out.", now: now));
            queued = Add(database, module, _ => { });
            await database.SaveChangesAsync(Ct);

            // Made old, as a year of use would leave them.
            await database.Set<EmailMessage>().Where(email => email.ModuleKey == module).ExecuteUpdateAsync(set => set.SetProperty(email => email.CreatedDate, now.AddDays(-100)), Ct);
        });

        await OkAsync(await admin.PutAsJsonAsync("/api/v1/notifications/emails/settings", new { KeepSentDays = 30, KeepProblemDays = 90, AlertOnFailure = true }, Ct));
        try
        {
            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
            {
                await provider.GetRequiredService<EmailUpkeep>().CleanUpAsync(Ct);

                // Gone for real, not soft-deleted: the space is what the clean-up is for.
                List<Guid> left = await provider.GetRequiredService<AppDbContext>().Set<EmailMessage>()
                    .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
                    .Where(email => email.ModuleKey == module)
                    .Select(email => email.Id)
                    .ToListAsync(Ct);
                left.ShouldNotContain(oldSent, "sent 40 days ago, kept 30");
                left.ShouldContain(recentSent, "sent yesterday");
                left.ShouldNotContain(oldFailed, "failed 100 days ago, kept 90");
                left.ShouldContain(retrying, "a retry is still due");
                left.ShouldContain(queued, "still on its way");
            });
        }
        finally
        {
            await OkAsync(await admin.PutAsJsonAsync("/api/v1/notifications/emails/settings", new { KeepSentDays = 30, KeepProblemDays = 30, AlertOnFailure = true }, Ct));
        }
    }

    [Fact]
    public async Task ATemporaryFailure_IsTriedAgainWhenItsWaitIsOver()
    {
        string module = Module();
        Guid id = Guid.Empty;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            AppDbContext database = provider.GetRequiredService<AppDbContext>();
            id = Add(database, module, email => email.MarkFailed("The operation has timed out.", now: DateTime.UtcNow.AddMinutes(-10)));
            await database.SaveChangesAsync(Ct);
            (await provider.GetRequiredService<EmailUpkeep>().RetryDueAsync(Ct)).ShouldBeGreaterThanOrEqualTo(1);
        });
        await factory.DeliverEventsAsync();

        EmailMessage email = await EmailAsync(id);
        email.Status.ShouldNotBe(EmailStatus.Failed, "it went back in the queue and was delivered the system's way");
        email.NextAttemptAt.ShouldBeNull();
        email.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task MailAdmins_AreToldInTheApp_AtMostOnceAnHour()
    {
        string module = Module();
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            AppDbContext database = provider.GetRequiredService<AppDbContext>();

            // Start from a clean slate: as if the last notice went over an hour ago.
            if (await database.Set<EmailLogSetting>().FirstOrDefaultAsync(Ct) is { } setting)
            {
                setting.MarkAlerted(DateTime.UtcNow.AddHours(-2));
                await database.SaveChangesAsync(Ct);
            }

            Add(database, module, email => email.MarkFailed("535 5.7.8 Username and Password not accepted", now: DateTime.UtcNow));
            await database.SaveChangesAsync(Ct);
        });

        int told = 0, again = -1;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider => told = await provider.GetRequiredService<EmailUpkeep>().AlertAsync(Ct));
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider => again = await provider.GetRequiredService<EmailUpkeep>().AlertAsync(Ct));

        told.ShouldBeGreaterThanOrEqualTo(1, "the seeded admin can manage mail");
        again.ShouldBe(0, "not twice within the hour");

        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            InAppNotification notice = await provider.GetRequiredService<AppDbContext>().Set<InAppNotification>().AsNoTracking()
                .Where(row => row.EventCode == EmailUpkeep.AlertEventCode)
                .OrderByDescending(row => row.CreatedDate)
                .FirstAsync(Ct);
            notice.Title.ShouldContain("could not be sent");
            notice.Link.ShouldBe("/studio/email-log?status=Failed");
        });
    }

    [Fact]
    public async Task TheLog_SaysWhyInPlainWords_AndResendAllPutsFailuresBackInTheQueue()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        string module = Module();
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
        {
            AppDbContext database = provider.GetRequiredService<AppDbContext>();
            Add(database, module, email => email.MarkFailed("Mailbox unavailable. The server response was: 5.1.1 User unknown", now: DateTime.UtcNow));
            Add(database, module, email => email.MarkFailed("Failure sending mail.", now: DateTime.UtcNow));
            await database.SaveChangesAsync(Ct);
        });

        JsonElement log = await OkAsync(await admin.GetAsync($"/api/v1/notifications/emails?status=Failed&module={module}", Ct));
        JsonElement[] rows = [.. log.GetProperty("page").GetProperty("items").EnumerateArray()];
        rows.Length.ShouldBe(2);
        JsonElement address = rows.Single(row => row.GetProperty("failureKind").GetString() == "Address");
        address.GetProperty("problem").GetString()!.ShouldContain("address");
        address.GetProperty("fix").GetString()!.ShouldContain("Correct");
        JsonElement busy = rows.Single(row => row.GetProperty("failureKind").GetString() == "Temporary");
        busy.GetProperty("nextAttemptAt").ValueKind.ShouldBe(JsonValueKind.String);

        JsonElement resent = await OkAsync(await admin.PostAsync($"/api/v1/notifications/emails/resend-failed?module={module}", null, Ct));
        resent.GetProperty("requeued").GetInt32().ShouldBe(2);

        JsonElement after = await OkAsync(await admin.GetAsync($"/api/v1/notifications/emails?status=Failed&module={module}", Ct));
        after.GetProperty("page").GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    private static string Module() => $"t{Guid.NewGuid():N}"[..16];

    private static Guid Add(AppDbContext database, string module, Action<EmailMessage> then)
    {
        var email = EmailMessage.Queue($"{Guid.NewGuid():N}@upkeep.example", "Upkeep test", "Hello.", "onboarding.welcome", module);
        then(email);
        database.Set<EmailMessage>().Add(email);
        return email.Id;
    }

    private async Task<EmailMessage> EmailAsync(Guid id)
    {
        EmailMessage? email = null;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
            email = await provider.GetRequiredService<AppDbContext>().Set<EmailMessage>().AsNoTracking().SingleAsync(row => row.Id == id, Ct));
        return email!;
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
