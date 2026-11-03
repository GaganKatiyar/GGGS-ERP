import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { TimesheetsApi } from '@gggs/shared/delivery';
import type { TimesheetDay, TimesheetTarget } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';
import { buildRows, copyableRows, mondayOf, rowTotal, type GridRow } from './timesheet-grid';

/** What was chosen: the lines to lay over this week, and whether their notes come too. */
export interface CopiedLines {
  readonly rows: readonly GridRow[];
  readonly withNotes: boolean;
}

/**
 * Copy a timesheet: from the last one submitted or any other week, hours alone or with their notes.
 * Only lines on work this week still offers can be copied; nothing is saved until the week is.
 */
@Component({
  selector: 'gggs-copy-timesheet-dialog',
  imports: [DatePipe, DecimalPipe, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" title="Copy a timesheet" size="lg" saveLabel="Copy" [busy]="busy()" [error]="error()" [saveDisabled]="chosen().size === 0" (save)="copy()">
      <div class="stack">
        <fieldset class="copy__choice">
          <legend class="field__label">Copy from</legend>
          <label><input type="radio" name="copy-from" [checked]="from() === 'recent'" (change)="setFrom('recent')" /> Most recently submitted timesheet</label>
          <label><input type="radio" name="copy-from" [checked]="from() === 'period'" (change)="setFrom('period')" /> Choose a period</label>
          @if (from() === 'period') {
            <input type="date" aria-label="Any day in the week to copy" [value]="periodDay()" (change)="choosePeriod($any($event.target).value)" />
          }
        </fieldset>
        <fieldset class="copy__choice">
          <legend class="field__label">Copy over</legend>
          <label><input type="radio" name="copy-over" [checked]="!withNotes()" (change)="withNotes.set(false)" /> Only time entries</label>
          <label><input type="radio" name="copy-over" [checked]="withNotes()" (change)="withNotes.set(true)" /> Time entries and notes</label>
        </fieldset>

        @if (sourceWeek(); as week) {
          <p class="muted copy__note">
            From the week of {{ week | date: 'd MMM y' }}.
            @if (skipped()) {
              {{ skipped() }} line(s) left out: that work is not on your timesheet this week.
            }
          </p>
        }

        @if (lines().length) {
          <div class="copy__scroll" tabindex="0" role="region" aria-label="Lines to copy">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col"><input type="checkbox" aria-label="Choose all lines" [checked]="chosen().size === lines().length" (change)="chooseAll($any($event.target).checked)" /></th>
                  <th scope="col">Project / task</th>
                  @for (day of days(); track day.date) {
                    <th scope="col" class="copy__num">{{ day.date | date: 'EEE' }}</th>
                  }
                  <th scope="col" class="copy__num">Total</th>
                </tr>
              </thead>
              <tbody>
                @for (line of lines(); track line.id) {
                  <tr>
                    <td><input type="checkbox" [attr.aria-label]="'Copy ' + line.title" [checked]="chosen().has(line.id)" (change)="toggle(line.id, $any($event.target).checked)" /></td>
                    <td>
                      {{ line.title }}
                      <div class="muted copy__note">{{ line.projectLabel }}</div>
                    </td>
                    @for (cell of line.cells; track $index) {
                      <td class="copy__num">{{ cell.hours ?? 0 | number: '1.0-2' }}</td>
                    }
                    <td class="copy__num">{{ total(line) | number: '1.0-2' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else if (!busy()) {
          <p class="muted">{{ emptyMessage() }}</p>
        }
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .copy__choice {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      align-items: center;
      margin: 0;
      padding: 0;
      border: 0;
    }

    .copy__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .copy__scroll {
      overflow-x: auto;
    }

    .copy__num {
      text-align: end;
    }
  `,
})
export class CopyTimesheetDialog {
  private readonly api = inject(TimesheetsApi);

  readonly visible = model(false);
  readonly targets = input.required<readonly TimesheetTarget[]>();
  /** The week being filled in, which cannot be copied onto itself. */
  readonly week = input.required<string>();
  readonly days = input.required<readonly TimesheetDay[]>();
  readonly copied = output<CopiedLines>();

  protected readonly from = signal<'recent' | 'period'>('recent');
  protected readonly withNotes = signal(false);
  protected readonly periodDay = signal('');
  protected readonly sourceWeek = signal<string | null>(null);
  protected readonly lines = signal<readonly GridRow[]>([]);
  protected readonly skipped = signal(0);
  protected readonly chosen = signal<ReadonlySet<number>>(new Set());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly emptyMessage = computed(() =>
    this.from() === 'period' && !this.periodDay()
      ? 'Choose a day in the week to copy.'
      : this.sourceWeek()
        ? 'Nothing in that week can be copied here.'
        : 'You have no submitted timesheet to copy yet.',
  );

  protected readonly total = rowTotal;

  constructor() {
    effect(() => {
      if (this.visible()) {
        this.from.set('recent');
        void this.loadRecent();
      }
    });
  }

  protected setFrom(from: 'recent' | 'period'): void {
    this.from.set(from);
    this.show(null, []);
    if (from === 'recent') {
      void this.loadRecent();
    } else if (this.periodDay()) {
      void this.load(mondayOf(this.periodDay()));
    }
  }

  protected choosePeriod(day: string): void {
    this.periodDay.set(day);
    if (day) {
      void this.load(mondayOf(day));
    }
  }

  protected toggle(id: number, on: boolean): void {
    this.chosen.update((chosen) => {
      const next = new Set(chosen);
      if (on) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  protected chooseAll(on: boolean): void {
    this.chosen.set(new Set(on ? this.lines().map((line) => line.id) : []));
  }

  protected copy(): void {
    this.copied.emit({ rows: this.lines().filter((line) => this.chosen().has(line.id)), withNotes: this.withNotes() });
    this.visible.set(false);
  }

  private async loadRecent(): Promise<void> {
    await this.run(async () => {
      const weeks = await this.api.bank({});
      const recent = weeks.find((week) => (week.status === 'Submitted' || week.status === 'Approved') && week.weekStart !== this.week());
      if (recent) {
        await this.fetch(recent.weekStart);
      } else {
        this.show(null, []);
      }
    });
  }

  private async load(week: string): Promise<void> {
    await this.run(() => this.fetch(week));
  }

  private async fetch(week: string): Promise<void> {
    let next = 0;
    const view = await this.api.week(week);
    const rows = buildRows(view, () => next++);
    this.show(week, rows);
  }

  private show(week: string | null, rows: readonly GridRow[]): void {
    const withHours = rows.filter((row) => rowTotal(row) > 0);
    const lines = copyableRows(withHours, this.targets());
    this.sourceWeek.set(week);
    this.lines.set(lines);
    this.skipped.set(withHours.length - lines.length);
    this.chosen.set(new Set(lines.map((line) => line.id)));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That timesheet could not be loaded.');
    } finally {
      this.busy.set(false);
    }
  }
}
