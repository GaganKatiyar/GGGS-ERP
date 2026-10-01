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

/// <summary><c>/api/v1/payroll/components*</c>, <c>/api/v1/payroll/structures*</c> and
/// <c>/api/v1/payroll/employee-salaries*</c> over HTTP: building a structure out of a Basic
/// (Fixed) and an HRA (PercentOfBasic) component, then assigning it to an employee and checking
/// the computed monthly/annual breakdown. No payroll runs, payslips or statutory compliance in
/// this v1 slice - see <see cref="Gggs.Payroll.Domain.Payroll.EmployeeSalary"/>'s own remarks.</summary>
[Collection(nameof(PayrollApiCollection))]
public sealed class PayrollEndpointTests(PayrollApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AllPermissions =
    [
        Permissions.Payroll.ComponentManage,
        Permissions.Payroll.StructureManage,
        Permissions.Payroll.EmployeeSalaryView,
        Permissions.Payroll.EmployeeSalaryManage,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingAComponent_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/components",
            new CreateSalaryComponentBody(ShortCode(), "Basic", SalaryComponentKind.Earning, SalaryComponentCalcType.Fixed, true, 1),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingAComponent_WithADuplicateCode_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        string code = ShortCode();
        var body = new CreateSalaryComponentBody(code, "Basic", SalaryComponentKind.Earning, SalaryComponentCalcType.Fixed, true, 1);

        (await client.PostAsJsonAsync("/api/v1/payroll/components", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/payroll/components", body with { Name = "Basic (dup)" }, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task DeactivatingThenReactivatingAComponent_MovesThroughBothStates()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid componentId = await CreateComponentAsync(client, "TEST", SalaryComponentKind.Earning, SalaryComponentCalcType.Fixed);

        HttpResponseMessage deactivateResponse = await client.PostAsync(
            $"/api/v1/payroll/components/{componentId}/deactivate", content: null, TestContext.Current.CancellationToken);
        SalaryComponentResponse? deactivated = await deactivateResponse.Content.ReadFromJsonAsync<SalaryComponentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        deactivated.ShouldNotBeNull();
        deactivated.IsActive.ShouldBeFalse();

        HttpResponseMessage reactivateResponse = await client.PostAsync(
            $"/api/v1/payroll/components/{componentId}/reactivate", content: null, TestContext.Current.CancellationToken);
        SalaryComponentResponse? reactivated = await reactivateResponse.Content.ReadFromJsonAsync<SalaryComponentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        reactivated.ShouldNotBeNull();
        reactivated.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task AddingTheSameBasicComponentTwice_ToAStructure_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);
        Guid basicId = await GetOrCreateBasicComponentAsync(client);

        (await client.PostAsJsonAsync(
                $"/api/v1/payroll/structures/{structureId}/components",
                new AddSalaryStructureComponentBody(basicId, 20000m, 1),
                TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Same component added twice - AddComponentAsync's "already added" guard, not the "second
        // Basic" guard (there is only one BASIC component to add here), but both land on 422.
        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 15000m, 2),
            TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AddingAPercentageComponent_OutOfRange_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);
        Guid hraId = await CreateComponentAsync(client, "HRA", SalaryComponentKind.Earning, SalaryComponentCalcType.PercentOfBasic);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(hraId, 150m, 1),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AssigningASalary_AgainstAStructureWithNoBasic_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);
        Guid hraId = await CreateComponentAsync(client, "HRA2", SalaryComponentKind.Earning, SalaryComponentCalcType.PercentOfBasic);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(hraId, 50m, 1),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 600000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AssigningASalary_WithBasicAndHra_ComputesTheCorrectMonthlyBreakdown()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid hraId = await CreateComponentAsync(client, "HRA", SalaryComponentKind.Earning, SalaryComponentCalcType.PercentOfBasic);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 20000m, 1),
            TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(hraId, 50m, 2),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 600000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        EmployeeSalaryResponse? salary = await response.Content.ReadFromJsonAsync<EmployeeSalaryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        salary.ShouldNotBeNull();
        salary.Components.Count.ShouldBe(2);

        EmployeeSalaryComponentResponse basicLine = salary.Components.Single(x => x.ComponentId == basicId);
        basicLine.MonthlyAmount.ShouldBe(20000m);
        basicLine.AnnualAmount.ShouldBe(240000m);

        EmployeeSalaryComponentResponse hraLine = salary.Components.Single(x => x.ComponentId == hraId);
        hraLine.MonthlyAmount.ShouldBe(10000m);
        hraLine.AnnualAmount.ShouldBe(120000m);

        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/v1/payroll/employees/{employeeId}/salary", TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddingAPercentOfBasicCappedComponent_WithoutAWageCeiling_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);
        Guid pfId = await CreateComponentAsync(client, "PF1", SalaryComponentKind.Deduction, SalaryComponentCalcType.PercentOfBasicCapped);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(pfId, 12m, 1),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AssigningASalary_WithPfBelowTheWageCeiling_ComputesPfOnActualBasic()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid pfId = await CreateComponentAsync(client, "PF2", SalaryComponentKind.Deduction, SalaryComponentCalcType.PercentOfBasicCapped);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 10000m, 1),
            TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(pfId, 12m, 2, WageCeiling: 15000m),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 120000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        EmployeeSalaryResponse? salary = await response.Content.ReadFromJsonAsync<EmployeeSalaryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        salary.ShouldNotBeNull();

        // Basic (10000) is below the 15000 ceiling, so PF is 12% of the actual Basic: 1200.
        EmployeeSalaryComponentResponse pfLine = salary.Components.Single(x => x.ComponentId == pfId);
        pfLine.MonthlyAmount.ShouldBe(1200m);
    }

    [Fact]
    public async Task AssigningASalary_WithPfAboveTheWageCeiling_ComputesPfOnTheCeiling()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid pfId = await CreateComponentAsync(client, "PF3", SalaryComponentKind.Deduction, SalaryComponentCalcType.PercentOfBasicCapped);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 30000m, 1),
            TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(pfId, 12m, 2, WageCeiling: 15000m),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 360000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        EmployeeSalaryResponse? salary = await response.Content.ReadFromJsonAsync<EmployeeSalaryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        salary.ShouldNotBeNull();

        // Basic (30000) exceeds the 15000 ceiling, so PF is 12% of the ceiling, not the full Basic: 1800.
        EmployeeSalaryComponentResponse pfLine = salary.Components.Single(x => x.ComponentId == pfId);
        pfLine.MonthlyAmount.ShouldBe(1800m);
    }

    [Fact]
    public async Task AssigningASalary_WithEsiAtOrBelowTheWageCeiling_ComputesEsiOnActualBasic()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid esiId = await CreateComponentAsync(client, "ESI1", SalaryComponentKind.Deduction, SalaryComponentCalcType.PercentOfBasicWithEligibilityCap);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 18000m, 1),
            TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(esiId, 0.75m, 2, WageCeiling: 21000m),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 216000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        EmployeeSalaryResponse? salary = await response.Content.ReadFromJsonAsync<EmployeeSalaryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        salary.ShouldNotBeNull();

        // Basic (18000) is at or under the 21000 ceiling, so ESI applies at 0.75% of the actual Basic: 135.
        EmployeeSalaryComponentResponse esiLine = salary.Components.Single(x => x.ComponentId == esiId);
        esiLine.MonthlyAmount.ShouldBe(135m);
    }

    [Fact]
    public async Task AssigningASalary_WithEsiAboveTheWageCeiling_IsNotDeductedAtAll()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid esiId = await CreateComponentAsync(client, "ESI2", SalaryComponentKind.Deduction, SalaryComponentCalcType.PercentOfBasicWithEligibilityCap);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 30000m, 1),
            TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(esiId, 0.75m, 2, WageCeiling: 21000m),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 360000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        EmployeeSalaryResponse? salary = await response.Content.ReadFromJsonAsync<EmployeeSalaryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        salary.ShouldNotBeNull();

        // Basic (30000) exceeds the 21000 ceiling, so ESI is not deducted at all - unlike PF, there
        // is no reduced-base fallback; the employee is simply not ESI-eligible.
        EmployeeSalaryComponentResponse esiLine = salary.Components.Single(x => x.ComponentId == esiId);
        esiLine.MonthlyAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task AddingASlab_ToANonFlatSlabComponent_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid hraId = await CreateComponentAsync(client, "HRA3", SalaryComponentKind.Earning, SalaryComponentCalcType.PercentOfBasic);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/payroll/components/{hraId}/slabs",
            new AddSalaryComponentSlabBody(15000m, 200m),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AddingAFlatSlabComponent_ToAStructure_WithNoSlabsConfigured_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);
        Guid ptId = await CreateComponentAsync(client, "PT1", SalaryComponentKind.Deduction, SalaryComponentCalcType.FlatSlab);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(ptId, 0m, 1),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AssigningASalary_WithPtBelowTheFirstSlabThreshold_ComputesZero()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid ptId = await CreateComponentAsync(client, "PT2", SalaryComponentKind.Deduction, SalaryComponentCalcType.FlatSlab);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/components/{ptId}/slabs",
            new AddSalaryComponentSlabBody(0m, 0m),
            TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(
            $"/api/v1/payroll/components/{ptId}/slabs",
            new AddSalaryComponentSlabBody(15000m, 200m),
            TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 12000m, 1),
            TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(ptId, 0m, 2),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 144000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        EmployeeSalaryResponse? salary = await response.Content.ReadFromJsonAsync<EmployeeSalaryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        salary.ShouldNotBeNull();

        // Basic (12000) is below the first paid threshold (15000), so PT resolves to the zero-rate
        // band: 0.
        EmployeeSalaryComponentResponse ptLine = salary.Components.Single(x => x.ComponentId == ptId);
        ptLine.MonthlyAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task AssigningASalary_WithPtAtOrAboveASlabThreshold_ComputesThatSlabsAmount()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);

        Guid basicId = await GetOrCreateBasicComponentAsync(client);
        Guid ptId = await CreateComponentAsync(client, "PT3", SalaryComponentKind.Deduction, SalaryComponentCalcType.FlatSlab);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/components/{ptId}/slabs",
            new AddSalaryComponentSlabBody(0m, 0m),
            TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(
            $"/api/v1/payroll/components/{ptId}/slabs",
            new AddSalaryComponentSlabBody(15000m, 200m),
            TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 20000m, 1),
            TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(ptId, 0m, 2),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 240000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        EmployeeSalaryResponse? salary = await response.Content.ReadFromJsonAsync<EmployeeSalaryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        salary.ShouldNotBeNull();

        // Basic (20000) is at or above the 15000 threshold, so PT resolves to that slab's amount: 200.
        EmployeeSalaryComponentResponse ptLine = salary.Components.Single(x => x.ComponentId == ptId);
        ptLine.MonthlyAmount.ShouldBe(200m);
    }

    [Fact]
    public async Task AssigningASalary_ForAnExitedEmployee_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(AllPermissions);
        Guid structureId = await CreateStructureAsync(client);
        Guid basicId = await GetOrCreateBasicComponentAsync(client);

        await client.PostAsJsonAsync(
            $"/api/v1/payroll/structures/{structureId}/components",
            new AddSalaryStructureComponentBody(basicId, 20000m, 1),
            TestContext.Current.CancellationToken);

        Guid employeeId = await SeedEmployeeAsync(status: "Exited");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/employee-salaries",
            new AssignEmployeeSalaryBody(employeeId, structureId, 600000m, "INR", new DateOnly(2026, 4, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<Guid> CreateStructureAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/payroll/structures",
            new CreateSalaryStructureBody(ShortCode(), "Standard CTC Structure"),
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

    /// <summary>A code short enough for <c>SalaryComponent.Code</c>/<c>SalaryStructure.Code</c>'s
    /// 30-character limit while still unique per test run - see Talent's own <c>ShortCode</c>
    /// helper, which hit the same limit with a full GUID.</summary>
    private static string ShortCode() => $"C{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// The Basic component's code has to be the literal string "BASIC" for
    /// <c>SalaryStructureService</c>'s own detection logic to recognize it - unlike every other
    /// component this suite creates, it cannot be given a random per-test code. Since these tests
    /// share one database across every <see cref="Fact"/> in this collection (the same reason
    /// <c>LeaveEndpointTests.GetOrCreatePolicyAsync</c> exists), this fetches the tenant's existing
    /// Basic component rather than assuming it is the first test to create one.
    /// </summary>
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

    private async Task<Guid> SeedEmployeeAsync(string status = "Confirmed")
    {
        Guid employeeId = Guid.NewGuid();

        using PayrollDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            db.EmployeeSnapshots.Add(EmployeeSnapshot.Create(
                employeeId, $"EMP-{Guid.NewGuid():N}"[..10], "Payroll Test Employee", status,
                new DateOnly(2020, 1, 1), version: 1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return employeeId;
    }
}
