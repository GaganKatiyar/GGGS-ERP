using Gggs.Spend.Domain.Expenses;

namespace Gggs.Spend.Api.Contracts;

public sealed record ExpenseCategoryResponse(Guid Id, string Code, string Name, bool IsActive);

public sealed record CreateExpenseCategoryBody(string Code, string Name);

public sealed record ExpenseLineResponse(Guid Id, Guid CategoryId, DateOnly ExpenseDate, decimal Amount, string? Merchant);

public sealed record ExpenseClaimResponse(
    Guid Id, string ClaimNo, Guid EmployeeId, string Title, string Currency, decimal Total, ExpenseClaimStatus Status,
    DateTime? SubmittedDate, DateTime? DecidedDate);

public sealed record CreateExpenseClaimBody(Guid EmployeeId, string ClaimNo, string Title, string Currency);

public sealed record AddExpenseLineBody(Guid CategoryId, DateOnly ExpenseDate, decimal Amount, string? Merchant);
