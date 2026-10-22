using System.Globalization;
using System.Security;
using System.Text;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.Payroll.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Payroll.Application;

/// <summary>Payroll cost for company-wide dashboards: the last closed run, the trend and cost by department.</summary>
internal sealed class PayrollMetrics(PayrollReports reports) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Payroll];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        PayrollDashboard dashboard = await reports.DashboardAsync(cancellationToken);
        List<MetricPoint> costTrend = [.. dashboard.Trend.Select(point => new MetricPoint(point.Label, point.EmployerCost))];
        TrendPoint? last = dashboard.Trend.Count == 0 ? null : dashboard.Trend[^1];
        return
        [
            new Metric("payroll.cost", MetricAreas.Payroll, "Payroll cost (last closed month)", last?.EmployerCost ?? 0, MetricUnit.Currency, last is null ? "No payroll closed yet" : $"{last.Label} · {last.Employees} paid",
                [.. dashboard.Departments.Select(row => new MetricPoint(row.Name, row.EmployerCost))], costTrend, Executive: true, HigherIsBetter: false),
            new Metric("payroll.net", MetricAreas.Payroll, "Net pay (last closed month)", last?.Net ?? 0, MetricUnit.Currency, last is null ? null : $"{last.Gross:N0} gross, {last.Deductions:N0} deducted",
                Trend: [.. dashboard.Trend.Select(point => new MetricPoint(point.Label, point.Net))]),
            new Metric("payroll.headcount", MetricAreas.Payroll, "People on payroll", dashboard.EmployeesWithSalary, MetricUnit.Count, $"{dashboard.EmployeesWithoutSalary} active without a salary"),
            new Metric("payroll.pending", MetricAreas.Payroll, "Inputs and declarations waiting", dashboard.PendingInputs + dashboard.PendingDeclarations, MetricUnit.Count,
                $"{dashboard.PendingInputs} input(s), {dashboard.PendingDeclarations} declaration(s)", HigherIsBetter: false),
        ];
    }
}

/// <summary>Finds a month's closed payroll run and checks the caller may take its reports out.</summary>
internal sealed class PayrollRunFinder(AppDbContext dbContext, IPermissionProvider permissions)
{
    public async Task<(PayrollRun? Run, string? Problem)> FindAsync(string? period, CancellationToken cancellationToken)
    {
        if (!(await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Payroll.ReportView))
        {
            return (null, "Payroll files need payroll report access as well.");
        }

        if (!DateOnly.TryParseExact((period ?? string.Empty) + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly month))
        {
            return (null, "Give the month as yyyy-mm.");
        }

        PayrollRun? run = await dbContext.PayrollRuns.AsNoTracking()
            .Where(row => row.Kind == PayrollRunKind.Regular && row.Period == month && (row.Status == RunStatus.Finalized || row.Status == RunStatus.Paid))
            .FirstOrDefaultAsync(cancellationToken);
        return run is null ? (null, $"There is no finalized payroll for {month:MMMM yyyy}.") : (run, null);
    }
}

/// <summary>
/// The bank transfer for a month's payroll: the bulk payment file to upload to the company's bank,
/// one line per person paid, from a finalized run.
/// </summary>
internal sealed class BankTransferAdapter(PayrollRunFinder finder, PayrollReports reports) : IIntegrationAdapter
{
    public string Key => "bank-transfer";

    public string Name => "Bank transfer file";

    public string Category => "Banking";

    public string Description => "Builds the salary bulk-payment file (NEFT/RTGS) for a finalized payroll, to upload to the company's bank.";

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("bankName", "Company bank"),
        new("debitAccount", "Debit account number", Secret: true),
        new("ifsc", "Debit branch IFSC"),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
    [
        new("salary-file", "Salary payment file", "The bulk-payment file for a month's finalized payroll.", [new("period", "Month (yyyy-mm)", Hint: "e.g. 2026-09")]),
    ];

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        string ifsc = settings.GetValueOrDefault("ifsc") ?? string.Empty;
        bool valid = ifsc.Length == 11 && ifsc[..4].All(char.IsLetter) && ifsc[4] == '0';
        return Task.FromResult(valid ? new IntegrationResult(true, "Settings look right; the file is built on demand.") : new IntegrationResult(false, "The IFSC must be 11 characters: four letters, a zero, then six characters."));
    }

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        (PayrollRun? run, string? problem) = await finder.FindAsync(parameters.GetValueOrDefault("period"), cancellationToken);
        if (run is null)
        {
            return new IntegrationResult(false, problem!);
        }

        ReportFile file = await reports.BankFileAsync(run.Id, cancellationToken);
        string header = $"# Debit {settings.GetValueOrDefault("bankName")} account ending {Last4(settings.GetValueOrDefault("debitAccount"))} (IFSC {settings.GetValueOrDefault("ifsc")}), value date {run.PayDate:yyyy-MM-dd}\n";
        byte[] content = [.. Encoding.UTF8.GetBytes(header), .. file.Content];
        int records = Math.Max(0, Encoding.UTF8.GetString(file.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
        return new IntegrationResult(true, $"{records} payment(s) for {run.Period:MMMM yyyy}, net {run.Net:N2} {run.Currency}.", file.FileName, file.ContentType, content, records);
    }

    private static string Last4(string? value) => string.IsNullOrEmpty(value) ? "????" : value.Length <= 4 ? value : value[^4..];
}

/// <summary>
/// The accounting journal for a month's payroll, as a CSV for any ledger or as a Tally Prime XML
/// import (a single journal voucher).
/// </summary>
internal sealed class AccountingJournalAdapter(PayrollRunFinder finder, PayrollReports reports) : IIntegrationAdapter
{
    public string Key => "accounting-journal";

    public string Name => "Accounting journal";

    public string Category => "Accounting";

    public string Description => "Posts a month's payroll to the books: the salary journal as CSV or a Tally Prime voucher import.";

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("format", "Format (csv or tally)", Hint: "csv or tally"),
        new("company", "Company name in the ledger", Required: false),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
    [
        new("payroll-journal", "Payroll journal", "The salary journal for a month's finalized payroll.", [new("period", "Month (yyyy-mm)", Hint: "e.g. 2026-09")]),
    ];

    public Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => Task.FromResult(Format(settings) is "csv" or "tally" ? new IntegrationResult(true, $"Journals will be produced as {Format(settings)}.") : new IntegrationResult(false, "The format must be csv or tally."));

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        (PayrollRun? run, string? problem) = await finder.FindAsync(parameters.GetValueOrDefault("period"), cancellationToken);
        if (run is null)
        {
            return new IntegrationResult(false, problem!);
        }

        ReportFile file = await reports.JournalAsync(run.Id, cancellationToken);
        List<string[]> rows = [.. Encoding.UTF8.GetString(file.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(SplitCsv).Where(row => row.Length >= 4 && row[1] != "Total")];
        if (Format(settings) != "tally")
        {
            return new IntegrationResult(true, $"{rows.Count} journal line(s) for {run.Period:MMMM yyyy}.", file.FileName, file.ContentType, file.Content, rows.Count);
        }

        string xml = TallyXml(settings.GetValueOrDefault("company") ?? string.Empty, run, rows);
        return new IntegrationResult(true, $"A Tally journal voucher with {rows.Count} ledger line(s) for {run.Period:MMMM yyyy}.", $"payroll-journal-{run.Period:yyyy-MM}.xml", "application/xml", Encoding.UTF8.GetBytes(xml), rows.Count);
    }

    /// <summary>A Tally Prime "Import Data" envelope with one journal voucher; debits are negative amounts in Tally's convention.</summary>
    public static string TallyXml(string company, PayrollRun run, IReadOnlyList<string[]> rows)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rows);
        var xml = new StringBuilder();
        xml.Append("<ENVELOPE><HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER><BODY><IMPORTDATA><REQUESTDESC><REPORTNAME>Vouchers</REPORTNAME>");
        xml.Append("<STATICVARIABLES><SVCURRENTCOMPANY>").Append(SecurityElement.Escape(company)).Append("</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC><REQUESTDATA>");
        xml.Append("<TALLYMESSAGE xmlns:UDF=\"TallyUDF\"><VOUCHER VCHTYPE=\"Journal\" ACTION=\"Create\"><DATE>").Append(run.PeriodEnd.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("</DATE>");
        xml.Append("<VOUCHERTYPENAME>Journal</VOUCHERTYPENAME><NARRATION>Payroll ").Append(run.Period.ToString("MMM yyyy", CultureInfo.InvariantCulture)).Append("</NARRATION>");
        foreach (string[] row in rows)
        {
            bool debit = decimal.TryParse(row[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal debitAmount) && debitAmount != 0;
            decimal amount = debit ? -debitAmount : decimal.TryParse(row[3], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal credit) ? credit : 0;
            xml.Append("<ALLLEDGERENTRIES.LIST><LEDGERNAME>").Append(SecurityElement.Escape(row[1])).Append("</LEDGERNAME><ISDEEMEDPOSITIVE>").Append(debit ? "Yes" : "No")
                .Append("</ISDEEMEDPOSITIVE><AMOUNT>").Append(amount.ToString("0.00", CultureInfo.InvariantCulture)).Append("</AMOUNT></ALLLEDGERENTRIES.LIST>");
        }

        xml.Append("</VOUCHER></TALLYMESSAGE></REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>");
        return xml.ToString();
    }

    private static string Format(IReadOnlyDictionary<string, string> settings) => (settings.GetValueOrDefault("format") ?? "csv").Trim().ToLowerInvariant();

    private static string[] SplitCsv(string line)
    {
        List<string> fields = [];
        var field = new StringBuilder();
        bool quoted = false;
        foreach (char c in line.TrimEnd('\r'))
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return [.. fields];
    }
}
