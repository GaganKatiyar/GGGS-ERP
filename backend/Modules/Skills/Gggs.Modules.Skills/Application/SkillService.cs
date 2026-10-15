using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Skills.Contracts;
using Gggs.Modules.Skills.Domain;
using Gggs.Modules.Skills.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Skills.Application;

public sealed record SkillItem(Guid Id, string Code, string Name, string Category, string? Description, bool IsActive, int PeopleCount);

public sealed record SkillBody(string Code, string Name, string Category, string? Description);

public sealed record EmployeeSkillItem(
    Guid Id,
    Guid SkillId,
    string SkillName,
    string Category,
    int CurrentLevel,
    int? TargetLevel,
    int Gap,
    decimal? YearsExperience,
    bool IsPrimary,
    DateOnly LastAssessedOn,
    AssessmentSource Source,
    string? AssessedByName);

public sealed record CertificationItem(Guid Id, string Name, string? Issuer, string? CredentialId, DateOnly? IssuedOn, DateOnly? ExpiresOn, Guid? SkillId, bool IsCurrent);

public sealed record SkillProfile(Guid EmployeeId, string? EmployeeName, IReadOnlyList<EmployeeSkillItem> Skills, IReadOnlyList<CertificationItem> Certifications);

public sealed record EmployeeSkillBody(Guid SkillId, int CurrentLevel, int? TargetLevel, decimal? YearsExperience, bool IsPrimary, AssessmentSource? Source);

public sealed record CertificationBody(string Name, string? Issuer, string? CredentialId, DateOnly? IssuedOn, DateOnly? ExpiresOn, Guid? SkillId);

public sealed record MatrixRow(Guid SkillId, string SkillName, string Category, IReadOnlyList<int> CountsByLevel, int People, decimal AverageLevel, int WithGap);

public sealed record SkilledPerson(Guid EmployeeId, string? EmployeeName, string? EmployeeCode, string? Designation, int Level, decimal? YearsExperience, bool IsPrimary);

/// <summary>
/// Skills and competency (spec §29): the catalog, who has which skill at what level - scoped like
/// employee records - certifications, and the skill matrix.
/// </summary>
internal sealed class SkillService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    // ----- Catalog ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SkillItem>> CatalogAsync(string? search, string? category, bool includeInactive, CancellationToken cancellationToken)
    {
        IQueryable<Skill> rows = dbContext.Skills.AsNoTracking().Where(skill => includeInactive || skill.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            rows = rows.Where(skill => skill.Name.Contains(term) || skill.Code.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            rows = rows.Where(skill => skill.Category == category);
        }

        List<Skill> skills = await rows.OrderBy(skill => skill.Category).ThenBy(skill => skill.Name).ToListAsync(cancellationToken);
        List<Guid> ids = [.. skills.Select(skill => skill.Id)];
        Dictionary<Guid, int> counts = await dbContext.EmployeeSkills.AsNoTracking()
            .Where(row => ids.Contains(row.SkillId))
            .GroupBy(row => row.SkillId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

        return [.. skills.Select(skill => new SkillItem(skill.Id, skill.Code, skill.Name, skill.Category, skill.Description, skill.IsActive, counts.GetValueOrDefault(skill.Id)))];
    }

    public async Task<SkillItem> SaveSkillAsync(Guid? id, SkillBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Skill skill;
        if (id is { } skillId)
        {
            skill = await dbContext.Skills.FirstOrDefaultAsync(row => row.Id == skillId, cancellationToken) ?? throw new NotFoundException(nameof(Skill), skillId);
            skill.Update(body.Name, body.Category, body.Description);
        }
        else
        {
            string code = Skill.NormalizeCode(body.Code ?? string.Empty);
            if (await dbContext.Skills.AnyAsync(row => row.Code == code, cancellationToken))
            {
                throw new DomainRuleException("SKILL_CODE_IN_USE", $"A skill with code '{code}' already exists.");
            }

            skill = Skill.Create(code, body.Name, body.Category, body.Description);
            dbContext.Skills.Add(skill);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SkillItem(skill.Id, skill.Code, skill.Name, skill.Category, skill.Description, skill.IsActive, 0);
    }

    public async Task SetSkillActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        Skill skill = await dbContext.Skills.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Skill), id);
        if (active)
        {
            skill.Activate();
        }
        else
        {
            skill.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- One person's skills ------------------------------------------------------------------

    public async Task<SkillProfile> ProfileAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        await EnsureScopeAsync(employeeId, Permissions.Skill.EmployeeSkillView, cancellationToken);
        return await LoadProfileAsync(employeeId, cancellationToken);
    }

    public Task<SkillProfile> MyProfileAsync(CancellationToken cancellationToken) => LoadProfileAsync(Me(), cancellationToken);

    public async Task<SkillProfile> SaveSkillAsync(Guid employeeId, EmployeeSkillBody body, bool self, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!self)
        {
            await EnsureScopeAsync(employeeId, Permissions.Skill.EmployeeSkillManage, cancellationToken);
        }

        if (!await dbContext.Skills.AnyAsync(skill => skill.Id == body.SkillId && skill.IsActive, cancellationToken))
        {
            throw new DomainRuleException("SKILL_UNKNOWN", "Choose an active skill from the catalog.");
        }

        EmployeeSkill? row = await dbContext.EmployeeSkills.FirstOrDefaultAsync(item => item.EmployeeId == employeeId && item.SkillId == body.SkillId, cancellationToken);
        if (row is null)
        {
            row = EmployeeSkill.Record(employeeId, body.SkillId);
            dbContext.EmployeeSkills.Add(row);
        }

        AssessmentSource source = self ? AssessmentSource.Self : body.Source ?? AssessmentSource.Manager;
        row.Assess(body.CurrentLevel, body.TargetLevel, body.YearsExperience, body.IsPrimary, source, currentUser.EmployeeId, Today);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadProfileAsync(employeeId, cancellationToken);
    }

    public async Task<SkillProfile> RemoveSkillAsync(Guid employeeId, Guid skillId, bool self, CancellationToken cancellationToken)
    {
        if (!self)
        {
            await EnsureScopeAsync(employeeId, Permissions.Skill.EmployeeSkillManage, cancellationToken);
        }

        EmployeeSkill row = await dbContext.EmployeeSkills.FirstOrDefaultAsync(item => item.EmployeeId == employeeId && item.SkillId == skillId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeSkill), skillId);
        dbContext.EmployeeSkills.Remove(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadProfileAsync(employeeId, cancellationToken);
    }

    public async Task<SkillProfile> SaveCertificationAsync(Guid employeeId, Guid? certificationId, CertificationBody body, bool self, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!self)
        {
            await EnsureScopeAsync(employeeId, Permissions.Skill.EmployeeSkillManage, cancellationToken);
        }

        if (certificationId is { } id)
        {
            EmployeeCertification certification = await dbContext.EmployeeCertifications.FirstOrDefaultAsync(row => row.Id == id && row.EmployeeId == employeeId, cancellationToken)
                ?? throw new NotFoundException(nameof(EmployeeCertification), id);
            certification.Update(body.Name, body.Issuer, body.CredentialId, body.IssuedOn, body.ExpiresOn, body.SkillId);
        }
        else
        {
            dbContext.EmployeeCertifications.Add(EmployeeCertification.Record(employeeId, body.Name, body.Issuer, body.CredentialId, body.IssuedOn, body.ExpiresOn, body.SkillId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadProfileAsync(employeeId, cancellationToken);
    }

    public async Task<SkillProfile> RemoveCertificationAsync(Guid employeeId, Guid certificationId, bool self, CancellationToken cancellationToken)
    {
        if (!self)
        {
            await EnsureScopeAsync(employeeId, Permissions.Skill.EmployeeSkillManage, cancellationToken);
        }

        EmployeeCertification certification = await dbContext.EmployeeCertifications.FirstOrDefaultAsync(row => row.Id == certificationId && row.EmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeCertification), certificationId);
        dbContext.EmployeeCertifications.Remove(certification);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadProfileAsync(employeeId, cancellationToken);
    }

    // ----- Across people ------------------------------------------------------------------------

    /// <summary>The skill matrix: for each skill, how many people sit at each level, and how many want to grow.</summary>
    public async Task<IReadOnlyList<MatrixRow>> MatrixAsync(string? category, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Skill.EmployeeSkillView, cancellationToken);
        IQueryable<EmployeeSkill> rows = dbContext.EmployeeSkills.AsNoTracking();
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        var data = await rows
            .Join(dbContext.Skills.AsNoTracking().Where(skill => category == null || skill.Category == category), row => row.SkillId, skill => skill.Id, (row, skill) => new { row.SkillId, skill.Name, skill.Category, row.CurrentLevel, row.TargetLevel })
            .ToListAsync(cancellationToken);

        return [.. data
            .GroupBy(row => new { row.SkillId, row.Name, row.Category })
            .Select(group => new MatrixRow(
                group.Key.SkillId,
                group.Key.Name,
                group.Key.Category,
                [.. Enumerable.Range(1, 5).Select(level => group.Count(row => row.CurrentLevel == level))],
                group.Count(),
                Math.Round((decimal)group.Average(row => row.CurrentLevel), 1),
                group.Count(row => row.TargetLevel > row.CurrentLevel)))
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.People)];
    }

    public async Task<IReadOnlyList<SkilledPerson>> PeopleWithAsync(Guid skillId, int minLevel, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Skill.EmployeeSkillView, cancellationToken);
        IQueryable<EmployeeSkill> rows = dbContext.EmployeeSkills.AsNoTracking().Where(row => row.SkillId == skillId && row.CurrentLevel >= minLevel);
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        List<EmployeeSkill> found = await rows.OrderByDescending(row => row.CurrentLevel).Take(500).ToListAsync(cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(found.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        return [.. found
            .Where(row => people.TryGetValue(row.EmployeeId, out EmployeeSummary? person) && person.Status != "Exited")
            .Select(row => new SkilledPerson(
                row.EmployeeId, people[row.EmployeeId].DisplayName, people[row.EmployeeId].EmployeeCode, people[row.EmployeeId].Designation,
                row.CurrentLevel, row.YearsExperience, row.IsPrimary))];
    }

    // ----- Internals ----------------------------------------------------------------------------

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("SKILL_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task EnsureScopeAsync(Guid employeeId, string permission, CancellationToken cancellationToken)
    {
        if (!await employees.IsInScopeAsync(employeeId, permission, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }
    }

    private async Task<SkillProfile> LoadProfileAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var skills = await dbContext.EmployeeSkills.AsNoTracking()
            .Where(row => row.EmployeeId == employeeId)
            .Join(dbContext.Skills.AsNoTracking(), row => row.SkillId, skill => skill.Id, (row, skill) => new { row, skill.Name, skill.Category })
            .ToListAsync(cancellationToken);
        List<EmployeeCertification> certifications = await dbContext.EmployeeCertifications.AsNoTracking()
            .Where(row => row.EmployeeId == employeeId)
            .OrderByDescending(row => row.IssuedOn)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(
                skills.Select(item => item.row.AssessedByEmployeeId).OfType<Guid>().Append(employeeId).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);

        return new SkillProfile(
            employeeId,
            names.GetValueOrDefault(employeeId),
            [.. skills
                .OrderByDescending(item => item.row.IsPrimary)
                .ThenByDescending(item => item.row.CurrentLevel)
                .ThenBy(item => item.Name)
                .Select(item => new EmployeeSkillItem(
                    item.row.Id, item.row.SkillId, item.Name, item.Category, item.row.CurrentLevel, item.row.TargetLevel, item.row.Gap,
                    item.row.YearsExperience, item.row.IsPrimary, item.row.LastAssessedOn, item.row.Source,
                    item.row.AssessedByEmployeeId is { } by ? names.GetValueOrDefault(by) : null))],
            [.. certifications.Select(row => new CertificationItem(row.Id, row.Name, row.Issuer, row.CredentialId, row.IssuedOn, row.ExpiresOn, row.SkillId, row.IsCurrent(Today)))]);
    }
}

/// <summary>Skill lookups for resourcing.</summary>
internal sealed class SkillDirectory(AppDbContext dbContext, TimeProvider timeProvider) : ISkillDirectory
{
    public async Task<IReadOnlyDictionary<Guid, SkillInfo>> GetSkillsAsync(IEnumerable<Guid> skillIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. skillIds.Distinct()];
        return await dbContext.Skills.AsNoTracking()
            .Where(skill => ids.Contains(skill.Id))
            .ToDictionaryAsync(skill => skill.Id, skill => new SkillInfo(skill.Id, skill.Code, skill.Name, skill.Category), cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeSkillLevel>> LevelsAsync(IEnumerable<Guid>? employeeIds, CancellationToken cancellationToken = default)
    {
        IQueryable<EmployeeSkill> rows = dbContext.EmployeeSkills.AsNoTracking();
        if (employeeIds is not null)
        {
            List<Guid> ids = [.. employeeIds.Distinct()];
            rows = rows.Where(row => ids.Contains(row.EmployeeId));
        }

        return await rows
            .Join(dbContext.Skills.AsNoTracking(), row => row.SkillId, skill => skill.Id, (row, skill) => new EmployeeSkillLevel(
                row.EmployeeId, row.SkillId, skill.Name, skill.Category, row.CurrentLevel, row.YearsExperience, row.IsPrimary))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> CertificationsAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. employeeIds.Distinct()];
        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var rows = await dbContext.EmployeeCertifications.AsNoTracking()
            .Where(row => ids.Contains(row.EmployeeId) && (row.ExpiresOn == null || row.ExpiresOn >= today))
            .Select(row => new { row.EmployeeId, row.Name })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.EmployeeId).ToDictionary(group => group.Key, group => (IReadOnlyList<string>)[.. group.Select(row => row.Name)]);
    }
}

/// <summary>The skills a new tenant's catalog starts with; editable after.</summary>
internal static class SkillCatalog
{
    public static IReadOnlyList<(string Code, string Name, string Category)> Defaults { get; } =
    [
        ("DOTNET", ".NET / C#", "Technical"), ("JAVA", "Java", "Technical"), ("PYTHON", "Python", "Technical"),
        ("TYPESCRIPT", "TypeScript", "Technical"), ("ANGULAR", "Angular", "Technical"), ("REACT", "React", "Technical"),
        ("SQLSERVER", "SQL Server", "Technical"), ("POSTGRES", "PostgreSQL", "Technical"), ("AZURE", "Microsoft Azure", "Cloud"),
        ("AWS", "Amazon Web Services", "Cloud"), ("KUBERNETES", "Kubernetes", "Cloud"), ("DOCKER", "Docker", "Cloud"),
        ("CICD", "CI/CD pipelines", "DevOps"), ("TEST_AUTOMATION", "Test automation", "Quality"), ("PLAYWRIGHT", "Playwright", "Quality"),
        ("MANUAL_TESTING", "Manual testing", "Quality"), ("ARCHITECTURE", "Solution architecture", "Technical"),
        ("DATA_ENGINEERING", "Data engineering", "Data"), ("ML", "Machine learning", "Data"), ("LLM", "LLM applications", "Data"),
        ("UX", "UI/UX design", "Design"), ("BA", "Business analysis", "Delivery"), ("SCRUM", "Scrum", "Delivery"),
        ("PM", "Project management", "Delivery"), ("RETAIL", "Retail domain", "Domain"), ("BFSI", "Banking and financial services", "Domain"),
        ("HEALTHCARE", "Healthcare domain", "Domain"), ("COMMUNICATION", "Communication", "Soft"), ("STAKEHOLDER", "Stakeholder management", "Soft"),
    ];
}

/// <summary>Gives the current tenant every catalog skill it lacks.</summary>
public sealed class SkillCatalogSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> codes = [.. await dbContext.Skills
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Select(skill => skill.Code)
            .ToListAsync(cancellationToken)];

        int added = 0;
        foreach ((string code, string name, string category) in SkillCatalog.Defaults.Where(seed => !codes.Contains(seed.Code)))
        {
            dbContext.Skills.Add(Skill.Create(code, name, category, null));
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}

internal sealed class SeedSkillCatalogOnTenantProvisioned(SkillCatalogSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}
