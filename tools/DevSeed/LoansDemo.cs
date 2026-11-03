using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Loans.Application;
using Gggs.Modules.Loans.Domain;
using Gggs.Modules.Payroll.Application;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.DevSeed;

/// <summary>
/// Loans demo: a personal loan two instalments in, an interest-free medical loan starting this
/// month, a salary advance paid with this month's salary (to the administrator, Priya, so "My
/// loans" has something), and a personal loan approved and waiting to be paid out. This month's
/// open payroll run is recalculated so the instalments show on it.
/// </summary>
internal static class LoansDemo
{
    public static async Task SeedAsync(IServiceProvider scope)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        if (await db.Set<Loan>().AnyAsync())
        {
            Console.WriteLine("Loans demo already present.");
            return;
        }

        IReadOnlyDictionary<string, Guid> codes = await scope.GetRequiredService<IEmployeeDirectory>().ResolveCodesAsync(["EMP00002", "EMP00003", "EMP00006", "EMP00007"]);
        if (codes.Count < 4)
        {
            Console.WriteLine("Demo employees missing; skipping the loans demo.");
            return;
        }

        LoanService loans = scope.GetRequiredService<LoanService>();
        Dictionary<string, Guid> types = (await loans.TypesAsync(CancellationToken.None)).ToDictionary(type => type.Code, type => type.Id);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly thisMonth = new(today.Year, today.Month, 1);

        async Task<Guid> ApplyAsync(string code, string type, decimal amount, int months, string purpose)
            => (await loans.ApplyAsync(new ApplyBody(types[type], amount, months, purpose, codes[code]), CancellationToken.None)).Loan.Id;

        // Meera's personal loan was paid out in July; August and September were deducted before
        // loans were tracked here, so they are recorded as history and October goes to payroll.
        Guid meera = await ApplyAsync("EMP00003", "PERSONAL", 150_000m, 18, "Home renovation");
        DateOnly paidOut = thisMonth.AddMonths(-3).AddDays(5);
        await loans.DisburseAsync(meera, new DisburseBody(DisbursementMode.Bank, "UTR-HDFC-55120931", paidOut, thisMonth.AddMonths(-2)), CancellationToken.None);
        Loan meeraLoan = await db.Set<Loan>().FirstAsync(row => row.Id == meera);
        await loans.FreeScheduleAsync(meeraLoan, CancellationToken.None);
        await db.SaveChangesAsync();
        List<Guid> history = await db.Set<LoanInstalment>().Where(row => row.LoanId == meera && row.Month < thisMonth).Select(row => row.Id).ToListAsync();
        await loans.DeductedAsync(history, thisMonth.AddDays(-1), CancellationToken.None);
        await loans.RaiseDueAsync(CancellationToken.None);

        // Rohan's interest-free medical loan, transferred last month, repaid from this month.
        Guid rohan = await ApplyAsync("EMP00006", "MEDICAL", 80_000m, 10, "Father's knee surgery");
        await loans.DisburseAsync(rohan, new DisburseBody(DisbursementMode.Bank, "UTR-HDFC-55188410", thisMonth.AddDays(-18), thisMonth), CancellationToken.None);

        // Priya's salary advance comes with this month's salary and is recovered over three months.
        Guid priya = await ApplyAsync("EMP00007", "SALARY_ADVANCE", 60_000m, 3, "Moving house");
        await loans.DisburseAsync(priya, new DisburseBody(DisbursementMode.Payroll, null, today, null), CancellationToken.None);

        // Vikram's loan is approved and waiting for finance to pay it out.
        await ApplyAsync("EMP00002", "PERSONAL", 200_000m, 24, "Car down payment");

        // This month's open run picks up the instalments and the advance.
        PayrollRun? open = await db.Set<PayrollRun>().AsNoTracking()
            .Where(run => run.Kind == PayrollRunKind.Regular && run.Period == thisMonth && run.Status != RunStatus.Cancelled && run.Status != RunStatus.Finalized && run.Status != RunStatus.Paid)
            .FirstOrDefaultAsync();
        if (open is not null)
        {
            await scope.GetRequiredService<RunService>().CalculateAsync(open.Id, CancellationToken.None);
        }

        Console.WriteLine($"Loans demo: 4 loans (2 being repaid, 1 advance with this month's salary, 1 to pay out){(open is null ? string.Empty : "; this month's payroll recalculated")}.");
    }
}
