import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective, PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { AttendanceDay, Regularization, ScheduledDay, ShiftSwap, TodayAttendance } from '@gggs/shared/models';
import { formatMinutes, shortTime } from '@gggs/shared/models';
import { AttendanceApi, ShiftsApi, addDays, todayIso } from '@gggs/shared/time';
import { Badge, DEFAULT_STATUS_VARIANTS, EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge, Tabs, type BadgeVariant, type TabItem } from '@gggs/shared/ui';
import { AttendanceCalendar } from './attendance-calendar';
import { addMonths, isFixable, monthRange, statusCounts } from './attendance-month';

type DaysView = 'calendar' | 'list';

/** Which way of showing the days a person last chose, kept per browser. */
const VIEW_KEY = 'gggs.my-attendance.view';

function savedView(): DaysView {
  try {
    return localStorage.getItem(VIEW_KEY) === 'list' ? 'list' : 'calendar';
  } catch {
    return 'calendar';
  }
}

/**
 * My attendance: punch in and out, see how each day was judged, ask to correct a day, and see the
 * shifts coming up - swapping one with a colleague if need be.
 */
@Component({
  selector: 'gggs-my-attendance-page',
  imports: [DatePipe, ReactiveFormsModule, HasPermissionDirective, PageHeader, Badge, StatusBadge, Skeleton, EmptyState, FormDialog, Tabs, EmployeeSelect, AttendanceCalendar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My attendance">
      @if (today(); as t) {
        <section class="today" aria-label="Today">
          <span class="today__dot" [class.today__dot--in]="t.punches.length % 2 === 1" aria-hidden="true"></span>
          <p class="today__text" aria-live="polite">
            <strong class="today__status">
              @if (t.punches.length === 0) {
                Not punched in yet
              } @else if (t.punches.length % 2 === 1) {
                Punched in at {{ shortTime(t.punches[t.punches.length - 1].localTime) }}
              } @else {
                Punched out at {{ shortTime(t.punches[t.punches.length - 1].localTime) }}
              }
            </strong>
            <span class="muted today__shift">
              Today · {{ t.day?.shiftName ?? 'No shift' }} {{ t.day ? shortTime(t.day.scheduledStart) + '–' + shortTime(t.day.scheduledEnd) : '' }}
              @if (t.punches.length > 1 && t.day) {
                · {{ formatMinutes(t.day.workedMinutes) }} worked so far
              }
            </span>
          </p>
          <button type="button" class="btn btn--primary today__punch" *gggsHasPermission="'self.attendance.punch'" [disabled]="punching()" (click)="punch()">
            <i class="pi pi-clock" aria-hidden="true"></i> {{ t.punches.length % 2 === 1 ? 'Punch out' : 'Punch in' }}
          </button>
        </section>
      } @else {
        <div class="today today--loading"><gggs-skeleton [lines]="1" /></div>
      }
    </gggs-page-header>

    <div class="stack">
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }

      <div class="controls">
        <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
        @if (tab() === 'days') {
          <div class="controls__tools">
            <div class="month">
              <button type="button" class="btn btn--ghost btn--icon" aria-label="Previous month" (click)="moveMonth(-1)"><i class="pi pi-chevron-left" aria-hidden="true"></i></button>
              <!-- The label opens the month picker, so a month far back is not many clicks away. -->
              <button type="button" class="month__label" [attr.aria-label]="'Choose a month. Showing ' + (month() + '-01' | date: 'MMMM y')" (click)="openMonthPicker(picker)">{{ month() + '-01' | date: 'MMMM y' }}</button>
              <input #picker type="month" class="sr-only" tabindex="-1" aria-hidden="true" [max]="currentMonth" [value]="month()" (change)="setMonth($event)" />
              <button type="button" class="btn btn--ghost btn--icon" aria-label="Next month" [disabled]="month() >= currentMonth" (click)="moveMonth(1)"><i class="pi pi-chevron-right" aria-hidden="true"></i></button>
            </div>
            <div class="view-switch" role="group" aria-label="Show days as">
              <button type="button" class="view-switch__option" [attr.aria-pressed]="view() === 'list'" (click)="setView('list')"><i class="pi pi-list" aria-hidden="true"></i> List</button>
              <button type="button" class="view-switch__option" [attr.aria-pressed]="view() === 'calendar'" (click)="setView('calendar')"><i class="pi pi-calendar" aria-hidden="true"></i> Calendar</button>
            </div>
          </div>
        }
      </div>

      @switch (tab()) {
        @case ('days') {
          <section class="panel panel--flush" aria-label="Days">
            @if (loadingDays()) {
              <div class="days__loading"><gggs-skeleton [lines]="6" /></div>
            } @else if (days().length === 0) {
              <gggs-empty-state icon="pi pi-calendar" title="Nothing recorded for this month" />
            } @else {
              <ul class="counts" aria-label="Days this month">
                @for (entry of counts(); track entry.status) {
                  <li><gggs-badge [label]="countLabel(entry.status, entry.count)" [variant]="variantOf(entry.status)" /></li>
                }
              </ul>
              @if (view() === 'calendar') {
                <gggs-attendance-calendar [month]="month()" [days]="days()" [scheduled]="scheduled()" [today]="todayIso" (fix)="openFix($event)" />
              } @else {
                <div class="table-wrap">
                  <table class="table table--compact grid">
                    <thead>
                      <tr>
                        <th scope="col">Day</th>
                        <th scope="col">Shift</th>
                        <th scope="col">In</th>
                        <th scope="col">Out</th>
                        <th scope="col">Worked</th>
                        <th scope="col">Late</th>
                        <th scope="col">Status</th>
                        <th scope="col"><span class="grid__hidden">Actions</span></th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (day of days(); track day.date) {
                        <tr [class.grid__row--today]="day.isToday">
                          <td>{{ day.date | date: 'EEE d MMM' }}</td>
                          <td>{{ day.shiftName }}</td>
                          <td>{{ shortTime(day.firstIn) || '—' }}</td>
                          <td>{{ shortTime(day.lastOut) || '—' }}</td>
                          <td>{{ formatMinutes(day.workedMinutes) || '—' }}</td>
                          <td>{{ day.lateMinutes ? formatMinutes(day.lateMinutes) : '' }}</td>
                          <td>
                            <gggs-status-badge [status]="day.status" />
                            @if (day.isRegularized) {
                              <span class="muted grid__note">corrected</span>
                            }
                            @if (day.note) {
                              <span class="muted grid__note">{{ day.note }}</span>
                            }
                          </td>
                          <td>
                            @if (fixable(day)) {
                              <button type="button" class="btn btn--link btn--sm" *gggsHasPermission="'attendance.regularization.create'" (click)="openFix(day)">Fix</button>
                            }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            }
          </section>
        }
        @case ('corrections') {
          <section class="panel">
            @if (regularizations().length === 0) {
              <gggs-empty-state icon="pi pi-pencil" title="No corrections requested" description="Use Fix on a day to say when you actually worked." />
            } @else {
              <table class="table grid">
                <thead>
                  <tr>
                    <th scope="col">Day</th>
                    <th scope="col">In</th>
                    <th scope="col">Out</th>
                    <th scope="col">Reason</th>
                    <th scope="col">Status</th>
                    <th scope="col"><span class="grid__hidden">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (request of regularizations(); track request.id) {
                    <tr>
                      <td>{{ request.date | date: 'EEE d MMM' }}</td>
                      <td>{{ shortTime(request.checkIn) || '—' }}</td>
                      <td>{{ shortTime(request.checkOut) || '—' }}</td>
                      <td>{{ request.reason }}</td>
                      <td><gggs-status-badge [status]="request.status" /></td>
                      <td>
                        @if (request.status === 'Pending') {
                          <button type="button" class="btn btn--link btn--sm" (click)="cancelFix(request)">Cancel</button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        }
        @case ('shifts') {
          <section class="panel stack">
            <div class="toolbar">
              <h2 class="shifts__heading">Next two weeks</h2>
              <button type="button" class="btn btn--sm" *gggsHasPermission="'self.shift-swap.request'" (click)="openSwap()">Swap a shift</button>
            </div>
            <ul class="shifts">
              @for (day of upcoming(); track day.date) {
                <li class="shifts__day" [class.shifts__day--off]="day.isWeeklyOff">
                  <strong>{{ day.date | date: 'EEE d' }}</strong>
                  <span>{{ day.isWeeklyOff ? 'Day off' : day.shiftName }}</span>
                  @if (!day.isWeeklyOff) {
                    <span class="muted">{{ shortTime(day.start) }}–{{ shortTime(day.end) }}</span>
                  }
                </li>
              }
            </ul>
            @if (swaps().length > 0) {
              <h3 class="shifts__heading">Swap requests</h3>
              <table class="table grid">
                <tbody>
                  @for (swap of swaps(); track swap.id) {
                    <tr>
                      <td>{{ swap.date | date: 'EEE d MMM' }}</td>
                      <td>{{ swap.requesterName }} ⇄ {{ swap.counterpartName }}</td>
                      <td><gggs-status-badge [status]="swap.status" /></td>
                      <td>
                        @if (swap.status === 'Pending') {
                          <button type="button" class="btn btn--link btn--sm" (click)="cancelSwap(swap)">Cancel</button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        }
      }
    </div>

    <gggs-form-dialog [(visible)]="fixOpen" [title]="'Correct ' + (fixDay() | date: 'EEE d MMM')" saveLabel="Send" [busy]="busy()" [error]="dialogError()" (save)="sendFix()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Checked in</span>
          <input type="time" [value]="fixIn()" (input)="fixIn.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Checked out</span>
          <input type="time" [value]="fixOut()" (input)="fixOut.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Why *</span>
          <input type="text" maxlength="300" [value]="fixReason()" (input)="fixReason.set($any($event.target).value)" placeholder="Forgot my card" />
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="swapOpen" title="Swap a shift" saveLabel="Ask" [busy]="busy()" [error]="dialogError()" (save)="sendSwap()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Day *</span>
          <input type="date" [min]="todayIso" [value]="swapDate()" (input)="swapDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">With *</span>
          <gggs-employee-select [formControl]="swapWith" ariaLabel="Swap with" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Reason</span>
          <input type="text" maxlength="300" [value]="swapReason()" (input)="swapReason.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    /* Beside the title, in place of a box of its own: the punch is one click, not the whole page. */
    .today {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      min-width: min(30rem, 100%);
      min-height: 3rem;
      padding: var(--gggs-space-1) var(--gggs-space-2) var(--gggs-space-1) var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-1);
    }

    .today--loading {
      padding-inline-end: var(--gggs-space-3);
    }

    .today--loading gggs-skeleton {
      flex: 1;
    }

    .today__dot {
      flex: none;
      width: 0.625rem;
      height: 0.625rem;
      border-radius: 50%;
      background: var(--gggs-color-text-muted);
    }

    .today__dot--in {
      background: var(--gggs-color-success);
      box-shadow: 0 0 0 3px var(--gggs-color-success-subtle);
    }

    .today__text {
      display: flex;
      flex: 1;
      flex-wrap: wrap;
      align-items: baseline;
      gap: 0 var(--gggs-space-2);
      min-width: 0;
      margin: 0;
    }

    .today__status {
      font-size: var(--gggs-font-size-md);
      font-weight: var(--gggs-font-weight-semibold);
    }

    .today__shift {
      font-size: var(--gggs-font-size-sm);
    }

    .today__punch {
      flex: none;
    }

    /* The tabs keep their own full-width rule; the month and view controls sit on it, saving a row. */
    .controls {
      position: relative;
      /* The tabs bring their own space below. */
      margin-block-end: calc(-1 * var(--gggs-space-3));
    }

    .controls__tools {
      position: absolute;
      inset-block-start: 0;
      inset-inline-end: 0;
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
    }

    .month {
      display: flex;
      align-items: center;
      gap: 0.125rem;
    }

    .month__label {
      min-width: 8rem;
      padding: var(--gggs-space-1) var(--gggs-space-2);
      border: 0;
      border-radius: var(--gggs-radius-md);
      background: none;
      color: var(--gggs-color-text);
      font: inherit;
      font-weight: var(--gggs-font-weight-semibold);
      font-variant-numeric: tabular-nums;
      text-align: center;
      cursor: pointer;

      &:hover {
        background: var(--gggs-color-surface-sunken);
      }

      &:focus-visible {
        outline: none;
        box-shadow: var(--gggs-focus-ring);
      }
    }

    .view-switch {
      display: inline-flex;
      padding: 2px;
      border: 1px solid var(--gggs-color-border-strong);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-sunken);
    }

    .view-switch__option {
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-1);
      min-height: 2rem;
      padding: 0.125rem var(--gggs-space-2);
      border: 0;
      border-radius: var(--gggs-radius-sm);
      background: none;
      color: var(--gggs-color-text-muted);
      font: inherit;
      font-weight: var(--gggs-font-weight-medium);
      cursor: pointer;

      &[aria-pressed='true'] {
        background: var(--gggs-color-surface-raised);
        color: var(--gggs-color-text);
        font-weight: var(--gggs-font-weight-semibold);
        box-shadow: var(--gggs-shadow-1);
      }

      &:focus-visible {
        outline: none;
        box-shadow: var(--gggs-focus-ring);
      }
    }

    .days__loading {
      padding: var(--gggs-space-4);
    }

    .counts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-1) var(--gggs-space-2);
      margin: 0;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border-block-end: 1px solid var(--gggs-color-border);
      list-style: none;
    }

    @media (max-width: 48rem) {
      .today {
        min-width: 0;
      }

      .controls__tools {
        position: static;
        flex-wrap: wrap;
        justify-content: space-between;
        gap: var(--gggs-space-1);
        margin-block: calc(-1 * var(--gggs-space-2)) var(--gggs-space-3);
      }

      .month__label {
        min-width: 0;
        padding-inline: 0;
        white-space: nowrap;
      }

      .view-switch__option {
        padding-inline: var(--gggs-space-1);
      }
    }

    .grid__row--today {
      background: var(--gggs-color-primary-subtle);
    }

    .grid__note {
      display: block;
      font-size: var(--gggs-font-size-xs);
    }

    .grid__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }

    .shifts__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .shifts {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(8rem, 1fr));
      gap: var(--gggs-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .shifts__day {
      display: flex;
      flex-direction: column;
      padding: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }

    /* Quieter than a working day but still readable: muted text on the sunken surface passes AA,
       where fading the whole tile did not. */
    .shifts__day--off {
      background: var(--gggs-color-surface-sunken);
      color: var(--gggs-color-text-muted);
    }
  `,
})
export class MyAttendancePage {
  private readonly attendance = inject(AttendanceApi);
  private readonly shifts = inject(ShiftsApi);
  /** Corrections are only asked for (and listed) by people allowed to request them. */
  private readonly canRegularize = inject(PermissionStore).has('attendance.regularization.create');

  protected readonly formatMinutes = formatMinutes;
  protected readonly shortTime = shortTime;
  protected readonly todayIso = todayIso();
  protected readonly currentMonth = this.todayIso.slice(0, 7);
  protected readonly fixable = isFixable;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'days', label: 'Days' },
    { key: 'corrections', label: 'Corrections' },
    { key: 'shifts', label: 'My shifts' },
  ];

  protected readonly tab = signal('days');
  protected readonly today = signal<TodayAttendance | null>(null);
  protected readonly days = signal<readonly AttendanceDay[]>([]);
  /** The rest of this month's shifts, which the calendar shows on the days still to come. */
  protected readonly scheduled = signal<readonly ScheduledDay[]>([]);
  protected readonly view = signal<DaysView>(savedView());
  protected readonly regularizations = signal<readonly Regularization[]>([]);
  protected readonly upcoming = signal<readonly ScheduledDay[]>([]);
  protected readonly swaps = signal<readonly ShiftSwap[]>([]);
  protected readonly month = signal(this.currentMonth);
  protected readonly loadingDays = signal(true);
  protected readonly punching = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly fixOpen = signal(false);
  protected readonly fixDay = signal<string>('');
  protected readonly fixIn = signal('09:00');
  protected readonly fixOut = signal('18:00');
  protected readonly fixReason = signal('');
  protected readonly swapOpen = signal(false);
  protected readonly swapDate = signal(addDays(this.todayIso, 1));
  protected readonly swapReason = signal('');
  protected readonly swapWith = new FormControl<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly counts = computed(() => statusCounts(this.days()));

  constructor() {
    void this.refresh();
  }

  protected setView(view: DaysView): void {
    this.view.set(view);
    try {
      localStorage.setItem(VIEW_KEY, view);
    } catch {
      // Storage may be blocked; the choice then lasts until the page is left.
    }
  }

  protected variantOf(status: string): BadgeVariant {
    return DEFAULT_STATUS_VARIANTS[status.toLowerCase()] ?? 'neutral';
  }

  protected countLabel(status: string, count: number): string {
    const spaced = status.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
    return `${spaced.charAt(0).toUpperCase()}${spaced.slice(1)} ${count}`;
  }

  protected moveMonth(by: number): void {
    this.goToMonth(addMonths(this.month(), by));
  }

  protected setMonth(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.goToMonth(value);
    }
  }

  protected openMonthPicker(picker: HTMLInputElement): void {
    try {
      picker.showPicker();
    } catch {
      // A browser without the picker still has the arrows.
    }
  }

  private goToMonth(month: string): void {
    // Future months have nothing recorded, and their shifts are under My shifts.
    if (month <= this.currentMonth && month !== this.month()) {
      this.month.set(month);
      void this.loadDays();
    }
  }

  protected async punch(): Promise<void> {
    this.punching.set(true);
    this.error.set(null);
    try {
      this.today.set(await this.attendance.punch());
      await this.loadDays();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The punch could not be recorded. Try again.');
    } finally {
      this.punching.set(false);
    }
  }

  protected openFix(day: AttendanceDay): void {
    this.fixDay.set(day.date);
    this.fixIn.set(shortTime(day.firstIn) || shortTime(day.scheduledStart) || '09:00');
    this.fixOut.set(shortTime(day.lastOut) || shortTime(day.scheduledEnd) || '18:00');
    this.fixReason.set('');
    this.dialogError.set(null);
    this.fixOpen.set(true);
  }

  protected async sendFix(): Promise<void> {
    if (!this.fixReason().trim()) {
      this.dialogError.set('Say why the record is wrong.');
      return;
    }
    await this.run(async () => {
      await this.attendance.requestRegularization({ date: this.fixDay(), checkIn: this.fixIn() || null, checkOut: this.fixOut() || null, reason: this.fixReason().trim() });
      this.fixOpen.set(false);
      this.tab.set('corrections');
    });
  }

  protected async cancelFix(request: Regularization): Promise<void> {
    await this.run(() => this.attendance.cancelRegularization(request.id));
  }

  protected openSwap(): void {
    this.swapDate.set(addDays(this.todayIso, 1));
    this.swapReason.set('');
    this.swapWith.setValue(null);
    this.dialogError.set(null);
    this.swapOpen.set(true);
  }

  protected async sendSwap(): Promise<void> {
    const counterpart = this.swapWith.value;
    if (!counterpart || !this.swapDate()) {
      this.dialogError.set('Choose the day and the colleague.');
      return;
    }
    await this.run(async () => {
      await this.shifts.requestSwap({ counterpartEmployeeId: counterpart, date: this.swapDate(), reason: this.swapReason().trim() || null });
      this.swapOpen.set(false);
    });
  }

  protected async cancelSwap(swap: ShiftSwap): Promise<void> {
    await this.run(() => this.shifts.cancelSwap(swap.id));
  }

  private async run(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    this.error.set(null);
    try {
      await call();
      await this.refresh();
    } catch (error) {
      const message = error instanceof ApiError ? error.message : 'That did not work. Try again.';
      if (this.fixOpen() || this.swapOpen()) {
        this.dialogError.set(message);
      } else {
        this.error.set(message);
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async refresh(): Promise<void> {
    try {
      const [today, regularizations, upcoming, swaps] = await Promise.all([
        this.attendance.today(),
        this.canRegularize ? this.attendance.myRegularizations().catch(() => []) : Promise.resolve([]),
        this.shifts.myShifts(this.todayIso, addDays(this.todayIso, 13)),
        this.shifts.mySwaps().catch(() => []),
      ]);
      this.today.set(today);
      this.regularizations.set(regularizations);
      this.upcoming.set(upcoming);
      this.swaps.set(swaps);
    } catch (error) {
      this.error.set(
        error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : error instanceof ApiError ? error.message : 'Attendance could not be loaded.',
      );
    }
    await this.loadDays();
  }

  private async loadDays(): Promise<void> {
    const month = this.month();
    const range = monthRange(month);
    const tomorrow = addDays(this.todayIso, 1);
    this.loadingDays.set(true);
    try {
      const [days, scheduled] = await Promise.all([
        this.attendance.myDays(range.first, range.last),
        // A missing schedule leaves the days to come blank, which is no reason to lose the record.
        tomorrow <= range.last ? this.shifts.myShifts(tomorrow, range.last).catch(() => []) : Promise.resolve([]),
      ]);
      // The arrows make it easy to move on before this answer arrives; it belongs to a month no longer shown.
      if (month === this.month()) {
        this.days.set([...days].sort((a, b) => b.date.localeCompare(a.date)));
        this.scheduled.set(scheduled);
      }
    } catch {
      if (month === this.month()) {
        this.days.set([]);
        this.scheduled.set([]);
      }
    } finally {
      if (month === this.month()) {
        this.loadingDays.set(false);
      }
    }
  }
}
