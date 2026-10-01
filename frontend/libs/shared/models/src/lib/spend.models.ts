/**
 * Spend (Expenses), mirroring the DTOs in `Gggs.Spend.Api.Contracts.SpendContracts`.
 */

export interface ExpenseCategory {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
}

export interface CreateExpenseCategoryRequest {
  readonly code: string;
  readonly name: string;
}

export type ExpenseClaimStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected';

export interface ExpenseLine {
  readonly id: string;
  readonly categoryId: string;
  readonly expenseDate: string;
  readonly amount: number;
  readonly merchant: string | null;
}

export interface ExpenseClaim {
  readonly id: string;
  readonly claimNo: string;
  readonly employeeId: string;
  readonly title: string;
  readonly currency: string;
  readonly total: number;
  readonly status: ExpenseClaimStatus;
  readonly submittedDate: string | null;
  readonly decidedDate: string | null;
}

export interface CreateExpenseClaimRequest {
  readonly employeeId: string;
  readonly claimNo: string;
  readonly title: string;
  readonly currency: string;
}

export interface AddExpenseLineRequest {
  readonly categoryId: string;
  readonly expenseDate: string;
  readonly amount: number;
  readonly merchant?: string;
}
