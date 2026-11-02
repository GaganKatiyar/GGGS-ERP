using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Studio.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Gggs.Api.IntegrationTests;

/// <summary>Studio automations: a business event, filtered by conditions, sends through a connection; failures retry and are logged.</summary>
[Collection(nameof(ApiCollection))]
public sealed class AutomationTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AHire_InTheChosenDepartment_PostsTheFilledInMessage_Once()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid department, string departmentName) = await DepartmentAsync(admin);
        Guid other = await CoreHrApi.DepartmentAsync(admin);
        Guid connection = await ConnectionAsync(admin, fail: false);
        string marker = Guid.NewGuid().ToString("N")[..8];

        JsonElement automation = await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/automations", Body("Welcome", departmentName, connection, $"{{{{employeeName}}}} joined {{{{department}}}} {marker}"), Ct));
        Guid automationId = automation.GetProperty("id").GetGuid();
        automation.GetProperty("triggerLabel").GetString().ShouldBe("Employee hired");
        automation.GetProperty("connectionName").GetString()!.ShouldStartWith("Recorder");

        await CoreHrApi.HireAsync(admin, other);
        await CoreHrApi.HireAsync(admin, department);
        await factory.DeliverEventsAsync();
        await factory.DeliverEventsAsync();

        string[] sent = [.. factory.Recorder.Sent.Select(message => message["text"]).Where(text => text.EndsWith(marker, StringComparison.Ordinal))];
        string text = sent.ShouldHaveSingleItem("only the hire in the chosen department matches, and only once");
        text.ShouldStartWith("Test Person-");
        text.ShouldContain($"joined {departmentName}");

        JsonElement[] deliveries = await DeliveriesAsync(admin, automationId);
        JsonElement delivery = deliveries.ShouldHaveSingleItem();
        delivery.GetProperty("status").GetString().ShouldBe("Succeeded");
        delivery.GetProperty("attempts").GetInt32().ShouldBe(1);
        delivery.GetProperty("automationName").GetString().ShouldBe("Welcome");
        delivery.GetProperty("createdDate").GetString()!.ShouldEndWith("Z", Case.Sensitive, "a time read back from the database still says it is UTC");
        delivery.GetProperty("parameters").GetProperty("text").GetString().ShouldBe(text);

        JsonElement listed = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/automations", Ct))!.Single(row => row.GetProperty("id").GetGuid() == automationId);
        listed.GetProperty("lastStatus").GetString().ShouldBe("Succeeded");

        // Switched off, it no longer acts.
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/studio/automations/{automationId}", Body("Welcome", departmentName, connection, "{{employeeName}} " + marker, isActive: false), Ct));
        await CoreHrApi.HireAsync(admin, department);
        await factory.DeliverEventsAsync();
        (await DeliveriesAsync(admin, automationId)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task AFailingSend_IsRetried_ThenAbandoned_AndCanBeRetriedByHand()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid department, string departmentName) = await DepartmentAsync(admin);
        Guid connection = await ConnectionAsync(admin, fail: true);
        JsonElement automation = await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/automations", Body("Failing", departmentName, connection, "{{employeeName}}"), Ct));
        Guid automationId = automation.GetProperty("id").GetGuid();

        await CoreHrApi.HireAsync(admin, department);
        await factory.DeliverEventsAsync();

        JsonElement delivery = (await DeliveriesAsync(admin, automationId)).ShouldHaveSingleItem();
        delivery.GetProperty("status").GetString().ShouldBe("Failed");
        delivery.GetProperty("lastMessage").GetString()!.ShouldContain("told to fail");
        delivery.GetProperty("nextAttemptAt").GetDateTime().ShouldBeGreaterThan(DateTime.UtcNow);
        Guid deliveryId = delivery.GetProperty("id").GetGuid();

        // Not due yet, nothing happens; a couple of minutes on, the worker's pass tries it again.
        await RetryPassAsync(TimeSpan.Zero);
        (await DeliveriesAsync(admin, automationId)).Single().GetProperty("attempts").GetInt32().ShouldBe(1);
        await RetryPassAsync(TimeSpan.FromMinutes(2));
        delivery = (await DeliveriesAsync(admin, automationId)).Single();
        delivery.GetProperty("attempts").GetInt32().ShouldBe(2);
        delivery.GetProperty("status").GetString().ShouldBe("Failed");

        for (int attempt = 3; attempt <= 5; attempt++)
        {
            delivery = await OkAsync(await admin.PostAsync($"/api/v1/studio/deliveries/{deliveryId}/retry", null, Ct));
            delivery.GetProperty("attempts").GetInt32().ShouldBe(attempt);
        }

        delivery.GetProperty("status").GetString().ShouldBe("Abandoned");
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/deliveries?status=Abandoned", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == deliveryId);

        // Mended, a retry by hand goes through; a delivered one cannot be sent again.
        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/integrations/connections/{connection}", new { AdapterKey = "test-recorder", Name = await ConnectionNameAsync(admin, connection), Settings = new Dictionary<string, string> { ["fail"] = "no" } }, Ct));
        delivery = await OkAsync(await admin.PostAsync($"/api/v1/studio/deliveries/{deliveryId}/retry", null, Ct));
        delivery.GetProperty("status").GetString().ShouldBe("Succeeded");
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync($"/api/v1/studio/deliveries/{deliveryId}/retry", null, Ct))).ShouldBe("DELIVERY_DONE");
    }

    [Fact]
    public async Task SwitchingOff_StopsWaitingRetries_ButARetryByHandStillSends()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (Guid department, string departmentName) = await DepartmentAsync(admin);
        Guid connection = await ConnectionAsync(admin, fail: true);
        Guid automationId = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/automations", Body("Paused", departmentName, connection, "{{employeeName}}"), Ct))).GetProperty("id").GetGuid();
        await CoreHrApi.HireAsync(admin, department);
        await factory.DeliverEventsAsync();

        await OkAsync(await admin.PutAsJsonAsync($"/api/v1/studio/automations/{automationId}", Body("Paused", departmentName, connection, "{{employeeName}}", isActive: false), Ct));
        await RetryPassAsync(TimeSpan.FromMinutes(2));

        JsonElement delivery = (await DeliveriesAsync(admin, automationId)).ShouldHaveSingleItem();
        delivery.GetProperty("status").GetString().ShouldBe("Abandoned");
        delivery.GetProperty("attempts").GetInt32().ShouldBe(1, "abandoned without another send");
        delivery.GetProperty("lastMessage").GetString()!.ShouldContain("switched off");

        delivery = await OkAsync(await admin.PostAsync($"/api/v1/studio/deliveries/{delivery.GetProperty("id").GetGuid()}/retry", null, Ct));
        delivery.GetProperty("attempts").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task TheBuilder_ChecksWhatItSaves_AndATestSendUsesSampleData()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin, fail: false);

        JsonElement[] triggers = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/triggers", Ct))!;
        triggers.Length.ShouldBe(16);
        triggers.Single(trigger => trigger.GetProperty("code").GetString() == "leave.approved").GetProperty("fields").EnumerateArray()
            .Select(field => field.GetProperty("key").GetString()).ShouldContain("leaveType");
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/connections", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == connection);

        // A file-producing connector has nothing to do unattended, so it is not offered.
        Guid bank = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new { AdapterKey = "bank-transfer", Name = $"Bank {Guid.NewGuid():N}"[..12], Settings = new Dictionary<string, string> { ["bankName"] = "HDFC Bank", ["debitAccount"] = "50100987654321", ["ifsc"] = "HDFC0001234" } }, Ct))).GetProperty("id").GetGuid();
        (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/connections", Ct))!.ShouldNotContain(row => row.GetProperty("id").GetGuid() == bank);
        (await ErrorAsync(admin, new { Name = "x", TriggerCode = "employee.hired", ConnectionId = bank, Operation = "salary-file", Parameters = new Dictionary<string, string> { ["period"] = "2026-10" } })).ShouldBe("AUTOMATION_CONNECTION");

        (await ErrorAsync(admin, new { Name = "x", TriggerCode = "tenant.provisioned", ConnectionId = connection, Operation = "post", Parameters = new Dictionary<string, string> { ["text"] = "hi" } })).ShouldBe("AUTOMATION_TRIGGER");
        (await ErrorAsync(admin, new { Name = "x", TriggerCode = "employee.hired", ConnectionId = Guid.NewGuid(), Operation = "post", Parameters = new Dictionary<string, string> { ["text"] = "hi" } })).ShouldBe("AUTOMATION_CONNECTION");
        (await ErrorAsync(admin, new { Name = "x", TriggerCode = "employee.hired", ConnectionId = connection, Operation = "delete-everything", Parameters = new Dictionary<string, string> { ["text"] = "hi" } })).ShouldBe("AUTOMATION_OPERATION");
        (await ErrorAsync(admin, new { Name = "x", TriggerCode = "employee.hired", ConnectionId = connection, Operation = "post", Parameters = new Dictionary<string, string>() })).ShouldBe("AUTOMATION_PARAMETERS");
        (await ErrorAsync(admin, new { Name = "x", TriggerCode = "employee.hired", Conditions = new[] { new { Field = "salary", Operator = "Equals", Value = "1" } }, ConnectionId = connection, Operation = "post", Parameters = new Dictionary<string, string> { ["text"] = "hi" } })).ShouldBe("AUTOMATION_CONDITIONS");

        string marker = Guid.NewGuid().ToString("N")[..8];
        JsonElement tested = await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/automations/test", Body("Test", null, connection, "{{employeeName}} joined {{department}} " + marker), Ct));
        tested.GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        tested.GetProperty("parameters").GetProperty("text").GetString().ShouldBe("Priya Sharma joined Engineering " + marker);
        factory.Recorder.Sent.ShouldContain(message => message["text"] == "Priya Sharma joined Engineering " + marker);
    }

    [Fact]
    public async Task AnAutomation_BelongsToItsTenant()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid connection = await ConnectionAsync(admin, fail: false);
        Guid automationId = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/studio/automations", Body("Ours", null, connection, "hi"), Ct))).GetProperty("id").GetGuid();

        using HttpClient other = factory.CreateClientFor(PeopleOsApiFactory.OtherTenantId, Permissions.DataHub.IntegrationView, Permissions.DataHub.IntegrationManage);
        (await other.GetFromJsonAsync<JsonElement[]>("/api/v1/studio/automations", Ct))!.ShouldNotContain(row => row.GetProperty("id").GetGuid() == automationId);
        (await other.DeleteAsync($"/api/v1/studio/automations/{automationId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OkAsync(await admin.DeleteAsync($"/api/v1/studio/automations/{automationId}", Ct))).GetBoolean().ShouldBeTrue();
    }

    /// <summary>The worker's retry pass for the test tenant, with its clock moved on.</summary>
    private Task RetryPassAsync(TimeSpan later) => factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, provider =>
        ActivatorUtilities.CreateInstance<AutomationEngine>(provider, new LaterClock(later)).RetryDueAsync(Ct));

    private sealed class LaterClock(TimeSpan later) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + later;
    }

    private static object Body(string name, string? departmentName, Guid connection, string text, bool isActive = true) => new
    {
        Name = name,
        TriggerCode = "employee.hired",
        Conditions = departmentName is null ? [] : new[] { new { Field = "department", Operator = "Equals", Value = departmentName } },
        ConnectionId = connection,
        Operation = "post",
        Parameters = new Dictionary<string, string> { ["text"] = text },
        IsActive = isActive,
    };

    private static async Task<(Guid Id, string Name)> DepartmentAsync(HttpClient admin)
    {
        string name = "Automation " + CoreHrApi.Unique("D");
        Guid id = await CoreHrApi.CreateAsync(admin, "/api/v1/organizations/departments", new { Code = CoreHrApi.Unique("DEPT"), Name = name });
        return (id, name);
    }

    private static async Task<Guid> ConnectionAsync(HttpClient admin, bool fail)
    {
        JsonElement saved = await OkAsync(await admin.PostAsJsonAsync("/api/v1/integrations/connections", new
        {
            AdapterKey = RecordingAdapter.AdapterKey,
            Name = $"Recorder {Guid.NewGuid():N}"[..17],
            Settings = new Dictionary<string, string> { ["fail"] = fail ? "yes" : "no" },
        }, Ct));
        return saved.GetProperty("id").GetGuid();
    }

    private static async Task<string> ConnectionNameAsync(HttpClient admin, Guid id)
        => (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/integrations/connections", Ct))!.Single(row => row.GetProperty("id").GetGuid() == id).GetProperty("name").GetString()!;

    private static async Task<JsonElement[]> DeliveriesAsync(HttpClient admin, Guid automationId)
        => (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/studio/deliveries?automationId={automationId}", Ct))!;

    private static async Task<string> ErrorAsync(HttpClient admin, object body)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/studio/automations", body, Ct);
        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        return await CoreHrApi.ErrorCodeAsync(response);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
