using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>A job title, independent of who currently holds it or which department they sit in.</summary>
public sealed class Designation : TenantAggregateRoot
{
    private Designation()
    {
        Code = null!;
        Title = null!;
    }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string? JobFamily { get; private set; }

    public int? JobLevel { get; private set; }

    public static Designation Create(string code, string title, string? jobFamily = null, int? jobLevel = null)
    {
        Guard.Against(jobLevel is < 0, "JOB_LEVEL_INVALID", "Job level cannot be negative.");

        return new Designation
        {
            Code = OrganizationCode.Normalize(code),
            Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 150),
            JobFamily = string.IsNullOrWhiteSpace(jobFamily) ? null : Guard.MaxLength(jobFamily.Trim(), 100),
            JobLevel = jobLevel,
        };
    }

    public void Update(string title, string? jobFamily, int? jobLevel)
    {
        Guard.Against(jobLevel is < 0, "JOB_LEVEL_INVALID", "Job level cannot be negative.");

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 150);
        JobFamily = string.IsNullOrWhiteSpace(jobFamily) ? null : Guard.MaxLength(jobFamily.Trim(), 100);
        JobLevel = jobLevel;
    }
}
