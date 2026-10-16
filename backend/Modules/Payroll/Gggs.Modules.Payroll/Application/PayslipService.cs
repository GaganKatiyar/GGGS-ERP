using System.Globalization;
using System.Text;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Payroll.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

public sealed record PayslipEmployee(
    string Name,
    string Code,
    string? Designation,
    string? Department,
    DateOnly? JoiningDate,
    string? Pan,
    string? Uan,
    string? BankName,
    string? BankAccount,
    string? Ifsc,
    decimal AnnualCtc);

public sealed record PayslipLine(string Code, string Name, decimal Amount, decimal? FullAmount, string? Note);

public sealed record PayslipYearToDate(decimal Gross, decimal Deductions, decimal Net, decimal Tds, decimal ProvidentFund);

public sealed record PayslipTax(string Regime, decimal TaxableIncome, decimal AnnualTax, decimal DeductedBefore, decimal ThisMonth);

/// <summary>A payslip as published (spec §21) - frozen at finalization so it never changes afterwards.</summary>
public sealed record PayslipDocument(
    string EmployerName,
    string? EmployerAddress,
    string PeriodLabel,
    DateOnly Period,
    DateOnly PayDate,
    string Currency,
    PayslipEmployee Employee,
    decimal PeriodDays,
    decimal PaidDays,
    decimal LopDays,
    IReadOnlyList<PayslipLine> Earnings,
    IReadOnlyList<PayslipLine> Reimbursements,
    IReadOnlyList<PayslipLine> Deductions,
    IReadOnlyList<PayslipLine> EmployerContributions,
    decimal Gross,
    decimal TotalDeductions,
    decimal Net,
    string NetInWords,
    PayslipYearToDate YearToDate,
    PayslipTax? Tax);

/// <summary>Builds the published payslip from a run result.</summary>
internal static class PayslipDocuments
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public static PayslipDocument Build(
        PayrollRun run,
        PayrollRunItem item,
        PayrollSettings settings,
        PayrollIdentity? identity,
        EmployeeSummary? person,
        string? department,
        YearToDate before,
        (decimal Gross, decimal Deductions, decimal Net) totalsBefore)
    {
        IReadOnlyList<PayLine> lines = item.Lines;
        List<PayslipLine> Of(ComponentKind kind) => [.. lines.Where(line => line.Kind == kind && line.Amount != 0).Select(line => new PayslipLine(line.Code, line.Name, line.Amount, line.FullAmount, line.Note))];
        decimal Line(string code) => lines.Where(line => line.Code == code).Sum(line => line.Amount);
        TaxComputation? tax = item.Tax;

        return new PayslipDocument(
            settings.EmployerName ?? "Your employer",
            settings.EmployerAddress,
            RunService.Label(run.Period),
            run.Period,
            run.PayDate,
            run.Currency,
            new PayslipEmployee(
                item.EmployeeName, item.EmployeeCode, item.Designation, department, person?.JoiningDate, identity?.Pan, identity?.Uan, identity?.BankName,
                identity?.BankAccountNumber, identity?.BankIfsc, item.AnnualCtc),
            item.PeriodDays,
            item.PaidDays,
            item.LopDays,
            Of(ComponentKind.Earning),
            Of(ComponentKind.Reimbursement),
            Of(ComponentKind.Deduction),
            Of(ComponentKind.EmployerContribution),
            item.Gross,
            item.Deductions,
            item.Net,
            AmountInWords.Of(item.Net, run.Currency),
            new PayslipYearToDate(
                totalsBefore.Gross + item.Gross, totalsBefore.Deductions + item.Deductions, totalsBefore.Net + item.Net,
                before.Tds + Line(StatutoryKeys.IncomeTax), before.ProvidentFundEmployee + Line(StatutoryKeys.ProvidentFundEmployee)),
            tax is null ? null : new PayslipTax(tax.Regime, tax.TaxableIncome, tax.AnnualTax, tax.AlreadyDeducted, tax.MonthlyTds));
    }
}

/// <summary>Amounts in words the Indian way - lakh and crore - as payslips and bank advices print them.</summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
        ["", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Of(decimal amount, string currency = "INR")
    {
        long rupees = (long)Math.Floor(Math.Abs(amount));
        int paise = (int)Math.Round((Math.Abs(amount) - rupees) * 100, MidpointRounding.AwayFromZero);
        string unit = currency == "INR" ? "Rupees" : currency;
        var words = new StringBuilder(rupees == 0 ? "Zero" : Words(rupees));
        if (paise > 0)
        {
            words.Append(" and ").Append(Words(paise)).Append(currency == "INR" ? " Paise" : " cents");
        }

        return $"{(amount < 0 ? "Minus " : string.Empty)}{unit} {words} only";
    }

    private static string Words(long number)
    {
        var parts = new List<string>();
        (long divisor, string name)[] scales = [(10_000_000, "Crore"), (100_000, "Lakh"), (1_000, "Thousand"), (100, "Hundred")];
        foreach ((long divisor, string name) in scales)
        {
            if (number >= divisor)
            {
                parts.Add($"{Words(number / divisor)} {name}");
                number %= divisor;
            }
        }

        if (number > 0)
        {
            parts.Add(number < 20 ? Ones[number] : Tens[number / 10] + (number % 10 > 0 ? "-" + Ones[number % 10] : string.Empty));
        }

        return string.Join(' ', parts);
    }
}

public sealed record PayslipItem(Guid Id, Guid EmployeeId, string? EmployeeName, string? EmployeeCode, DateOnly Period, string PeriodLabel, DateOnly PayDate, string Currency, decimal Gross, decimal Deductions, decimal Net, DateTime PublishedDate);

public sealed record PayslipView(Guid Id, Guid EmployeeId, Guid RunId, PayslipDocument Document);

public sealed record MonthlyTax(DateOnly Period, string PeriodLabel, decimal Gross, decimal Taxable, decimal Tds);

/// <summary>
/// The year's salary and tax (Form 16 Part B, spec §22): what was paid and deducted month by month,
/// and the latest projection's deductions and tax. Part A comes from the TRACES portal.
/// </summary>
public sealed record TaxStatement(
    Guid EmployeeId,
    string? EmployeeName,
    string? Pan,
    int FinancialYear,
    string FinancialYearLabel,
    string EmployerName,
    string? EmployerTan,
    string? EmployerPan,
    decimal GrossSalary,
    decimal Exemptions,
    decimal StandardDeduction,
    decimal ProfessionalTax,
    decimal ChapterVIA,
    decimal TaxableIncome,
    decimal TaxOnIncome,
    decimal Rebate,
    decimal Cess,
    decimal AnnualTax,
    decimal TdsDeducted,
    string Regime,
    IReadOnlyList<MonthlyTax> Months);

/// <summary>Published payslips and the year's tax statement, for employees and for payroll.</summary>
internal sealed class PayslipService(
    AppDbContext dbContext,
    PayrollCatalog catalog,
    IEmployeeDirectory employees,
    IEmployeePayrollDetails payrollDetails,
    ICurrentUser currentUser,
    ITenantClock clock)
{
    public Task<IReadOnlyList<PayslipItem>> MineAsync(int? year, CancellationToken cancellationToken) => ForEmployeeAsync(Me(), year, cancellationToken);

    public async Task<PayslipView> MyPayslipAsync(Guid id, CancellationToken cancellationToken)
    {
        Payslip payslip = await dbContext.Payslips.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id && row.EmployeeId == Me(), cancellationToken)
            ?? throw new NotFoundException(nameof(Payslip), id);
        return View(payslip);
    }

    public async Task<IReadOnlyList<PayslipItem>> ListAsync(DateOnly? period, Guid? employeeId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Payroll.RunView, cancellationToken);
        IQueryable<Payslip> payslips = dbContext.Payslips.AsNoTracking();
        if (scope is not null)
        {
            payslips = payslips.Where(payslip => scope.Contains(payslip.EmployeeId));
        }

        if (period is { } month)
        {
            DateOnly first = new(month.Year, month.Month, 1);
            payslips = payslips.Where(payslip => payslip.Period == first);
        }

        if (employeeId is { } person)
        {
            payslips = payslips.Where(payslip => payslip.EmployeeId == person);
        }

        return await ItemsAsync(await payslips.OrderByDescending(payslip => payslip.Period).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<PayslipView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Payslip payslip = await dbContext.Payslips.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Payslip), id);
        return await employees.IsInScopeAsync(payslip.EmployeeId, Permissions.Payroll.RunView, cancellationToken) ? View(payslip) : throw new NotFoundException(nameof(Payslip), id);
    }

    public async Task<IReadOnlyList<PayslipItem>> ForEmployeeAsync(Guid employeeId, int? year, CancellationToken cancellationToken)
    {
        IQueryable<Payslip> payslips = dbContext.Payslips.AsNoTracking().Where(payslip => payslip.EmployeeId == employeeId);
        if (year is { } fy)
        {
            DateOnly start = FinancialYear.Start(fy);
            DateOnly end = FinancialYear.End(fy);
            payslips = payslips.Where(payslip => payslip.Period >= start && payslip.Period <= end);
        }

        return await ItemsAsync(await payslips.OrderByDescending(payslip => payslip.Period).ToListAsync(cancellationToken), cancellationToken);
    }

    public Task<TaxStatement> MyTaxStatementAsync(int? year, CancellationToken cancellationToken) => TaxStatementAsync(Me(), year, cancellationToken);

    public async Task<TaxStatement> EmployeeTaxStatementAsync(Guid employeeId, int? year, CancellationToken cancellationToken)
        => await employees.IsInScopeAsync(employeeId, Permissions.Payroll.RunView, cancellationToken)
            ? await TaxStatementAsync(employeeId, year, cancellationToken)
            : throw new NotFoundException("Employee", employeeId);

    private async Task<TaxStatement> TaxStatementAsync(Guid employeeId, int? year, CancellationToken cancellationToken)
    {
        int fy = year ?? FinancialYear.Of(await clock.TodayAsync(cancellationToken));
        DateOnly start = FinancialYear.Start(fy);
        DateOnly end = FinancialYear.End(fy);
        List<PayrollRunItem> items = await dbContext.Payslips.AsNoTracking()
            .Where(payslip => payslip.EmployeeId == employeeId && payslip.Period >= start && payslip.Period <= end)
            .Join(dbContext.PayrollRunItems.AsNoTracking(), payslip => payslip.RunItemId, item => item.Id, (payslip, item) => item)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, DateOnly> periods = await dbContext.PayrollRuns.AsNoTracking()
            .Where(run => items.Select(item => item.RunId).Contains(run.Id))
            .ToDictionaryAsync(run => run.Id, run => run.Period, cancellationToken);

        List<(DateOnly Period, PayrollRunItem Item)> ordered = [.. items.Select(item => (periods.GetValueOrDefault(item.RunId), item)).OrderBy(row => row.Item1)];
        TaxComputation? latest = ordered.Count > 0 ? ordered[^1].Item.Tax : null;
        PayrollSettings settings = await catalog.SettingsAsync(cancellationToken);
        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        PayrollIdentity? identity = (await payrollDetails.GetAsync([employeeId], reveal: false, cancellationToken)).GetValueOrDefault(employeeId);

        decimal Tds(PayrollRunItem item) => item.Lines.Where(line => line.Code == StatutoryKeys.IncomeTax).Sum(line => line.Amount);
        return new TaxStatement(
            employeeId, person?.DisplayName, identity?.Pan, fy, FinancialYear.Label(fy), settings.EmployerName ?? "Your employer", settings.EmployerTan, settings.EmployerPan,
            ordered.Sum(row => row.Item.Gross), latest?.HraExemption ?? 0, latest?.StandardDeduction ?? 0, latest?.ProfessionalTax ?? 0, latest?.ChapterVIA ?? 0,
            latest?.TaxableIncome ?? 0, latest?.TaxBeforeRebate ?? 0, latest?.Rebate ?? 0, latest?.Cess ?? 0, latest?.AnnualTax ?? 0, ordered.Sum(row => Tds(row.Item)),
            latest?.Regime ?? settings.DefaultRegime.ToString(),
            [.. ordered.Select(row => new MonthlyTax(row.Period, RunService.Label(row.Period), row.Item.Gross, row.Item.TaxableEarnings, Tds(row.Item)))]);
    }

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("PAYROLL_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<IReadOnlyList<PayslipItem>> ItemsAsync(IReadOnlyList<Payslip> payslips, CancellationToken cancellationToken)
    {
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(payslips.Select(payslip => payslip.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        return [.. payslips.Select(payslip => new PayslipItem(
            payslip.Id, payslip.EmployeeId, people.GetValueOrDefault(payslip.EmployeeId)?.DisplayName, people.GetValueOrDefault(payslip.EmployeeId)?.EmployeeCode,
            payslip.Period, payslip.Period.ToString("MMMM yyyy", CultureInfo.InvariantCulture), payslip.PayDate, payslip.Currency, payslip.Gross, payslip.Deductions,
            payslip.Net, payslip.PublishedDate))];
    }

    private static PayslipView View(Payslip payslip)
        => new(payslip.Id, payslip.EmployeeId, payslip.RunId, JsonSerializer.Deserialize<PayslipDocument>(payslip.DocumentJson, PayslipDocuments.Json)!);
}
