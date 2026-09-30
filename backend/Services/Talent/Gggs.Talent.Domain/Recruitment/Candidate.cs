using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Recruitment;

/// <summary>
/// A person who has applied, or is being considered, for a role at this tenant - not an employee,
/// and never becomes one automatically (hiring is still a deliberate Core HR
/// <c>EmployeeService.HireAsync</c> call once an offer is accepted, a later slice).
/// <para>
/// v1 keeps email/phone as plain columns rather than the per-tenant envelope-encrypted +
/// blind-indexed fields architecture.md §17 specifies for candidate PII: that pattern has no
/// existing implementation anywhere in this codebase yet to build on, and duplicating candidates
/// across postings by email is not needed until a public apply flow exists to create the
/// duplicates. Revisit encryption before this reaches a public careers page.
/// </para>
/// </summary>
public sealed class Candidate : TenantAggregateRoot
{
    private Candidate()
    {
        FirstName = null!;
        Email = null!;
    }

    public string FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string Email { get; private set; }

    public string? Phone { get; private set; }

    public string? CurrentEmployer { get; private set; }

    public int? TotalExperienceMonths { get; private set; }

    public string? Source { get; private set; }

    public string DisplayName => LastName is null ? FirstName : $"{FirstName} {LastName}";

    public static Candidate Create(
        string firstName, string? lastName, string email, string? phone,
        string? currentEmployer, int? totalExperienceMonths, string? source)
    {
        Guard.Against(
            totalExperienceMonths is < 0, "CANDIDATE_EXPERIENCE_MUST_NOT_BE_NEGATIVE", "Total experience cannot be negative.");

        return new Candidate
        {
            FirstName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(firstName).Trim(), 100),
            LastName = string.IsNullOrWhiteSpace(lastName) ? null : Guard.MaxLength(lastName.Trim(), 100),
            Email = NormalizeEmail(email),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : Guard.MaxLength(phone.Trim(), 20),
            CurrentEmployer = string.IsNullOrWhiteSpace(currentEmployer) ? null : Guard.MaxLength(currentEmployer.Trim(), 200),
            TotalExperienceMonths = totalExperienceMonths,
            Source = string.IsNullOrWhiteSpace(source) ? null : Guard.MaxLength(source.Trim(), 50),
            IsActive = true,
        };
    }

    /// <summary>Public so the application layer can compute the same normalized form for the
    /// "does this candidate already exist" lookup, the same way <c>EmployeeCodeFormat.Normalize</c>
    /// and <c>OrganizationCode.Normalize</c> are.</summary>
    public static string NormalizeEmail(string email)
        => Guard.MaxLength(Guard.NotNullOrWhiteSpace(email).Trim().ToLowerInvariant(), 256);
}
