import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { ScheduledDay, TodayAttendance } from '@gggs/shared/models';
import { shortTime } from '@gggs/shared/models';
import { AttendanceApi, ShiftsApi, todayIso } from '@gggs/shared/time';
import { clockLabel, workedMs } from './work-time';

/**
 * "Let's get to work": today's shift, one button to clock in or out, and how long the day has run
 * so far. The same punch the attendance page records; the timer ticks while clocked in.
 */
@Component({
  selector: 'gggs-work-card',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="work" aria-labelledby="work-title">
      <h2 id="work-title">Let's get to work</h2>
      <div class="work__row">
        <span class="muted">{{ dateLabel }}</span>
        <span class="work__clock" [attr.aria-label]="'Worked today ' + worked()">{{ worked() }}</span>
      </div>
      <p class="work__shift">
        @if (shift(); as day) {
          @if (day.isWeeklyOff) {
            Day off today
          } @else {
            Shift: {{ time(day.start) }}–{{ time(day.end) }} <span class="muted">· {{ day.shiftName }}</span>
          }
        } @else {
          <span class="muted">Loading your shift…</span>
        }
      </p>
      <button type="button" class="btn btn--primary work__button" [disabled]="busy() || !today() || !today()!.canPunch" (click)="punch()">
        <i class="pi pi-clock" aria-hidden="true"></i>
        {{ clockedIn() ? 'Clock out' : 'Clock in' }}
      </button>
      @if (status(); as message) {
        <p class="work__status" role="status">{{ message }}</p>
      }
      @if (error(); as message) {
        <p class="work__error" role="alert">{{ message }}</p>
      }
      <div class="work__links">
        <a routerLink="/me/attendance">My attendance</a>
        <a routerLink="/me/policies">View policies</a>
      </div>
    </article>
  `,
  styles: `
    .work {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-3);
      height: 100%;
      padding: var(--gggs-space-5);
      box-sizing: border-box;
      border: 1px solid var(--gggs-color-primary);
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-2), 0 6px 0 -2px color-mix(in srgb, var(--gggs-color-primary) 35%, transparent);
    }

    h2 {
      margin: 0;
      font-family: var(--gggs-font-family-display);
      font-size: var(--gggs-font-size-lg);
    }

    .work__row {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: var(--gggs-space-3);
      padding-block-end: var(--gggs-space-3);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .work__clock {
      font-family: var(--gggs-font-family-display);
      font-size: var(--gggs-font-size-xl);
      font-weight: var(--gggs-font-weight-bold);
      font-variant-numeric: tabular-nums;
    }

    .work__shift {
      margin: 0;
    }

    .work__button {
      justify-content: center;
      width: 100%;
      min-height: 2.75rem;
    }

    .work__status,
    .work__error {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .work__status {
      color: var(--gggs-color-success);
    }

    .work__error {
      color: var(--gggs-color-danger);
    }

    .work__links {
      display: flex;
      justify-content: space-between;
      gap: var(--gggs-space-3);
      font-size: var(--gggs-font-size-sm);
    }
  `,
})
export class WorkCard {
  private readonly attendance = inject(AttendanceApi);
  private readonly shifts = inject(ShiftsApi);

  protected readonly today = signal<TodayAttendance | null>(null);
  protected readonly shift = signal<ScheduledDay | null>(null);
  protected readonly busy = signal(false);
  protected readonly status = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  private readonly now = signal(Date.now());

  protected readonly dateLabel = new Date().toLocaleDateString(undefined, { day: 'numeric', month: 'short', weekday: 'long' });
  protected readonly time = shortTime;
  protected readonly clockedIn = computed(() => (this.today()?.punches.length ?? 0) % 2 === 1);
  protected readonly worked = computed(() => clockLabel(workedMs(this.today()?.punches ?? [], this.now())));

  constructor() {
    // A second's tick for the clock; it only moves while clocked in, so ticking otherwise is cheap.
    const tick = setInterval(() => this.clockedIn() && this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(tick));

    const day = todayIso();
    void this.attendance.today().then((today) => this.today.set(today), () => this.error.set('Today’s attendance could not be loaded.'));
    void this.shifts.myShifts(day, day).then((days) => this.shift.set(days[0] ?? null), () => this.shift.set(null));
  }

  protected async punch(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      const today = await this.attendance.punch();
      this.today.set(today);
      this.now.set(Date.now());
      const last = today.punches[today.punches.length - 1];
      this.status.set(`${today.punches.length % 2 === 1 ? 'Clocked in' : 'Clocked out'} at ${last ? shortTime(last.localTime) : 'now'}.`);
    } catch (error) {
      this.status.set(null);
      this.error.set(error instanceof ApiError ? error.message : 'That did not go through. Try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
