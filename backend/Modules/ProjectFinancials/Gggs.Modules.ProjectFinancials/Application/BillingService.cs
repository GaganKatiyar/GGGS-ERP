using System.Globalization;
using System.Text;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Client.Contracts;
using Gggs.Modules.Contract.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.ProjectFinancials.Domain;
using Gggs.Modules.ProjectFinancials.Infrastructure;
using Gggs.Modules.Timesheets.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.ProjectFinancials.Application;

public sealed record PlannedLineItem(InvoiceLineKind Kind, string Description, decimal Quantity, decimal Rate, decimal Amount, DateOnly? Month);

public sealed record BillingPreview(
    Guid ProjectId,
    string Code,
    string Name,
    string BillingType,
    string Currency,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PlannedLineItem> Lines,
    decimal Amount,
    decimal HeldByCap,
    decimal UnpricedHours,
    IReadOnlyList<string> TypedInvoices,
    decimal CreditsWaiting = 0);

public sealed record PrepareInvoiceBody(DateOnly From, DateOnly To, DateOnly? InvoiceDate, DateOnly? DueDate, decimal? TaxAmount);

public sealed record CreditBody(string Reason, DateOnly? Date, IReadOnlyList<Guid>? LineIds, decimal? Amount, decimal? TaxAmount);

public sealed record CreditNoteItem(Guid Id, Guid InvoiceId, string Number, DateOnly Date, decimal Amount, decimal TaxAmount, string Reason);

public sealed record OverdueInvoice(Guid ProjectId, string ProjectCode, Guid InvoiceId, string Number, DateOnly DueDate, decimal Amount, string Currency, int DaysOverdue);

/// <summary>A month's billing across the portfolio: what is ready to invoice, and what is overdue.</summary>
public sealed record BillingRun(DateOnly Month, IReadOnlyList<BillingPreview> Ready, IReadOnlyList<OverdueInvoice> Overdue, int Drafts);

public sealed record ExportQuery
{
    public DateOnly From { get; init; }

    public DateOnly To { get; init; }

    public string? Format { get; init; }

    public string? Company { get; init; }

    public string? SalesLedger { get; init; }

    public string? TaxLedger { get; init; }
}

public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Billing: invoices built from the work (spec §75), so nothing earned is forgotten and nothing is billed twice;
/// credit notes to take them back; and the accounting system's copy. GST filing stays in the books.
/// </summary>
internal sealed class BillingService(
    AppDbContext dbContext,
    FinanceService finance,
    IProjectDirectory projects,
    IProjectPermissions projectPermissions,
    IContractDirectory contracts,
    IClientDirectory clients,
    IEmployeeDirectory employees,
    ITimesheetLedger timesheets,
    ITenantClock clock)
{
    public async Task<BillingPreview> PreviewAsync(Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsView, cancellationToken);
        return await PreviewOfAsync(await FactAsync(projectId, cancellationToken), from, to, cancellationToken);
    }

    /// <summary>Puts everything ready in the period on a draft invoice, and marks its hours as billed.</summary>
    public async Task<InvoiceItem> PrepareAsync(Guid projectId, PrepareInvoiceBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        if (body.To < body.From)
        {
            throw new DomainRuleException("BILLING_PERIOD", "The billing period ends before it starts.");
        }

        ProjectFinancialFacts fact = await FactAsync(projectId, cancellationToken);
        BillingPlan plan = await PlanAsync(fact, body.From, body.To, cancellationToken);
        if (plan.Lines.Count == 0)
        {
            throw new DomainRuleException("BILLING_NOTHING", "Nothing in that period is ready to invoice.");
        }

        DateOnly date = body.InvoiceDate ?? await clock.TodayAsync(cancellationToken);
        ProjectInvoice invoice = ProjectInvoice.Raise(
            projectId, await finance.NextNumberAsync(date, cancellationToken),
            new InvoiceDetails(date, body.DueDate, body.From, body.To, plan.Amount, body.TaxAmount ?? 0, plan.Lines is [{ Kind: InvoiceLineKind.Milestone } only] ? only.ReferenceId : null, null));
        dbContext.ProjectInvoices.Add(invoice);

        foreach (PlannedLine planned in plan.Lines)
        {
            ProjectInvoiceLine line = ProjectInvoiceLine.For(invoice.Id, projectId, planned);
            dbContext.ProjectInvoiceLines.Add(line);
            dbContext.ProjectBilledHours.AddRange((planned.EntryIds ?? []).Select(entry => ProjectBilledHours.For(line, entry)));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique indexes caught two invoices racing for the same hours, or the same number.
            throw new DomainRuleException("BILLING_ALREADY_BILLED", "Someone else has just prepared an invoice for this project. Check it, then prepare again if anything is left.");
        }

        return await finance.InvoiceItemAsync(invoice, cancellationToken);
    }

    /// <summary>Takes a line off a draft, which puts what it billed back up for billing.</summary>
    public async Task<InvoiceItem> RemoveLineAsync(Guid projectId, Guid invoiceId, Guid lineId, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectInvoice invoice = await finance.InvoiceAsync(projectId, invoiceId, cancellationToken);
        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new DomainRuleException("INVOICE_STATE", "Lines can only come off a draft. Cancel or credit an invoice that has gone out.");
        }

        List<ProjectInvoiceLine> lines = await dbContext.ProjectInvoiceLines.Where(row => row.InvoiceId == invoiceId).ToListAsync(cancellationToken);
        ProjectInvoiceLine line = lines.Find(row => row.Id == lineId) ?? throw new NotFoundException(nameof(ProjectInvoiceLine), lineId);
        if (lines.Count == 1)
        {
            throw new DomainRuleException("INVOICE_LAST_LINE", "This is the invoice's only line. Cancel the invoice instead.");
        }

        dbContext.ProjectBilledHours.RemoveRange(await dbContext.ProjectBilledHours.Where(row => row.LineId == lineId).ToListAsync(cancellationToken));
        dbContext.ProjectInvoiceLines.Remove(line);
        invoice.SetAmount(lines.Where(row => row.Id != lineId).Sum(row => row.Amount));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await finance.InvoiceItemAsync(invoice, cancellationToken);
    }

    /// <summary>
    /// A credit note: for chosen lines of an invoice that has gone out, for all of them, or for an amount, which
    /// reduces the invoice without freeing any line. What a line bills becomes billable again; if the hours
    /// themselves were wrong, they can then be reopened.
    /// </summary>
    public async Task<CreditNoteItem> CreditAsync(Guid projectId, Guid invoiceId, CreditBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        ProjectInvoice invoice = await finance.InvoiceAsync(projectId, invoiceId, cancellationToken);
        List<ProjectInvoiceLine> lines = await dbContext.ProjectInvoiceLines.Where(row => row.InvoiceId == invoiceId && row.CreditNoteId == null).ToListAsync(cancellationToken);
        List<ProjectInvoiceLine> chosen = body.LineIds is { Count: > 0 } ids ? [.. lines.Where(row => ids.Contains(row.Id))] : body.Amount is null ? lines : [];
        if (body.LineIds is { Count: > 0 } asked && chosen.Count != asked.Distinct().Count())
        {
            throw new DomainRuleException("INVOICE_CREDIT_LINES", "One of those lines is not on this invoice, or has already been credited.");
        }

        decimal amount = chosen.Count > 0 ? chosen.Sum(row => row.Amount) : body.Amount ?? invoice.OpenAmount;
        decimal tax = body.TaxAmount ?? (invoice.Amount == 0 ? 0 : Math.Round(invoice.TaxAmount * amount / invoice.Amount, 2));
        if (tax < 0 || tax > invoice.TaxAmount)
        {
            throw new DomainRuleException("INVOICE_CREDIT_AMOUNT", $"The tax on a credit note can be up to {invoice.TaxAmount:0.##}.");
        }

        DateOnly date = body.Date ?? await clock.TodayAsync(cancellationToken);
        invoice.Credit(amount);

        ProjectCreditNote note = ProjectCreditNote.Raise(invoice, await NextCreditNumberAsync(date, cancellationToken), date, amount, tax, body.Reason);
        dbContext.ProjectCreditNotes.Add(note);
        List<Guid> chosenIds = [.. chosen.Select(row => row.Id)];
        dbContext.ProjectBilledHours.RemoveRange(await dbContext.ProjectBilledHours.Where(row => chosenIds.Contains(row.LineId)).ToListAsync(cancellationToken));
        chosen.ForEach(row => row.Credit(note.Id));

        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(note);
    }

    public async Task<IReadOnlyList<CreditNoteItem>> CreditNotesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await EnsureAsync(projectId, Permissions.Project.FinancialsView, cancellationToken);
        List<ProjectCreditNote> notes = await dbContext.ProjectCreditNotes.AsNoTracking().Where(row => row.ProjectId == projectId).OrderByDescending(row => row.Date).ToListAsync(cancellationToken);
        return [.. notes.Select(Item)];
    }

    /// <summary>The month's billing, for projects the caller can see: what is ready, largest first, what is overdue, and what is still in draft.</summary>
    public async Task<BillingRun> RunAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        DateOnly last = first.AddMonths(1).AddDays(-1);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyCollection<Guid>? visible = await projectPermissions.VisibleIdsAsync(Permissions.Project.FinancialsView, cancellationToken);
        List<ProjectFinancialFacts> facts = [.. (await projects.FinancialFactsAsync(visible, cancellationToken))
            .Where(fact => fact.Status != "Cancelled" && fact.BillingType != "NonBillable")];

        List<BillingPreview> ready = [];
        foreach (ProjectFinancialFacts fact in facts)
        {
            BillingPreview preview = await PreviewOfAsync(fact, first, last, cancellationToken);
            if (preview.Lines.Count > 0 || preview.HeldByCap > 0 || preview.UnpricedHours > 0 || preview.CreditsWaiting > 0)
            {
                ready.Add(preview);
            }
        }

        List<Guid> ids = [.. facts.Select(fact => fact.Id)];
        Dictionary<Guid, ProjectFinancialFacts> byId = facts.ToDictionary(fact => fact.Id);
        List<ProjectInvoice> open = await dbContext.ProjectInvoices.AsNoTracking()
            .Where(row => ids.Contains(row.ProjectId) && (row.Status == InvoiceStatus.Sent || row.Status == InvoiceStatus.Draft))
            .ToListAsync(cancellationToken);

        return new BillingRun(
            first,
            [.. ready.OrderByDescending(row => row.Amount)],
            [.. open.Where(row => row.IsOverdue(today)).OrderBy(row => row.DueDate).Select(row => new OverdueInvoice(
                row.ProjectId, byId[row.ProjectId].Code, row.Id, row.Number, row.DueDate!.Value, row.OpenAmount, byId[row.ProjectId].Currency, today.DayNumber - row.DueDate!.Value.DayNumber))],
            open.Count(row => row.Status == InvoiceStatus.Draft));
    }

    /// <summary>Invoices that went out in the period and credit notes against them, for the accounting system. Drafts are not the books' business.</summary>
    public async Task<ExportFile> ExportAsync(ExportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyCollection<Guid>? visible = await projectPermissions.VisibleIdsAsync(Permissions.Project.FinancialsView, cancellationToken);
        IQueryable<ProjectInvoice> invoices = dbContext.ProjectInvoices.AsNoTracking()
            .Where(row => row.InvoiceDate >= query.From && row.InvoiceDate <= query.To && (row.Status == InvoiceStatus.Sent || row.Status == InvoiceStatus.Paid));
        IQueryable<ProjectCreditNote> notes = dbContext.ProjectCreditNotes.AsNoTracking().Where(row => row.Date >= query.From && row.Date <= query.To);
        if (visible is not null)
        {
            invoices = invoices.Where(row => visible.Contains(row.ProjectId));
            notes = notes.Where(row => visible.Contains(row.ProjectId));
        }

        List<ProjectInvoice> sent = await invoices.ToListAsync(cancellationToken);
        List<ProjectCreditNote> credits = await notes.ToListAsync(cancellationToken);
        List<Guid> creditedIds = [.. credits.Select(row => row.InvoiceId).Distinct()];
        Dictionary<Guid, string> creditedNumbers = await dbContext.ProjectInvoices.AsNoTracking().Where(row => creditedIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Number, cancellationToken);

        IReadOnlyDictionary<Guid, ProjectSummary> summaries = await projects.GetManyAsync(sent.Select(row => row.ProjectId).Concat(credits.Select(row => row.ProjectId)).Distinct(), cancellationToken);
        IReadOnlyDictionary<Guid, ClientSummary> clientsById = await clients.GetManyAsync(summaries.Values.Select(project => project.ClientId).OfType<Guid>().Distinct(), cancellationToken);
        string Party(Guid projectId)
        {
            ProjectSummary? project = summaries.GetValueOrDefault(projectId);
            return project?.ClientId is { } client && clientsById.TryGetValue(client, out ClientSummary? found) ? found.Name : project?.Name ?? "Unknown";
        }

        List<ExportVoucher> vouchers =
        [
            .. sent.Select(row => new ExportVoucher(false, row.Number, row.InvoiceDate, Party(row.ProjectId), row.Amount, row.TaxAmount, $"{summaries.GetValueOrDefault(row.ProjectId)?.Code} {row.Notes}".Trim())),
            .. credits.Select(row => new ExportVoucher(true, row.Number, row.Date, Party(row.ProjectId), row.Amount, row.TaxAmount, $"Against {creditedNumbers.GetValueOrDefault(row.InvoiceId)}: {row.Reason}")),
        ];
        vouchers.Sort((first, second) => first.Date != second.Date ? first.Date.CompareTo(second.Date) : string.CompareOrdinal(first.Number, second.Number));

        string stamp = $"{query.From:yyyyMMdd}-{query.To:yyyyMMdd}";
        if (string.Equals(query.Format, "tally", StringComparison.OrdinalIgnoreCase))
        {
            var ledgers = new ExportLedgers(query.Company?.Trim() ?? string.Empty, Default(query.SalesLedger, "Sales"), Default(query.TaxLedger, "Output GST"));
            if (ledgers.Company.Length == 0)
            {
                throw new DomainRuleException("BILLING_EXPORT_COMPANY", "Say which Tally company these go into.");
            }

            return new ExportFile(Encoding.UTF8.GetBytes(InvoiceExport.TallyXml(ledgers, vouchers)), "application/xml", $"invoices-{stamp}.xml");
        }

        return new ExportFile(Encoding.UTF8.GetBytes(InvoiceExport.Csv(vouchers)), "text/csv", $"invoices-{stamp}.csv");
    }

    private static string Default(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static CreditNoteItem Item(ProjectCreditNote note) => new(note.Id, note.InvoiceId, note.Number, note.Date, note.Amount, note.TaxAmount, note.Reason);

    private async Task<BillingPreview> PreviewOfAsync(ProjectFinancialFacts fact, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        BillingPlan plan = await PlanAsync(fact, from, to, cancellationToken);

        // An invoice typed by hand says nothing about which hours or months it covered, so a person has to check.
        IQueryable<ProjectInvoiceLine> invoiceLines = dbContext.ProjectInvoiceLines;
        List<string> typed = await dbContext.ProjectInvoices.AsNoTracking()
            .Where(row => row.ProjectId == fact.Id && row.Status != InvoiceStatus.Cancelled && (row.PeriodFrom == null || row.PeriodFrom <= to) && (row.PeriodTo == null || row.PeriodTo >= from))
            .Where(row => !invoiceLines.Any(line => line.InvoiceId == row.Id))
            .Select(row => row.Number)
            .ToListAsync(cancellationToken);
        return new BillingPreview(
            fact.Id, fact.Code, fact.Name, fact.BillingType, fact.Currency, from, to,
            [.. plan.Lines.Select(line => new PlannedLineItem(line.Kind, line.Description, line.Quantity, line.Rate, line.Amount, line.Month))],
            plan.Amount, plan.HeldByCap, plan.UnpricedHours, typed, plan.CreditsWaiting);
    }

    private async Task<BillingPlan> PlanAsync(ProjectFinancialFacts fact, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Guid id = fact.Id;
        DateOnly hoursTo = to > today ? to : today;
        IReadOnlyList<LoggedHours> hours = await timesheets.ProjectHoursAsync([id], fact.StartDate.AddMonths(-1), hoursTo, approvedOnly: true, cancellationToken);
        ProjectFinanceProfile? profile = await dbContext.ProjectFinanceProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.ProjectId == id, cancellationToken);
        decimal? contract = profile?.ContractValue ?? (fact.ContractId is { } contractId ? (await contracts.GetManyAsync([contractId], cancellationToken)).GetValueOrDefault(contractId)?.Value : null);
        List<ProjectRate> rates = await dbContext.ProjectRates.AsNoTracking().Where(row => row.ProjectId == id).ToListAsync(cancellationToken);

        var input = new FinanceInput(fact, contract, profile?.RetainerMonthlyFee, profile?.MilestoneAmounts ?? new Dictionary<Guid, decimal>(), hours, rates, [], [], [], today)
        {
            Terms = profile?.Terms ?? new CommercialTerms(),
            ServiceCredits = (await projects.ServiceCreditsAsync([id], cancellationToken)).ToDictionary(row => row.Month, row => row.Percent),
            OutcomeUnits = await dbContext.ProjectOutcomeUnits.AsNoTracking().Where(row => row.ProjectId == id).ToListAsync(cancellationToken),
        };

        Dictionary<Guid, string> names = (await employees.GetManyAsync(hours.Select(row => row.EmployeeId).Concat(fact.Team.Select(seat => seat.EmployeeId)).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        return BillingPlanner.Plan(input, from, to, await AlreadyBilledAsync(id, cancellationToken), employee => names.GetValueOrDefault(employee) ?? "Unknown");
    }

    private async Task<AlreadyBilled> AlreadyBilledAsync(Guid projectId, CancellationToken cancellationToken)
    {
        List<ProjectInvoice> invoices = await dbContext.ProjectInvoices.AsNoTracking().Where(row => row.ProjectId == projectId && row.Status != InvoiceStatus.Cancelled).ToListAsync(cancellationToken);
        List<Guid> live = [.. invoices.Select(row => row.Id)];
        var lines = await dbContext.ProjectInvoiceLines.AsNoTracking()
            .Where(row => live.Contains(row.InvoiceId) && row.CreditNoteId == null)
            .Select(row => new { row.InvoiceId, row.Kind, row.Month, row.ReferenceId, row.Amount })
            .ToListAsync(cancellationToken);
        List<Guid> built = await dbContext.ProjectInvoiceLines.AsNoTracking().Where(row => live.Contains(row.InvoiceId)).Select(row => row.InvoiceId).Distinct().ToListAsync(cancellationToken);
        List<Guid> entries = await dbContext.ProjectBilledHours.AsNoTracking().Where(row => row.ProjectId == projectId).Select(row => row.TimesheetEntryId).ToListAsync(cancellationToken);
        // A milestone named on an invoice typed by hand has been billed too.
        IEnumerable<(InvoiceLineKind, DateOnly?, Guid?)> typedMilestones = invoices.Where(row => row.MilestoneId is not null && !built.Contains(row.Id)).Select(row => (InvoiceLineKind.Milestone, (DateOnly?)null, row.MilestoneId));
        return new AlreadyBilled(
            entries.ToHashSet(),
            lines.Select(row => (row.Kind, row.Month, row.ReferenceId)).Concat(typedMilestones).ToHashSet(),
            invoices.Sum(row => row.OpenAmount))
        {
            Overage = lines.Where(row => row.Kind == InvoiceLineKind.Overage && row.Month is not null).GroupBy(row => row.Month!.Value).ToDictionary(group => group.Key, group => group.Sum(row => row.Amount)),
            ServiceCredits = lines.Where(row => row.Kind == InvoiceLineKind.ServiceCredit && row.Month is not null).GroupBy(row => row.Month!.Value).ToDictionary(group => group.Key, group => -group.Sum(row => row.Amount)),
        };
    }

    private async Task<ProjectFinancialFacts> FactAsync(Guid projectId, CancellationToken cancellationToken)
        => await projects.FinancialFactsAsync([projectId], cancellationToken) is [var found, ..] ? found : throw new NotFoundException("Project", projectId);

    private async Task EnsureAsync(Guid projectId, string permission, CancellationToken cancellationToken)
    {
        if (!await projectPermissions.CanAsync(projectId, permission, cancellationToken))
        {
            throw new NotFoundException("Project", projectId);
        }
    }

    private async Task<string> NextCreditNumberAsync(DateOnly date, CancellationToken cancellationToken)
    {
        string prefix = $"CN-{date.Year}-";
        List<string> numbers = await dbContext.ProjectCreditNotes.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(row => row.Number.StartsWith(prefix)).Select(row => row.Number).ToListAsync(cancellationToken);
        int next = numbers.Select(number => int.TryParse(number[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:0000}";
    }
}

/// <summary>Whether hours have been invoiced, for the timesheet module deciding whether they can be reopened.</summary>
internal sealed class BilledHoursLookup(AppDbContext dbContext) : IBilledHours
{
    public Task<bool> AnyBilledAsync(IEnumerable<Guid> timesheetEntryIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. timesheetEntryIds.Distinct()];
        return ids.Count == 0 ? Task.FromResult(false) : dbContext.ProjectBilledHours.AnyAsync(row => ids.Contains(row.TimesheetEntryId), cancellationToken);
    }
}
