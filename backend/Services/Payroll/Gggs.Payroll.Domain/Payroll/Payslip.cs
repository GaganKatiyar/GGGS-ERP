using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

/// <summary>
/// One employee's published payslip for a <see cref="PayrollRun"/> - created automatically, one per
/// <see cref="PayrollRunEmployee"/>, the moment that run is finalized (see
/// <c>PayrollRunService.FinalizeAsync</c>). No stored document file: this row is a structured,
/// read-only pointer at the already-computed <see cref="PayrollRunEmployee"/>/
/// <see cref="PayrollRunLine"/> rows, and both the on-screen view and the downloadable PDF
/// (<c>Gggs.Payroll.Api.Documents.PayslipPdfDocument</c>, rendered with QuestPDF) are built fresh
/// from that data on every request rather than stored - there is nothing to keep in sync, and no
/// need for the Documents service's browser-shaped upload path. Immutable once created - a
/// correction means reversing and reissuing the run, not editing a payslip in place, matching how
/// <see cref="PayrollRunEmployee"/> itself is replaced wholesale rather than adjusted.
/// </summary>
public sealed class Payslip : TenantAggregateRoot
{
    private Payslip()
    {
    }

    public Guid PayrollRunId { get; private set; }

    public Guid PayrollRunEmployeeId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateTime PublishedDate { get; private set; }

    public static Payslip Create(Guid payrollRunId, Guid payrollRunEmployeeId, Guid employeeId, DateTime publishedAtUtc) => new()
    {
        PayrollRunId = Guard.NotEmpty(payrollRunId),
        PayrollRunEmployeeId = Guard.NotEmpty(payrollRunEmployeeId),
        EmployeeId = Guard.NotEmpty(employeeId),
        PublishedDate = publishedAtUtc,
        IsActive = true,
    };
}
