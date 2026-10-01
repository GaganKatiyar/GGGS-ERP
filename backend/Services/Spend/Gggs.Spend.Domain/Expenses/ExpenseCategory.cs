using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Spend.Domain.Expenses;

/// <summary>
/// A named kind of spend a claim line can be against - "Travel," "Meals," "Office Supplies." One
/// flat tenant-wide catalog, matching every other v1 module's own simplification (no GL account
/// code, no per-category receipt-required threshold or mileage rate yet - architecture.md's
/// <c>exp.ExpenseCategory</c>).
/// </summary>
public sealed class ExpenseCategory : TenantAggregateRoot
{
    private ExpenseCategory()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public static ExpenseCategory Create(string code, string name) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100),
        IsActive = true,
    };

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
