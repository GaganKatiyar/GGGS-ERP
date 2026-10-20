import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { TimesheetsApi } from '@gggs/shared/delivery';
import type { TimesheetRules } from '@gggs/shared/models';
import { PageHeader, Skeleton } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

type NumberRule = 'standardHoursPerDay' | 'minHoursPerDay' | 'maxHoursPerDay' | 'minWeeklyHours' | 'submissionDeadlineDays';
type FlagRule = 'enforceWeeklyMinimum' | 'allowWeekendEntries' | 'allowHolidayEntries';

/**
 * Timesheet settings (spec §73): the hours rules every week is checked against, and the period
 * lock that closes weeks once payroll and billing have used them. The lock only moves forward.
 */
@Component({
  selector: 'gggs-timesheet-settings-page',
  imports: [DatePipe, PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Timesheet settings" subtitle="The rules each week is checked against, and the period lock." />
    @if (rules(); as r) {
      <section class="panel stack">
        <h2 class="ts-set__heading">Hours rules</h2>
        <div class="ts-set__grid">
          @for (field of numberFields; track field.key) {
            <label class="field">
              <span class="field__label">{{ field.label }}</span>
              <input type="number" min="0" [max]="field.max" step="0.5" [value]="r[field.key]" (input)="setNumber(field.key, $any($event.target).value)" />
            </label>
          }
        </div>
        <div class="stack">
          @for (flag of flagFields; track flag.key) {
            <label class="ts-set__flag">
              <input type="checkbox" [checked]="r[flag.key]" (change)="setFlag(flag.key, $any($event.target).checked)" />
              <span>{{ flag.label }}</span>
            </label>
          }
        </div>
        <div class="toolbar">
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="save()">Save rules</button>
        </div>
      </section>

      <section class="panel stack">
        <h2 class="ts-set__heading">Period lock</h2>
        <p class="muted">
          {{ r.lockedThrough ? 'Weeks up to ' + (r.lockedThrough | date: 'd MMM y') + ' are locked: no one can change or reopen them.' : 'No weeks are locked yet.' }}
        </p>
        <div class="toolbar ts-set__lock">
          <label class="field">
            <span class="field__label">Lock through</span>
            <input type="date" [value]="lockThrough()" (change)="lockThrough.set($any($event.target).value)" />
          </label>
          <button type="button" class="btn" [disabled]="busy() || !lockThrough()" (click)="lock()">Lock</button>
        </div>
      </section>
    } @else {
      <gggs-skeleton [lines]="6" />
    }
    @if (saved()) {
      <p class="ts-set__saved" role="status">{{ saved() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .ts-set__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .ts-set__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .ts-set__flag {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .ts-set__lock {
      align-items: end;
    }

    .ts-set__saved {
      color: var(--gggs-color-success);
    }
  `,
})
export class TimesheetSettingsPage {
  private readonly api = inject(TimesheetsApi);

  protected readonly numberFields: readonly { key: NumberRule; label: string; max: number }[] = [
    { key: 'standardHoursPerDay', label: 'Standard hours a day', max: 24 },
    { key: 'minHoursPerDay', label: 'Minimum hours a working day', max: 24 },
    { key: 'maxHoursPerDay', label: 'Maximum hours a day', max: 24 },
    { key: 'minWeeklyHours', label: 'Expected hours a week', max: 168 },
    { key: 'submissionDeadlineDays', label: 'Days after the week to submit', max: 30 },
  ];

  protected readonly flagFields: readonly { key: FlagRule; label: string }[] = [
    { key: 'enforceWeeklyMinimum', label: 'Block submitting a week below the expected hours' },
    { key: 'allowWeekendEntries', label: 'Allow hours on weekends' },
    { key: 'allowHolidayEntries', label: 'Allow hours on holidays' },
  ];

  protected readonly rules = signal<TimesheetRules | null>(null);
  protected readonly lockThrough = signal('');
  protected readonly busy = signal(false);
  protected readonly saved = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.rules().then(
      (rules) => this.rules.set(rules),
      (error: unknown) => this.error.set(message(error, 'The settings could not be loaded.')),
    );
  }

  protected setNumber(key: NumberRule, value: string): void {
    this.rules.update((rules) => (rules ? { ...rules, [key]: Number(value) } : rules));
  }

  protected setFlag(key: FlagRule, value: boolean): void {
    this.rules.update((rules) => (rules ? { ...rules, [key]: value } : rules));
  }

  protected async save(): Promise<void> {
    const rules = this.rules();
    if (rules) {
      await this.run(async () => this.rules.set(await this.api.saveRules(rules)), 'Rules saved.');
    }
  }

  protected async lock(): Promise<void> {
    await this.run(async () => this.rules.set(await this.api.lock(this.lockThrough())), 'Period locked.');
  }

  private async run(action: () => Promise<void>, done: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(null);
    try {
      await action();
      this.saved.set(done);
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}
