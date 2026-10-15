import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { AttendanceDay, Regularization, ScheduledDay, ShiftSwap, TodayAttendance } from '@gggs/shared/models';
import { formatMinutes, shortTime } from '@gggs/shared/models';
import { AttendanceApi, ShiftsApi, addDays, todayIso } from '@gggs/shared/time';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';

const FIXABLE = new Set(['Absent', 'MissingPunch', 'HalfDay']);

/**
 * My attendance: punch in and out, see how each day was judged, ask to correct a day, and see the
 * shifts coming up - swapping one with a colleague if need be.
 */
@Component({
  selector: 'gggs-my-attendance-page',
  imports: [DatePipe, ReactiveFormsModule, HasPermissionDirective, PageHeader, StatusBadge, Skeleton, EmptyState, FormDialog, Tabs, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My attendance" subtitle="Punch in and out, and see how each day counted." />

    <div class="stack">
      <section class="panel today">
        @if (today(); as t) {
          <div class="today__main">
            <span class="muted">Today · {{ t.day?.shiftName ?? 'No shift' }} {{ t.day ? shortTime(t.day.scheduledStart) + '–' + shortTime(t.day.scheduledEnd) : '' }}</span>
            <strong class="today__status">
              @if (t.punches.length === 0) {
                Not punched in yet
              } @else if (t.punches.length % 2 === 1) {
                Punched in at {{ shortTime(t.punches[t.punches.length - 1].localTime) }}
              } @else {
                Punched out at {{ shortTime(t.punches[t.punches.length - 1].localTime) }}
              }
            </strong>
            @if (t.punches.length > 1 && t.day) {
              <span class="muted">{{ formatMinutes(t.day.workedMinutes) }} worked so far</span>
            }
          </div>
          <button type="button" class="btn btn--primary today__punch" *gggsHasPermission="'self.attendance.punch'" [disabled]="punching()" (click)="punch()">
            <i class="pi pi-clock" aria-hidden="true"></i> {{ t.punches.length % 2 === 1 ? 'Punch out' : 'Punch in' }}
          </button>
        } @else {
          <gggs-skeleton [lines]="2" />
        }
      </section>
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }

      <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

      @switch (tab()) {
        @case ('days') {
          <section class="panel stack">
            <div class="toolbar">
              <label class="field days__month">
                <span class="field__label">Month</span>
                <input type="month" [value]="month()" (change)="setMonth($event)" />
              </label>
            </div>
            @if (loadingDays()) {
              <gggs-skeleton [lines]="6" />
            } @else if (days().length === 0) {
              <gggs-empty-state icon="pi pi-calendar" title="Nothing recorded for this month" />
            } @else {
              <table class="grid">
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
                        @if (!day.isToday && fixable(day)) {
                          <button type="button" class="btn btn--link btn--sm" *gggsHasPermission="'attendance.regularization.create'" (click)="openFix(day)">Fix</button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        }
        @case ('corrections') {
          <section class="panel">
            @if (regularizations().length === 0) {
              <gggs-empty-state icon="pi pi-pencil" title="No corrections requested" description="Use Fix on a day to say when you actually worked." />
            } @else {
              <table class="grid">
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
              <table class="grid">
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
    .today {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--gggs-space-4);
    }

    .today__main {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: var(--gggs-space-1, 0.25rem);
    }

    .today__status {
      font-size: var(--gggs-font-size-lg, 1.25rem);
    }

    .today__punch {
      min-width: 10rem;
      padding-block: var(--gggs-space-3);
      font-size: var(--gggs-font-size-md);
    }

    .days__month {
      max-width: 12rem;
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
      vertical-align: top;
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
      flex: 1;
      margin: 0;
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

    .shifts__day--off {
      opacity: 0.6;
    }
  `,
})
export class MyAttendancePage {
  private readonly attendance = inject(AttendanceApi);
  private readonly shifts = inject(ShiftsApi);

  protected readonly formatMinutes = formatMinutes;
  protected readonly shortTime = shortTime;
  protected readonly todayIso = todayIso();
  protected readonly tabs: readonly TabItem[] = [
    { key: 'days', label: 'Days' },
    { key: 'corrections', label: 'Corrections' },
    { key: 'shifts', label: 'My shifts' },
  ];

  protected readonly tab = signal('days');
  protected readonly today = signal<TodayAttendance | null>(null);
  protected readonly days = signal<readonly AttendanceDay[]>([]);
  protected readonly regularizations = signal<readonly Regularization[]>([]);
  protected readonly upcoming = signal<readonly ScheduledDay[]>([]);
  protected readonly swaps = signal<readonly ShiftSwap[]>([]);
  protected readonly month = signal(this.todayIso.slice(0, 7));
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

  protected readonly monthRange = computed(() => {
    const [year, month] = this.month().split('-').map(Number);
    const first = `${this.month()}-01`;
    const last = new Date(Date.UTC(year, month, 0)).toISOString().slice(0, 10);
    return { first, last };
  });

  constructor() {
    void this.refresh();
  }

  protected fixable(day: AttendanceDay): boolean {
    return FIXABLE.has(day.status);
  }

  protected setMonth(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.month.set(value);
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
        this.attendance.myRegularizations().catch(() => []),
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
    this.loadingDays.set(true);
    try {
      const range = this.monthRange();
      this.days.set([...(await this.attendance.myDays(range.first, range.last))].sort((a, b) => b.date.localeCompare(a.date)));
    } catch {
      this.days.set([]);
    } finally {
      this.loadingDays.set(false);
    }
  }
}
