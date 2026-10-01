import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AddExpenseLineRequest,
  CreateExpenseCategoryRequest,
  CreateExpenseClaimRequest,
  ExpenseCategory,
  ExpenseClaim,
  ExpenseLine,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Expense categories and claims over the gateway's `/api/v1/spend/*` routes. No Workflow
 * integration, multi-currency or receipts in this v1 slice. */
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

  approveClaim(id: string): Promise<ExpenseClaim> {
    return firstValueFrom(this.http.post<ExpenseClaim>(`/api/v1/spend/claims/${id}/approve`, {}));
  }

  rejectClaim(id: string): Promise<ExpenseClaim> {
    return firstValueFrom(this.http.post<ExpenseClaim>(`/api/v1/spend/claims/${id}/reject`, {}));
  }
}
