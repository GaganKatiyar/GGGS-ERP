using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.ProjectFinancials.Domain;

/// <summary>What an invoice line bills for.</summary>
public enum InvoiceLineKind
{
    Hours = 1,
    Milestone = 2,
    MonthlyFee = 3,
    Overage = 4,
    Seat = 5,
    Progress = 6,
}

/// <summary>
/// One line of an invoice prepared from the work. <see cref="Month"/> and <see cref="ReferenceId"/> say what
/// it covers (a month's fee, a seat, a milestone), which is what stops the same thing being billed twice.
/// </summary>
public sealed class ProjectInvoiceLine : TenantAggregateRoot
{
    private ProjectInvoiceLine()
    {
        Description = null!;
    }

    public Guid InvoiceId { get; private set; }

    public Guid ProjectId { get; private set; }

    public InvoiceLineKind Kind { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal Rate { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly? Month { get; private set; }

    /// <summary>The milestone, or the team seat, this line bills.</summary>
    public Guid? ReferenceId { get; private set; }

    public Guid? CreditNoteId { get; private set; }

    public bool IsCredited => CreditNoteId is not null;

    public static ProjectInvoiceLine For(Guid invoiceId, Guid projectId, PlannedLine planned)
    {
        ArgumentNullException.ThrowIfNull(planned);
        return new ProjectInvoiceLine
        {
            InvoiceId = Guard.NotEmpty(invoiceId),
            ProjectId = Guard.NotEmpty(projectId),
            Kind = planned.Kind,
            Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(planned.Description).Trim(), 300),
            Quantity = planned.Quantity,
            Rate = planned.Rate,
            Amount = Math.Round(planned.Amount, 2),
            Month = planned.Month,
            ReferenceId = planned.ReferenceId,
        };
    }

    public void Credit(Guid creditNoteId) => CreditNoteId = Guard.NotEmpty(creditNoteId);
}

/// <summary>
/// A timesheet entry that has been invoiced. A unique index on the entry is the last word on double billing:
/// even two people preparing an invoice at once cannot bill the same hours. Cancelling or crediting removes the row.
/// </summary>
public sealed class ProjectBilledHours : TenantAggregateRoot
{
    private ProjectBilledHours()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public Guid LineId { get; private set; }

    public Guid TimesheetEntryId { get; private set; }

    public static ProjectBilledHours For(ProjectInvoiceLine line, Guid timesheetEntryId)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new ProjectBilledHours { ProjectId = line.ProjectId, InvoiceId = line.InvoiceId, LineId = line.Id, TimesheetEntryId = Guard.NotEmpty(timesheetEntryId) };
    }
}

/// <summary>Takes back some or all of an invoice that has gone out. The accounting system keeps the tax position; this says what was reversed and why.</summary>
public sealed class ProjectCreditNote : TenantAggregateRoot
{
    private ProjectCreditNote()
    {
        Number = null!;
        Reason = null!;
    }

    public Guid ProjectId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public string Number { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Amount { get; private set; }

    public decimal TaxAmount { get; private set; }

    public string Reason { get; private set; }

    public static ProjectCreditNote Raise(ProjectInvoice invoice, string number, DateOnly date, decimal amount, decimal taxAmount, string reason)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return new ProjectCreditNote
        {
            ProjectId = invoice.ProjectId,
            InvoiceId = invoice.Id,
            Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number).Trim(), 40),
            Date = date,
            Amount = Math.Round(amount, 2),
            TaxAmount = Math.Round(taxAmount, 2),
            Reason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 500),
        };
    }
}
