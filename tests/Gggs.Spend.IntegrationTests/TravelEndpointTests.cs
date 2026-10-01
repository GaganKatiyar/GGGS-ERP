using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Spend.Api.Contracts;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Snapshots;
using Gggs.Spend.Domain.Travel;
using Gggs.Spend.Infrastructure;
using Shouldly;

namespace Gggs.Spend.IntegrationTests;

/// <summary><c>/api/v1/spend/travel-requests*</c>, <c>/api/v1/spend/travel-advances*</c> and
/// <c>/api/v1/spend/travel-settlements*</c> over HTTP: an employee requesting travel, an admin
/// approving it and disbursing an advance, the employee raising an expense claim linked to the
/// trip, and an admin building and settling the trip's reconciliation. See
/// <see cref="TravelSettlement"/>'s own remarks on the net-payable/net-recoverable math.</summary>
[Collection(nameof(SpendApiCollection))]
public sealed class TravelEndpointTests(SpendApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AdminPermissions =
    [
        Permissions.Spend.CategoryManage,
        Permissions.Spend.ClaimView,
        Permissions.Spend.ClaimApprove,
        Permissions.Spend.TravelView,
        Permissions.Spend.TravelApprove,
        Permissions.Spend.TravelAdvanceManage,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task AnEmployee_CanCreateSubmitAndHaveATravelRequestApproved()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid employeeId = await SeedEmployeeAsync();
        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.TravelRequestCreate);

        HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/travel-requests",
            new CreateTravelRequestBody(employeeId, ShortCode(), "Client visit", "Pune", "Bengaluru",
                new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3), 15000m, "INR"),
            TestContext.Current.CancellationToken);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        TravelRequestResponse? request = await createResponse.Content.ReadFromJsonAsync<TravelRequestResponse>(JsonOptions, TestContext.Current.CancellationToken);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(TravelRequestStatus.Draft);

        HttpResponseMessage submitResponse = await ownerClient.PostAsync(
            $"/api/v1/spend/travel-requests/{request.Id}/submit", content: null, TestContext.Current.CancellationToken);
        submitResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage approveResponse = await adminClient.PostAsync(
            $"/api/v1/spend/travel-requests/{request.Id}/approve", content: null, TestContext.Current.CancellationToken);
        approveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        TravelRequestResponse? approved = await approveResponse.Content.ReadFromJsonAsync<TravelRequestResponse>(JsonOptions, TestContext.Current.CancellationToken);
        approved.ShouldNotBeNull();
        approved.Status.ShouldBe(TravelRequestStatus.Approved);
    }

    [Fact]
    public async Task RequestingAnAdvance_AgainstAnUnapprovedRequest_IsRejected()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid employeeId = await SeedEmployeeAsync();
        Guid requestId = await CreateTravelRequestAsync(factory.CreateClientForEmployee(employeeId, Permissions.Self.TravelRequestCreate), employeeId);

        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            $"/api/v1/spend/travel-requests/{requestId}/advances",
            new RequestTravelAdvanceBody(10000m, "INR"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Settlement_WithExpensesBelowTheAdvance_ComputesNetRecoverable()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid employeeId = await SeedEmployeeAsync();
        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.TravelRequestCreate, Permissions.Self.ExpenseClaimCreate);

        Guid requestId = await CreateApprovedTravelRequestAsync(adminClient, ownerClient, employeeId);

        HttpResponseMessage advanceResponse = await adminClient.PostAsJsonAsync(
            $"/api/v1/spend/travel-requests/{requestId}/advances",
            new RequestTravelAdvanceBody(10000m, "INR"),
            TestContext.Current.CancellationToken);
        TravelAdvanceResponse? advance = await advanceResponse.Content.ReadFromJsonAsync<TravelAdvanceResponse>(JsonOptions, TestContext.Current.CancellationToken);

        await adminClient.PostAsync($"/api/v1/spend/travel-advances/{advance!.Id}/disburse", content: null, TestContext.Current.CancellationToken);

        // Only 6000 of actual, Approved spend against a 10000 advance - the employee owes back 4000.
        await CreateAndApproveLinkedClaimAsync(adminClient, ownerClient, employeeId, requestId, 6000m);

        HttpResponseMessage buildResponse = await adminClient.PostAsync(
            $"/api/v1/spend/travel-requests/{requestId}/settlement/build", content: null, TestContext.Current.CancellationToken);
        buildResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        TravelSettlementResponse? settlement = await buildResponse.Content.ReadFromJsonAsync<TravelSettlementResponse>(JsonOptions, TestContext.Current.CancellationToken);
        settlement.ShouldNotBeNull();
        settlement.AdvanceTotal.ShouldBe(10000m);
        settlement.ExpenseTotal.ShouldBe(6000m);
        settlement.NetRecoverable.ShouldBe(4000m);
        settlement.NetPayable.ShouldBe(0m);

        HttpResponseMessage settleResponse = await adminClient.PostAsync(
            $"/api/v1/spend/travel-settlements/{settlement.Id}/settle", content: null, TestContext.Current.CancellationToken);
        settleResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        TravelSettlementResponse? settled = await settleResponse.Content.ReadFromJsonAsync<TravelSettlementResponse>(JsonOptions, TestContext.Current.CancellationToken);
        settled.ShouldNotBeNull();
        settled.Status.ShouldBe(TravelSettlementStatus.Settled);
    }

    [Fact]
    public async Task Settlement_WithExpensesAboveTheAdvance_ComputesNetPayable()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid employeeId = await SeedEmployeeAsync();
        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.TravelRequestCreate, Permissions.Self.ExpenseClaimCreate);

        Guid requestId = await CreateApprovedTravelRequestAsync(adminClient, ownerClient, employeeId);

        HttpResponseMessage advanceResponse = await adminClient.PostAsJsonAsync(
            $"/api/v1/spend/travel-requests/{requestId}/advances",
            new RequestTravelAdvanceBody(5000m, "INR"),
            TestContext.Current.CancellationToken);
        TravelAdvanceResponse? advance = await advanceResponse.Content.ReadFromJsonAsync<TravelAdvanceResponse>(JsonOptions, TestContext.Current.CancellationToken);
        await adminClient.PostAsync($"/api/v1/spend/travel-advances/{advance!.Id}/disburse", content: null, TestContext.Current.CancellationToken);

        // 8000 of actual, Approved spend against only a 5000 advance - the employee is owed 3000.
        await CreateAndApproveLinkedClaimAsync(adminClient, ownerClient, employeeId, requestId, 8000m);

        HttpResponseMessage buildResponse = await adminClient.PostAsync(
            $"/api/v1/spend/travel-requests/{requestId}/settlement/build", content: null, TestContext.Current.CancellationToken);
        TravelSettlementResponse? settlement = await buildResponse.Content.ReadFromJsonAsync<TravelSettlementResponse>(JsonOptions, TestContext.Current.CancellationToken);
        settlement.ShouldNotBeNull();
        settlement.NetPayable.ShouldBe(3000m);
        settlement.NetRecoverable.ShouldBe(0m);
    }

    [Fact]
    public async Task LinkingAClaim_ToSomeoneElsesTravelRequest_IsRejected()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid ownerId = await SeedEmployeeAsync();
        using HttpClient ownerClient = factory.CreateClientForEmployee(ownerId, Permissions.Self.TravelRequestCreate);
        Guid requestId = await CreateTravelRequestAsync(ownerClient, ownerId);

        Guid otherEmployeeId = await SeedEmployeeAsync();
        using HttpClient otherClient = factory.CreateClientForEmployee(otherEmployeeId, Permissions.Self.ExpenseClaimCreate);

        HttpResponseMessage response = await otherClient.PostAsJsonAsync(
            "/api/v1/spend/claims",
            new CreateExpenseClaimBody(otherEmployeeId, ShortCode(), "Mislinked claim", "INR", requestId),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<Guid> CreateTravelRequestAsync(HttpClient ownerClient, Guid employeeId)
    {
        HttpResponseMessage response = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/travel-requests",
            new CreateTravelRequestBody(employeeId, ShortCode(), "Trip", "Pune", "Chennai",
                new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3), 12000m, "INR"),
            TestContext.Current.CancellationToken);

        TravelRequestResponse? body = await response.Content.ReadFromJsonAsync<TravelRequestResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<Guid> CreateApprovedTravelRequestAsync(HttpClient adminClient, HttpClient ownerClient, Guid employeeId)
    {
        Guid requestId = await CreateTravelRequestAsync(ownerClient, employeeId);
        await ownerClient.PostAsync($"/api/v1/spend/travel-requests/{requestId}/submit", content: null, TestContext.Current.CancellationToken);
        await adminClient.PostAsync($"/api/v1/spend/travel-requests/{requestId}/approve", content: null, TestContext.Current.CancellationToken);
        return requestId;
    }

    /// <summary>Creates a category (admin), a claim linked to the trip with one line for the given
    /// amount (owner), submits it, and has the admin approve it - the shape
    /// <see cref="TravelSettlementService.BuildAsync"/> needs to see it as real spend.</summary>
    private static async Task CreateAndApproveLinkedClaimAsync(
        HttpClient adminClient, HttpClient ownerClient, Guid employeeId, Guid travelRequestId, decimal amount)
    {
        HttpResponseMessage categoryResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/spend/categories",
            new CreateExpenseCategoryBody(ShortCode(), "Travel"),
            TestContext.Current.CancellationToken);
        ExpenseCategoryResponse? category = await categoryResponse.Content.ReadFromJsonAsync<ExpenseCategoryResponse>(JsonOptions, TestContext.Current.CancellationToken);

        HttpResponseMessage claimResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/claims",
            new CreateExpenseClaimBody(employeeId, ShortCode(), "Trip expenses", "INR", travelRequestId),
            TestContext.Current.CancellationToken);
        ExpenseClaimResponse? claim = await claimResponse.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);

        await ownerClient.PostAsJsonAsync(
            $"/api/v1/spend/claims/{claim!.Id}/lines",
            new AddExpenseLineBody(category!.Id, new DateOnly(2026, 6, 2), amount, null),
            TestContext.Current.CancellationToken);

        await ownerClient.PostAsync($"/api/v1/spend/claims/{claim.Id}/submit", content: null, TestContext.Current.CancellationToken);
        await adminClient.PostAsync($"/api/v1/spend/claims/{claim.Id}/approve", content: null, TestContext.Current.CancellationToken);
    }

    private static string ShortCode() => $"C{Guid.NewGuid():N}"[..18];

    private async Task<Guid> SeedEmployeeAsync()
    {
        Guid employeeId = Guid.NewGuid();

        using SpendDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            db.EmployeeSnapshots.Add(EmployeeSnapshot.Create(
                employeeId, $"EMP-{Guid.NewGuid():N}"[..10], "Travel Test Employee", "Confirmed",
                new DateOnly(2020, 1, 1), version: 1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return employeeId;
    }
}
