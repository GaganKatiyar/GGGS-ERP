using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Performance.Domain;
using Gggs.Modules.Performance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Performance.Application;

public sealed record KeyResultItem(Guid Id, string Title, MetricType MetricType, decimal StartValue, decimal TargetValue, decimal CurrentValue, string? Unit, decimal Weight, decimal Progress);

public sealed record GoalItem(
    Guid Id,
    string Title,
    string? Description,
    GoalType Type,
    GoalLevel Level,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? ParentGoalId,
    string? ParentTitle,
    Guid? CycleId,
    DateOnly StartDate,
    DateOnly DueDate,
    decimal Weight,
    GoalStatus Status,
    decimal Progress,
    Confidence? Confidence,
    DateOnly? LastCheckInOn,
    int KeyResults,
    int Children,
    bool CanEdit);

public sealed record CheckInItem(Guid Id, DateOnly CheckInOn, decimal Progress, Confidence Confidence, string? Note, string? ByName);

public sealed record GoalView(GoalItem Goal, IReadOnlyList<KeyResultItem> KeyResults, IReadOnlyList<CheckInItem> CheckIns, IReadOnlyList<GoalItem> Children);

public sealed record GoalQuery
{
    public Guid? OwnerId { get; init; }

    public GoalLevel? Level { get; init; }

    public Guid? CycleId { get; init; }

    public GoalStatus? Status { get; init; }

    public Guid? DepartmentId { get; init; }
}

public sealed record KeyResultBody(Guid? Id, string Title, MetricType MetricType, decimal StartValue, decimal TargetValue, string? Unit, decimal Weight);

public sealed record GoalBody(GoalDetails Details, IReadOnlyList<KeyResultBody>? KeyResults, bool Activate);

public sealed record CheckInBody(decimal? Progress, Confidence Confidence, string? Note, IReadOnlyList<KeyResultValue>? Values);

/// <summary>
/// Goals (spec: OKR and KPI): company, department and team goals everyone can align to, individual
/// goals cascading from them, key results, check-ins and the progress that feeds reviews.
/// </summary>
internal sealed class GoalService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock)
{
    public async Task<IReadOnlyList<GoalItem>> GoalsAsync(GoalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Goal> rows = await VisibleAsync(cancellationToken);
        if (query.OwnerId is { } owner)
        {
            rows = rows.Where(row => row.OwnerEmployeeId == owner);
        }

        if (query.Level is { } level)
        {
            rows = rows.Where(row => row.Level == level);
        }

        if (query.CycleId is { } cycle)
        {
            rows = rows.Where(row => row.CycleId == cycle);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.DepartmentId is { } department)
        {
            rows = rows.Where(row => row.DepartmentId == department);
        }

        return await ItemsAsync(await rows.OrderBy(row => row.Level).ThenBy(row => row.DueDate).Take(1000).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<GoalItem>> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        List<Goal> goals = await dbContext.Goals.AsNoTracking()
            .Where(row => row.OwnerEmployeeId == me && row.Status != GoalStatus.Cancelled)
            .OrderBy(row => row.DueDate)
            .ToListAsync(cancellationToken);
        return await ItemsAsync(goals, cancellationToken);
    }

    public async Task<GoalView> GoalAsync(Guid id, CancellationToken cancellationToken)
    {
        Goal goal = await (await VisibleAsync(cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Goal), id);
        List<KeyResult> results = await dbContext.KeyResults.AsNoTracking().Where(row => row.GoalId == id).OrderBy(row => row.SortOrder).ToListAsync(cancellationToken);
        List<GoalCheckIn> checkIns = await dbContext.GoalCheckIns.AsNoTracking().Where(row => row.GoalId == id).OrderByDescending(row => row.CheckInOn).ThenByDescending(row => row.CreatedDate).Take(50).ToListAsync(cancellationToken);
        List<Goal> children = await (await VisibleAsync(cancellationToken)).Where(row => row.ParentGoalId == id).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(checkIns.Select(row => row.ByEmployeeId), cancellationToken);
        return new GoalView(
            (await ItemsAsync([goal], cancellationToken))[0],
            [.. results.Select(Item)],
            [.. checkIns.Select(row => new CheckInItem(row.Id, row.CheckInOn, row.Progress, row.Confidence, row.Note, row.ByEmployeeId is { } by ? names.GetValueOrDefault(by) : null))],
            await ItemsAsync(children, cancellationToken));
    }

    public async Task<GoalView> CreateAsync(GoalBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        GoalDetails details = body.Details with { OwnerEmployeeId = body.Details.Level == GoalLevel.Individual ? body.Details.OwnerEmployeeId ?? currentUser.EmployeeId : body.Details.OwnerEmployeeId };
        await EnsureCanManageAsync(details, cancellationToken);
        await ValidateAsync(null, details, cancellationToken);
        var goal = Goal.Create(details, body.Activate);
        dbContext.Goals.Add(goal);
        int order = 0;
        foreach (KeyResultBody result in body.KeyResults ?? [])
        {
            dbContext.KeyResults.Add(KeyResult.Add(goal.Id, Details(result), order++));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GoalAsync(goal.Id, cancellationToken);
    }

    public async Task<GoalView> UpdateAsync(Guid id, GoalDetails details, CancellationToken cancellationToken)
    {
        Goal goal = await ManagedAsync(id, cancellationToken);
        await EnsureCanManageAsync(details, cancellationToken);
        await ValidateAsync(goal.Id, details, cancellationToken);
        goal.Update(details);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GoalAsync(id, cancellationToken);
    }

    /// <summary>Replaces the goal's key results; those kept (by id) keep the value reached so far.</summary>
    public async Task<GoalView> SaveKeyResultsAsync(Guid id, IReadOnlyList<KeyResultBody> bodies, CancellationToken cancellationToken)
    {
        Goal goal = await ManagedAsync(id, cancellationToken);
        goal.EnsureOpen();
        List<KeyResult> existing = await dbContext.KeyResults.Where(row => row.GoalId == id).ToListAsync(cancellationToken);
        HashSet<Guid> kept = [.. (bodies ?? []).Where(body => body.Id is not null).Select(body => body.Id!.Value)];
        dbContext.KeyResults.RemoveRange(existing.Where(row => !kept.Contains(row.Id)));
        int order = 0;
        foreach (KeyResultBody body in bodies ?? [])
        {
            if (body.Id is { } resultId && existing.Find(row => row.Id == resultId) is { } result)
            {
                result.Update(Details(body));
            }
            else
            {
                dbContext.KeyResults.Add(KeyResult.Add(id, Details(body), order));
            }

            order++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        List<KeyResult> results = await dbContext.KeyResults.Where(row => row.GoalId == id).ToListAsync(cancellationToken);
        if (results.Count > 0)
        {
            goal.Record(KeyResult.Rollup(results), null, goal.LastCheckInOn ?? await clock.TodayAsync(cancellationToken));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GoalAsync(id, cancellationToken);
    }

    public async Task<GoalView> CheckInAsync(Guid id, CheckInBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Goal goal = await ManagedAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<KeyResult> results = await dbContext.KeyResults.Where(row => row.GoalId == id).ToListAsync(cancellationToken);
        decimal progress;
        if (results.Count > 0)
        {
            foreach (KeyResultValue value in body.Values ?? [])
            {
                KeyResult result = results.Find(row => row.Id == value.KeyResultId) ?? throw new DomainRuleException("CHECK_IN_RESULT", "A value names a key result of another goal.");
                result.Record(value.Value);
            }

            progress = KeyResult.Rollup(results);
        }
        else
        {
            progress = body.Progress ?? throw new DomainRuleException("CHECK_IN_PROGRESS", "Say how far along the goal is (0-100%).");
        }

        goal.Record(progress, body.Confidence, today);
        dbContext.GoalCheckIns.Add(GoalCheckIn.Record(id, currentUser.EmployeeId, today, progress, body.Confidence, body.Note,
            [.. results.Select(result => new KeyResultValue(result.Id, result.CurrentValue))]));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GoalAsync(id, cancellationToken);
    }

    public async Task<GoalView> ActAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        Goal goal = await ManagedAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        switch (action)
        {
            case "activate":
                goal.Activate();
                break;
            case "complete":
                goal.Complete(today);
                break;
            case "cancel":
                goal.Cancel();
                break;
            case "reopen":
                goal.Reopen();
                break;
            default:
                throw new DomainRuleException("GOAL_ACTION", $"'{action}' is not a goal action.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GoalAsync(id, cancellationToken);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("GOAL_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private static KeyResultDetails Details(KeyResultBody body) => new(body.Title, body.MetricType, body.StartValue, body.TargetValue, body.Unit, body.Weight <= 0 ? 1 : body.Weight);

    private static KeyResultItem Item(KeyResult row) => new(row.Id, row.Title, row.MetricType, row.StartValue, row.TargetValue, row.CurrentValue, row.Unit, row.Weight, row.Progress);

    /// <summary>
    /// Company, department and team goals are visible to everyone, so people can align to them;
    /// individual goals to their owner, and to whoever has the owner in goal or review scope.
    /// </summary>
    private async Task<IQueryable<Goal>> VisibleAsync(CancellationToken cancellationToken)
    {
        IQueryable<Goal> rows = dbContext.Goals.AsNoTracking();
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        IReadOnlyCollection<Guid>? goalScope = granted.Has(Permissions.Performance.GoalManage) ? await employees.InScopeIdsAsync(Permissions.Performance.GoalManage, cancellationToken) : [];
        IReadOnlyCollection<Guid>? reviewScope = granted.Has(Permissions.Performance.ReviewView) ? await employees.InScopeIdsAsync(Permissions.Performance.ReviewView, cancellationToken) : [];
        if (goalScope is null || reviewScope is null)
        {
            return rows;
        }

        List<Guid> scope = [.. goalScope.Concat(reviewScope).Distinct()];
        Guid? me = currentUser.EmployeeId;
        return rows.Where(row => row.Level != GoalLevel.Individual || row.OwnerEmployeeId == me || (row.OwnerEmployeeId != null && scope.Contains(row.OwnerEmployeeId.Value)));
    }

    private async Task<Goal> ManagedAsync(Guid id, CancellationToken cancellationToken)
    {
        Goal goal = await dbContext.Goals.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Goal), id);
        if (!await CanManageAsync(goal.Level, goal.OwnerEmployeeId, cancellationToken))
        {
            throw new NotFoundException(nameof(Goal), id);
        }

        return goal;
    }

    private async Task EnsureCanManageAsync(GoalDetails details, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(details.Level, details.OwnerEmployeeId, cancellationToken))
        {
            throw new ForbiddenException("GOAL_FORBIDDEN", details.Level == GoalLevel.Individual
                ? "You can set goals for yourself and for the people you manage."
                : "Company, department and team goals are set by those who manage goals.");
        }
    }

    private async Task<bool> CanManageAsync(GoalLevel level, Guid? owner, CancellationToken cancellationToken)
        => (await RightsAsync(cancellationToken)).CanManage(level, owner);

    private async Task<GoalRights> RightsAsync(CancellationToken cancellationToken)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        bool manage = granted.Has(Permissions.Performance.GoalManage);
        IReadOnlyCollection<Guid>? scope = manage ? await employees.InScopeIdsAsync(Permissions.Performance.GoalManage, cancellationToken) : [];
        return new GoalRights(currentUser.EmployeeId, granted.Has(Permissions.Performance.GoalManageOwn), manage, scope is null ? null : [.. scope]);
    }

    /// <summary>
    /// Who may change which goals: one's own individual goals; those of people in goal scope;
    /// department and team goals with goal management; company goals only with it tenant-wide.
    /// </summary>
    private sealed record GoalRights(Guid? Me, bool Own, bool Manage, HashSet<Guid>? Scope)
    {
        public bool CanManage(GoalLevel level, Guid? owner) => level switch
        {
            GoalLevel.Company => Manage && Scope is null,
            GoalLevel.Department or GoalLevel.Team => Manage,
            _ => owner is { } person && ((Own && person == Me) || (Manage && (Scope is null || Scope.Contains(person)))),
        };
    }

    private async Task ValidateAsync(Guid? goalId, GoalDetails details, CancellationToken cancellationToken)
    {
        if (details.ParentGoalId is { } parentId)
        {
            Goal parent = await dbContext.Goals.AsNoTracking().FirstOrDefaultAsync(row => row.Id == parentId, cancellationToken) ?? throw new NotFoundException(nameof(Goal), parentId);
            if (parent.Level > details.Level)
            {
                throw new DomainRuleException("GOAL_PARENT", "A goal aligns to a goal at its own level or above.");
            }

            for (Goal? cursor = parent; cursor?.ParentGoalId is { } next;)
            {
                if (next == goalId)
                {
                    throw new DomainRuleException("GOAL_PARENT", "That alignment would make a loop.");
                }

                cursor = await dbContext.Goals.AsNoTracking().FirstOrDefaultAsync(row => row.Id == next, cancellationToken);
            }
        }

        if (details.OwnerEmployeeId is { } owner && !await employees.IsActiveAsync(owner, cancellationToken))
        {
            throw new DomainRuleException("GOAL_OWNER", "The owner must be an active employee.");
        }

        if (details.Level == GoalLevel.Individual && details.OwnerEmployeeId is { } person)
        {
            decimal others = await dbContext.Goals.AsNoTracking()
                .Where(row => row.Id != goalId && row.OwnerEmployeeId == person && row.Level == GoalLevel.Individual && row.CycleId == details.CycleId
                    && (row.Status == GoalStatus.Draft || row.Status == GoalStatus.Active))
                .SumAsync(row => row.Weight, cancellationToken);
            if (others + details.Weight > 100)
            {
                throw new DomainRuleException("GOAL_WEIGHT", $"The owner's goals would weigh {others + details.Weight}% in total; keep it at 100% or less.");
            }
        }
    }

    private async Task<List<GoalItem>> ItemsAsync(IReadOnlyList<Goal> goals, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. goals.Select(row => row.Id)];
        List<Guid> parentIds = [.. goals.Where(row => row.ParentGoalId != null).Select(row => row.ParentGoalId!.Value).Distinct()];
        Dictionary<Guid, string> parents = await dbContext.Goals.AsNoTracking().Where(row => parentIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Title, cancellationToken);
        Dictionary<Guid, int> results = await dbContext.KeyResults.AsNoTracking().Where(row => ids.Contains(row.GoalId))
            .GroupBy(row => row.GoalId).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        Dictionary<Guid, int> children = await dbContext.Goals.AsNoTracking().Where(row => row.ParentGoalId != null && ids.Contains(row.ParentGoalId.Value))
            .GroupBy(row => row.ParentGoalId!.Value).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        Dictionary<Guid, string> owners = await NamesAsync(goals.Select(row => row.OwnerEmployeeId), cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await organization.GetNamesAsync(OrgUnitKind.Department, goals.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value).Distinct(), cancellationToken);
        GoalRights rights = await RightsAsync(cancellationToken);
        var items = new List<GoalItem>();
        foreach (Goal row in goals)
        {
            items.Add(new GoalItem(
                row.Id, row.Title, row.Description, row.Type, row.Level, row.OwnerEmployeeId, row.OwnerEmployeeId is { } owner ? owners.GetValueOrDefault(owner) : null,
                row.DepartmentId, row.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null, row.ParentGoalId,
                row.ParentGoalId is { } parent ? parents.GetValueOrDefault(parent) : null, row.CycleId, row.StartDate, row.DueDate, row.Weight, row.Status, row.Progress,
                row.Confidence, row.LastCheckInOn, results.GetValueOrDefault(row.Id), children.GetValueOrDefault(row.Id),
                rights.CanManage(row.Level, row.OwnerEmployeeId)));
        }

        return items;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.OfType<Guid>().Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
}
