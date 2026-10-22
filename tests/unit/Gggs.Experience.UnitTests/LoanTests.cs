using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Loans.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

/// <summary>Employee loans: the EMI and its schedule, eligibility, interest at exit and the perquisite.</summary>
public sealed class LoanTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    private static LoanType Personal(decimal rate = 8m, decimal? cap = 500_000m, decimal? multiple = 3m, int service = 12, int maxActive = 1, decimal emiCap = 40m)
        => LoanType.Create(new LoanTypeDetails("personal", "Personal loan", null, rate, 24, cap, multiple, service, maxActive, emiCap, false));

    [Fact]
    public void The_emi_is_the_reducing_balance_instalment_and_equal_principal_when_interest_free()
    {
        LoanMath.Emi(100_000m, 8m, 12).ShouldBe(8699m);
        LoanMath.Emi(10_000m, 0m, 3).ShouldBe(3333m);
        LoanMath.Emi(0m, 8m, 12).ShouldBe(0m);
    }

    [Fact]
    public void The_schedule_repays_the_principal_exactly_with_the_last_month_taking_the_rounding()
    {
        IReadOnlyList<ScheduledInstalment> free = LoanMath.Schedule(10_000m, 0m, 3, October);
        free.Select(row => row.Amount).ShouldBe([3333m, 3333m, 3334m]);
        free.Select(row => row.Month).ShouldBe([October, October.AddMonths(1), October.AddMonths(2)]);

        IReadOnlyList<ScheduledInstalment> paid = LoanMath.Schedule(100_000m, 8m, 12, October);
        paid.Count.ShouldBe(12);
        paid.Sum(row => row.Principal).ShouldBe(100_000m);
        paid[0].Interest.ShouldBe(667m);
        paid[^1].BalanceAfter.ShouldBe(0m);
        paid.Take(11).ShouldAllBe(row => row.Amount == 8699m);
        Math.Abs(paid[^1].Amount - 8699m).ShouldBeLessThan(5m);
        paid.Select(row => row.Interest).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public void Interest_accrues_by_the_day_and_the_perquisite_is_the_gap_to_the_benchmark()
    {
        LoanMath.AccruedInterest(50_000m, 8m, October, October.AddDays(30)).ShouldBe(329m);
        LoanMath.AccruedInterest(50_000m, 0m, October, October.AddDays(30)).ShouldBe(0m);
        LoanMath.AccruedInterest(50_000m, 8m, October, October).ShouldBe(0m);

        LoanMath.Perquisite(100_000m, 0m, 9.25m).ShouldBe(771m);
        LoanMath.Perquisite(100_000m, 8m, 9.25m).ShouldBe(104m);
        LoanMath.Perquisite(100_000m, 10m, 9.25m).ShouldBe(0m, "charging at least the benchmark gives no perquisite");
    }

    [Fact]
    public void The_limit_is_the_lower_of_the_cap_and_the_gross_multiple()
    {
        Personal().Limit(100_000m).ShouldBe(300_000m);
        Personal().Limit(250_000m).ShouldBe(500_000m);
        Personal(multiple: null).Limit(null).ShouldBe(500_000m);
        Personal(cap: null).Limit(null).ShouldBe(0m, "a gross multiple needs a salary");
        Should.Throw<DomainRuleException>(() => Personal(cap: null, multiple: null)).ErrorCode.ShouldBe("LOAN_TYPE_LIMIT");
        Should.Throw<DomainRuleException>(() => Personal(rate: 31m)).ErrorCode.ShouldBe("LOAN_TYPE_TERMS");
    }

    [Fact]
    public void Eligibility_checks_service_open_loans_tenure_amount_and_the_instalment_cap()
    {
        LoanType type = Personal();
        type.Ineligibility(100_000m, 12, 24, 60_000m, 0, 0).ShouldBeNull();
        type.Ineligibility(100_000m, 12, 6, 60_000m, 0, 0).ShouldContain("12 month(s) of service");
        type.Ineligibility(100_000m, 12, 24, 60_000m, 1, 0).ShouldContain("already have 1");
        type.Ineligibility(100_000m, 36, 24, 60_000m, 0, 0).ShouldContain("1 to 24 months");
        type.Ineligibility(200_000m, 24, 24, 60_000m, 0, 0).ShouldContain("up to 180,000");

        // 40% of 60,000 is 24,000: an 8,699 instalment fits alone but not on top of 16,000 already repaid.
        type.Ineligibility(100_000m, 12, 24, 60_000m, 0, 16_000m).ShouldContain("more than 40%");
    }

    [Fact]
    public void A_loan_moves_from_application_to_closure_and_refuses_steps_out_of_order()
    {
        LoanType type = Personal();
        var loan = Loan.Apply("LN-2026-00001", Guid.NewGuid(), type, 100_000m, 12, "Wedding", October);
        loan.Emi.ShouldBe(8699m);
        Should.Throw<DomainRuleException>(() => loan.Disburse(DisbursementMode.Bank, "UTR1", October, October)).ErrorCode.ShouldBe("LOAN_STATE");

        loan.Decide(approved: true, null, DateTime.UtcNow);
        Should.Throw<DomainRuleException>(() => loan.Disburse(DisbursementMode.Bank, null, October, October)).ErrorCode.ShouldBe("LOAN_REFERENCE");
        Should.Throw<DomainRuleException>(() => loan.Disburse(DisbursementMode.Payroll, null, October, October.AddMonths(-1))).ErrorCode.ShouldBe("LOAN_FIRST_EMI");
        loan.Disburse(DisbursementMode.Payroll, null, new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 20));
        loan.FirstEmiMonth.ShouldBe(new DateOnly(2026, 11, 1));
        loan.Outstanding.ShouldBe(100_000m);
        Should.Throw<DomainRuleException>(() => loan.Cancel()).ErrorCode.ShouldBe("LOAN_STATE");

        loan.Repay(30_000m).ShouldBe(70_000m);
        loan.Repay(90_000m).ShouldBe(0m, "never below zero");
        loan.Close(October.AddMonths(3), "Repaid in full");
        loan.Status.ShouldBe(LoanStatus.Closed);
        loan.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void An_instalment_settles_once()
    {
        LoanType type = Personal(rate: 0m);
        var loan = Loan.Apply("LN-2026-00002", Guid.NewGuid(), type, 9_000m, 3, "Advance", October);
        LoanInstalment instalment = LoanInstalment.From(loan, LoanMath.Schedule(9_000m, 0m, 3, October)[0]);
        instalment.Amount.ShouldBe(3000m);
        instalment.MarkRaised();
        instalment.Settle(InstalmentStatus.Deducted, October.AddDays(30));
        instalment.Settle(InstalmentStatus.Prepaid, October.AddDays(40));
        instalment.Status.ShouldBe(InstalmentStatus.Deducted);
        instalment.SettledOn.ShouldBe(October.AddDays(30));
    }
}
