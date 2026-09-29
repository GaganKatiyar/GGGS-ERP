using Gggs.Time.Domain.Leave;

namespace Gggs.Time.Application.Leave;

/// <summary>
/// Finds the <see cref="LeavePolicyRule"/> that governs one leave type for one employee.
/// <para>
/// v1 has no <c>PolicyAssignment</c> targeting (architecture.md §7.2's per-legal-entity/branch/
/// department/grade/employment-type/employee scoping, with priority ordering): a tenant has one
/// <see cref="LeavePolicy"/>, and every employee is governed by it. <see cref="ResolveRuleAsync"/>
/// is the seam that targeting would slot into later - the caller does not know or care whether the
/// answer came from "the tenant's one policy" or a real resolution against the employee's org
/// position, only that it got back the rule that applies to this employee right now.
/// </para>
/// </summary>
public sealed class LeavePolicyResolver(ILeavePolicyRepository policies)
{
    /// <summary><paramref name="employeeId"/> is unused today - see this type's own remarks - and
    /// kept on the signature so a real per-employee resolution can land later without every caller
    /// changing.</summary>
    public async Task<LeavePolicyRule?> ResolveRuleAsync(Guid employeeId, Guid leaveTypeId, CancellationToken cancellationToken = default)
    {
        _ = employeeId;

        Guid? policyId = await policies.GetTenantDefaultPolicyIdAsync(cancellationToken);

        if (policyId is null)
        {
            return null;
        }

        return await policies.GetRuleAsync(policyId.Value, leaveTypeId, cancellationToken);
    }
}
