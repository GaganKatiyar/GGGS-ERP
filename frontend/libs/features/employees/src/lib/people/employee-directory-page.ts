import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { EmployeeRow, PagedResult } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { FilterBar, PageHeader, StatusBadge } from '@gggs/shared/ui';

/**
 * The employee directory: everyone the caller's scope reaches, searchable by name, code or email
 * and filterable by department and status. A row opens Employee 360.
 */
@Component({
  selector: 'gggs-employee-directory-page',
  imports: [ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, FilterBar, DataTable, DataTableCell, StatusBadge, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Employees" subtitle="Everyone you can see, with where they sit today.">
      <div class="toolbar">
        <a class="btn" routerLink="org-chart"><i class="pi pi-sitemap" aria-hidden="true"></i> Org chart</a>
        <a class="btn" routerLink="import" *gggsHasPermission="'employee.profile.create'"><i class="pi pi-upload" aria-hidden="true"></i> Import</a>
        <a class="btn btn--primary" routerLink="new" *gggsHasPermission="'employee.profile.create'"><i class="pi pi-user-plus" aria-hidden="true"></i> Hire</a>
      </div>
    </gggs-page-header>

    <gggs-filter-bar
      placeholder="Search by name, code or email"
      [active]="!!search() || !!department.value || status() !== ''"
      [(search)]="search"
      (searchChange)="reload()"
      (cleared)="department.setValue(null); status.set(''); reload()"
    >
      <span class="directory__department">
        <gggs-org-select kind="departments" placeholder="All departments" ariaLabel="Department" [formControl]="department" />
      </span>
      <select aria-label="Status" [value]="status()" (change)="setStatus($event)">
        <option value="">Current employees</option>
        <option value="Active">Active</option>
        <option value="OnNotice">On notice</option>
        <option value="Exited">Exited</option>
      </select>
    </gggs-filter-bar>

    <gggs-data-table
      [result]="result()"
      [columns]="columns"
      [loading]="loading()"
      [fetchAll]="fetchAll"
      exportName="employees"
      emptyTitle="No employees match"
      emptyDescription="Try a different search or filter."
      (queryChange)="onQuery($event)"
      (rowClick)="open($event)"
    >
      <ng-template gggsCell="displayName" let-row>
        <strong>{{ row.displayName }}</strong>
        @if (row.officialEmail) {
          <div class="muted directory__email">{{ row.officialEmail }}</div>
        }
      </ng-template>
      <ng-template gggsCell="status" let-row>
        <gggs-status-badge [status]="row.status" />
      </ng-template>
    </gggs-data-table>
  `,
  styles: `
    .directory__department {
      min-width: 14rem;
    }

    .directory__email {
      font-size: var(--gggs-font-size-xs);
    }
  `,
})
export class EmployeeDirectoryPage {
  private readonly api = inject(EmployeeDirectoryApi);
  private readonly router = inject(Router);

  protected readonly columns: readonly DataTableColumn<EmployeeRow>[] = [
    { field: 'employeeCode', header: 'Code', sortable: true, width: '8rem' },
    { field: 'displayName', header: 'Name', sortable: true },
    { field: 'designation', header: 'Designation', sortable: true },
    { field: 'departmentName', header: 'Department' },
    { field: 'managerName', header: 'Manager' },
    { field: 'locationName', header: 'Location', hidden: true },
    { field: 'joiningDate', header: 'Joined', sortable: true, format: 'date', width: '8rem' },
    { field: 'status', header: 'Status', sortable: true, format: 'status', width: '8rem' },
  ];

  protected readonly result = signal<PagedResult<EmployeeRow> | null>(null);
  protected readonly loading = signal(false);
  protected readonly search = signal('');
  protected readonly status = signal('');
  protected readonly department = new FormControl<string | null>(null);
  private query: DataTableQuery = { page: 1, pageSize: 25 };

  constructor() {
    this.department.valueChanges.subscribe(() => this.reload());
  }

  /** Everything matching the filters, for Export. */
  protected readonly fetchAll = async (): Promise<readonly EmployeeRow[]> => {
    const rows: EmployeeRow[] = [];
    for (let page = 1; ; page++) {
      const result = await this.api.list({ ...this.filters(), page, pageSize: 200 });
      rows.push(...result.items);
      if (!result.hasNextPage) {
        return rows;
      }
    }
  };

  protected onQuery(query: DataTableQuery): void {
    this.query = query;
    void this.load();
  }

  protected reload(): void {
    this.query = { ...this.query, page: 1 };
    void this.load();
  }

  protected setStatus(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected open(row: EmployeeRow): void {
    void this.router.navigate(['/employees', row.id]);
  }

  private filters() {
    return {
      search: this.search() || undefined,
      departmentId: this.department.value ?? undefined,
      status: this.status() || undefined,
      includeExited: this.status() === 'Exited' ? true : undefined,
      sort: this.query.sort,
    };
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.result.set(await this.api.list({ ...this.filters(), page: this.query.page, pageSize: this.query.pageSize }));
    } finally {
      this.loading.set(false);
    }
  }
}
