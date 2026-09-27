using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Employees;

public enum EmploymentType
{
    Permanent = 1,
    Contract = 2,
    Intern = 3,
    Consultant = 4,
}

/// <summary>
/// What actually happened on the date this record starts. Recorded rather than inferred, because
/// "why did this person's department change" is a question payroll, HR and the employee themselves
/// all eventually ask, and inferring it from a diff of the previous record cannot recover a reason.
/// </summary>
public enum EmploymentChangeType
{
    Hire = 1,
    Transfer = 2,
    Promotion = 3,
    Redesignation = 4,
    ManagerChange = 5,

    /// <summary>Fixing a mistake in a previous record, not a real organizational event.</summary>
    Correction = 6,

    /// <summary>A former employee's second stint - kept apart from <see cref="Hire"/> so the timeline shows they left and came back, not that they joined twice.</summary>
    Rehire = 7,
}

/// <summary>
/// One effective-dated slice of an employee's job: where they sit, what they are called, who they
/// report to.
/// <para>
/// A row here is never edited to reflect a change - a change ends the current row and adds a new
/// one, which is what "effective-dated" means and what makes "what was Priya's department on 1
/// March" an ordinary query rather than something lost the moment it changes. The one exception is
/// <see cref="EmploymentChangeType.Correction"/>: fixing a typo in a record that was never live is
/// not a new organizational event and edits in place.
/// </para>
/// </summary>
public sealed class EmploymentRecord : TenantAggregateRoot
{
    private EmploymentRecord()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Null means this is the open-ended, currently live record.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public bool IsCurrent { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid DesignationId { get; private set; }

    public Guid? GradeId { get; private set; }

    public Guid? CostCenterId { get; private set; }

    /// <summary>The employee this one reports to. Null for someone with no manager - typically the most senior role in the tenant.</summary>
    public Guid? ReportingManagerId { get; private set; }

    public EmploymentType EmploymentType { get; private set; }

    public DateOnly? ProbationEndDate { get; private set; }

    public EmploymentChangeType ChangeType { get; private set; }

    public string? ChangeReason { get; private set; }

    public static EmploymentRecord Create(
        Guid employeeId,
        DateOnly effectiveFrom,
        Guid legalEntityId,
        Guid branchId,
        Guid departmentId,
        Guid designationId,
        Guid? gradeId,
        Guid? costCenterId,
        Guid? reportingManagerId,
        EmploymentType employmentType,
        EmploymentChangeType changeType,
        string? changeReason,
        DateOnly? probationEndDate = null)
    {
        Guard.NotEmpty(employeeId);
        Guard.NotEmpty(legalEntityId);
        Guard.NotEmpty(branchId);
        Guard.NotEmpty(departmentId);
        Guard.NotEmpty(designationId);

        Guard.Against(
            reportingManagerId == employeeId,
            "EMPLOYMENT_RECORD_CANNOT_REPORT_TO_SELF",
            "An employee cannot be their own reporting manager.");

        return new EmploymentRecord
        {
            EmployeeId = employeeId,
            EffectiveFrom = effectiveFrom,
            IsCurrent = true,
            LegalEntityId = legalEntityId,
            BranchId = branchId,
            DepartmentId = departmentId,
            DesignationId = designationId,
            GradeId = gradeId,
            CostCenterId = costCenterId,
            ReportingManagerId = reportingManagerId,
            EmploymentType = employmentType,
            ProbationEndDate = probationEndDate,
            ChangeType = changeType,
            ChangeReason = string.IsNullOrWhiteSpace(changeReason) ? null : Guard.MaxLength(changeReason.Trim(), 500),
        };
    }

    /// <summary>
    /// Closes this record the day before a successor's own <paramref name="nextEffectiveFrom"/>
    /// begins.
    /// <para>
    /// Takes the successor's start date rather than an end date directly, and subtracts the day
    /// itself, because <see cref="EffectiveTo"/> is inclusive - the record is still current on that
    /// date - matching <c>UserRole.ValidTo</c>'s convention. Closing this record on the successor's
    /// own first day, rather than the day before it, would leave two records both current for that
    /// one day, and "the employee's current department" would stop being a well-defined question.
    /// </para>
    /// </summary>
    public void EndOn(DateOnly nextEffectiveFrom)
    {
        Guard.Against(!IsCurrent, "EMPLOYMENT_RECORD_NOT_CURRENT", "Only the current employment record can be ended.");

        DateOnly effectiveTo = nextEffectiveFrom.AddDays(-1);

        Guard.Against(
            effectiveTo < EffectiveFrom,
            "EMPLOYMENT_RECORD_DATES_INVALID",
            "A new employment record cannot take effect before the current one started.");

        EffectiveTo = effectiveTo;
        IsCurrent = false;
    }
}
