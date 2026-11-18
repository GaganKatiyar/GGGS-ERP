using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Organization.Domain;

/// <summary>
/// What every organization record has: a code the tenant chooses, a name, and an active flag.
/// <para>
/// The code is unique per tenant within its kind and is what imports and integrations match on, so
/// it is trimmed and upper-cased once here rather than compared loosely everywhere else. Records are
/// deactivated rather than deleted: employees and history point at them.
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

    /// <summary>
    /// Gives the record a new code. Other records point at it by id, so nothing breaks inside the
    /// app; only imports and integrations that name it by the old code need the new one.
    /// </summary>
    public void ChangeCode(string code) => Code = NormalizeCode(code);

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
public sealed class Company : OrgUnit
{
    private Company()
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

    public static Company Create(string code, string name, string countryCode, string currencyCode)
    {
        var entity = new Company();
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

    public Guid? CompanyId { get; private set; }

    public Guid? HeadEmployeeId { get; private set; }

    public static BusinessUnit Create(string code, string name, Guid? companyId, Guid? headEmployeeId)
    {
        var unit = new BusinessUnit();
        unit.SetIdentity(code, name);
        unit.Assign(companyId, headEmployeeId);
        return unit;
    }

    public void Assign(Guid? companyId, Guid? headEmployeeId)
    {
        CompanyId = companyId;
        HeadEmployeeId = headEmployeeId;
    }
}

/// <summary>
/// The company name an office works under: what offer letters and payslips print, and where its
/// costs are booked. Each company has one default cost center, carrying the company's own name, that
/// anyone without an office-specific one falls back to. The default cannot be deactivated.
/// </summary>
public sealed class CostCenter : OrgUnit
{
    private CostCenter()
    {
    }

    /// <summary>The parent company.</summary>
    public Guid? CompanyId { get; private set; }

    public bool IsDefault { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    /// <summary>Registration of this office's company, when it has its own. Public registry data.</summary>
    public string? Gstin { get; private set; }

    public string? Pan { get; private set; }

    public PostalAddress? Address { get; private set; }

    public static CostCenter Create(string code, string name, Guid? companyId, Guid? ownerEmployeeId)
    {
        var center = new CostCenter();
        center.SetIdentity(code, name);
        center.CompanyId = companyId;
        center.OwnerEmployeeId = ownerEmployeeId;
        return center;
    }

    /// <summary>The default cost center a new company starts with, named after it.</summary>
    public static CostCenter DefaultFor(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        CostCenter center = Create(company.Code, company.Name, company.Id, null);
        center.IsDefault = true;
        center.UpdateRegistration(company.Gstin, company.Pan, company.Address);
        return center;
    }

    public void Update(Guid? companyId, Guid? ownerEmployeeId)
    {
        Guard.Against(IsDefault && companyId != CompanyId, "COST_CENTER_DEFAULT_COMPANY", "A company's default cost center stays with that company.");
        CompanyId = companyId;
        OwnerEmployeeId = ownerEmployeeId;
    }

    public void UpdateRegistration(string? gstin, string? pan, PostalAddress? address)
    {
        Gstin = string.IsNullOrWhiteSpace(gstin) ? null : Guard.MaxLength(gstin.Trim().ToUpperInvariant(), 15);
        Pan = string.IsNullOrWhiteSpace(pan) ? null : Guard.MaxLength(pan.Trim().ToUpperInvariant(), 10);
        Address = address;
    }

    public void MakeDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;
}

/// <summary>A physical or virtual place people work from.</summary>
public sealed class Location : OrgUnit
{
    private Location()
    {
    }

    public Guid? CompanyId { get; private set; }

    /// <summary>The company name people at this office work under; null means the company default.</summary>
    public Guid? CostCenterId { get; private set; }

    public PostalAddress? Address { get; private set; }

    public string? TimeZone { get; private set; }

    public static Location Create(string code, string name, Guid? companyId, PostalAddress? address, string? timeZone, Guid? costCenterId = null)
    {
        var location = new Location();
        location.SetIdentity(code, name);
        location.Update(companyId, address, timeZone, costCenterId);
        return location;
    }

    public void Update(Guid? companyId, PostalAddress? address, string? timeZone, Guid? costCenterId)
    {
        CompanyId = companyId;
        CostCenterId = costCenterId;
        Address = address;
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? null : Guard.MaxLength(timeZone.Trim(), 64);
    }
}

/// <summary>
/// A title people hold - what offer letters, payslips and the directory print ("Senior Software
/// Engineer"). Most designations are a role at a level ("Business Analyst L2"): <see cref="RoleId"/>
/// and <see cref="LevelId"/> name both, and the role keeps them in step (<see cref="ForRole"/>).
/// One without a role is a free-standing title someone keeps by hand.
/// </summary>
public sealed class Designation : OrgUnit
{
    private Designation()
    {
    }

    public Guid? LevelId { get; private set; }

    /// <summary>The role this designation is a level of; null for a free-standing title.</summary>
    public Guid? RoleId { get; private set; }

    public static Designation Create(string code, string name, Guid? levelId, Guid? roleId = null)
    {
        var designation = new Designation();
        designation.SetIdentity(code, name);
        designation.LevelId = levelId;
        designation.RoleId = roleId;
        return designation;
    }

    /// <summary>The designation for <paramref name="role"/> at <paramref name="level"/>: code "TS-BA-L2", name "Business Analyst L2".</summary>
    public static Designation ForRole(Role role, Level level)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(level);
        return Create(RoleCode(role, level), RoleName(role, level), level.Id, role.Id);
    }

    public static string RoleCode(Role role, Level level) => NormalizeCode($"{role.Code}-{level.Code}");

    /// <summary>Whether "role code-level code" fits in a code (32 characters); a role whose code is too long gets no designations.</summary>
    public static bool FitsRoleCode(Role role, Level level) => role.Code.Length + 1 + level.Code.Length <= 32;

    public static string RoleName(Role role, Level level) => $"{role.Name} {level.Code}";

    public void SetLevel(Guid? levelId) => LevelId = levelId;

    public void SetRole(Guid? roleId) => RoleId = roleId;
}

/// <summary>How senior someone is (L1, L2, L3 …). <see cref="Rank"/> orders levels from junior to senior.</summary>
public sealed class Level : OrgUnit
{
    private Level()
    {
    }

    public int Rank { get; private set; }

    public static Level Create(string code, string name, int rank)
    {
        var level = new Level();
        level.SetIdentity(code, name);
        level.Rank = rank;
        return level;
    }

    public void SetRank(int rank) => Rank = rank;
}

/// <summary>
/// A department or one of its functions (spec §10). The structure is two levels: a department sits
/// in a business unit, and a function is a department whose <see cref="ParentDepartmentId"/> names
/// the department it belongs to. A function holds nothing beneath it. Effective-dated: closing ends
/// it on a date rather than deleting it, so history still says where people sat.
/// </summary>
public sealed class Department : OrgUnit
{
    private Department()
    {
    }

    /// <summary>The department this function belongs to; null for a department itself.</summary>
    public Guid? ParentDepartmentId { get; private set; }

    /// <summary>A function always takes its department's business unit.</summary>
    public Guid? BusinessUnitId { get; private set; }

    /// <summary>True for a function: a team inside a department.</summary>
    public bool IsFunction => ParentDepartmentId is not null;

    public Guid? HeadEmployeeId { get; private set; }

    public Guid? CostCenterId { get; private set; }

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
        Guid? businessUnitId,
        Guid? headEmployeeId,
        Guid? costCenterId,
        Guid? locationId)
    {
        Guard.Against(parentDepartmentId == Id, "DEPARTMENT_OWN_PARENT", "A department cannot be its own parent.");

        ParentDepartmentId = parentDepartmentId;
        BusinessUnitId = businessUnitId;
        HeadEmployeeId = headEmployeeId;
        CostCenterId = costCenterId;
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
/// A role (spec §10; shown as "Role"): what the work is, at what level, needing what experience.
/// Someone's role says what they do; their designation says what they are called.
/// </summary>
public sealed class Role : OrgUnit
{
    private readonly List<string> _requiredSkills = [];

    private Role()
    {
    }

    /// <summary>A <c>job-family</c> lookup code.</summary>
    public string? JobFamilyCode { get; private set; }

    public Guid? LevelId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public decimal? MinimumExperienceYears { get; private set; }

    public decimal? MaximumExperienceYears { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Skill names until the Skills module (M6) gives them identities.</summary>
    public IReadOnlyList<string> RequiredSkills => _requiredSkills;

    public static Role Create(string code, string title)
    {
        var role = new Role();
        role.SetIdentity(code, title);
        return role;
    }

    public void Define(
        string? jobFamilyCode,
        Guid? levelId,
        Guid? departmentId,
        decimal? minimumExperienceYears,
        decimal? maximumExperienceYears,
        string? description,
        IEnumerable<string>? requiredSkills)
    {
        Guard.Against(
            minimumExperienceYears < 0 || maximumExperienceYears < 0,
            "ROLE_EXPERIENCE_NEGATIVE",
            "Experience cannot be negative.");
        Guard.Against(
            minimumExperienceYears is not null && maximumExperienceYears is not null && maximumExperienceYears < minimumExperienceYears,
            "ROLE_EXPERIENCE_RANGE",
            "Maximum experience cannot be less than minimum experience.");

        JobFamilyCode = string.IsNullOrWhiteSpace(jobFamilyCode) ? null : jobFamilyCode.Trim().ToUpperInvariant();
        LevelId = levelId;
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
