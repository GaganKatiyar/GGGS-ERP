import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { DataTable, DataTableCell, type DataTableColumn } from '@gggs/shared/data-table';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { EmployeeRow, PagedResult } from '@gggs/shared/models';
import { EmptyState, KpiCard, PageHeader, StatusBadge } from '@gggs/shared/ui';

/** A manager's direct reports. Attendance, leave and allocation columns join as those modules ship. */
@Component({
  selector: 'gggs-team-page',
  imports: [PageHeader, KpiCard, DataTable, DataTableCell, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My team" subtitle="The people who report to you." />

    @if (error()) {
      <gggs-empty-state icon="pi pi-users" title="Your team could not be shown" [description]="error()!" />
    } @else {
      <div class="kpi-row team__kpis">
        <gggs-kpi-card label="Direct reports" icon="pi pi-users" [value]="result()?.totalCount ?? null" />
        <gggs-kpi-card label="On notice" icon="pi pi-sign-out" [value]="onNotice()" />
      </div>

      <gggs-data-table
        [result]="result()"
        [columns]="columns"
        [loading]="loading()"
        exportName="my-team"
        emptyTitle="Nobody reports to you"
        emptyDescription="People appear here once HR sets you as their manager."
        (rowClick)="open($event)"
      >
        <ng-template gggsCell="status" let-row>
          <gggs-status-badge [status]="row.status" />
        </ng-template>
      </gggs-data-table>
    }
  `,
  styles: `
    .team__kpis {
      margin-block-end: var(--gggs-space-4);
    }
  `,
})
export class TeamPage {
  private readonly api = inject(EmployeeDirectoryApi);
  private readonly router = inject(Router);

  protected readonly columns: readonly DataTableColumn<EmployeeRow>[] = [
    { field: 'employeeCode', header: 'Code', width: '8rem' },
    { field: 'displayName', header: 'Name' },
    { field: 'designation', header: 'Designation' },
    { field: 'departmentName', header: 'Department' },
    { field: 'locationName', header: 'Location' },
    { field: 'joiningDate', header: 'Joined', format: 'date', width: '8rem' },
    { field: 'status', header: 'Status', format: 'status', width: '8rem' },
  ];

  protected readonly result = signal<PagedResult<EmployeeRow> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly onNotice = signal<number | null>(null);

  constructor() {
    void this.load();
  }

  protected open(row: EmployeeRow): void {
    void this.router.navigate(['/employees', row.id]);
  }

  private async load(): Promise<void> {
    try {
      const me = await this.api.me();
      const result = await this.api.list({ managerEmployeeId: me.id, pageSize: 200 });
      this.result.set(result);
      this.onNotice.set(result.items.filter((row) => row.status === 'OnNotice').length);
    } catch (error) {
      this.error.set(
        error instanceof ApiError && error.status === 404 ? 'Your account is not linked to an employee record.' : error instanceof ApiError ? error.message : 'Try again in a moment.',
      );
    } finally {
      this.loading.set(false);
    }
  }
}
