using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workflow.Domain;
using Gggs.Modules.Workflow.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Workflow.Application;

/// <summary>
/// Runs requests through their stages (spec §109): resolves approvers, follows delegations, takes
/// decisions, and moves a request on, back, or to its end.
/// <para>
/// Nothing here saves on behalf of <see cref="IWorkflowStarter.StartAsync"/>: a module starting an
/// approval does so inside its own unit of work, so its record and the request are saved together
/// or not at all. The decision paths save, because the inbox endpoints are their own unit of work.
/// </para>
/// </summary>
internal sealed class WorkflowEngine(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure orgStructure,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    IEnumerable<WorkflowSubject> subjects,
    TimeProvider timeProvider) : IWorkflowStarter
{
    /// <summary>
    /// History written in one pass shares a clock reading; a tick apart per entry (datetime2 keeps
    /// 100 ns) keeps "approved" before "completed" when the history is read back in time order.
    /// </summary>
    private int _historyOrder;

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<WorkflowStartResult> StartAsync(WorkflowStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string subjectType = request.SubjectType.Trim().ToLowerInvariant();
        if (!subjects.Any(subject => string.Equals(subject.Code, subjectType, StringComparison.Ordinal)))
        {
            // A programming error, not a user's: the owning module forgot to register its subject.
            throw new InvalidOperationException($"Workflow subject '{subjectType}' is not registered.");
        }

        if (await dbContext.WorkflowInstances.AnyAsync(
                instance => instance.SubjectType == subjectType
                    && instance.SubjectId == request.SubjectId
                    && (instance.Status == WorkflowInstanceStatus.Pending || instance.Status == WorkflowInstanceStatus.SentBack),
                cancellationToken))
        {
            throw new DomainRuleException("WORKFLOW_ALREADY_OPEN", "This record already has a request awaiting approval.");
        }

        WorkflowDefinition? definition = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.SubjectType == subjectType && candidate.IsActive, cancellationToken);

        if (definition is null)
        {
            return new WorkflowStartResult(WorkflowStartOutcome.AutoApproved, null);
        }

        WorkflowInstance instance = WorkflowInstance.Start(
            definition, subjectType, request.SubjectId, request.Title, request.Link,
            request.RequesterEmployeeId, currentUser.UserId, request.Fields, Now);

        dbContext.WorkflowInstances.Add(instance);
        Log(instance, WorkflowActionKind.Submitted);

        bool pending = await ActivateFromAsync(instance, 0, cancellationToken);
        if (!pending)
        {
            // Every stage's condition left this request out: approved without anyone being asked.
            return new WorkflowStartResult(WorkflowStartOutcome.AutoApproved, instance.Id);
        }

        return new WorkflowStartResult(WorkflowStartOutcome.Pending, instance.Id);
    }

    public async Task WithdrawAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken = default)
    {
        string normalized = subjectType.Trim().ToLowerInvariant();
        WorkflowInstance? instance = await dbContext.WorkflowInstances.FirstOrDefaultAsync(
            candidate => candidate.SubjectType == normalized
                && candidate.SubjectId == subjectId
                && (candidate.Status == WorkflowInstanceStatus.Pending || candidate.Status == WorkflowInstanceStatus.SentBack),
            cancellationToken);

        if (instance is not null)
        {
            await CloseAsWithdrawnAsync(instance, comment: null, cancellationToken);
        }
    }

    /// <summary>An approver's decision on their task.</summary>
    public async Task DecideAsync(Guid taskId, WorkflowTaskStatus decision, string? comment, CancellationToken cancellationToken)
    {
        WorkflowTask task = await dbContext.WorkflowTasks.FirstOrDefaultAsync(candidate => candidate.Id == taskId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowTask), taskId);

        // Someone else's task is reported as missing, the same as one that does not exist.
        if (currentUser.EmployeeId is not { } me || task.AssigneeEmployeeId != me)
        {
            throw new NotFoundException(nameof(WorkflowTask), taskId);
        }

        if (decision != WorkflowTaskStatus.Approved && string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainRuleException("WORKFLOW_COMMENT_REQUIRED", "Say why, so the requester knows what to change.");
        }

        WorkflowInstance instance = await LoadInstanceAsync(task.InstanceId, cancellationToken);
        if (instance.Status != WorkflowInstanceStatus.Pending)
        {
            throw new DomainRuleException("WORKFLOW_CLOSED", "This request is no longer awaiting a decision.");
        }

        task.Decide(decision, currentUser.UserId, comment, Now);
        instance.Touch(Now);

        switch (decision)
        {
            case WorkflowTaskStatus.Rejected:
                Log(instance, WorkflowActionKind.Rejected, task.Id, comment);
                await ClosePendingTasksAsync(instance, cancellationToken);
                instance.Reject(Now);
                publisher.Publish(new WorkflowRejectedIntegrationEvent(
                    instance.Id, instance.SubjectType, instance.SubjectId, instance.Title, instance.RequesterEmployeeId, comment, instance.Link));
                break;

            case WorkflowTaskStatus.SentBack:
                Log(instance, WorkflowActionKind.SentBack, task.Id, comment);
                await ClosePendingTasksAsync(instance, cancellationToken);
                instance.SendBack();
                publisher.Publish(new WorkflowSentBackIntegrationEvent(
                    instance.Id, instance.SubjectType, instance.SubjectId, instance.Title, instance.RequesterEmployeeId, comment, instance.Link));
                break;

            default:
                Log(instance, WorkflowActionKind.Approved, task.Id, comment);
                await AdvanceIfStageCompleteAsync(instance, task, cancellationToken);
                break;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The requester puts a sent-back request through again, from the first stage.</summary>
    public async Task ResubmitAsync(Guid instanceId, string? comment, CancellationToken cancellationToken)
    {
        WorkflowInstance instance = await LoadOwnInstanceAsync(instanceId, cancellationToken);

        instance.Resubmit(Now);
        instance.Touch(Now);
        Log(instance, WorkflowActionKind.Resubmitted, comment: comment);

        bool pending = await ActivateFromAsync(instance, 0, cancellationToken);
        if (!pending)
        {
            PublishApproved(instance);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The requester takes a request back before it is decided.</summary>
    public async Task WithdrawOwnAsync(Guid instanceId, string? comment, CancellationToken cancellationToken)
    {
        WorkflowInstance instance = await LoadOwnInstanceAsync(instanceId, cancellationToken);
        await CloseAsWithdrawnAsync(instance, comment, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>An administrator moves a pending task to someone else.</summary>
    public async Task ReassignAsync(Guid taskId, Guid toEmployeeId, string? comment, CancellationToken cancellationToken)
    {
        WorkflowTask task = await dbContext.WorkflowTasks.FirstOrDefaultAsync(candidate => candidate.Id == taskId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowTask), taskId);
        WorkflowInstance instance = await LoadInstanceAsync(task.InstanceId, cancellationToken);

        if (!task.IsPending || instance.Status != WorkflowInstanceStatus.Pending)
        {
            throw new DomainRuleException("WORKFLOW_TASK_CLOSED", "Only a pending task can be reassigned.");
        }

        if (toEmployeeId == task.AssigneeEmployeeId)
        {
            throw new DomainRuleException("WORKFLOW_REASSIGN_SAME", "The task is already with that person.");
        }

        if (toEmployeeId == instance.RequesterEmployeeId)
        {
            throw new DomainRuleException("WORKFLOW_SELF_APPROVAL", "A request cannot be given to the person who raised it.");
        }

        if (!await employees.IsActiveAsync(toEmployeeId, cancellationToken))
        {
            throw new DomainRuleException("WORKFLOW_ASSIGNEE_INACTIVE", "Tasks can only go to current employees.");
        }

        task.HandOver(Now, escalated: false);
        instance.Touch(Now);
        WorkflowStage stage = instance.Stages[task.StageIndex];
        AddTask(instance, task.StageIndex, stage, toEmployeeId, delegatedFrom: null);
        Log(instance, WorkflowActionKind.Reassigned, task.Id, comment);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Moves every overdue task one level up (spec §109 escalation). Run by the SLA sweep for one
    /// tenant at a time; returns how many tasks it escalated.
    /// </summary>
    public async Task<int> EscalateOverdueAsync(CancellationToken cancellationToken)
    {
        DateTime now = Now;
        List<WorkflowTask> overdue = await dbContext.WorkflowTasks
            .Where(task => task.Status == WorkflowTaskStatus.Pending && task.DueDate != null && task.DueDate <= now && task.EscalatedDate == null)
            .OrderBy(task => task.DueDate)
            .Take(200)
            .ToListAsync(cancellationToken);

        int escalated = 0;
        foreach (WorkflowTask task in overdue)
        {
            WorkflowInstance instance = await LoadInstanceAsync(task.InstanceId, cancellationToken);
            if (instance.Status != WorkflowInstanceStatus.Pending)
            {
                task.Close(now);
                continue;
            }

            WorkflowStage stage = instance.Stages[task.StageIndex];
            Guid? target = await EscalationTargetAsync(instance, stage, task, cancellationToken);

            if (target is null)
            {
                task.MarkEscalated(now);
                Log(instance, WorkflowActionKind.Escalated, task.Id, "Overdue, with no one above to escalate to.");
                continue;
            }

            task.HandOver(now, escalated: true);
            instance.Touch(now);
            AddTask(instance, task.StageIndex, stage, target.Value, delegatedFrom: null);
            Log(instance, WorkflowActionKind.Escalated, task.Id, "Escalated after the SLA passed.");
            publisher.Publish(new WorkflowTaskEscalatedIntegrationEvent(task.Id, instance.Id, instance.Title, task.AssigneeEmployeeId, target.Value, instance.Link));
            escalated++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return escalated;
    }

    private async Task AdvanceIfStageCompleteAsync(WorkflowInstance instance, WorkflowTask decided, CancellationToken cancellationToken)
    {
        WorkflowStage stage = instance.Stages[decided.StageIndex];

        List<WorkflowTask> stillPending = await dbContext.WorkflowTasks
            .Where(task => task.InstanceId == instance.Id
                && task.Round == instance.Round
                && task.StageIndex == decided.StageIndex
                && task.Status == WorkflowTaskStatus.Pending
                && task.Id != decided.Id)
            .ToListAsync(cancellationToken);

        if (stage.Mode == StageMode.All && stillPending.Count > 0)
        {
            return;
        }

        foreach (WorkflowTask other in stillPending)
        {
            other.Close(Now);
        }

        bool pending = await ActivateFromAsync(instance, decided.StageIndex + 1, cancellationToken);
        if (!pending)
        {
            PublishApproved(instance);
        }
    }

    /// <summary>
    /// Opens the first applicable stage at or after <paramref name="startIndex"/>, or approves the
    /// request when none is left. Returns whether the request is now waiting on someone.
    /// </summary>
    private async Task<bool> ActivateFromAsync(WorkflowInstance instance, int startIndex, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowStage> stages = instance.Stages;
        IReadOnlyDictionary<string, string> fields = instance.Fields;

        for (int index = startIndex; index < stages.Count; index++)
        {
            WorkflowStage stage = stages[index];

            if (stage.Condition is { } condition && !condition.IsMet(fields))
            {
                Log(instance, WorkflowActionKind.StageSkipped, comment: $"'{stage.Name}' does not apply to this request.");
                continue;
            }

            List<Guid> approvers = await ResolveStageAsync(instance, stage, cancellationToken);
            if (approvers.Count == 0)
            {
                throw new DomainRuleException(
                    "WORKFLOW_NO_APPROVER",
                    $"No one can approve the '{stage.Name}' stage for this request. Ask an administrator to set a fallback approver.");
            }

            instance.EnterStage(index);
            foreach (Guid approver in approvers)
            {
                (Guid assignee, Guid? delegatedFrom) = await ApplyDelegationAsync(instance, approver, cancellationToken);
                AddTask(instance, index, stage, assignee, delegatedFrom);
            }

            return true;
        }

        instance.Approve(Now);
        Log(instance, instance.DefinitionId is null || stages.Count == 0 ? WorkflowActionKind.AutoApproved : WorkflowActionKind.Completed);
        return false;
    }

    /// <summary>The stage's approvers: resolved, current, distinct, never the requester - or the fallback.</summary>
    private async Task<List<Guid>> ResolveStageAsync(WorkflowInstance instance, WorkflowStage stage, CancellationToken cancellationToken)
    {
        var resolved = new List<Guid>();
        foreach (ApproverRule rule in stage.Approvers)
        {
            if (await ResolveAsync(rule, instance.RequesterEmployeeId, cancellationToken) is { } approver)
            {
                resolved.Add(approver);
            }
        }

        List<Guid> eligible = await EligibleAsync(resolved, instance.RequesterEmployeeId, cancellationToken);
        if (eligible.Count == 0 && instance.FallbackApproverEmployeeId is { } fallback)
        {
            eligible = await EligibleAsync([fallback], instance.RequesterEmployeeId, cancellationToken);
        }

        return eligible;
    }

    private async Task<List<Guid>> EligibleAsync(IEnumerable<Guid> candidates, Guid? requester, CancellationToken cancellationToken)
    {
        var eligible = new List<Guid>();
        foreach (Guid candidate in candidates.Distinct())
        {
            if (candidate != requester && await employees.IsActiveAsync(candidate, cancellationToken))
            {
                eligible.Add(candidate);
            }
        }

        return eligible;
    }

    private async Task<Guid?> ResolveAsync(ApproverRule rule, Guid? requester, CancellationToken cancellationToken)
    {
        if (rule.Kind == ApproverKind.Employee)
        {
            return rule.EmployeeId;
        }

        if (requester is null || await employees.GetAsync(requester.Value, cancellationToken) is not { } person)
        {
            return null;
        }

        switch (rule.Kind)
        {
            case ApproverKind.ReportingManager:
                return person.ManagerEmployeeId;

            case ApproverKind.SkipLevelManager:
                return person.ManagerEmployeeId is { } manager && await employees.GetAsync(manager, cancellationToken) is { } managerRecord
                    ? managerRecord.ManagerEmployeeId
                    : null;

            case ApproverKind.DepartmentHead:
                return person.DepartmentId is { } department ? await orgStructure.GetDepartmentHeadAsync(department, cancellationToken) : null;

            case ApproverKind.HrBusinessPartner:
                return person.HrbpEmployeeId;

            default:
                return null;
        }
    }

    /// <summary>One hop: if the approver has an active delegation covering this request, the delegate gets it.</summary>
    private async Task<(Guid Assignee, Guid? DelegatedFrom)> ApplyDelegationAsync(WorkflowInstance instance, Guid approver, CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(Now);
        string subjectType = instance.SubjectType;

        Delegation? delegation = await dbContext.Delegations
            .AsNoTracking()
            .Where(candidate => candidate.DelegatorEmployeeId == approver
                && candidate.IsActive
                && candidate.StartsOn <= today
                && candidate.EndsOn >= today
                && (candidate.SubjectType == null || candidate.SubjectType == subjectType))
            .OrderByDescending(candidate => candidate.SubjectType != null)
            .FirstOrDefaultAsync(cancellationToken);

        if (delegation is null
            || delegation.DelegateEmployeeId == instance.RequesterEmployeeId
            || !await employees.IsActiveAsync(delegation.DelegateEmployeeId, cancellationToken))
        {
            return (approver, null);
        }

        return (delegation.DelegateEmployeeId, approver);
    }

    private async Task<Guid?> EscalationTargetAsync(WorkflowInstance instance, WorkflowStage stage, WorkflowTask task, CancellationToken cancellationToken)
    {
        Guid? target = stage.EscalateTo is { } rule
            ? await ResolveAsync(rule, instance.RequesterEmployeeId, cancellationToken)
            : (await employees.GetAsync(task.AssigneeEmployeeId, cancellationToken))?.ManagerEmployeeId;

        if (target is not { } candidate || candidate == task.AssigneeEmployeeId)
        {
            return null;
        }

        List<Guid> eligible = await EligibleAsync([candidate], instance.RequesterEmployeeId, cancellationToken);
        return eligible.Count > 0 ? eligible[0] : null;
    }

    private void AddTask(WorkflowInstance instance, int stageIndex, WorkflowStage stage, Guid assignee, Guid? delegatedFrom)
    {
        DateTime? due = stage.SlaHours is { } hours ? Now.AddHours(hours) : null;
        var task = WorkflowTask.Assign(instance, stageIndex, stage.Name, assignee, delegatedFrom, due);
        dbContext.WorkflowTasks.Add(task);

        publisher.Publish(new WorkflowTaskAssignedIntegrationEvent(
            task.Id, instance.Id, instance.SubjectType, instance.SubjectId, instance.Title, assignee, delegatedFrom, due, instance.Link));
    }

    private async Task CloseAsWithdrawnAsync(WorkflowInstance instance, string? comment, CancellationToken cancellationToken)
    {
        await ClosePendingTasksAsync(instance, cancellationToken);
        instance.Withdraw(Now);
        Log(instance, WorkflowActionKind.Withdrawn, comment: comment);
        publisher.Publish(new WorkflowWithdrawnIntegrationEvent(instance.Id, instance.SubjectType, instance.SubjectId));
    }

    private async Task ClosePendingTasksAsync(WorkflowInstance instance, CancellationToken cancellationToken)
    {
        List<WorkflowTask> pending = await dbContext.WorkflowTasks
            .Where(task => task.InstanceId == instance.Id && task.Status == WorkflowTaskStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (WorkflowTask task in pending)
        {
            task.Close(Now);
        }
    }

    private void PublishApproved(WorkflowInstance instance)
        => publisher.Publish(new WorkflowApprovedIntegrationEvent(
            instance.Id, instance.SubjectType, instance.SubjectId, instance.Title, instance.RequesterEmployeeId, instance.Link));

    private async Task<WorkflowInstance> LoadInstanceAsync(Guid instanceId, CancellationToken cancellationToken)
        => await dbContext.WorkflowInstances.FirstOrDefaultAsync(instance => instance.Id == instanceId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowInstance), instanceId);

    /// <summary>A request the caller raised; anyone else's is reported as missing.</summary>
    private async Task<WorkflowInstance> LoadOwnInstanceAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        WorkflowInstance instance = await LoadInstanceAsync(instanceId, cancellationToken);
        bool mine = instance.RequestedByUserId == currentUser.UserId
            || (currentUser.EmployeeId is { } me && instance.RequesterEmployeeId == me);

        return mine ? instance : throw new NotFoundException(nameof(WorkflowInstance), instanceId);
    }

    private void Log(WorkflowInstance instance, WorkflowActionKind kind, Guid? taskId = null, string? comment = null)
        => dbContext.WorkflowActions.Add(WorkflowAction.Record(
            instance.Id, kind, currentUser.UserId, currentUser.EmployeeId, Now.AddTicks(_historyOrder++), taskId, comment));
}
