import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { TimesheetsApi, addDaysIso, isoToday } from '@gggs/shared/delivery';
import type { MissingTimesheet, TimesheetRow, TimesheetStatus, TimesheetView } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { Drawer, EmptyState, FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const mondayOf = (iso: string): string => {
  const [year, month, day] = iso.split('-').map(Number);
  return addDaysIso(iso, -((new Date(year, month - 1, day).getDay() + 6) % 7));
};

/**
 * Team timesheets (spec §72): the week for everyone in scope - who has submitted, who is missing,
 * what is waiting for approval - and the corrections an approved week sometimes needs. With
 * `approvals` set (from the route) it opens on the weeks waiting for a decision.
 */
@Component({
  selector: 'gggs-team-timesheets-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, EmptyState, Drawer, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      [title]="approvals() ? 'Timesheet approvals' : 'Team timesheets'"
      [subtitle]="approvals() ? 'Weeks waiting for a decision. Approve or send back from your approvals inbox.' : 'Who has submitted the week, who has not, and what each person booked.'"
    >
      @if (approvals()) {
        <a class="btn btn--primary" routerLink="/approvals">Open approvals inbox</a>
      }
    </gggs-page-header>

    <section class="panel toolbar team-ts__filters">
      <label class="field">
        <span class="field__label">Week</span>
        <input type="date" [value]="week()" (change)="setWeek($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
      <label class="field team-ts__all">
        <input type="checkbox" [checked]="allWeeks()" (change)="allWeeks.set($any($event.target).checked); load()" />
        <span>All weeks</span>
      </label>
    </section>

    <section class="panel stack">
      <h2 class="team-ts__heading">Timesheets <span class="muted">({{ total() }})</span></h2>
      @for (row of rows(); track row.id) {
        <button type="button" class="team-ts__row" (click)="open(row)">
          <span><strong>{{ row.employeeName ?? row.employeeCode }}</strong> <span class="muted">{{ row.employeeCode }}</span></span>
          <span>Week of {{ row.weekStart | date: 'd MMM y' }}</span>
          <span>{{ row.totalHours | number: '1.0-2' }} h · {{ row.billableHours | number: '1.0-2' }} billable</span>
          <span class="muted">{{ row.isLate ? 'late' : '' }}{{ row.correctionCount ? ' · corrected ×' + row.correctionCount : '' }}</span>
          <gggs-status-badge [status]="row.status" />
        </button>
      } @empty {
        <gggs-empty-state icon="pi pi-stopwatch" [title]="approvals() ? 'Nothing waiting for approval' : 'No timesheets match'" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    @if (!approvals() && !allWeeks()) {
      <section class="panel stack">
        <h2 class="team-ts__heading">Not yet submitted <span class="muted">({{ missing().length }})</span></h2>
        @for (person of missing(); track person.employeeId) {
          <div class="team-ts__row team-ts__row--static">
            <span><strong>{{ person.employeeName ?? person.employeeCode }}</strong> <span class="muted">{{ person.employeeCode }}</span></span>
            <span class="muted">{{ person.designation ?? '' }}</span>
            <span></span>
            <span></span>
            <gggs-status-badge [status]="person.status ?? 'Missing'" />
          </div>
        } @empty {
          <p class="muted">Everyone in scope has submitted this week.</p>
        }
      </section>
    }

    <gggs-drawer [(visible)]="drawerOpen" [title]="detail() ? (detail()?.timesheet?.employeeName ?? 'Timesheet') + ' · week of ' + (detail()?.timesheet?.weekStart | date: 'd MMM y') : 'Timesheet'" width="min(48rem, 100vw)">
      @if (detail(); as d) {
        <div class="stack">
          <div class="toolbar">
            <gggs-status-badge [status]="d.timesheet.status" />
            @if (d.timesheet.isLocked) {
              <gggs-status-badge status="Locked" />
            }
            <span><strong>{{ d.timesheet.totalHours | number: '1.0-2' }}</strong> h · {{ d.timesheet.billableHours | number: '1.0-2' }} billable{{ d.overtimeHours ? ' · ' + d.overtimeHours + ' overtime' : '' }}</span>
          </div>
          <table class="team-ts__table">
            <thead>
              <tr>
                <th scope="col">Day</th>
                <th scope="col">Project / task</th>
                <th scope="col">Kind</th>
                <th scope="col">Hours</th>
                <th scope="col">Description</th>
              </tr>
            </thead>
            <tbody>
              @for (entry of d.entries; track entry.id) {
                <tr>
                  <td>{{ entry.date | date: 'EEE d MMM' }}</td>
                  <td>{{ entry.projectCode ?? '—' }} {{ entry.taskCode ?? '' }}</td>
                  <td>{{ humanize(entry.category) }}</td>
                  <td>{{ entry.hours | number: '1.0-2' }}</td>
                  <td class="muted">{{ entry.description ?? '' }}</td>
                </tr>
              }
            </tbody>
          </table>
          @if (d.issues.length) {
            <ul class="team-ts__issues">
              @for (issue of d.issues; track issue.code + (issue.date ?? '')) {
                <li>{{ issue.level }}: {{ issue.message }}</li>
              }
            </ul>
          }
          @if (d.timesheet.comment) {
            <p><strong>Comment:</strong> {{ d.timesheet.comment }}</p>
          }
          @if (d.timesheet.workflowInstanceId && d.timesheet.status === 'Submitted') {
            <a class="btn" routerLink="/approvals">Decide in approvals inbox</a>
          }
          @if (canReopen() && d.timesheet.status === 'Approved' && !d.timesheet.isLocked) {
            <button type="button" class="btn" (click)="reopenOpen.set(true)">Reopen for correction</button>
          }
        </div>
      }
    </gggs-drawer>

    <gggs-form-dialog [(visible)]="reopenOpen" title="Reopen the week" saveLabel="Reopen" [busy]="busy()" [error]="reopenError()" [saveDisabled]="!reason().trim()" (save)="reopen()">
      <p class="muted">The week goes back to the employee as a draft. The hours already counted on tasks are taken off until it is approved again.</p>
      <label class="field">
        <span class="field__label">Reason</span>
        <textarea rows="3" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styles: `
    .team-ts__filters {
      margin-block-end: var(--gggs-space-3);
      align-items: end;
    }

    .team-ts__all {
      flex-direction: row;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    .team-ts__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .team-ts__row {
      display: grid;
      grid-template-columns: 2fr 1.3fr 1.6fr 1.2fr 7rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding: var(--gggs-space-2) 0;
      border: 0;
      border-block-end: 1px solid var(--gggs-color-border);
      background: none;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .team-ts__row--static {
      cursor: default;
    }

    .team-ts__row:hover {
      background: var(--gggs-color-surface-hover, transparent);
    }

    .team-ts__table {
      width: 100%;
      border-collapse: collapse;
    }

    .team-ts__table th,
    .team-ts__table td {
      padding: var(--gggs-space-1);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .team-ts__issues {
      margin: 0;
      padding-inline-start: var(--gggs-space-4);
    }

    @media (max-width: 56rem) {
      .team-ts__row {
        grid-template-columns: 1fr 1fr;
      }
    }
  `,
})
export class TeamTimesheetsPage {
  private readonly api = inject(TimesheetsApi);
  private readonly permissions = inject(PermissionStore);

  /** From the route's data: the approvals view of the same list. */
  readonly approvals = input(false);

  protected readonly statuses: readonly TimesheetStatus[] = ['Draft', 'Submitted', 'Approved', 'Rejected'];
  protected readonly humanize = humanize;
  protected readonly week = signal(mondayOf(isoToday()));
  protected readonly status = signal<TimesheetStatus | ''>('');
  protected readonly allWeeks = signal(false);
  protected readonly rows = signal<readonly TimesheetRow[]>([]);
  protected readonly total = signal(0);
  protected readonly missing = signal<readonly MissingTimesheet[]>([]);
  protected readonly detail = signal<TimesheetView | null>(null);
  protected readonly drawerOpen = signal(false);
  protected readonly reopenOpen = signal(false);
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly reopenError = signal<string | null>(null);
  protected readonly canReopen = computed(() => this.permissions.has('timesheet.entry.approve'));

  constructor() {
    effect(() => {
      if (this.approvals()) {
        this.status.set('Submitted');
        this.allWeeks.set(true);
      }
      void this.load();
    });
  }

  protected setWeek(day: string): void {
    if (day) {
      this.week.set(mondayOf(day));
      this.allWeeks.set(false);
      void this.load();
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      const weekStart = this.allWeeks() ? undefined : this.week();
      const [page, missing] = await Promise.all([
        this.api.list({ weekStart, status: this.status(), pageSize: 100 }),
        this.approvals() || this.allWeeks() ? Promise.resolve([]) : this.api.missing(this.week()),
      ]);
      this.rows.set(page.items);
      this.total.set(page.totalCount ?? page.items.length);
      this.missing.set(missing);
    } catch (error) {
      this.error.set(message(error, 'Timesheets could not be loaded.'));
    }
  }

  protected async open(row: TimesheetRow): Promise<void> {
    this.detail.set(null);
    this.drawerOpen.set(true);
    try {
      this.detail.set(await this.api.get(row.id));
    } catch (error) {
      this.error.set(message(error, 'The timesheet could not be loaded.'));
      this.drawerOpen.set(false);
    }
  }

  protected async reopen(): Promise<void> {
    const id = this.detail()?.timesheet.id;
    if (!id) {
      return;
    }

    this.busy.set(true);
    this.reopenError.set(null);
    try {
      await this.api.reopen(id, this.reason().trim());
      this.reopenOpen.set(false);
      this.reason.set('');
      this.detail.set(await this.api.get(id));
      await this.load();
    } catch (error) {
      this.reopenError.set(message(error, 'The week could not be reopened.'));
    } finally {
      this.busy.set(false);
    }
  }
}
