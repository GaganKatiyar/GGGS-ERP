namespace Gggs.Modules.Skills.Contracts;

/// <summary>One person's level in one skill, 1 (aware) to 5 (expert).</summary>
public sealed record EmployeeSkillLevel(Guid EmployeeId, Guid SkillId, string SkillName, string Category, int Level, decimal? YearsExperience, bool IsPrimary);

public sealed record SkillInfo(Guid Id, string Code, string Name, string Category);

/// <summary>Skill lookups for resourcing and matching (ADR-12).</summary>
public interface ISkillDirectory
{
    Task<IReadOnlyDictionary<Guid, SkillInfo>> GetSkillsAsync(IEnumerable<Guid> skillIds, CancellationToken cancellationToken = default);

    /// <summary>Every skill of each employee given; null means every employee in the tenant.</summary>
    Task<IReadOnlyList<EmployeeSkillLevel>> LevelsAsync(IEnumerable<Guid>? employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Current certifications (not expired) per employee, by name.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> CertificationsAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default);
}
