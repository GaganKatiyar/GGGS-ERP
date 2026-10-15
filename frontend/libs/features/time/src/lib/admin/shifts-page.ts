import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective, PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { RosterRow, ScheduledDay, ShiftSwap, Weekday, WorkShift } from '@gggs/shared/models';
import { WEEKDAYS, shortTime } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { ShiftsApi, addDays, todayIso } from '@gggs/shared/time';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';

/**
 * Shifts and rosters (spec §19): the working patterns, who works which, a week's roster with
 * one-day changes, and the swaps people have asked for.
 */
@Component({
  selector: 'gggs-shifts-page',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, Tabs, Skeleton, EmptyState, StatusBadge, FormDialog, OrgSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Shifts & rosters" subtitle="Working patterns, who works which, and day-by-day changes.">
      <a class="btn" routerLink="..">Back to attendance</a>
    </gggs-page-header>

    <gggs-tabs [tabs]="tabs" [(selected)]="tab" (selectedChange)="onTab()" />

    <div class="shifts__body">
      @switch (tab()) {
        @case ('shifts') {
          <section class="panel stack">
            <div class="toolbar">
              <p class="muted shifts__hint">People without an assignment work the default shift. Without any shift, PeopleOS assumes 09:00–18:00, weekends off.</p>
              <button type="button" class="btn btn--primary btn--sm" *gggsHasPermission="'attendance.shift.manage'" (click)="openShift(null)">Add shift</button>
            </div>
            @if (shifts().length === 0) {
              <gggs-empty-state icon="pi pi-clock" title="No shifts yet" />
            } @else {
              <table class="grid">
                <thead>
                  <tr>
                    <th scope="col">Shift</th>
                    <th scope="col">Hours</th>
                    <th scope="col">Break</th>
                    <th scope="col">Grace</th>
                    <th scope="col">Half / full day</th>
                    <th scope="col">Days off</th>
                    <th scope="col">Status</th>
                    <th scope="col"><span class="grid__hidden">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (shift of shifts(); track shift.id) {
                    <tr>
                      <td><strong>{{ shift.name }}</strong> <span class="muted">{{ shift.code }}</span> @if (shift.isDefault) { <span class="shifts__default">default</span> }</td>
                      <td>{{ shortTime(shift.startTime) }}–{{ shortTime(shift.endTime) }}</td>
                      <td>{{ shift.breakMinutes }}m</td>
                      <td>{{ shift.graceMinutes }}m</td>
                      <td>{{ shift.halfDayMinutes / 60 }}h / {{ shift.fullDayMinutes / 60 }}h</td>
                      <td>{{ shortDays(shift.weeklyOff) }}</td>
                      <td><gggs-status-badge [status]="shift.isActive ? 'Active' : 'Inactive'" /></td>
                      <td class="grid__actions" *gggsHasPermission="'attendance.shift.manage'">
                        <button type="button" class="btn btn--link btn--sm" (click)="openShift(shift)">Edit</button>
                        @if (!shift.isDefault && shift.isActive) {
                          <button type="button" class="btn btn--link btn--sm" (click)="makeDefault(shift)">Make default</button>
                        }
                        @if (!shift.isDefault) {
                          <button type="button" class="btn btn--link btn--sm" (click)="toggleShift(shift)">{{ shift.isActive ? 'Retire' : 'Restore' }}</button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        }
        @case ('roster') {
          <section class="panel toolbar roster__filters">
            <label class="field">
              <span class="field__label">Week of</span>
              <input type="date" [value]="weekStart()" (change)="setWeek($event)" />
            </label>
            <label class="field roster__department">
              <span class="field__label">Department *</span>
              <gggs-org-select kind="departments" placeholder="Choose a department" ariaLabel="Department" [formControl]="department" />
            </label>
            <button type="button" class="btn btn--primary" *gggsHasPermission="'attendance.roster.manage'" (click)="openAssign()">Assign shift</button>
          </section>
          @if (!department.value) {
            <gggs-empty-state icon="pi pi-table" title="Choose a department" description="The roster shows one department's week." />
          } @else if (loadingRoster()) {
            <gggs-skeleton [lines]="6" />
          } @else {
            <section class="panel roster__scroll">
              <table class="grid roster">
                <thead>
                  <tr>
                    <th scope="col">Employee</th>
                    @for (day of weekDays(); track day) {
                      <th scope="col">{{ day | date: 'EEE d' }}</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of roster(); track row.employeeId) {
                    <tr>
                      <th scope="row">{{ row.employeeName }} <span class="muted">{{ row.employeeCode }}</span></th>
                      @for (day of row.days; track day.date) {
                        <td>
                          <button type="button" class="roster__cell" [class.roster__cell--off]="day.isWeeklyOff" [disabled]="!canRoster" (click)="openOverride(row, day)">
                            {{ day.isWeeklyOff ? 'Off' : day.shiftName }}
                            @if (!day.isWeeklyOff) {
                              <span class="muted">{{ shortTime(day.start) }}–{{ shortTime(day.end) }}</span>
                            }
                          </button>
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </section>
          }
        }
        @case ('swaps') {
          <section class="panel">
            @if (swaps().length === 0) {
              <gggs-empty-state icon="pi pi-arrow-right-arrow-left" title="No swap requests" />
            } @else {
              <table class="grid">
                <thead>
                  <tr>
                    <th scope="col">Day</th>
                    <th scope="col">Between</th>
                    <th scope="col">Reason</th>
                    <th scope="col">Status</th>
                  </tr>
                </thead>
                <tbody>
                  @for (swap of swaps(); track swap.id) {
                    <tr>
                      <td>{{ swap.date | date: 'EEE d MMM' }}</td>
                      <td>{{ swap.requesterName }} ⇄ {{ swap.counterpartName }}</td>
                      <td>{{ swap.reason ?? '' }}</td>
                      <td><gggs-status-badge [status]="swap.status" /></td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        }
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
      @if (message()) {
        <p class="shifts__message" role="status">{{ message() }}</p>
      }
    </div>

    <gggs-form-dialog [(visible)]="shiftOpen" [title]="editing() ? 'Edit shift' : 'Add shift'" [busy]="busy()" [error]="dialogError()" (save)="saveShift()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" maxlength="32" [disabled]="!!editing()" [value]="draft().code" (input)="patch('code', $any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="draft().name" (input)="patch('name', $any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Starts *</span>
          <input type="time" [value]="draft().startTime" (input)="patch('startTime', $any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Ends * <span class="muted">(earlier than the start: next day)</span></span>
          <input type="time" [value]="draft().endTime" (input)="patch('endTime', $any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Break (minutes)</span>
          <input type="number" min="0" [value]="draft().breakMinutes" (input)="patch('breakMinutes', +$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Grace for lateness (minutes)</span>
          <input type="number" min="0" max="240" [value]="draft().graceMinutes" (input)="patch('graceMinutes', +$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Half day after (hours)</span>
          <input type="number" min="0.5" step="0.5" [value]="draft().halfDayMinutes / 60" (input)="patch('halfDayMinutes', +$any($event.target).value * 60)" />
        </label>
        <label class="field">
          <span class="field__label">Full day after (hours)</span>
          <input type="number" min="1" step="0.5" [value]="draft().fullDayMinutes / 60" (input)="patch('fullDayMinutes', +$any($event.target).value * 60)" />
        </label>
        <fieldset class="weekdays form-grid__full">
          <legend>Days off</legend>
          @for (day of weekdays; track day) {
            <label class="weekdays__day"><input type="checkbox" [checked]="draft().weeklyOff.includes(day)" (change)="toggleDay(day)" /> {{ day.slice(0, 3) }}</label>
          }
        </fieldset>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="assignOpen" title="Assign a shift" saveLabel="Assign" [busy]="busy()" [error]="dialogError()" (save)="assign()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Shift *</span>
          <select [value]="assignShift()" (change)="assignShift.set($any($event.target).value)">
            @for (shift of activeShifts(); track shift.id) {
              <option [value]="shift.id">{{ shift.name }} ({{ shortTime(shift.startTime) }}–{{ shortTime(shift.endTime) }})</option>
            }
          </select>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Who</span>
          <gggs-employee-select ariaLabel="Employee" placeholder="One person - or leave empty for the whole department" [formControl]="assignEmployee" />
        </label>
        <label class="field">
          <span class="field__label">From *</span>
          <input type="date" [value]="assignFrom()" (input)="assignFrom.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Until <span class="muted">(empty: from then on)</span></span>
          <input type="date" [value]="assignTo()" (input)="assignTo.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="overrideOpen" [title]="overrideTitle()" [busy]="busy()" [error]="dialogError()" (save)="saveOverride()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Works</span>
          <select [value]="overrideShift()" (change)="overrideShift.set($any($event.target).value)">
            <option value="">Day off</option>
            @for (shift of activeShifts(); track shift.id) {
              <option [value]="shift.id">{{ shift.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Reason</span>
          <input type="text" maxlength="300" [value]="overrideReason()" (input)="overrideReason.set($any($event.target).value)" />
        </label>
        <button type="button" class="btn btn--link btn--sm" (click)="clearOverride()">Back to the regular pattern</button>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .shifts__body {
      margin-block-start: var(--gggs-space-3);
    }

    .shifts__hint {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .shifts__default {
      padding: 0 var(--gggs-space-2);
      border-radius: 999px;
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-size: var(--gggs-font-size-xs);
    }

    .shifts__message {
      color: var(--gggs-color-success);
    }

    .grid {
      width: 100%;
      border-collapse: collapse;
    }

    .grid th,
    .grid td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .grid__actions {
      white-space: nowrap;
    }

    .grid__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }

    .roster__filters {
      flex-wrap: wrap;
      align-items: flex-end;
      margin-block-end: var(--gggs-space-3);
    }

    .roster__department {
      min-width: 16rem;
    }

    .roster__scroll {
      overflow-x: auto;
    }

    .roster__cell {
      display: flex;
      flex-direction: column;
      width: 100%;
      min-width: 6.5rem;
      padding: var(--gggs-space-1, 0.25rem);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-sm);
      background: transparent;
      color: inherit;
      font: inherit;
      font-size: var(--gggs-font-size-sm);
      text-align: start;
      cursor: pointer;
    }

    .roster__cell--off {
      background: var(--gggs-color-surface-hover);
      color: var(--gggs-color-text-muted);
    }

    .weekdays {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      margin: 0;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }

    .weekdays__day {
      display: flex;
      gap: var(--gggs-space-1, 0.25rem);
      align-items: center;
    }
  `,
})
export class ShiftsPage {
  private readonly api = inject(ShiftsApi);

  protected readonly shortTime = shortTime;
  protected readonly weekdays = WEEKDAYS;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'shifts', label: 'Shifts' },
    { key: 'roster', label: 'Roster' },
    { key: 'swaps', label: 'Swaps' },
  ];

  protected readonly canRoster = inject(PermissionStore).has('attendance.roster.manage');
  protected readonly tab = signal('shifts');
  protected readonly shifts = signal<readonly WorkShift[]>([]);
  protected readonly roster = signal<readonly RosterRow[]>([]);
  protected readonly swaps = signal<readonly ShiftSwap[]>([]);
  protected readonly weekStart = signal(this.mondayOf(todayIso()));
  protected readonly department = new FormControl<string | null>(null);
  protected readonly loadingRoster = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly shiftOpen = signal(false);
  protected readonly editing = signal<WorkShift | null>(null);
  protected readonly draft = signal({
    code: '',
    name: '',
    startTime: '09:00',
    endTime: '18:00',
    breakMinutes: 60,
    graceMinutes: 15,
    halfDayMinutes: 240,
    fullDayMinutes: 480,
    weeklyOff: ['Saturday', 'Sunday'] as Weekday[],
  });

  protected readonly assignOpen = signal(false);
  protected readonly assignShift = signal('');
  protected readonly assignEmployee = new FormControl<string | null>(null);
  protected readonly assignFrom = signal(todayIso());
  protected readonly assignTo = signal('');

  protected readonly overrideOpen = signal(false);
  protected readonly overrideTarget = signal<{ row: RosterRow; day: ScheduledDay } | null>(null);
  protected readonly overrideShift = signal('');
  protected readonly overrideReason = signal('');

  protected readonly activeShifts = computed(() => this.shifts().filter((shift) => shift.isActive));
  protected readonly weekDays = computed(() => Array.from({ length: 7 }, (_, offset) => addDays(this.weekStart(), offset)));
  protected readonly overrideTitle = computed(() => {
    const target = this.overrideTarget();
    return target ? `${target.row.employeeName} on ${target.day.date}` : 'Change a day';
  });

  constructor() {
    this.department.valueChanges.subscribe(() => void this.loadRoster());
    void this.loadShifts();
  }

  protected shortDays(days: readonly Weekday[]): string {
    return days.length ? days.map((day) => day.slice(0, 3)).join(', ') : 'None';
  }

  protected onTab(): void {
    this.error.set(null);
    this.message.set(null);
    if (this.tab() === 'roster') {
      void this.loadRoster();
    } else if (this.tab() === 'swaps') {
      void this.api.swaps().then((swaps) => this.swaps.set(swaps), (error: unknown) => this.fail(error));
    }
  }

  protected setWeek(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.weekStart.set(this.mondayOf(value));
      void this.loadRoster();
    }
  }

  protected patch(field: string, value: string | number): void {
    this.draft.update((draft) => ({ ...draft, [field]: value }));
  }

  protected toggleDay(day: Weekday): void {
    this.draft.update((draft) => ({ ...draft, weeklyOff: draft.weeklyOff.includes(day) ? draft.weeklyOff.filter((value) => value !== day) : [...draft.weeklyOff, day] }));
  }

  protected openShift(shift: WorkShift | null): void {
    this.editing.set(shift);
    this.draft.set(
      shift
        ? {
            code: shift.code,
            name: shift.name,
            startTime: shortTime(shift.startTime),
            endTime: shortTime(shift.endTime),
            breakMinutes: shift.breakMinutes,
            graceMinutes: shift.graceMinutes,
            halfDayMinutes: shift.halfDayMinutes,
            fullDayMinutes: shift.fullDayMinutes,
            weeklyOff: [...shift.weeklyOff],
          }
        : { code: '', name: '', startTime: '09:00', endTime: '18:00', breakMinutes: 60, graceMinutes: 15, halfDayMinutes: 240, fullDayMinutes: 480, weeklyOff: ['Saturday', 'Sunday'] },
    );
    this.dialogError.set(null);
    this.shiftOpen.set(true);
  }

  protected async saveShift(): Promise<void> {
    const draft = this.draft();
    if (!draft.code.trim() || !draft.name.trim()) {
      this.dialogError.set('Give the shift a code and a name.');
      return;
    }
    await this.inDialog(async () => {
      await this.api.save(this.editing()?.id ?? null, { ...draft, code: draft.code.trim(), name: draft.name.trim() });
      this.shiftOpen.set(false);
      await this.loadShifts();
    });
  }

  protected async makeDefault(shift: WorkShift): Promise<void> {
    await this.inPage(async () => {
      await this.api.makeDefault(shift.id);
      await this.loadShifts();
    });
  }

  protected async toggleShift(shift: WorkShift): Promise<void> {
    await this.inPage(async () => {
      await this.api.setActive(shift.id, !shift.isActive);
      await this.loadShifts();
    });
  }

  protected openAssign(): void {
    this.assignShift.set(this.activeShifts()[0]?.id ?? '');
    this.assignEmployee.setValue(null);
    this.assignFrom.set(todayIso());
    this.assignTo.set('');
    this.dialogError.set(null);
    this.assignOpen.set(true);
  }

  protected async assign(): Promise<void> {
    const employee = this.assignEmployee.value;
    const people = employee ? [employee] : this.roster().map((row) => row.employeeId);
    if (!this.assignShift() || people.length === 0) {
      this.dialogError.set('Choose a shift, and a person or a department with people in it.');
      return;
    }
    await this.inDialog(async () => {
      const result = await this.api.assign({ employeeIds: people, shiftId: this.assignShift(), effectiveFrom: this.assignFrom(), effectiveTo: this.assignTo() || null });
      this.assignOpen.set(false);
      this.message.set(`${result.assigned} people assigned.`);
      await this.loadRoster();
    });
  }

  protected openOverride(row: RosterRow, day: ScheduledDay): void {
    this.overrideTarget.set({ row, day });
    this.overrideShift.set(day.isWeeklyOff ? '' : (day.shiftId ?? ''));
    this.overrideReason.set('');
    this.dialogError.set(null);
    this.overrideOpen.set(true);
  }

  protected async saveOverride(): Promise<void> {
    const target = this.overrideTarget();
    if (!target) {
      return;
    }
    await this.inDialog(async () => {
      await this.api.setOverride({ employeeId: target.row.employeeId, date: target.day.date, shiftId: this.overrideShift() || null, reason: this.overrideReason().trim() || null });
      this.overrideOpen.set(false);
      await this.loadRoster();
    });
  }

  protected async clearOverride(): Promise<void> {
    const target = this.overrideTarget();
    if (!target) {
      return;
    }
    await this.inDialog(async () => {
      await this.api.clearOverride(target.row.employeeId, target.day.date);
      this.overrideOpen.set(false);
      await this.loadRoster();
    });
  }

  private mondayOf(iso: string): string {
    const weekday = new Date(`${iso}T00:00:00Z`).getUTCDay();
    return addDays(iso, weekday === 0 ? -6 : 1 - weekday);
  }

  private async loadShifts(): Promise<void> {
    try {
      this.shifts.set(await this.api.list());
    } catch (error) {
      this.fail(error);
    }
  }

  private async loadRoster(): Promise<void> {
    const department = this.department.value;
    if (!department) {
      this.roster.set([]);
      return;
    }
    this.loadingRoster.set(true);
    try {
      this.roster.set(await this.api.roster(this.weekStart(), addDays(this.weekStart(), 6), department));
    } catch (error) {
      this.fail(error);
    } finally {
      this.loadingRoster.set(false);
    }
  }

  private async inDialog(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await call();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async inPage(call: () => Promise<void>): Promise<void> {
    this.error.set(null);
    try {
      await call();
    } catch (error) {
      this.fail(error);
    }
  }

  private fail(error: unknown): void {
    this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
  }
}
