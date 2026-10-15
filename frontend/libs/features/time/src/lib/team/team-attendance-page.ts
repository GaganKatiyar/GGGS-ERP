import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { DataTable, DataTableCell, type DataTableColumn } from '@gggs/shared/data-table';
import type { AttendanceRow, AttendanceSummary, PagedResult } from '@gggs/shared/models';
import { formatMinutes, shortTime, toAttendanceRows } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { AttendanceApi, todayIso } from '@gggs/shared/time';
import { EmptyState, KpiCard, PageHeader, StatusBadge } from '@gggs/shared/ui';

/**
 * A day across my team (or, with wider access, a department): who is in, who is late, who is off.
 * Shared by managers (`/team/attendance`) and HR (`/attendance`).
 */
@Component({
  selector: 'gggs-team-attendance-page',
  imports: [ReactiveFormsModule, PageHeader, KpiCard, DataTable, DataTableCell, StatusBadge, OrgSelect, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Team attendance" subtitle="Who is in today, and how each day counted." />

    <section class="panel toolbar team__filters">
      <label class="field">
        <span class="field__label">Day</span>
        <input type="date" [max]="today" [value]="date()" (change)="setDate($event)" />
      </label>
      <label class="field team__department">
        <span class="field__label">Department</span>
        <gggs-org-select kind="departments" placeholder="Everyone I can see" ariaLabel="Department" [formControl]="department" />
      </label>
    </section>

    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="Attendance could not be loaded" [description]="error()!" />
    } @else {
      <div class="kpi-row team__kpis">
        <gggs-kpi-card label="People" icon="pi pi-users" [value]="summary()?.people ?? null" />
        <gggs-kpi-card label="Present" icon="pi pi-check" [value]="count('Present')" />
        <gggs-kpi-card label="Late" icon="pi pi-clock" [value]="summary()?.late ?? null" />
        <gggs-kpi-card label="On leave" icon="pi pi-calendar" [value]="count('OnLeave')" />
        <gggs-kpi-card label="Absent" icon="pi pi-times" [value]="count('Absent')" />
      </div>

      <gggs-data-table
        [result]="result()"
        [columns]="columns"
        [loading]="loading()"
        exportName="attendance"
        emptyTitle="Nobody to show"
        emptyDescription="Choose another day or department."
        (rowClick)="open($event)"
      >
        <ng-template gggsCell="firstIn" let-row>{{ shortTime(row.firstIn) || '—' }}</ng-template>
        <ng-template gggsCell="lastOut" let-row>{{ shortTime(row.lastOut) || '—' }}</ng-template>
        <ng-template gggsCell="workedMinutes" let-row>{{ formatMinutes(row.workedMinutes) || '—' }}</ng-template>
        <ng-template gggsCell="lateMinutes" let-row>{{ formatMinutes(row.lateMinutes) }}</ng-template>
        <ng-template gggsCell="status" let-row>
          <gggs-status-badge [status]="row.status" />
          @if (row.note) {
            <span class="muted team__note">{{ row.note }}</span>
          }
        </ng-template>
      </gggs-data-table>
    }
  `,
  styles: `
    .team__filters {
      align-items: flex-end;
      margin-block-end: var(--gggs-space-3);
    }

    .team__department {
      min-width: 16rem;
    }

    .team__kpis {
      margin-block-end: var(--gggs-space-3);
    }

    .team__note {
      display: block;
      font-size: var(--gggs-font-size-xs);
    }
  `,
})
export class TeamAttendancePage {
  private readonly api = inject(AttendanceApi);
  private readonly router = inject(Router);

  protected readonly today = todayIso();
  protected readonly formatMinutes = formatMinutes;
  protected readonly shortTime = shortTime;

  protected readonly columns: readonly DataTableColumn<AttendanceRow>[] = [
    { field: 'employeeName', header: 'Employee' },
    { field: 'shiftName', header: 'Shift' },
    { field: 'firstIn', header: 'In', width: '6rem' },
    { field: 'lastOut', header: 'Out', width: '6rem' },
    { field: 'workedMinutes', header: 'Worked', width: '7rem' },
    { field: 'lateMinutes', header: 'Late', width: '6rem' },
    { field: 'status', header: 'Status', width: '10rem' },
  ];

  protected readonly date = signal(this.today);
  protected readonly department = new FormControl<string | null>(null);
  protected readonly result = signal<PagedResult<AttendanceRow> | null>(null);
  protected readonly summary = signal<AttendanceSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.department.valueChanges.subscribe(() => void this.load());
    void this.load();
  }

  protected count(status: string): number | null {
    const summary = this.summary();
    return summary ? (summary.byStatus[status as keyof typeof summary.byStatus] ?? 0) : null;
  }

  protected setDate(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.date.set(value);
      void this.load();
    }
  }

  protected open(row: AttendanceRow): void {
    void this.router.navigate(['/employees', row.employeeId]);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const department = this.department.value;
      const [days, summary] = await Promise.all([
        this.api.days({ from: this.date(), to: this.date(), departmentId: department }),
        this.api.summary(this.date(), department),
      ]);
      this.result.set({ items: toAttendanceRows(days), page: 1, pageSize: days.length || 1, totalCount: days.length, totalPages: 1, hasNextPage: false });
      this.summary.set(summary);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    } finally {
      this.loading.set(false);
    }
  }
}
