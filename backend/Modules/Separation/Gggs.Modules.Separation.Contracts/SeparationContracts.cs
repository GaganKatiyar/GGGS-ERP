namespace Gggs.Modules.Separation.Contracts;

/// <summary>A former employee in the alumni network, as Recruitment sees a returning candidate ("boomerang").</summary>
public sealed record AlumniMatch(
    Guid AlumniId,
    Guid EmployeeId,
    string Name,
    string? Email,
    string? Mobile,
    string? LastDesignation,
    DateOnly ExitDate,
    string SeparationType,
    bool EligibleForRehire);

/// <summary>The alumni network for other modules (Recruitment flags former employees who apply again).</summary>
public interface IAlumniDirectory
{
    /// <summary>
    /// Alumni whose email (case-insensitive) or mobile (digits only) is among those given.
    /// Mobiles are compared on their digits.
    /// </summary>
    Task<IReadOnlyList<AlumniMatch>> MatchAsync(IReadOnlyCollection<string> emails, IReadOnlyCollection<string> mobiles, CancellationToken cancellationToken = default);
}
