using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Project.Domain;
using Gggs.Modules.Project.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Project.Application;

public sealed record CommentBody(string Body, IReadOnlyList<Guid>? MentionedEmployeeIds);

public sealed record CommentItem(Guid Id, Guid AuthorEmployeeId, string? AuthorName, string Body, DateTime PostedAt, DateTime? EditedAt, bool CanEdit, bool CanDelete);

public sealed record ActivityItem(Guid Id, string Field, string? OldValue, string? NewValue, string? ActorName, DateTime At);

public sealed record WatcherItem(Guid EmployeeId, string? Name);

public sealed record MentionablePerson(Guid EmployeeId, string Name);

public sealed record TaskConversation(
    IReadOnlyList<CommentItem> Comments,
    IReadOnlyList<ActivityItem> Activity,
    IReadOnlyList<WatcherItem> Watchers,
    IReadOnlyList<MentionablePerson> People,
    bool IsWatching,
    bool CanComment,
    bool CanAttach);

public sealed record SavedViewBody(string Name, TaskViewFilters Filters, bool? Shared);

public sealed record SavedViewItem(Guid Id, string Name, TaskViewFilters Filters, bool IsShared, bool CanDelete);

/// <summary>Writes a task's history into the caller's unit of work, with names as they read today.</summary>
internal sealed class TaskActivityLog(AppDbContext dbContext, ProjectAccess access, IEmployeeDirectory employees, TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public void Created(ProjectTask task, string how)
        => dbContext.TaskActivities.Add(TaskActivity.Record(task, access.Me, Now, how, null, task.Code));

    public async Task ChangedAsync(TaskState before, ProjectTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(task);
        TaskState after = TaskState.Of(task);
        foreach (string field in TaskFields.Changed(before, after))
        {
            (string? oldValue, string? newValue) = field switch
            {
                TaskFields.Assignee => (await PersonAsync(before.AssigneeEmployeeId, cancellationToken), await PersonAsync(after.AssigneeEmployeeId, cancellationToken)),
                TaskFields.Column => (await ColumnAsync(before.ColumnId, cancellationToken), await ColumnAsync(after.ColumnId, cancellationToken)),
                TaskFields.Sprint => (await SprintAsync(before.SprintId, cancellationToken), await SprintAsync(after.SprintId, cancellationToken)),
                TaskFields.Type => (before.Type.ToString(), after.Type.ToString()),
                TaskFields.Title => (before.Title, after.Title),
                TaskFields.Priority => (before.Priority.ToString(), after.Priority.ToString()),
                TaskFields.StartDate => (Day(before.StartDate), Day(after.StartDate)),
                TaskFields.DueDate => (Day(before.DueDate), Day(after.DueDate)),
                TaskFields.Estimate => (before.EstimatedHours?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), after.EstimatedHours?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                TaskFields.Points => (before.StoryPoints?.ToString(System.Globalization.CultureInfo.InvariantCulture), after.StoryPoints?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                _ => (null, null),
            };
            dbContext.TaskActivities.Add(TaskActivity.Record(task, access.Me, Now, field, oldValue, newValue));
        }
    }

    private static string? Day(DateOnly? day) => day?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string?> PersonAsync(Guid? id, CancellationToken cancellationToken)
        => id is { } employee ? (await employees.GetAsync(employee, cancellationToken))?.DisplayName : null;

    private async Task<string?> ColumnAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.BoardColumns.AsNoTracking().Where(row => row.Id == id).Select(row => row.Name).FirstOrDefaultAsync(cancellationToken);

    private async Task<string?> SprintAsync(Guid? id, CancellationToken cancellationToken)
        => id is { } sprint ? await dbContext.Sprints.AsNoTracking().Where(row => row.Id == sprint).Select(row => row.Name).FirstOrDefaultAsync(cancellationToken) : null;
}

/// <summary>The conversation around a task (comments, mentions, watchers and history) and the views people save of the task list.</summary>
internal sealed class TaskCollaborationService(
    AppDbContext dbContext,
    ProjectAccess access,
    ServiceLevelService serviceLevels,
    IEmployeeDirectory employees,
    INotifier notifier,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<TaskConversation> ConversationAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        await TaskAsync(projectId, taskId, track: false, cancellationToken);

        List<TaskComment> comments = await dbContext.TaskComments.AsNoTracking().Where(row => row.TaskId == taskId).OrderBy(row => row.PostedAt).ToListAsync(cancellationToken);
        List<TaskActivity> activity = await dbContext.TaskActivities.AsNoTracking().Where(row => row.TaskId == taskId).OrderByDescending(row => row.At).Take(200).ToListAsync(cancellationToken);
        List<Guid> watchers = await dbContext.TaskWatchers.AsNoTracking().Where(row => row.TaskId == taskId).Select(row => row.EmployeeId).ToListAsync(cancellationToken);

        Dictionary<Guid, string> names = (await employees.GetManyAsync(
                comments.Select(row => row.AuthorEmployeeId).Concat(activity.Select(row => row.ActorEmployeeId).OfType<Guid>()).Concat(watchers).Distinct(), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        bool canManage = await access.CanAsync(projectId, Permissions.Project.Manage, cancellationToken);
        bool canTake = await CanTakePartAsync(projectId, cancellationToken);
        (HashSet<Guid> onProject, _) = await PeopleAsync(projectId, cancellationToken);
        IReadOnlyList<EmployeeSummary> mentionable = await employees.GetManyAsync(onProject, cancellationToken);

        return new TaskConversation(
            [.. comments.Select(row => new CommentItem(
                row.Id, row.AuthorEmployeeId, names.GetValueOrDefault(row.AuthorEmployeeId), row.Body, row.PostedAt, row.EditedAt,
                row.AuthorEmployeeId == access.Me, row.AuthorEmployeeId == access.Me || canManage))],
            [.. activity.Select(row => new ActivityItem(row.Id, row.Field, row.OldValue, row.NewValue, row.ActorEmployeeId is { } actor ? names.GetValueOrDefault(actor) : null, row.At))],
            [.. watchers.Select(id => new WatcherItem(id, names.GetValueOrDefault(id)))],
            [.. mentionable.Select(person => new MentionablePerson(person.Id, person.DisplayName)).OrderBy(person => person.Name, StringComparer.CurrentCulture)],
            access.Me is { } me && watchers.Contains(me),
            access.Me is not null && canTake,
            canTake);
    }

    public async Task<TaskConversation> AddCommentAsync(Guid projectId, Guid taskId, CommentBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureCanTakePartAsync(projectId, cancellationToken);
        Guid me = access.RequireEmployee();
        ProjectTask task = await TaskAsync(projectId, taskId, track: false, cancellationToken);

        dbContext.TaskComments.Add(TaskComment.Post(projectId, taskId, me, body.Body, Now));
        await serviceLevels.OnCommentAsync(task, me, cancellationToken);
        if (!await dbContext.TaskWatchers.AnyAsync(row => row.TaskId == taskId && row.EmployeeId == me, cancellationToken))
        {
            dbContext.TaskWatchers.Add(TaskWatcher.For(projectId, taskId, me));
        }

        await NotifyAsync(task, me, body.MentionedEmployeeIds ?? [], cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ConversationAsync(projectId, taskId, cancellationToken);
    }

    public async Task<TaskConversation> EditCommentAsync(Guid projectId, Guid taskId, Guid commentId, CommentBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        TaskComment comment = await CommentAsync(taskId, commentId, cancellationToken);
        if (comment.AuthorEmployeeId != access.Me)
        {
            throw new DomainRuleException("TASK_COMMENT_AUTHOR", "Only whoever wrote a comment can edit it.");
        }

        comment.Edit(body.Body, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ConversationAsync(projectId, taskId, cancellationToken);
    }

    public async Task<TaskConversation> DeleteCommentAsync(Guid projectId, Guid taskId, Guid commentId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        TaskComment comment = await CommentAsync(taskId, commentId, cancellationToken);
        if (comment.AuthorEmployeeId != access.Me && !await access.CanAsync(projectId, Permissions.Project.Manage, cancellationToken))
        {
            throw new DomainRuleException("TASK_COMMENT_AUTHOR", "Only whoever wrote a comment, or a project manager, can delete it.");
        }

        dbContext.TaskComments.Remove(comment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ConversationAsync(projectId, taskId, cancellationToken);
    }

    public async Task<TaskConversation> WatchAsync(Guid projectId, Guid taskId, bool on, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        Guid me = access.RequireEmployee();
        await TaskAsync(projectId, taskId, track: false, cancellationToken);
        TaskWatcher? existing = await dbContext.TaskWatchers.FirstOrDefaultAsync(row => row.TaskId == taskId && row.EmployeeId == me, cancellationToken);
        if (on && existing is null)
        {
            dbContext.TaskWatchers.Add(TaskWatcher.For(projectId, taskId, me));
        }
        else if (!on && existing is not null)
        {
            dbContext.TaskWatchers.Remove(existing);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ConversationAsync(projectId, taskId, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedViewItem>> ViewsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        Guid? me = access.Me;
        List<TaskSavedView> views = await dbContext.TaskSavedViews.AsNoTracking()
            .Where(row => row.ProjectId == projectId && (row.OwnerEmployeeId == null || row.OwnerEmployeeId == me))
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken);
        bool canManageShared = await access.CanAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        return [.. views.Select(view => new SavedViewItem(view.Id, view.Name, view.Filters, view.IsShared, !view.IsShared || canManageShared))];
    }

    public async Task<IReadOnlyList<SavedViewItem>> SaveViewAsync(Guid projectId, SavedViewBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await access.EnsureAsync(projectId, body.Shared == true ? Permissions.Project.TaskManage : Permissions.Project.TaskView, cancellationToken);
        Guid? owner = body.Shared == true ? null : access.RequireEmployee();
        dbContext.TaskSavedViews.Add(TaskSavedView.Save(projectId, owner, body.Name, body.Filters ?? new TaskViewFilters()));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewsAsync(projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedViewItem>> DeleteViewAsync(Guid projectId, Guid viewId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        Guid? me = access.Me;
        TaskSavedView view = await dbContext.TaskSavedViews.FirstOrDefaultAsync(row => row.Id == viewId && row.ProjectId == projectId && (row.OwnerEmployeeId == null || row.OwnerEmployeeId == me), cancellationToken)
            ?? throw new NotFoundException(nameof(TaskSavedView), viewId);
        if (view.IsShared)
        {
            await access.EnsureAsync(projectId, Permissions.Project.TaskManage, cancellationToken);
        }

        dbContext.TaskSavedViews.Remove(view);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewsAsync(projectId, cancellationToken);
    }

    /// <summary>Whoever can change tasks, or is on the project, can talk about them and attach to them.</summary>
    internal async Task<bool> CanTakePartAsync(Guid projectId, CancellationToken cancellationToken)
        => await access.CanAsync(projectId, Permissions.Project.TaskManage, cancellationToken)
            || (await access.CanAsync(projectId, Permissions.Project.TaskView, cancellationToken) && await access.IsOnProjectAsync(projectId, cancellationToken));

    private async Task EnsureCanTakePartAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(projectId, Permissions.Project.TaskView, cancellationToken);
        if (!await CanTakePartAsync(projectId, cancellationToken))
        {
            throw new DomainRuleException("TASK_COMMENT_ACCESS", "Only people on the project can comment on its tasks.");
        }
    }

    /// <summary>
    /// Whoever is named hears they were; watchers, the assignee and the reporter hear there is something new. The
    /// comment itself stays out of the message, so what is said on a project stays behind its sign-in.
    /// </summary>
    private async Task NotifyAsync(ProjectTask task, Guid author, IReadOnlyList<Guid> mentioned, CancellationToken cancellationToken)
    {
        Guid projectId = task.ProjectId;
        (HashSet<Guid> people, string projectName) = await PeopleAsync(projectId, cancellationToken);

        // Naming someone outside the project does not reach them: they could not open the link anyway.
        List<Guid> named = [.. mentioned.Distinct().Where(id => id != author && people.Contains(id))];
        List<Guid> watching = await dbContext.TaskWatchers.AsNoTracking().Where(row => row.TaskId == task.Id).Select(row => row.EmployeeId).ToListAsync(cancellationToken);
        List<Guid> others = [.. watching.Concat(new[] { task.AssigneeEmployeeId, task.ReporterEmployeeId }.OfType<Guid>()).Distinct().Where(id => id != author && !named.Contains(id))];
        string authorName = (await employees.GetAsync(author, cancellationToken))?.DisplayName ?? "Someone";
        var values = new Dictionary<string, string> { ["taskCode"] = task.Code, ["taskTitle"] = task.Title, ["projectName"] = projectName, ["authorName"] = authorName };
        string link = $"/projects/{projectId}?tab=tasks&task={task.Id}";

        if (named.Count > 0)
        {
            await notifier.NotifyAsync(new NotificationRequest(NotificationTemplates.ProjectTaskMentioned, [.. named.Select(id => new NotificationRecipient(id))], values, link), cancellationToken);
        }

        if (others.Count > 0)
        {
            await notifier.NotifyAsync(new NotificationRequest(NotificationTemplates.ProjectTaskCommented, [.. others.Select(id => new NotificationRecipient(id))], values, link), cancellationToken);
        }
    }

    /// <summary>The team today and the project's managers: who can be named, and who could open the link.</summary>
    private async Task<(HashSet<Guid> People, string ProjectName)> PeopleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        HashSet<Guid> people = [.. await dbContext.ProjectTeamMembers.AsNoTracking()
            .Where(row => row.ProjectId == projectId && row.Status == TeamMemberStatus.Active)
            .Select(row => row.EmployeeId)
            .ToListAsync(cancellationToken)];
        var project = await dbContext.Projects.AsNoTracking()
            .Where(row => row.Id == projectId)
            .Select(row => new { row.Name, row.ProjectManagerEmployeeId, row.DeliveryManagerEmployeeId, row.AccountManagerEmployeeId })
            .FirstAsync(cancellationToken);
        people.UnionWith(new[] { project.ProjectManagerEmployeeId, project.DeliveryManagerEmployeeId, project.AccountManagerEmployeeId }.OfType<Guid>());
        return (people, project.Name);
    }

    private async Task<ProjectTask> TaskAsync(Guid projectId, Guid taskId, bool track, CancellationToken cancellationToken)
    {
        IQueryable<ProjectTask> tasks = track ? dbContext.ProjectTasks : dbContext.ProjectTasks.AsNoTracking();
        return await tasks.FirstOrDefaultAsync(row => row.Id == taskId && row.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException("Task", taskId);
    }

    private async Task<TaskComment> CommentAsync(Guid taskId, Guid commentId, CancellationToken cancellationToken)
        => await dbContext.TaskComments.FirstOrDefaultAsync(row => row.Id == commentId && row.TaskId == taskId, cancellationToken)
            ?? throw new NotFoundException(nameof(TaskComment), commentId);
}

/// <summary>Files on a task live in Documents, as files on a project do, under the task's own rules.</summary>
internal sealed class ProjectTaskDocumentAccess(AppDbContext dbContext, ProjectAccess access, TaskCollaborationService collaboration) : Gggs.Modules.Documents.Contracts.IDocumentOwnerAccess
{
    public string OwnerType => "ProjectTask";

    public async Task<bool> CanReadAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => await ProjectOfAsync(ownerId, cancellationToken) is { } project && await access.CanAsync(project, Permissions.Project.TaskView, cancellationToken);

    public async Task<bool> CanWriteAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => await ProjectOfAsync(ownerId, cancellationToken) is { } project && await collaboration.CanTakePartAsync(project, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. ownerIds.Distinct()];
        return await dbContext.ProjectTasks.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Code, cancellationToken);
    }

    private async Task<Guid?> ProjectOfAsync(Guid taskId, CancellationToken cancellationToken)
        => await dbContext.ProjectTasks.AsNoTracking().Where(row => row.Id == taskId).Select(row => (Guid?)row.ProjectId).FirstOrDefaultAsync(cancellationToken);
}
