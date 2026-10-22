using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>Completing a settlement that was sent to payroll: its off-cycle run approved, finalized and paid.</summary>
internal static class SettlementPayroll
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Pays the case's settlement through its payroll run and returns the settlement as the case now shows it.</summary>
    public static async Task<JsonElement> PayAsync(PeopleOsApiFactory factory, HttpClient admin, Guid caseId, string reference)
    {
        JsonElement view = await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct));
        Guid runId = view.GetProperty("settlement").GetProperty("payrollRunId").GetGuid();
        (await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/submit", null, Ct))).GetProperty("status").GetString().ShouldBe("Approved");
        await OkAsync(await admin.PostAsync($"/api/v1/payroll/runs/{runId}/finalize", null, Ct));
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/runs/{runId}/paid", new { PaidOn = Iso(Today), Reference = reference }, Ct));
        await factory.DeliverEventsAsync();
        return (await OkAsync(await admin.GetAsync($"/api/v1/separation/cases/{caseId}", Ct))).GetProperty("settlement");
    }
}
