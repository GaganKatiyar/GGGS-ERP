import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { InsightApi } from '@gggs/shared/insight';
import type { CalendarEntry, CalendarScope } from '@gggs/shared/models';
import { PageHeader } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const iso = (date: Date) => `${date.getFullYear()}-${`${date.getMonth() + 1}`.padStart(2, '0')}-${`${date.getDate()}`.padStart(2, '0')}`;

interface KindInfo {
  readonly key: string;
  readonly label: string;
  readonly colour: string;
}

const KINDS: readonly KindInfo[] = [
  { key: 'holiday', label: 'Holidays', colour: 'var(--gggs-cat-7)' },
  { key: 'leave', label: 'Leave', colour: 'var(--gggs-cat-3)' },
  { key: 'birthday', label: 'Birthdays', colour: 'var(--gggs-cat-5)' },
  { key: 'anniversary', label: 'Work anniversaries', colour: 'var(--gggs-cat-4)' },
  { key: 'joining', label: 'Joinings', colour: 'var(--gggs-cat-2)' },
  { key: 'exit', label: 'Last days', colour: 'var(--gggs-cat-9)' },
  { key: 'milestone', label: 'Milestones', colour: 'var(--gggs-cat-1)' },
  { key: 'interview', label: 'Interviews', colour: 'var(--gggs-cat-6)' },
  { key: 'review', label: 'Reviews', colour: 'var(--gggs-cat-8)' },
  { key: 'survey', label: 'Surveys', colour: 'var(--gggs-cat-10)' },
];

interface Day {
  readonly date: string;
  readonly day: number;
  readonly inMonth: boolean;
  readonly today: boolean;
  readonly weekend: boolean;
}

/**
 * The HR calendar (spec: HR calendar): a month of holidays, leave, birthdays, anniversaries,
 * joinings, last days, project milestones, interviews, review deadlines and survey closings - for
 * oneself, one's team, or the whole company.
 */
@Component({
  selector: 'gggs-calendar-page',
  imports: [RouterLink, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="HR calendar" [subtitle]="monthLabel()">
      <button type="button" class="btn" aria-label="Previous month" (click)="move(-1)"><i class="pi pi-chevron-left" aria-hidden="true"></i></button>
      <button type="button" class="btn" (click)="goToday()">Today</button>
      <button type="button" class="btn" aria-label="Next month" (click)="move(1)"><i class="pi pi-chevron-right" aria-hidden="true"></i></button>
      <select class="cal__scope" aria-label="Whose calendar" [value]="scope()" (change)="scope.set($any($event.target).value)">
        <option value="mine">My calendar</option>
        @if (canSeeTeam) {
          <option value="team">My team</option>
          <option value="all">Whole company</option>
        }
      </select>
    </gggs-page-header>
    <div class="toolbar cal__legend">
      @for (kind of kinds; track kind.key) {
        <label class="xp__flag">
          <input type="checkbox" [checked]="!hidden().includes(kind.key)" (change)="toggle(kind.key)" />
          <span class="cal__swatch" [style.background]="kind.colour"></span>
          {{ kind.label }}
        </label>
      }
    </div>
    <div class="cal">
      <div class="cal__grid" role="grid" [attr.aria-label]="monthLabel()">
        @for (name of weekdays; track name) {
          <div class="cal__weekday" role="columnheader">{{ name }}</div>
        }
        @for (day of days(); track day.date) {
          <button type="button" role="gridcell" class="cal__day" [class.cal__day--out]="!day.inMonth" [class.cal__day--today]="day.today" [class.cal__day--weekend]="day.weekend"
            [class.cal__day--selected]="day.date === selected()" (click)="selected.set(day.date)">
            <span class="cal__num">{{ day.day }}</span>
            @for (entry of entriesOn(day.date).slice(0, 3); track $index) {
              <span class="cal__chip" [style.borderInlineStartColor]="colour(entry.kind)" [title]="entry.title">{{ entry.title }}</span>
            }
            @if (entriesOn(day.date).length > 3) {
              <span class="cal__more">+{{ entriesOn(day.date).length - 3 }} more</span>
            }
          </button>
        }
      </div>
      <aside class="panel stack cal__agenda">
        <h2 class="xp__heading">{{ selectedLabel() }}</h2>
        @for (entry of entriesOn(selected()); track $index) {
          <div class="cal__entry" [style.borderInlineStartColor]="colour(entry.kind)">
            @if (entry.link) {
              <a [routerLink]="entry.link"><strong>{{ entry.title }}</strong></a>
            } @else {
              <strong>{{ entry.title }}</strong>
            }
            <span class="muted">{{ kindLabel(entry.kind) }}{{ entry.start !== entry.end ? ' · ' + entry.start + ' to ' + entry.end : '' }}{{ entry.detail ? ' · ' + entry.detail : '' }}</span>
          </div>
        } @empty {
          <p class="muted">Nothing on this day.</p>
        }
        <h2 class="xp__heading">Coming up</h2>
        @for (entry of upcoming(); track $index) {
          <div class="cal__entry" [style.borderInlineStartColor]="colour(entry.kind)">
            <strong>{{ entry.title }}</strong>
            <span class="muted">{{ entry.start }}</span>
          </div>
        } @empty {
          <p class="muted">Nothing else this month.</p>
        }
      </aside>
    </div>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .cal {
      display: grid;
      grid-template-columns: 1fr 20rem;
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .cal__grid {
      display: grid;
      grid-template-columns: repeat(7, minmax(0, 1fr));
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md, 0.5rem);
      overflow: hidden;
    }

    .cal__weekday {
      padding: var(--gggs-space-2);
      font-size: var(--gggs-font-size-sm);
      color: var(--gggs-color-text-muted);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .cal__day {
      display: flex;
      flex-direction: column;
      gap: 2px;
      min-height: 6.5rem;
      padding: var(--gggs-space-1);
      border: 0;
      border-inline-end: 1px solid var(--gggs-color-border);
      border-block-end: 1px solid var(--gggs-color-border);
      background: transparent;
      color: inherit;
      text-align: start;
      cursor: pointer;
      overflow: hidden;
    }

    .cal__day--out {
      opacity: 0.45;
    }

    .cal__day--weekend {
      background: color-mix(in srgb, var(--gggs-color-border) 25%, transparent);
    }

    .cal__day--selected {
      outline: 2px solid var(--gggs-color-primary);
      outline-offset: -2px;
    }

    .cal__day--today .cal__num {
      background: var(--gggs-color-primary);
      color: var(--gggs-color-primary-contrast);
      border-radius: 999px;
      padding-inline: 0.4rem;
    }

    .cal__num {
      align-self: flex-start;
      font-weight: 600;
      font-size: var(--gggs-font-size-sm);
    }

    .cal__chip {
      font-size: 0.72rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      border-inline-start: 3px solid;
      padding-inline-start: 0.25rem;
    }

    .cal__more {
      font-size: 0.72rem;
      color: var(--gggs-color-text-muted);
    }

    .cal__entry {
      display: grid;
      gap: 2px;
      border-inline-start: 3px solid;
      padding-inline-start: var(--gggs-space-2);
    }

    .cal__swatch {
      display: inline-block;
      width: 0.75rem;
      height: 0.75rem;
      border-radius: 2px;
    }

    .cal__legend {
      flex-wrap: wrap;
      margin-block-end: var(--gggs-space-3);
    }

    .cal__scope {
      width: auto;
    }

    @media (max-width: 64rem) {
      .cal {
        grid-template-columns: 1fr;
      }

      .cal__chip {
        display: none;
      }

      .cal__day {
        min-height: 3rem;
      }
    }
  `,
})
export class CalendarPage {
  private readonly api = inject(InsightApi);

  protected readonly canSeeTeam = inject(PermissionStore).has('employee.profile.view');
  protected readonly kinds = KINDS;
  protected readonly weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  protected readonly month = signal(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  protected readonly scope = signal<CalendarScope>('mine');
  protected readonly hidden = signal<readonly string[]>([]);
  protected readonly selected = signal(iso(new Date()));
  protected readonly entries = signal<readonly CalendarEntry[]>([]);
  protected readonly error = signal<string | null>(null);

  protected readonly monthLabel = computed(() => this.month().toLocaleDateString(undefined, { month: 'long', year: 'numeric' }));
  protected readonly selectedLabel = computed(() => new Date(`${this.selected()}T00:00:00`).toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' }));

  /** Six weeks from the Monday on or before the first of the month. */
  protected readonly days = computed<readonly Day[]>(() => {
    const first = this.month();
    const start = new Date(first);
    start.setDate(first.getDate() - ((first.getDay() + 6) % 7));
    const today = iso(new Date());
    return Array.from({ length: 42 }, (_, index) => {
      const date = new Date(start);
      date.setDate(start.getDate() + index);
      return { date: iso(date), day: date.getDate(), inMonth: date.getMonth() === first.getMonth(), today: iso(date) === today, weekend: date.getDay() === 0 || date.getDay() === 6 };
    });
  });

  private readonly visible = computed(() => this.entries().filter((entry) => !this.hidden().includes(entry.kind)));
  private readonly byDay = computed(() => {
    const map = new Map<string, CalendarEntry[]>();
    for (const entry of this.visible()) {
      for (let day = new Date(`${entry.start}T00:00:00`); iso(day) <= entry.end; day.setDate(day.getDate() + 1)) {
        const key = iso(day);
        map.set(key, [...(map.get(key) ?? []), entry]);
      }
    }

    return map;
  });

  protected readonly upcoming = computed(() => {
    const today = iso(new Date());
    return this.visible().filter((entry) => entry.start > today && entry.start > this.selected()).slice(0, 8);
  });

  constructor() {
    effect(() => {
      const days = this.days();
      const scope = this.scope();
      this.error.set(null);
      this.api.calendar(days[0].date, days[days.length - 1].date, scope).then(
        (view) => this.entries.set(view.entries),
        (error: unknown) => {
          this.entries.set([]);
          this.error.set(message(error, 'The calendar could not be loaded.'));
        },
      );
    });
  }

  protected entriesOn(date: string): readonly CalendarEntry[] {
    return this.byDay().get(date) ?? [];
  }

  protected colour(kind: string): string {
    return KINDS.find((candidate) => candidate.key === kind)?.colour ?? 'var(--gggs-cat-9)';
  }

  protected kindLabel(kind: string): string {
    return KINDS.find((candidate) => candidate.key === kind)?.label ?? kind;
  }

  protected toggle(kind: string): void {
    this.hidden.update((hidden) => (hidden.includes(kind) ? hidden.filter((key) => key !== kind) : [...hidden, kind]));
  }

  protected move(months: number): void {
    this.month.update((month) => new Date(month.getFullYear(), month.getMonth() + months, 1));
  }

  protected goToday(): void {
    const now = new Date();
    this.month.set(new Date(now.getFullYear(), now.getMonth(), 1));
    this.selected.set(iso(now));
  }
}
