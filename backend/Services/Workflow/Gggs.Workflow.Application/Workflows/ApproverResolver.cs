using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Workflow.Application.Snapshots;
using Gggs.Workflow.Domain.Snapshots;
using Microsoft.Extensions.Logging;

namespace Gggs.Workflow.Application.Workflows;

/// <summary>
/// Turns a <see cref="WorkflowApproverRuleModel"/> into the user ids who must act on a step, for
/// one instance's subject employee.
/// <para>
/// Segregation of duties (architecture.md §8.4 point 7): the instance's own initiator and subject
/// employee's linked user are always removed from a resolved list, even if the rule would
/// otherwise have named them (a self-approving manager, most commonly) - <c>WorkflowEngine</c>
/// passes both ids in and this method is where they are filtered out, so no caller can forget the
/// check.
/// </para>
/// </summary>
public sealed class ApproverResolver(IEmployeeSnapshotRepository snapshots, ILogger<ApproverResolver> logger)
{
    private const int MaxManagerChainDepth = 10;

    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        WorkflowApproverRuleModel rule,
        Guid subjectEmployeeId,
        Guid initiatorUserId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> resolved = rule.Type switch
        {
            WorkflowApproverRuleType.ReportingManager
                => await ResolveReportingManagerAsync(subjectEmployeeId, rule.Level ?? 1, cancellationToken),
            WorkflowApproverRuleType.SpecificUser => rule.UserId is { } userId ? [userId] : [],
            WorkflowApproverRuleType.Role => throw NotSupported(rule.Type),
            WorkflowApproverRuleType.ApproverGroup => throw NotSupported(rule.Type),
            _ => throw NotSupported(rule.Type),
        };

        List<Guid> withoutSelfApproval = resolved
            .Where(userId => userId != initiatorUserId)
            .Distinct()
            .ToList();

        EmployeeSnapshot? subject = await snapshots.GetAsync(subjectEmployeeId, cancellationToken);

        if (subject?.UserId is { } subjectUserId)
        {
            withoutSelfApproval.RemoveAll(userId => userId == subjectUserId);
        }

        return withoutSelfApproval;
    }

    /// <summary>
    /// Walks <see cref="EmployeeSnapshot.ManagerEmployeeId"/> <paramref name="level"/> times from
    /// the subject employee. A missing snapshot, a missing manager link, an exited manager with no
    /// linked user, or a cycle all resolve to an empty list rather than throwing - the caller
    /// (<c>WorkflowEngine</c>) treats "no approver resolved" as a configuration warning
    /// (architecture.md §13.3 step 8: falls back to the tenant's default approver), not a failure
    /// of this method.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveReportingManagerAsync(
        Guid subjectEmployeeId, int level, CancellationToken cancellationToken)
    {
        Guid currentEmployeeId = subjectEmployeeId;
        var visited = new HashSet<Guid>();

        for (int depth = 0; depth < Math.Min(level, MaxManagerChainDepth); depth++)
        {
            if (!visited.Add(currentEmployeeId))
            {
                logger.LogWarning(
                    "Manager chain for employee {EmployeeId} cycles at depth {Depth}; stopping.",
                    subjectEmployeeId, depth);
                return [];
            }

            EmployeeSnapshot? current = await snapshots.GetAsync(currentEmployeeId, cancellationToken);

            if (current?.ManagerEmployeeId is not { } managerId)
            {
                return [];
            }

            currentEmployeeId = managerId;
        }

        EmployeeSnapshot? manager = await snapshots.GetAsync(currentEmployeeId, cancellationToken);
        return manager?.UserId is { } managerUserId ? [managerUserId] : [];
    }

    private static DomainRuleException NotSupported(WorkflowApproverRuleType type) => new(
        "WORKFLOW_APPROVER_RULE_NOT_SUPPORTED",
        $"The approver rule type '{type}' is not supported yet.");
}
