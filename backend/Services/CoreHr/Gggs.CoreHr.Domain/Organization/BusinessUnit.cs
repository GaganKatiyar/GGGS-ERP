using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>
/// A grouping above department, for reporting and cost roll-up rather than legal or payroll
/// purposes - "Retail Division", "Engineering", a line of business that spans several departments
/// and sometimes several legal entities.
/// </summary>
public sealed class BusinessUnit : TenantAggregateRoot
{
    private BusinessUnit()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// The employee who leads it. A plain id rather than a composite FK to <c>Employee</c>: that
    /// table does not exist yet, and forcing every organization unit to wait for it would put the
    /// dependency the wrong way round.
    /// </summary>
    public Guid? HeadEmployeeId { get; private set; }

    public static BusinessUnit Create(string code, string name) => new()
    {
        Code = OrganizationCode.Normalize(code),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
    };

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200);

    public void SetHead(Guid? employeeId) => HeadEmployeeId = employeeId;
}
