using Gggs.Workflow.Domain.Workflows;

namespace Gggs.Workflow.Application.Workflows;

/// <summary>
/// "While I am out, send my approvals to someone else" - checked once per resolved approver, when
/// a step's tasks are created (architecture.md §13.3 step 2). A delegation that starts after a
/// task already exists does not retroactively reassign it; the next step (or the next instance) is
/// the first to see it.
/// </summary>
public sealed class DelegationResolver(IDelegationRepository delegations)
{
    private const int MaxChainDepth = 5;

    /// <summary>
    /// The user a task assigned to <paramref name="approverUserId"/> should actually go to, or
    /// <see langword="null"/> if nobody is standing in for them. Follows a chain (A delegates to
    /// B, who has separately delegated to C) up to <see cref="MaxChainDepth"/> hops, stopping at
    /// the first cycle rather than looping forever - a misconfigured pair of mutual delegations
    /// fails safe to "no delegate" rather than hanging the request.
    /// </summary>
    public async Task<Guid?> ResolveDelegateAsync(Guid approverUserId, DateOnly onDate, CancellationToken cancellationToken = default)
    {
        Guid current = approverUserId;
        var visited = new HashSet<Guid> { approverUserId };
        Guid? resolved = null;

        for (int hop = 0; hop < MaxChainDepth; hop++)
        {
            IReadOnlyList<Delegation> active = await delegations.GetActiveForUserAsync(current, onDate, cancellationToken);
            Delegation? next = active.FirstOrDefault(d => d.IsActiveOn(onDate));

            if (next is null)
            {
                break;
            }

            if (!visited.Add(next.ToUserId))
            {
                break;
            }

            resolved = next.ToUserId;
            current = next.ToUserId;
        }

        return resolved;
    }
}
