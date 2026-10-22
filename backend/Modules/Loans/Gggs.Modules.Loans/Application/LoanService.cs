using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Loans.Domain;
using Gggs.Modules.Loans.Infrastructure;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Loans.Application;

public sealed record LoanTypeItem(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal InterestRate,
    int MaxTenureMonths,
    decimal? MaxAmount,
    decimal? MaxGrossMultiple,
    int MinServiceMonths,
    int MaxActive,
    decimal MaxEmiPercentOfGross,
    bool PerquisiteExempt,
    bool IsActive);

public sealed record LoanTypeBody(LoanTypeDetails Details, bool IsActive = true);

/// <summary>A loan type as offered to one employee: how much they could borrow, or why they cannot.</summary>
public sealed record LoanOffer(LoanTypeItem Type, decimal Limit, string? Problem);

public sealed record LoanItem(
    Guid Id,
    string Number,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    Guid TypeId,
    string TypeName,
    decimal Principal,
    decimal InterestRate,
    int TenureMonths,
    decimal Emi,
    string Purpose,
    LoanStatus Status,
    DateOnly AppliedOn,
    Guid? WorkflowInstanceId,
    string? DecisionNote,
    DisbursementMode? DisbursementMode,
    string? DisbursementReference,
    DateOnly? DisbursedOn,
    DateOnly? FirstEmiMonth,
    decimal Outstanding,
    int InstalmentsLeft,
    DateOnly? NextInstalmentMonth,
    decimal? NextInstalmentAmount,
    Guid? SettlementId,
    DateOnly? ClosedOn,
    string? CloseNote,
    bool CanCancel);

public sealed record InstalmentItem(Guid Id, int Sequence, DateOnly Month, decimal Principal, decimal Interest, decimal Amount, InstalmentStatus Status, bool Raised, DateOnly? SettledOn);

public sealed record RepaymentItem(Guid Id, RepaymentKind Kind, decimal Amount, DateOnly Date, string? Reference, string? Note);

public sealed record LoanView(LoanItem Loan, IReadOnlyList<InstalmentItem> Schedule, IReadOnlyList<RepaymentItem> Repayments);

public sealed record MyLoans(IReadOnlyList<LoanItem> Loans, IReadOnlyList<LoanOffer> Offers, decimal Outstanding, decimal MonthlyEmi);

public sealed record ApplyBody(Guid TypeId, decimal Amount, int TenureMonths, string Purpose, Guid? EmployeeId = null);

public sealed record QuoteBody(Guid TypeId, decimal Amount, int TenureMonths);

public sealed record LoanQuote(decimal Limit, decimal Emi, decimal TotalInterest, string? Problem, IReadOnlyList<ScheduledInstalment> Schedule);

public sealed record DisburseBody(DisbursementMode Mode, string? Reference, DateOnly? DisbursedOn, DateOnly? FirstEmiMonth);

public sealed record PrepayBody(decimal Amount, DateOnly? Date, string? Reference, string? Note);

public sealed record WriteOffBody(string Reason);

public sealed record LoanQuery
{
    public LoanStatus? Status { get; init; }

    public Guid? EmployeeId { get; init; }
}

public sealed record LoanSummary(int PendingApproval, int ToDisburse, int Active, decimal Outstanding, decimal DueThisMonth, decimal RecoveredThisMonth, IReadOnlyDictionary<string, decimal> OutstandingByType);

/// <summary>
/// Employee loans and salary advances: types and their eligibility rules, applications approved
/// through the workflow, disbursement (with salary or by transfer), a reducing-balance schedule whose
/// instalments payroll deducts as they fall due, prepayment and write-off.
/// </summary>
internal sealed class LoanService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IPayrollSalaries salaries,
    IPayrollInputs payrollInputs,
    IWorkflowStarter workflow,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    LoanSeeder seeder,
    TimeProvider timeProvider)
{
    public const string SubjectType = "loans.application";
    public const string InstalmentSource = "loans.instalment";
    public const string DisbursementSource = "loans.disbursement";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Approve a loan or salary advance",
        "loans",
        [
            new WorkflowSubjectField("amount", "Amount", WorkflowFieldType.Number),
            new WorkflowSubjectField("tenureMonths", "Tenure in months", WorkflowFieldType.Number),
            new WorkflowSubjectField("type", "Loan type code", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Types --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<LoanTypeItem>> TypesAsync(CancellationToken cancellationToken)
    {
        await seeder.EnsureSeededAsync(cancellationToken);
        return [.. (await dbContext.LoanTypes.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken)).Select(TypeItem)];
    }

    public async Task<LoanTypeItem> SaveTypeAsync(Guid? id, LoanTypeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = body.Details.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (await dbContext.LoanTypes.AnyAsync(row => row.Code == code && row.Id != id, cancellationToken))
        {
            throw new DomainRuleException("LOAN_TYPE_CODE", $"A loan type with code {code} already exists.");
        }

        LoanType type;
        if (id is { } existing)
        {
            type = await dbContext.LoanTypes.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(LoanType), existing);
            type.Update(body.Details, body.IsActive);
        }
        else
        {
            type = LoanType.Create(body.Details);
            if (!body.IsActive)
            {
                type.Update(body.Details, active: false);
            }

            dbContext.LoanTypes.Add(type);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TypeItem(type);
    }

    // ----- Applying -----------------------------------------------------------------------------

    public async Task<MyLoans> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        List<Loan> loans = await dbContext.Loans.AsNoTracking().Where(row => row.EmployeeId == me).OrderByDescending(row => row.CreatedDate).ToListAsync(cancellationToken);
        List<LoanItem> items = await ItemsAsync(loans, cancellationToken);
        Applicant applicant = await ApplicantAsync(me, cancellationToken);
        List<LoanOffer> offers = [];
        foreach (LoanType type in await ActiveTypesAsync(cancellationToken))
        {
            decimal limit = type.Limit(applicant.MonthlyGross);
            // Could they borrow anything at all? The amount and tenure are checked when they apply.
            string? problem = type.Ineligibility(1, 1, applicant.ServiceMonths, applicant.MonthlyGross, applicant.ActiveOfType(type.Id), 0);
            offers.Add(new LoanOffer(TypeItem(type), limit, problem));
        }

        List<LoanItem> active = [.. items.Where(item => item.Status == LoanStatus.Active)];
        return new MyLoans(items, offers, active.Sum(item => item.Outstanding), active.Sum(item => item.Emi));
    }

    /// <summary>What an application would look like - the limit, the instalment and its schedule - without making it.</summary>
    public async Task<LoanQuote> QuoteAsync(QuoteBody body, Guid? employeeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid person = employeeId ?? Me();
        LoanType type = await dbContext.LoanTypes.AsNoTracking().FirstOrDefaultAsync(row => row.Id == body.TypeId, cancellationToken) ?? throw new NotFoundException(nameof(LoanType), body.TypeId);
        Applicant applicant = await ApplicantAsync(person, cancellationToken);
        string? problem = type.Ineligibility(body.Amount, body.TenureMonths, applicant.ServiceMonths, applicant.MonthlyGross, applicant.ActiveOfType(type.Id), applicant.Emis);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyList<ScheduledInstalment> schedule = body.Amount > 0 && body.TenureMonths is > 0 and <= 120
            ? LoanMath.Schedule(LoanMath.Rupees(body.Amount), type.InterestRate, body.TenureMonths, new DateOnly(today.Year, today.Month, 1).AddMonths(1))
            : [];
        return new LoanQuote(type.Limit(applicant.MonthlyGross), LoanMath.Emi(LoanMath.Rupees(body.Amount), type.InterestRate, body.TenureMonths), schedule.Sum(row => row.Interest), problem, schedule);
    }

    /// <summary>Applies for a loan - for oneself, or (with loan management) for an employee - and sends it for approval.</summary>
    public async Task<LoanView> ApplyAsync(ApplyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid person = body.EmployeeId ?? Me();
        if (body.EmployeeId is { } other && other != currentUser.EmployeeId
            && !((await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Loan.Manage) && await employees.IsInScopeAsync(other, Permissions.Loan.Manage, cancellationToken)))
        {
            throw new NotFoundException("Employee", other);
        }

        if (!await employees.IsActiveAsync(person, cancellationToken))
        {
            throw new DomainRuleException("LOAN_EMPLOYEE", "Loans are for active employees.");
        }

        LoanType type = await dbContext.LoanTypes.AsNoTracking().FirstOrDefaultAsync(row => row.Id == body.TypeId, cancellationToken) ?? throw new NotFoundException(nameof(LoanType), body.TypeId);
        Applicant applicant = await ApplicantAsync(person, cancellationToken);
        if (type.Ineligibility(body.Amount, body.TenureMonths, applicant.ServiceMonths, applicant.MonthlyGross, applicant.ActiveOfType(type.Id), applicant.Emis) is { } problem)
        {
            throw new DomainRuleException("LOAN_INELIGIBLE", problem);
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        string number = await NextNumberAsync(today, cancellationToken);
        var loan = Loan.Apply(number, person, type, body.Amount, body.TenureMonths, body.Purpose, today);
        dbContext.Loans.Add(loan);
        await dbContext.SaveChangesAsync(cancellationToken);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = loan.Id,
                Title = $"{loan.Number}: {type.Name} of {Amount(loan.Principal)} over {loan.TenureMonths} months",
                RequesterEmployeeId = person,
                Link = $"/loans/{loan.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["amount"] = loan.Principal.ToString(CultureInfo.InvariantCulture),
                    ["tenureMonths"] = loan.TenureMonths.ToString(CultureInfo.InvariantCulture),
                    ["type"] = type.Code,
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            loan.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            loan.Decide(approved: true, "Approved automatically", Now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(loan, cancellationToken);
    }

    public async Task<LoanView> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        Loan loan = await dbContext.Loans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Loan), id);
        if (loan.EmployeeId != currentUser.EmployeeId && !await CanManageAsync(loan.EmployeeId, cancellationToken))
        {
            throw new NotFoundException(nameof(Loan), id);
        }

        if (loan.Status == LoanStatus.PendingApproval)
        {
            await workflow.WithdrawAsync(SubjectType, loan.Id, cancellationToken);
        }

        loan.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(loan, cancellationToken);
    }

    public async Task DecideAsync(Guid id, bool approved, CancellationToken cancellationToken)
    {
        Loan? loan = await dbContext.Loans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (loan?.Status != LoanStatus.PendingApproval)
        {
            return;
        }

        loan.Decide(approved, null, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAsync(loan, approved ? "approved" : "not approved", cancellationToken);
    }

    // ----- Managing -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<LoanItem>> ListAsync(LoanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Loan> rows = await VisibleAsync(cancellationToken);
        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.EmployeeId is { } person)
        {
            rows = rows.Where(row => row.EmployeeId == person);
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<LoanSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        List<Loan> loans = await (await VisibleAsync(cancellationToken)).Where(row => row.Status == LoanStatus.PendingApproval || row.Status == LoanStatus.Approved || row.Status == LoanStatus.Active).ToListAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly month = new(today.Year, today.Month, 1);
        List<Guid> ids = [.. loans.Select(row => row.Id)];
        List<LoanInstalment> thisMonth = await dbContext.LoanInstalments.AsNoTracking()
            .Where(row => row.Month == month && (row.Status == InstalmentStatus.Scheduled || row.Status == InstalmentStatus.Deducted))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> typeNames = await dbContext.LoanTypes.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        List<Loan> active = [.. loans.Where(row => row.Status == LoanStatus.Active)];
        return new LoanSummary(
            loans.Count(row => row.Status == LoanStatus.PendingApproval),
            loans.Count(row => row.Status == LoanStatus.Approved),
            active.Count,
            active.Sum(row => row.Outstanding),
            thisMonth.Where(row => ids.Contains(row.LoanId) && row.Status == InstalmentStatus.Scheduled).Sum(row => row.Amount),
            thisMonth.Where(row => row.Status == InstalmentStatus.Deducted).Sum(row => row.Amount),
            active.GroupBy(row => typeNames.GetValueOrDefault(row.LoanTypeId) ?? "Other").ToDictionary(group => group.Key, group => group.Sum(row => row.Outstanding)));
    }

    public async Task<LoanView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Loan loan = await (await VisibleAsync(cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Loan), id);
        return await ViewAsync(loan, cancellationToken);
    }

    /// <summary>Pays an approved loan out - with the next salary, or by a transfer already made - and lays out its schedule.</summary>
    public async Task<LoanView> DisburseAsync(Guid id, DisburseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Loan loan = await ManagedAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly on = body.DisbursedOn ?? today;
        DateOnly firstEmi = body.FirstEmiMonth ?? new DateOnly(on.Year, on.Month, 1).AddMonths(1);
        string? reference = body.Reference;
        if (body.Mode == DisbursementMode.Payroll)
        {
            await payrollInputs.AddAsync(
                new PayrollInputRequest(loan.EmployeeId, new DateOnly(on.Year, on.Month, 1), PayrollInputKind.LoanDisbursement, loan.Principal, $"Loan {loan.Number} paid out", DisbursementSource, loan.Id),
                cancellationToken);
            reference ??= $"With {on:MMMM yyyy} salary";
        }

        loan.Disburse(body.Mode, reference, on, firstEmi);
        foreach (ScheduledInstalment line in LoanMath.Schedule(loan.Principal, loan.InterestRate, loan.TenureMonths, firstEmi))
        {
            dbContext.LoanInstalments.Add(LoanInstalment.From(loan, line));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RaiseDueAsync(cancellationToken);
        await NotifyAsync(loan, body.Mode == DisbursementMode.Payroll ? "sent to payroll for payment" : "paid out", cancellationToken);
        return await ViewAsync(loan, cancellationToken);
    }

    /// <summary>
    /// Records money the employee paid back early. What is left is spread over the same number of
    /// remaining months (a smaller instalment); paying it all closes the loan.
    /// </summary>
    public async Task<LoanView> PrepayAsync(Guid id, PrepayBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Loan loan = await ManagedAsync(id, cancellationToken);
        if (loan.Status != LoanStatus.Active)
        {
            throw new DomainRuleException("LOAN_STATE", "Only a loan being repaid takes prepayments.");
        }

        if (body.Amount <= 0 || LoanMath.Rupees(body.Amount) > loan.Outstanding)
        {
            throw new DomainRuleException("LOAN_PREPAYMENT", $"A prepayment is more than zero and at most the {Amount(loan.Outstanding)} outstanding.");
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly on = body.Date ?? today;
        decimal amount = LoanMath.Rupees(body.Amount);
        dbContext.LoanRepayments.Add(LoanRepayment.Record(loan.Id, RepaymentKind.Prepayment, amount, on, body.Reference, body.Note));
        (List<LoanInstalment> open, decimal locked) = await FreeScheduleAsync(loan, cancellationToken);
        decimal left = loan.Repay(amount);
        decimal toSchedule = left - locked;
        if (toSchedule <= 0 || loan.SettlementId is not null)
        {
            foreach (LoanInstalment instalment in open)
            {
                instalment.Settle(InstalmentStatus.Prepaid, on);
            }

            if (left <= 0)
            {
                loan.Close(on, "Prepaid in full");
            }
        }
        else if (open.Count > 0)
        {
            foreach (LoanInstalment instalment in open)
            {
                instalment.Settle(InstalmentStatus.Superseded, on);
            }

            IReadOnlyList<ScheduledInstalment> schedule = LoanMath.Schedule(toSchedule, loan.InterestRate, open.Count, open[0].Month, open[0].Sequence);
            foreach (ScheduledInstalment line in schedule)
            {
                dbContext.LoanInstalments.Add(LoanInstalment.From(loan, line));
            }

            loan.Reschedule(schedule.Count > 0 ? schedule[0].Amount : 0);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RaiseDueAsync(cancellationToken);
        return await ViewAsync(loan, cancellationToken);
    }

    public async Task<LoanView> WriteOffAsync(Guid id, WriteOffBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Loan loan = await ManagedAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        (List<LoanInstalment> open, _) = await FreeScheduleAsync(loan, cancellationToken);
        foreach (LoanInstalment instalment in open)
        {
            instalment.Settle(InstalmentStatus.WrittenOff, today);
        }

        if (loan.Outstanding > 0)
        {
            dbContext.LoanRepayments.Add(LoanRepayment.Record(loan.Id, RepaymentKind.WriteOff, loan.Outstanding, today, null, body.Reason));
        }

        loan.WriteOff(today, body.Reason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(loan, cancellationToken);
    }

    // ----- Payroll ------------------------------------------------------------------------------

    /// <summary>
    /// Sends payroll every instalment due by next month that it does not have yet, for loans still
    /// recovered monthly. Safe to repeat. Returns how many were raised.
    /// </summary>
    public async Task<int> RaiseDueAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly horizon = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var due = await (
            from instalment in dbContext.LoanInstalments
            join loan in dbContext.Loans on instalment.LoanId equals loan.Id
            where instalment.Status == InstalmentStatus.Scheduled && !instalment.Raised && instalment.Month <= horizon
                && loan.Status == LoanStatus.Active && loan.SettlementId == null
            orderby instalment.Month
            select new { Instalment = instalment, loan.Number, loan.TenureMonths })
            .ToListAsync(cancellationToken);
        foreach (var row in due)
        {
            await payrollInputs.AddAsync(
                new PayrollInputRequest(row.Instalment.EmployeeId, row.Instalment.Month, PayrollInputKind.LoanRecovery, row.Instalment.Amount,
                    $"Loan {row.Number} instalment {row.Instalment.Sequence} of {row.TenureMonths}", InstalmentSource, row.Instalment.Id),
                cancellationToken);
            row.Instalment.MarkRaised();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    /// <summary>A finalized payroll deducted these instalments: the loans' balances come down, and repaid loans close.</summary>
    public async Task DeductedAsync(IReadOnlyCollection<Guid> instalmentIds, DateOnly on, CancellationToken cancellationToken)
    {
        if (instalmentIds.Count == 0)
        {
            return;
        }

        List<LoanInstalment> instalments = await dbContext.LoanInstalments.Where(row => instalmentIds.Contains(row.Id) && row.Status == InstalmentStatus.Scheduled).ToListAsync(cancellationToken);
        List<Guid> loanIds = [.. instalments.Select(row => row.LoanId).Distinct()];
        Dictionary<Guid, Loan> loans = await dbContext.Loans.Where(row => loanIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (LoanInstalment instalment in instalments)
        {
            instalment.Settle(InstalmentStatus.Deducted, on);
            if (loans.TryGetValue(instalment.LoanId, out Loan? loan) && loan.Status == LoanStatus.Active)
            {
                loan.Repay(instalment.Principal);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        HashSet<Guid> stillScheduled = [.. await dbContext.LoanInstalments.Where(row => loanIds.Contains(row.LoanId) && row.Status == InstalmentStatus.Scheduled).Select(row => row.LoanId).Distinct().ToListAsync(cancellationToken)];
        foreach (Loan loan in loans.Values.Where(loan => loan.Status == LoanStatus.Active && (loan.Outstanding <= 0 || !stillScheduled.Contains(loan.Id)) && loan.SettlementId is null))
        {
            loan.Close(on, "Repaid in full");
            await dbContext.SaveChangesAsync(cancellationToken);
            await NotifyAsync(loan, "repaid in full", cancellationToken);
        }
    }

    // ----- Internals ----------------------------------------------------------------------------

    /// <summary>
    /// Takes the loan's undeducted instalments back from payroll. Returns those now free to change,
    /// and the principal of any payroll can no longer give back (already in a finalized run).
    /// </summary>
    internal async Task<(List<LoanInstalment> Open, decimal LockedPrincipal)> FreeScheduleAsync(Loan loan, CancellationToken cancellationToken)
    {
        List<LoanInstalment> scheduled = await dbContext.LoanInstalments.Where(row => row.LoanId == loan.Id && row.Status == InstalmentStatus.Scheduled).OrderBy(row => row.Sequence).ToListAsync(cancellationToken);
        List<LoanInstalment> open = [];
        decimal locked = 0;
        foreach (LoanInstalment instalment in scheduled)
        {
            if (instalment.Raised && !await payrollInputs.WithdrawAsync(InstalmentSource, instalment.Id, cancellationToken))
            {
                locked += instalment.Principal;
                continue;
            }

            instalment.MarkWithdrawn();
            open.Add(instalment);
        }

        return (open, locked);
    }

    private sealed record Applicant(int ServiceMonths, decimal? MonthlyGross, decimal Emis, IReadOnlyList<Loan> Open)
    {
        public int ActiveOfType(Guid typeId) => Open.Count(loan => loan.LoanTypeId == typeId);
    }

    private async Task<Applicant> ApplicantAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        int service = person?.JoiningDate is { } joined ? Math.Max(0, ((today.Year - joined.Year) * 12) + today.Month - joined.Month - (today.Day < joined.Day ? 1 : 0)) : 0;
        decimal? gross = (await salaries.CurrentAsync([employeeId], today, cancellationToken)).TryGetValue(employeeId, out SalaryInfo? salary) ? salary.MonthlyGross : null;
        List<Loan> open = await dbContext.Loans.AsNoTracking()
            .Where(row => row.EmployeeId == employeeId && (row.Status == LoanStatus.PendingApproval || row.Status == LoanStatus.Approved || row.Status == LoanStatus.Active))
            .ToListAsync(cancellationToken);
        return new Applicant(service, gross, open.Sum(row => row.Emi), open);
    }

    private async Task<List<LoanType>> ActiveTypesAsync(CancellationToken cancellationToken)
    {
        await seeder.EnsureSeededAsync(cancellationToken);
        return await dbContext.LoanTypes.AsNoTracking().Where(row => row.IsActive).OrderBy(row => row.Name).ToListAsync(cancellationToken);
    }

    private async Task<Loan> ManagedAsync(Guid id, CancellationToken cancellationToken)
    {
        Loan loan = await dbContext.Loans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Loan), id);
        return await employees.IsInScopeAsync(loan.EmployeeId, Permissions.Loan.Manage, cancellationToken) ? loan : throw new NotFoundException(nameof(Loan), id);
    }

    private async Task<bool> CanManageAsync(Guid employeeId, CancellationToken cancellationToken)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Loan.Manage) && await employees.IsInScopeAsync(employeeId, Permissions.Loan.Manage, cancellationToken);

    private async Task<IQueryable<Loan>> VisibleAsync(CancellationToken cancellationToken)
    {
        IQueryable<Loan> rows = dbContext.Loans.AsNoTracking();
        IReadOnlyCollection<Guid>? scope = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Loan.View)
            ? await employees.InScopeIdsAsync(Permissions.Loan.View, cancellationToken)
            : [];
        if (scope is null)
        {
            return rows;
        }

        Guid? me = currentUser.EmployeeId;
        return rows.Where(row => row.EmployeeId == me || scope.Contains(row.EmployeeId));
    }

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("LOAN_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<string> NextNumberAsync(DateOnly today, CancellationToken cancellationToken)
    {
        string prefix = $"LN-{today.Year}-";
        List<string> taken = await dbContext.Loans.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Where(row => row.Number.StartsWith(prefix)).Select(row => row.Number).ToListAsync(cancellationToken);
        int next = taken.Select(number => int.TryParse(number[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:00000}";
    }

    private Task NotifyAsync(Loan loan, string status, CancellationToken cancellationToken)
        => notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.LoanUpdate,
                [new NotificationRecipient(loan.EmployeeId)],
                new Dictionary<string, string> { ["number"] = loan.Number, ["amount"] = Amount(loan.Principal), ["status"] = status },
                "/me/loans"),
            cancellationToken);

    private static string Amount(decimal value) => $"INR {value.ToString("N0", CultureInfo.InvariantCulture)}";

    private static LoanTypeItem TypeItem(LoanType row)
        => new(row.Id, row.Code, row.Name, row.Description, row.InterestRate, row.MaxTenureMonths, row.MaxAmount, row.MaxGrossMultiple, row.MinServiceMonths, row.MaxActive, row.MaxEmiPercentOfGross, row.PerquisiteExempt, row.IsActive);

    internal async Task<LoanView> ViewAsync(Loan loan, CancellationToken cancellationToken)
    {
        List<LoanInstalment> schedule = await dbContext.LoanInstalments.AsNoTracking().Where(row => row.LoanId == loan.Id && row.Status != InstalmentStatus.Superseded).OrderBy(row => row.Sequence).ToListAsync(cancellationToken);
        List<LoanRepayment> repayments = await dbContext.LoanRepayments.AsNoTracking().Where(row => row.LoanId == loan.Id).OrderBy(row => row.Date).ToListAsync(cancellationToken);
        return new LoanView(
            (await ItemsAsync([loan], cancellationToken))[0],
            [.. schedule.Select(row => new InstalmentItem(row.Id, row.Sequence, row.Month, row.Principal, row.Interest, row.Amount, row.Status, row.Raised, row.SettledOn))],
            [.. repayments.Select(row => new RepaymentItem(row.Id, row.Kind, row.Amount, row.Date, row.Reference, row.Note))]);
    }

    private async Task<List<LoanItem>> ItemsAsync(IReadOnlyList<Loan> loans, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. loans.Select(row => row.Id)];
        var next = (await dbContext.LoanInstalments.AsNoTracking().Where(row => ids.Contains(row.LoanId) && row.Status == InstalmentStatus.Scheduled).ToListAsync(cancellationToken))
            .GroupBy(row => row.LoanId)
            .ToDictionary(group => group.Key, group => (Count: group.Count(), First: group.MinBy(row => row.Sequence)!));
        Dictionary<Guid, string> types = await dbContext.LoanTypes.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(loans.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(person => person.Id);
        Guid? me = currentUser.EmployeeId;
        return [.. loans.Select(row =>
        {
            bool scheduled = next.TryGetValue(row.Id, out (int Count, LoanInstalment First) upcoming);
            EmployeeSummary? person = people.GetValueOrDefault(row.EmployeeId);
            return new LoanItem(
                row.Id, row.Number, row.EmployeeId, person?.DisplayName, person?.EmployeeCode, row.LoanTypeId, types.GetValueOrDefault(row.LoanTypeId) ?? string.Empty,
                row.Principal, row.InterestRate, row.TenureMonths, row.Emi, row.Purpose, row.Status, row.AppliedOn, row.WorkflowInstanceId, row.DecisionNote,
                row.DisbursementMode, row.DisbursementReference, row.DisbursedOn, row.FirstEmiMonth, row.Outstanding,
                scheduled ? upcoming.Count : 0, scheduled ? upcoming.First.Month : null, scheduled ? upcoming.First.Amount : null,
                row.SettlementId, row.ClosedOn, row.CloseNote,
                row.EmployeeId == me && row.Status is LoanStatus.PendingApproval or LoanStatus.Approved);
        })];
    }
}

/// <summary>Seeds a tenant's default loan types.</summary>
public sealed class LoanSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> codes = [.. await dbContext.LoanTypes.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Code).ToListAsync(cancellationToken)];
        int added = 0;
        foreach (LoanTypeDetails details in LoanDefaults.Types.Where(row => !codes.Contains(row.Code)))
        {
            dbContext.LoanTypes.Add(LoanType.Create(details));
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>Tenants set up before loans existed get the default types on first use.</summary>
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (!await dbContext.LoanTypes.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).AnyAsync(cancellationToken))
        {
            await SeedCurrentTenantAsync(cancellationToken);
        }
    }
}
