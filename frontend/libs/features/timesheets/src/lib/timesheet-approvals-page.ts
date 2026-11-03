import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { TimesheetsApi } from '@gggs/shared/delivery';
import type { TimesheetApprovalRow, TimesheetApprovalStatus, TimesheetView } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { Drawer, EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

interface ProjectGroup {
  readonly key: string;
  readonly label: string;
  readonly hours: number;
  readonly rows: readonly TimesheetApprovalRow[];
}

const OTHER_TIME = 'Non-project time';

/**
 * Timesheet approvals: the hours waiting on the signed-in approver, by project. Each project manager sees
 * their own project's hours and no one else's; decisions are made in the approvals inbox like any other.
 */
@Component({
  selector: 'gggs-timesheet-approvals-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, EmptyState, Skeleton, Drawer],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Timesheet approvals" subtitle="Hours waiting for a decision, by project. Approve or send back from your approvals inbox.">
      <a class="btn btn--primary" routerLink="/approvals">Open approvals inbox</a>
    </gggs-page-header>

    <section class="panel toolbar ts-appr__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="setStatus($any($event.target).value)">
          @for (option of statuses; track option) {
            <option [value]="option" [selected]="option === status()">{{ labelFor(option) }}</option>
          }
        </select>
      </label>
    </section>

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (rows(); as list) {
      @for (group of groups(); track group.key) {
        <section class="panel stack ts-appr__group">
          <h2 class="ts-appr__heading">{{ group.label }} <span class="muted">{{ group.hours | number: '1.0-2' }} h</span></h2>
          <div class="ts-appr__scroll" tabindex="0" role="region" [attr.aria-label]="group.label">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">Person</th>
                  <th scope="col">Week</th>
                  <th scope="col" class="ts-appr__num">Hours</th>
                  <th scope="col" class="ts-appr__num">Billable</th>
                  <th scope="col">Approver</th>
                  <th scope="col">Status</th>
                  <th scope="col" aria-label="Actions"></th>
                </tr>
              </thead>
              <tbody>
                @for (row of group.rows; track row.id) {
                  <tr>
                    <td>{{ row.employeeName ?? row.employeeCode }} <span class="muted">{{ row.employeeCode }}</span></td>
                    <td>{{ row.weekStart | date: 'd MMM y' }}</td>
                    <td class="ts-appr__num">{{ row.hours | number: '1.2-2' }}</td>
                    <td class="ts-appr__num">{{ row.billableHours | number: '1.2-2' }}</td>
                    <td>{{ row.ownerName ?? '—' }}</td>
                    <td><gggs-status-badge [status]="row.status" /></td>
                    <td class="ts-appr__actions">
                      <button type="button" class="btn btn--link btn--sm" (click)="view(row.timesheetId)">View hours</button>
                      @if (row.status === 'Submitted') {
                        <a class="btn btn--link btn--sm" routerLink="/approvals">Decide</a>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      } @empty {
        <section class="panel">
          <gggs-empty-state icon="pi pi-stopwatch" [title]="status() === 'Submitted' ? 'Nothing waiting for approval' : 'No hours match'" />
        </section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }

    <gggs-drawer [(visible)]="drawerOpen" [title]="detail() ? (detail()?.timesheet?.employeeName ?? 'Timesheet') + ' · week of ' + (detail()?.timesheet?.weekStart | date: 'd MMM y') : 'Timesheet'" width="min(48rem, 100vw)">
      @if (detail(); as d) {
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Day</th>
              <th scope="col">Project / task</th>
              <th scope="col">Kind</th>
              <th scope="col" class="ts-appr__num">Hours</th>
              <th scope="col">Note</th>
            </tr>
          </thead>
          <tbody>
            @for (entry of d.entries; track entry.id) {
              <tr>
                <td>{{ entry.date | date: 'EEE d MMM' }}</td>
                <td>{{ entry.projectCode ?? '—' }} {{ entry.taskCode ?? '' }}</td>
                <td>{{ humanize(entry.category) }}</td>
                <td class="ts-appr__num">{{ entry.hours | number: '1.0-2' }}</td>
                <td class="muted">{{ entry.description ?? '' }}</td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <gggs-skeleton [lines]="5" />
      }
    </gggs-drawer>
  `,
  styles: `
    .ts-appr__filters {
      margin-block-end: var(--gggs-space-3);
    }

    .ts-appr__group {
      margin-block-end: var(--gggs-space-3);
    }

    .ts-appr__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .ts-appr__scroll {
      overflow-x: auto;
    }

    .ts-appr__num {
      text-align: end;
    }

    .ts-appr__actions {
      white-space: nowrap;
    }
  `,
})
export class TimesheetApprovalsPage {
  private readonly api = inject(TimesheetsApi);

  /** Bound from the query string: a task in the inbox links to the week it is about. */
  readonly open = input<string | undefined>(undefined);

  protected readonly statuses: readonly TimesheetApprovalStatus[] = ['Submitted', 'Approved', 'Rejected', 'Returned'];
  protected readonly humanize = humanize;
  protected readonly status = signal<TimesheetApprovalStatus>('Submitted');
  protected readonly rows = signal<readonly TimesheetApprovalRow[] | null>(null);
  protected readonly detail = signal<TimesheetView | null>(null);
  protected readonly drawerOpen = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly groups = computed<readonly ProjectGroup[]>(() => {
    const groups = new Map<string, TimesheetApprovalRow[]>();
    for (const row of this.rows() ?? []) {
      const key = row.projectId ?? '';
      groups.set(key, [...(groups.get(key) ?? []), row]);
    }

    return [...groups].map(([key, rows]) => ({
      key,
      label: rows[0].projectCode ? `${rows[0].projectCode} · ${rows[0].projectName}` : OTHER_TIME,
      hours: rows.reduce((sum, row) => sum + row.hours, 0),
      rows,
    }));
  });

  constructor() {
    void this.load();
    effect(() => {
      const week = this.open();
      if (week) {
        void this.view(week);
      }
    });
  }

  protected labelFor(status: TimesheetApprovalStatus): string {
    return status === 'Submitted' ? 'Waiting for a decision' : status === 'Returned' ? 'Returned to the person' : humanize(status);
  }

  protected setStatus(status: TimesheetApprovalStatus): void {
    this.status.set(status);
    void this.load();
  }

  protected async view(timesheetId: string): Promise<void> {
    this.detail.set(null);
    this.drawerOpen.set(true);
    try {
      this.detail.set(await this.api.get(timesheetId));
    } catch (error) {
      this.drawerOpen.set(false);
      this.error.set(error instanceof ApiError ? error.message : 'Those hours could not be loaded.');
    }
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      this.rows.set((await this.api.approvals({ status: this.status(), pageSize: 100 })).items);
    } catch (error) {
      this.rows.set(null);
      this.error.set(error instanceof ApiError ? error.message : 'Timesheet approvals could not be loaded.');
    }
  }
}
