using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.Modules.Contract.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;

namespace Gggs.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ClientContractTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AClient_KeepsExactlyOnePrimaryContact()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement created = await ClientAsync(admin);
        Guid clientId = created.GetProperty("client").GetProperty("id").GetGuid();
        created.GetProperty("client").GetProperty("primaryContactName").GetString().ShouldBe("Asha Rao");
        created.GetProperty("client").GetProperty("industryName").GetString().ShouldNotBeNullOrEmpty();

        JsonElement second = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/clients/{clientId}/contacts", new { Name = "Ravi Iyer", Email = "ravi@client.example", IsPrimary = true }, Ct));
        second.GetProperty("isPrimary").GetBoolean().ShouldBeTrue();

        JsonElement detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}", Ct);
        JsonElement[] contacts = [.. detail.GetProperty("contacts").EnumerateArray()];
        contacts.Count(contact => contact.GetProperty("isPrimary").GetBoolean()).ShouldBe(1);
        detail.GetProperty("client").GetProperty("primaryContactName").GetString().ShouldBe("Ravi Iyer");

        // Removing the primary hands the role to whoever remains.
        await OkAsync(await admin.DeleteAsync($"/api/v1/clients/{clientId}/contacts/{second.GetProperty("id").GetGuid()}", Ct), HttpStatusCode.NoContent);
        detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}", Ct);
        detail.GetProperty("client").GetProperty("primaryContactName").GetString().ShouldBe("Asha Rao");

        HttpResponseMessage duplicate = await admin.PostAsJsonAsync("/api/v1/clients", new { Code = created.GetProperty("client").GetProperty("code").GetString(), Name = "Again" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(duplicate)).ShouldBe("CLIENT_CODE_IN_USE");

        HttpResponseMessage badIndustry = await admin.PostAsJsonAsync("/api/v1/clients", new { Code = CoreHrApi.Unique("CL"), Name = "Odd", IndustryCode = "NOT_AN_INDUSTRY" }, Ct);
        (await CoreHrApi.ErrorCodeAsync(badIndustry)).ShouldBe("CLIENT_INDUSTRY_INVALID");

        JsonElement listed = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/clients/contacts?clientId={clientId}", Ct);
        listed.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task AContract_IsDraftedActivatedAmendedAndTerminated()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid clientId = (await ClientAsync(admin)).GetProperty("client").GetProperty("id").GetGuid();

        JsonElement msa = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/contracts", Contract(clientId), Ct), HttpStatusCode.Created)).GetProperty("contract");
        msa.GetProperty("number").GetString()!.ShouldStartWith("CTR-");
        msa.GetProperty("status").GetString().ShouldBe("Draft");
        Guid msaId = msa.GetProperty("id").GetGuid();

        JsonElement sow = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/contracts", Contract(clientId, "Sow", msaId, end: Today.AddDays(90)), Ct), HttpStatusCode.Created)).GetProperty("contract");
        sow.GetProperty("parentNumber").GetString().ShouldBe(msa.GetProperty("number").GetString());

        HttpResponseMessage nested = await admin.PostAsJsonAsync("/api/v1/contracts", Contract(clientId, "Sow", sow.GetProperty("id").GetGuid()), Ct);
        (await CoreHrApi.ErrorCodeAsync(nested)).ShouldBe("CONTRACT_PARENT");

        JsonElement detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/contracts/{msaId}", Ct);
        detail.GetProperty("children").GetArrayLength().ShouldBe(1);

        // No activation workflow defined for this tenant: activating takes effect at once.
        JsonElement activated = await OkAsync(await admin.PostAsync($"/api/v1/contracts/{msaId}/activate", null, Ct));
        activated.GetProperty("status").GetString().ShouldBe("Active");

        HttpResponseMessage currency = await admin.PutAsJsonAsync($"/api/v1/contracts/{msaId}", Contract(clientId, currency: "USD"), Ct);
        (await CoreHrApi.ErrorCodeAsync(currency)).ShouldBe("CONTRACT_SIGNED_TERMS");

        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/contracts/{msaId}/terminate", new { On = Iso(Today), Reason = "Client wound down the programme" }, Ct), HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/contracts/{msaId}", Ct)).GetProperty("contract").GetProperty("status").GetString().ShouldBe("Terminated");

        Guid inactive = (await ClientAsync(admin, status: "Inactive")).GetProperty("client").GetProperty("id").GetGuid();
        (await CoreHrApi.ErrorCodeAsync(await admin.PostAsJsonAsync("/api/v1/contracts", Contract(inactive), Ct))).ShouldBe("CONTRACT_CLIENT_INACTIVE");
    }

    [Fact]
    public async Task TheDailySweep_ExpiresEndedContracts_AndRemindsOwnersOnce()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        TestOrg org = await PlatformServicesApi.OrgAsync(admin);
        Guid clientId = (await ClientAsync(admin)).GetProperty("client").GetProperty("id").GetGuid();

        // Activated after its end date has passed: it lands straight in Expired.
        Guid ended = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/contracts", Contract(clientId, "PurchaseOrder", start: Today.AddDays(-60), end: Today.AddDays(-1)), Ct), HttpStatusCode.Created))
            .GetProperty("contract").GetProperty("id").GetGuid();
        (await OkAsync(await admin.PostAsync($"/api/v1/contracts/{ended}/activate", null, Ct))).GetProperty("status").GetString().ShouldBe("Expired");

        Guid renewing = (await OkAsync(await admin.PostAsJsonAsync("/api/v1/contracts", Contract(clientId, "Sla", end: Today.AddDays(10), ownerId: org.Manager), Ct), HttpStatusCode.Created))
            .GetProperty("contract").GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsync($"/api/v1/contracts/{renewing}/activate", null, Ct));

        (await admin.GetFromJsonAsync<JsonElement>("/api/v1/contracts?renewalWithinDays=30&pageSize=200", Ct)).GetProperty("items").EnumerateArray()
            .ShouldContain(row => row.GetProperty("id").GetGuid() == renewing);

        int first = 0;
        int second = 0;
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => first = (await services.GetRequiredService<ContractService>().SweepAsync(Ct)).Reminded);
        await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => second = (await services.GetRequiredService<ContractService>().SweepAsync(Ct)).Reminded);

        first.ShouldBeGreaterThanOrEqualTo(1);
        second.ShouldBe(0, "a reminder goes out once per renewal date");

        using HttpClient manager = await PlatformServicesApi.PersonAsync(factory, org.Manager);
        await factory.DeliverEventsAsync();
        JsonElement inbox = await manager.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct);
        inbox.GetProperty("items").EnumerateArray().ShouldContain(row => row.GetProperty("eventCode").GetString() == "contract.renewal-due");
    }
}
