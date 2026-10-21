using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Separation.Domain;
using Gggs.Modules.Separation.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Separation.Application;

public sealed record PolicyView(
    int DefaultNoticeDays,
    int ProbationNoticeDays,
    bool BuyoutAllowed,
    bool GardenLeaveAllowed,
    IReadOnlyList<string> BlockedLeaveTypeCodes,
    IReadOnlyList<string> EncashableLeaveTypeCodes,
    int? EncashmentCapDays,
    int RetirementAge,
    int AbscondingDays,
    int SettlementDueWorkingDays,
    int GratuityDueDays,
    int ShowCauseReplyDays,
    int ExitProposalLeadDays);

public sealed record PolicyBody(
    int DefaultNoticeDays,
    int ProbationNoticeDays,
    bool BuyoutAllowed,
    bool GardenLeaveAllowed,
    IReadOnlyList<string>? BlockedLeaveTypeCodes,
    IReadOnlyList<string>? EncashableLeaveTypeCodes,
    int? EncashmentCapDays,
    int RetirementAge,
    int AbscondingDays,
    int SettlementDueWorkingDays,
    int GratuityDueDays,
    int ShowCauseReplyDays = 7,
    int ExitProposalLeadDays = 60);

public sealed record NoticeRuleView(Guid Id, Guid? GradeId, string? EmploymentTypeCode, bool? OnProbation, int NoticeDays);

public sealed record NoticeRuleBody(Guid? GradeId, string? EmploymentTypeCode, bool? OnProbation, int NoticeDays);

public sealed record ReasonView(string Code, string Label, bool Voluntary);

/// <summary>
/// The tenant's separation policy and notice rules, and the notice period that applies to a given
/// employee: the most specific matching rule, else the probation or default period.
/// </summary>
internal sealed class SeparationPolicyService(AppDbContext dbContext, IEmployeeDirectory employees, ITenantClock clock)
{
    public static IReadOnlyList<ReasonView> Reasons { get; } = [.. SeparationReasons.All.Select(reason => new ReasonView(reason.Code, reason.Label, reason.Voluntary))];

    public async Task<PolicyView> GetAsync(CancellationToken cancellationToken) => View(await PolicyAsync(cancellationToken));

    public async Task<PolicyView> UpdateAsync(PolicyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        SeparationPolicy policy = await PolicyAsync(cancellationToken);
        policy.Update(
            body.DefaultNoticeDays, body.ProbationNoticeDays, body.BuyoutAllowed, body.GardenLeaveAllowed,
            body.BlockedLeaveTypeCodes ?? [], body.EncashableLeaveTypeCodes ?? [], body.EncashmentCapDays,
            body.RetirementAge, body.AbscondingDays, body.SettlementDueWorkingDays, body.GratuityDueDays,
            body.ShowCauseReplyDays, body.ExitProposalLeadDays);
        await dbContext.SaveChangesAsync(cancellationToken);
        return View(policy);
    }

    public async Task<IReadOnlyList<NoticeRuleView>> RulesAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.NoticeRules.AsNoTracking().ToListAsync(cancellationToken)).OrderByDescending(rule => rule.Specificity).Select(Rule)];

    public async Task<NoticeRuleView> AddRuleAsync(NoticeRuleBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var rule = NoticeRule.Create(body.GradeId, body.EmploymentTypeCode, body.OnProbation, body.NoticeDays);
        dbContext.NoticeRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Rule(rule);
    }

    public async Task RemoveRuleAsync(Guid id, CancellationToken cancellationToken)
    {
        NoticeRule rule = await dbContext.NoticeRules.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(NoticeRule), id);
        dbContext.NoticeRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The notice period that applies to an employee today.</summary>
    public async Task<int> NoticeDaysForAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        SeparationPolicy policy = await PolicyAsync(cancellationToken);
        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        bool onProbation = person?.ConfirmationDate is null && person?.ProbationEndDate is { } end && end >= today;
        NoticeRule? rule = (await dbContext.NoticeRules.AsNoTracking().ToListAsync(cancellationToken))
            .Where(candidate => candidate.Matches(person?.GradeId, person?.EmploymentTypeCode, onProbation))
            .OrderByDescending(candidate => candidate.Specificity)
            .FirstOrDefault();
        return rule?.NoticeDays ?? (onProbation ? policy.ProbationNoticeDays : policy.DefaultNoticeDays);
    }

    /// <summary>The policy, created with defaults the first time it is needed.</summary>
    public async Task<SeparationPolicy> PolicyAsync(CancellationToken cancellationToken)
    {
        SeparationPolicy? policy = await dbContext.SeparationPolicies.FirstOrDefaultAsync(cancellationToken);
        if (policy is null)
        {
            policy = SeparationPolicy.CreateDefault();
            dbContext.SeparationPolicies.Add(policy);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return policy;
    }

    private static PolicyView View(SeparationPolicy policy) => new(
        policy.DefaultNoticeDays, policy.ProbationNoticeDays, policy.BuyoutAllowed, policy.GardenLeaveAllowed, policy.BlockedLeaveTypes, policy.EncashableLeaveTypes,
        policy.EncashmentCapDays, policy.RetirementAge, policy.AbscondingDays, policy.SettlementDueWorkingDays, policy.GratuityDueDays,
        policy.ShowCauseReplyDays, policy.ExitProposalLeadDays);

    private static NoticeRuleView Rule(NoticeRule rule) => new(rule.Id, rule.GradeId, rule.EmploymentTypeCode, rule.OnProbation, rule.NoticeDays);
}

/// <summary>Leave types the policy blocks cannot be applied for while the employee is serving notice.</summary>
internal sealed class SeparationLeaveRule(AppDbContext dbContext) : ILeaveApplicationRule
{
    public async Task<string?> CheckAsync(Guid employeeId, string leaveTypeCode, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        bool onNotice = await dbContext.SeparationCases.AsNoTracking().AnyAsync(row => row.EmployeeId == employeeId && row.Status == CaseStatus.Approved && row.LastWorkingDay >= startDate, cancellationToken);
        if (!onNotice)
        {
            return null;
        }

        SeparationPolicy? policy = await dbContext.SeparationPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        IReadOnlyList<string> blocked = policy?.BlockedLeaveTypes ?? SeparationPolicy.CreateDefault().BlockedLeaveTypes;
        return blocked.Contains(leaveTypeCode, StringComparer.OrdinalIgnoreCase)
            ? $"{leaveTypeCode} leave cannot be taken while serving notice; it is paid out in the settlement instead."
            : null;
    }
}
