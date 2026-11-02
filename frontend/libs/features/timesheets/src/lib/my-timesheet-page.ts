import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { TimesheetsApi, addDaysIso, isoToday } from '@gggs/shared/delivery';
import type { HourCategory, TimesheetDay, TimesheetView } from '@gggs/shared/models';
import { BOOKABLE_HOUR_CATEGORIES, humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { CopyTimesheetDialog, type CopiedLines } from './copy-timesheet-dialog';
import { attendanceLabel, blankCells, buildRows, copyInto, dayTotal, groupRows, mondayOf, rowTotal, toEntries, weekTotal, workedLabel, type GridRow } from './timesheet-grid';
import { TimesheetTabs } from './timesheet-tabs';

/** A column of the grid: a day, and where it sits in the week. */
interface Column {
  readonly day: TimesheetDay;
  readonly index: number;
}

/**
 * My timesheet (spec §70-71): everything the person can book to, by project and activity, with a cell
 * for each day. Leave, holidays and office attendance sit beside it for reference: they come from their
 * own modules and are never booked as hours.
 */
@Component({
  selector: 'gggs-my-timesheet-page',
  imports: [DatePipe, DecimalPipe, PageHeader, StatusBadge, EmptyState, Skeleton, FormDialog, CopyTimesheetDialog, TimesheetTabs],
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
    <gggs-timesheet-tabs />

    @if (view(); as v) {
      <section class="panel stack">
        <div class="toolbar ts__summary">
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
          <span>Working hours: <strong>{{ total() | number: '1.2-2' }}</strong></span>
          <span>Leave hours: <strong>{{ leaveHours() | number: '1.2-2' }}</strong></span>
          <span>Holiday hours: <strong>{{ holidayHours() | number: '1.2-2' }}</strong></span>
          @if (v.overtimeHours) {
            <span>Overtime: <strong>{{ v.overtimeHours | number: '1.0-2' }}</strong></span>
          }
          <span class="muted">{{ v.rules.minHoursPerDay | number: '1.0-2' }} hours a day expected</span>
        </div>

        <div class="toolbar">
          @if (editable()) {
            <button type="button" class="btn btn--sm" (click)="copyOpen.set(true)"><i class="pi pi-copy" aria-hidden="true"></i> Copy a timesheet</button>
            <button type="button" class="btn btn--sm" (click)="addOtherTime()">Add non-project time</button>
          }
          <span class="ts__spacer"></span>
          <div class="ts__mode" role="group" aria-label="View">
            <button type="button" class="btn btn--sm" [class.btn--primary]="dayIndex() === null" [attr.aria-pressed]="dayIndex() === null" (click)="dayIndex.set(null)">Week view</button>
            <button type="button" class="btn btn--sm" [class.btn--primary]="dayIndex() !== null" [attr.aria-pressed]="dayIndex() !== null" (click)="dayIndex.set(todayIndex())">Day view</button>
          </div>
          @if (dayIndex() !== null) {
            <select aria-label="Day" [value]="dayIndex()" (change)="dayIndex.set(+$any($event.target).value)">
              @for (day of v.days; track day.date; let index = $index) {
                <option [value]="index" [selected]="index === dayIndex()">{{ day.date | date: 'EEE d MMM' }}</option>
              }
            </select>
          }
        </div>

        <div class="ts__scroll" tabindex="0" role="region" aria-label="Timesheet grid">
          <table class="table ts__grid" [class.ts__grid--day]="dayIndex() !== null">
            <thead>
              <tr>
                <th scope="col" class="ts__what">Project / activity</th>
                <th scope="col" class="ts__task">Task</th>
                @for (column of columns(); track column.day.date) {
                  <th scope="col" class="ts__day" [class.ts__day--off]="!column.day.isWorkingDay || !!column.day.holiday">
                    {{ column.day.date | date: 'EEE' }}
                    <span class="ts__note">{{ column.day.date | date: 'd MMM' }}</span>
                  </th>
                }
                @if (dayIndex() !== null) {
                  <th scope="col">Note</th>
                }
                <th scope="col" class="ts__num">Total</th>
              </tr>
              <tr class="ts__totals">
                <th scope="row" colspan="2" class="ts__num">Week total</th>
                @for (column of columns(); track column.day.date) {
                  <td class="ts__num" [class.ts__over]="dayHours(column.index) > v.rules.maxHoursPerDay">{{ dayGrand(column) | number: '1.2-2' }}</td>
                }
                @if (dayIndex() !== null) {
                  <td></td>
                }
                <td class="ts__num">{{ weekGrand() | number: '1.2-2' }}</td>
              </tr>
            </thead>
            @for (group of groups(); track group.key) {
              <tbody>
                <tr class="ts__group">
                  <th scope="rowgroup" colspan="2">
                    <button type="button" class="btn btn--link btn--sm" [attr.aria-expanded]="!collapsed().has(group.key)" [attr.aria-label]="(collapsed().has(group.key) ? 'Show ' : 'Hide ') + group.label" (click)="toggleGroup(group.key)">
                      <i [class]="collapsed().has(group.key) ? 'pi pi-plus-circle' : 'pi pi-minus-circle'" aria-hidden="true"></i>
                    </button>
                    {{ group.label }}
                    @if (group.isDefault) {
                      <span class="muted ts__tag">Department default</span>
                    }
                  </th>
                  @for (column of columns(); track column.day.date) {
                    <td class="ts__num">{{ groupDay(group.rows, column.index) | number: '1.2-2' }}</td>
                  }
                  @if (dayIndex() !== null) {
                    <td></td>
                  }
                  <td class="ts__num">{{ groupWeek(group.rows) | number: '1.2-2' }}</td>
                </tr>
                @if (!collapsed().has(group.key)) {
                  @for (row of group.rows; track row.id; let first = $first) {
                    <tr>
                      <td class="ts__what">
                        @if (first || row.activityName !== group.rows[$index - 1].activityName) {
                          {{ row.activityName ?? '' }}
                        }
                      </td>
                      <td class="ts__task">
                        {{ row.title }}
                        <select class="ts__kind" [attr.aria-label]="'Kind of hours for ' + row.title" [disabled]="!editable()" (change)="setCategory(row.id, $any($event.target).value)">
                          @for (category of categoriesFor(row); track category) {
                            <option [value]="category" [selected]="category === row.category">{{ humanize(category) }}</option>
                          }
                        </select>
                      </td>
                      @for (column of columns(); track column.day.date) {
                        <td class="ts__cell">
                          <span class="ts__entry">
                            <input
                              type="number"
                              min="0"
                              max="24"
                              step="0.25"
                              [attr.aria-label]="row.title + ', hours on ' + column.day.date"
                              [value]="row.cells[column.index].hours ?? ''"
                              [disabled]="!editable()"
                              (input)="setHours(row.id, column.index, $any($event.target).value)"
                            />
                            @if (dayIndex() === null) {
                              <button
                                type="button"
                                class="ts__note-btn"
                                [class.ts__note-btn--on]="!!row.cells[column.index].note"
                                [attr.aria-label]="'Note for ' + row.title + ' on ' + column.day.date"
                                [title]="row.cells[column.index].note || 'Add a note'"
                                (click)="openNote(row, column)"
                              >
                                <i class="pi pi-comment" aria-hidden="true"></i>
                              </button>
                            }
                          </span>
                        </td>
                      }
                      @if (dayIndex() !== null) {
                        <td><input type="text" maxlength="500" class="ts__day-note" [attr.aria-label]="'Note for ' + row.title" [value]="row.cells[dayIndex()!].note" [disabled]="!editable()" (input)="setNote(row.id, dayIndex()!, $any($event.target).value)" /></td>
                      }
                      <td class="ts__num">{{ rowTotal(row) | number: '1.2-2' }}</td>
                    </tr>
                  }
                }
              </tbody>
            }
            <tfoot>
              <tr>
                <th scope="row" colspan="2">Leave</th>
                @for (column of columns(); track column.day.date) {
                  <td class="ts__num" [title]="column.day.leave ?? ''">{{ column.day.leaveHours | number: '1.2-2' }}</td>
                }
                @if (dayIndex() !== null) {
                  <td></td>
                }
                <td class="ts__num">{{ leaveHours() | number: '1.2-2' }}</td>
              </tr>
              <tr>
                <th scope="row" colspan="2">Holiday</th>
                @for (column of columns(); track column.day.date) {
                  <td class="ts__num" [title]="column.day.holiday ?? ''">{{ column.day.holidayHours | number: '1.2-2' }}</td>
                }
                @if (dayIndex() !== null) {
                  <td></td>
                }
                <td class="ts__num">{{ holidayHours() | number: '1.2-2' }}</td>
              </tr>
              <tr>
                <th scope="row" colspan="2">Your office attendance <span class="muted">(hh:mm)</span></th>
                @for (column of columns(); track column.day.date) {
                  <td class="ts__num ts__attendance">
                    {{ attendanceLabel(column.day) }}
                    <span class="ts__note">{{ workedLabel(column.day.workedMinutes) }}</span>
                  </td>
                }
                @if (dayIndex() !== null) {
                  <td></td>
                }
                <td></td>
              </tr>
            </tfoot>
          </table>
        </div>

        @if (groups().length === 0) {
          <gggs-empty-state icon="pi pi-stopwatch" title="Nothing to book to yet" description="Your department has no default project and you are on no project. Add non-project time, or ask your administrator." />
        }

        @if (editable()) {
          <div class="toolbar">
            <button type="button" class="btn" [disabled]="busy()" (click)="load(weekStart())">Cancel</button>
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
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
        <p class="muted ts__rules">
          Rules: up to {{ v.rules.maxHoursPerDay }} h a day, {{ v.rules.minWeeklyHours }} h a week expected{{ v.rules.enforceWeeklyMinimum ? ' (required)' : '' }};
          {{ v.rules.allowWeekendEntries ? 'weekend hours allowed' : 'no weekend hours' }}; due {{ v.rules.submissionDeadlineDays }} day(s) after the week ends.
        </p>
      </section>

      <gggs-copy-timesheet-dialog [(visible)]="copyOpen" [targets]="v.targets" [week]="weekStart()" [days]="v.days" (copied)="applyCopy($event)" />
      <gggs-form-dialog [(visible)]="noteOpen" [title]="noteTitle()" [saveDisabled]="!editable()" (save)="saveNote()">
        <label class="field">
          <span class="field__label">Note</span>
          <textarea rows="3" maxlength="500" [value]="noteText()" [disabled]="!editable()" (input)="noteText.set($any($event.target).value)"></textarea>
        </label>
      </gggs-form-dialog>
    } @else if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
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

    .ts__summary {
      flex-wrap: wrap;
    }

    .ts__mode {
      display: inline-flex;
    }

    .ts__scroll {
      overflow: auto;
      max-height: 70vh;
    }

    .ts__grid {
      width: 100%;
      min-width: 66rem;
      border-collapse: separate;
      border-spacing: 0;
    }

    .ts__grid--day {
      min-width: 44rem;
    }

    .ts__grid thead tr > * {
      position: sticky;
      top: 0;
      z-index: 2;
      background: var(--gggs-color-surface);
    }

    .ts__grid .ts__totals > * {
      top: 2.75rem;
      background: var(--gggs-color-success-subtle);
      font-weight: var(--gggs-font-weight-semibold);
    }

    .ts__what {
      min-width: 12rem;
    }

    .ts__task {
      min-width: 13rem;
    }

    .ts__kind {
      display: block;
      width: 100%;
      margin-block-start: 2px;
      font-size: var(--gggs-font-size-xs);
    }

    .ts__group > * {
      background: var(--gggs-color-surface-sunken, transparent);
      font-weight: var(--gggs-font-weight-semibold);
    }

    .ts__tag {
      font-size: var(--gggs-font-size-xs);
      font-weight: normal;
    }

    .ts__day {
      width: 5.5rem;
      font-size: var(--gggs-font-size-sm);
    }

    .ts__day--off {
      color: var(--gggs-color-text-muted);
    }

    .ts__note {
      display: block;
      font-size: var(--gggs-font-size-xs);
      font-weight: normal;
    }

    .ts__num {
      text-align: end;
    }

    .ts__entry {
      display: inline-flex;
      align-items: center;
      gap: 2px;
    }

    .ts__cell input {
      width: 4rem;
    }

    .ts__day-note {
      width: 100%;
    }

    .ts__note-btn {
      padding: 2px;
      border: 0;
      background: none;
      color: var(--gggs-color-text-muted);
      cursor: pointer;
    }

    .ts__note-btn--on {
      color: var(--gggs-color-primary);
    }

    .ts__attendance {
      font-size: var(--gggs-font-size-sm);
    }

    .ts__over {
      color: var(--gggs-color-danger);
    }

    .ts__issues {
      margin: 0;
      padding-inline-start: var(--gggs-space-4);
    }

    .ts__issue--error {
      color: var(--gggs-color-danger);
    }

    .ts__issue--warning {
      color: var(--gggs-color-warning);
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
  `,
})
export class MyTimesheetPage {
  private readonly api = inject(TimesheetsApi);
  private readonly router = inject(Router);

  /** Bound from the `week` query parameter: a reminder links straight to the week it is about. */
  readonly week = input<string | undefined>(undefined);

  protected readonly humanize = humanize;
  protected readonly isoToday = isoToday;
  protected readonly rowTotal = rowTotal;
  protected readonly attendanceLabel = attendanceLabel;
  protected readonly workedLabel = workedLabel;
  protected readonly weekStart = signal(mondayOf(isoToday()));
  protected readonly weekEnd = computed(() => addDaysIso(this.weekStart(), 6));
  protected readonly view = signal<TimesheetView | null>(null);
  protected readonly rows = signal<readonly GridRow[]>([]);
  protected readonly collapsed = signal<ReadonlySet<string>>(new Set());
  /** Which day the day view shows; null is the week view. */
  protected readonly dayIndex = signal<number | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly copyOpen = signal(false);
  protected readonly noteOpen = signal(false);
  protected readonly noteTitle = signal('');
  protected readonly noteText = signal('');
  private noteAt: { rowId: number; day: number } | null = null;
  private nextId = 0;
  private readonly newId = () => this.nextId++;

  protected readonly editable = computed(() => {
    const sheet = this.view()?.timesheet;
    return !!sheet && !sheet.isLocked && (sheet.status === 'Draft' || sheet.status === 'Rejected');
  });

  /** A week that can no longer change shows only what was booked, not every line that could have been. */
  protected readonly groups = computed(() => groupRows(this.editable() ? this.rows() : this.rows().filter((row) => rowTotal(row) > 0)));

  protected readonly columns = computed<readonly Column[]>(() => {
    const days = this.view()?.days ?? [];
    const only = this.dayIndex();
    return days.map((day, index) => ({ day, index })).filter((column) => only === null || column.index === only);
  });

  protected readonly total = computed(() => weekTotal(this.rows()));
  protected readonly leaveHours = computed(() => (this.view()?.days ?? []).reduce((sum, day) => sum + day.leaveHours, 0));
  protected readonly holidayHours = computed(() => (this.view()?.days ?? []).reduce((sum, day) => sum + day.holidayHours, 0));
  protected readonly weekGrand = computed(() => this.total() + this.leaveHours() + this.holidayHours());

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

  /** The day to open the day view on: today when it is in this week, otherwise the Monday. */
  protected todayIndex(): number {
    return Math.max(0, this.view()?.days.findIndex((day) => day.date === isoToday()) ?? 0);
  }

  protected dayHours(day: number): number {
    return dayTotal(this.rows(), day);
  }

  protected dayGrand(column: Column): number {
    return this.dayHours(column.index) + column.day.leaveHours + column.day.holidayHours;
  }

  protected groupDay(rows: readonly GridRow[], day: number): number {
    return dayTotal(rows, day);
  }

  protected groupWeek(rows: readonly GridRow[]): number {
    return weekTotal(rows);
  }

  /** The kinds of hour a line can be; Leave only ever appears on old entries, since leave comes from the Leave module. */
  protected categoriesFor(row: GridRow): readonly HourCategory[] {
    return BOOKABLE_HOUR_CATEGORIES.includes(row.category) ? BOOKABLE_HOUR_CATEGORIES : [row.category, ...BOOKABLE_HOUR_CATEGORIES];
  }

  protected toggleGroup(key: string): void {
    this.collapsed.update((collapsed) => {
      const next = new Set(collapsed);
      if (!next.delete(key)) {
        next.add(key);
      }
      return next;
    });
  }

  protected addOtherTime(): void {
    this.rows.update((rows) => [
      ...rows,
      { id: this.newId(), projectId: null, projectLabel: 'Other time', isDefault: false, taskId: null, title: 'Other time', activityName: null, category: 'Internal', activity: '', cells: blankCells() },
    ]);
  }

  protected setCategory(rowId: number, category: HourCategory): void {
    this.patch(rowId, (row) => ({ ...row, category }));
  }

  protected setHours(rowId: number, day: number, value: string): void {
    const hours = value === '' ? null : Number(value);
    this.patch(rowId, (row) => ({ ...row, cells: row.cells.map((cell, index) => (index === day ? { ...cell, hours } : cell)) }));
  }

  protected setNote(rowId: number, day: number, note: string): void {
    this.patch(rowId, (row) => ({ ...row, cells: row.cells.map((cell, index) => (index === day ? { ...cell, note } : cell)) }));
  }

  protected openNote(row: GridRow, column: Column): void {
    this.noteAt = { rowId: row.id, day: column.index };
    this.noteTitle.set(`${row.title}, ${column.day.date}`);
    this.noteText.set(row.cells[column.index].note);
    this.noteOpen.set(true);
  }

  protected saveNote(): void {
    if (this.noteAt) {
      this.setNote(this.noteAt.rowId, this.noteAt.day, this.noteText().trim());
    }
    this.noteOpen.set(false);
  }

  protected applyCopy(copied: CopiedLines): void {
    this.rows.update((rows) => copyInto(rows, copied.rows, copied.withNotes, this.newId));
    this.message.set('Copied. Check the hours, then save the week to keep them.');
  }

  protected async save(): Promise<boolean> {
    return this.run(async () => {
      this.apply(await this.api.saveWeek(this.weekStart(), toEntries(this.rows(), this.view()?.days ?? []), null));
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
      this.apply(await this.api.week(this.weekStart()));
    });
  }

  protected async withdraw(): Promise<void> {
    await this.run(async () => {
      await this.api.withdraw(this.weekStart());
      this.message.set('Withdrawn: the week is a draft again.');
      this.apply(await this.api.week(this.weekStart()));
    });
  }

  protected async load(week: string): Promise<void> {
    this.error.set(null);
    this.message.set(null);
    try {
      this.apply(await this.api.week(week));
    } catch (error) {
      this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : error instanceof ApiError ? error.message : 'The timesheet could not be loaded.');
    }
  }

  private patch(rowId: number, change: (row: GridRow) => GridRow): void {
    this.rows.update((rows) => rows.map((row) => (row.id === rowId ? change(row) : row)));
  }

  private apply(view: TimesheetView): void {
    this.view.set(view);
    this.rows.set(buildRows(view, this.newId));
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
}
