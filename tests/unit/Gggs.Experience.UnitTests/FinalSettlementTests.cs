using Gggs.Modules.Separation.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

/// <summary>Separation v2 phase D: statutory full and final settlement rules.</summary>
public sealed class FinalSettlementTests
{
    private static (decimal Years, decimal Amount, bool Eligible) Gratuity(string joined, string lastDay, decimal basic = 30_000m)
        => SettlementMath.Gratuity(basic, DateOnly.Parse(joined, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(lastDay, System.Globalization.CultureInfo.InvariantCulture), 15m, 26m, 5m, 2_000_000m);

    [Fact]
    public void Four_years_and_240_working_days_count_as_five_years()
    {
        // 4 years and 300 calendar days: about 257 working days in the fifth year.
        (decimal years, decimal amount, bool eligible) = Gratuity("2021-01-01", "2025-10-28");
        eligible.ShouldBeTrue();
        years.ShouldBeLessThan(5m);
        amount.ShouldBe(86_538m, "five years of 15/26 x 30,000");

        // 4 years and 200 calendar days: about 171 working days - not continuous service for the fifth year.
        Gratuity("2021-01-01", "2025-07-19").Eligible.ShouldBeFalse();
        Gratuity("2021-01-01", "2026-01-01").Eligible.ShouldBeTrue("five full years");
    }

    [Fact]
    public void Gratuity_is_capped_at_the_ceiling()
        => Gratuity("1990-01-01", "2026-10-21", basic: 300_000m).Amount.ShouldBe(2_000_000m);

    [Theory]
    [InlineData(18_000, "2024-01-15", "2026-10-21", true, 6.71, 3_913)]
    [InlineData(25_000, "2024-01-15", "2026-10-21", false, 0, 0)]
    [InlineData(12_000, "2026-10-10", "2026-10-21", false, 0, 0)]
    [InlineData(6_000, "2026-04-01", "2027-03-31", true, 12, 5_998)]
    public void Statutory_bonus_is_paid_pro_rata_for_the_year_worked(int basic, string joined, string lastDay, bool eligible, decimal months, int amount)
    {
        (decimal worked, decimal bonus, bool qualifies) = SettlementMath.StatutoryBonus(
            basic, DateOnly.Parse(joined, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(lastDay, System.Globalization.CultureInfo.InvariantCulture), 21_000m, 7_000m, 8.33m, 30);
        qualifies.ShouldBe(eligible);
        worked.ShouldBe(months);
        bonus.ShouldBe(amount);
    }

    [Fact]
    public void Terminal_benefits_split_at_their_tax_free_limit()
    {
        SettlementMath.SplitExempt(2_600_000m, 2_500_000m).ShouldBe((2_500_000m, 100_000m));
        SettlementMath.SplitExempt(400_000m, 500_000m).ShouldBe((400_000m, 0m));
        SettlementMath.SplitExempt(-5m, 100m).ShouldBe((0m, 0m));
    }

    [Fact]
    public void The_settlement_falls_due_two_working_days_after_the_last_day()
    {
        var friday = new DateOnly(2026, 10, 30);
        SettlementMath.WorkingDaysAfter(friday, 2, new HashSet<DateOnly>()).ShouldBe(new DateOnly(2026, 11, 2), "Saturday and Monday; Sunday is not a working day");
        SettlementMath.WorkingDaysAfter(friday, 2, new HashSet<DateOnly> { new(2026, 10, 31) }).ShouldBe(new DateOnly(2026, 11, 3), "a holiday on the Saturday pushes it on");
    }

    [Fact]
    public void A_settlement_in_payroll_drops_its_run_when_reopened()
    {
        Settlement settlement = Settlement.Start(Guid.NewGuid());
        Should.Throw<Gggs.BuildingBlocks.Domain.Exceptions.DomainRuleException>(() => settlement.SentToPayroll(Guid.NewGuid())).ErrorCode.ShouldBe("SETTLEMENT_NOT_APPROVED");
        settlement.Approve();
        Guid run = Guid.NewGuid();
        settlement.SentToPayroll(run);
        settlement.PayrollRunId.ShouldBe(run);
        settlement.Reopen();
        settlement.PayrollRunId.ShouldBeNull();
    }
}
