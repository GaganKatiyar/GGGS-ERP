import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { TimesheetsApi, addDaysIso, isoToday } from '@gggs/shared/delivery';
import type { HourCategory, TimesheetTarget, TimesheetView } from '@gggs/shared/models';
import { HOUR_CATEGORIES, humanize } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/** One line of the week: a project (and task), or a non-project category, with hours for each day. */
interface GridRow {
  key: number;
  projectId: string | null;
  taskId: string | null;
  category: HourCategory;
  activity: string;
  description: string;
  hours: (number | null)[];
}

const mondayOf = (iso: string): string => {
  const [year, month, day] = iso.split('-').map(Number);
  const date = new Date(year, month - 1, day);
  return addDaysIso(iso, -((date.getDay() + 6) % 7));
};

/**
 * My timesheet (spec §70-71): the week as a grid - one line per project, task and kind of hour -
 * checked against the company's rules as it is filled in, then submitted for approval.
 */
@Component({
  selector: 'gggs-my-timesheet-page',
  imports: [DatePipe, DecimalPipe, PageHeader, StatusBadge, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My timesheet" subtitle="Book the week against your projects, then submit it for approval.">
      <div class="toolbar">
        <button type="button" class="btn btn--sm" aria-label="Previous week" (click)="shift(-7)"><i class="pi pi-chevron-left" aria-hidden="true"></i></button>
        <strong class="ts__week">{{ weekStart() | date: 'd MMM' }} – {{ weekEnd() | date: 'd MMM y' }}</strong>
        <button type="button" class="btn btn--sm" aria-label="Next week" (click)="shift(7)"><i class="pi pi-chevron-right" aria-hidden="true"></i></button>
        <button type="button" class="btn btn--link btn--sm" (click)="goTo(isoToday())">This week</button>
      </div>
    </gggs-page-header>

    @if (view(); as v) {
      <section class="panel stack">
        <div class="toolbar">
          <gggs-status-badge [status]="v.timesheet.status" />
          @if (v.timesheet.isLocked) {
            <gggs-status-badge status="Locked" />
          }
          @if (v.timesheet.isLate) {
            <span class="muted">submitted late</span>
          }
          @if (v.timesheet.lastCorrectionReason) {
            <span class="muted">Reopened: {{ v.timesheet.lastCorrectionReason }}</span>
          }
          <span class="ts__spacer"></span>
          <span><strong>{{ total() | number: '1.0-2' }}</strong> h · {{ billable() | number: '1.0-2' }} billable{{ v.overtimeHours ? ' · ' + v.overtimeHours + ' overtime' : '' }}</span>
        </div>

        <div class="ts__scroll">
          <table class="ts__grid">
            <thead>
              <tr>
                <th scope="col" class="ts__what">Project / task</th>
                <th scope="col">Kind</th>
                @for (day of v.days; track day.date) {
                  <th scope="col" class="ts__day" [class.ts__day--off]="!day.isWorkingDay || !!day.holiday">
                    {{ day.date | date: 'EEE d' }}
                    @if (day.holiday) {
                      <span class="ts__note">{{ day.holiday }}</span>
                    } @else if (day.leave) {
                      <span class="ts__note">{{ day.leave }}</span>
                    }
                  </th>
                }
                <th scope="col">Total</th>
                <th scope="col"><span class="ts__hidden">Remove</span></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.key; let index = $index) {
                <tr>
                  <td class="ts__what">
                    <select aria-label="Project" [value]="row.projectId ?? ''" [disabled]="!editable()" (change)="setProject(index, $any($event.target).value)">
                      <option value="">No project</option>
                      @for (target of v.targets; track target.projectId) {
                        <option [value]="target.projectId">{{ target.projectCode }} · {{ target.projectName }}</option>
                      }
                    </select>
                    @if (tasksFor(row.projectId).length) {
                      <select aria-label="Task" [value]="row.taskId ?? ''" [disabled]="!editable()" (change)="patch(index, 'taskId', $any($event.target).value || null)">
                        <option value="">No task</option>
                        @for (task of tasksFor(row.projectId); track task.taskId) {
                          <option [value]="task.taskId">{{ task.code }} {{ task.title }}</option>
                        }
                      </select>
                    }
                    <input type="text" maxlength="500" placeholder="What you did" aria-label="Description" [value]="row.description" [disabled]="!editable()" (input)="patch(index, 'description', $any($event.target).value)" />
                  </td>
                  <td>
                    <select aria-label="Kind of hours" [value]="row.category" [disabled]="!editable()" (change)="patch(index, 'category', $any($event.target).value)">
                      @for (category of categories; track category) {
                        <option [value]="category">{{ humanize(category) }}</option>
                      }
                    </select>
                  </td>
                  @for (day of v.days; track day.date; let dayIndex = $index) {
                    <td class="ts__cell">
                      <input
                        type="number"
                        min="0"
                        max="24"
                        step="0.25"
                        [attr.aria-label]="'Hours on ' + day.date"
                        [value]="row.hours[dayIndex] ?? ''"
                        [disabled]="!editable()"
                        (input)="setHours(index, dayIndex, $any($event.target).value)"
                      />
                    </td>
                  }
                  <td class="ts__total">{{ rowTotal(row) | number: '1.0-2' }}</td>
                  <td>
                    @if (editable()) {
                      <button type="button" class="btn btn--link btn--sm" aria-label="Remove line" (click)="removeRow(index)"><i class="pi pi-times" aria-hidden="true"></i></button>
                    }
                  </td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <th scope="row" colspan="2">Day total</th>
                @for (day of v.days; track day.date; let dayIndex = $index) {
                  <td class="ts__total" [class.ts__over]="dayTotal(dayIndex) > v.rules.maxHoursPerDay">{{ dayTotal(dayIndex) | number: '1.0-2' }}</td>
                }
                <td class="ts__total">{{ total() | number: '1.0-2' }}</td>
                <td></td>
              </tr>
            </tfoot>
          </table>
        </div>
        @if (rows().length === 0) {
          <gggs-empty-state icon="pi pi-stopwatch" title="Nothing booked this week" [description]="v.targets.length ? 'Add a line for each project you worked on.' : 'You are not on any project this week; book internal, training or bench time.'" />
        }

        @if (editable()) {
          <div class="toolbar">
            <button type="button" class="btn btn--sm" (click)="addRow()"><i class="pi pi-plus" aria-hidden="true"></i> Add line</button>
            @if (rows().length === 0) {
              <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="copyPrevious()">Copy last week</button>
            }
            <span class="ts__spacer"></span>
            <button type="button" class="btn" [disabled]="busy()" (click)="save()">Save</button>
            <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="submit()">Submit week</button>
          </div>
        } @else if (v.timesheet.status === 'Submitted') {
          <div class="toolbar">
            <span class="muted">With your approver since {{ v.timesheet.submittedDate | date: 'd MMM, HH:mm' }}.</span>
            <span class="ts__spacer"></span>
            <button type="button" class="btn" [disabled]="busy()" (click)="withdraw()">Withdraw to edit</button>
          </div>
        }

        @if (v.issues.length) {
          <ul class="ts__issues">
            @for (issue of v.issues; track issue.code + (issue.date ?? '')) {
              <li [class]="'ts__issue ts__issue--' + issue.level.toLowerCase()">{{ issue.message }}</li>
            }
          </ul>
        }
        @if (message()) {
          <p class="ts__message" role="status">{{ message() }}</p>
        }
        @if (error()) {
          <p class="field__error" role="alert">{{ error() }}</p>
        }
        <p class="muted ts__rules">
          Rules: up to {{ v.rules.maxHoursPerDay }} h a day, {{ v.rules.minWeeklyHours }} h a week expected{{ v.rules.enforceWeeklyMinimum ? ' (required)' : '' }};
          {{ v.rules.allowWeekendEntries ? 'weekend hours allowed' : 'no weekend hours' }}; due {{ v.rules.submissionDeadlineDays }} day(s) after the week ends.
        </p>
      </section>
    } @else {
      <gggs-skeleton [lines]="8" />
    }
  `,
  styles: `
    .ts__week {
      min-width: 12rem;
      text-align: center;
    }

    .ts__spacer {
      flex: 1;
    }

    .ts__scroll {
      overflow-x: auto;
    }

    .ts__grid {
      width: 100%;
      border-collapse: collapse;
      min-width: 60rem;
    }

    .ts__grid th,
    .ts__grid td {
      padding: var(--gggs-space-1);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
      vertical-align: top;
    }

    .ts__what {
      min-width: 18rem;
    }

    .ts__what select,
    .ts__what input {
      display: block;
      width: 100%;
      margin-block-end: 2px;
    }

    .ts__day {
      width: 5rem;
      font-size: var(--gggs-font-size-sm);
    }

    .ts__day--off {
      background: var(--gggs-color-surface-sunken, transparent);
      color: var(--gggs-color-text-muted);
    }

    .ts__note {
      display: block;
      font-size: var(--gggs-font-size-xs);
      font-weight: normal;
    }

    .ts__cell input {
      width: 4.5rem;
    }

    .ts__total {
      font-weight: var(--gggs-font-weight-semibold);
      text-align: end;
    }

    .ts__over {
      color: var(--gggs-color-danger, #dc2626);
    }

    .ts__issues {
      margin: 0;
      padding-inline-start: var(--gggs-space-4);
    }

    .ts__issue--error {
      color: var(--gggs-color-danger, #dc2626);
    }

    .ts__issue--warning {
      color: var(--gggs-color-warning, #b45309);
    }

    .ts__issue--info {
      color: var(--gggs-color-text-muted);
    }

    .ts__message {
      color: var(--gggs-color-success);
    }

    .ts__rules {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .ts__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class MyTimesheetPage {
  private readonly api = inject(TimesheetsApi);
  private readonly router = inject(Router);

  /** Bound from the `week` query parameter: a reminder links straight to the week it is about. */
  readonly week = input<string | undefined>(undefined);

  protected readonly categories = HOUR_CATEGORIES;
  protected readonly humanize = humanize;
  protected readonly isoToday = isoToday;
  protected readonly weekStart = signal(mondayOf(isoToday()));
  protected readonly weekEnd = computed(() => addDaysIso(this.weekStart(), 6));
  protected readonly view = signal<TimesheetView | null>(null);
  protected readonly rows = signal<readonly GridRow[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  private nextKey = 0;

  protected readonly editable = computed(() => {
    const sheet = this.view()?.timesheet;
    return !!sheet && !sheet.isLocked && (sheet.status === 'Draft' || sheet.status === 'Rejected');
  });

  protected readonly total = computed(() => this.rows().reduce((sum, row) => sum + this.rowTotal(row), 0));
  protected readonly billable = computed(() => this.rows().filter((row) => row.category === 'Billable').reduce((sum, row) => sum + this.rowTotal(row), 0));

  constructor() {
    effect(() => {
      const requested = this.week();
      if (requested) {
        this.weekStart.set(mondayOf(requested));
      }
    });
    effect(() => {
      void this.load(this.weekStart());
    });
  }

  protected shift(days: number): void {
    this.goTo(addDaysIso(this.weekStart(), days));
  }

  protected goTo(day: string): void {
    const monday = mondayOf(day);
    this.weekStart.set(monday);
    void this.router.navigate([], { queryParams: { week: monday }, replaceUrl: true });
  }

  protected tasksFor(projectId: string | null): TimesheetTarget['tasks'] {
    return this.view()?.targets.find((target) => target.projectId === projectId)?.tasks ?? [];
  }

  protected rowTotal(row: GridRow): number {
    return row.hours.reduce<number>((sum, hours) => sum + (hours ?? 0), 0);
  }

  protected dayTotal(day: number): number {
    return this.rows().reduce((sum, row) => sum + (row.hours[day] ?? 0), 0);
  }

  protected addRow(): void {
    const target = this.view()?.targets[0];
    this.rows.update((rows) => [
      ...rows,
      { key: this.nextKey++, projectId: target?.projectId ?? null, taskId: null, category: target ? (target.billable ? 'Billable' : 'NonBillable') : 'Internal', activity: '', description: '', hours: Array(7).fill(null) },
    ]);
  }

  protected removeRow(index: number): void {
    this.rows.update((rows) => rows.filter((_, at) => at !== index));
  }

  protected setProject(index: number, projectId: string): void {
    const target = this.view()?.targets.find((candidate) => candidate.projectId === projectId);
    this.rows.update((rows) =>
      rows.map((row, at) =>
        at === index
          ? { ...row, projectId: projectId || null, taskId: null, category: projectId ? (target?.billable ? 'Billable' : 'NonBillable') : row.category === 'Billable' || row.category === 'NonBillable' ? 'Internal' : row.category }
          : row,
      ),
    );
  }

  protected patch<K extends keyof GridRow>(index: number, field: K, value: GridRow[K]): void {
    this.rows.update((rows) => rows.map((row, at) => (at === index ? { ...row, [field]: value } : row)));
  }

  protected setHours(index: number, day: number, value: string): void {
    const hours = value === '' ? null : Number(value);
    this.rows.update((rows) => rows.map((row, at) => (at === index ? { ...row, hours: row.hours.map((current, d) => (d === day ? hours : current)) } : row)));
  }

  protected async save(): Promise<boolean> {
    return this.run(async () => {
      this.apply(await this.api.saveWeek(this.weekStart(), this.entries(), null));
      this.message.set('Saved.');
    });
  }

  protected async submit(): Promise<void> {
    if (!(await this.save())) {
      return;
    }

    await this.run(async () => {
      const result = await this.api.submit(this.weekStart());
      this.message.set(result.status === 'Approved' ? 'Submitted and approved.' : 'Submitted for approval.');
      await this.load(this.weekStart());
    });
  }

  protected async withdraw(): Promise<void> {
    await this.run(async () => {
      await this.api.withdraw(this.weekStart());
      this.message.set('Withdrawn: the week is a draft again.');
      await this.load(this.weekStart());
    });
  }

  protected async copyPrevious(): Promise<void> {
    await this.run(async () => this.apply(await this.api.copyPrevious(this.weekStart())));
  }

  private entries() {
    const days = this.view()?.days ?? [];
    return this.rows().flatMap((row) =>
      row.hours
        .map((hours, day) => ({ hours, day }))
        .filter((cell) => (cell.hours ?? 0) > 0)
        .map((cell) => ({
          date: days[cell.day].date,
          projectId: row.projectId,
          taskId: row.taskId,
          category: row.category,
          hours: cell.hours ?? 0,
          activity: row.activity || null,
          description: row.description || null,
        })),
    );
  }

  private apply(view: TimesheetView): void {
    this.view.set(view);
    const grouped = new Map<string, GridRow>();
    for (const entry of view.entries) {
      const key = [entry.projectId ?? '', entry.taskId ?? '', entry.category, entry.activity ?? '', entry.description ?? ''].join('|');
      let row = grouped.get(key);
      if (!row) {
        row = { key: this.nextKey++, projectId: entry.projectId ?? null, taskId: entry.taskId ?? null, category: entry.category, activity: entry.activity ?? '', description: entry.description ?? '', hours: Array(7).fill(null) };
        grouped.set(key, row);
      }
      const day = view.days.findIndex((candidate) => candidate.date === entry.date);
      if (day >= 0) {
        row.hours[day] = (row.hours[day] ?? 0) + entry.hours;
      }
    }
    this.rows.set([...grouped.values()]);
  }

  private async run(action: () => Promise<void>): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await action();
      return true;
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  private async load(week: string): Promise<void> {
    this.error.set(null);
    try {
      this.apply(await this.api.week(week));
    } catch (error) {
      this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : error instanceof ApiError ? error.message : 'The timesheet could not be loaded.');
    }
  }
}
