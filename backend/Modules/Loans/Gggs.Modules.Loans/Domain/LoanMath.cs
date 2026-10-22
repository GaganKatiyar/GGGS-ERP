namespace Gggs.Modules.Loans.Domain;

/// <summary>One month of a repayment schedule.</summary>
public sealed record ScheduledInstalment(int Sequence, DateOnly Month, decimal Principal, decimal Interest, decimal BalanceAfter)
{
    public decimal Amount => Principal + Interest;
}

/// <summary>
/// The arithmetic of a loan, in whole rupees: the equated monthly instalment on a reducing balance,
/// the schedule it gives, interest accrued between instalments, and the taxable perquisite on a loan
/// charged less than the benchmark rate (Income-tax Rule 3(7)(i)).
/// </summary>
public static class LoanMath
{
    public static decimal Rupees(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>The monthly instalment: equal principal when interest-free, else the reducing-balance EMI.</summary>
    public static decimal Emi(decimal principal, decimal annualRate, int months)
    {
        if (principal <= 0 || months <= 0)
        {
            return 0;
        }

        if (annualRate <= 0)
        {
            return Rupees(principal / months);
        }

        double rate = (double)annualRate / 1200;
        double growth = Math.Pow(1 + rate, months);
        return Rupees((decimal)((double)principal * rate * growth / (growth - 1)));
    }

    /// <summary>
    /// The schedule from <paramref name="firstMonth"/>: each month's interest on the balance, the rest
    /// of the instalment off the principal, and the last month clearing whatever rounding left.
    /// </summary>
    public static IReadOnlyList<ScheduledInstalment> Schedule(decimal principal, decimal annualRate, int months, DateOnly firstMonth, int firstSequence = 1)
    {
        var schedule = new List<ScheduledInstalment>(Math.Max(months, 0));
        decimal emi = Emi(principal, annualRate, months);
        decimal balance = principal;
        DateOnly month = new(firstMonth.Year, firstMonth.Month, 1);
        for (int i = 0; i < months && balance > 0; i++)
        {
            decimal interest = Rupees(balance * annualRate / 1200);
            decimal toPrincipal = i == months - 1 ? balance : Math.Min(balance, emi - interest);
            balance -= toPrincipal;
            schedule.Add(new ScheduledInstalment(firstSequence + i, month.AddMonths(i), toPrincipal, interest, balance));
        }

        return schedule;
    }

    /// <summary>Simple interest on <paramref name="balance"/> from one day to another (actual/365).</summary>
    public static decimal AccruedInterest(decimal balance, decimal annualRate, DateOnly from, DateOnly to)
    {
        int days = to.DayNumber - from.DayNumber;
        return balance <= 0 || annualRate <= 0 || days <= 0 ? 0 : Rupees(balance * annualRate / 100 * days / 365);
    }

    /// <summary>
    /// A month's taxable perquisite on a concessional loan: the balance at the benchmark rate less the
    /// interest actually charged. Nothing when the loan charges at least the benchmark.
    /// </summary>
    public static decimal Perquisite(decimal balance, decimal chargedRate, decimal benchmarkRate)
        => balance <= 0 || benchmarkRate <= chargedRate ? 0 : Rupees(balance * (benchmarkRate - chargedRate) / 1200);
}
