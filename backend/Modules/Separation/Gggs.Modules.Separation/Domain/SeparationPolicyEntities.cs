using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Separation.Domain;

/// <summary>
/// The organisation's separation rules: notice periods, what may happen during notice, when a
/// settlement is due and when long absence is treated as absconding. One per tenant, created with
/// sensible Indian defaults the first time it is read.
/// </summary>
public sealed class SeparationPolicy : TenantAggregateRoot
{
    private SeparationPolicy()
    {
        BlockedLeaveTypeCodes = string.Empty;
        EncashableLeaveTypeCodes = string.Empty;
    }

    /// <summary>Notice for a confirmed employee when no rule matches.</summary>
    public int DefaultNoticeDays { get; private set; }

    /// <summary>Notice while still on probation, when no probation rule matches.</summary>
    public int ProbationNoticeDays { get; private set; }

    /// <summary>The employee may pay for notice not served (a buyout) to leave earlier.</summary>
    public bool BuyoutAllowed { get; private set; }

    /// <summary>The employee may be put on garden leave: paid to the last day, not working.</summary>
    public bool GardenLeaveAllowed { get; private set; }

    /// <summary>Comma-separated leave type codes that cannot be applied for while on notice.</summary>
    public string BlockedLeaveTypeCodes { get; private set; }

    /// <summary>Comma-separated leave type codes paid out in the settlement.</summary>
    public string EncashableLeaveTypeCodes { get; private set; }

    /// <summary>The most days of leave encashed at exit; null for no cap.</summary>
    public int? EncashmentCapDays { get; private set; }

    public int RetirementAge { get; private set; }

    /// <summary>Consecutive unexplained absent days after which HR is alerted to possible absconding.</summary>
    public int AbscondingDays { get; private set; }

    /// <summary>Working days after the last day within which the settlement must be paid (Code on Wages s.17(2): two).</summary>
    public int SettlementDueWorkingDays { get; private set; }

    /// <summary>Days after the last day within which gratuity must be paid (Payment of Gratuity Act s.7: thirty).</summary>
    public int GratuityDueDays { get; private set; }

    public static SeparationPolicy CreateDefault() => new()
    {
        DefaultNoticeDays = 60,
        ProbationNoticeDays = 15,
        BuyoutAllowed = true,
        GardenLeaveAllowed = false,
        BlockedLeaveTypeCodes = "EL",
        EncashableLeaveTypeCodes = "EL",
        EncashmentCapDays = null,
        RetirementAge = 60,
        AbscondingDays = 7,
        SettlementDueWorkingDays = 2,
        GratuityDueDays = 30,
    };

    public void Update(
        int defaultNoticeDays,
        int probationNoticeDays,
        bool buyoutAllowed,
        bool gardenLeaveAllowed,
        IEnumerable<string> blockedLeaveTypeCodes,
        IEnumerable<string> encashableLeaveTypeCodes,
        int? encashmentCapDays,
        int retirementAge,
        int abscondingDays,
        int settlementDueWorkingDays,
        int gratuityDueDays)
    {
        DefaultNoticeDays = Days(defaultNoticeDays, "default notice period");
        ProbationNoticeDays = Days(probationNoticeDays, "probation notice period");
        BuyoutAllowed = buyoutAllowed;
        GardenLeaveAllowed = gardenLeaveAllowed;
        BlockedLeaveTypeCodes = Codes(blockedLeaveTypeCodes);
        EncashableLeaveTypeCodes = Codes(encashableLeaveTypeCodes);
        if (encashmentCapDays is < 0 or > 365)
        {
            throw new DomainRuleException("SEPARATION_POLICY", "The encashment cap runs 0 to 365 days.");
        }

        EncashmentCapDays = encashmentCapDays;
        if (retirementAge is < 50 or > 75)
        {
            throw new DomainRuleException("SEPARATION_POLICY", "The retirement age runs 50 to 75.");
        }

        RetirementAge = retirementAge;
        if (abscondingDays is < 2 or > 60)
        {
            throw new DomainRuleException("SEPARATION_POLICY", "The absconding threshold runs 2 to 60 days.");
        }

        AbscondingDays = abscondingDays;
        if (settlementDueWorkingDays is < 1 or > 60)
        {
            throw new DomainRuleException("SEPARATION_POLICY", "The settlement deadline runs 1 to 60 working days.");
        }

        SettlementDueWorkingDays = settlementDueWorkingDays;
        if (gratuityDueDays is < 1 or > 90)
        {
            throw new DomainRuleException("SEPARATION_POLICY", "The gratuity deadline runs 1 to 90 days.");
        }

        GratuityDueDays = gratuityDueDays;
    }

    public IReadOnlyList<string> BlockedLeaveTypes => Split(BlockedLeaveTypeCodes);

    public IReadOnlyList<string> EncashableLeaveTypes => Split(EncashableLeaveTypeCodes);

    private static int Days(int days, string what) => days is < 0 or > 180
        ? throw new DomainRuleException("SEPARATION_POLICY", $"The {what} runs 0 to 180 days.")
        : days;

    private static string Codes(IEnumerable<string> codes)
        => Guard.MaxLength(string.Join(',', (codes ?? []).Select(code => code.Trim().ToUpperInvariant()).Where(code => code.Length > 0).Distinct()), 400);

    private static string[] Split(string codes) => codes.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// A notice period for a slice of the workforce: a grade, an employment type, or people on
/// probation. The most specific rule that matches an employee wins.
/// </summary>
public sealed class NoticeRule : TenantAggregateRoot
{
    private NoticeRule()
    {
    }

    public Guid? GradeId { get; private set; }

    public string? EmploymentTypeCode { get; private set; }

    /// <summary>True: only while on probation; false: only once confirmed; null: either.</summary>
    public bool? OnProbation { get; private set; }

    public int NoticeDays { get; private set; }

    /// <summary>How many conditions the rule sets - a more specific rule beats a general one.</summary>
    public int Specificity => (GradeId is null ? 0 : 2) + (EmploymentTypeCode is null ? 0 : 1) + (OnProbation is null ? 0 : 4);

    public static NoticeRule Create(Guid? gradeId, string? employmentTypeCode, bool? onProbation, int noticeDays)
    {
        if (gradeId is null && string.IsNullOrWhiteSpace(employmentTypeCode) && onProbation is null)
        {
            throw new DomainRuleException("NOTICE_RULE_EMPTY", "A notice rule needs a grade, an employment type or a probation condition; the default covers everyone else.");
        }

        if (noticeDays is < 0 or > 180)
        {
            throw new DomainRuleException("NOTICE_RULE_DAYS", "The notice period runs 0 to 180 days.");
        }

        return new NoticeRule
        {
            GradeId = gradeId,
            EmploymentTypeCode = string.IsNullOrWhiteSpace(employmentTypeCode) ? null : Guard.MaxLength(employmentTypeCode.Trim().ToUpperInvariant(), 40),
            OnProbation = onProbation,
            NoticeDays = noticeDays,
        };
    }

    public bool Matches(Guid? gradeId, string? employmentTypeCode, bool onProbation)
        => (GradeId is null || GradeId == gradeId)
            && (EmploymentTypeCode is null || string.Equals(EmploymentTypeCode, employmentTypeCode, StringComparison.OrdinalIgnoreCase))
            && (OnProbation is null || OnProbation == onProbation);
}

/// <summary>One step in a case's history, shown as its timeline.</summary>
public sealed class CaseEvent : TenantAggregateRoot
{
    private CaseEvent()
    {
        Kind = null!;
        Title = null!;
    }

    public Guid CaseId { get; private set; }

    /// <summary>A stable key ("submitted", "approved", "early-release-requested"...).</summary>
    public string Kind { get; private set; }

    public string Title { get; private set; }

    public string? Note { get; private set; }

    public Guid? ActorEmployeeId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public static CaseEvent Record(Guid caseId, string kind, string title, string? note, Guid? actorEmployeeId, DateTime occurredAt) => new()
    {
        CaseId = Guard.NotEmpty(caseId),
        Kind = Guard.MaxLength(Guard.NotNullOrWhiteSpace(kind), 60),
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title), 200),
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 1000),
        ActorEmployeeId = actorEmployeeId,
        OccurredAt = occurredAt,
    };
}

/// <summary>Why people leave - a fixed list so exits can be compared over time and across teams.</summary>
public static class SeparationReasons
{
    public sealed record Reason(string Code, string Label, bool Voluntary);

    public static IReadOnlyList<Reason> All { get; } =
    [
        new("CAREER_GROWTH", "Career growth elsewhere", true),
        new("COMPENSATION", "Better compensation", true),
        new("ROLE_FIT", "Role or work not a good fit", true),
        new("MANAGER", "Relationship with manager", true),
        new("WORK_LIFE", "Work-life balance", true),
        new("RELOCATION", "Relocation", true),
        new("HIGHER_STUDIES", "Higher studies", true),
        new("HEALTH_FAMILY", "Health or family reasons", true),
        new("CULTURE", "Culture or team", true),
        new("ENTREPRENEURSHIP", "Starting own venture", true),
        new("PERFORMANCE", "Performance", false),
        new("MISCONDUCT", "Misconduct", false),
        new("RESTRUCTURING", "Restructuring or redundancy", false),
        new("CONTRACT_END", "End of contract", false),
        new("RETIREMENT", "Retirement", false),
        new("DEATH", "Death", false),
        new("ABSCONDING", "Absconding", false),
        new("OTHER", "Other", true),
    ];

    public static bool IsKnown(string? code) => code is null || All.Any(reason => reason.Code == code);

    public static string? LabelOf(string? code) => All.FirstOrDefault(reason => reason.Code == code)?.Label;
}
