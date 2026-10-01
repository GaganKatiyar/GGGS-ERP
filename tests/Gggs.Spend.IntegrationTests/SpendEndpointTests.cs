using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Spend.Api.Contracts;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Snapshots;
using Gggs.Spend.Infrastructure;
using Shouldly;

namespace Gggs.Spend.IntegrationTests;

/// <summary><c>/api/v1/spend/categories*</c> and <c>/api/v1/spend/claims*</c> over HTTP: creating
/// a category, creating a claim and adding lines to it as the owning employee, submitting it, and
/// an admin approving or rejecting it. No Workflow integration in this v1 slice - see
/// <see cref="ExpenseClaim"/>'s own remarks.</summary>
[Collection(nameof(SpendApiCollection))]
public sealed class SpendEndpointTests(SpendApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static readonly string[] AdminPermissions =
    [
        Permissions.Spend.CategoryManage,
        Permissions.Spend.ClaimView,
        Permissions.Spend.ClaimApprove,
    ];

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingACategory_WithoutThePermission_IsForbidden()
    {
        using HttpClient client = factory.CreateClientFor();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/spend/categories",
            new CreateExpenseCategoryBody(ShortCode(), "Travel"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnEmployee_CanCreateAClaimAddLinesAndSubmitIt()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid categoryId = await CreateCategoryAsync(adminClient);
        Guid employeeId = await SeedEmployeeAsync();

        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.ExpenseClaimCreate);

        HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/claims",
            new CreateExpenseClaimBody(employeeId, ShortCode(), "Client visit", "INR"),
            TestContext.Current.CancellationToken);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        ExpenseClaimResponse? claim = await createResponse.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);
        claim.ShouldNotBeNull();
        claim.Status.ShouldBe(ExpenseClaimStatus.Draft);

        HttpResponseMessage lineResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/spend/claims/{claim.Id}/lines",
            new AddExpenseLineBody(categoryId, new DateOnly(2026, 4, 10), 1500m, "Taxi Co"),
            TestContext.Current.CancellationToken);
        lineResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage submitResponse = await ownerClient.PostAsync(
            $"/api/v1/spend/claims/{claim.Id}/submit", content: null, TestContext.Current.CancellationToken);
        submitResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        ExpenseClaimResponse? submitted = await submitResponse.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);
        submitted.ShouldNotBeNull();
        submitted.Status.ShouldBe(ExpenseClaimStatus.Submitted);
        submitted.Total.ShouldBe(1500m);
        submitted.SubmittedDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task SubmittingAClaim_WithNoLines_IsRejected()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid employeeId = await SeedEmployeeAsync();

        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.ExpenseClaimCreate);

        HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/claims",
            new CreateExpenseClaimBody(employeeId, ShortCode(), "Empty claim", "INR"),
            TestContext.Current.CancellationToken);
        ExpenseClaimResponse? claim = await createResponse.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);

        HttpResponseMessage submitResponse = await ownerClient.PostAsync(
            $"/api/v1/spend/claims/{claim!.Id}/submit", content: null, TestContext.Current.CancellationToken);

        submitResponse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AnAdmin_CanApproveASubmittedClaim()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        (Guid claimId, _) = await CreateAndSubmitAClaimAsync(adminClient);

        HttpResponseMessage response = await adminClient.PostAsync(
            $"/api/v1/spend/claims/{claimId}/approve", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ExpenseClaimResponse? approved = await response.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);
        approved.ShouldNotBeNull();
        approved.Status.ShouldBe(ExpenseClaimStatus.Approved);
        approved.DecidedDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task AnAdmin_CanRejectASubmittedClaim()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        (Guid claimId, _) = await CreateAndSubmitAClaimAsync(adminClient);

        HttpResponseMessage response = await adminClient.PostAsync(
            $"/api/v1/spend/claims/{claimId}/reject", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ExpenseClaimResponse? rejected = await response.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);
        rejected.ShouldNotBeNull();
        rejected.Status.ShouldBe(ExpenseClaimStatus.Rejected);
    }

    [Fact]
    public async Task AnEmployee_CanReadTheirOwnClaim_ButNotSomeoneElses()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        (Guid claimId, Guid employeeId) = await CreateAndSubmitAClaimAsync(adminClient);

        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.ExpenseClaimCreate);
        HttpResponseMessage ownResponse = await ownerClient.GetAsync($"/api/v1/spend/claims/{claimId}", TestContext.Current.CancellationToken);
        ownResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpClient otherEmployeeClient = factory.CreateClientForEmployee(Guid.NewGuid(), Permissions.Self.ExpenseClaimCreate);
        HttpResponseMessage otherResponse = await otherEmployeeClient.GetAsync($"/api/v1/spend/claims/{claimId}", TestContext.Current.CancellationToken);
        otherResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApprovingAClaim_BeforeItHasBeenSubmitted_IsRejected()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid employeeId = await SeedEmployeeAsync();
        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.ExpenseClaimCreate);

        HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/claims",
            new CreateExpenseClaimBody(employeeId, ShortCode(), "Draft claim", "INR"),
            TestContext.Current.CancellationToken);
        ExpenseClaimResponse? claim = await createResponse.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);

        HttpResponseMessage response = await adminClient.PostAsync(
            $"/api/v1/spend/claims/{claim!.Id}/approve", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AnEmployee_CannotCreateAClaimForSomeoneElse()
    {
        Guid employeeId = await SeedEmployeeAsync();
        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.ExpenseClaimCreate);

        HttpResponseMessage response = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/claims",
            new CreateExpenseClaimBody(Guid.NewGuid(), ShortCode(), "Claim for someone else", "INR"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnEmployee_CannotAddALineOrSubmitSomeoneElsesClaim()
    {
        using HttpClient adminClient = factory.CreateClientFor(AdminPermissions);
        Guid categoryId = await CreateCategoryAsync(adminClient);
        Guid ownerId = await SeedEmployeeAsync();
        using HttpClient ownerClient = factory.CreateClientForEmployee(ownerId, Permissions.Self.ExpenseClaimCreate);

        HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/claims",
            new CreateExpenseClaimBody(ownerId, ShortCode(), "Someone else's claim", "INR"),
            TestContext.Current.CancellationToken);
        ExpenseClaimResponse? claim = await createResponse.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);

        using HttpClient otherEmployeeClient = factory.CreateClientForEmployee(Guid.NewGuid(), Permissions.Self.ExpenseClaimCreate);

        HttpResponseMessage lineResponse = await otherEmployeeClient.PostAsJsonAsync(
            $"/api/v1/spend/claims/{claim!.Id}/lines",
            new AddExpenseLineBody(categoryId, new DateOnly(2026, 4, 1), 100m, null),
            TestContext.Current.CancellationToken);
        lineResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        HttpResponseMessage submitResponse = await otherEmployeeClient.PostAsync(
            $"/api/v1/spend/claims/{claim.Id}/submit", content: null, TestContext.Current.CancellationToken);
        submitResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Creates a category, a claim with one line, and submits it. Returns the claim id and
    /// the employee it belongs to.</summary>
    private async Task<(Guid ClaimId, Guid EmployeeId)> CreateAndSubmitAClaimAsync(HttpClient adminClient)
    {
        Guid categoryId = await CreateCategoryAsync(adminClient);
        Guid employeeId = await SeedEmployeeAsync();

        using HttpClient ownerClient = factory.CreateClientForEmployee(employeeId, Permissions.Self.ExpenseClaimCreate);

        HttpResponseMessage createResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/spend/claims",
            new CreateExpenseClaimBody(employeeId, ShortCode(), "Submitted claim", "INR"),
            TestContext.Current.CancellationToken);
        ExpenseClaimResponse? claim = await createResponse.Content.ReadFromJsonAsync<ExpenseClaimResponse>(JsonOptions, TestContext.Current.CancellationToken);

        await ownerClient.PostAsJsonAsync(
            $"/api/v1/spend/claims/{claim!.Id}/lines",
            new AddExpenseLineBody(categoryId, new DateOnly(2026, 4, 5), 500m, null),
            TestContext.Current.CancellationToken);

        await ownerClient.PostAsync($"/api/v1/spend/claims/{claim.Id}/submit", content: null, TestContext.Current.CancellationToken);

        return (claim.Id, employeeId);
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/spend/categories",
            new CreateExpenseCategoryBody(ShortCode(), "Travel"),
            TestContext.Current.CancellationToken);

        ExpenseCategoryResponse? body = await response.Content.ReadFromJsonAsync<ExpenseCategoryResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static string ShortCode() => $"C{Guid.NewGuid():N}"[..18];

    private async Task<Guid> SeedEmployeeAsync()
    {
        Guid employeeId = Guid.NewGuid();

        using SpendDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            db.EmployeeSnapshots.Add(EmployeeSnapshot.Create(
                employeeId, $"EMP-{Guid.NewGuid():N}"[..10], "Spend Test Employee", "Confirmed",
                new DateOnly(2020, 1, 1), version: 1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return employeeId;
    }
}
