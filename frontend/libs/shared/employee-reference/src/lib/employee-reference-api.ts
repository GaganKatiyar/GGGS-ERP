import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { EmployeeListItem, PagedResult } from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * The one employee search query every picker needs, over the same `/api/v1/employees` route
 * `EmployeesApi` (inside the employees feature) already calls. Split out here rather than reused
 * from there: a feature never imports another feature (the one-way module-boundary rule -
 * `OrgReferenceApi`'s own remarks explain the same move for organization lookups), and Attendance,
 * Leave and Workflow all need to look an employee up by name without owning employee management
 * themselves.
 */
@Injectable({ providedIn: 'root' })
export class EmployeeReferenceApi {
  private readonly http = inject(HttpClient);

  search(query: string, pageSize = 10): Promise<PagedResult<EmployeeListItem>> {
    return firstValueFrom(
      this.http.get<PagedResult<EmployeeListItem>>('/api/v1/employees', {
        params: { page: 1, pageSize, search: query },
      }),
    );
  }

  /** Everyone whose current employment record reports to the caller - `GET /api/v1/employees/my-team`. */
  listMyTeam(): Promise<readonly EmployeeListItem[]> {
    return firstValueFrom(
      this.http.get<readonly EmployeeListItem[]>('/api/v1/employees/my-team'),
    );
  }
}
