using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Insight.Domain;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Studio.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>Accounts follow people in and out through a directory that records instead of creating anything real.</summary>
[Collection(nameof(ApiCollection))]
public sealed class AccountTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NothingHappens_UntilSomeoneSwitchesItOn()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin);
        int before = factory.Recorder.Accounts.Count;

        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        await factory.DeliverEventsAsync();

        factory.Recorder.Accounts.Count.ShouldBe(before);
        (await AccountsAsync(admin, person)).ShouldBeEmpty();
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/account-connections", Ct))!
            .Single(row => row.GetProperty("connectionId").GetGuid() == connection).GetProperty("provisionOnHire").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task AHire_GetsAnAccount_AndThePasswordIsNeverKept()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin);
        Guid email = await OkIdAsync(admin, RecordingEmailAdapter.AdapterKey);
        await OkAsync(await admin.PutAsJsonAsync("/api/v1/studio/channels/email", new { ConnectionId = email }, Ct));
        await SwitchAsync(admin, connection, onHire: true, onExit: true);
        try
        {
            string personal = $"{Guid.NewGuid():N}@personal.example";
            Guid person = await HireAsync(admin, personal);
            await factory.DeliverEventsAsync();

            JsonElement account = (await AccountsAsync(admin, person)).ShouldHaveSingleItem();
            account.GetProperty("status").GetString().ShouldBe("Active");
            string login = account.GetProperty("login").GetString()!;
            login.ShouldEndWith("@directory.example");
            account.GetProperty("lastMessage").GetString()!.ShouldContain("personal email");

            // What the directory was given, and where the password went.
            RecordedAccount made = factory.Recorder.Accounts.Single(row => row.Login == login);
            made.Password.Length.ShouldBe(16);
            IReadOnlyDictionary<string, string> sent = factory.Recorder.Sent.Single(message => message.GetValueOrDefault("to") == personal);
            sent["body"].ShouldContain(made.Password);
            sent["body"].ShouldContain(login);

            // The new address becomes their work email, as nothing was on record.
            JsonElement profile = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{person}", Ct);
            (profile.TryGetProperty("profile", out JsonElement inner) ? inner : profile).GetProperty("officialEmail").GetString().ShouldBe(login);

            // Nothing kept anywhere holds the password: not the email record, not the run log, not the account.
            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async provider =>
            {
                AppDbContext database = provider.GetRequiredService<AppDbContext>();
                EmailMessage record = await database.Set<EmailMessage>().AsNoTracking().SingleAsync(row => row.ToAddress == personal, Ct);
                record.Status.ShouldBe(EmailStatus.Sent);
                record.Body.ShouldNotContain(made.Password);
                record.Body.ShouldContain("••••");
                record.Body.ShouldContain(login);
                (await database.Set<IntegrationRun>().AsNoTracking().Where(row => row.ConnectionId == connection).ToListAsync(Ct)).ShouldAllBe(row => !row.Message.Contains(made.Password, StringComparison.Ordinal));
            });
            account.GetRawText().ShouldNotContain(made.Password);

            // A hire delivered twice still means one account: the same person cannot get a second.
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/studio/accounts", new { EmployeeId = person, ConnectionId = connection }, Ct))).ShouldBe("ACCOUNT_EXISTS");

            // They leave: the account is disabled, and only disabled.
            (await admin.PostAsJsonAsync($"/api/v1/employees/{person}/exit", new { ExitDate = "2025-01-31", ExitReasonCode = "RESIGNATION" }, Ct)).EnsureSuccessStatusCode();
            await factory.DeliverEventsAsync();
            (await AccountsAsync(admin, person)).Single().GetProperty("status").GetString().ShouldBe("Disabled");
            factory.Recorder.Disabled.ShouldContain(made.ExternalId);
        }
        finally
        {
            await SwitchAsync(admin, connection, onHire: false, onExit: false);
            await OkAsync(await admin.PutAsJsonAsync("/api/v1/studio/channels/email", new { ConnectionId = (Guid?)null }, Ct));
        }
    }

    [Fact]
    public async Task NoPersonalEmail_StillMakesTheAccount_AndSaysWhatToDo()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin);
        await SwitchAsync(admin, connection, onHire: true, onExit: false);
        try
        {
            Guid person = await HireAsync(admin, personalEmail: null);
            await factory.DeliverEventsAsync();

            JsonElement account = (await AccountsAsync(admin, person)).ShouldHaveSingleItem();
            account.GetProperty("status").GetString().ShouldBe("Active");
            account.GetProperty("lastMessage").GetString()!.ShouldContain("reset the password");
        }
        finally
        {
            await SwitchAsync(admin, connection, onHire: false, onExit: false);
        }
    }

    [Fact]
    public async Task ALeaver_WithDisablingSwitchedOff_KeepsTheirAccount()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin);
        await SwitchAsync(admin, connection, onHire: true, onExit: false);
        try
        {
            Guid person = await HireAsync(admin, personalEmail: null);
            await factory.DeliverEventsAsync();
            (await admin.PostAsJsonAsync($"/api/v1/employees/{person}/exit", new { ExitDate = "2025-01-31", ExitReasonCode = "RESIGNATION" }, Ct)).EnsureSuccessStatusCode();
            await factory.DeliverEventsAsync();

            (await AccountsAsync(admin, person)).Single().GetProperty("status").GetString().ShouldBe("Active");
        }
        finally
        {
            await SwitchAsync(admin, connection, onHire: false, onExit: false);
        }
    }

    [Fact]
    public async Task AFailure_IsRetried_ThenWaits_AndARetryByHandFinishesIt()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid connection, string name) = await NamedConnectionAsync(admin, fail: true);
        await SwitchAsync(admin, connection, onHire: true, onExit: true);
        try
        {
            Guid person = await HireAsync(admin, personalEmail: null);
            await factory.DeliverEventsAsync();

            JsonElement account = (await AccountsAsync(admin, person)).ShouldHaveSingleItem();
            account.GetProperty("status").GetString().ShouldBe("Creating");
            account.GetProperty("attempts").GetInt32().ShouldBe(1);
            account.GetProperty("needsAttention").GetBoolean().ShouldBeFalse();
            account.GetProperty("lastMessage").GetString()!.ShouldContain("told to fail");
            Guid id = account.GetProperty("id").GetGuid();

            await RetryPassAsync(TimeSpan.Zero);
            (await AccountsAsync(admin, person)).Single().GetProperty("attempts").GetInt32().ShouldBe(1, "not due yet");

            await RetryPassAsync(TimeSpan.FromMinutes(2));
            await RetryPassAsync(TimeSpan.FromMinutes(8));
            await RetryPassAsync(TimeSpan.FromMinutes(25));
            await RetryPassAsync(TimeSpan.FromMinutes(90));
            account = (await AccountsAsync(admin, person)).Single();
            account.GetProperty("attempts").GetInt32().ShouldBe(5);
            account.GetProperty("needsAttention").GetBoolean().ShouldBeTrue();

            // Mended, a retry by hand goes through on the worker's next pass.
            await OkAsync(await admin.PutAsJsonAsync($"/api/v1/integrations/connections/{connection}", new { AdapterKey = RecordingAccountAdapter.AdapterKey, Name = name, Settings = new Dictionary<string, string> { ["fail"] = "no" } }, Ct));
            await OkAsync(await admin.PostAsync($"/api/v1/studio/accounts/{id}/retry", null, Ct));
            await RetryPassAsync(TimeSpan.Zero);
            (await AccountsAsync(admin, person)).Single().GetProperty("status").GetString().ShouldBe("Active");
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync($"/api/v1/studio/accounts/{id}/retry", null, Ct))).ShouldBe("ACCOUNT_SETTLED");
        }
        finally
        {
            await SwitchAsync(admin, connection, onHire: false, onExit: false);
        }
    }

    [Fact]
    public async Task SomeoneAlreadyEmployed_CanBeGivenAnAccountByHand_AndHaveItDisabled()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin);
        Guid person = await HireAsync(admin, personalEmail: null);
        await factory.DeliverEventsAsync();

        JsonElement asked = await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/accounts", new { EmployeeId = person, ConnectionId = connection }, Ct));
        asked.GetProperty("status").GetString().ShouldBe("Creating");
        await RetryPassAsync(TimeSpan.Zero);
        JsonElement account = (await AccountsAsync(admin, person)).Single();
        account.GetProperty("status").GetString().ShouldBe("Active");

        await OkAsync(await admin.PostAsync($"/api/v1/studio/accounts/{account.GetProperty("id").GetGuid()}/disable", null, Ct));
        await RetryPassAsync(TimeSpan.Zero);
        (await AccountsAsync(admin, person)).Single().GetProperty("status").GetString().ShouldBe("Disabled");
    }

    [Fact]
    public async Task AConnectionSavedSwitchedOff_IsOff_AndMakesNoAccounts()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement saved = await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = RecordingAccountAdapter.AdapterKey,
            Name = $"Directory {Guid.NewGuid():N}"[..18],
            Settings = new Dictionary<string, string> { ["fail"] = "no" },
            IsActive = false,
        }, Ct));
        saved.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        Guid connection = saved.GetProperty("id").GetGuid();
        await SwitchAsync(admin, connection, onHire: true, onExit: true);
        try
        {
            int before = factory.Recorder.Accounts.Count;
            Guid person = await HireAsync(admin, personalEmail: null);
            await factory.DeliverEventsAsync();

            (await AccountsAsync(admin, person)).ShouldBeEmpty();
            factory.Recorder.Accounts.Count.ShouldBe(before);
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/studio/accounts", new { EmployeeId = person, ConnectionId = connection }, Ct))).ShouldBe("ACCOUNT_CONNECTION");
        }
        finally
        {
            await SwitchAsync(admin, connection, onHire: false, onExit: false);
        }
    }

    [Fact]
    public async Task OnlyADirectoryCanMakeAccounts_AndAnAutomationCannotUseOne()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid directory = await ConnectionAsync(admin);
        Guid sms = await OkIdAsync(admin, RecordingSmsAdapter.AdapterKey);
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));

        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/studio/accounts", new { EmployeeId = person, ConnectionId = sms }, Ct))).ShouldBe("ACCOUNT_CONNECTION");
        (await admin.PutAsJsonAsync($"/api/v1/studio/account-connections/{sms}", new { ProvisionOnHire = true, DisableOnExit = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/connections", Ct))!.ShouldNotContain(row => row.GetProperty("id").GetGuid() == directory);
    }

    [Fact]
    public async Task AnAccount_BelongsToItsTenant()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin);
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        JsonElement asked = await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/accounts", new { EmployeeId = person, ConnectionId = connection }, Ct));
        Guid id = asked.GetProperty("id").GetGuid();

        using HttpClient other = factory.CreateClientFor(PeopleOsApiFactory.OtherTenantId, Permissions.DataHub.IntegrationView, Permissions.DataHub.IntegrationManage);
        (await other.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/accounts", Ct))!.ShouldNotContain(row => row.GetProperty("id").GetGuid() == id);
        (await other.PostAsync($"/api/v1/studio/accounts/{id}/disable", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> HireAsync(HttpClient admin, string? personalEmail)
    {
        HttpResponseMessage hired = await admin.PostAsJsonAsync("/api/v1/employees", new
        {
            FirstName = "Test",
            LastName = CoreHrApi.Unique("Directory").Replace("-", string.Empty, StringComparison.Ordinal),
            PersonalEmail = personalEmail,
            JoiningDate = "2024-01-15",
            Assignment = new { DepartmentId = await CoreHrApi.DepartmentAsync(admin), EmploymentTypeCode = "FULL_TIME", WorkModeCode = "HYBRID", Designation = "Engineer" },
        }, Ct);
        string text = await hired.Content.ReadAsStringAsync(Ct);
        hired.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private Task RetryPassAsync(TimeSpan later) => factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, provider =>
        ActivatorUtilities.CreateInstance<AccountEngine>(provider, new LaterClock(later)).RetryDueAsync(Ct));

    private sealed class LaterClock(TimeSpan later) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + later;
    }

    private static async Task<JsonElement[]> AccountsAsync(HttpClient admin, Guid employee)
        => (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/studio/accounts?employeeId={employee}", Ct))!;

    private static async Task SwitchAsync(HttpClient admin, Guid connection, bool onHire, bool onExit)
        => await OkAsync(await admin.PutAsJsonAsync($"/api/v1/studio/account-connections/{connection}", new { ProvisionOnHire = onHire, DisableOnExit = onExit }, Ct));

    private static async Task<Guid> ConnectionAsync(HttpClient admin) => (await NamedConnectionAsync(admin, fail: false)).Id;

    private static async Task<(Guid Id, string Name)> NamedConnectionAsync(HttpClient admin, bool fail)
    {
        string name = $"Directory {Guid.NewGuid():N}"[..18];
        JsonElement saved = await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = RecordingAccountAdapter.AdapterKey,
            Name = name,
            Settings = new Dictionary<string, string> { ["fail"] = fail ? "yes" : "no" },
        }, Ct));
        return (saved.GetProperty("id").GetGuid(), name);
    }

    private static async Task<Guid> OkIdAsync(HttpClient admin, string adapter)
    {
        JsonElement saved = await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = adapter,
            Name = $"Channel {Guid.NewGuid():N}"[..16],
            Settings = new Dictionary<string, string> { ["fail"] = "no" },
        }, Ct));
        return saved.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
