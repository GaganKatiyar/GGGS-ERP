import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import type { LeaveRequest } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { LeaveApi, addDays, todayIso } from '@gggs/shared/time';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

interface CalendarDay {
  readonly date: string;
  readonly isWeekend: boolean;
  readonly people: readonly LeaveRequest[];
}

/** Who is away, day by day, for the next weeks - approved and still pending. */
@Component({
  selector: 'gggs-team-leave-page',
  imports: [DatePipe, DecimalPipe, ReactiveFormsModule, PageHeader, OrgSelect, StatusBadge, Skeleton, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Team leave" subtitle="Who is away over the coming weeks." />

    <section class="panel toolbar calendar__filters">
      <label class="field">
        <span class="field__label">From</span>
        <input type="date" [value]="from()" (change)="setFrom($event)" />
      </label>
      <label class="field calendar__department">
        <span class="field__label">Department</span>
        <gggs-org-select kind="departments" placeholder="Everyone I can see" ariaLabel="Department" [formControl]="department" />
      </label>
    </section>

    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="Leave could not be loaded" [description]="error()!" />
    } @else if (loading()) {
      <gggs-skeleton [lines]="8" />
    } @else if (requests().length === 0) {
      <gggs-empty-state icon="pi pi-sun" title="Nobody is away" description="No approved or pending leave in these four weeks." />
    } @else {
      <div class="stack">
        <section class="panel">
          <ol class="calendar">
            @for (day of days(); track day.date) {
              <li class="calendar__day" [class.calendar__day--weekend]="day.isWeekend">
                <span class="calendar__date">{{ day.date | date: 'EEE d' }}</span>
                @for (person of day.people; track person.id) {
                  <span class="calendar__chip" [style.border-color]="person.color" [class.calendar__chip--pending]="person.status === 'Pending'" [title]="person.leaveTypeName + (person.status === 'Pending' ? ' (pending)' : '')">
                    {{ person.employeeName }}
                  </span>
                }
              </li>
            }
          </ol>
        </section>

        <section class="panel">
          <table class="table grid">
            <thead>
              <tr>
                <th scope="col">Employee</th>
                <th scope="col">Type</th>
                <th scope="col">Dates</th>
                <th scope="col">Days</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (request of requests(); track request.id) {
                <tr>
                  <td>{{ request.employeeName }}</td>
                  <td>{{ request.leaveTypeName }}</td>
                  <td>{{ request.startDate | date: 'd MMM' }} – {{ request.endDate | date: 'd MMM' }}</td>
                  <td>{{ request.days | number: '1.0-1' }}</td>
                  <td><gggs-status-badge [status]="request.status" /></td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      </div>
    }
  `,
  styles: `
    .calendar__filters {
      align-items: flex-end;
      margin-block-end: var(--gggs-space-3);
    }

    .calendar__department {
      min-width: 16rem;
    }

    .calendar {
      display: grid;
      grid-template-columns: repeat(7, minmax(0, 1fr));
      gap: var(--gggs-space-1, 0.25rem);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    @media (max-width: 48rem) {
      .calendar {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }

    .calendar__day {
      display: flex;
      flex-direction: column;
      gap: 0.125rem;
      min-height: 4.5rem;
      padding: var(--gggs-space-1, 0.25rem);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-sm);
    }

    .calendar__day--weekend {
      background: var(--gggs-color-surface-hover);
    }

    .calendar__date {
      font-size: var(--gggs-font-size-xs);
      color: var(--gggs-color-text-muted);
    }

    .calendar__chip {
      overflow: hidden;
      padding: 0 0.25rem;
      border-inline-start: 3px solid;
      font-size: var(--gggs-font-size-xs);
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    .calendar__chip--pending {
      opacity: 0.6;
      font-style: italic;
    }


  `,
})
export class TeamLeavePage {
  private readonly api = inject(LeaveApi);

  protected readonly from = signal(this.mondayOf(todayIso()));
  protected readonly department = new FormControl<string | null>(null);
  protected readonly requests = signal<readonly LeaveRequest[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly days = computed<readonly CalendarDay[]>(() => {
    const result: CalendarDay[] = [];
    for (let offset = 0; offset < 28; offset++) {
      const date = addDays(this.from(), offset);
      const weekday = new Date(`${date}T00:00:00Z`).getUTCDay();
      result.push({
        date,
        isWeekend: weekday === 0 || weekday === 6,
        people: this.requests().filter((request) => request.startDate <= date && request.endDate >= date),
      });
    }
    return result;
  });

  constructor() {
    this.department.valueChanges.subscribe(() => void this.load());
    void this.load();
  }

  protected setFrom(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.from.set(this.mondayOf(value));
      void this.load();
    }
  }

  private mondayOf(iso: string): string {
    const weekday = new Date(`${iso}T00:00:00Z`).getUTCDay();
    return addDays(iso, weekday === 0 ? -6 : 1 - weekday);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.requests.set(await this.api.calendar(this.from(), addDays(this.from(), 27), this.department.value));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    } finally {
      this.loading.set(false);
    }
  }
}
