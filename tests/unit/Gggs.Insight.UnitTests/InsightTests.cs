using Gggs.Modules.Attendance.Application;
using Gggs.Modules.Attendance.Domain;
using Gggs.Modules.Employee.Infrastructure;
using Gggs.Modules.Insight.Application;
using Gggs.Modules.Insight.Domain;
using Gggs.Modules.Payroll.Application;
using Gggs.Modules.Payroll.Domain;
using Shouldly;

namespace Gggs.Insight.UnitTests;

public sealed class CsvParsingTests
{
    [Fact]
    public void Rows_are_keyed_by_header_with_quotes_commas_and_line_breaks_inside_fields()
    {
        string csv = "﻿Name,Note,Amount\r\n\"Mehta, Arjun\",\"Said \"\"hi\"\"\nthen left\",100\r\nKavya,,\r\n\r\n";
        List<Dictionary<string, string>> rows = ImportService.Parse(csv);
        rows.Count.ShouldBe(2);
        rows[0]["name"].ShouldBe("Mehta, Arjun");
        rows[0]["Note"].ShouldBe("Said \"hi\"\nthen left");
        rows[0]["amount"].ShouldBe("100");
        rows[1]["Note"].ShouldBe(string.Empty);
    }

    [Fact]
    public void A_short_row_fills_the_missing_columns_with_blanks()
        => ImportService.Parse("a,b,c\n1")[0]["c"].ShouldBe(string.Empty);

    [Fact]
    public void Nothing_but_a_header_is_no_rows()
        => ImportService.Parse("a,b\n").ShouldBeEmpty();
}

public sealed class SearchScoringTests
{
    [Theory]
    [InlineData("Priya Menon", "priya menon", 1)]
    [InlineData("Priya Menon", "pri", 0.8)]
    [InlineData("Priya Menon", "men", 0.6)]
    [InlineData("Priya Menon", "eno", 0.4)]
    [InlineData("Priya Menon", "xyz", 0)]
    public void Exact_beats_prefix_beats_word_beats_contains(string value, string term, double expected)
        => SearchService.Score(value, term).ShouldBe(expected);
}

public sealed class DigestTests
{
    [Fact]
    public void Weekly_digests_go_on_mondays_and_monthly_on_the_first_once_a_day()
    {
        var weekly = ReportSubscription.Create(Guid.NewGuid(), null, "people", DigestFrequency.Weekly);
        weekly.IsDue(new DateOnly(2026, 10, 19)).ShouldBeTrue();
        weekly.IsDue(new DateOnly(2026, 10, 20)).ShouldBeFalse();
        weekly.Sent(new DateOnly(2026, 10, 19));
        weekly.IsDue(new DateOnly(2026, 10, 19)).ShouldBeFalse();

        var monthly = ReportSubscription.Create(Guid.NewGuid(), null, "executive", DigestFrequency.Monthly);
        monthly.IsDue(new DateOnly(2026, 11, 1)).ShouldBeTrue();
        monthly.IsDue(new DateOnly(2026, 11, 2)).ShouldBeFalse();
    }

    [Fact]
    public void Figures_read_naturally_in_a_digest()
    {
        DigestService.Format(new Modules.Insight.Contracts.Metric("k", "people", "Attrition", 12.34m, Modules.Insight.Contracts.MetricUnit.Percent)).ShouldBe("12.3%");
        DigestService.Format(new Modules.Insight.Contracts.Metric("k", "payroll", "Cost", 1234567m, Modules.Insight.Contracts.MetricUnit.Currency)).ShouldBe("1,234,567");
        DigestService.Format(new Modules.Insight.Contracts.Metric("k", "time", "Hours", 7.5m, Modules.Insight.Contracts.MetricUnit.Hours)).ShouldBe("7.5 h");
    }
}

public sealed class CalendarOccurrenceTests
{
    [Fact]
    public void Yearly_dates_recur_within_the_range_and_a_leap_day_falls_on_the_28th()
    {
        EmployeeCalendar.Occurrences(new DateOnly(1990, 3, 5), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)).ShouldBe([new DateOnly(2026, 3, 5)]);
        EmployeeCalendar.Occurrences(new DateOnly(1992, 2, 29), new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28)).ShouldBe([new DateOnly(2027, 2, 28)]);
        EmployeeCalendar.Occurrences(new DateOnly(1990, 12, 31), new DateOnly(2026, 12, 15), new DateOnly(2027, 1, 15)).ShouldBe([new DateOnly(2026, 12, 31)]);
        EmployeeCalendar.Occurrences(new DateOnly(1990, 6, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1)).ShouldBeEmpty();
    }
}

public sealed class AttendanceRateTests
{
    [Fact]
    public void Half_days_count_half_and_days_off_are_not_working_days()
        => AttendanceMetrics.Rate([AttendanceStatus.Present, AttendanceStatus.HalfDay, AttendanceStatus.Absent, AttendanceStatus.Present]).ShouldBe(62.5m);
}

public sealed class TallyJournalTests
{
    [Fact]
    public void Debits_are_negative_deemed_positive_entries_and_text_is_escaped()
    {
        PayrollRun run = PayrollRunFor(new DateOnly(2026, 9, 1));
        string xml = AccountingJournalAdapter.TallyXml("Acme & Sons", run,
        [
            ["2026-09-30", "Salary expense - Basic", "100000.00", "", "Payroll"],
            ["2026-09-30", "Salaries payable (net pay)", "", "100000.00", "Payroll"],
        ]);
        xml.ShouldContain("<SVCURRENTCOMPANY>Acme &amp; Sons</SVCURRENTCOMPANY>");
        xml.ShouldContain("<DATE>20260930</DATE>");
        xml.ShouldContain("<LEDGERNAME>Salary expense - Basic</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-100000.00</AMOUNT>");
        xml.ShouldContain("<LEDGERNAME>Salaries payable (net pay)</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>100000.00</AMOUNT>");
    }

    private static PayrollRun PayrollRunFor(DateOnly period) => PayrollRun.Open(period, period.AddMonths(1).AddDays(-1), null, "INR", null);
}
