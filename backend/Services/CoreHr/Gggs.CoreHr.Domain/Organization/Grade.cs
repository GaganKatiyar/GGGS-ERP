using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>A pay grade: a rank in the ladder and, optionally, the salary band that goes with it.</summary>
public sealed class Grade : TenantAggregateRoot
{
    private Grade()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Where this grade sits in the ladder. Lower is more senior, matching how bands are usually listed.</summary>
    public int Rank { get; private set; }

    public decimal? SalaryBandMinimum { get; private set; }

    public decimal? SalaryBandMaximum { get; private set; }

    public string? Currency { get; private set; }

    public static Grade Create(string code, string name, int rank)
    {
        Guard.Against(rank <= 0, "GRADE_RANK_INVALID", "Grade rank must be a positive number.");

        return new Grade
        {
            Code = OrganizationCode.Normalize(code),
            Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
            Rank = rank,
        };
    }

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);

    public void SetSalaryBand(decimal? minimum, decimal? maximum, string? currency)
    {
        Guard.Against(
            minimum is not null && maximum is not null && minimum > maximum,
            "SALARY_BAND_INVALID",
            "The minimum of a salary band cannot exceed its maximum.");

        Guard.Against(
            (minimum is not null || maximum is not null) && string.IsNullOrWhiteSpace(currency),
            "SALARY_BAND_CURRENCY_REQUIRED",
            "A salary band needs a currency.");

        SalaryBandMinimum = minimum;
        SalaryBandMaximum = maximum;
        Currency = string.IsNullOrWhiteSpace(currency) ? null : Guard.MaxLength(currency.Trim().ToUpperInvariant(), 3);
    }
}
