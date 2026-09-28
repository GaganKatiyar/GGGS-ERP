import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  ChangeAssignmentRequest,
  ChangeReportingManagerRequest,
  ConfirmEmployeeRequest,
  Employee,
  EmployeeListItem,
  EmploymentRecord,
  ExitEmployeeRequest,
  HireEmployeeRequest,
  PagedResult,
  RehireEmployeeRequest,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Employees and employment history, over the gateway's `/api/v1/employees` routes. */
@Injectable({ providedIn: 'root' })
export class EmployeesApi {
  private readonly http = inject(HttpClient);

  list(query: {
    page: number;
    pageSize: number;
    search?: string;
  }): Promise<PagedResult<EmployeeListItem>> {
    const params: Record<string, string> = {
      page: String(query.page),
      pageSize: String(query.pageSize),
    };

    if (query.search) {
      params['search'] = query.search;
    }

    return firstValueFrom(
      this.http.get<PagedResult<EmployeeListItem>>('/api/v1/employees', { params }),
    );
  }

  get(id: string): Promise<Employee> {
    return firstValueFrom(this.http.get<Employee>(`/api/v1/employees/${id}`));
  }

  getEmploymentHistory(id: string): Promise<readonly EmploymentRecord[]> {
    return firstValueFrom(
      this.http.get<readonly EmploymentRecord[]>(`/api/v1/employees/${id}/employment`),
    );
  }

  hire(request: HireEmployeeRequest): Promise<Employee> {
    return firstValueFrom(this.http.post<Employee>('/api/v1/employees', request));
  }

  transfer(id: string, request: ChangeAssignmentRequest): Promise<EmploymentRecord> {
    return firstValueFrom(
      this.http.post<EmploymentRecord>(`/api/v1/employees/${id}/transfer`, request),
    );
  }

  promote(id: string, request: ChangeAssignmentRequest): Promise<EmploymentRecord> {
    return firstValueFrom(
      this.http.post<EmploymentRecord>(`/api/v1/employees/${id}/promote`, request),
    );
  }

  confirm(id: string, request: ConfirmEmployeeRequest): Promise<Employee> {
    return firstValueFrom(
      this.http.post<Employee>(`/api/v1/employees/${id}/confirm`, request),
    );
  }

  serveNotice(id: string): Promise<Employee> {
    return firstValueFrom(
      this.http.post<Employee>(`/api/v1/employees/${id}/serve-notice`, {}),
    );
  }

  exit(id: string, request: ExitEmployeeRequest): Promise<Employee> {
    return firstValueFrom(
      this.http.post<Employee>(`/api/v1/employees/${id}/exit`, request),
    );
  }

  rehire(id: string, request: RehireEmployeeRequest): Promise<Employee> {
    return firstValueFrom(
      this.http.post<Employee>(`/api/v1/employees/${id}/rehire`, request),
    );
  }

  changeManager(
    id: string,
    request: ChangeReportingManagerRequest,
  ): Promise<EmploymentRecord> {
    return firstValueFrom(
      this.http.post<EmploymentRecord>(`/api/v1/employees/${id}/change-manager`, request),
    );
  }
}
