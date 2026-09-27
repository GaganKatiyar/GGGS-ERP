using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Domain.Employees;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

/// <summary>
/// The effective-dating rule this aggregate exists to enforce, plus the guards that keep a record's
/// own references sane. <see cref="EmployeeService"/>'s tests cover the cross-entity checks (does
/// the department actually exist); this is only about what the record can decide on its own.
/// </summary>
public sealed class EmploymentRecordTests
{
    private static readonly DateOnly EffectiveFrom = new(2026, 1, 15);

    [Fact]
    public void CreatingARecordThatReportsToItself_IsRejected()
    {
        Guid employeeId = Guid.NewGuid();

        DomainRuleException exception = Should.Throw<DomainRuleException>(() => Create(employeeId: employeeId, reportingManagerId: employeeId));

        exception.ErrorCode.ShouldBe("EMPLOYMENT_RECORD_CANNOT_REPORT_TO_SELF");
    }

    [Fact]
    public void ANewRecord_StartsAsCurrentWithNoEndDate()
    {
        EmploymentRecord record = Create();

        record.IsCurrent.ShouldBeTrue();
        record.EffectiveTo.ShouldBeNull();
    }

    [Fact]
    public void EndingARecord_ClosesItTheDayBeforeTheSuccessorStarts()
    {
        // EffectiveTo is inclusive - the record is still "current" through that date - so ending it
        // on the successor's own first day would leave both records current on the transition day.
        EmploymentRecord record = Create();
        DateOnly successorStart = EffectiveFrom.AddMonths(6);

        record.EndOn(successorStart);

        record.EffectiveTo.ShouldBe(successorStart.AddDays(-1));
        record.IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public void EndingARecordBeforeItEverStarted_IsRejected()
    {
        EmploymentRecord record = Create();

        DomainRuleException exception = Should.Throw<DomainRuleException>(() => record.EndOn(EffectiveFrom));

        // A successor effective on the same day the current record started would compute an
        // EffectiveTo of the day before EffectiveFrom - an inverted range this guard refuses.
        exception.ErrorCode.ShouldBe("EMPLOYMENT_RECORD_DATES_INVALID");
    }

    [Fact]
    public void EndingARecordThatIsAlreadyEnded_IsRejected()
    {
        EmploymentRecord record = Create();
        record.EndOn(EffectiveFrom.AddMonths(6));

        DomainRuleException exception = Should.Throw<DomainRuleException>(() => record.EndOn(EffectiveFrom.AddMonths(9)));

        exception.ErrorCode.ShouldBe("EMPLOYMENT_RECORD_NOT_CURRENT");
    }

    private static EmploymentRecord Create(Guid? employeeId = null, Guid? reportingManagerId = null)
        => EmploymentRecord.Create(
            employeeId ?? Guid.NewGuid(),
            EffectiveFrom,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            gradeId: null,
            costCenterId: null,
            reportingManagerId,
            EmploymentType.Permanent,
            EmploymentChangeType.Hire,
            "Hired");
}
