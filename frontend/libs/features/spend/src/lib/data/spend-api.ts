import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import type {
  AddExpenseLineRequest,
  CreateExpenseCategoryRequest,
  CreateExpenseClaimRequest,
  CreateTravelRequestRequest,
  ExpenseCategory,
  ExpenseClaim,
  ExpenseLine,
  RequestTravelAdvanceRequest,
  TravelAdvance,
  TravelRequest,
  TravelSettlement,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Expense categories/claims and travel requests/advances/settlements over the gateway's
 * `/api/v1/spend/*` routes. Approving/rejecting a submitted claim or travel request is not here -
 * it goes through `WorkflowApi`'s unified approvals inbox, the same as leave requests. No
 * multi-currency or receipts in this v1 slice. */
@Injectable({ providedIn: 'root' })
export class SpendApi {
  private readonly http = inject(HttpClient);

  // ---- Categories ----

  listCategories(): Promise<readonly ExpenseCategory[]> {
    return firstValueFrom(this.http.get<readonly ExpenseCategory[]>('/api/v1/spend/categories'));
  }

  createCategory(request: CreateExpenseCategoryRequest): Promise<ExpenseCategory> {
    return firstValueFrom(this.http.post<ExpenseCategory>('/api/v1/spend/categories', request));
  }

  deactivateCategory(id: string): Promise<ExpenseCategory> {
    return firstValueFrom(this.http.post<ExpenseCategory>(`/api/v1/spend/categories/${id}/deactivate`, {}));
  }

  reactivateCategory(id: string): Promise<ExpenseCategory> {
    return firstValueFrom(this.http.post<ExpenseCategory>(`/api/v1/spend/categories/${id}/reactivate`, {}));
  }

  // ---- Claims ----

  listMyClaims(): Promise<readonly ExpenseClaim[]> {
    return firstValueFrom(this.http.get<readonly ExpenseClaim[]>('/api/v1/spend/claims/me'));
  }

  listEmployeeClaims(employeeId: string): Promise<readonly ExpenseClaim[]> {
    return firstValueFrom(this.http.get<readonly ExpenseClaim[]>(`/api/v1/spend/employees/${employeeId}/claims`));
  }

  getClaim(id: string): Promise<ExpenseClaim> {
    return firstValueFrom(this.http.get<ExpenseClaim>(`/api/v1/spend/claims/${id}`));
  }

  createClaim(request: CreateExpenseClaimRequest): Promise<ExpenseClaim> {
    return firstValueFrom(this.http.post<ExpenseClaim>('/api/v1/spend/claims', request));
  }

  listLines(claimId: string): Promise<readonly ExpenseLine[]> {
    return firstValueFrom(this.http.get<readonly ExpenseLine[]>(`/api/v1/spend/claims/${claimId}/lines`));
  }

  addLine(claimId: string, request: AddExpenseLineRequest): Promise<ExpenseLine> {
    return firstValueFrom(this.http.post<ExpenseLine>(`/api/v1/spend/claims/${claimId}/lines`, request));
  }

  submitClaim(id: string): Promise<ExpenseClaim> {
    return firstValueFrom(this.http.post<ExpenseClaim>(`/api/v1/spend/claims/${id}/submit`, {}));
  }

  // ---- Travel requests ----

  listMyTravelRequests(): Promise<readonly TravelRequest[]> {
    return firstValueFrom(this.http.get<readonly TravelRequest[]>('/api/v1/spend/travel-requests/me'));
  }

  listEmployeeTravelRequests(employeeId: string): Promise<readonly TravelRequest[]> {
    return firstValueFrom(this.http.get<readonly TravelRequest[]>(`/api/v1/spend/employees/${employeeId}/travel-requests`));
  }

  getTravelRequest(id: string): Promise<TravelRequest> {
    return firstValueFrom(this.http.get<TravelRequest>(`/api/v1/spend/travel-requests/${id}`));
  }

  createTravelRequest(request: CreateTravelRequestRequest): Promise<TravelRequest> {
    return firstValueFrom(this.http.post<TravelRequest>('/api/v1/spend/travel-requests', request));
  }

  submitTravelRequest(id: string): Promise<TravelRequest> {
    return firstValueFrom(this.http.post<TravelRequest>(`/api/v1/spend/travel-requests/${id}/submit`, {}));
  }

  // ---- Travel advances ----

  listAdvances(travelRequestId: string): Promise<readonly TravelAdvance[]> {
    return firstValueFrom(this.http.get<readonly TravelAdvance[]>(`/api/v1/spend/travel-requests/${travelRequestId}/advances`));
  }

  requestAdvance(travelRequestId: string, request: RequestTravelAdvanceRequest): Promise<TravelAdvance> {
    return firstValueFrom(this.http.post<TravelAdvance>(`/api/v1/spend/travel-requests/${travelRequestId}/advances`, request));
  }

  disburseAdvance(id: string): Promise<TravelAdvance> {
    return firstValueFrom(this.http.post<TravelAdvance>(`/api/v1/spend/travel-advances/${id}/disburse`, {}));
  }

  // ---- Travel settlement ----

  /** Null when the trip has no settlement built yet - a 404 here is a normal, expected state, not
   * an error (see PayrollApi.getEmployeeSalary's own remarks on the same shape). */
  async getSettlement(travelRequestId: string): Promise<TravelSettlement | null> {
    try {
      return await firstValueFrom(this.http.get<TravelSettlement>(`/api/v1/spend/travel-requests/${travelRequestId}/settlement`));
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        return null;
      }

      throw error;
    }
  }

  buildSettlement(travelRequestId: string): Promise<TravelSettlement> {
    return firstValueFrom(this.http.post<TravelSettlement>(`/api/v1/spend/travel-requests/${travelRequestId}/settlement/build`, {}));
  }

  settleSettlement(id: string): Promise<TravelSettlement> {
    return firstValueFrom(this.http.post<TravelSettlement>(`/api/v1/spend/travel-settlements/${id}/settle`, {}));
  }
}
