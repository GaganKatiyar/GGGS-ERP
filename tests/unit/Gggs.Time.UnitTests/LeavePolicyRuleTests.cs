using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Time.Domain.Leave;
using Shouldly;

namespace Gggs.Time.UnitTests;

public sealed class LeavePolicyRuleTests
{
    [Fact]
    public void Create_WithNegativeAccrualAmount_Throws()
    {
        Should.Throw<DomainRuleException>(() => LeavePolicyRule.Create(
            Guid.NewGuid(), Guid.NewGuid(), AccrualFrequency.Monthly, accrualAmount: -1m,
            proRataOnJoining: false, maxBalance: null, carryForwardMax: null, carryForwardExpiryMonths: null,
            encashmentAllowed: false, eligibleAfterDays: 0, minNoticeDays: 0, maxConsecutiveDays: null,
            minDaysPerRequest: 0.5m, allowHalfDay: true, allowNegativeBalance: false, negativeLimit: 0m));
    }

    [Fact]
    public void Create_WithZeroMinDaysPerRequest_Throws()
    {
        Should.Throw<DomainRuleException>(() => LeavePolicyRule.Create(
            Guid.NewGuid(), Guid.NewGuid(), AccrualFrequency.Monthly, accrualAmount: 1m,
            proRataOnJoining: false, maxBalance: null, carryForwardMax: null, carryForwardExpiryMonths: null,
            encashmentAllowed: false, eligibleAfterDays: 0, minNoticeDays: 0, maxConsecutiveDays: null,
            minDaysPerRequest: 0m, allowHalfDay: true, allowNegativeBalance: false, negativeLimit: 0m));
    }

    [Fact]
    public void Create_WithCarryForwardMaxAboveTheBalanceCap_Throws()
    {
        Should.Throw<DomainRuleException>(() => LeavePolicyRule.Create(
            Guid.NewGuid(), Guid.NewGuid(), AccrualFrequency.Monthly, accrualAmount: 1m,
            proRataOnJoining: false, maxBalance: 10m, carryForwardMax: 15m, carryForwardExpiryMonths: null,
            encashmentAllowed: false, eligibleAfterDays: 0, minNoticeDays: 0, maxConsecutiveDays: null,
            minDaysPerRequest: 0.5m, allowHalfDay: true, allowNegativeBalance: false, negativeLimit: 0m));
    }

    [Fact]
    public void Create_WithValidValues_Succeeds()
    {
        LeavePolicyRule rule = LeavePolicyRule.Create(
            Guid.NewGuid(), Guid.NewGuid(), AccrualFrequency.Monthly, accrualAmount: 1.5m,
            proRataOnJoining: true, maxBalance: 30m, carryForwardMax: 10m, carryForwardExpiryMonths: 3,
            encashmentAllowed: true, eligibleAfterDays: 90, minNoticeDays: 2, maxConsecutiveDays: 15,
            minDaysPerRequest: 0.5m, allowHalfDay: true, allowNegativeBalance: false, negativeLimit: 0m);

        rule.AccrualAmount.ShouldBe(1.5m);
        rule.EligibleAfterDays.ShouldBe(90);
    }
}
