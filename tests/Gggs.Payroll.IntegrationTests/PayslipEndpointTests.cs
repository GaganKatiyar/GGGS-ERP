using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Payroll.Api.Contracts;
using Gggs.Payroll.Domain.Payroll;
using Gggs.Payroll.Domain.Snapshots;
using Gggs.Payroll.Infrastructure;
using Shouldly;

namespace Gggs.Payroll.IntegrationTests;

/// <summary><c>/api/v1/payroll/payslips/me</c>, <c>/api/v1/payroll/payslips/{id}</c>,
/// <c>/api/v1/payroll/payslips/{id}/pdf</c> and <c>/api/v1/payroll/employees/{employeeId}/payslips</c>
/// over HTTP: finalizing a run publishes one payslip per employee it contains, readable (as JSON or
/// PDF) by that employee through <see cref="Permissions.Self.PayslipView"/> or by an admin holding
/// <see cref="Permissions.Payroll.RunView"/> - never by anyone else.</summary>
[Collection(nameof(PayrollApiCollection))]
public sealed class PayslipEndpointTests(PayrollApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AdminPermissions =
    [
        Permissions.Payroll.ComponentManage,
        Permissions.Payroll.StructureManage,
        Permissions.Payroll.EmployeeSalaryManage,
        Permissions.Payroll.PayGroupManage,
        Permissions.Payroll.RunView,
        Permissions.Payroll.RunProcess,
        Permissions.Payroll.RunFinalize,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task FinalizingARun_PublishesAPayslipForEachEmployeeInIt()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);

        (Guid runId, Guid employeeId) = await SetUpAndFinalizeARunAsync(adminClient);

        HttpResponseMessage adminListResponse = await adminClient.GetAsync(
            $"/api/v1/payroll/employees/{employeeId}/payslips", TestContext.Current.CancellationToken);
        adminListResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        List<PayslipListItem>? items = await adminListResponse.Content.ReadFromJsonAsync<List<PayslipListItem>>(JsonOptions, TestContext.Current.CancellationToken);
        items.ShouldNotBeNull();
        items.ShouldContain(x => x.PayrollRunId == runId);
    }

    [Fact]
    public async Task AnEmployee_CanReadTheirOwnPayslip_ButNotSomeoneElses()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        (Guid runId, Guid employeeId) = await SetUpAndFinalizeARunAsync(adminClient);

        HttpResponseMessage adminListResponse = await adminClient.GetAsync(
            $"/api/v1/payroll/employees/{employeeId}/payslips", TestContext.Current.CancellationToken);
        List<PayslipListItem>? items = await adminListResponse.Content.ReadFromJsonAsync<List<PayslipListItem>>(JsonOptions, TestContext.Current.CancellationToken);
        Guid payslipId = items!.Single(x => x.PayrollRunId == runId).Id;

        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.PayslipView);
        HttpResponseMessage ownResponse = await ownerClient.GetAsync($"/api/v1/payroll/payslips/{payslipId}", TestContext.Current.CancellationToken);
        ownResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        PayslipResponse? payslip = await ownResponse.Content.ReadFromJsonAsync<PayslipResponse>(JsonOptions, TestContext.Current.CancellationToken);
        payslip.ShouldNotBeNull();
        payslip.EmployeeId.ShouldBe(employeeId);
        payslip.Lines.ShouldNotBeEmpty();

        using HttpClient otherEmployeeClient = factory.CreateClientForEmployee(Guid.NewGuid(), Permissions.Self.PayslipView);
        HttpResponseMessage otherResponse = await otherEmployeeClient.GetAsync(
            $"/api/v1/payroll/payslips/{payslipId}", TestContext.Current.CancellationToken);
        otherResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnAdmin_CanReadAnyEmployeesPayslip_EvenWithoutAnEmployeeProfile()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        (Guid runId, Guid employeeId) = await SetUpAndFinalizeARunAsync(adminClient);

        HttpResponseMessage adminListResponse = await adminClient.GetAsync(
            $"/api/v1/payroll/employees/{employeeId}/payslips", TestContext.Current.CancellationToken);
        List<PayslipListItem>? items = await adminListResponse.Content.ReadFromJsonAsync<List<PayslipListItem>>(JsonOptions, TestContext.Current.CancellationToken);
        Guid payslipId = items!.Single(x => x.PayrollRunId == runId).Id;

        // The admin client here carries no X-Test-Employee header at all - platform/payroll staff
        // routinely have no linked employee profile, and RunView alone must still be enough.
        HttpResponseMessage response = await adminClient.GetAsync($"/api/v1/payroll/payslips/{payslipId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnEmployee_CanDownloadTheirOwnPayslipPdf_ButNotSomeoneElses()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        (Guid runId, Guid employeeId) = await SetUpAndFinalizeARunAsync(adminClient);

        HttpResponseMessage adminListResponse = await adminClient.GetAsync(
            $"/api/v1/payroll/employees/{employeeId}/payslips", TestContext.Current.CancellationToken);
        List<PayslipListItem>? items = await adminListResponse.Content.ReadFromJsonAsync<List<PayslipListItem>>(JsonOptions, TestContext.Current.CancellationToken);
        Guid payslipId = items!.Single(x => x.PayrollRunId == runId).Id;

        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.PayslipView);
        HttpResponseMessage ownResponse = await ownerClient.GetAsync($"/api/v1/payroll/payslips/{payslipId}/pdf", TestContext.Current.CancellationToken);
        ownResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        ownResponse.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");
        byte[] pdfBytes = await ownResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        pdfBytes.Length.ShouldBeGreaterThan(0);
        // "%PDF" magic bytes - confirms QuestPDF actually produced a PDF, not just an octet stream.
        System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 4).ShouldBe("%PDF");

        using HttpClient otherEmployeeClient = factory.CreateClientForEmployee(Guid.NewGuid(), Permissions.Self.PayslipView);
        HttpResponseMessage otherResponse = await otherEmployeeClient.GetAsync(
            $"/api/v1/payroll/payslips/{payslipId}/pdf", TestContext.Current.CancellationToken);
        otherResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListingMyPayslips_WithNoLinkedEmployeeProfile_ReturnsAnEmptyList()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Self.PayslipView);

        HttpResponseMessage response = await client.GetAsync("/api/v1/payroll/payslips/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        List<PayslipListItem>? items = await response.Content.ReadFromJsonAsync<List<PayslipListItem>>(JsonOptions, TestContext.Current.CancellationToken);
        items.ShouldNotBeNull();
        items.ShouldBeEmpty();
    }

    /// <summary>Creates a component/structure/salary/pay-group/period/run, calculates and finalizes
    /// it, and returns the run id plus the one employee it was calculated for.</summary>
    private async Task<(Guid RunId, Guid EmployeeId)> SetUpAndFinalizeARunAsync(HttpClient adminClient)
    {
        Guid basicId = await GetOrCreateBasicComponentAsync(adminClient);
        Guid structureId = await CreateStructureAsync(adminClient);
        await adminClient.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 20000m, 1),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();
        await adminClient.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 240000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        Guid payGroupId = await CreatePayGroupAsync(adminClient);
        Guid periodId = await CreatePayPeriodAsync(adminClient, payGroupId);
        Guid runId = await CreateRunAsync(adminClient, payGroupId, periodId);

        await adminClient.PostAsync($"/api/v1/payroll/runs/{runId}/calculate", content: null, TestContext.Current.CancellationToken);
        HttpResponseMessage finalizeResponse = await adminClient.PostAsync(
            $"/api/v1/payroll/runs/{runId}/finalize", content: null, TestContext.Current.CancellationToken);
        finalizeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (runId, employeeId);
    }

    private static async Task<Guid> CreatePayGroupAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/pay-groups",
            new CreatePayGroupBody(ShortCode(), "Monthly Payroll", PayFrequency.Monthly, "INR"),
            TestContext.Current.CancellationToken);

        PayGroupResponse? body = await response.Content.ReadFromJsonAsync<PayGroupResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> CreatePayPeriodAsync(HttpClient client, Guid payGroupId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/payroll/pay-groups/{payGroupId}/periods",
            new CreatePayPeriodBody(ShortCode(), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 1)),
            TestContext.Current.CancellationToken);

        PayPeriodResponse? body = await response.Content.ReadFromJsonAsync<PayPeriodResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> CreateRunAsync(HttpClient client, Guid payGroupId, Guid payPeriodId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/runs",
            new CreatePayrollRunBody(payGroupId, payPeriodId, ShortCode()),
            TestContext.Current.CancellationToken);

        PayrollRunResponse? body = await response.Content.ReadFromJsonAsync<PayrollRunResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> CreateStructureAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/structures",
            new CreateSalaryStructureBody(ShortCode(), "Payslip Test Structure"),
            TestContext.Current.CancellationToken);

        SalaryStructureResponse? body = await response.Content.ReadFromJsonAsync<SalaryStructureResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static string ShortCode() => $"C{Guid.NewGuid():N}"[..18];

    private static async Task<Guid> GetOrCreateBasicComponentAsync(HttpClient client)
    {
        HttpResponseMessage listResponse = await client.GetAsync("/api/v1/payroll/components", TestContext.Current.CancellationToken);
        List<SalaryComponentResponse>? existing = await listResponse.Content.ReadFromJsonAsync<List<SalaryComponentResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);

        SalaryComponentResponse? basic = existing?.FirstOrDefault(x => x.Code == "BASIC");
        if (basic is not null)
        {
            return basic.Id;
        }

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/components",
            new CreateSalaryComponentBody("BASIC", "Basic", SalaryComponentKind.Earning, SalaryComponentCalcType.Fixed, true, 1),
            TestContext.Current.CancellationToken);

        SalaryComponentResponse? body = await response.Content.ReadFromJsonAsync<SalaryComponentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private async Task<Guid> SeedEmployeeAsync()
    {
        Guid employeeId = Guid.NewGuid();

        using PayrollDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            db.EmployeeSnapshots.Add(EmployeeSnapshot.Create(
                employeeId, $"EMP-{Guid.NewGuid():N}"[..10], "Payslip Test Employee", "Confirmed",
                new DateOnly(2020, 1, 1), version: 1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return employeeId;
    }
}
