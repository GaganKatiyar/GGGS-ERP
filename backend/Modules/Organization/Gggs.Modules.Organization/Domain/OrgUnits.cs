using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Organization.Domain;

/// <summary>
/// What every organization record has: a code the tenant chooses, a name, and an active flag.
/// <para>
/// The code is unique per tenant within its kind and is what imports and integrations match on, so
/// it is trimmed and upper-cased once here rather than compared loosely everywhere else. Records are
/// deactivated rather than deleted: employees, positions and history point at them.
/// </para>
/// </summary>
public abstract class OrgUnit : TenantAggregateRoot
{
    protected OrgUnit()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; protected set; }

    public string Name { get; protected set; }

    public static string NormalizeCode(string code)
        => Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32);

    protected void SetIdentity(string code, string name)
    {
        Code = NormalizeCode(code);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
    }

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

/// <summary>A postal address, stored with the record it belongs to. Country and state are lookup codes.</summary>
public sealed record PostalAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? StateCode,
    string? CountryCode,
    string? PostalCode);

/// <summary>
/// A registered company - the employer of record, the payroll and statutory boundary (spec §7, §10).
/// </summary>
public sealed class LegalEntity : OrgUnit
{
    private LegalEntity()
    {
    }

    public string? LegalName { get; private set; }

    public string CountryCode { get; private set; } = "IN";

    public string CurrencyCode { get; private set; } = "INR";

    public PostalAddress? Address { get; private set; }

    /// <summary>Company registration identifiers. Public registry data, so not encrypted.</summary>
    public string? Gstin { get; private set; }

    public string? Pan { get; private set; }

    public string? Cin { get; private set; }

    public string? Tan { get; private set; }

    public static LegalEntity Create(string code, string name, string countryCode, string currencyCode)
    {
        var entity = new LegalEntity();
        entity.SetIdentity(code, name);
        entity.CountryCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(countryCode).Trim().ToUpperInvariant(), 8);
        entity.CurrencyCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currencyCode).Trim().ToUpperInvariant(), 3);
        return entity;
    }

    public void UpdateDetails(string name, string? legalName, string countryCode, string currencyCode, PostalAddress? address)
    {
        Rename(name);
        LegalName = Trimmed(legalName, 300);
        CountryCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(countryCode).Trim().ToUpperInvariant(), 8);
        CurrencyCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currencyCode).Trim().ToUpperInvariant(), 3);
        Address = address;
    }

    public void UpdateRegistration(string? gstin, string? pan, string? cin, string? tan)
    {
        Gstin = Upper(gstin, 15);
        Pan = Upper(pan, 10);
        Cin = Upper(cin, 21);
        Tan = Upper(tan, 10);
    }

    private static string? Trimmed(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), max);

    private static string? Upper(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim().ToUpperInvariant(), max);
}

/// <summary>A line of business, optionally inside one company.</summary>
public sealed class BusinessUnit : OrgUnit
{
    private BusinessUnit()
    {
    }

    public Guid? LegalEntityId { get; private set; }

    public Guid? HeadEmployeeId { get; private set; }

    public static BusinessUnit Create(string code, string name, Guid? legalEntityId, Guid? headEmployeeId)
    {
        var unit = new BusinessUnit();
        unit.SetIdentity(code, name);
        unit.Assign(legalEntityId, headEmployeeId);
        return unit;
    }

    public void Assign(Guid? legalEntityId, Guid? headEmployeeId)
    {
        LegalEntityId = legalEntityId;
        HeadEmployeeId = headEmployeeId;
    }
}

/// <summary>A grouping of departments inside a business unit.</summary>
public sealed class Division : OrgUnit
{
    private Division()
    {
    }

    public Guid? BusinessUnitId { get; private set; }

    public Guid? HeadEmployeeId { get; private set; }

    public static Division Create(string code, string name, Guid? businessUnitId, Guid? headEmployeeId)
    {
        var division = new Division();
        division.SetIdentity(code, name);
        division.Assign(businessUnitId, headEmployeeId);
        return division;
    }

    public void Assign(Guid? businessUnitId, Guid? headEmployeeId)
    {
        BusinessUnitId = businessUnitId;
        HeadEmployeeId = headEmployeeId;
    }
}

/// <summary>Where a cost is booked.</summary>
public sealed class CostCenter : OrgUnit
{
    private CostCenter()
    {
    }

    public Guid? OwnerEmployeeId { get; private set; }

    public static CostCenter Create(string code, string name, Guid? ownerEmployeeId)
    {
        var center = new CostCenter();
        center.SetIdentity(code, name);
        center.OwnerEmployeeId = ownerEmployeeId;
        return center;
    }

    public void SetOwner(Guid? ownerEmployeeId) => OwnerEmployeeId = ownerEmployeeId;
}

/// <summary>Where revenue and margin are reported.</summary>
public sealed class ProfitCenter : OrgUnit
{
    private ProfitCenter()
    {
    }

    public Guid? OwnerEmployeeId { get; private set; }

    public static ProfitCenter Create(string code, string name, Guid? ownerEmployeeId)
    {
        var center = new ProfitCenter();
        center.SetIdentity(code, name);
        center.OwnerEmployeeId = ownerEmployeeId;
        return center;
    }

    public void SetOwner(Guid? ownerEmployeeId) => OwnerEmployeeId = ownerEmployeeId;
}

/// <summary>A physical or virtual place people work from.</summary>
public sealed class Location : OrgUnit
{
    private Location()
    {
    }

    public Guid? LegalEntityId { get; private set; }

    public PostalAddress? Address { get; private set; }

    public string? TimeZone { get; private set; }

    public static Location Create(string code, string name, Guid? legalEntityId, PostalAddress? address, string? timeZone)
    {
        var location = new Location();
        location.SetIdentity(code, name);
        location.Update(legalEntityId, address, timeZone);
        return location;
    }

    public void Update(Guid? legalEntityId, PostalAddress? address, string? timeZone)
    {
        LegalEntityId = legalEntityId;
        Address = address;
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? null : Guard.MaxLength(timeZone.Trim(), 64);
    }
}

/// <summary>A pay and seniority band. <see cref="Rank"/> orders grades from junior to senior.</summary>
public sealed class Grade : OrgUnit
{
    private Grade()
    {
    }

    public int Rank { get; private set; }

    public static Grade Create(string code, string name, int rank)
    {
        var grade = new Grade();
        grade.SetIdentity(code, name);
        grade.Rank = rank;
        return grade;
    }

    public void SetRank(int rank) => Rank = rank;
}

/// <summary>A career level (L1 … L8), used by jobs and career paths. <see cref="Rank"/> orders them.</summary>
public sealed class JobLevel : OrgUnit
{
    private JobLevel()
    {
    }

    public int Rank { get; private set; }

    public static JobLevel Create(string code, string name, int rank)
    {
        var level = new JobLevel();
        level.SetIdentity(code, name);
        level.Rank = rank;
        return level;
    }

    public void SetRank(int rank) => Rank = rank;
}

/// <summary>
/// A department, possibly inside another (spec §10). Effective-dated: closing a department ends it
/// on a date rather than deleting it, so history still says where people sat.
/// </summary>
public sealed class Department : OrgUnit
{
    private Department()
    {
    }

    public Guid? ParentDepartmentId { get; private set; }

    public Guid? DivisionId { get; private set; }

    public Guid? HeadEmployeeId { get; private set; }

    public Guid? CostCenterId { get; private set; }

    public Guid? ProfitCenterId { get; private set; }

    public Guid? LocationId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public static Department Create(string code, string name, DateOnly effectiveFrom)
    {
        var department = new Department();
        department.SetIdentity(code, name);
        department.EffectiveFrom = effectiveFrom;
        return department;
    }

    public void Place(
        Guid? parentDepartmentId,
        Guid? divisionId,
        Guid? headEmployeeId,
        Guid? costCenterId,
        Guid? profitCenterId,
        Guid? locationId)
    {
        Guard.Against(parentDepartmentId == Id, "DEPARTMENT_OWN_PARENT", "A department cannot be its own parent.");

        ParentDepartmentId = parentDepartmentId;
        DivisionId = divisionId;
        HeadEmployeeId = headEmployeeId;
        CostCenterId = costCenterId;
        ProfitCenterId = profitCenterId;
        LocationId = locationId;
    }

    /// <summary>Ends the department on <paramref name="effectiveTo"/>; it stays for history.</summary>
    public void Close(DateOnly effectiveTo)
    {
        Guard.Against(effectiveTo < EffectiveFrom, "DEPARTMENT_CLOSE_BEFORE_START", "A department cannot close before it opened.");
        EffectiveTo = effectiveTo;
        Deactivate();
    }
}

/// <summary>
/// A role definition (spec §10): what the work is, at what level, needing what experience. Many
/// <see cref="Position"/>s can be seats for the same job.
/// </summary>
public sealed class Job : OrgUnit
{
    private readonly List<string> _requiredSkills = [];

    private Job()
    {
    }

    /// <summary>A <c>job-family</c> lookup code.</summary>
    public string? JobFamilyCode { get; private set; }

    public Guid? JobLevelId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public decimal? MinimumExperienceYears { get; private set; }

    public decimal? MaximumExperienceYears { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Skill names until the Skills module (M6) gives them identities.</summary>
    public IReadOnlyList<string> RequiredSkills => _requiredSkills;

    public static Job Create(string code, string title)
    {
        var job = new Job();
        job.SetIdentity(code, title);
        return job;
    }

    public void Define(
        string? jobFamilyCode,
        Guid? jobLevelId,
        Guid? departmentId,
        decimal? minimumExperienceYears,
        decimal? maximumExperienceYears,
        string? description,
        IEnumerable<string>? requiredSkills)
    {
        Guard.Against(
            minimumExperienceYears < 0 || maximumExperienceYears < 0,
            "JOB_EXPERIENCE_NEGATIVE",
            "Experience cannot be negative.");
        Guard.Against(
            minimumExperienceYears is not null && maximumExperienceYears is not null && maximumExperienceYears < minimumExperienceYears,
            "JOB_EXPERIENCE_RANGE",
            "Maximum experience cannot be less than minimum experience.");

        JobFamilyCode = string.IsNullOrWhiteSpace(jobFamilyCode) ? null : jobFamilyCode.Trim().ToUpperInvariant();
        JobLevelId = jobLevelId;
        DepartmentId = departmentId;
        MinimumExperienceYears = minimumExperienceYears;
        MaximumExperienceYears = maximumExperienceYears;
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 4000);

        _requiredSkills.Clear();
        _requiredSkills.AddRange((requiredSkills ?? [])
            .Where(skill => !string.IsNullOrWhiteSpace(skill))
            .Select(skill => Guard.MaxLength(skill.Trim(), 100))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// An actual seat in the organization (spec §10): this job, in this department, reporting to that
/// position. Budgeted seats count towards headcount plans; occupancy is however many active
/// employees hold it right now. Effective-dated like departments.
/// </summary>
public sealed class Position : OrgUnit
{
    private Position()
    {
    }

    public Guid JobId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid? LocationId { get; private set; }

    public Guid? GradeId { get; private set; }

    public Guid? ReportsToPositionId { get; private set; }

    public bool IsBudgeted { get; private set; }

    /// <summary>How many people may hold this seat at once - usually one.</summary>
    public int Headcount { get; private set; } = 1;

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public static Position Create(string code, string title, Guid jobId, Guid departmentId, DateOnly effectiveFrom)
    {
        var position = new Position();
        position.SetIdentity(code, title);
        position.JobId = Guard.NotEmpty(jobId);
        position.DepartmentId = Guard.NotEmpty(departmentId);
        position.EffectiveFrom = effectiveFrom;
        return position;
    }

    public void Configure(
        Guid jobId,
        Guid departmentId,
        Guid? locationId,
        Guid? gradeId,
        Guid? reportsToPositionId,
        bool isBudgeted,
        int headcount)
    {
        Guard.Against(reportsToPositionId == Id, "POSITION_REPORTS_TO_ITSELF", "A position cannot report to itself.");
        Guard.Against(headcount < 1, "POSITION_HEADCOUNT", "A position needs a headcount of at least one.");

        JobId = Guard.NotEmpty(jobId);
        DepartmentId = Guard.NotEmpty(departmentId);
        LocationId = locationId;
        GradeId = gradeId;
        ReportsToPositionId = reportsToPositionId;
        IsBudgeted = isBudgeted;
        Headcount = headcount;
    }

    public void Close(DateOnly effectiveTo)
    {
        Guard.Against(effectiveTo < EffectiveFrom, "POSITION_CLOSE_BEFORE_START", "A position cannot close before it opened.");
        EffectiveTo = effectiveTo;
        Deactivate();
    }
}
