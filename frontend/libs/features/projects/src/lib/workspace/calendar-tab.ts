import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { CalendarEvent } from '@gggs/shared/models';

interface Day {
  readonly iso: string;
  readonly day: number;
  readonly inMonth: boolean;
  readonly isToday: boolean;
  readonly events: readonly CalendarEvent[];
}

const pad = (value: number) => String(value).padStart(2, '0');
const iso = (date: Date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;

/** The project calendar (spec §56): phases, milestones, sprints, meetings and deadlines on a month grid. */
@Component({
  selector: 'gggs-calendar-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <button type="button" class="btn btn--sm" aria-label="Previous month" (click)="shift(-1)"><i class="pi pi-chevron-left" aria-hidden="true"></i></button>
        <h2 class="cal__heading">{{ monthLabel() }}</h2>
        <button type="button" class="btn btn--sm" aria-label="Next month" (click)="shift(1)"><i class="pi pi-chevron-right" aria-hidden="true"></i></button>
        <button type="button" class="btn btn--link btn--sm" (click)="today()">Today</button>
        <span class="cal__spacer"></span>
        @for (kind of kinds; track kind) {
          <span class="cal__legend"><span class="cal__dot" [attr.data-kind]="kind"></span>{{ kind }}</span>
        }
      </div>
      <div class="cal" role="group" [attr.aria-label]="monthLabel()">
        @for (name of weekdays; track name) {
          <div class="cal__weekday" aria-hidden="true">{{ name }}</div>
        }
        @for (day of days(); track day.iso) {
          <div class="cal__day" [class.cal__day--out]="!day.inMonth" [class.cal__day--today]="day.isToday">
            <span class="cal__date">{{ day.day }}</span>
            @for (event of day.events.slice(0, 4); track event.kind + event.id) {
              <span class="cal__event" [attr.data-kind]="event.kind" [title]="event.kind + ': ' + event.title + (event.detail ? ' (' + event.detail + ')' : '')">
                {{ event.detail && event.kind === 'Meeting' ? event.detail + ' ' : '' }}{{ event.title }}
              </span>
            }
            @if (day.events.length > 4) {
              <span class="muted cal__more">+{{ day.events.length - 4 }} more</span>
            }
          </div>
        }
      </div>
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styles: `
    .cal__heading {
      margin: 0;
      min-width: 10rem;
      text-align: center;
      font-size: var(--gggs-font-size-md);
    }

    .cal__spacer {
      flex: 1;
    }

    .cal__legend {
      display: inline-flex;
      gap: var(--gggs-space-1);
      align-items: center;
      font-size: var(--gggs-font-size-xs);
    }

    .cal {
      display: grid;
      grid-template-columns: repeat(7, minmax(0, 1fr));
      border-block-start: 1px solid var(--gggs-color-border);
      border-inline-start: 1px solid var(--gggs-color-border);
    }

    .cal__weekday {
      padding: var(--gggs-space-1);
      font-size: var(--gggs-font-size-xs);
      color: var(--gggs-color-text-muted);
      border-inline-end: 1px solid var(--gggs-color-border);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .cal__day {
      display: flex;
      flex-direction: column;
      gap: 2px;
      min-height: 6.5rem;
      padding: var(--gggs-space-1);
      border-inline-end: 1px solid var(--gggs-color-border);
      border-block-end: 1px solid var(--gggs-color-border);
      overflow: hidden;
    }

    .cal__day--out {
      background: var(--gggs-color-surface-sunken);
      color: var(--gggs-color-text-subtle);
    }

    .cal__day--today .cal__date {
      color: var(--gggs-color-primary);
      font-weight: var(--gggs-font-weight-semibold);
    }

    .cal__date {
      font-size: var(--gggs-font-size-xs);
    }

    .cal__event {
      padding: 1px 4px;
      border-radius: 4px;
      font-size: var(--gggs-font-size-xs);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      background: var(--gggs-color-primary-subtle);
    }

    .cal__dot {
      width: 0.6rem;
      height: 0.6rem;
      border-radius: 50%;
      background: var(--gggs-color-primary-subtle);
    }

    [data-kind='Milestone'] {
      background: var(--gggs-color-warning-subtle);
      color: var(--gggs-color-warning-strong);
    }

    [data-kind='Sprint'] {
      background: var(--gggs-color-accent-subtle);
      color: var(--gggs-color-accent);
    }

    [data-kind='Meeting'] {
      background: var(--gggs-color-info-subtle);
      color: var(--gggs-color-info);
    }

    [data-kind='Phase'] {
      background: var(--gggs-color-success-subtle);
      color: var(--gggs-color-success);
    }

    [data-kind='Task'] {
      background: var(--gggs-color-surface-sunken);
      color: var(--gggs-color-text-muted);
    }

    .cal__more {
      font-size: var(--gggs-font-size-xs);
    }
  `,
})
export class CalendarTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();

  protected readonly weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  protected readonly kinds = ['Phase', 'Milestone', 'Sprint', 'Meeting', 'Task'];
  protected readonly month = signal(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  protected readonly events = signal<readonly CalendarEvent[]>([]);
  protected readonly error = signal<string | null>(null);

  protected readonly monthLabel = computed(() => this.month().toLocaleDateString(undefined, { month: 'long', year: 'numeric' }));

  /** Six weeks from the Monday on or before the first of the month. */
  private readonly range = computed(() => {
    const first = this.month();
    const offset = (first.getDay() + 6) % 7;
    const start = new Date(first.getFullYear(), first.getMonth(), 1 - offset);
    const end = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 41);
    return { start, end };
  });

  protected readonly days = computed<readonly Day[]>(() => {
    const { start } = this.range();
    const month = this.month().getMonth();
    const todayIso = iso(new Date());
    return Array.from({ length: 42 }, (_, index) => {
      const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + index);
      const dayIso = iso(date);
      return {
        iso: dayIso,
        day: date.getDate(),
        inMonth: date.getMonth() === month,
        isToday: dayIso === todayIso,
        events: this.events().filter((event) => event.start <= dayIso && event.end >= dayIso),
      };
    });
  });

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      const { start, end } = this.range();
      void this.load(projectId, iso(start), iso(end));
    });
  }

  protected shift(months: number): void {
    const current = this.month();
    this.month.set(new Date(current.getFullYear(), current.getMonth() + months, 1));
  }

  protected today(): void {
    this.month.set(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  }

  private async load(projectId: string, from: string, to: string): Promise<void> {
    this.error.set(null);
    try {
      this.events.set(await this.api.calendar(projectId, from, to));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The calendar could not be loaded.');
    }
  }
}
