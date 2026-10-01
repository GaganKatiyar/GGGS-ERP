/**
 * Spend (Expenses and Travel), mirroring the DTOs in `Gggs.Spend.Api.Contracts.SpendContracts`.
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
  readonly travelRequestId: string | null;
}

export interface CreateExpenseClaimRequest {
  readonly employeeId: string;
  readonly claimNo: string;
  readonly title: string;
  readonly currency: string;
  readonly travelRequestId?: string;
}

export interface AddExpenseLineRequest {
  readonly categoryId: string;
  readonly expenseDate: string;
  readonly amount: number;
  readonly merchant?: string;
}

export type TravelRequestStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected';

export interface TravelRequest {
  readonly id: string;
  readonly requestNo: string;
  readonly employeeId: string;
  readonly purpose: string;
  readonly fromCity: string;
  readonly toCity: string;
  readonly fromDate: string;
  readonly toDate: string;
  readonly estimatedCost: number;
  readonly currency: string;
  readonly status: TravelRequestStatus;
  readonly submittedDate: string | null;
  readonly decidedDate: string | null;
}

export interface CreateTravelRequestRequest {
  readonly employeeId: string;
  readonly requestNo: string;
  readonly purpose: string;
  readonly fromCity: string;
  readonly toCity: string;
  readonly fromDate: string;
  readonly toDate: string;
  readonly estimatedCost: number;
  readonly currency: string;
}

export type TravelAdvanceStatus = 'Requested' | 'Disbursed';

export interface TravelAdvance {
  readonly id: string;
  readonly travelRequestId: string;
  readonly amount: number;
  readonly currency: string;
  readonly status: TravelAdvanceStatus;
  readonly disbursedDate: string | null;
}

export interface RequestTravelAdvanceRequest {
  readonly amount: number;
  readonly currency: string;
}

export type TravelSettlementStatus = 'Draft' | 'Settled';

export interface TravelSettlement {
  readonly id: string;
  readonly travelRequestId: string;
  readonly advanceTotal: number;
  readonly expenseTotal: number;
  readonly netPayable: number;
  readonly netRecoverable: number;
  readonly status: TravelSettlementStatus;
  readonly settledDate: string | null;
}
