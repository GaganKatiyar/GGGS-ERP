using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Spend.Domain.Expenses;

namespace Gggs.Spend.Application.Expenses;

public sealed class ExpenseCategoryService(IExpenseCategoryRepository categories, IUnitOfWork unitOfWork)
{
    public async Task<ExpenseCategory> CreateAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        Guard(
            !await categories.CodeExistsAsync(code, cancellationToken),
            "EXPENSE_CATEGORY_CODE_ALREADY_EXISTS",
            $"A category with code '{code}' already exists.");

        ExpenseCategory category = ExpenseCategory.Create(code, name);
        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<ExpenseCategory> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ExpenseCategory category = await GetOrThrowAsync(id, cancellationToken);
        category.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<ExpenseCategory> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ExpenseCategory category = await GetOrThrowAsync(id, cancellationToken);
        category.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return category;
    }

    private async Task<ExpenseCategory> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await categories.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseCategory), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
