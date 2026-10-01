using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Spend.Application.Snapshots;
using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Snapshots;

namespace Gggs.Spend.Application.Expenses;

/// <summary>
/// Creating a claim, adding lines to it while it is still Draft, and moving it through
/// Submitted -&gt; Approved/Rejected. No Workflow integration yet - see <see cref="ExpenseClaim"/>'s
/// own remarks.
/// </summary>
public sealed class ExpenseClaimService(
    IExpenseClaimRepository claims,
    IExpenseLineRepository lines,
    IExpenseCategoryRepository categories,
    IEmployeeSnapshotRepository snapshots,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<ExpenseClaim> CreateAsync(
        Guid employeeId, string claimNo, string title, string currency, CancellationToken cancellationToken = default)
    {
        EmployeeSnapshot? employee = await snapshots.GetAsync(employeeId, cancellationToken);
        Guard(employee is not null, "SPEND_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != "Exited", "SPEND_EMPLOYEE_EXITED", "This employee has already exited.");

        Guard(
            !await claims.ClaimNoExistsAsync(claimNo, cancellationToken),
            "EXPENSE_CLAIM_NO_ALREADY_EXISTS",
            $"A claim with number '{claimNo}' already exists.");

        ExpenseClaim claim = ExpenseClaim.Create(claimNo, employeeId, title, currency);
        claims.Add(claim);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return claim;
    }

    public async Task<ExpenseLine> AddLineAsync(
        Guid claimId, Guid categoryId, DateOnly expenseDate, decimal amount, string? merchant,
        CancellationToken cancellationToken = default)
    {
        ExpenseClaim claim = await claims.GetAsync(claimId, cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseClaim), claimId);

        _ = await categories.GetAsync(categoryId, cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseCategory), categoryId);

        ExpenseLine line = ExpenseLine.Create(claimId, categoryId, expenseDate, amount, merchant);
        lines.Add(line);

        IReadOnlyList<ExpenseLine> existingLines = await lines.ListForClaimAsync(claimId, cancellationToken);
        claim.RecomputeTotal(existingLines.Sum(x => x.Amount) + amount);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return line;
    }

    public async Task<ExpenseClaim> SubmitAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        ExpenseClaim claim = await GetOrThrowAsync(claimId, cancellationToken);
        claim.Submit(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return claim;
    }

    public async Task<ExpenseClaim> ApproveAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        ExpenseClaim claim = await GetOrThrowAsync(claimId, cancellationToken);
        claim.Approve(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return claim;
    }

    public async Task<ExpenseClaim> RejectAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        ExpenseClaim claim = await GetOrThrowAsync(claimId, cancellationToken);
        claim.Reject(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return claim;
    }

    private async Task<ExpenseClaim> GetOrThrowAsync(Guid claimId, CancellationToken cancellationToken)
        => await claims.GetAsync(claimId, cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseClaim), claimId);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
