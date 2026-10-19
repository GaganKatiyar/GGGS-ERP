using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Expense.Contracts;
using Gggs.Modules.Expense.Domain;
using Gggs.Modules.Expense.Infrastructure;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Expense.Application;

public sealed record CategoryItem(Guid Id, string Code, string Name, decimal? LimitPerLine, decimal ReceiptRequiredAbove, bool IsActive);

public sealed record CategoryBody(string Code, string Name, decimal? LimitPerLine, decimal ReceiptRequiredAbove, bool IsActive = true);

public sealed record LineItem(Guid Id, Guid CategoryId, string CategoryName, DateOnly Date, string Description, decimal Amount, string? Merchant, bool HasReceipt, bool Billable, string? PolicyFlag, string? Justification);

public sealed record ClaimItem(
    Guid Id,
    string Number,
    Guid EmployeeId,
    string? EmployeeName,
    string Title,
    Guid? TravelRequestId,
    string? TravelNumber,
    Guid? ProjectId,
    string? ProjectName,
    string Currency,
    decimal Total,
    int Lines,
    int PolicyFlags,
    ClaimStatus Status,
    Guid? WorkflowInstanceId,
    DateTime? SubmittedDate,
    PaymentMode? PaymentMode,
    string? PaymentReference,
    DateOnly? PaidOn,
    DateTime CreatedDate,
    bool CanEdit);

public sealed record ClaimView(ClaimItem Claim, IReadOnlyList<LineItem> Lines);

public sealed record ClaimQuery
{
    public ClaimStatus? Status { get; init; }

    public Guid? EmployeeId { get; init; }
}

public sealed record ClaimBody(string Title, Guid? TravelRequestId, Guid? ProjectId, string? Currency);

public sealed record PayBody(PaymentMode Mode, string? Reference);

public sealed record TravelItem(
    Guid Id,
    string Number,
    Guid EmployeeId,
    string? EmployeeName,
    string Purpose,
    string FromCity,
    string ToCity,
    DateOnly DepartOn,
    DateOnly ReturnOn,
    TravelMode Mode,
    decimal EstimatedCost,
    decimal AdvanceRequested,
    Guid? ProjectId,
    string? ProjectName,
    TravelStatus Status,
    Guid? WorkflowInstanceId,
    decimal? AdvancePaid,
    DateOnly? AdvancePaidOn,
    decimal Claimed,
    decimal? SettlementAmount,
    DateOnly? SettledOn,
    bool CanEdit);

public sealed record TravelBody(TravelDetails Details, bool Submit);

public sealed record AdvanceBody(decimal Amount);

public sealed record ExpenseDashboard(int PendingApproval, decimal PendingAmount, int ApprovedUnpaid, decimal ApprovedUnpaidAmount, decimal PaidThisMonth, decimal OpenAdvances, IReadOnlyDictionary<string, decimal> ByCategoryThisMonth);

/// <summary>
/// Expenses and travel (spec: expense and travel): claims whose lines are checked against category
/// policy and approved through the workflow, reimbursed through payroll or directly; travel requests
/// approved, advanced and settled against the trip's claims.
/// </summary>
internal sealed class ExpenseService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IProjectDirectory projects,
    IWorkflowStarter workflow,
    IPayrollInputs payrollInputs,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string ClaimSubjectType = "expense.claim";
    public const string TravelSubjectType = "expense.travel";
    public const string ClaimSource = "expense.claim";
    public const string TravelSource = "expense.travel";

    public static WorkflowSubject ClaimSubject { get; } = new(
        ClaimSubjectType,
        "Approve an expense claim",
        "expense",
        [
            new WorkflowSubjectField("amount", "Amount", WorkflowFieldType.Number),
            new WorkflowSubjectField("policyFlags", "Lines outside policy", WorkflowFieldType.Number),
            new WorkflowSubjectField("billable", "Billable to a client (true/false)", WorkflowFieldType.Text),
        ]);

    public static WorkflowSubject TravelSubject { get; } = new(
        TravelSubjectType,
        "Approve a travel request",
        "expense",
        [
            new WorkflowSubjectField("estimatedCost", "Estimated cost", WorkflowFieldType.Number),
            new WorkflowSubjectField("advance", "Advance requested", WorkflowFieldType.Number),
            new WorkflowSubjectField("mode", "Mode", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Categories ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<CategoryItem>> CategoriesAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.ExpenseCategories.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken))
            .Select(row => new CategoryItem(row.Id, row.Code, row.Name, row.LimitPerLine, row.ReceiptRequiredAbove, row.IsActive))];

    public async Task<IReadOnlyList<CategoryItem>> SaveCategoryAsync(Guid? id, CategoryBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await dbContext.ExpenseCategories.AnyAsync(row => row.Code == code && row.Id != id, cancellationToken))
        {
            throw new DomainRuleException("EXPENSE_CATEGORY_CODE", $"A category already uses the code {code}.");
        }

        if (id is { } existing)
        {
            ExpenseCategory category = await dbContext.ExpenseCategories.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(ExpenseCategory), existing);
            category.Update(code, body.Name, body.LimitPerLine, body.ReceiptRequiredAbove, body.IsActive);
        }
        else
        {
            dbContext.ExpenseCategories.Add(ExpenseCategory.Create(code, body.Name, body.LimitPerLine, body.ReceiptRequiredAbove));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await CategoriesAsync(cancellationToken);
    }

    // ----- Claims -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ClaimItem>> ClaimsAsync(ClaimQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<ExpenseClaim> rows = await VisibleClaimsAsync(cancellationToken);
        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.EmployeeId is { } employee)
        {
            rows = rows.Where(row => row.EmployeeId == employee);
        }

        return await ClaimItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimItem>> MyClaimsAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return await ClaimItemsAsync(await dbContext.ExpenseClaims.AsNoTracking().Where(row => row.EmployeeId == me).OrderByDescending(row => row.CreatedDate).Take(300).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<ClaimView> ClaimAsync(Guid id, CancellationToken cancellationToken)
    {
        ExpenseClaim claim = await (await VisibleClaimsAsync(cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ExpenseClaim), id);
        return await ViewAsync(claim, cancellationToken);
    }

    public async Task<ClaimView> DraftAsync(ClaimBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        await ValidateLinksAsync(me, body.TravelRequestId, body.ProjectId, cancellationToken);
        string number = await NextNumberAsync(dbContext.ExpenseClaims.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Number), "EXP", cancellationToken);
        var claim = ExpenseClaim.Draft(number, me, body.Title, body.TravelRequestId, body.ProjectId, string.IsNullOrWhiteSpace(body.Currency) ? "INR" : body.Currency);
        dbContext.ExpenseClaims.Add(claim);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(claim, cancellationToken);
    }

    public async Task<ClaimView> UpdateAsync(Guid id, ClaimBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ExpenseClaim claim = await OwnClaimAsync(id, cancellationToken);
        await ValidateLinksAsync(claim.EmployeeId, body.TravelRequestId, body.ProjectId, cancellationToken);
        claim.Update(body.Title, body.TravelRequestId, body.ProjectId, string.IsNullOrWhiteSpace(body.Currency) ? claim.Currency : body.Currency);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(claim, cancellationToken);
    }

    /// <summary>Replaces the claim's lines, each checked against its category's policy.</summary>
    public async Task<ClaimView> SaveLinesAsync(Guid id, IReadOnlyList<ClaimLineInput> lines, CancellationToken cancellationToken)
    {
        ExpenseClaim claim = await OwnClaimAsync(id, cancellationToken);
        claim.EnsureEditable();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, ExpenseCategory> categories = await dbContext.ExpenseCategories.AsNoTracking().Where(row => row.IsActive).ToDictionaryAsync(row => row.Id, cancellationToken);
        dbContext.ExpenseLines.RemoveRange(await dbContext.ExpenseLines.Where(row => row.ClaimId == id).ToListAsync(cancellationToken));
        decimal total = 0;
        foreach (ClaimLineInput input in lines ?? [])
        {
            ExpenseCategory category = categories.GetValueOrDefault(input.CategoryId) ?? throw new DomainRuleException("EXPENSE_CATEGORY", "A line names a category that is not active.");
            var line = ExpenseLine.Add(id, input, category.PolicyCheck(input.Amount, input.HasReceipt), today);
            dbContext.ExpenseLines.Add(line);
            total += line.Amount;
        }

        claim.SetTotal(total);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(claim, cancellationToken);
    }

    public async Task<ClaimView> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        ExpenseClaim claim = await OwnClaimAsync(id, cancellationToken);
        claim.Submit(Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        List<ExpenseLine> lines = await dbContext.ExpenseLines.AsNoTracking().Where(row => row.ClaimId == id).ToListAsync(cancellationToken);
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = ClaimSubjectType,
                SubjectId = claim.Id,
                Title = $"{claim.Number}: {claim.Title} ({claim.Total.ToString("N2", CultureInfo.InvariantCulture)} {claim.Currency})",
                RequesterEmployeeId = claim.EmployeeId,
                Link = $"/expenses/claims/{claim.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["amount"] = claim.Total.ToString(CultureInfo.InvariantCulture),
                    ["policyFlags"] = lines.Count(line => line.PolicyFlag is not null).ToString(CultureInfo.InvariantCulture),
                    ["billable"] = lines.Any(line => line.Billable) ? "true" : "false",
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            claim.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            claim.Decide(approved: true, Now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(claim, cancellationToken);
    }

    public async Task<ClaimView> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        ExpenseClaim claim = await dbContext.ExpenseClaims.FirstOrDefaultAsync(row => row.Id == id && row.EmployeeId == currentUser.EmployeeId, cancellationToken) ?? throw new NotFoundException(nameof(ExpenseClaim), id);
        if (claim.Status == ClaimStatus.PendingApproval)
        {
            await workflow.WithdrawAsync(ClaimSubjectType, claim.Id, cancellationToken);
        }

        claim.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(claim, cancellationToken);
    }

    /// <summary>Reimburses an approved claim: through the next payroll as a reimbursement, or marked paid directly.</summary>
    public async Task<ClaimView> PayAsync(Guid id, PayBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ExpenseClaim claim = await dbContext.ExpenseClaims.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ExpenseClaim), id);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        string? reference = body.Reference;
        if (body.Mode == PaymentMode.Payroll)
        {
            Guid input = await payrollInputs.AddAsync(
                new PayrollInputRequest(claim.EmployeeId, new DateOnly(today.Year, today.Month, 1), PayrollInputKind.Reimbursement, claim.Total, $"Expense claim {claim.Number}", ClaimSource, claim.Id),
                cancellationToken);
            reference ??= $"Payroll input {input.ToString("N")[..8]}";
        }

        claim.Pay(body.Mode, reference, today);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAsync(claim.EmployeeId, "expense claim", claim.Number, claim.Total, claim.Currency, body.Mode == PaymentMode.Payroll ? "sent to payroll" : "paid", cancellationToken);
        return await ViewAsync(claim, cancellationToken);
    }

    public async Task DecideClaimAsync(Guid id, bool? approved, CancellationToken cancellationToken)
    {
        ExpenseClaim? claim = await dbContext.ExpenseClaims.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (claim?.Status != ClaimStatus.PendingApproval)
        {
            return;
        }

        claim.Decide(approved, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAsync(claim.EmployeeId, "expense claim", claim.Number, claim.Total, claim.Currency, approved switch { true => "approved", false => "rejected", null => "sent back" }, cancellationToken);
    }

    // ----- Travel -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TravelItem>> TripsAsync(TravelStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<TravelRequest> rows = await VisibleTripsAsync(cancellationToken);
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return await TripItemsAsync(await rows.OrderByDescending(row => row.DepartOn).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<TravelItem>> MyTripsAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return await TripItemsAsync(await dbContext.TravelRequests.AsNoTracking().Where(row => row.EmployeeId == me).OrderByDescending(row => row.DepartOn).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<TravelItem> TripAsync(Guid id, CancellationToken cancellationToken)
        => (await TripItemsAsync([await (await VisibleTripsAsync(cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(TravelRequest), id)], cancellationToken))[0];

    public async Task<TravelItem> RequestTripAsync(TravelBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        await ValidateLinksAsync(me, null, body.Details.ProjectId, cancellationToken);
        string number = await NextNumberAsync(dbContext.TravelRequests.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Number), "TRV", cancellationToken);
        var trip = TravelRequest.Draft(number, me, body.Details);
        dbContext.TravelRequests.Add(trip);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (body.Submit)
        {
            await SubmitTripAsync(trip, cancellationToken);
        }

        return await TripAsync(trip.Id, cancellationToken);
    }

    public async Task<TravelItem> UpdateTripAsync(Guid id, TravelDetails details, CancellationToken cancellationToken)
    {
        TravelRequest trip = await OwnTripAsync(id, cancellationToken);
        trip.Update(details);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await TripAsync(id, cancellationToken);
    }

    public async Task<TravelItem> TripActionAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        TravelRequest trip = await OwnTripAsync(id, cancellationToken);
        if (action == "submit")
        {
            await SubmitTripAsync(trip, cancellationToken);
        }
        else
        {
            if (trip.Status == TravelStatus.PendingApproval)
            {
                await workflow.WithdrawAsync(TravelSubjectType, trip.Id, cancellationToken);
            }

            trip.Cancel();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await TripAsync(id, cancellationToken);
    }

    public async Task<TravelItem> PayAdvanceAsync(Guid id, decimal amount, CancellationToken cancellationToken)
    {
        TravelRequest trip = await dbContext.TravelRequests.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(TravelRequest), id);
        trip.PayAdvance(amount, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await TripAsync(id, cancellationToken);
    }

    /// <summary>Settles a trip: approved and paid claims on it less the advance, the difference paid or recovered through payroll.</summary>
    public async Task<TravelItem> SettleAsync(Guid id, CancellationToken cancellationToken)
    {
        TravelRequest trip = await dbContext.TravelRequests.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(TravelRequest), id);
        if (await dbContext.ExpenseClaims.AnyAsync(row => row.TravelRequestId == id && (row.Status == ClaimStatus.Draft || row.Status == ClaimStatus.PendingApproval), cancellationToken))
        {
            throw new DomainRuleException("TRAVEL_CLAIMS_OPEN", "Some of the trip's claims are still in draft or awaiting approval.");
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<ExpenseClaim> claims = await dbContext.ExpenseClaims.Where(row => row.TravelRequestId == id && row.Status == ClaimStatus.Approved).ToListAsync(cancellationToken);
        decimal paidAlready = await dbContext.ExpenseClaims.Where(row => row.TravelRequestId == id && row.Status == ClaimStatus.Paid).SumAsync(row => row.Total, cancellationToken);
        decimal claimed = claims.Sum(row => row.Total) + paidAlready;
        trip.Settle(claimed, today);

        // Claims not yet paid are settled here, net of the advance.
        decimal net = claims.Sum(row => row.Total) - (trip.AdvancePaid ?? 0);
        foreach (ExpenseClaim claim in claims)
        {
            claim.Pay(PaymentMode.Payroll, $"Settled with {trip.Number}", today);
        }

        if (net != 0)
        {
            await payrollInputs.AddAsync(
                new PayrollInputRequest(trip.EmployeeId, new DateOnly(today.Year, today.Month, 1), net > 0 ? PayrollInputKind.Reimbursement : PayrollInputKind.Deduction, Math.Abs(net),
                    net > 0 ? $"Travel settlement {trip.Number}" : $"Unspent travel advance {trip.Number}", TravelSource, trip.Id),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await TripAsync(id, cancellationToken);
    }

    public async Task DecideTripAsync(Guid id, bool? approved, CancellationToken cancellationToken)
    {
        TravelRequest? trip = await dbContext.TravelRequests.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (trip?.Status != TravelStatus.PendingApproval)
        {
            return;
        }

        trip.Decide(approved);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAsync(trip.EmployeeId, "travel request", trip.Number, trip.EstimatedCost, "INR", approved switch { true => "approved", false => "rejected", null => "sent back" }, cancellationToken);
    }

    public async Task<ExpenseDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        List<ExpenseClaim> claims = await (await VisibleClaimsAsync(cancellationToken)).Where(row => row.Status != ClaimStatus.Draft && row.Status != ClaimStatus.Cancelled).ToListAsync(cancellationToken);
        List<Guid> monthIds = [.. claims.Where(row => row.SubmittedDate >= monthStart.ToDateTime(TimeOnly.MinValue)).Select(row => row.Id)];
        var byCategory = await (
                from line in dbContext.ExpenseLines.AsNoTracking()
                join category in dbContext.ExpenseCategories.AsNoTracking() on line.CategoryId equals category.Id
                where monthIds.Contains(line.ClaimId)
                group line by category.Name into grouped
                select new { Name = grouped.Key, Amount = grouped.Sum(line => line.Amount) })
            .ToDictionaryAsync(row => row.Name, row => row.Amount, cancellationToken);
        decimal advances = await (await VisibleTripsAsync(cancellationToken)).Where(row => row.AdvancePaid != null && row.Status == TravelStatus.Approved).SumAsync(row => row.AdvancePaid ?? 0, cancellationToken);
        return new ExpenseDashboard(
            claims.Count(row => row.Status == ClaimStatus.PendingApproval),
            claims.Where(row => row.Status == ClaimStatus.PendingApproval).Sum(row => row.Total),
            claims.Count(row => row.Status == ClaimStatus.Approved),
            claims.Where(row => row.Status == ClaimStatus.Approved).Sum(row => row.Total),
            claims.Where(row => row.Status == ClaimStatus.Paid && row.PaidOn >= monthStart).Sum(row => row.Total),
            advances,
            byCategory);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("EXPENSE_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task SubmitTripAsync(TravelRequest trip, CancellationToken cancellationToken)
    {
        trip.Submit();
        await dbContext.SaveChangesAsync(cancellationToken);
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = TravelSubjectType,
                SubjectId = trip.Id,
                Title = $"{trip.Number}: {trip.FromCity} to {trip.ToCity}, {trip.DepartOn:d MMM}",
                RequesterEmployeeId = trip.EmployeeId,
                Link = $"/expenses/travel/{trip.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["estimatedCost"] = trip.EstimatedCost.ToString(CultureInfo.InvariantCulture),
                    ["advance"] = trip.AdvanceRequested.ToString(CultureInfo.InvariantCulture),
                    ["mode"] = trip.Mode.ToString(),
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            trip.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            trip.Decide(approved: true);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task NotifyAsync(Guid employeeId, string kind, string number, decimal amount, string currency, string status, CancellationToken cancellationToken)
        => notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.ExpenseDecided,
                [new NotificationRecipient(employeeId)],
                new Dictionary<string, string> { ["kind"] = kind, ["number"] = number, ["amount"] = $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}", ["status"] = status },
                "/me/expenses"),
            cancellationToken);

    private async Task ValidateLinksAsync(Guid employeeId, Guid? travelRequestId, Guid? projectId, CancellationToken cancellationToken)
    {
        if (travelRequestId is { } trip && !await dbContext.TravelRequests.AnyAsync(row => row.Id == trip && row.EmployeeId == employeeId && row.Status == TravelStatus.Approved, cancellationToken))
        {
            throw new DomainRuleException("CLAIM_TRIP", "Link the claim to one of your approved, unsettled trips.");
        }

        if (projectId is { } project && (await projects.GetManyAsync([project], cancellationToken)).Count == 0)
        {
            throw new DomainRuleException("CLAIM_PROJECT", "The project does not exist.");
        }
    }

    private async Task<IQueryable<ExpenseClaim>> VisibleClaimsAsync(CancellationToken cancellationToken)
        => Scoped(dbContext.ExpenseClaims.AsNoTracking(), await ScopeAsync(Permissions.Spend.ClaimView, cancellationToken));

    private async Task<IQueryable<TravelRequest>> VisibleTripsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await ScopeAsync(Permissions.Spend.TravelView, cancellationToken);
        IQueryable<TravelRequest> rows = dbContext.TravelRequests.AsNoTracking();
        if (scope is null)
        {
            return rows;
        }

        Guid? me = currentUser.EmployeeId;
        return rows.Where(row => row.EmployeeId == me || scope.Contains(row.EmployeeId));
    }

    private IQueryable<ExpenseClaim> Scoped(IQueryable<ExpenseClaim> rows, IReadOnlyCollection<Guid>? scope)
    {
        if (scope is null)
        {
            return rows;
        }

        Guid? me = currentUser.EmployeeId;
        return rows.Where(row => row.EmployeeId == me || scope.Contains(row.EmployeeId));
    }

    /// <summary>Who else's records the caller sees: null for everyone, else the employees in scope (empty without the permission).</summary>
    private async Task<IReadOnlyCollection<Guid>?> ScopeAsync(string permission, CancellationToken cancellationToken)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(permission) ? await employees.InScopeIdsAsync(permission, cancellationToken) : [];

    private async Task<ExpenseClaim> OwnClaimAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.ExpenseClaims.FirstOrDefaultAsync(row => row.Id == id && row.EmployeeId == currentUser.EmployeeId, cancellationToken) ?? throw new NotFoundException(nameof(ExpenseClaim), id);

    private async Task<TravelRequest> OwnTripAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.TravelRequests.FirstOrDefaultAsync(row => row.Id == id && row.EmployeeId == currentUser.EmployeeId, cancellationToken) ?? throw new NotFoundException(nameof(TravelRequest), id);

    private async Task<string> NextNumberAsync(IQueryable<string> numbers, string kind, CancellationToken cancellationToken)
    {
        int year = (await clock.TodayAsync(cancellationToken)).Year;
        string prefix = $"{kind}-{year}-";
        List<string> taken = await numbers.Where(number => number.StartsWith(prefix)).ToListAsync(cancellationToken);
        int next = taken.Select(number => int.TryParse(number[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:00000}";
    }

    private async Task<ClaimView> ViewAsync(ExpenseClaim claim, CancellationToken cancellationToken)
    {
        List<ExpenseLine> lines = await dbContext.ExpenseLines.AsNoTracking().Where(row => row.ClaimId == claim.Id).OrderBy(row => row.Date).ToListAsync(cancellationToken);
        Dictionary<Guid, string> categories = await dbContext.ExpenseCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        return new ClaimView(
            (await ClaimItemsAsync([claim], cancellationToken))[0],
            [.. lines.Select(row => new LineItem(row.Id, row.CategoryId, categories.GetValueOrDefault(row.CategoryId) ?? string.Empty, row.Date, row.Description, row.Amount, row.Merchant, row.HasReceipt, row.Billable, row.PolicyFlag, row.Justification))]);
    }

    private async Task<List<ClaimItem>> ClaimItemsAsync(IReadOnlyList<ExpenseClaim> claims, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. claims.Select(row => row.Id)];
        var lines = await dbContext.ExpenseLines.AsNoTracking().Where(row => ids.Contains(row.ClaimId))
            .GroupBy(row => row.ClaimId).Select(group => new { group.Key, Count = group.Count(), Flags = group.Count(row => row.PolicyFlag != null) })
            .ToDictionaryAsync(row => row.Key, cancellationToken);
        List<Guid> tripIds = [.. claims.Where(row => row.TravelRequestId != null).Select(row => row.TravelRequestId!.Value)];
        Dictionary<Guid, string> trips = await dbContext.TravelRequests.AsNoTracking().Where(row => tripIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Number, cancellationToken);
        IReadOnlyDictionary<Guid, ProjectSummary> projectNames = await projects.GetManyAsync(claims.Where(row => row.ProjectId != null).Select(row => row.ProjectId!.Value).Distinct(), cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(claims.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
        Guid? me = currentUser.EmployeeId;
        return [.. claims.Select(row => new ClaimItem(
            row.Id, row.Number, row.EmployeeId, names.GetValueOrDefault(row.EmployeeId), row.Title, row.TravelRequestId,
            row.TravelRequestId is { } trip ? trips.GetValueOrDefault(trip) : null, row.ProjectId,
            row.ProjectId is { } project && projectNames.TryGetValue(project, out ProjectSummary? summary) ? summary.Name : null, row.Currency, row.Total,
            lines.TryGetValue(row.Id, out var count) ? count.Count : 0, count?.Flags ?? 0, row.Status, row.WorkflowInstanceId, row.SubmittedDate, row.PaymentMode,
            row.PaymentReference, row.PaidOn, row.CreatedDate, row.EmployeeId == me && row.Status is ClaimStatus.Draft or ClaimStatus.Rejected))];
    }

    private async Task<List<TravelItem>> TripItemsAsync(IReadOnlyList<TravelRequest> trips, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. trips.Select(row => row.Id)];
        Dictionary<Guid, decimal> claimed = await dbContext.ExpenseClaims.AsNoTracking()
            .Where(row => row.TravelRequestId != null && ids.Contains(row.TravelRequestId.Value) && (row.Status == ClaimStatus.Approved || row.Status == ClaimStatus.Paid || row.Status == ClaimStatus.PendingApproval))
            .GroupBy(row => row.TravelRequestId!.Value).Select(group => new { group.Key, Total = group.Sum(row => row.Total) })
            .ToDictionaryAsync(row => row.Key, row => row.Total, cancellationToken);
        IReadOnlyDictionary<Guid, ProjectSummary> projectNames = await projects.GetManyAsync(trips.Where(row => row.ProjectId != null).Select(row => row.ProjectId!.Value).Distinct(), cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(trips.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
        Guid? me = currentUser.EmployeeId;
        return [.. trips.Select(row => new TravelItem(
            row.Id, row.Number, row.EmployeeId, names.GetValueOrDefault(row.EmployeeId), row.Purpose, row.FromCity, row.ToCity, row.DepartOn, row.ReturnOn, row.Mode,
            row.EstimatedCost, row.AdvanceRequested, row.ProjectId, row.ProjectId is { } project && projectNames.TryGetValue(project, out ProjectSummary? summary) ? summary.Name : null,
            row.Status, row.WorkflowInstanceId, row.AdvancePaid, row.AdvancePaidOn, claimed.GetValueOrDefault(row.Id), row.SettlementAmount, row.SettledOn,
            row.EmployeeId == me && row.Status is TravelStatus.Draft or TravelStatus.Rejected))];
    }
}

/// <summary>Receipts and supporting documents on a claim follow the claim's own access.</summary>
internal sealed class ClaimDocumentAccess(AppDbContext dbContext, IEmployeeDirectory employees, IPermissionProvider permissions, ICurrentUser currentUser) : IDocumentOwnerAccess
{
    public string OwnerType => "ExpenseClaim";

    public async Task<bool> CanReadAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        ExpenseClaim? claim = await dbContext.ExpenseClaims.AsNoTracking().FirstOrDefaultAsync(row => row.Id == ownerId, cancellationToken);
        return claim is not null && (claim.EmployeeId == currentUser.EmployeeId
            || ((await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Spend.ClaimView) && await employees.IsInScopeAsync(claim.EmployeeId, Permissions.Spend.ClaimView, cancellationToken)));
    }

    public async Task<bool> CanWriteAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => await dbContext.ExpenseClaims.AsNoTracking().AnyAsync(row => row.Id == ownerId && row.EmployeeId == currentUser.EmployeeId && (row.Status == ClaimStatus.Draft || row.Status == ClaimStatus.Rejected), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. ownerIds.Distinct()];
        return await dbContext.ExpenseClaims.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Number, cancellationToken);
    }
}

/// <summary>Open travel advances for other modules.</summary>
internal sealed class ExpenseLedger(AppDbContext dbContext) : IExpenseLedger
{
    public async Task<IReadOnlyList<OpenAdvance>> OpenAdvancesAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => [.. (await dbContext.TravelRequests.AsNoTracking()
                .Where(row => row.EmployeeId == employeeId && row.AdvancePaid != null && row.Status == TravelStatus.Approved)
                .ToListAsync(cancellationToken))
            .Select(row => new OpenAdvance(row.Id, row.Number, row.AdvancePaid ?? 0, row.AdvancePaidOn ?? row.DepartOn))];
}

/// <summary>Seeds a tenant's default expense categories.</summary>
public sealed class ExpenseSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> codes = [.. await dbContext.ExpenseCategories.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Code).ToListAsync(cancellationToken)];
        int added = 0;
        foreach ((string code, string name, decimal? limit, decimal receipt) in ExpenseDefaults.Categories.Where(row => !codes.Contains(row.Code)))
        {
            dbContext.ExpenseCategories.Add(ExpenseCategory.Create(code, name, limit, receipt));
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}
