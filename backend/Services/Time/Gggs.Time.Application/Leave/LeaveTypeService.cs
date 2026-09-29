using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Time.Domain.Leave;

namespace Gggs.Time.Application.Leave;

public sealed class LeaveTypeService(ILeaveTypeRepository leaveTypes, IUnitOfWork unitOfWork)
{
    public async Task<LeaveType> CreateAsync(
        string code, string name, bool isPaid, LeaveUnit unit, string? genderRestrictionCode,
        bool isCompOff, bool isLop, string colorHex, CancellationToken cancellationToken = default)
    {
        LeaveType? existing = await leaveTypes.GetByCodeAsync(code, cancellationToken);

        if (existing is not null)
        {
            throw new DomainRuleException("LEAVE_TYPE_CODE_IN_USE", $"A leave type with code '{code}' already exists.");
        }

        LeaveType leaveType = LeaveType.Create(code, name, isPaid, unit, genderRestrictionCode, isCompOff, isLop, colorHex);
        leaveTypes.Add(leaveType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return leaveType;
    }
}

public sealed class LeavePolicyService(ILeavePolicyRepository policies, IUnitOfWork unitOfWork)
{
    /// <summary>
    /// v1 has no <c>PolicyAssignment</c> targeting (see <see cref="LeavePolicyResolver"/>'s own
    /// remarks): a tenant has at most one policy, and <see cref="ILeavePolicyRepository.GetTenantDefaultPolicyIdAsync"/>
    /// silently picks whichever one exists. A second policy would make its own rules invisible -
    /// <see cref="LeavePolicyResolver"/> would never find them - so this rejects the second policy
    /// outright rather than let that trap exist.
    /// </summary>
    public async Task<LeavePolicy> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (await policies.GetTenantDefaultPolicyIdAsync(cancellationToken) is not null)
        {
            throw new DomainRuleException(
                "LEAVE_POLICY_ALREADY_EXISTS",
                "This tenant already has a leave policy. Only one is supported until per-employee policy targeting is built.");
        }

        LeavePolicy policy = LeavePolicy.Create(name);
        policies.Add(policy);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return policy;
    }

    public async Task<LeavePolicyRule> CreateRuleAsync(
        Guid policyId,
        Guid leaveTypeId,
        AccrualFrequency accrualFrequency,
        decimal accrualAmount,
        bool proRataOnJoining,
        decimal? maxBalance,
        decimal? carryForwardMax,
        int? carryForwardExpiryMonths,
        bool encashmentAllowed,
        int eligibleAfterDays,
        int minNoticeDays,
        int? maxConsecutiveDays,
        decimal minDaysPerRequest,
        bool allowHalfDay,
        bool allowNegativeBalance,
        decimal negativeLimit,
        CancellationToken cancellationToken = default)
    {
        LeavePolicy policy = await policies.GetAsync(policyId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeavePolicy), policyId);

        LeavePolicyRule? existing = await policies.GetRuleAsync(policyId, leaveTypeId, cancellationToken);

        if (existing is not null)
        {
            throw new DomainRuleException(
                "LEAVE_POLICY_RULE_ALREADY_EXISTS", $"'{policy.Name}' already has a rule for this leave type.");
        }

        LeavePolicyRule rule = LeavePolicyRule.Create(
            policyId, leaveTypeId, accrualFrequency, accrualAmount, proRataOnJoining, maxBalance, carryForwardMax,
            carryForwardExpiryMonths, encashmentAllowed, eligibleAfterDays, minNoticeDays, maxConsecutiveDays,
            minDaysPerRequest, allowHalfDay, allowNegativeBalance, negativeLimit);

        policies.Add(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rule;
    }
}
