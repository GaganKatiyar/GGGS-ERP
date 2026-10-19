using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Performance.Domain;
using Gggs.Modules.Performance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Performance.Application;

public sealed record PipNoteItem(DateOnly On, string? ByName, string Text);

public sealed record PipItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    Guid? ManagerEmployeeId,
    string? ManagerName,
    Guid? ReviewId,
    string Reason,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<PipObjective> Objectives,
    IReadOnlyList<PipNoteItem> Notes,
    PipStatus Status,
    bool Extended,
    string? Outcome,
    DateOnly? ClosedOn,
    int DaysLeft,
    bool CanManage);

public sealed record PipBody(Guid EmployeeId, PipDetails Details);

public sealed record PipNoteBody(string Text);

public sealed record PipExtendBody(DateOnly EndDate);

public sealed record PipCloseBody(bool Succeeded, string Outcome, IReadOnlyList<PipObjective>? Objectives);

/// <summary>Performance improvement plans: drafted by the manager or HR, run to an end date with check-ins, and closed with an outcome.</summary>
internal sealed class PipService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    INotifier notifier,
    ITenantClock clock)
{
    public async Task<IReadOnlyList<PipItem>> PlansAsync(PipStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<ImprovementPlan> rows = await VisibleAsync(cancellationToken);
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.StartDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<PipItem>> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = currentUser.EmployeeId ?? throw new DomainRuleException("PIP_NO_EMPLOYEE", "Your account is not linked to an employee record.");
        List<ImprovementPlan> rows = await dbContext.ImprovementPlans.AsNoTracking().Where(row => row.EmployeeId == me && row.Status != PipStatus.Draft).OrderByDescending(row => row.StartDate).ToListAsync(cancellationToken);
        return await ItemsAsync(rows, cancellationToken);
    }

    public async Task<PipItem> PlanAsync(Guid id, CancellationToken cancellationToken)
        => (await ItemsAsync([await (await VisibleAsync(cancellationToken)).FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ImprovementPlan), id)], cancellationToken))[0];

    public async Task<PipItem> CreateAsync(PipBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        EmployeeSummary person = await employees.GetAsync(body.EmployeeId, cancellationToken) ?? throw new NotFoundException("Employee", body.EmployeeId);
        await EnsureManagesAsync(person.Id, cancellationToken);
        if (await dbContext.ImprovementPlans.AnyAsync(row => row.EmployeeId == person.Id && (row.Status == PipStatus.Draft || row.Status == PipStatus.Active), cancellationToken))
        {
            throw new DomainRuleException("PIP_EXISTS", $"{person.DisplayName} already has an open improvement plan.");
        }

        var plan = ImprovementPlan.Draft(person.Id, person.ManagerEmployeeId, body.Details);
        dbContext.ImprovementPlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(plan.Id, cancellationToken);
    }

    public async Task<PipItem> UpdateAsync(Guid id, PipDetails details, CancellationToken cancellationToken)
    {
        ImprovementPlan plan = await ManagedAsync(id, cancellationToken);
        plan.Update(details);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(id, cancellationToken);
    }

    public async Task<PipItem> StartAsync(Guid id, CancellationToken cancellationToken)
    {
        ImprovementPlan plan = await ManagedAsync(id, cancellationToken);
        plan.Start();
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.PipStarted,
                [new NotificationRecipient(plan.EmployeeId)],
                new Dictionary<string, string> { ["startDate"] = Format(plan.StartDate), ["endDate"] = Format(plan.EndDate) },
                "/me/reviews"),
            cancellationToken);
        return await PlanAsync(id, cancellationToken);
    }

    public async Task<PipItem> NoteAsync(Guid id, string text, CancellationToken cancellationToken)
    {
        ImprovementPlan plan = await ManagedAsync(id, cancellationToken);
        plan.AddNote(await clock.TodayAsync(cancellationToken), currentUser.EmployeeId, text);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(id, cancellationToken);
    }

    public async Task<PipItem> ExtendAsync(Guid id, DateOnly endDate, CancellationToken cancellationToken)
    {
        ImprovementPlan plan = await ManagedAsync(id, cancellationToken);
        plan.Extend(endDate);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(id, cancellationToken);
    }

    public async Task<PipItem> CloseAsync(Guid id, PipCloseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImprovementPlan plan = await ManagedAsync(id, cancellationToken);
        plan.Close(body.Succeeded, body.Outcome, body.Objectives, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(id, cancellationToken);
    }

    public async Task<PipItem> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        ImprovementPlan plan = await ManagedAsync(id, cancellationToken);
        plan.Cancel(await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await PlanAsync(id, cancellationToken);
    }

    private static string Format(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Plans of people in PIP scope, and those one is the manager on.</summary>
    private async Task<IQueryable<ImprovementPlan>> VisibleAsync(CancellationToken cancellationToken)
    {
        IQueryable<ImprovementPlan> rows = dbContext.ImprovementPlans.AsNoTracking();
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        IReadOnlyCollection<Guid>? scope = granted.Has(Permissions.Performance.PipView) ? await employees.InScopeIdsAsync(Permissions.Performance.PipView, cancellationToken) : [];
        if (scope is null)
        {
            return rows;
        }

        Guid? me = currentUser.EmployeeId;
        return rows.Where(row => row.ManagerEmployeeId == me || scope.Contains(row.EmployeeId));
    }

    private async Task<ImprovementPlan> ManagedAsync(Guid id, CancellationToken cancellationToken)
    {
        ImprovementPlan plan = await dbContext.ImprovementPlans.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ImprovementPlan), id);
        await EnsureManagesAsync(plan.EmployeeId, cancellationToken);
        return plan;
    }

    private async Task EnsureManagesAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (employeeId == currentUser.EmployeeId || !(await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Performance.PipManage)
            || !await employees.IsInScopeAsync(employeeId, Permissions.Performance.PipManage, cancellationToken))
        {
            throw new ForbiddenException("PIP_FORBIDDEN", "Improvement plans are run by the employee's manager or HR.");
        }
    }

    private async Task<List<PipItem>> ItemsAsync(IReadOnlyList<ImprovementPlan> plans, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(
                plans.SelectMany(row => new[] { (Guid?)row.EmployeeId, row.ManagerEmployeeId }.Concat(row.Notes.Select(note => note.ByEmployeeId))).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        IReadOnlyCollection<Guid>? scope = granted.Has(Permissions.Performance.PipManage) ? await employees.InScopeIdsAsync(Permissions.Performance.PipManage, cancellationToken) : [];
        return [.. plans.Select(row => new PipItem(
            row.Id, row.EmployeeId, people.GetValueOrDefault(row.EmployeeId)?.DisplayName, people.GetValueOrDefault(row.EmployeeId)?.EmployeeCode, row.ManagerEmployeeId,
            row.ManagerEmployeeId is { } manager ? people.GetValueOrDefault(manager)?.DisplayName : null, row.ReviewId, row.Reason, row.StartDate, row.EndDate, row.Objectives,
            [.. row.Notes.Select(note => new PipNoteItem(note.On, note.ByEmployeeId is { } by ? people.GetValueOrDefault(by)?.DisplayName : null, note.Text))],
            row.Status, row.Extended, row.Outcome, row.ClosedOn, Math.Max(0, row.EndDate.DayNumber - today.DayNumber),
            row.EmployeeId != currentUser.EmployeeId && granted.Has(Permissions.Performance.PipManage) && (scope is null || scope.Contains(row.EmployeeId))))];
    }
}
