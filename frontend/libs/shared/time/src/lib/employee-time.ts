import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import type { AttendanceDay, LeaveBalance, LeaveRequest } from '@gggs/shared/models';
import { formatMinutes, shortTime } from '@gggs/shared/models';
import { EmptyState, KpiCard, StatusBadge } from '@gggs/shared/ui';
import { AttendanceApi, LeaveApi, addDays, todayIso } from './time-apis';

/** Employee 360 attendance tab: the last thirty days, as the attendance module derived them. */
@Component({
  selector: 'gggs-employee-attendance',
  imports: [DatePipe, KpiCard, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      <div class="kpi-row">
        <gggs-kpi-card label="Present (30 days)" [value]="count('Present')" />
        <gggs-kpi-card label="Half days" [value]="count('HalfDay')" />
        <gggs-kpi-card label="Absent" [value]="count('Absent')" />
        <gggs-kpi-card label="Late arrivals" [value]="late()" />
      </div>
      @for (day of days(); track day.date) {
        <div class="e-time__row">
          <strong class="e-time__date">{{ day.date | date: 'EEE d MMM' }}</strong>
          <span class="muted">{{ day.shiftName }}</span>
          <span>{{ shortTime(day.firstIn) || '—' }} – {{ shortTime(day.lastOut) || '—' }}</span>
          <span>{{ day.workedMinutes ? formatMinutes(day.workedMinutes) : '' }}</span>
          <gggs-status-badge [status]="day.status" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-clock" title="No attendance in the last thirty days" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </div>
  `,
  styles: `
    .kpi-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .e-time__row {
      display: grid;
      grid-template-columns: 8rem 1fr 9rem 5rem 8rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    @media (max-width: 40rem) {
      .e-time__row {
        grid-template-columns: 1fr 1fr;
      }
    }
  `,
})
export class EmployeeAttendance {
  private readonly api = inject(AttendanceApi);

  readonly employeeId = input.required<string>();

  protected readonly formatMinutes = formatMinutes;
  protected readonly shortTime = shortTime;
  protected readonly days = signal<readonly AttendanceDay[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly late = computed(() => this.days().filter((day) => day.lateMinutes > 0).length);

  constructor() {
    effect(() => {
      const employeeId = this.employeeId();
      const to = todayIso();
      this.api.days({ from: addDays(to, -29), to, employeeId }).then(
        (days) => this.days.set([...days].sort((a, b) => b.date.localeCompare(a.date))),
        (error: unknown) => this.error.set(error instanceof Error ? error.message : 'Attendance could not be loaded.'),
      );
    });
  }

  protected count(status: AttendanceDay['status']): number {
    return this.days().filter((day) => day.status === status).length;
  }
}

/** Employee 360 leave tab: balances by type and the year's requests. */
@Component({
  selector: 'gggs-employee-leave',
  imports: [DatePipe, DecimalPipe, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      <div class="e-leave__balances">
        @for (balance of balances(); track balance.leaveTypeId) {
          <div class="panel e-leave__balance" [style.border-inline-start-color]="balance.color">
            <span class="muted">{{ balance.name }}</span>
            <strong class="e-leave__value">{{ balance.available | number: '1.0-1' }}</strong>
            <span class="muted">available · {{ balance.takenThisYear | number: '1.0-1' }} taken this year</span>
          </div>
        }
      </div>
      <h3 class="e-leave__heading">Requests this year</h3>
      @for (request of requests(); track request.id) {
        <div class="e-leave__row">
          <strong>{{ request.leaveTypeName }}</strong>
          <span>{{ request.startDate | date: 'd MMM' }} – {{ request.endDate | date: 'd MMM' }}</span>
          <span>{{ request.days | number: '1.0-1' }} day(s)</span>
          <gggs-status-badge [status]="request.status" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-calendar" title="No leave requested this year" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </div>
  `,
  styles: `
    .e-leave__balances {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .e-leave__balance {
      display: flex;
      flex-direction: column;
      border-inline-start: 4px solid;
    }

    .e-leave__value {
      font-size: var(--gggs-font-size-xl);
    }

    .e-leave__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .e-leave__row {
      display: grid;
      grid-template-columns: 1fr 10rem 6rem 8rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }
  `,
})
export class EmployeeLeave {
  private readonly api = inject(LeaveApi);

  readonly employeeId = input.required<string>();

  protected readonly balances = signal<readonly LeaveBalance[]>([]);
  protected readonly requests = signal<readonly LeaveRequest[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const employeeId = this.employeeId();
      const year = new Date().getFullYear();
      Promise.all([
        this.api.balances(employeeId),
        this.api.requests({ employeeId, from: `${year}-01-01`, to: `${year}-12-31`, pageSize: 100 }),
      ]).then(
        ([balances, requests]) => {
          this.balances.set(balances);
          this.requests.set(requests.items);
        },
        (error: unknown) => this.error.set(error instanceof Error ? error.message : 'Leave could not be loaded.'),
      );
    });
  }
}
