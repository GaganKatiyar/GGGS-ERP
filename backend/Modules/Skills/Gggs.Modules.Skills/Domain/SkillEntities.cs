using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Skills.Domain;

/// <summary>A skill people can have (spec §29): ".NET", "Kubernetes", "Retail domain", "Negotiation".</summary>
public sealed class Skill : TenantAggregateRoot
{
    private Skill()
    {
        Code = null!;
        Name = null!;
        Category = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Technical, Tool, Domain, Soft, Language, Certification… free text, so tenants group as they like.</summary>
    public string Category { get; private set; }

    public string? Description { get; private set; }

    public static Skill Create(string code, string name, string category, string? description)
    {
        var skill = new Skill { Code = NormalizeCode(code) };
        skill.Update(name, category, description);
        return skill;
    }

    public static string NormalizeCode(string code) => Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32);

    public void Update(string name, string category, string? description)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        Category = Guard.MaxLength(Guard.NotNullOrWhiteSpace(category).Trim(), 50);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 500);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public enum AssessmentSource
{
    Self = 1,
    Manager = 2,
    Assessment = 3,
    Certification = 4,
    Project = 5,
    Imported = 6,
}

/// <summary>
/// How good one person is at one skill (spec §29): current and target level on a 1-5 scale, years of
/// experience, and who said so. Skill gap = target - current.
/// </summary>
public sealed class EmployeeSkill : TenantAggregateRoot
{
    private EmployeeSkill()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid SkillId { get; private set; }

    public int CurrentLevel { get; private set; }

    public int? TargetLevel { get; private set; }

    public decimal? YearsExperience { get; private set; }

    /// <summary>A primary skill is what the person is staffed for; the rest are secondary.</summary>
    public bool IsPrimary { get; private set; }

    public DateOnly LastAssessedOn { get; private set; }

    public AssessmentSource Source { get; private set; }

    public Guid? AssessedByEmployeeId { get; private set; }

    public int Gap => TargetLevel is { } target ? Math.Max(0, target - CurrentLevel) : 0;

    public static EmployeeSkill Record(Guid employeeId, Guid skillId) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        SkillId = Guard.NotEmpty(skillId),
    };

    public void Assess(int currentLevel, int? targetLevel, decimal? yearsExperience, bool isPrimary, AssessmentSource source, Guid? assessedBy, DateOnly on)
    {
        if (currentLevel is < 1 or > 5 || targetLevel is < 1 or > 5)
        {
            throw new DomainRuleException("SKILL_LEVEL", "Skill levels run from 1 (aware) to 5 (expert).");
        }

        if (yearsExperience is < 0 or > 60)
        {
            throw new DomainRuleException("SKILL_EXPERIENCE", "Years of experience must be between 0 and 60.");
        }

        if (!Enum.IsDefined(source))
        {
            throw new DomainRuleException("SKILL_SOURCE", "Unknown assessment source.");
        }

        CurrentLevel = currentLevel;
        TargetLevel = targetLevel;
        YearsExperience = yearsExperience;
        IsPrimary = isPrimary;
        Source = source;
        AssessedByEmployeeId = assessedBy;
        LastAssessedOn = on;
    }
}

/// <summary>A professional certification (AWS Solutions Architect, PMP) with its expiry.</summary>
public sealed class EmployeeCertification : TenantAggregateRoot
{
    private EmployeeCertification()
    {
        Name = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string Name { get; private set; }

    public string? Issuer { get; private set; }

    public string? CredentialId { get; private set; }

    public DateOnly? IssuedOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>The skill this certification evidences, when there is one.</summary>
    public Guid? SkillId { get; private set; }

    public static EmployeeCertification Record(Guid employeeId, string name, string? issuer, string? credentialId, DateOnly? issuedOn, DateOnly? expiresOn, Guid? skillId)
    {
        var certification = new EmployeeCertification { EmployeeId = Guard.NotEmpty(employeeId) };
        certification.Update(name, issuer, credentialId, issuedOn, expiresOn, skillId);
        return certification;
    }

    public void Update(string name, string? issuer, string? credentialId, DateOnly? issuedOn, DateOnly? expiresOn, Guid? skillId)
    {
        if (issuedOn is { } issued && expiresOn is { } expires && expires < issued)
        {
            throw new DomainRuleException("SKILL_CERTIFICATION_DATES", "A certification cannot expire before it was issued.");
        }

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);
        Issuer = string.IsNullOrWhiteSpace(issuer) ? null : Guard.MaxLength(issuer.Trim(), 200);
        CredentialId = string.IsNullOrWhiteSpace(credentialId) ? null : Guard.MaxLength(credentialId.Trim(), 100);
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        SkillId = skillId;
    }

    public bool IsCurrent(DateOnly today) => ExpiresOn is null || ExpiresOn >= today;
}
