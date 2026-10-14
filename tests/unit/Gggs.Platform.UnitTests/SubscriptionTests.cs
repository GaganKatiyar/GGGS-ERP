using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>
/// Entitlement rules. These decide which modules a customer sees, so being wrong either gives away
/// paid features or blocks a customer from something they are paying for.
/// </summary>
public sealed class SubscriptionTests
{
    private static readonly DateOnly Today = new(2026, 9, 23);
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void APastDueSubscriptionStillGrantsAccess()
    {
        // Cutting off payroll over a late invoice punishes employees who are owed wages, not the
        // finance team that missed it. Collections escalate to suspension deliberately instead.
        Subscription subscription = PaidSubscription();
        subscription.MarkPastDue();

        subscription.Status.ShouldBe(SubscriptionStatus.PastDue);
        subscription.GrantsAccess.ShouldBeTrue();
    }

    [Fact]
    public void ACancelledSubscriptionGrantsNothing()
    {
        Subscription subscription = PaidSubscription();
        subscription.Cancel(Today);

        subscription.GrantsAccess.ShouldBeFalse();
        subscription.EndDate.ShouldBe(Today);
    }

    [Fact]
    public void ResolvedFeatures_ComeFromThePlan()
    {
        Plan plan = ProfessionalPlan();
        Subscription subscription = PaidSubscription();

        IReadOnlySet<string> features = subscription.ResolveFeatures(plan, Today);

        features.ShouldContain("Payroll");
        features.ShouldNotContain("Recruitment");
    }

    [Fact]
    public void AnOverrideCanEnableAFeatureThePlanExcludes()
    {
        Plan plan = ProfessionalPlan();
        Subscription subscription = PaidSubscription();

        subscription.SetFeatureOverride("Recruitment", isEnabled: true, expiresOn: null, reason: "Pilot agreed with account team");

        subscription.ResolveFeatures(plan, Today).ShouldContain("Recruitment");
    }

    [Fact]
    public void AnOverrideCanWithdrawAFeatureThePlanIncludes()
    {
        // Used after an incident, to take a module out of a customer's hands without downgrading
        // their whole plan.
        Plan plan = ProfessionalPlan();
        Subscription subscription = PaidSubscription();

        subscription.SetFeatureOverride("Payroll", isEnabled: false, expiresOn: null, reason: "Withdrawn pending data fix");

        subscription.ResolveFeatures(plan, Today).ShouldNotContain("Payroll");
    }

    [Fact]
    public void AnExpiredOverrideStopsApplying()
    {
        // Otherwise a three-month pilot quietly becomes a permanent entitlement nobody is billing.
        Plan plan = ProfessionalPlan();
        Subscription subscription = PaidSubscription();

        subscription.SetFeatureOverride(
            "Recruitment",
            isEnabled: true,
            expiresOn: Today.AddDays(-1),
            reason: "Expired pilot");

        subscription.ResolveFeatures(plan, Today).ShouldNotContain("Recruitment");
    }

    [Fact]
    public void SettingAnOverrideTwice_ReplacesTheFirst()
    {
        Subscription subscription = PaidSubscription();

        subscription.SetFeatureOverride("Recruitment", true, null, "Pilot");
        subscription.SetFeatureOverride("Recruitment", false, null, "Pilot ended");

        subscription.FeatureOverrides.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void EveryOverrideRequiresAReason()
    {
        // An entitlement nobody can explain later is an audit finding.
        Should.Throw<ArgumentException>(() =>
            PaidSubscription().SetFeatureOverride("Payroll", true, null, "   "));
    }

    [Fact]
    public void ATrialConvertsToPaidOnlyOnce()
    {
        Subscription subscription = Subscription.Start(
            TenantId, Guid.CreateVersion7(), Today, trialEndsOn: Today.AddDays(30), seatLimit: 25, billingReference: null);

        subscription.Status.ShouldBe(SubscriptionStatus.Trial);

        subscription.ConvertTrialToPaid("INV-1001");
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.TrialEndsOn.ShouldBeNull();

        Should.Throw<DomainRuleException>(() => subscription.ConvertTrialToPaid("INV-1002"))
            .ErrorCode.ShouldBe("SUBSCRIPTION_NOT_TRIAL");
    }

    [Fact]
    public void AClosedSubscriptionCannotChangePlan()
    {
        Subscription subscription = PaidSubscription();
        subscription.Cancel(Today);

        Should.Throw<DomainRuleException>(() => subscription.ChangePlan(Guid.CreateVersion7(), 50))
            .ErrorCode.ShouldBe("SUBSCRIPTION_CLOSED");
    }

    [Fact]
    public void RetiringAPlan_DoesNotAffectExistingSubscriptions()
    {
        // Withdrawing a plan from sale must never cut off the customers already on it.
        Plan plan = ProfessionalPlan();
        Subscription subscription = PaidSubscription();

        plan.Retire();

        plan.IsActive.ShouldBeFalse();
        subscription.GrantsAccess.ShouldBeTrue();
        subscription.ResolveFeatures(plan, Today).ShouldContain("Payroll");
    }

    private static Plan ProfessionalPlan() => Plan.Create(
        "PRO", "Professional", PlanEdition.Professional, maxEmployees: 1000, isPubliclyAvailable: true,
        featureCodes: ["CoreHr", "Attendance", "Leave", "Payroll", "Expenses"]);

    private static Subscription PaidSubscription() => Subscription.Start(
        TenantId, Guid.CreateVersion7(), Today, trialEndsOn: null, seatLimit: 100, billingReference: "INV-1001");
}
