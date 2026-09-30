import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import type {
  AddSalaryStructureComponentRequest,
  AssignEmployeeSalaryRequest,
  CreateSalaryComponentRequest,
  CreateSalaryStructureRequest,
  EmployeeSalary,
  SalaryComponent,
  SalaryStructure,
  SalaryStructureComponent,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Salary components, structures and employee salary assignment, over the gateway's
 * `/api/v1/payroll/*` routes. No payroll runs, payslips or statutory compliance in this v1 slice. */
@Injectable({ providedIn: 'root' })
export class PayrollApi {
  private readonly http = inject(HttpClient);

  // ---- Components ----

  listComponents(): Promise<readonly SalaryComponent[]> {
    return firstValueFrom(this.http.get<readonly SalaryComponent[]>('/api/v1/payroll/components'));
  }

  createComponent(request: CreateSalaryComponentRequest): Promise<SalaryComponent> {
    return firstValueFrom(this.http.post<SalaryComponent>('/api/v1/payroll/components', request));
  }

  deactivateComponent(id: string): Promise<SalaryComponent> {
    return firstValueFrom(this.http.post<SalaryComponent>(`/api/v1/payroll/components/${id}/deactivate`, {}));
  }

  reactivateComponent(id: string): Promise<SalaryComponent> {
    return firstValueFrom(this.http.post<SalaryComponent>(`/api/v1/payroll/components/${id}/reactivate`, {}));
  }

  // ---- Structures ----

  listStructures(): Promise<readonly SalaryStructure[]> {
    return firstValueFrom(this.http.get<readonly SalaryStructure[]>('/api/v1/payroll/structures'));
  }

  getStructure(id: string): Promise<SalaryStructure> {
    return firstValueFrom(this.http.get<SalaryStructure>(`/api/v1/payroll/structures/${id}`));
  }

  createStructure(request: CreateSalaryStructureRequest): Promise<SalaryStructure> {
    return firstValueFrom(this.http.post<SalaryStructure>('/api/v1/payroll/structures', request));
  }

  listStructureComponents(structureId: string): Promise<readonly SalaryStructureComponent[]> {
    return firstValueFrom(
      this.http.get<readonly SalaryStructureComponent[]>(`/api/v1/payroll/structures/${structureId}/components`),
    );
  }

  addStructureComponent(structureId: string, request: AddSalaryStructureComponentRequest): Promise<SalaryStructureComponent> {
    return firstValueFrom(
      this.http.post<SalaryStructureComponent>(`/api/v1/payroll/structures/${structureId}/components`, request),
    );
  }

  // ---- Employee salary ----

  /** Null when the employee has no salary assigned yet - a 404 here is a normal, expected state,
   * not an error (see RecruitmentApi.getOffer's own remarks on the same shape). */
  async getEmployeeSalary(employeeId: string): Promise<EmployeeSalary | null> {
    try {
      return await firstValueFrom(this.http.get<EmployeeSalary>(`/api/v1/payroll/employees/${employeeId}/salary`));
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        return null;
      }

      throw error;
    }
  }

  assignEmployeeSalary(request: AssignEmployeeSalaryRequest): Promise<EmployeeSalary> {
    return firstValueFrom(this.http.post<EmployeeSalary>('/api/v1/payroll/employee-salaries', request));
  }
}
