using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Benefits.Contracts;
using Gggs.Modules.Benefits.Domain;
using Gggs.Modules.Benefits.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Benefits.Application;

public sealed record PlanItem(
    Guid Id,
    string Code,
    string Name,
    BenefitType Type,
    string? Provider,
    string? Description,
    decimal? CoverageAmount,
    string Currency,
    decimal EmployeeContribution,
    decimal EmployerContribution,
    decimal DependentContribution,
    bool AllowsDependents,
    int MaxDependents,
    IReadOnlyList<string> EligibleEmploymentTypes,
    int MinServiceMonths,
    bool AutoEnroll,
    bool Mandatory,
    bool RequiresApproval,
    bool EnrollmentOpen,
    bool EmployeeShareTaxDeductible,
    bool IsActive,
    int Enrolled,
    decimal MonthlyEmployeeCost,
    decimal MonthlyEmployerCost);

public sealed record PlanBody(string? Code, PlanDetails Details);

public sealed record DependentItem(Guid Id, string FullName, string RelationshipCode, DateOnly? DateOfBirth);

public sealed record EnrollmentItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    Guid PlanId,
    string PlanCode,
    string PlanName,
    BenefitType PlanType,
    EnrollmentStatus Status,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal EmployeeMonthly,
    decimal EmployerMonthly,
    IReadOnlyList<DependentItem> Dependents,
    string? Notes,
    bool Mandatory);

/// <summary>A plan as an employee sees it: whether they can join, and their enrollment if they have one.</summary>
public sealed record OfferedPlan(PlanItem Plan, string? Ineligibility, EnrollmentItem? Enrollment);

public sealed record MyBenefits(IReadOnlyList<OfferedPlan> Plans, IReadOnlyList<DependentItem> Dependents, decimal MonthlyEmployeeCost, decimal MonthlyEmployerCost);

public sealed record EnrollBody(Guid PlanId, IReadOnlyList<Guid>? DependentIds, DateOnly? EffectiveFrom, string? Notes);

public sealed record EnrollOnBehalfBody(Guid EmployeeId, Guid PlanId, IReadOnlyList<Guid>? DependentIds, DateOnly? EffectiveFrom, string? Notes);

public sealed record DependentsBody(IReadOnlyList<Guid> DependentIds);

public sealed record EndBody(DateOnly? LastDay);

/// <summary>
/// Benefits (spec §24): plans with employee and employer shares, self-enrollment (approved when the
/// plan asks for it) with dependents, automatic enrollment of joiners, and the monthly charges payroll
/// deducts.
/// </summary>
internal sealed class BenefitService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IEmployeePayrollDetails payrollDetails,
    IWorkflowStarter workflow,
    INotifier notifier,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider) : IBenefitCharges
{
    public const string SubjectType = "benefit.enrollment";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Approve a benefit enrollment",
        "benefits",
        [
            new WorkflowSubjectField("plan", "Plan", WorkflowFieldType.Text),
            new WorkflowSubjectField("monthlyCost", "Employer cost a month", WorkflowFieldType.Number),
            new WorkflowSubjectField("dependents", "Dependents", WorkflowFieldType.Number),
        ]);

    // ----- Plans --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PlanItem>> PlansAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        List<BenefitPlan> plans = await dbContext.BenefitPlans.AsNoTracking().Where(plan => includeInactive || plan.IsActive).OrderBy(plan => plan.Type).ThenBy(plan => plan.Name).ToListAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var stats = (await dbContext.BenefitEnrollments.AsNoTracking()
                .Where(enrollment => enrollment.Status == EnrollmentStatus.Active && enrollment.EffectiveFrom <= today && (enrollment.EffectiveTo == null || enrollment.EffectiveTo >= today))
                .ToListAsync(cancellationToken))
            .GroupBy(enrollment => enrollment.PlanId)
            .ToDictionary(group => group.Key, group => (Count: group.Count(), Employee: group.Sum(row => row.EmployeeMonthly), Employer: group.Sum(row => row.EmployerMonthly)));
        return [.. plans.Select(plan => Item(plan, stats.TryGetValue(plan.Id, out var stat) ? stat : (0, 0, 0)))];
    }

    public async Task<PlanItem> SavePlanAsync(Guid? id, PlanBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        BenefitPlan plan;
        if (id is { } existing)
        {
            plan = await dbContext.BenefitPlans.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(BenefitPlan), existing);
            plan.Update(body.Details);
        }
        else
        {
            string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0 || await dbContext.BenefitPlans.AnyAsync(row => row.Code == code, cancellationToken))
            {
                throw new DomainRuleException("PLAN_CODE", "Give the plan a code no other plan uses.");
            }

            plan = BenefitPlan.Create(code, body.Details);
            dbContext.BenefitPlans.Add(plan);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await PlansAsync(includeInactive: true, cancellationToken)).First(item => item.Id == plan.Id);
    }

    public async Task SetPlanActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        BenefitPlan plan = await dbContext.BenefitPlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(BenefitPlan), id);
        if (active)
        {
            plan.Activate();
        }
        else
        {
            plan.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Self-service -------------------------------------------------------------------------

    public async Task<MyBenefits> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        EmployeeSummary person = await employees.GetAsync(me, cancellationToken) ?? throw new NotFoundException("Employee", me);
        IReadOnlyList<PlanItem> plans = await PlansAsync(includeInactive: false, cancellationToken);
        List<EnrollmentItem> enrollments = [.. (await EnrollmentsOfAsync([me], cancellationToken)).Where(enrollment => enrollment.Status is EnrollmentStatus.Pending or EnrollmentStatus.Active && (enrollment.EffectiveTo is null || enrollment.EffectiveTo >= today))];
        Dictionary<Guid, BenefitPlan> entities = await dbContext.BenefitPlans.AsNoTracking().Where(plan => plan.IsActive).ToDictionaryAsync(plan => plan.Id, cancellationToken);
        IReadOnlyList<DependentItem> dependents = await DependentsAsync(me, cancellationToken);

        List<OfferedPlan> offered = [.. plans.Select(plan =>
        {
            EnrollmentItem? enrollment = enrollments.Where(row => row.PlanId == plan.Id).MaxBy(row => row.EffectiveFrom);
            string? reason = enrollment is null && entities.TryGetValue(plan.Id, out BenefitPlan? entity)
                ? entity.Ineligibility(person.EmploymentTypeCode, person.JoiningDate, today) ?? (entity.EnrollmentOpen ? null : "Enrollment is closed for now.")
                : null;
            return new OfferedPlan(plan, reason, enrollment);
        })];
        List<EnrollmentItem> active = [.. enrollments.Where(row => row.Status == EnrollmentStatus.Active)];
        return new MyBenefits(offered, dependents, active.Sum(row => row.EmployeeMonthly), active.Sum(row => row.EmployerMonthly));
    }

    public async Task<EnrollmentItem> EnrollMeAsync(EnrollBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        BenefitPlan plan = await PlanAsync(body.PlanId, cancellationToken);
        if (!plan.EnrollmentOpen)
        {
            throw new DomainRuleException("PLAN_CLOSED", "Enrollment in this plan is closed for now.");
        }

        return await EnrollAsync(me, plan, body.DependentIds ?? [], body.EffectiveFrom, body.Notes, selfService: true, cancellationToken);
    }

    public async Task<EnrollmentItem> UpdateMyDependentsAsync(Guid enrollmentId, IReadOnlyList<Guid> dependentIds, CancellationToken cancellationToken)
    {
        Guid me = Me();
        BenefitEnrollment enrollment = await dbContext.BenefitEnrollments.FirstOrDefaultAsync(row => row.Id == enrollmentId && row.EmployeeId == me, cancellationToken)
            ?? throw new NotFoundException(nameof(BenefitEnrollment), enrollmentId);
        BenefitPlan plan = await PlanAsync(enrollment.PlanId, cancellationToken);
        await CheckDependentsAsync(me, dependentIds, cancellationToken);
        enrollment.Cover(plan, dependentIds);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await EnrollmentsOfAsync([me], cancellationToken)).First(item => item.Id == enrollment.Id);
    }

    /// <summary>Leaves a plan at the end of this month (a pending enrollment is withdrawn). Mandatory plans cannot be left.</summary>
    public async Task<EnrollmentItem> CancelMineAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        Guid me = Me();
        BenefitEnrollment enrollment = await dbContext.BenefitEnrollments.FirstOrDefaultAsync(row => row.Id == enrollmentId && row.EmployeeId == me, cancellationToken)
            ?? throw new NotFoundException(nameof(BenefitEnrollment), enrollmentId);
        BenefitPlan plan = await PlanAsync(enrollment.PlanId, cancellationToken);
        if (plan.Mandatory && enrollment.Status == EnrollmentStatus.Active)
        {
            throw new DomainRuleException("PLAN_MANDATORY", $"{plan.Name} is mandatory and cannot be left.");
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        enrollment.End(new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1));
        await workflow.WithdrawAsync(SubjectType, enrollment.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await EnrollmentsOfAsync([me], cancellationToken)).First(item => item.Id == enrollment.Id);
    }

    // ----- Administration -----------------------------------------------------------------------

    public async Task<IReadOnlyList<EnrollmentItem>> EnrollmentsAsync(Guid? planId, EnrollmentStatus? status, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Benefit.EnrollmentView, cancellationToken);
        IQueryable<BenefitEnrollment> rows = dbContext.BenefitEnrollments.AsNoTracking();
        if (scope is not null)
        {
            rows = rows.Where(row => scope.Contains(row.EmployeeId));
        }

        if (planId is { } plan)
        {
            rows = rows.Where(row => row.PlanId == plan);
        }

        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.EffectiveFrom).Take(1000).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<EnrollmentItem>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
        => await employees.IsInScopeAsync(employeeId, Permissions.Benefit.EnrollmentView, cancellationToken)
            ? await EnrollmentsOfAsync([employeeId], cancellationToken)
            : throw new NotFoundException("Employee", employeeId);

    public async Task<EnrollmentItem> EnrollOnBehalfAsync(EnrollOnBehalfBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!await employees.IsInScopeAsync(body.EmployeeId, Permissions.Benefit.EnrollmentManage, cancellationToken))
        {
            throw new NotFoundException("Employee", body.EmployeeId);
        }

        return await EnrollAsync(body.EmployeeId, await PlanAsync(body.PlanId, cancellationToken), body.DependentIds ?? [], body.EffectiveFrom, body.Notes, selfService: false, cancellationToken);
    }

    public async Task<EnrollmentItem> EndAsync(Guid enrollmentId, DateOnly? lastDay, CancellationToken cancellationToken)
    {
        BenefitEnrollment enrollment = await dbContext.BenefitEnrollments.FirstOrDefaultAsync(row => row.Id == enrollmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(BenefitEnrollment), enrollmentId);
        if (!await employees.IsInScopeAsync(enrollment.EmployeeId, Permissions.Benefit.EnrollmentManage, cancellationToken))
        {
            throw new NotFoundException(nameof(BenefitEnrollment), enrollmentId);
        }

        enrollment.End(lastDay ?? await clock.TodayAsync(cancellationToken));
        await workflow.WithdrawAsync(SubjectType, enrollment.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([enrollment], cancellationToken))[0];
    }

    // ----- Events, workflow and payroll ---------------------------------------------------------

    /// <summary>Enrolls a joiner in every automatic plan they are eligible for, from their joining date.</summary>
    public async Task AutoEnrollAsync(Guid employeeId, DateOnly joined, CancellationToken cancellationToken)
    {
        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        List<BenefitPlan> plans = await dbContext.BenefitPlans.Where(plan => plan.IsActive && plan.AutoEnroll).ToListAsync(cancellationToken);
        HashSet<Guid> enrolled = [.. await dbContext.BenefitEnrollments.Where(row => row.EmployeeId == employeeId && row.Status != EnrollmentStatus.Cancelled && row.Status != EnrollmentStatus.Rejected).Select(row => row.PlanId).ToListAsync(cancellationToken)];
        DateOnly checkOn = joined.AddMonths(plans.Count == 0 ? 0 : plans.Max(plan => plan.MinServiceMonths));
        foreach (BenefitPlan plan in plans.Where(plan => !enrolled.Contains(plan.Id) && plan.Ineligibility(person?.EmploymentTypeCode, joined, checkOn) is null))
        {
            dbContext.BenefitEnrollments.Add(BenefitEnrollment.Join(employeeId, plan, [], joined.AddMonths(plan.MinServiceMonths), pending: false, "Enrolled automatically on joining."));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Coverage ends with employment.</summary>
    public async Task EndForLeaverAsync(Guid employeeId, DateOnly exitDate, CancellationToken cancellationToken)
    {
        foreach (BenefitEnrollment enrollment in await dbContext.BenefitEnrollments
            .Where(row => row.EmployeeId == employeeId && (row.Status == EnrollmentStatus.Active || row.Status == EnrollmentStatus.Pending) && (row.EffectiveTo == null || row.EffectiveTo > exitDate))
            .ToListAsync(cancellationToken))
        {
            enrollment.End(exitDate);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyDecisionAsync(Guid enrollmentId, bool approved, CancellationToken cancellationToken)
    {
        BenefitEnrollment? enrollment = await dbContext.BenefitEnrollments.FirstOrDefaultAsync(row => row.Id == enrollmentId, cancellationToken);
        if (enrollment?.Status != EnrollmentStatus.Pending)
        {
            return;
        }

        enrollment.Decide(approved, timeProvider.GetUtcNow().UtcDateTime);
        string plan = await dbContext.BenefitPlans.Where(row => row.Id == enrollment.PlanId).Select(row => row.Name).FirstAsync(cancellationToken);
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.BenefitEnrollmentDecided,
                [new NotificationRecipient(enrollment.EmployeeId)],
                new Dictionary<string, string> { ["planName"] = plan, ["decision"] = approved ? "approved" : "not approved" },
                "/me/benefits"),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BenefitCharge>> ForPeriodAsync(IEnumerable<Guid> employeeIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        List<Guid> ids = [.. employeeIds.Distinct()];
        var rows = await dbContext.BenefitEnrollments.AsNoTracking()
            .Where(row => ids.Contains(row.EmployeeId) && row.Status == EnrollmentStatus.Active && row.EffectiveFrom <= periodEnd && (row.EffectiveTo == null || row.EffectiveTo >= periodStart))
            .Join(dbContext.BenefitPlans.AsNoTracking(), row => row.PlanId, plan => plan.Id, (row, plan) => new { row, plan })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(pair =>
        {
            decimal share = pair.row.ShareOf(periodStart, periodEnd);
            return new BenefitCharge(
                pair.row.EmployeeId, pair.row.Id, pair.plan.Code, pair.plan.Name, pair.plan.Type.ToString(),
                Math.Round(pair.row.EmployeeMonthly * share, 2), Math.Round(pair.row.EmployerMonthly * share, 2), pair.plan.EmployeeShareTaxDeductible);
        }).Where(charge => charge.EmployeeAmount > 0 || charge.EmployerAmount > 0)];
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<EnrollmentItem> EnrollAsync(Guid employeeId, BenefitPlan plan, IReadOnlyList<Guid> dependents, DateOnly? effectiveFrom, string? notes, bool selfService, CancellationToken cancellationToken)
    {
        EmployeeSummary person = await employees.GetAsync(employeeId, cancellationToken) ?? throw new NotFoundException("Employee", employeeId);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        if (plan.Ineligibility(person.EmploymentTypeCode, person.JoiningDate, today) is { } reason)
        {
            throw new DomainRuleException("PLAN_INELIGIBLE", reason);
        }

        if (await dbContext.BenefitEnrollments.AnyAsync(row => row.EmployeeId == employeeId && row.PlanId == plan.Id
                && (row.Status == EnrollmentStatus.Pending || (row.Status == EnrollmentStatus.Active && (row.EffectiveTo == null || row.EffectiveTo >= today))), cancellationToken))
        {
            throw new DomainRuleException("ENROLLMENT_EXISTS", $"Already enrolled in {plan.Name}.");
        }

        await CheckDependentsAsync(employeeId, dependents, cancellationToken);

        // Self-enrollment starts with next month's payroll; payroll may backdate on someone's behalf.
        DateOnly from = effectiveFrom ?? (selfService ? new DateOnly(today.Year, today.Month, 1).AddMonths(1) : today);
        bool pending = selfService && plan.RequiresApproval;
        var enrollment = BenefitEnrollment.Join(employeeId, plan, dependents, from, pending, notes);
        dbContext.BenefitEnrollments.Add(enrollment);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (pending)
        {
            WorkflowStartResult started = await workflow.StartAsync(
                new WorkflowStartRequest
                {
                    SubjectType = SubjectType,
                    SubjectId = enrollment.Id,
                    Title = $"Enrollment in {plan.Name}",
                    RequesterEmployeeId = employeeId,
                    Link = "/benefits/enrollments",
                    Fields = new Dictionary<string, string>
                    {
                        ["plan"] = plan.Code,
                        ["monthlyCost"] = enrollment.EmployerMonthly.ToString(CultureInfo.InvariantCulture),
                        ["dependents"] = enrollment.DependentIds.Count.ToString(CultureInfo.InvariantCulture),
                    },
                },
                cancellationToken);
            if (started.Outcome == WorkflowStartOutcome.Pending)
            {
                enrollment.AwaitApproval(started.InstanceId!.Value);
            }
            else
            {
                enrollment.Decide(approved: true, timeProvider.GetUtcNow().UtcDateTime);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (await ItemsAsync([enrollment], cancellationToken))[0];
    }

    private async Task CheckDependentsAsync(Guid employeeId, IReadOnlyList<Guid> dependents, CancellationToken cancellationToken)
    {
        if (dependents.Count == 0)
        {
            return;
        }

        HashSet<Guid> family = [.. (await payrollDetails.DependentsAsync([employeeId], cancellationToken)).Select(dependent => dependent.Id)];
        if (dependents.Any(id => !family.Contains(id)))
        {
            throw new DomainRuleException("ENROLLMENT_DEPENDENTS", "Only dependents recorded on the employee's profile can be covered.");
        }
    }

    private async Task<IReadOnlyList<DependentItem>> DependentsAsync(Guid employeeId, CancellationToken cancellationToken)
        => [.. (await payrollDetails.DependentsAsync([employeeId], cancellationToken)).Select(dependent => new DependentItem(dependent.Id, dependent.FullName, dependent.RelationshipCode, dependent.DateOfBirth))];

    private async Task<BenefitPlan> PlanAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.BenefitPlans.AsNoTracking().FirstOrDefaultAsync(plan => plan.Id == id && plan.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(BenefitPlan), id);

    private async Task<IReadOnlyList<EnrollmentItem>> EnrollmentsOfAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. employeeIds];
        return await ItemsAsync(await dbContext.BenefitEnrollments.AsNoTracking().Where(row => ids.Contains(row.EmployeeId)).OrderByDescending(row => row.EffectiveFrom).ToListAsync(cancellationToken), cancellationToken);
    }

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("BENEFIT_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<IReadOnlyList<EnrollmentItem>> ItemsAsync(IReadOnlyList<BenefitEnrollment> enrollments, CancellationToken cancellationToken)
    {
        List<Guid> planIds = [.. enrollments.Select(row => row.PlanId).Distinct()];
        Dictionary<Guid, BenefitPlan> plans = await dbContext.BenefitPlans.AsNoTracking().Where(plan => planIds.Contains(plan.Id)).ToDictionaryAsync(plan => plan.Id, cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(enrollments.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id);
        Dictionary<Guid, DependentInfo> dependents = (await payrollDetails.DependentsAsync(enrollments.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(dependent => dependent.Id);
        return [.. enrollments.Where(row => plans.ContainsKey(row.PlanId)).Select(row =>
        {
            BenefitPlan plan = plans[row.PlanId];
            return new EnrollmentItem(
                row.Id, row.EmployeeId, people.GetValueOrDefault(row.EmployeeId)?.DisplayName, people.GetValueOrDefault(row.EmployeeId)?.EmployeeCode, plan.Id, plan.Code,
                plan.Name, plan.Type, row.Status, row.EffectiveFrom, row.EffectiveTo, row.EmployeeMonthly, row.EmployerMonthly,
                [.. row.DependentIds.Where(dependents.ContainsKey).Select(id => new DependentItem(id, dependents[id].FullName, dependents[id].RelationshipCode, dependents[id].DateOfBirth))],
                row.Notes, plan.Mandatory);
        })];
    }

    private static PlanItem Item(BenefitPlan plan, (int Count, decimal Employee, decimal Employer) stats) => new(
        plan.Id, plan.Code, plan.Name, plan.Type, plan.Provider, plan.Description, plan.CoverageAmount, plan.Currency, plan.EmployeeContribution, plan.EmployerContribution,
        plan.DependentContribution, plan.AllowsDependents, plan.MaxDependents, plan.EmploymentTypes, plan.MinServiceMonths, plan.AutoEnroll, plan.Mandatory, plan.RequiresApproval,
        plan.EnrollmentOpen, plan.EmployeeShareTaxDeductible, plan.IsActive, stats.Count, stats.Employee, stats.Employer);
}

/// <summary>Seeds a tenant's default plans.</summary>
public sealed class BenefitSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> codes = [.. await dbContext.BenefitPlans.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(plan => plan.Code).ToListAsync(cancellationToken)];
        int added = 0;
        foreach ((string code, PlanDetails details) in BenefitDefaults.Plans.Where(seed => !codes.Contains(seed.Code)))
        {
            dbContext.BenefitPlans.Add(BenefitPlan.Create(code, details));
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}

internal sealed class SeedBenefitsOnTenantProvisioned(BenefitSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

internal sealed class AutoEnrollOnHire(BenefitService benefits) : IIntegrationEventConsumer<EmployeeHiredIntegrationEvent>
{
    public Task HandleAsync(EmployeeHiredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return benefits.AutoEnrollAsync(integrationEvent.EmployeeId, integrationEvent.JoiningDate, cancellationToken);
    }
}

internal sealed class EndBenefitsOnExit(BenefitService benefits) : IIntegrationEventConsumer<EmployeeExitedIntegrationEvent>
{
    public Task HandleAsync(EmployeeExitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return benefits.EndForLeaverAsync(integrationEvent.EmployeeId, integrationEvent.ExitDate, cancellationToken);
    }
}

internal sealed class DecideEnrollmentOnApproval(BenefitService benefits) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == BenefitService.SubjectType)
        {
            await benefits.ApplyDecisionAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class DecideEnrollmentOnRejection(BenefitService benefits) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == BenefitService.SubjectType)
        {
            await benefits.ApplyDecisionAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}
