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

/// <summary><c>/api/v1/payroll/pay-groups*</c>, <c>/api/v1/payroll/pay-groups/{id}/periods*</c> and
/// <c>/api/v1/payroll/runs*</c> over HTTP: opening a period, starting a run, calculating it against
/// an employee's assigned salary, and finalizing it. <see cref="PayrollRunService.CalculateAsync"/>
/// snapshots every employee in the tenant with a current salary - since these tests share one
/// database across every <see cref="Fact"/> in this collection (the same reason
/// <c>PayrollEndpointTests</c>'s own <c>GetOrCreateBasicComponentAsync</c> exists), assertions here
/// are scoped to one employee's own <c>PayrollRunEmployee</c> row rather than the run's tenant-wide
/// totals, which earlier tests' leftover salary assignments would otherwise skew.</summary>
[Collection(nameof(PayrollApiCollection))]
public sealed class PayrollRunEndpointTests(PayrollApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AllPermissions =
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
    public async Task CreatingAPayGroup_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/pay-groups",
            new CreatePayGroupBody(ShortCode(), "Monthly Payroll", PayFrequency.Monthly, "INR"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StartingARun_AgainstAClosedPeriod_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid payGroupId = await CreatePayGroupAsync(client);
        Guid periodId = await CreatePayPeriodAsync(client, payGroupId);

        // Finalizing a run against this period is the only way v1 closes a period - run the whole
        // lifecycle once first so the period is Closed, then try to start a second run against it.
        Guid firstRunId = await CreateRunAsync(client, payGroupId, periodId);
        await client.PostAsync($"/api/v1/payroll/runs/{firstRunId}/calculate", content: null, TestContext.Current.CancellationToken);
        await client.PostAsync($"/api/v1/payroll/runs/{firstRunId}/finalize", content: null, TestContext.Current.CancellationToken);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/runs",
            new CreatePayrollRunBody(payGroupId, periodId, ShortCode()),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CalculatingARun_ComputesGrossAndNetForAnAssignedEmployee()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid pfId = await CreateComponentAsync(client, "PF", SalaryComponentKind.Deduction, SalaryComponentCalcType.Fixed);

        Guid structureId = await CreateStructureAsync(client);
        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 30000m, 1),
            TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(pfId, 1800m, 2),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();
        await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 360000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        Guid payGroupId = await CreatePayGroupAsync(client);
        Guid periodId = await CreatePayPeriodAsync(client, payGroupId);
        Guid runId = await CreateRunAsync(client, payGroupId, periodId);

        HttpResponseMessage calculateResponse = await client.PostAsync(
            $"/api/v1/payroll/runs/{runId}/calculate", content: null, TestContext.Current.CancellationToken);
        calculateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        PayrollRunResponse? run = await calculateResponse.Content.ReadFromJsonAsync<PayrollRunResponse>(JsonOptions, TestContext.Current.CancellationToken);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PayrollRunStatus.Calculated);
        run.CalculatedDate.ShouldNotBeNull();

        HttpResponseMessage employeesResponse = await client.GetAsync(
            $"/api/v1/payroll/runs/{runId}/employees", TestContext.Current.CancellationToken);
        List<PayrollRunEmployeeResponse>? runEmployees = await employeesResponse.Content.ReadFromJsonAsync<List<PayrollRunEmployeeResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);
        runEmployees.ShouldNotBeNull();

        PayrollRunEmployeeResponse result = runEmployees.Single(x => x.EmployeeId == employeeId);
        result.Gross.ShouldBe(30000m);
        result.NetPay.ShouldBe(28200m);
        result.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RecalculatingARun_ReplacesThePriorResultWholesale()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid structureId = await CreateStructureAsync(client);
        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 25000m, 1),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();
        await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 300000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        Guid payGroupId = await CreatePayGroupAsync(client);
        Guid periodId = await CreatePayPeriodAsync(client, payGroupId);
        Guid runId = await CreateRunAsync(client, payGroupId, periodId);

        await client.PostAsync($"/api/v1/payroll/runs/{runId}/calculate", content: null, TestContext.Current.CancellationToken);
        HttpResponseMessage secondCalculate = await client.PostAsync(
            $"/api/v1/payroll/runs/{runId}/calculate", content: null, TestContext.Current.CancellationToken);
        secondCalculate.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage employeesResponse = await client.GetAsync(
            $"/api/v1/payroll/runs/{runId}/employees", TestContext.Current.CancellationToken);
        List<PayrollRunEmployeeResponse>? runEmployees = await employeesResponse.Content.ReadFromJsonAsync<List<PayrollRunEmployeeResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);
        runEmployees.ShouldNotBeNull();

        // Exactly one result row for this employee - a second calculation must have replaced the
        // first, not appended to it.
        runEmployees.Count(x => x.EmployeeId == employeeId).ShouldBe(1);
    }

    [Fact]
    public async Task FinalizingARun_ThenRecalculatingIt_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid payGroupId = await CreatePayGroupAsync(client);
        Guid periodId = await CreatePayPeriodAsync(client, payGroupId);
        Guid runId = await CreateRunAsync(client, payGroupId, periodId);

        await client.PostAsync($"/api/v1/payroll/runs/{runId}/calculate", content: null, TestContext.Current.CancellationToken);

        HttpResponseMessage finalizeResponse = await client.PostAsync(
            $"/api/v1/payroll/runs/{runId}/finalize", content: null, TestContext.Current.CancellationToken);
        finalizeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        PayrollRunResponse? finalized = await finalizeResponse.Content.ReadFromJsonAsync<PayrollRunResponse>(JsonOptions, TestContext.Current.CancellationToken);
        finalized.ShouldNotBeNull();
        finalized.Status.ShouldBe(PayrollRunStatus.Finalized);
        finalized.FinalizedDate.ShouldNotBeNull();

        HttpResponseMessage recalculateResponse = await client.PostAsync(
            $"/api/v1/payroll/runs/{runId}/calculate", content: null, TestContext.Current.CancellationToken);

        recalculateResponse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task FinalizingARun_BeforeItHasBeenCalculated_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid payGroupId = await CreatePayGroupAsync(client);
        Guid periodId = await CreatePayPeriodAsync(client, payGroupId);
        Guid runId = await CreateRunAsync(client, payGroupId, periodId);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/payroll/runs/{runId}/finalize", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
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
            new CreateSalaryStructureBody(ShortCode(), "Run Test Structure"),
            TestContext.Current.CancellationToken);

        SalaryStructureResponse? body = await response.Content.ReadFromJsonAsync<SalaryStructureResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> CreateComponentAsync(
        HttpClient client, string name, SalaryComponentKind kind, SalaryComponentCalcType calcType, string? code = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/components",
            new CreateSalaryComponentBody(code ?? ShortCode(), name, kind, calcType, true, 1),
            TestContext.Current.CancellationToken);

        SalaryComponentResponse? body = await response.Content.ReadFromJsonAsync<SalaryComponentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    /// <summary>A code short enough for the various 20-30 character code columns while still
    /// unique per test run - see <c>PayrollEndpointTests</c>'s own <c>ShortCode</c> helper.</summary>
    private static string ShortCode() => $"C{Guid.NewGuid():N}"[..18];

    /// <summary>Same reasoning as <c>PayrollEndpointTests.GetOrCreateBasicComponentAsync</c>: the
    /// Basic component's code must be the literal "BASIC", and these tests share one database
    /// across the whole collection, so a run-calculation test fetches the tenant's existing Basic
    /// component rather than assuming it is the first to create one.</summary>
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

        return await CreateComponentAsync(client, "Basic", SalaryComponentKind.Earning, SalaryComponentCalcType.Fixed, code: "BASIC");
    }

    private async Task<Guid> SeedEmployeeAsync()
    {
        Guid employeeId = Guid.NewGuid();

        using PayrollDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            db.EmployeeSnapshots.Add(EmployeeSnapshot.Create(
                employeeId, $"EMP-{Guid.NewGuid():N}"[..10], "Payroll Run Test Employee", "Confirmed",
                new DateOnly(2020, 1, 1), version: 1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return employeeId;
    }
}
