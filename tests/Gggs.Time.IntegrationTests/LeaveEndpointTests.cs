using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Time.Api.Contracts;
using Gggs.Time.Domain.Leave;
using Gggs.Time.Domain.Snapshots;
using Gggs.Time.Infrastructure;
using Shouldly;

namespace Gggs.Time.IntegrationTests;

/// <summary><c>/api/v1/leaves/types</c> and <c>/api/v1/leaves/policies</c> over HTTP.</summary>
[Collection(nameof(TimeApiCollection))]
public sealed class LeaveTypeAndPolicyEndpointTests(TimeApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task CreatingALeaveType_WithAUniqueCode_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Leave.TypeManage);
        string code = $"CAS-{Guid.NewGuid().ToString("N")[..8]}";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/types",
            new CreateLeaveTypeBody(code, "Casual leave", true, LeaveUnit.Day, null, false, false, "#22c55e"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreatingALeaveType_WithADuplicateCode_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Leave.TypeManage);
        string code = $"CAS-{Guid.NewGuid().ToString("N")[..8]}";
        var body = new CreateLeaveTypeBody(code, "Casual leave", true, LeaveUnit.Day, null, false, false, "#22c55e");

        (await client.PostAsJsonAsync("/api/v1/leaves/types", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync("/api/v1/leaves/types", body, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingALeaveType_WithoutThePermission_IsRefused()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Leave.RequestView);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/types",
            new CreateLeaveTypeBody("X", "X", true, LeaveUnit.Day, null, false, false, "#000000"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingAPolicyRule_ForTheSameLeaveTypeTwice_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Leave.TypeManage, Permissions.Leave.PolicyManage);

        Guid leaveTypeId = await CreateLeaveTypeAsync(client);
        Guid policyId = await GetOrCreatePolicyAsync(client);

        var ruleBody = new CreateLeavePolicyRuleBody(
            leaveTypeId, AccrualFrequency.Monthly, 1.5m, false, 24m, 10m, 3, false, 0, 1, null, 0.5m, true, false, 0m);

        (await client.PostAsJsonAsync($"/api/v1/leaves/policies/{policyId}/rules", ruleBody, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/v1/leaves/policies/{policyId}/rules", ruleBody, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<Guid> CreateLeaveTypeAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/types",
            new CreateLeaveTypeBody($"T-{Guid.NewGuid().ToString("N")[..8]}", "A type", true, LeaveUnit.Day, null, false, false, "#000000"),
            TestContext.Current.CancellationToken);

        LeaveTypeResponse? body = await response.Content.ReadFromJsonAsync<LeaveTypeResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    /// <summary>
    /// v1 allows at most one policy per tenant (<see cref="Gggs.Time.Application.Leave.LeavePolicyService.CreateAsync"/>
    /// rejects a second one), and these tests share one database across every <see cref="Fact"/> in
    /// this collection - so this gets the tenant's existing policy rather than assuming it is the
    /// first test to create one.
    /// </summary>
    private static async Task<Guid> GetOrCreatePolicyAsync(HttpClient client)
    {
        HttpResponseMessage listResponse = await client.GetAsync("/api/v1/leaves/policies", TestContext.Current.CancellationToken);
        List<LeavePolicyResponse>? existing = await listResponse.Content.ReadFromJsonAsync<List<LeavePolicyResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);

        if (existing is { Count: > 0 })
        {
            return existing[0].Id;
        }

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/policies", new CreateLeavePolicyBody($"Policy-{Guid.CreateVersion7():N}"), TestContext.Current.CancellationToken);

        LeavePolicyResponse? body = await response.Content.ReadFromJsonAsync<LeavePolicyResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }
}

/// <summary><c>/api/v1/leaves/requests</c> over HTTP - the full submit flow through to the
/// StartWorkflowIntegrationEvent being staged in the outbox.</summary>
[Collection(nameof(TimeApiCollection))]
public sealed class LeaveRequestEndpointTests(TimeApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    /// <summary>Always well after "today", wherever these tests actually run - a hardcoded date
    /// eventually falls into the past and starts failing MinNoticeDays for reasons that have
    /// nothing to do with what the test is checking.</summary>
    private static DateOnly FutureDate(int daysFromNow) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysFromNow);

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        Gggs.BuildingBlocks.Web.JsonConfiguration.Apply(options);
        return options;
    }

    [Fact]
    public async Task SubmittingALeaveRequest_WithNoPolicyConfigured_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Leave.TypeManage, Permissions.Leave.RequestCreate);
        await SeedEmployeeAsync();

        Guid leaveTypeId = await CreateLeaveTypeAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/requests",
            new CreateLeaveRequestBody(
                null, leaveTypeId, FutureDate(10), LeaveDaySession.Full, FutureDate(10), LeaveDaySession.Full,
                "Personal", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SubmittingALeaveRequest_WithASufficientBalance_Succeeds()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Leave.TypeManage, Permissions.Leave.PolicyManage, Permissions.Leave.RequestCreate, Permissions.Leave.BalanceAdjust);

        await SeedEmployeeAsync();
        Guid leaveTypeId = await CreateLeaveTypeAsync(client);
        await ConfigurePolicyAsync(client, leaveTypeId);
        int periodYear = FutureDate(20).Year;
        await SeedOpeningBalanceAsync(leaveTypeId, openingDays: 10m, periodYear: periodYear);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/requests",
            new CreateLeaveRequestBody(
                null, leaveTypeId, FutureDate(20), LeaveDaySession.Full, FutureDate(21), LeaveDaySession.Full,
                "Personal", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        LeaveRequestResponse? body = await response.Content.ReadFromJsonAsync<LeaveRequestResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Status.ShouldBe(LeaveRequestStatus.Pending);
        body.TotalDays.ShouldBe(2m);
    }

    [Fact]
    public async Task SubmittingALeaveRequest_ExceedingTheAvailableBalance_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Leave.TypeManage, Permissions.Leave.PolicyManage, Permissions.Leave.RequestCreate, Permissions.Leave.BalanceAdjust);

        await SeedEmployeeAsync();
        Guid leaveTypeId = await CreateLeaveTypeAsync(client);
        await ConfigurePolicyAsync(client, leaveTypeId);
        int periodYear = FutureDate(30).Year;
        await SeedOpeningBalanceAsync(leaveTypeId, openingDays: 1m, periodYear: periodYear);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/requests",
            new CreateLeaveRequestBody(
                null, leaveTypeId, FutureDate(30), LeaveDaySession.Full, FutureDate(32), LeaveDaySession.Full,
                "Personal", null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SubmittingALeaveRequest_ThatOverlapsAnExistingOne_IsRejected()
    {
        using HttpClient client = factory.CreateClientFor(
            Permissions.Leave.TypeManage, Permissions.Leave.PolicyManage, Permissions.Leave.RequestCreate, Permissions.Leave.BalanceAdjust);

        await SeedEmployeeAsync();
        Guid leaveTypeId = await CreateLeaveTypeAsync(client);
        await ConfigurePolicyAsync(client, leaveTypeId);
        int periodYear = FutureDate(40).Year;
        await SeedOpeningBalanceAsync(leaveTypeId, openingDays: 20m, periodYear: periodYear);

        var body = new CreateLeaveRequestBody(
            null, leaveTypeId, FutureDate(40), LeaveDaySession.Full, FutureDate(41), LeaveDaySession.Full, "First", null);

        (await client.PostAsJsonAsync("/api/v1/leaves/requests", body, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/leaves/requests",
            body with { Reason = "Overlapping" },
            TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreatingALeaveRequest_WithoutThePermission_IsRefused()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Leave.RequestView);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/requests",
            new CreateLeaveRequestBody(
                null, Guid.NewGuid(), FutureDate(50), LeaveDaySession.Full, FutureDate(50), LeaveDaySession.Full, null, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task SeedEmployeeAsync()
    {
        using TimeDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            if (await db.EmployeeSnapshots.FindAsync([TimeApiFactory.CurrentEmployeeId]) is not null)
            {
                return;
            }

            db.EmployeeSnapshots.Add(EmployeeSnapshot.Create(
                TimeApiFactory.CurrentEmployeeId, "EMP-0001", "Test Employee", "Confirmed",
                new DateOnly(2020, 1, 1), version: 1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task<Guid> CreateLeaveTypeAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/leaves/types",
            new CreateLeaveTypeBody($"T-{Guid.NewGuid().ToString("N")[..8]}", "A type", true, LeaveUnit.Day, null, false, false, "#000000"),
            TestContext.Current.CancellationToken);

        LeaveTypeResponse? body = await response.Content.ReadFromJsonAsync<LeaveTypeResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    /// <summary>
    /// v1 allows at most one policy per tenant, and these tests share one database across every
    /// <see cref="Fact"/> in this collection - so this gets the tenant's existing policy rather
    /// than assuming it is the first test to create one. Each call still adds its own rule, for
    /// its own (always-freshly-created) leave type, which is what
    /// <see cref="Gggs.Time.Application.Leave.LeavePolicyService.CreateRuleAsync"/> actually scopes
    /// its own duplicate check to.
    /// </summary>
    private static async Task ConfigurePolicyAsync(HttpClient client, Guid leaveTypeId)
    {
        HttpResponseMessage listResponse = await client.GetAsync("/api/v1/leaves/policies", TestContext.Current.CancellationToken);
        List<LeavePolicyResponse>? existingPolicies = await listResponse.Content.ReadFromJsonAsync<List<LeavePolicyResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);

        Guid policyId;

        if (existingPolicies is { Count: > 0 })
        {
            policyId = existingPolicies[0].Id;
        }
        else
        {
            HttpResponseMessage policyResponse = await client.PostAsJsonAsync(
                "/api/v1/leaves/policies", new CreateLeavePolicyBody($"Policy-{Guid.CreateVersion7():N}"), TestContext.Current.CancellationToken);

            LeavePolicyResponse? policy = await policyResponse.Content.ReadFromJsonAsync<LeavePolicyResponse>(
                JsonOptions, TestContext.Current.CancellationToken);
            policyId = policy!.Id;
        }

        var ruleBody = new CreateLeavePolicyRuleBody(
            leaveTypeId, AccrualFrequency.Monthly, 1.5m, false, 24m, 10m, 3, false,
            EligibleAfterDays: 0, MinNoticeDays: 0, MaxConsecutiveDays: null, MinDaysPerRequest: 0.5m,
            AllowHalfDay: true, AllowNegativeBalance: false, NegativeLimit: 0m);

        (await client.PostAsJsonAsync($"/api/v1/leaves/policies/{policyId}/rules", ruleBody, TestContext.Current.CancellationToken))
            .EnsureSuccessStatusCode();
    }

    private async Task SeedOpeningBalanceAsync(Guid leaveTypeId, decimal openingDays, int periodYear)
    {
        using TimeDbContext db = factory.CreateTenantScopedContext(out IDisposable scopeHandle);
        using (scopeHandle)
        {
            LeaveLedgerEntry entry = LeaveLedgerEntry.Post(
                TimeApiFactory.CurrentEmployeeId, leaveTypeId, periodYear, LeaveLedgerTransactionType.Opening, openingDays,
                new DateOnly(periodYear, 1, 1), sourceType: "Test", sourceId: Guid.NewGuid(), remarks: null);
            db.LeaveLedgerEntries.Add(entry);

            LeaveBalance balance = LeaveBalance.CreateEmpty(TimeApiFactory.CurrentEmployeeId, leaveTypeId, periodYear);
            balance.Recalculate([entry], pendingApprovalDays: 0m);
            db.LeaveBalances.Add(balance);

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }
}
