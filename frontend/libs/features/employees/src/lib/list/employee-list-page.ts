import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { PageHeader } from '@gggs/shared/ui';
import type { EmployeeListItem, PagedResult } from '@gggs/shared/models';
import { EmployeesApi } from '../data/employees-api';

/** The employee directory: every hire this tenant has made, current and past. */
@Component({
  selector: 'gggs-employee-list-page',
  imports: [RouterLink, HasPermissionDirective, PageHeader, DataTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Employees" subtitle="Everyone this tenant has hired.">
      <a
        routerLink="new"
        class="btn btn--primary"
        *gggsHasPermission="'employee.profile.create'"
      >
        Hire employee
      </a>
    </gggs-page-header>

    <gggs-data-table
      [result]="result()"
      [columns]="columns"
      [loading]="isLoading()"
      emptyTitle="No employees yet"
      emptyDescription="Hire the first one to see them here."
      (queryChange)="load($event)"
      (rowClick)="openEmployee($event)"
    />
  `,
  styles: `
    .btn {
      display: inline-flex;
      align-items: center;
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      text-decoration: none;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #2f5fd6);
      color: #fff;
    }
  `,
})
export class EmployeeListPage {
  private readonly api = inject(EmployeesApi);
  private readonly router = inject(Router);

  protected readonly isLoading = signal(false);
  protected readonly result = signal<PagedResult<EmployeeListItem> | null>(null);

  protected readonly columns: readonly DataTableColumn<EmployeeListItem>[] = [
    { field: 'employeeCode', header: 'Code', width: '10rem' },
    { field: 'displayName', header: 'Name', sortable: true },
    { field: 'status', header: 'Status', width: '10rem' },
    { field: 'dateOfJoining', header: 'Joined', width: '10rem' },
  ];

  constructor() {
    void this.load({ page: 1, pageSize: 25 });
  }

  protected async load(query: DataTableQuery): Promise<void> {
    this.isLoading.set(true);

    try {
      this.result.set(await this.api.list({ page: query.page, pageSize: query.pageSize }));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected openEmployee(employee: EmployeeListItem): void {
    void this.router.navigate(['/employees', employee.id]);
  }
}
