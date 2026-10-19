namespace Gggs.Modules.Recruitment.Domain;

/// <summary>Why a candidate scored what they did against a requisition.</summary>
public sealed record MatchBreakdown(int Score, int SkillsScore, int ExperienceScore, int SalaryScore, IReadOnlyList<string> MatchedSkills, IReadOnlyList<string> MissingSkills);

/// <summary>
/// A transparent, rule-based match of a candidate to a requisition: skills (60 points), experience
/// against the range (25) and expected CTC against the budget (15). It ranks a pipeline for a recruiter;
/// it never rejects or advances anyone on its own.
/// </summary>
public static class CandidateMatcher
{
    public const int SkillsWeight = 60;
    public const int ExperienceWeight = 25;
    public const int SalaryWeight = 15;

    public static MatchBreakdown Match(
        IReadOnlyList<string> requiredSkills,
        decimal? minExperience,
        decimal? maxExperience,
        decimal? salaryMax,
        IReadOnlyList<string> candidateSkills,
        decimal? experience,
        decimal? expectedCtc)
    {
        ArgumentNullException.ThrowIfNull(requiredSkills);
        ArgumentNullException.ThrowIfNull(candidateSkills);

        var have = new HashSet<string>(candidateSkills.Select(skill => skill.Trim()), StringComparer.OrdinalIgnoreCase);
        List<string> matched = [.. requiredSkills.Where(have.Contains)];
        List<string> missing = [.. requiredSkills.Where(skill => !have.Contains(skill))];
        int skills = requiredSkills.Count == 0 ? SkillsWeight : (int)Math.Round(SkillsWeight * (decimal)matched.Count / requiredSkills.Count, MidpointRounding.AwayFromZero);

        int experienceScore = ExperienceScore(minExperience, maxExperience, experience);
        int salary = SalaryScore(salaryMax, expectedCtc);
        return new MatchBreakdown(skills + experienceScore + salary, skills, experienceScore, salary, matched, missing);
    }

    /// <summary>Full marks inside the range; a year short or over costs a fifth of the points, down to zero.</summary>
    private static int ExperienceScore(decimal? min, decimal? max, decimal? experience)
    {
        if (min is null && max is null)
        {
            return ExperienceWeight;
        }

        if (experience is not { } years)
        {
            return 0;
        }

        decimal off = years < (min ?? 0) ? (min ?? 0) - years : max is { } upper && years > upper ? years - upper : 0;
        return (int)Math.Max(0, Math.Round(ExperienceWeight * (1 - (off / 5)), MidpointRounding.AwayFromZero));
    }

    /// <summary>Full marks within budget; every 10% over the budget costs a third of the points.</summary>
    private static int SalaryScore(decimal? budget, decimal? expected)
    {
        if (budget is not { } max || max <= 0 || expected is not { } ask)
        {
            return SalaryWeight;
        }

        if (ask <= max)
        {
            return SalaryWeight;
        }

        decimal over = (ask - max) / max;
        return (int)Math.Max(0, Math.Round(SalaryWeight * (1 - (over / 0.3m)), MidpointRounding.AwayFromZero));
    }
}
