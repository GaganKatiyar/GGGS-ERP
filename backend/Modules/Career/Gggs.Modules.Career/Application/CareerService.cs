using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Career.Domain;
using Gggs.Modules.Career.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Performance.Contracts;
using Gggs.Modules.Skills.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Career.Application;

public sealed record StepItem(int Index, string Title, Guid? JobId, string? JobName, Guid? GradeId, string? GradeName, int MinMonthsInRole, IReadOnlyList<string> Skills, string? Description);

public sealed record PathItem(Guid Id, string Name, string? Description, CareerTrack Track, Guid? DepartmentId, string? DepartmentName, IReadOnlyList<StepItem> Steps, bool IsActive);

public sealed record PathBody(string Name, string? Description, CareerTrack Track, Guid? DepartmentId, IReadOnlyList<CareerStep> Steps, bool IsActive = true);

public sealed record CareerPosition(PathItem Path, int CurrentStep, StepItem? Next, IReadOnlyList<string> SkillsToBuild, int MonthsInRole, bool EligibleByTime);

public sealed record PromotionItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    string? DepartmentName,
    Guid? NominatedByEmployeeId,
    string? NominatedByName,
    string? CurrentDesignation,
    decimal? CurrentAnnualCtc,
    string ProposedDesignation,
    Guid? ProposedJobId,
    string? ProposedJobName,
    Guid? ProposedGradeId,
    string? ProposedGradeName,
    Guid? ProposedPositionId,
    decimal? ProposedAnnualCtc,
    decimal? IncreasePercent,
    DateOnly EffectiveDate,
    string Justification,
    int? Rating,
    PromotionStatus Status,
    Guid? WorkflowInstanceId,
    DateTime? DecidedDate,
    DateOnly? AppliedOn,
    DateTime CreatedDate,
    bool CanEdit);

public sealed record PromotionBody(Guid EmployeeId, PromotionDetails Details, bool Submit);

public sealed record MyCareer(
    string? Designation,
    string? JobName,
    string? GradeName,
    DateOnly? JoiningDate,
    RatingInfo? LatestRating,
    IReadOnlyList<CareerPosition> Paths,
    IReadOnlyList<PromotionItem> Promotions);

public sealed record SuccessorItem(Guid EmployeeId, string? Name, string? Designation, Readiness Readiness, string? Notes, int? Rating, Level? Potential);

public sealed record SuccessionItem(
    Guid Id,
    string RoleTitle,
    Guid? PositionId,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? IncumbentEmployeeId,
    string? IncumbentName,
    Criticality Criticality,
    Criticality VacancyRisk,
    string? Notes,
    IReadOnlyList<SuccessorItem> Successors,
    bool Covered,
    bool CanManage);

public sealed record SuccessionBody(string RoleTitle, Guid? PositionId, Guid? DepartmentId, Guid? IncumbentEmployeeId, Criticality Criticality, Criticality VacancyRisk, string? Notes, IReadOnlyList<SuccessorEntry> Successors);

public sealed record AssessmentBody(Guid EmployeeId, Level Potential, Level FlightRisk, string? Notes);

public sealed record TalentRow(
    Guid EmployeeId,
    string? EmployeeName,
    string? Designation,
    string? DepartmentName,
    int? Rating,
    Level? Performance,
    Level? Potential,
    Level? FlightRisk,
    int? Box,
    string BoxLabel,
    DateOnly? AssessedOn);

public sealed record NineBoxView(IReadOnlyList<TalentRow> People, IReadOnlyDictionary<int, int> Boxes, int Unassessed, int Unrated);

/// <summary>
/// Career and succession (spec: career paths, promotion, succession): paths with steps by job or grade,
/// each person's position and next step with the skills to build, promotion nominations approved through
/// the workflow and applied to the employee and payroll on their date, succession plans for key roles,
/// and the 9-box talent review of performance against potential.
/// </summary>
internal sealed class CareerService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IEmployeePromotions promotions,
    IOrgStructure organization,
    IPayrollSalaries salaries,
    IPerformanceRatings ratings,
    ISkillDirectory skills,
    IWorkflowStarter workflow,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string SubjectType = "career.promotion";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Approve a promotion",
        "career",
        [
            new WorkflowSubjectField("increasePercent", "CTC increase (%)", WorkflowFieldType.Number),
            new WorkflowSubjectField("rating", "Latest rating (1-5)", WorkflowFieldType.Number),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Paths --------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PathItem>> PathsAsync(CancellationToken cancellationToken)
        => await PathItemsAsync(await dbContext.CareerPaths.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<PathItem>> SavePathAsync(Guid? id, PathBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (id is { } existing)
        {
            CareerPath path = await dbContext.CareerPaths.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(CareerPath), existing);
            path.Update(body.Name, body.Description, body.Track, body.DepartmentId, body.Steps, body.IsActive);
        }
        else
        {
            dbContext.CareerPaths.Add(CareerPath.Create(body.Name, body.Description, body.Track, body.DepartmentId, body.Steps));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await PathsAsync(cancellationToken);
    }

    /// <summary>The caller's role, rating, where they stand on each path that has their job or grade, the next step and the skills to build for it.</summary>
    public async Task<MyCareer> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = currentUser.EmployeeId ?? throw new DomainRuleException("CAREER_NO_EMPLOYEE", "Your account is not linked to an employee record.");
        EmployeeSummary person = await employees.GetAsync(me, cancellationToken) ?? throw new NotFoundException("Employee", me);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<CareerPath> paths = await dbContext.CareerPaths.AsNoTracking().Where(row => row.IsActive).ToListAsync(cancellationToken);
        List<PathItem> items = await PathItemsAsync(paths, cancellationToken);
        HashSet<string> mySkills = new((await skills.LevelsAsync([me], cancellationToken)).Where(level => level.Level >= 3).Select(level => level.SkillName), StringComparer.OrdinalIgnoreCase);
        int months = person.JoiningDate is { } joined ? Math.Max(0, ((today.Year - joined.Year) * 12) + today.Month - joined.Month) : 0;
        var positions = new List<CareerPosition>();
        foreach (CareerPath path in paths)
        {
            int step = path.StepOf(person.JobId, person.GradeId);
            if (step < 0)
            {
                continue;
            }

            PathItem item = items.First(row => row.Id == path.Id);
            StepItem? next = step + 1 < item.Steps.Count ? item.Steps[step + 1] : null;
            positions.Add(new CareerPosition(
                item, step, next, next is null ? [] : [.. next.Skills.Where(skill => !mySkills.Contains(skill))], months, next is null || months >= item.Steps[step].MinMonthsInRole));
        }

        IReadOnlyDictionary<Guid, RatingInfo> latest = await ratings.LatestAsync([me], cancellationToken);
        Names names = await NamesAsync([person.JobId], [person.GradeId], cancellationToken);
        List<PromotionNomination> mine = await dbContext.Promotions.AsNoTracking()
            .Where(row => row.EmployeeId == me && (row.Status == PromotionStatus.Applied || row.Status == PromotionStatus.Approved))
            .OrderByDescending(row => row.EffectiveDate).ToListAsync(cancellationToken);
        return new MyCareer(
            person.Designation, names.Job(person.JobId), names.Grade(person.GradeId), person.JoiningDate, latest.GetValueOrDefault(me), positions,
            [.. (await PromotionItemsAsync(mine, cancellationToken)).Select(row => row with { Justification = string.Empty, CurrentAnnualCtc = null, ProposedAnnualCtc = null, IncreasePercent = null })]);
    }

    // ----- Promotions ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<PromotionItem>> PromotionsAsync(PromotionStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<PromotionNomination> rows = await VisiblePromotionsAsync(cancellationToken);
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return await PromotionItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<PromotionItem> PromotionAsync(Guid id, CancellationToken cancellationToken)
    {
        PromotionNomination nomination = await dbContext.Promotions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(PromotionNomination), id);

        // An approver who acts on the nomination's workflow task may read it without career access.
        if ((await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Career.PromotionView)
            && !await (await VisiblePromotionsAsync(cancellationToken)).AnyAsync(row => row.Id == id, cancellationToken)
            && !(await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Workflow.TaskAct))
        {
            throw new NotFoundException(nameof(PromotionNomination), id);
        }

        return (await PromotionItemsAsync([nomination], cancellationToken))[0];
    }

    public async Task<PromotionItem> NominateAsync(PromotionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        EmployeeSummary person = await employees.GetAsync(body.EmployeeId, cancellationToken) ?? throw new NotFoundException("Employee", body.EmployeeId);
        await EnsureManagesAsync(person.Id, cancellationToken);
        if (await dbContext.Promotions.AnyAsync(row => row.EmployeeId == person.Id && (row.Status == PromotionStatus.Draft || row.Status == PromotionStatus.PendingApproval || row.Status == PromotionStatus.Approved), cancellationToken))
        {
            throw new DomainRuleException("PROMOTION_EXISTS", $"{person.DisplayName} already has a promotion in progress.");
        }

        await ValidateAsync(body.Details, cancellationToken);
        decimal? ctc = (await salaries.CurrentAsync([person.Id], null, cancellationToken)).TryGetValue(person.Id, out SalaryInfo? salary) ? salary.AnnualCtc : null;
        int? rating = (await ratings.LatestAsync([person.Id], cancellationToken)).TryGetValue(person.Id, out RatingInfo? latest) ? latest.Rating : null;
        var nomination = PromotionNomination.Draft(person.Id, currentUser.EmployeeId, person.Designation, ctc, rating, body.Details);
        dbContext.Promotions.Add(nomination);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (body.Submit)
        {
            await SubmitAsync(nomination, cancellationToken);
        }

        return await PromotionAsync(nomination.Id, cancellationToken);
    }

    public async Task<PromotionItem> UpdatePromotionAsync(Guid id, PromotionDetails details, CancellationToken cancellationToken)
    {
        PromotionNomination nomination = await ManagedPromotionAsync(id, cancellationToken);
        await ValidateAsync(details, cancellationToken);
        nomination.Update(details);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PromotionAsync(id, cancellationToken);
    }

    public async Task<PromotionItem> PromotionActionAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        PromotionNomination nomination = await ManagedPromotionAsync(id, cancellationToken);
        switch (action)
        {
            case "submit":
                await SubmitAsync(nomination, cancellationToken);
                break;
            case "withdraw":
                if (nomination.Status == PromotionStatus.PendingApproval)
                {
                    await workflow.WithdrawAsync(SubjectType, nomination.Id, cancellationToken);
                }

                nomination.Withdraw();
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            default:
                throw new DomainRuleException("PROMOTION_ACTION", $"'{action}' is not a promotion action.");
        }

        return await PromotionAsync(id, cancellationToken);
    }

    public async Task DecideAsync(Guid id, bool? approved, CancellationToken cancellationToken)
    {
        PromotionNomination? nomination = await dbContext.Promotions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (nomination?.Status != PromotionStatus.PendingApproval)
        {
            return;
        }

        nomination.Decide(approved, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (approved is not null)
        {
            await NotifyDecisionAsync(nomination, approved.Value ? "approved" : "rejected", cancellationToken);
        }

        if (nomination.IsDue(await clock.TodayAsync(cancellationToken)))
        {
            await ApplyAsync(nomination, cancellationToken);
        }
    }

    /// <summary>Applies approved promotions whose date has come. Returns how many were applied.</summary>
    public async Task<int> ApplyDueAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<PromotionNomination> due = await dbContext.Promotions.Where(row => row.Status == PromotionStatus.Approved && row.EffectiveDate <= today).ToListAsync(cancellationToken);
        foreach (PromotionNomination nomination in due)
        {
            await ApplyAsync(nomination, cancellationToken);
        }

        return due.Count;
    }

    // ----- Succession and talent review ---------------------------------------------------------

    public async Task<IReadOnlyList<SuccessionItem>> SuccessionAsync(CancellationToken cancellationToken)
    {
        List<SuccessionPlan> plans = await dbContext.SuccessionPlans.AsNoTracking().OrderByDescending(row => row.Criticality).ThenBy(row => row.RoleTitle).ToListAsync(cancellationToken);
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Career.SuccessionView, cancellationToken);
        if (scope is not null)
        {
            plans = [.. plans.Where(plan => plan.IncumbentEmployeeId is { } incumbent && scope.Contains(incumbent))];
        }

        return await SuccessionItemsAsync(plans, cancellationToken);
    }

    public async Task<IReadOnlyList<SuccessionItem>> SaveSuccessionAsync(Guid? id, SuccessionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (Guid person in new[] { body.IncumbentEmployeeId }.OfType<Guid>().Concat((body.Successors ?? []).Select(entry => entry.EmployeeId)))
        {
            if (!await employees.IsActiveAsync(person, cancellationToken))
            {
                throw new DomainRuleException("SUCCESSION_PERSON", "The incumbent and successors must be active employees.");
            }
        }

        if (id is { } existing)
        {
            SuccessionPlan plan = await dbContext.SuccessionPlans.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(SuccessionPlan), existing);
            plan.Update(body.RoleTitle, body.PositionId, body.DepartmentId, body.IncumbentEmployeeId, body.Criticality, body.VacancyRisk, body.Notes, body.Successors ?? []);
        }
        else
        {
            dbContext.SuccessionPlans.Add(SuccessionPlan.Create(body.RoleTitle, body.PositionId, body.DepartmentId, body.IncumbentEmployeeId, body.Criticality, body.VacancyRisk, body.Notes, body.Successors ?? []));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await SuccessionAsync(cancellationToken);
    }

    public async Task<NineBoxView> AssessAsync(AssessmentBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.EmployeeId == currentUser.EmployeeId || !await employees.IsInScopeAsync(body.EmployeeId, Permissions.Career.TalentReviewManage, cancellationToken))
        {
            throw new ForbiddenException("ASSESSMENT_SCOPE", "You can assess the potential of people you manage, not your own.");
        }

        dbContext.TalentAssessments.Add(TalentAssessment.Record(body.EmployeeId, body.Potential, body.FlightRisk, body.Notes, currentUser.EmployeeId, await clock.TodayAsync(cancellationToken)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await NineBoxAsync(null, cancellationToken);
    }

    /// <summary>Everyone in talent-review scope placed on the 9-box from their latest rating and latest assessment.</summary>
    public async Task<NineBoxView> NineBoxAsync(Guid? departmentId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Career.TalentReviewManage, cancellationToken);
        IReadOnlyList<Guid> people = scope is null
            ? await employees.ListActiveIdsAsync(departmentId is { } department ? [department] : null, null, cancellationToken)
            : [.. scope.Where(id => id != currentUser.EmployeeId)];
        IReadOnlyList<EmployeeSummary> summaries = await employees.GetManyAsync(people, cancellationToken);
        List<EmployeeSummary> active = [.. summaries.Where(person => person.ExitDate is null && (departmentId is null || person.DepartmentId == departmentId))];
        List<Guid> ids = [.. active.Select(person => person.Id)];
        IReadOnlyDictionary<Guid, RatingInfo> latest = await ratings.LatestAsync(ids, cancellationToken);
        Dictionary<Guid, TalentAssessment> assessed = (await dbContext.TalentAssessments.AsNoTracking().Where(row => ids.Contains(row.EmployeeId)).ToListAsync(cancellationToken))
            .GroupBy(row => row.EmployeeId).ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.AssessedOn).ThenByDescending(row => row.CreatedDate).First());
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, active.Where(person => person.DepartmentId != null).Select(person => person.DepartmentId!.Value).Distinct(), cancellationToken);
        List<TalentRow> rows = [.. active.Select(person =>
        {
            int? rating = latest.TryGetValue(person.Id, out RatingInfo? info) ? info.Rating : null;
            Level? performance = rating is { } value ? NineBox.PerformanceBand(value) : null;
            TalentAssessment? assessment = assessed.GetValueOrDefault(person.Id);
            int? box = performance is { } band && assessment is not null ? NineBox.Box(band, assessment.Potential) : null;
            return new TalentRow(
                person.Id, person.DisplayName, person.Designation, person.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null, rating, performance,
                assessment?.Potential, assessment?.FlightRisk, box, box is { } b ? NineBox.Label(b) : "Unrated", assessment?.AssessedOn);
        }).OrderByDescending(row => row.Box ?? 0).ThenBy(row => row.EmployeeName, StringComparer.CurrentCulture)];
        return new NineBoxView(
            rows,
            Enumerable.Range(1, 9).ToDictionary(box => box, box => rows.Count(row => row.Box == box)),
            rows.Count(row => row.Potential is null),
            rows.Count(row => row.Rating is null));
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task SubmitAsync(PromotionNomination nomination, CancellationToken cancellationToken)
    {
        nomination.Submit();
        await dbContext.SaveChangesAsync(cancellationToken);
        string name = (await employees.GetAsync(nomination.EmployeeId, cancellationToken))?.DisplayName ?? "Employee";
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = nomination.Id,
                Title = $"Promotion: {name} to {nomination.ProposedDesignation}",
                RequesterEmployeeId = currentUser.EmployeeId,
                Link = $"/career/promotions/{nomination.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["increasePercent"] = (nomination.IncreasePercent ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["rating"] = (nomination.Rating ?? 0).ToString(CultureInfo.InvariantCulture),
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            nomination.AwaitApproval(started.InstanceId!.Value);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            nomination.Decide(approved: true, Now);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (nomination.IsDue(await clock.TodayAsync(cancellationToken)))
            {
                await ApplyAsync(nomination, cancellationToken);
            }
        }
    }

    /// <summary>Applies the promotion to the employee record and, with a new CTC, to payroll.</summary>
    private async Task ApplyAsync(PromotionNomination nomination, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        string reason = $"Promotion to {nomination.ProposedDesignation}";
        await promotions.PromoteAsync(
            new Promotion(nomination.EmployeeId, nomination.EffectiveDate, nomination.ProposedPositionId, nomination.ProposedJobId, nomination.ProposedGradeId, nomination.ProposedDesignation, reason),
            cancellationToken);
        if (nomination.ProposedAnnualCtc is { } ctc)
        {
            await salaries.ReviseAsync(new SalaryRevisionRequest(nomination.EmployeeId, ctc, nomination.EffectiveDate, null, "Promotion", SubjectType, nomination.Id), cancellationToken);
        }

        nomination.MarkApplied(today);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyDecisionAsync(PromotionNomination nomination, string status, CancellationToken cancellationToken)
    {
        if (nomination.NominatedByEmployeeId is not { } nominator)
        {
            return;
        }

        string name = (await employees.GetAsync(nomination.EmployeeId, cancellationToken))?.DisplayName ?? "the employee";
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.PromotionDecided,
                [new NotificationRecipient(nominator)],
                new Dictionary<string, string> { ["employeeName"] = name, ["designation"] = nomination.ProposedDesignation, ["status"] = status },
                $"/career/promotions/{nomination.Id}"),
            cancellationToken);
    }

    private async Task ValidateAsync(PromotionDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        List<OrgReference> references = [];
        if (details.ProposedJobId is { } job)
        {
            references.Add(new OrgReference(OrgUnitKind.Job, job));
        }

        if (details.ProposedGradeId is { } grade)
        {
            references.Add(new OrgReference(OrgUnitKind.Grade, grade));
        }

        if (details.ProposedPositionId is { } position)
        {
            references.Add(new OrgReference(OrgUnitKind.Position, position));
        }

        IReadOnlyList<OrgReference> invalid = references.Count == 0 ? [] : await organization.FindInvalidAsync(references, cancellationToken);
        if (invalid.Count > 0)
        {
            throw new DomainRuleException("PROMOTION_ORG", $"The {invalid[0].Kind.ToString().ToLowerInvariant()} chosen is not active.");
        }
    }

    private async Task<IQueryable<PromotionNomination>> VisiblePromotionsAsync(CancellationToken cancellationToken)
    {
        IQueryable<PromotionNomination> rows = dbContext.Promotions.AsNoTracking();
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        IReadOnlyCollection<Guid>? scope = granted.Has(Permissions.Career.PromotionView) ? await employees.InScopeIdsAsync(Permissions.Career.PromotionView, cancellationToken) : [];
        if (scope is null)
        {
            return rows;
        }

        Guid? me = currentUser.EmployeeId;
        return rows.Where(row => row.NominatedByEmployeeId == me || scope.Contains(row.EmployeeId));
    }

    private async Task<PromotionNomination> ManagedPromotionAsync(Guid id, CancellationToken cancellationToken)
    {
        PromotionNomination nomination = await dbContext.Promotions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(PromotionNomination), id);
        await EnsureManagesAsync(nomination.EmployeeId, cancellationToken);
        return nomination;
    }

    private async Task EnsureManagesAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (employeeId == currentUser.EmployeeId || !await employees.IsInScopeAsync(employeeId, Permissions.Career.PromotionManage, cancellationToken))
        {
            throw new ForbiddenException("PROMOTION_SCOPE", "Promotions are proposed for the people you manage, not yourself.");
        }
    }

    private async Task<List<PathItem>> PathItemsAsync(IReadOnlyList<CareerPath> paths, CancellationToken cancellationToken)
    {
        List<CareerStep> steps = [.. paths.SelectMany(path => path.Steps)];
        Names names = await NamesAsync(steps.Select(step => step.JobId), steps.Select(step => step.GradeId), cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, paths.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value).Distinct(), cancellationToken);
        return [.. paths.Select(path => new PathItem(
            path.Id, path.Name, path.Description, path.Track, path.DepartmentId, path.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null,
            [.. path.Steps.Select((step, index) => new StepItem(index, step.Title, step.JobId, names.Job(step.JobId), step.GradeId, names.Grade(step.GradeId), step.MinMonthsInRole, step.Skills, step.Description))],
            path.IsActive))];
    }

    private async Task<List<PromotionItem>> PromotionItemsAsync(IReadOnlyList<PromotionNomination> rows, CancellationToken cancellationToken)
    {
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(rows.SelectMany(row => new[] { (Guid?)row.EmployeeId, row.NominatedByEmployeeId }).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);
        Names names = await NamesAsync(rows.Select(row => row.ProposedJobId), rows.Select(row => row.ProposedGradeId), cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, people.Values.Where(person => person.DepartmentId != null).Select(person => person.DepartmentId!.Value).Distinct(), cancellationToken);
        IReadOnlyCollection<Guid>? scope = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Career.PromotionManage) ? await employees.InScopeIdsAsync(Permissions.Career.PromotionManage, cancellationToken) : [];
        return [.. rows.Select(row =>
        {
            EmployeeSummary? person = people.GetValueOrDefault(row.EmployeeId);
            return new PromotionItem(
                row.Id, row.EmployeeId, person?.DisplayName, person?.EmployeeCode, person?.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null,
                row.NominatedByEmployeeId, row.NominatedByEmployeeId is { } by ? people.GetValueOrDefault(by)?.DisplayName : null, row.CurrentDesignation, row.CurrentAnnualCtc,
                row.ProposedDesignation, row.ProposedJobId, names.Job(row.ProposedJobId), row.ProposedGradeId, names.Grade(row.ProposedGradeId), row.ProposedPositionId,
                row.ProposedAnnualCtc, row.IncreasePercent, row.EffectiveDate, row.Justification, row.Rating, row.Status, row.WorkflowInstanceId, row.DecidedDate, row.AppliedOn,
                row.CreatedDate, row.Status is PromotionStatus.Draft or PromotionStatus.Rejected && row.EmployeeId != currentUser.EmployeeId && (scope is null || scope.Contains(row.EmployeeId)));
        })];
    }

    private async Task<List<SuccessionItem>> SuccessionItemsAsync(IReadOnlyList<SuccessionPlan> plans, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. plans.SelectMany(plan => new[] { plan.IncumbentEmployeeId }.OfType<Guid>().Concat(plan.Successors.Select(entry => entry.EmployeeId))).Distinct()];
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(ids, cancellationToken)).ToDictionary(person => person.Id);
        IReadOnlyDictionary<Guid, RatingInfo> latest = await ratings.LatestAsync(ids, cancellationToken);
        Dictionary<Guid, Level> potential = (await dbContext.TalentAssessments.AsNoTracking().Where(row => ids.Contains(row.EmployeeId)).ToListAsync(cancellationToken))
            .GroupBy(row => row.EmployeeId).ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.AssessedOn).First().Potential);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, plans.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value).Distinct(), cancellationToken);
        bool canManage = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Career.SuccessionManage);
        return [.. plans.Select(plan => new SuccessionItem(
            plan.Id, plan.RoleTitle, plan.PositionId, plan.DepartmentId, plan.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null,
            plan.IncumbentEmployeeId, plan.IncumbentEmployeeId is { } incumbent ? people.GetValueOrDefault(incumbent)?.DisplayName : null, plan.Criticality, plan.VacancyRisk, plan.Notes,
            [.. plan.Successors.Select(entry => new SuccessorItem(
                entry.EmployeeId, people.GetValueOrDefault(entry.EmployeeId)?.DisplayName, people.GetValueOrDefault(entry.EmployeeId)?.Designation, entry.Readiness, entry.Notes,
                latest.TryGetValue(entry.EmployeeId, out RatingInfo? rating) ? rating.Rating : null, potential.TryGetValue(entry.EmployeeId, out Level level) ? level : null))],
            plan.Covered, canManage))];
    }

    private async Task<Names> NamesAsync(IEnumerable<Guid?> jobs, IEnumerable<Guid?> grades, CancellationToken cancellationToken) => new(
        await organization.GetNamesAsync(OrgUnitKind.Job, jobs.OfType<Guid>().Distinct(), cancellationToken),
        await organization.GetNamesAsync(OrgUnitKind.Grade, grades.OfType<Guid>().Distinct(), cancellationToken));

    private sealed record Names(IReadOnlyDictionary<Guid, string> Jobs, IReadOnlyDictionary<Guid, string> Grades)
    {
        public string? Job(Guid? id) => id is { } value ? Jobs.GetValueOrDefault(value) : null;

        public string? Grade(Guid? id) => id is { } value ? Grades.GetValueOrDefault(value) : null;
    }
}
