using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Payroll.Domain.Payroll;

public enum PayPeriodStatus
{
    Open = 1,
    Locked = 2,
    Closed = 3,
}

/// <summary>
/// One pay cycle for a <see cref="PayGroup"/> - "September 2026." No <c>CutoffDate</c>/attendance
/// input window in this v1 slice (architecture.md's <c>PayPeriod.CutoffDate</c>): a period is either
/// open for a run to be calculated against it, or it is not. Locking a period is what a
/// <see cref="PayrollRun.Finalize"/> does automatically - see its own remarks - rather than a
/// separate action an operator takes.
/// </summary>
public sealed class PayPeriod : TenantAggregateRoot
{
    private PayPeriod()
    {
        PeriodCode = null!;
    }

    public Guid PayGroupId { get; private set; }

    public string PeriodCode { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public DateOnly PayDate { get; private set; }

    public PayPeriodStatus Status { get; private set; }

    public static PayPeriod Create(Guid payGroupId, string periodCode, DateOnly startDate, DateOnly endDate, DateOnly payDate)
    {
        Guard.Against(endDate < startDate, "PAY_PERIOD_END_BEFORE_START", "The period cannot end before it starts.");

        return new PayPeriod
        {
            PayGroupId = Guard.NotEmpty(payGroupId),
            PeriodCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(periodCode).Trim(), 20),
            StartDate = startDate,
            EndDate = endDate,
            PayDate = payDate,
            Status = PayPeriodStatus.Open,
            IsActive = true,
        };
    }

    /// <summary>Open -> Locked: no new run may be started against this period once it is locked.
    /// A run already in progress may still be calculated/finalized.</summary>
    public void Lock()
    {
        Guard.Against(Status != PayPeriodStatus.Open, "PAY_PERIOD_NOT_OPEN", $"Period '{PeriodCode}' must be Open to lock; it is {Status}.");
        Status = PayPeriodStatus.Locked;
    }

    /// <summary>Locked -> Closed: terminal, set once the period's run has been finalized.</summary>
    public void Close()
    {
        Guard.Against(Status != PayPeriodStatus.Locked, "PAY_PERIOD_NOT_LOCKED", $"Period '{PeriodCode}' must be Locked to close; it is {Status}.");
        Status = PayPeriodStatus.Closed;
    }
}
