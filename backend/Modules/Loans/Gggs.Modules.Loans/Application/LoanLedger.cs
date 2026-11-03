using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Loans.Contracts;
using Gggs.Modules.Loans.Domain;
using Gggs.Modules.Loans.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Loans.Application;

/// <summary>
/// Loans at exit (Separation): what is owed, the settlement taking recovery over from monthly
/// payroll, and the settlement's payment closing the loans.
/// </summary>
internal sealed class LoanLedger(AppDbContext dbContext, LoanService loans) : ILoanLedger
{
    public async Task<IReadOnlyList<LoanOutstanding>> OutstandingAsync(Guid employeeId, DateOnly onDay, CancellationToken cancellationToken = default)
    {
        List<Loan> active = await dbContext.Loans.AsNoTracking().Where(row => row.EmployeeId == employeeId && row.Status == LoanStatus.Active && row.Outstanding > 0).OrderBy(row => row.Number).ToListAsync(cancellationToken);
        if (active.Count == 0)
        {
            return [];
        }

        List<Guid> ids = [.. active.Select(row => row.Id)];
        Dictionary<Guid, DateOnly> lastDeducted = await dbContext.LoanInstalments.AsNoTracking()
            .Where(row => ids.Contains(row.LoanId) && row.Status == InstalmentStatus.Deducted)
            .GroupBy(row => row.LoanId).Select(group => new { group.Key, Month = group.Max(row => row.Month) })
            .ToDictionaryAsync(row => row.Key, row => row.Month, cancellationToken);
        Dictionary<Guid, string> types = await dbContext.LoanTypes.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);

        // Each instalment carries its month's interest, so interest runs from the month after the last one deducted.
        return [.. active.Select(loan =>
        {
            DateOnly from = lastDeducted.TryGetValue(loan.Id, out DateOnly month) ? month.AddMonths(1) : loan.DisbursedOn ?? onDay;
            return new LoanOutstanding(loan.Id, loan.Number, types.GetValueOrDefault(loan.LoanTypeId) ?? "Loan", loan.Outstanding, LoanMath.AccruedInterest(loan.Outstanding, loan.InterestRate, from, onDay));
        })];
    }

    public async Task<int> HandOverToSettlementAsync(Guid employeeId, Guid settlementId, CancellationToken cancellationToken = default)
    {
        List<Loan> active = await dbContext.Loans.Where(row => row.EmployeeId == employeeId && row.Status == LoanStatus.Active && (row.SettlementId == null || row.SettlementId == settlementId)).ToListAsync(cancellationToken);
        foreach (Loan loan in active)
        {
            await loans.FreeScheduleAsync(loan, cancellationToken);
            loan.HandOver(settlementId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return active.Count;
    }

    public async Task<int> TakeBackFromSettlementAsync(Guid settlementId, CancellationToken cancellationToken = default)
    {
        List<Loan> handed = await dbContext.Loans.Where(row => row.SettlementId == settlementId && row.Status == LoanStatus.Active).ToListAsync(cancellationToken);
        foreach (Loan loan in handed)
        {
            loan.TakeBack();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await loans.RaiseDueAsync(cancellationToken);
        return handed.Count;
    }

    public async Task<int> SettledAsync(Guid settlementId, IReadOnlyDictionary<Guid, decimal> recovered, DateOnly paidOn, string? reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recovered);
        List<Loan> handed = await dbContext.Loans.Where(row => row.SettlementId == settlementId && row.Status == LoanStatus.Active).ToListAsync(cancellationToken);
        int closed = 0;
        foreach (Loan loan in handed)
        {
            decimal amount = LoanMath.Rupees(recovered.GetValueOrDefault(loan.Id));
            if (amount > 0)
            {
                dbContext.LoanRepayments.Add(LoanRepayment.Record(loan.Id, RepaymentKind.Settlement, amount, paidOn, reference, "Recovered in the full and final settlement"));
            }

            // What the settlement recovered goes to principal first; anything over it was the accrued interest.
            decimal left = amount > 0 ? loan.Repay(Math.Min(amount, loan.Outstanding)) : loan.Outstanding;
            if (left <= 0)
            {
                foreach (LoanInstalment instalment in await dbContext.LoanInstalments.Where(row => row.LoanId == loan.Id && row.Status == InstalmentStatus.Scheduled).ToListAsync(cancellationToken))
                {
                    instalment.Settle(InstalmentStatus.RecoveredAtExit, paidOn);
                }

                loan.Close(paidOn, "Recovered in the full and final settlement");
                closed++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return closed;
    }
}

/// <summary>The signed-in employee's loans for the assistant.</summary>
internal sealed class MyLoansTool(LoanService loans) : IAiTool
{
    public string Name => "my_loans";

    public string Description => "The signed-in employee's own loans and salary advances: each loan's status, amount, interest, instalment, what is still owed and the next deduction, plus which loan types they could apply for and up to how much. Use when they ask about a loan, an advance, an EMI or borrowing from the company.";

    public JsonElement InputSchema { get; } = AiJson.Schema();

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Self.LoanApply;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        MyLoans mine = await loans.MineAsync(cancellationToken);
        return AiToolOutput.Data(new
        {
            mine.Outstanding,
            MonthlyInstalments = mine.MonthlyEmi,
            Loans = mine.Loans.Select(loan => new
            {
                loan.Number,
                Type = loan.TypeName,
                Status = Loan.Label(loan.Status),
                loan.Principal,
                loan.InterestRate,
                loan.TenureMonths,
                Instalment = loan.Emi,
                loan.Outstanding,
                loan.InstalmentsLeft,
                NextDeduction = loan.NextInstalmentMonth is { } month ? new { Month = month.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), Amount = loan.NextInstalmentAmount } : null,
            }),
            CanApplyFor = mine.Offers.Select(offer => new { offer.Type.Name, offer.Type.InterestRate, offer.Type.MaxTenureMonths, UpTo = offer.Limit, NotEligibleBecause = offer.Problem }),
        });
    }
}
