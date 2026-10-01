using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

/// <summary>
/// One employee's published payslip for a <see cref="PayrollRun"/> - created automatically, one per
/// <see cref="PayrollRunEmployee"/>, the moment that run is finalized (see
/// <c>PayrollRunService.FinalizeAsync</c>). No PDF/document file in this v1 slice: there is no
/// rendering engine anywhere in this codebase yet (QuestPDF/Playwright - architecture.md §15), and
/// the Documents service's own upload path is shaped for a browser file upload, not a backend
/// service handing over bytes it just generated - see this slice's own research before building
/// that properly. A payslip here is a structured, read-only pointer at the already-computed
/// <see cref="PayrollRunEmployee"/>/<see cref="PayrollRunLine"/> rows: the frontend renders it
/// on-screen directly from that data. Immutable once created - a correction means reversing and
/// reissuing the run, not editing a payslip in place, matching how <see cref="PayrollRunEmployee"/>
/// itself is replaced wholesale rather than adjusted.
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
