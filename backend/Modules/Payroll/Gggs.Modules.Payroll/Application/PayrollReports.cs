using System.Globalization;
using System.Text;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Payroll.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

/// <summary>A file to download.</summary>
public sealed record ReportFile(string FileName, string ContentType, byte[] Content);

public sealed record TrendPoint(DateOnly Period, string Label, int Employees, decimal Gross, decimal Deductions, decimal Net, decimal EmployerCost);

public sealed record DepartmentCost(string Name, int Employees, decimal Gross, decimal EmployerCost);

public sealed record PayrollDashboard(
    RunItem? LatestRun,
    RunItem? LastFinalized,
    IReadOnlyList<TrendPoint> Trend,
    StatutoryTotals? Statutory,
    IReadOnlyList<DepartmentCost> Departments,
    int EmployeesWithSalary,
    int EmployeesWithoutSalary,
    int PendingDeclarations,
    int PendingInputs);

/// <summary>
/// Payroll outputs (spec §21 "bank file, accounting", §22, §103): the register, the bank transfer file,
/// the EPF ECR, ESI and professional-tax returns, the accounting journal, and the payroll dashboard.
/// </summary>
internal sealed class PayrollReports(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IEmployeePayrollDetails payrollDetails,
    IOrgStructure organization,
    ITenantClock clock)
{
    public async Task<ReportFile> RegisterAsync(Guid runId, CancellationToken cancellationToken)
    {
        (PayrollRun run, List<PayrollRunItem> items) = await RunAsync(runId, requireLocked: false, cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, items.Select(item => item.DepartmentId).OfType<Guid>(), cancellationToken);
        List<(string Code, string Name, ComponentKind Kind)> columns = [.. items
            .SelectMany(item => item.Lines)
            .Select(line => (line.Code, line.Name, line.Kind))
            .DistinctBy(column => column.Code)
            .OrderBy(column => column.Kind)
            .ThenBy(column => column.Code, StringComparer.Ordinal)];

        var csv = new Csv();
        csv.Row(["Employee code", "Name", "Department", "Designation", "Status", "On hold", "Paid days", "LOP days", .. columns.Select(column => column.Name), "Gross", "Deductions", "Net", "Employer cost"]);
        foreach (PayrollRunItem item in items.OrderBy(item => item.EmployeeCode, StringComparer.Ordinal))
        {
            Dictionary<string, decimal> amounts = item.Lines.GroupBy(line => line.Code).ToDictionary(group => group.Key, group => group.Sum(line => line.Amount));
            csv.Row([
                item.EmployeeCode, item.EmployeeName, item.DepartmentId is { } department ? departments.GetValueOrDefault(department) ?? string.Empty : string.Empty,
                item.Designation ?? string.Empty, item.Status.ToString(), item.OnHold ? "Yes" : string.Empty, Number(item.PaidDays), Number(item.LopDays),
                .. columns.Select(column => Number(amounts.GetValueOrDefault(column.Code))),
                Number(item.Gross + item.Reimbursements), Number(item.Deductions), Number(item.Net), Number(item.Gross + item.Reimbursements + item.EmployerContributions)]);
        }

        return csv.File($"payroll-register-{run.Period:yyyy-MM}.csv");
    }

    /// <summary>The bank transfer file: one line per person paid, with their full account number. Only for a finalized run.</summary>
    public async Task<ReportFile> BankFileAsync(Guid runId, CancellationToken cancellationToken)
    {
        (PayrollRun run, List<PayrollRunItem> items) = await RunAsync(runId, requireLocked: true, cancellationToken);
        List<PayrollRunItem> paid = [.. items.Where(item => item.IsPayable && item.Net > 0)];
        IReadOnlyDictionary<Guid, PayrollIdentity> accounts = await payrollDetails.GetAsync(paid.Select(item => item.EmployeeId), reveal: true, cancellationToken);

        var csv = new Csv();
        csv.Row(["Beneficiary name", "Account number", "IFSC", "Amount", "Payment date", "Narration", "Employee code"]);
        foreach (PayrollRunItem item in paid.OrderBy(item => item.EmployeeCode, StringComparer.Ordinal))
        {
            PayrollIdentity? account = accounts.GetValueOrDefault(item.EmployeeId);
            if (account?.BankAccountNumber is null || account.BankIfsc is null)
            {
                continue;
            }

            csv.Row([
                account.BankAccountHolder ?? item.EmployeeName, account.BankAccountNumber, account.BankIfsc, Number(item.Net),
                run.PayDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), $"Salary {run.Period:MMM yyyy}", item.EmployeeCode]);
        }

        return csv.File($"bank-transfer-{run.Period:yyyy-MM}.csv");
    }

    /// <summary>The EPFO electronic challan-cum-return (ECR 2.0 text format) for the month.</summary>
    public async Task<ReportFile> ProvidentFundReturnAsync(Guid runId, CancellationToken cancellationToken)
    {
        (PayrollRun run, List<PayrollRunItem> items) = await RunAsync(runId, requireLocked: true, cancellationToken);
        List<PayrollRunItem> members = [.. items.Where(item => item.IsPayable && item.ProvidentFund is not null)];
        IReadOnlyDictionary<Guid, PayrollIdentity> identities = await payrollDetails.GetAsync(members.Select(item => item.EmployeeId), reveal: true, cancellationToken);

        var text = new StringBuilder();
        foreach (PayrollRunItem item in members.OrderBy(item => item.EmployeeCode, StringComparer.Ordinal))
        {
            ProvidentFundFigures pf = item.ProvidentFund!;
            string uan = identities.GetValueOrDefault(item.EmployeeId)?.Uan ?? string.Empty;
            text.Append(string.Join("#~#",
                uan, item.EmployeeName.ToUpperInvariant(), Whole(item.Gross), Whole(pf.EpfWages), Whole(pf.EpsWages), Whole(pf.EdliWages),
                Whole(pf.EmployeeShare), Whole(pf.PensionShare), Whole(pf.EmployerShare), Whole(item.LopDays), "0"));
            text.Append('\n');
        }

        return new ReportFile($"ecr-{run.Period:yyyy-MM}.txt", "text/plain", Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>The ESI monthly contribution upload.</summary>
    public async Task<ReportFile> StateInsuranceReturnAsync(Guid runId, CancellationToken cancellationToken)
    {
        (PayrollRun run, List<PayrollRunItem> items) = await RunAsync(runId, requireLocked: true, cancellationToken);
        List<PayrollRunItem> members = [.. items.Where(item => item.IsPayable && item.Lines.Any(line => line.Code == StatutoryKeys.StateInsuranceEmployee))];
        IReadOnlyDictionary<Guid, PayrollIdentity> identities = await payrollDetails.GetAsync(members.Select(item => item.EmployeeId), reveal: true, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(members.Select(item => item.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);

        var csv = new Csv();
        csv.Row(["IP Number", "IP Name", "No of Days for which wages paid", "Total Monthly Wages", "Reason Code for Zero workings days", "Last Working Day", "Employee contribution", "Employer contribution"]);
        foreach (PayrollRunItem item in members.OrderBy(item => item.EmployeeCode, StringComparer.Ordinal))
        {
            decimal Line(string code) => item.Lines.Where(line => line.Code == code).Sum(line => line.Amount);
            DateOnly? exit = people.GetValueOrDefault(item.EmployeeId)?.ExitDate;
            csv.Row([
                identities.GetValueOrDefault(item.EmployeeId)?.EsicNumber ?? string.Empty, item.EmployeeName, Whole(item.PaidDays), Whole(item.Gross),
                EsiReason(item, people.GetValueOrDefault(item.EmployeeId), run), exit is { } left && left <= run.PeriodEnd ? left.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty,
                Number(Line(StatutoryKeys.StateInsuranceEmployee)), Number(Line(StatutoryKeys.StateInsuranceEmployer))]);
        }

        return csv.File($"esi-{run.Period:yyyy-MM}.csv");
    }

    /// <summary>
    /// Members who left during the run's month, for marking their exit on the EPFO unified portal:
    /// UAN, name, date of exit and reason (C cessation, S superannuation, R retirement, D death in service).
    /// </summary>
    public async Task<ReportFile> ProvidentFundExitsAsync(Guid runId, CancellationToken cancellationToken)
    {
        (PayrollRun run, List<PayrollRunItem> items) = await RunAsync(runId, requireLocked: true, cancellationToken);
        List<PayrollRunItem> members = [.. items.Where(item => item.IsPayable && (item.ProvidentFund is not null || run.Kind == PayrollRunKind.FullAndFinal))];
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(members.Select(item => item.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        List<PayrollRunItem> leavers = [.. members.Where(item => people.GetValueOrDefault(item.EmployeeId)?.ExitDate is { } left && left >= run.Period && left <= run.PeriodEnd.AddMonths(1))];
        IReadOnlyDictionary<Guid, PayrollIdentity> identities = await payrollDetails.GetAsync(leavers.Select(item => item.EmployeeId), reveal: true, cancellationToken);

        var csv = new Csv();
        csv.Row(["UAN", "Member name", "Date of exit", "Reason of exit"]);
        foreach (PayrollRunItem item in leavers.OrderBy(item => item.EmployeeCode, StringComparer.Ordinal))
        {
            EmployeeSummary person = people[item.EmployeeId];
            csv.Row([
                identities.GetValueOrDefault(item.EmployeeId)?.Uan ?? string.Empty, item.EmployeeName.ToUpperInvariant(),
                person.ExitDate!.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), EpfExitCode(person.ExitReasonCode)]);
        }

        return csv.File($"epf-exits-{run.Period:yyyy-MM}.csv");
    }

    /// <summary>EPFO exit reasons: superannuation, retirement, death in service, else cessation of service.</summary>
    internal static string EpfExitCode(string? exitReason) => exitReason switch
    {
        "RETIREMENT" => "S",
        "DEATH" => "D",
        _ => "C",
    };

    /// <summary>ESIC reason codes for a month with no or few working days: 2 left service, 3 retired, 5 expired, 10 retrenchment.</summary>
    private static string EsiReason(PayrollRunItem item, EmployeeSummary? person, PayrollRun run)
    {
        if (person?.ExitDate is { } left && left <= run.PeriodEnd)
        {
            return person.ExitReasonCode switch
            {
                "RETIREMENT" => "3",
                "DEATH" => "5",
                "RETRENCHMENT" => "10",
                _ => "2",
            };
        }

        return item.PaidDays > 0 ? "0" : "1";
    }

    /// <summary>Professional tax deducted, by state and by employee.</summary>
    public async Task<ReportFile> ProfessionalTaxReturnAsync(Guid runId, CancellationToken cancellationToken)
    {
        (PayrollRun run, List<PayrollRunItem> items) = await RunAsync(runId, requireLocked: false, cancellationToken);
        var csv = new Csv();
        csv.Row(["State", "Employee code", "Name", "Gross", "Professional tax"]);
        foreach (PayrollRunItem item in items.Where(item => item.IsPayable).OrderBy(item => item.StateCode).ThenBy(item => item.EmployeeCode, StringComparer.Ordinal))
        {
            decimal pt = item.Lines.Where(line => line.Code == StatutoryKeys.ProfessionalTax).Sum(line => line.Amount);
            if (pt > 0)
            {
                csv.Row([item.StateCode ?? string.Empty, item.EmployeeCode, item.EmployeeName, Number(item.Gross), Number(pt)]);
            }
        }

        return csv.File($"professional-tax-{run.Period:yyyy-MM}.csv");
    }

    /// <summary>The payroll journal: salary and employer costs debited; net pay and each statutory payable credited.</summary>
    public async Task<ReportFile> JournalAsync(Guid runId, CancellationToken cancellationToken)
    {
        (PayrollRun run, List<PayrollRunItem> items) = await RunAsync(runId, requireLocked: false, cancellationToken);
        List<PayrollRunItem> payable = [.. items.Where(item => item.IsPayable)];
        List<PayLine> lines = [.. payable.SelectMany(item => item.Lines)];
        decimal Sum(params string[] codes) => lines.Where(line => codes.Contains(line.Code)).Sum(line => line.Amount);

        var entries = new List<(string Account, decimal Debit, decimal Credit)>();
        foreach (IGrouping<string, PayLine> group in lines.Where(line => line.Kind is ComponentKind.Earning or ComponentKind.Reimbursement).GroupBy(line => line.Name).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            entries.Add(($"Salary expense - {group.Key}", group.Sum(line => line.Amount), 0));
        }

        foreach (IGrouping<string, PayLine> group in lines.Where(line => line.Kind == ComponentKind.EmployerContribution).GroupBy(line => line.Name).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            entries.Add(($"Employer cost - {group.Key}", group.Sum(line => line.Amount), 0));
        }

        decimal edliAdmin = payable.Select(item => item.ProvidentFund).OfType<ProvidentFundFigures>().Sum(pf => pf.EdliCharge + pf.AdminCharge);
        if (edliAdmin > 0)
        {
            entries.Add(("Employer cost - EDLI and PF admin charges", edliAdmin, 0));
        }

        entries.Add(("Salaries payable (net pay)", 0, payable.Sum(item => item.Net)));
        entries.Add(("Provident fund payable", 0, Sum(StatutoryKeys.ProvidentFundEmployee, StatutoryKeys.ProvidentFundEmployer) + edliAdmin));
        entries.Add(("ESI payable", 0, Sum(StatutoryKeys.StateInsuranceEmployee, StatutoryKeys.StateInsuranceEmployer)));
        entries.Add(("Professional tax payable", 0, Sum(StatutoryKeys.ProfessionalTax)));
        entries.Add(("TDS payable (salary)", 0, Sum(StatutoryKeys.IncomeTax)));
        entries.Add(("Labour welfare fund payable", 0, Sum(StatutoryKeys.LabourWelfareEmployee, StatutoryKeys.LabourWelfareEmployer)));
        entries.Add(("Gratuity provision", 0, Sum(StatutoryKeys.Gratuity)));
        decimal benefits = lines.Where(line => line.Code.StartsWith("BEN", StringComparison.Ordinal)).Sum(line => line.Amount);
        entries.Add(("Benefits payable", 0, benefits));
        decimal otherDeductions = lines.Where(line => line.Kind == ComponentKind.Deduction).Sum(line => line.Amount)
            - Sum(StatutoryKeys.ProvidentFundEmployee, StatutoryKeys.StateInsuranceEmployee, StatutoryKeys.ProfessionalTax, StatutoryKeys.IncomeTax, StatutoryKeys.LabourWelfareEmployee)
            - lines.Where(line => line.Kind == ComponentKind.Deduction && line.Code.StartsWith("BEN", StringComparison.Ordinal)).Sum(line => line.Amount);
        entries.Add(("Other recoveries", 0, otherDeductions));

        var csv = new Csv();
        csv.Row(["Date", "Account", "Debit", "Credit", "Narration"]);
        string date = run.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach ((string account, decimal debit, decimal credit) in entries.Where(entry => entry.Debit != 0 || entry.Credit != 0))
        {
            csv.Row([date, account, debit == 0 ? string.Empty : Number(debit), credit == 0 ? string.Empty : Number(credit), $"Payroll {run.Period:MMM yyyy}"]);
        }

        csv.Row([date, "Total", Number(entries.Sum(entry => entry.Debit)), Number(entries.Sum(entry => entry.Credit)), string.Empty]);
        return csv.File($"payroll-journal-{run.Period:yyyy-MM}.csv");
    }

    public async Task<PayrollDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<PayrollRun> runs = await dbContext.PayrollRuns.AsNoTracking().Where(run => run.Kind == PayrollRunKind.Regular && run.Status != RunStatus.Cancelled).OrderByDescending(run => run.Period).Take(24).ToListAsync(cancellationToken);
        List<PayrollRun> locked = [.. runs.Where(run => run.IsLocked).Take(12).OrderBy(run => run.Period)];
        PayrollRun? last = locked.LastOrDefault();

        StatutoryTotals? statutory = null;
        var departments = new List<DepartmentCost>();
        if (last is not null)
        {
            List<PayrollRunItem> items = [.. (await dbContext.PayrollRunItems.AsNoTracking().Where(item => item.RunId == last.Id).ToListAsync(cancellationToken)).Where(item => item.IsPayable)];
            List<PayLine> lines = [.. items.SelectMany(item => item.Lines)];
            decimal Sum(params string[] codes) => lines.Where(line => codes.Contains(line.Code)).Sum(line => line.Amount);
            statutory = new StatutoryTotals(
                Sum(StatutoryKeys.ProvidentFundEmployee), Sum(StatutoryKeys.ProvidentFundEmployer), Sum(StatutoryKeys.StateInsuranceEmployee), Sum(StatutoryKeys.StateInsuranceEmployer),
                Sum(StatutoryKeys.ProfessionalTax), Sum(StatutoryKeys.IncomeTax), Sum(StatutoryKeys.LabourWelfareEmployee, StatutoryKeys.LabourWelfareEmployer));
            IReadOnlyDictionary<Guid, string> names = await organization.GetNamesAsync(OrgUnitKind.Department, items.Select(item => item.DepartmentId).OfType<Guid>(), cancellationToken);
            departments = [.. items
                .GroupBy(item => item.DepartmentId is { } department ? names.GetValueOrDefault(department) ?? "Unassigned" : "Unassigned")
                .Select(group => new DepartmentCost(group.Key, group.Count(), group.Sum(item => item.Gross), group.Sum(item => item.Gross + item.Reimbursements + item.EmployerContributions)))
                .OrderByDescending(cost => cost.EmployerCost)];
        }

        IReadOnlyList<Guid> active = await employees.ListActiveIdsAsync(null, null, cancellationToken);
        int withSalary = await dbContext.EmployeeSalaries.AsNoTracking()
            .Where(salary => active.Contains(salary.EmployeeId) && salary.EffectiveFrom <= today && (salary.EffectiveTo == null || salary.EffectiveTo >= today))
            .Select(salary => salary.EmployeeId)
            .Distinct()
            .CountAsync(cancellationToken);
        int fy = FinancialYear.Of(today);
        var month = new DateOnly(today.Year, today.Month, 1);

        return new PayrollDashboard(
            runs.Count > 0 ? RunService.Item(runs[0]) : null,
            last is null ? null : RunService.Item(last),
            [.. locked.Select(run => new TrendPoint(run.Period, run.Period.ToString("MMM yy", CultureInfo.InvariantCulture), run.EmployeeCount, run.Gross, run.Deductions, run.Net, run.EmployerCost))],
            statutory,
            departments,
            withSalary,
            active.Count - withSalary,
            await dbContext.TaxDeclarations.CountAsync(declaration => declaration.FinancialYear == fy && declaration.Status == DeclarationStatus.Submitted, cancellationToken),
            await dbContext.PayrollInputs.CountAsync(input => input.Period >= month && input.Status == InputStatus.Pending, cancellationToken));
    }

    private async Task<(PayrollRun Run, List<PayrollRunItem> Items)> RunAsync(Guid runId, bool requireLocked, CancellationToken cancellationToken)
    {
        PayrollRun run = await dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == runId, cancellationToken) ?? throw new NotFoundException(nameof(PayrollRun), runId);
        if (requireLocked && !run.IsLocked)
        {
            throw new DomainRuleException("RUN_NOT_FINAL", "This file is produced from a finalized run.");
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Payroll.ReportView, cancellationToken);
        List<PayrollRunItem> items = await dbContext.PayrollRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync(cancellationToken);
        return (run, [.. items.Where(item => scope is null || scope.Contains(item.EmployeeId))]);
    }

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Whole(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>A small CSV writer: quotes fields that need it and writes UTF-8 with a BOM so spreadsheets read it right.</summary>
    private sealed class Csv
    {
        private readonly StringBuilder _text = new();

        public void Row(IEnumerable<string> fields)
        {
            _text.Append(string.Join(',', fields.Select(Escape)));
            _text.Append("\r\n");
        }

        public ReportFile File(string name) => new(name, "text/csv", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(_text.ToString())]);

        private static string Escape(string field)
        {
            string value = field.Length > 0 && "=+-@".Contains(field[0], StringComparison.Ordinal) && !decimal.TryParse(field, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                ? "'" + field
                : field;
            return value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal)
                ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : value;
        }
    }
}
