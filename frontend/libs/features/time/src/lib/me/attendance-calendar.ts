import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output } from '@angular/core';
import { HasPermissionDirective } from '@gggs/core/permissions';
import type { AttendanceDay, AttendanceStatus, ScheduledDay } from '@gggs/shared/models';
import { formatMinutes, shortTime } from '@gggs/shared/models';
import { DEFAULT_STATUS_VARIANTS, StatusBadge, type BadgeVariant } from '@gggs/shared/ui';
import { calendarMonth, isFixable, type CalendarCell } from './attendance-month';

const LABELS: Readonly<Record<AttendanceStatus, string>> = {
  Present: 'Present',
  Absent: 'Absent',
  MissingPunch: 'Missing punch',
  HalfDay: 'Half day',
  OnLeave: 'On leave',
  Holiday: 'Holiday',
  WeeklyOff: 'Weekly off',
};

/** What a square shows on a phone, where a full status does not fit. The full status is in its label and details. */
const SHORT_LABELS: Readonly<Record<AttendanceStatus, string>> = {
  Present: 'P',
  Absent: 'A',
  MissingPunch: 'MP',
  HalfDay: 'HD',
  OnLeave: 'L',
  Holiday: 'H',
  WeeklyOff: 'Off',
};

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

/**
 * A month of attendance as a calendar: each square says how the day counted, or what is scheduled if
 * it is still to come, and choosing a square shows its details beside the grid.
 */
@Component({
  selector: 'gggs-attendance-calendar',
  imports: [DatePipe, HasPermissionDirective, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="calendar">
      <div class="calendar__month">
        <div class="calendar__weekdays" aria-hidden="true">
          @for (weekday of weekdays; track weekday) {
            <span>{{ weekday }}</span>
          }
        </div>
        <div class="calendar__days" role="group" [attr.aria-label]="month() + '-01' | date: 'MMMM y'">
          @for (blank of blanks(); track $index) {
            <span class="day day--blank" aria-hidden="true"></span>
          }
          @for (cell of grid().cells; track cell.date) {
            <button
              type="button"
              class="day"
              [class.day--quiet]="isQuiet(cell)"
              [class.day--future]="cell.isFuture"
              [class.day--today]="cell.isToday"
              [attr.aria-pressed]="selected() === cell.date"
              [attr.aria-label]="describe(cell)"
              (click)="selected.set(cell.date)"
            >
              <span class="day__top">
                <span class="day__number">{{ cell.dayOfMonth }}</span>
                @if (cell.recorded?.isRegularized) {
                  <span class="day__corrected">corrected</span>
                }
              </span>
              @if (cell.recorded; as day) {
                <span class="day__tag" [attr.data-tone]="tone(day.status)">
                  <span class="day__tag-long">{{ labels[day.status] }}</span>
                  <span class="day__tag-short">{{ shortLabels[day.status] }}</span>
                </span>
              }
              @if (line(cell); as text) {
                <span class="day__line">{{ text }}</span>
              }
              @if (cell.recorded?.lateMinutes; as late) {
                <span class="day__line">Late {{ formatMinutes(late) }}</span>
              }
            </button>
          }
        </div>
      </div>

      <aside class="details" aria-live="polite" aria-label="Day details">
        @if (selectedCell(); as cell) {
          <h2 class="details__heading">{{ cell.date | date: 'EEE d MMMM' }}{{ cell.isToday ? ' · Today' : '' }}</h2>
          @if (cell.recorded; as day) {
            <gggs-status-badge [status]="day.status" />
            @if (hasTimes(day)) {
              <dl class="details__facts">
                <dt>Shift</dt>
                <dd>{{ shortTime(day.scheduledStart) }}–{{ shortTime(day.scheduledEnd) }}</dd>
                <dt>In</dt>
                <dd>{{ shortTime(day.firstIn) || '—' }}</dd>
                <dt>Out</dt>
                <dd>{{ shortTime(day.lastOut) || '—' }}</dd>
                <dt>Worked</dt>
                <dd>{{ formatMinutes(day.workedMinutes) || '—' }}</dd>
                <dt>Late</dt>
                <dd>{{ day.lateMinutes ? formatMinutes(day.lateMinutes) : '—' }}</dd>
              </dl>
            }
            @if (day.note) {
              <p class="details__note">{{ day.note }}</p>
            }
            @if (day.isRegularized) {
              <p class="details__note">This day was corrected.</p>
            }
            @if (fixable(day)) {
              <button type="button" class="btn btn--block" *gggsHasPermission="'attendance.regularization.create'" (click)="fix.emit(day)">Fix this day</button>
            }
          } @else if (cell.scheduled; as shift) {
            <p class="details__note">{{ shift.isWeeklyOff ? 'A day off.' : 'Scheduled: ' + shift.shiftName + ', ' + shortTime(shift.start) + '–' + shortTime(shift.end) + '.' }}</p>
          } @else {
            <p class="details__note">Nothing recorded for this day.</p>
          }
        } @else {
          <p class="details__note">Choose a day to see its details.</p>
        }
      </aside>
    </div>
  `,
  styles: `
    .calendar {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 17rem;
      align-items: stretch;
    }

    .calendar__month {
      min-width: 0;
      padding: var(--gggs-space-3);
    }

    .calendar__weekdays,
    .calendar__days {
      display: grid;
      grid-template-columns: repeat(7, minmax(0, 1fr));
      gap: var(--gggs-space-1);
    }

    .calendar__weekdays {
      margin-block-end: var(--gggs-space-1);
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-xs);
      font-weight: var(--gggs-font-weight-semibold);
      letter-spacing: 0.03em;
      text-align: center;
      text-transform: uppercase;
    }

    .day {
      display: flex;
      flex-direction: column;
      gap: 0.125rem;
      min-width: 0;
      min-height: 4.75rem;
      padding: 0.3125rem 0.4375rem;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
      color: var(--gggs-color-text);
      font: inherit;
      font-size: var(--gggs-font-size-xs);
      line-height: 1.3;
      text-align: start;
      cursor: pointer;

      &:hover {
        border-color: var(--gggs-color-border-strong);
        background: var(--gggs-color-surface-sunken);
      }

      &:focus-visible {
        outline: none;
        box-shadow: var(--gggs-focus-ring);
      }
    }

    .day--blank {
      visibility: hidden;
    }

    .day--quiet,
    .day--future {
      background: var(--gggs-color-surface-sunken);
    }

    .day--future {
      border-style: dashed;
    }

    .day--today {
      border-color: var(--gggs-color-primary);
      box-shadow: inset 0 0 0 1px var(--gggs-color-primary);
    }

    .day[aria-pressed='true'] {
      border-color: var(--gggs-color-text);
      box-shadow: inset 0 0 0 1px var(--gggs-color-text);
    }

    .day__top {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--gggs-space-1);
    }

    .day__number {
      font-size: var(--gggs-font-size-sm);
      font-weight: var(--gggs-font-weight-semibold);
      font-variant-numeric: tabular-nums;
    }

    .day__corrected,
    .day__line {
      overflow: hidden;
      color: var(--gggs-color-text-muted);
      font-variant-numeric: tabular-nums;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .day__tag {
      display: block;
      width: fit-content;
      max-width: 100%;
      overflow: hidden;
      padding: 0 0.375rem;
      border-radius: var(--gggs-radius-sm);
      font-weight: var(--gggs-font-weight-semibold);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .day__tag-short {
      display: none;
    }

    [data-tone='success'] {
      background: var(--gggs-color-success-subtle);
      color: var(--gggs-color-success);
    }

    [data-tone='danger'] {
      background: var(--gggs-color-danger-subtle);
      color: var(--gggs-color-danger);
    }

    [data-tone='warning'] {
      background: var(--gggs-color-warning-subtle);
      color: var(--gggs-color-warning-strong);
    }

    [data-tone='primary'] {
      background: var(--gggs-color-info-subtle);
      color: var(--gggs-color-info);
    }

    [data-tone='neutral'] {
      background: var(--gggs-color-border);
      color: var(--gggs-color-text-muted);
    }

    .details {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--gggs-space-2);
      min-width: 0;
      padding: var(--gggs-space-4);
      border-inline-start: 1px solid var(--gggs-color-border);
    }

    .details__heading {
      margin: 0;
      font-family: var(--gggs-font-family-display);
      font-size: var(--gggs-font-size-md);
      font-weight: var(--gggs-font-weight-bold);
    }

    .details__facts {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr);
      gap: var(--gggs-space-1) var(--gggs-space-3);
      width: 100%;
      margin: 0;
      font-variant-numeric: tabular-nums;

      dt {
        color: var(--gggs-color-text-muted);
      }

      dd {
        margin: 0;
        text-align: end;
      }
    }

    .details__note {
      margin: 0;
      color: var(--gggs-color-text-muted);
    }

    @media (max-width: 58rem) {
      .calendar {
        grid-template-columns: minmax(0, 1fr);
      }

      .details {
        border-block-start: 1px solid var(--gggs-color-border);
        border-inline-start: 0;
      }
    }

    @media (max-width: 40rem) {
      .calendar__month {
        padding: var(--gggs-space-2);
      }

      .day {
        align-items: center;
        min-height: 3.125rem;
        padding: var(--gggs-space-1) 0.125rem;
      }

      .day__top {
        justify-content: center;
      }

      .day__corrected,
      .day__line,
      .day__tag-long {
        display: none;
      }

      .day__tag {
        padding-inline: 0.3125rem;
      }

      .day__tag-short {
        display: inline;
      }
    }
  `,
})
export class AttendanceCalendar {
  /** The month shown, as `yyyy-MM`. */
  readonly month = input.required<string>();
  readonly days = input.required<readonly AttendanceDay[]>();
  /** The shifts for days still to come. */
  readonly scheduled = input<readonly ScheduledDay[]>([]);
  readonly today = input.required<string>();
  /** The person asked to correct this day. */
  readonly fix = output<AttendanceDay>();

  protected readonly weekdays = WEEKDAYS;
  protected readonly labels = LABELS;
  protected readonly shortLabels = SHORT_LABELS;
  protected readonly formatMinutes = formatMinutes;
  protected readonly shortTime = shortTime;
  protected readonly fixable = isFixable;

  protected readonly grid = computed(() => calendarMonth(this.month(), this.days(), this.scheduled(), this.today()));
  protected readonly blanks = computed(() => Array.from({ length: this.grid().leadingBlanks }));

  /** Opens on today in the current month, and on nothing in any other. */
  protected readonly selected = linkedSignal<string, string | null>({
    source: this.month,
    computation: (month) => (this.today().startsWith(month) ? this.today() : null),
  });
  protected readonly selectedCell = computed(() => this.grid().cells.find((cell) => cell.date === this.selected()) ?? null);

  /** The colour family a status has on its badge everywhere else. */
  protected tone(status: AttendanceStatus): BadgeVariant {
    return DEFAULT_STATUS_VARIANTS[status.toLowerCase()] ?? 'neutral';
  }

  protected isQuiet(cell: CalendarCell): boolean {
    return cell.recorded?.status === 'WeeklyOff' || cell.recorded?.status === 'Holiday';
  }

  protected hasTimes(day: AttendanceDay): boolean {
    return day.status !== 'WeeklyOff' && day.status !== 'Holiday' && day.status !== 'OnLeave';
  }

  /** The times under a square's status, or what it is for when it has none. */
  protected line(cell: CalendarCell): string {
    const day = cell.recorded;
    if (day) {
      const first = shortTime(day.firstIn);
      const last = shortTime(day.lastOut);
      return first && last ? `${first}–${last}` : first ? `In ${first}` : (day.status === 'OnLeave' || day.status === 'Holiday' ? (day.note ?? '') : '');
    }

    const shift = cell.scheduled;
    return shift ? (shift.isWeeklyOff ? 'Day off' : `${shortTime(shift.start)}–${shortTime(shift.end)}`) : '';
  }

  protected describe(cell: CalendarCell): string {
    const when = new Intl.DateTimeFormat('en-GB', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' }).format(new Date(`${cell.date}T00:00:00Z`));
    const what = cell.recorded ? LABELS[cell.recorded.status] : cell.scheduled ? (cell.scheduled.isWeeklyOff ? 'Day off' : 'Scheduled') : 'Nothing recorded';
    return `${when}${cell.isToday ? ', today' : ''}, ${what}`;
  }
}
