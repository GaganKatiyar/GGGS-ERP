using Gggs.Spend.Domain.Expenses;
using Gggs.Spend.Domain.Travel;

namespace Gggs.Spend.Api.Contracts;

public sealed record ExpenseCategoryResponse(Guid Id, string Code, string Name, bool IsActive);

public sealed record CreateExpenseCategoryBody(string Code, string Name);

public sealed record ExpenseLineResponse(Guid Id, Guid CategoryId, DateOnly ExpenseDate, decimal Amount, string? Merchant);

public sealed record ExpenseClaimResponse(
    Guid Id, string ClaimNo, Guid EmployeeId, string Title, string Currency, decimal Total, ExpenseClaimStatus Status,
    DateTime? SubmittedDate, DateTime? DecidedDate, Guid? TravelRequestId, Guid? WorkflowInstanceId);

public sealed record CreateExpenseClaimBody(Guid EmployeeId, string ClaimNo, string Title, string Currency, Guid? TravelRequestId = null);

public sealed record AddExpenseLineBody(Guid CategoryId, DateOnly ExpenseDate, decimal Amount, string? Merchant);

public sealed record TravelRequestResponse(
    Guid Id, string RequestNo, Guid EmployeeId, string Purpose, string FromCity, string ToCity, DateOnly FromDate,
    DateOnly ToDate, decimal EstimatedCost, string Currency, TravelRequestStatus Status, DateTime? SubmittedDate,
    DateTime? DecidedDate, Guid? WorkflowInstanceId);

public sealed record CreateTravelRequestBody(
    Guid EmployeeId, string RequestNo, string Purpose, string FromCity, string ToCity, DateOnly FromDate, DateOnly ToDate,
    decimal EstimatedCost, string Currency);

public sealed record TravelAdvanceResponse(
    Guid Id, Guid TravelRequestId, decimal Amount, string Currency, TravelAdvanceStatus Status, DateTime? DisbursedDate);

public sealed record RequestTravelAdvanceBody(decimal Amount, string Currency);

public sealed record TravelSettlementResponse(
    Guid Id, Guid TravelRequestId, decimal AdvanceTotal, decimal ExpenseTotal, decimal NetPayable, decimal NetRecoverable,
    TravelSettlementStatus Status, DateTime? SettledDate);
