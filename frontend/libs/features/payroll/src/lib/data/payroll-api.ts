import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import type {
  AddSalaryComponentSlabRequest,
  AddSalaryStructureComponentRequest,
  AssignEmployeeSalaryRequest,
  CreatePayGroupRequest,
  CreatePayPeriodRequest,
  CreatePayrollRunRequest,
  CreateSalaryComponentRequest,
  CreateSalaryStructureRequest,
  EmployeeSalary,
  PayGroup,
  PayPeriod,
  Payslip,
  PayslipListItem,
  PayrollRun,
  PayrollRunEmployee,
  SalaryComponent,
  SalaryComponentSlab,
  SalaryStructure,
  SalaryStructureComponent,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Salary components, structures, employee salary assignment, payroll runs, and the payslips they
 * publish, over the gateway's `/api/v1/payroll/*` routes. No statutory compliance in this v1 slice. */
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

  listComponentSlabs(componentId: string): Promise<readonly SalaryComponentSlab[]> {
    return firstValueFrom(this.http.get<readonly SalaryComponentSlab[]>(`/api/v1/payroll/components/${componentId}/slabs`));
  }

  addComponentSlab(componentId: string, request: AddSalaryComponentSlabRequest): Promise<SalaryComponentSlab> {
    return firstValueFrom(this.http.post<SalaryComponentSlab>(`/api/v1/payroll/components/${componentId}/slabs`, request));
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

  // ---- Pay groups and periods ----

  listPayGroups(): Promise<readonly PayGroup[]> {
    return firstValueFrom(this.http.get<readonly PayGroup[]>('/api/v1/payroll/pay-groups'));
  }

  createPayGroup(request: CreatePayGroupRequest): Promise<PayGroup> {
    return firstValueFrom(this.http.post<PayGroup>('/api/v1/payroll/pay-groups', request));
  }

  deactivatePayGroup(id: string): Promise<PayGroup> {
    return firstValueFrom(this.http.post<PayGroup>(`/api/v1/payroll/pay-groups/${id}/deactivate`, {}));
  }

  reactivatePayGroup(id: string): Promise<PayGroup> {
    return firstValueFrom(this.http.post<PayGroup>(`/api/v1/payroll/pay-groups/${id}/reactivate`, {}));
  }

  listPayPeriods(payGroupId: string): Promise<readonly PayPeriod[]> {
    return firstValueFrom(this.http.get<readonly PayPeriod[]>(`/api/v1/payroll/pay-groups/${payGroupId}/periods`));
  }

  createPayPeriod(payGroupId: string, request: CreatePayPeriodRequest): Promise<PayPeriod> {
    return firstValueFrom(this.http.post<PayPeriod>(`/api/v1/payroll/pay-groups/${payGroupId}/periods`, request));
  }

  // ---- Payroll runs ----

  listPayrollRuns(): Promise<readonly PayrollRun[]> {
    return firstValueFrom(this.http.get<readonly PayrollRun[]>('/api/v1/payroll/runs'));
  }

  getPayrollRun(id: string): Promise<PayrollRun> {
    return firstValueFrom(this.http.get<PayrollRun>(`/api/v1/payroll/runs/${id}`));
  }

  listPayrollRunEmployees(id: string): Promise<readonly PayrollRunEmployee[]> {
    return firstValueFrom(this.http.get<readonly PayrollRunEmployee[]>(`/api/v1/payroll/runs/${id}/employees`));
  }

  createPayrollRun(request: CreatePayrollRunRequest): Promise<PayrollRun> {
    return firstValueFrom(this.http.post<PayrollRun>('/api/v1/payroll/runs', request));
  }

  calculatePayrollRun(id: string): Promise<PayrollRun> {
    return firstValueFrom(this.http.post<PayrollRun>(`/api/v1/payroll/runs/${id}/calculate`, {}));
  }

  finalizePayrollRun(id: string): Promise<PayrollRun> {
    return firstValueFrom(this.http.post<PayrollRun>(`/api/v1/payroll/runs/${id}/finalize`, {}));
  }

  // ---- Payslips ----

  listMyPayslips(): Promise<readonly PayslipListItem[]> {
    return firstValueFrom(this.http.get<readonly PayslipListItem[]>('/api/v1/payroll/payslips/me'));
  }

  listEmployeePayslips(employeeId: string): Promise<readonly PayslipListItem[]> {
    return firstValueFrom(this.http.get<readonly PayslipListItem[]>(`/api/v1/payroll/employees/${employeeId}/payslips`));
  }

  getPayslip(id: string): Promise<Payslip> {
    return firstValueFrom(this.http.get<Payslip>(`/api/v1/payroll/payslips/${id}`));
  }

  getPayslipPdf(id: string): Promise<Blob> {
    return firstValueFrom(this.http.get(`/api/v1/payroll/payslips/${id}/pdf`, { responseType: 'blob' }));
  }
}
