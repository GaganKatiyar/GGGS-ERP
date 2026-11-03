using System.Globalization;
using System.Text;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Client.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

public sealed record SlaColumn(Guid Id, string Name);

public sealed record SlaTicketItem(
    Guid TaskId,
    string Code,
    string Title,
    TaskType Type,
    WorkPriority Priority,
    string? ColumnName,
    Guid? AssigneeEmployeeId,
    string? AssigneeName,
    DateTime OpenedAt,
    DateTime ResponseDueAt,
    DateTime ResolutionDueAt,
    DateTime? RespondedAt,
    DateTime? ResolvedAt,
    bool IsPaused,
    bool ResponseBreached,
    bool ResolutionBreached,
    string? ExcludedReason);

public sealed record ServiceLevelView(
    Guid ProjectId,
    SlaPolicyDetails? Policy,
    SlaPolicyDetails Defaults,
    IReadOnlyList<SlaColumn> Columns,
    IReadOnlyList<SlaTicketItem> Tickets,
    SlaMonthReport Report,
    bool CanManage,
    bool CanManageTickets,
    bool CanManageCredits);

public sealed record ExcludeBody(string Reason);

public sealed record ServiceLevelQuery
{
    public DateOnly Month { get; init; }

    public Guid? ClientId { get; init; }
}

public sealed record ServiceLevelFile(byte[] Content, string FileName);

/// <summary>
/// Service levels on a project's tickets: a clock on each incident and service request, breaches swept up
/// half-hourly as the Helpdesk does for HR, credits for the invoice, and the month's report for the client.
/// </summary>
internal sealed class ServiceLevelService(
    AppDbContext dbContext,
    ProjectAccess access,
    IEmployeeDirectory employees,
    IClientDirectory clients,
    INotifier notifier,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<ServiceLevelView> ViewAsync(Guid projectId, DateOnly month, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        ProjectSlaPolicy? policy = await dbContext.ProjectSlaPolicies.AsNoTracking().FirstOrDefaultAsync(row => row.ProjectId == projectId, cancellationToken);
        List<SlaColumn> columns = await dbContext.BoardColumns.AsNoTracking()
            .Where(row => row.ProjectId == projectId)
            .OrderBy(row => row.SortOrder)
            .Select(row => new SlaColumn(row.Id, row.Name))
            .ToListAsync(cancellationToken);
        List<TicketSla> rows = await dbContext.TicketSlas.AsNoTracking().Where(row => row.ProjectId == projectId).ToListAsync(cancellationToken);
        TimeZoneInfo zone = await clock.GetTimeZoneAsync(cancellationToken);

        return new ServiceLevelView(
            projectId,
            policy?.Details,
            SlaDefaults.Details,
            columns,
            await TicketsAsync(rows, month, zone, columns, cancellationToken),
            SlaReporting.Month(rows, month, zone, policy?.MonthlyCreditCapPercent ?? 100),
            await access.CanAsync(projectId, Permissions.Project.Manage, cancellationToken),
            await access.CanAsync(projectId, Permissions.Project.TaskManage, cancellationToken),
            await access.CanAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken));
    }

    public async Task<ServiceLevelView> SavePolicyAsync(Guid projectId, SlaPolicyDetails body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
        List<Guid> columnIds = await dbContext.BoardColumns.Where(row => row.ProjectId == projectId).Select(row => row.Id).ToListAsync(cancellationToken);
        if ((body.PausedColumnIds ?? []).Any(id => !columnIds.Contains(id)))
        {
            throw new DomainRuleException("SLA_COLUMN", "One of those columns is not on this project's board.");
        }

        ProjectSlaPolicy? policy = await dbContext.ProjectSlaPolicies.FirstOrDefaultAsync(row => row.ProjectId == projectId, cancellationToken);
        if (policy is null)
        {
            dbContext.ProjectSlaPolicies.Add(ProjectSlaPolicy.For(projectId, body));
        }
        else
        {
            policy.Update(body);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(projectId, await clock.TodayAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Ends the policy, once nothing is running on it. Breaches already recorded still count.</summary>
    public async Task DeletePolicyAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.Manage, cancellationToken);
        ProjectSlaPolicy policy = await dbContext.ProjectSlaPolicies.FirstOrDefaultAsync(row => row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(ProjectSlaPolicy), projectId);
        if (await dbContext.TicketSlas.AnyAsync(row => row.ProjectId == projectId && row.ResolvedAt == null, cancellationToken))
        {
            throw new DomainRuleException("SLA_POLICY_IN_USE", "Open tickets are running on this policy. Resolve them first.");
        }

        dbContext.ProjectSlaPolicies.Remove(policy);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Brings a ticket's clock in line with its task, after any change to the task. Call before saving.
    /// Being picked up from the queue counts as the first response.
    /// </summary>
    public async Task SyncAsync(ProjectTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        TicketSla? sla = await dbContext.TicketSlas.FirstOrDefaultAsync(row => row.TaskId == task.Id, cancellationToken);
        if (!ServiceTickets.IsTicket(task.Type))
        {
            if (sla is not null)
            {
                EnsureRemovable(sla, task.Code);
                dbContext.TicketSlas.Remove(sla);
            }

            return;
        }

        ProjectSlaPolicy? policy = await dbContext.ProjectSlaPolicies.AsNoTracking().FirstOrDefaultAsync(row => row.ProjectId == task.ProjectId, cancellationToken);
        if (policy is null)
        {
            return;
        }

        SlaCalendar calendar = policy.Calendar;
        TimeZoneInfo zone = await clock.GetTimeZoneAsync(cancellationToken);
        SlaTarget? target = policy.Targets.FirstOrDefault(row => row.Priority == task.Priority);
        if (sla is null)
        {
            if (target is null)
            {
                return;
            }

            sla = TicketSla.Open(task.ProjectId, task.Id, target, Now, calendar, zone);
            dbContext.TicketSlas.Add(sla);
        }
        else if (sla.Priority != task.Priority && target is not null)
        {
            sla.Retarget(target, calendar, zone);
        }

        BoardColumn column = await dbContext.BoardColumns.FirstAsync(row => row.Id == task.ColumnId, cancellationToken);
        if (column.Category == ColumnCategory.Done)
        {
            sla.Resolve(Now, calendar, zone);
            return;
        }

        sla.Reopen(Now, calendar, zone);
        if (column.Category == ColumnCategory.InProgress)
        {
            sla.Respond(Now);
        }

        if (policy.PausedColumnIds.Contains(column.Id))
        {
            sla.Pause(Now);
        }
        else
        {
            sla.Resume(Now, calendar, zone);
        }
    }

    /// <summary>A breach is on the record: the ticket cannot be deleted or retyped out of the report while it counts.</summary>
    public async Task EnsureCanRemoveAsync(ProjectTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        TicketSla? sla = await dbContext.TicketSlas.FirstOrDefaultAsync(row => row.TaskId == task.Id, cancellationToken);
        if (sla is not null)
        {
            EnsureRemovable(sla, task.Code);
            dbContext.TicketSlas.Remove(sla);
        }
    }

    public async Task<ServiceLevelView> RespondAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        TicketSla sla = await TicketAsync(projectId, taskId, cancellationToken);
        sla.Respond(Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(projectId, await clock.TodayAsync(cancellationToken), cancellationToken);
    }

    public async Task<ServiceLevelView> ExcludeAsync(Guid projectId, Guid taskId, ExcludeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        TicketSla sla = await TicketAsync(projectId, taskId, cancellationToken);
        sla.Exclude(body.Reason, access.Me);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(projectId, await clock.TodayAsync(cancellationToken), cancellationToken);
    }

    public async Task<ServiceLevelView> ReinstateAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.FinancialsManage, cancellationToken);
        TicketSla sla = await TicketAsync(projectId, taskId, cancellationToken);
        sla.Reinstate();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(projectId, await clock.TodayAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Every project on a policy that the caller can see, for one month and optionally one client.</summary>
    public async Task<IReadOnlyList<ServiceLevelRow>> ReportAsync(ServiceLevelQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Domain.Project> visible = await access.ApplyAsync(dbContext.Projects.AsNoTracking(), Permissions.Project.TaskView, cancellationToken);
        if (query.ClientId is { } clientId)
        {
            visible = visible.Where(row => row.ClientId == clientId);
        }

        IQueryable<ProjectSlaPolicy> policies = dbContext.ProjectSlaPolicies.AsNoTracking();
        var projects = await visible
            .Join(policies, project => project.Id, policy => policy.ProjectId, (project, policy) => new { project.Id, project.Code, project.Name, project.ClientId, policy.MonthlyCreditCapPercent })
            .ToListAsync(cancellationToken);
        List<Guid> ids = [.. projects.Select(row => row.Id)];
        ILookup<Guid, TicketSla> tickets = (await dbContext.TicketSlas.AsNoTracking().Where(row => ids.Contains(row.ProjectId)).ToListAsync(cancellationToken)).ToLookup(row => row.ProjectId);
        IReadOnlyDictionary<Guid, ClientSummary> names = await clients.GetManyAsync(projects.Select(row => row.ClientId).OfType<Guid>().Distinct(), cancellationToken);
        TimeZoneInfo zone = await clock.GetTimeZoneAsync(cancellationToken);

        return [.. projects
            .Select(row => new ServiceLevelRow(
                row.Id, row.Code, row.Name, row.ClientId, row.ClientId is { } client ? names.GetValueOrDefault(client)?.Name : null,
                SlaReporting.Month([.. tickets[row.Id]], query.Month, zone, row.MonthlyCreditCapPercent)))
            .OrderBy(row => row.ClientName ?? "￿", StringComparer.CurrentCulture)
            .ThenBy(row => row.Code, StringComparer.Ordinal)];
    }

    public async Task<ServiceLevelFile> ExportAsync(ServiceLevelQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceLevelRow> rows = await ReportAsync(query, cancellationToken);
        return new ServiceLevelFile(Encoding.UTF8.GetBytes(ServiceLevelCsv.Write(rows)), $"service-levels-{query.Month:yyyy-MM}.csv");
    }

    /// <summary>What each project's breaches cost, month by month, for billing and revenue.</summary>
    public async Task<IReadOnlyList<ServiceCreditMonth>> CreditsAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. projectIds.Distinct()];
        List<TicketSla> breached = await dbContext.TicketSlas.AsNoTracking()
            .Where(row => ids.Contains(row.ProjectId) && (row.ResponseBreachedAt != null || row.ResolutionBreachedAt != null) && row.ExcludedReason == null)
            .ToListAsync(cancellationToken);
        if (breached.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, decimal> caps = await dbContext.ProjectSlaPolicies.AsNoTracking()
            .Where(row => ids.Contains(row.ProjectId))
            .ToDictionaryAsync(row => row.ProjectId, row => row.MonthlyCreditCapPercent, cancellationToken);
        TimeZoneInfo zone = await clock.GetTimeZoneAsync(cancellationToken);
        return [.. breached.GroupBy(row => row.ProjectId).SelectMany(group =>
            SlaReporting.Credits(group, zone, caps.GetValueOrDefault(group.Key, 100)).Select(month => new ServiceCreditMonth(group.Key, month.Month, month.Percent, month.Breaches)))];
    }

    /// <summary>Records every target missed by now, and tells whoever holds the ticket and the project's manager.</summary>
    public async Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken)
    {
        List<TicketSla> due = await dbContext.TicketSlas
            .Where(row => (row.ResponseBreachedAt == null && row.RespondedAt == null && row.ResponseDueAt < now)
                || (row.ResolutionBreachedAt == null && row.ResolvedAt == null && row.PausedSince == null && row.ResolutionDueAt < now))
            .ToListAsync(cancellationToken);
        List<(TicketSla Ticket, string Kind)> breaches = [];
        foreach (TicketSla ticket in due)
        {
            (bool response, bool resolution) = ticket.CheckBreach(now);
            if (response)
            {
                breaches.Add((ticket, "response"));
            }

            if (resolution)
            {
                breaches.Add((ticket, "resolution"));
            }
        }

        if (breaches.Count == 0)
        {
            return 0;
        }

        List<Guid> taskIds = [.. breaches.Select(row => row.Ticket.TaskId).Distinct()];
        Dictionary<Guid, ProjectTask> tasks = await dbContext.ProjectTasks.AsNoTracking().Where(row => taskIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        List<Guid> projectIds = [.. breaches.Select(row => row.Ticket.ProjectId).Distinct()];
        var projects = await dbContext.Projects.AsNoTracking()
            .Where(row => projectIds.Contains(row.Id))
            .Select(row => new { row.Id, row.ProjectManagerEmployeeId })
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        foreach ((TicketSla ticket, string kind) in breaches)
        {
            if (!tasks.TryGetValue(ticket.TaskId, out ProjectTask? task))
            {
                continue;
            }

            NotificationRecipient[] recipients = [.. new[] { task.AssigneeEmployeeId, projects.GetValueOrDefault(ticket.ProjectId)?.ProjectManagerEmployeeId }.OfType<Guid>().Distinct().Select(id => new NotificationRecipient(id))];
            if (recipients.Length > 0)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(
                        NotificationTemplates.ProjectSlaBreached,
                        recipients,
                        new Dictionary<string, string>
                        {
                            ["taskCode"] = task.Code,
                            ["taskTitle"] = task.Title,
                            ["kind"] = kind,
                            ["dueAt"] = (kind == "response" ? ticket.ResponseDueAt : ticket.ResolutionDueAt).ToString("d MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture),
                        },
                        $"/projects/{ticket.ProjectId}?tab=service"),
                    cancellationToken);
            }
        }

        // Last, because notifications commit with the caller's save.
        await dbContext.SaveChangesAsync(cancellationToken);
        return breaches.Count;
    }

    private static void EnsureRemovable(TicketSla sla, string code)
    {
        if (sla.CountsAgainst)
        {
            throw new DomainRuleException("SLA_BREACH_ON_RECORD", $"{code} has an SLA breach on record. Exclude the breach first, with a reason.");
        }
    }

    private async Task<TicketSla> TicketAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken)
        => await dbContext.TicketSlas.FirstOrDefaultAsync(row => row.ProjectId == projectId && row.TaskId == taskId, cancellationToken)
            ?? throw new NotFoundException(nameof(TicketSla), taskId);

    /// <summary>What is open now, and everything that resolved or went wrong in the month.</summary>
    private async Task<IReadOnlyList<SlaTicketItem>> TicketsAsync(IReadOnlyList<TicketSla> rows, DateOnly month, TimeZoneInfo zone, IReadOnlyList<SlaColumn> columns, CancellationToken cancellationToken)
    {
        bool In(DateTime? utc)
        {
            if (utc is not { } value)
            {
                return false;
            }

            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(value, zone);
            return local.Year == month.Year && local.Month == month.Month;
        }

        List<TicketSla> shown = [.. rows.Where(row => row.ResolvedAt is null || In(row.ResolvedAt) || In(row.FirstBreachAt)).OrderBy(row => row.ResolvedAt is not null).ThenBy(row => row.ResolutionDueAt).Take(200)];
        List<Guid> taskIds = [.. shown.Select(row => row.TaskId)];
        Dictionary<Guid, ProjectTask> tasks = await dbContext.ProjectTasks.AsNoTracking().Where(row => taskIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        Dictionary<Guid, string> people = (await employees.GetManyAsync(tasks.Values.Select(task => task.AssigneeEmployeeId).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        Dictionary<Guid, string> columnNames = columns.ToDictionary(column => column.Id, column => column.Name);

        return [.. shown.Where(row => tasks.ContainsKey(row.TaskId)).Select(row =>
        {
            ProjectTask task = tasks[row.TaskId];
            return new SlaTicketItem(
                task.Id, task.Code, task.Title, task.Type, row.Priority, columnNames.GetValueOrDefault(task.ColumnId),
                task.AssigneeEmployeeId, task.AssigneeEmployeeId is { } assignee ? people.GetValueOrDefault(assignee) : null,
                row.OpenedAt, row.ResponseDueAt, row.ResolutionDueAt, row.RespondedAt, row.ResolvedAt, row.PausedSince is not null,
                row.ResponseBreachedAt is not null, row.ResolutionBreachedAt is not null, row.ExcludedReason);
        })];
    }
}

/// <summary>Fans the half-hourly SLA sweep out to every tenant.</summary>
[PlatformJob("Project SLA sweep: fans out one breach check per active tenant.")]
internal sealed class ProjectSlaSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "project-sla-sweep";

    public Task RunAsync() => fanOut.FanOutAsync<ProjectSlaJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class ProjectSlaJob(ServiceLevelService levels, TimeProvider timeProvider, ILogger<ProjectSlaJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int breached = await levels.SweepAsync(timeProvider.GetUtcNow().UtcDateTime, CancellationToken.None);
        if (breached > 0)
        {
            logger.LogInformation("Project SLA sweep for tenant {TenantId}: {Breached} breach(es).", tenantId, breached);
        }
    }
}
