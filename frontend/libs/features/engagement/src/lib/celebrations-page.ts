import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal, type OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EngagementApi } from '@gggs/shared/experience';
import type { CelebrationKind, CelebrationRow } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, localIsoDate, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/** The date ranges the screen offers, each worked out from the company's today. */
type RangeKey = 'today' | 'week' | 'month' | 'late';

interface RangeOption {
  readonly key: RangeKey;
  readonly label: string;
}

const RANGES: readonly RangeOption[] = [
  { key: 'today', label: 'Today' },
  { key: 'week', label: 'Next 7 days' },
  { key: 'month', label: 'This month' },
  { key: 'late', label: 'Missed (last 7 days)' },
];

/** What changes between the two screens. */
interface KindCopy {
  readonly title: string;
  readonly subtitle: string;
  readonly icon: string;
  readonly occasion: string;
  readonly empty: string;
}

const COPY: Record<CelebrationKind, KindCopy> = {
  Birthday: {
    title: 'Birthday wishes',
    subtitle: 'Wish people on their birthday. They get an email and a notification, and their manager a heads-up.',
    icon: 'pi pi-gift',
    occasion: 'birthday',
    empty: 'Nobody has a birthday in this range, or their date of birth is not on their record.',
  },
  WorkAnniversary: {
    title: 'Work anniversaries',
    subtitle: 'Congratulate people on their years with the company. They get an email and a notification, and their manager a heads-up.',
    icon: 'pi pi-calendar',
    occasion: 'work anniversary',
    empty: 'Nobody completes another year with the company in this range.',
  },
};

/** Days added to an ISO date, in plain calendar arithmetic. */
function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function rangeFor(key: RangeKey, today: string): { from: string; to: string } {
  switch (key) {
    case 'week':
      return { from: today, to: addDays(today, 6) };
    case 'month': {
      const [year, month] = today.split('-').map(Number);
      const last = new Date(Date.UTC(year, month, 0)).getUTCDate();
      return { from: `${today.slice(0, 7)}-01`, to: `${today.slice(0, 7)}-${String(last).padStart(2, '0')}` };
    }
    case 'late':
      return { from: addDays(today, -7), to: addDays(today, -1) };
    default:
      return { from: today, to: today };
  }
}

function message(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback;
}

/**
 * Birthday wishes and work anniversaries (HR Portal): who celebrates in a range, who has been wished,
 * and a way to wish them - one person or everyone due - with an optional personal note. Nothing goes
 * out by itself; a wish can be sent on the day or up to a week late, once.
 */
@Component({
  selector: 'gggs-celebrations-page',
  imports: [DatePipe, FormsModule, RouterLink, PageHeader, KpiCard, EmptyState, Skeleton, StatusBadge, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header [title]="copy().title" [subtitle]="copy().subtitle">
      <div class="toolbar">
        <a class="btn" routerLink="/admin/notifications"><i class="pi pi-pencil" aria-hidden="true"></i> Edit wording</a>
        <button type="button" class="btn btn--primary" [disabled]="due().length === 0" (click)="openSend(due())">
          <i [class]="copy().icon" aria-hidden="true"></i> Wish everyone due ({{ due().length }})
        </button>
      </div>
    </gggs-page-header>

    <div class="celebrations__ranges" role="group" aria-label="Date range">
      @for (option of ranges; track option.key) {
        <button type="button" class="btn btn--sm" [class.btn--primary]="range() === option.key" [attr.aria-pressed]="range() === option.key" (click)="choose(option.key)">
          {{ option.label }}
        </button>
      }
    </div>

    <div class="xp__kpis">
      <gggs-kpi-card [label]="kind() === 'Birthday' ? 'Birthdays' : 'Anniversaries'" [icon]="copy().icon" [value]="rows().length" />
      <gggs-kpi-card label="Ready to wish" icon="pi pi-send" [value]="due().length" hint="On the day, or up to a week late" />
      <gggs-kpi-card label="Already wished" icon="pi pi-check-circle" [value]="wishedCount()" />
    </div>

    @if (notice()) {
      <p class="alert alert--success" role="status"><i class="pi pi-check-circle" aria-hidden="true"></i><span>{{ notice() }}</span></p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="4" />
    } @else if (rows().length === 0) {
      <gggs-empty-state [icon]="copy().icon" [title]="'No ' + copy().occasion + 's'" [description]="copy().empty" />
    } @else {
      <section class="panel table-wrap">
        <table class="table celebrations__table">
          <thead>
            <tr>
              <th scope="col">Employee</th>
              <th scope="col">Department</th>
              <th scope="col">Manager</th>
              <th scope="col">Date</th>
              @if (kind() === 'WorkAnniversary') {
                <th scope="col">Years</th>
              }
              <th scope="col">Status</th>
              <th scope="col"><span class="celebrations__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.employeeId + row.date) {
              <tr>
                <td>
                  <a [routerLink]="['/employees', row.employeeId]"><strong>{{ row.employeeName }}</strong></a>
                  <div class="muted celebrations__code">{{ row.employeeCode }}</div>
                </td>
                <td>{{ row.departmentName ?? '—' }}</td>
                <td>{{ row.managerName ?? '—' }}</td>
                <td>
                  {{ row.date | date: 'EEE d MMM' }}
                  @if (row.date === today()) {
                    <span class="celebrations__today">Today</span>
                  }
                </td>
                @if (kind() === 'WorkAnniversary') {
                  <td>{{ row.years }} {{ row.years === 1 ? 'year' : 'years' }}</td>
                }
                <td>
                  @if (row.wishedAt) {
                    <gggs-status-badge status="Sent" />
                    <div class="muted celebrations__code">{{ row.wishedAt | date: 'd MMM, h:mm a' }} · {{ row.wishedBy }}</div>
                  } @else if (row.date > today()) {
                    <gggs-status-badge status="Upcoming" />
                  } @else if (row.canSend) {
                    <gggs-status-badge status="Pending" />
                  } @else {
                    <gggs-status-badge status="Missed" />
                  }
                </td>
                <td>
                  @if (row.canSend) {
                    <button type="button" class="btn btn--sm" (click)="openSend([row])"><i class="pi pi-send" aria-hidden="true"></i> Send wish</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }

    <gggs-form-dialog
      [(visible)]="sendOpen"
      [title]="selection().length === 1 ? 'Wish ' + selection()[0].employeeName : 'Wish ' + selection().length + ' people'"
      saveLabel="Send wish"
      [busy]="busy()"
      [error]="sendError()"
      (save)="send()"
    >
      <div class="stack">
        <p class="celebrations__who">
          @if (selection().length === 1) {
            <strong>{{ selection()[0].employeeName }}</strong> gets the {{ copy().occasion }} wish by email and in the app.
            @if (selection()[0].managerName) {
              Their manager, <strong>{{ selection()[0].managerName }}</strong>, gets a heads-up.
            }
          } @else {
            Each of them gets the {{ copy().occasion }} wish by email and in the app, and their managers a heads-up.
          }
        </p>
        <label class="field">
          <span class="field__label">Personal note <span class="muted">(optional, added to the wish)</span></span>
          <textarea rows="4" maxlength="500" [(ngModel)]="note" name="note" placeholder="Have a wonderful day!"></textarea>
          <span class="muted celebrations__count">{{ note().length }}/500</span>
        </label>
        <p class="muted">The standard wording is under <a routerLink="/admin/notifications">Studio › Email templates</a>.</p>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .celebrations__ranges {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
    }

    .celebrations__code,
    .celebrations__count {
      font-size: var(--gggs-font-size-xs);
    }

    .celebrations__today {
      margin-left: var(--gggs-space-2);
      padding: 0 var(--gggs-space-2);
      border-radius: 999px;
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-size: var(--gggs-font-size-xs);
      font-weight: 600;
    }

    .celebrations__who {
      margin: 0;
    }

    .celebrations__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `,
})
export class CelebrationsPage implements OnInit {
  private readonly api = inject(EngagementApi);

  /** Bound from route data: which occasion this screen is for. */
  readonly kind = input<CelebrationKind>('Birthday');

  protected readonly ranges = RANGES;
  protected readonly copy = computed(() => COPY[this.kind()]);
  protected readonly range = signal<RangeKey>('today');
  protected readonly today = signal(localIsoDate());
  protected readonly rows = signal<readonly CelebrationRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly due = computed(() => this.rows().filter((row) => row.canSend));
  protected readonly wishedCount = computed(() => this.rows().filter((row) => !!row.wishedAt).length);

  protected readonly sendOpen = signal(false);
  protected readonly selection = signal<readonly CelebrationRow[]>([]);
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly sendError = signal<string | null>(null);

  /** The route's data (the kind) is bound by now, so the first load asks for the right one. */
  ngOnInit(): void {
    void this.load();
  }

  protected choose(key: RangeKey): void {
    this.range.set(key);
    this.notice.set(null);
    void this.load();
  }

  protected openSend(rows: readonly CelebrationRow[]): void {
    if (rows.length === 0) {
      return;
    }

    this.selection.set(rows);
    this.note.set('');
    this.sendError.set(null);
    this.sendOpen.set(true);
  }

  protected async send(): Promise<void> {
    this.busy.set(true);
    this.sendError.set(null);
    try {
      const note = this.note().trim();
      const result = await this.api.sendWishes({
        kind: this.kind(),
        items: this.selection().map((row) => ({ employeeId: row.employeeId, date: row.date })),
        note: note || null,
      });
      this.sendOpen.set(false);
      const skipped = result.skipped.length ? ` ${result.skipped.length} skipped: ${result.skipped.map((item) => item.reason).join(' ')}` : '';
      this.notice.set(`${result.sent === 1 ? '1 wish' : `${result.sent} wishes`} sent.${skipped}`);
      await this.load();
    } catch (error) {
      this.sendError.set(message(error, 'The wish could not be sent. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const { from, to } = rangeFor(this.range(), this.today());
      const list = await this.api.celebrations(this.kind(), from, to);
      if (list.today !== this.today()) {
        // The company's day differs from this browser's: ask again for the company's range.
        this.today.set(list.today);
        const fixed = rangeFor(this.range(), list.today);
        this.rows.set((await this.api.celebrations(this.kind(), fixed.from, fixed.to)).rows);
      } else {
        this.rows.set(list.rows);
      }
    } catch (error) {
      this.error.set(message(error, 'Celebrations could not be loaded.'));
    } finally {
      this.loading.set(false);
    }
  }
}
