import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import type { HolidayCalendarListItem, HolidayListItem } from '@gggs/shared/models';
import { LeaveApi } from '../leave-api';

/**
 * Holiday calendars - `leave.holiday.manage`'s screen. v1 has one calendar per year, tenant-wide
 * (`IHolidayRepository.GetForYearAsync`'s own remarks: no `HolidayCalendarAssignment` targeting
 * yet), so this is a year picker plus that year's dates, not a list of many calendars to manage.
 */
@Component({
  selector: 'gggs-holidays-page',
  imports: [ReactiveFormsModule, PageHeader, Card, Button, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Holidays" subtitle="One calendar per year, shared by every employee." />

    <gggs-card title="Calendars" class="section">
      @if (calendars().length === 0) {
        <p class="muted">No holiday calendars yet.</p>
      } @else {
        <div class="calendar-tabs">
          @for (calendar of calendars(); track calendar.id) {
            <button
              type="button"
              class="calendar-tab"
              [class.calendar-tab--active]="selectedCalendar()?.id === calendar.id"
              (click)="selectCalendar(calendar)"
            >
              {{ calendar.year }}
            </button>
          }
        </div>
      }

      <form [formGroup]="calendarForm" (ngSubmit)="createCalendar()" novalidate class="inline-form">
        <gggs-form-field label="Year" required>
          <input type="number" formControlName="year" min="2000" max="2100" required />
        </gggs-form-field>
        <gggs-form-field label="Country code" required>
          <input type="text" formControlName="countryCode" maxlength="2" placeholder="IN" required />
        </gggs-form-field>
        <gggs-form-field label="State code">
          <input type="text" formControlName="stateCode" maxlength="10" />
        </gggs-form-field>
        <gggs-button type="submit" [loading]="isSavingCalendar()">Add calendar</gggs-button>
      </form>
    </gggs-card>

    @if (selectedCalendar(); as calendar) {
      <gggs-card [title]="calendar.year + ' holidays'">
        @if (holidays().length === 0) {
          <p class="muted">No holidays added for {{ calendar.year }} yet.</p>
        } @else {
          <table class="holidays-table">
            <thead>
              <tr>
                <th scope="col">Date</th>
                <th scope="col">Name</th>
                <th scope="col">Optional</th>
              </tr>
            </thead>
            <tbody>
              @for (holiday of holidays(); track holiday.id) {
                <tr>
                  <td>{{ holiday.holidayDate }}</td>
                  <td>{{ holiday.name }}</td>
                  <td>{{ holiday.isOptional ? 'Yes' : 'No' }}</td>
                </tr>
              }
            </tbody>
          </table>
        }

        <form [formGroup]="holidayForm" (ngSubmit)="addHoliday()" novalidate class="inline-form">
          <gggs-form-field label="Date" required>
            <input type="date" formControlName="holidayDate" required />
          </gggs-form-field>
          <gggs-form-field label="Name" required>
            <input type="text" formControlName="name" maxlength="150" required />
          </gggs-form-field>
          <label class="checkbox-field">
            <input type="checkbox" formControlName="isOptional" />
            Optional
          </label>
          @if (holidayErrorMessage()) {
            <p class="field-error" role="alert">{{ holidayErrorMessage() }}</p>
          }
          <gggs-button type="submit" [loading]="isSavingHoliday()">Add holiday</gggs-button>
        </form>
      </gggs-card>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 0.75rem;
    }

    .calendar-tabs {
      display: flex;
      gap: 0.5rem;
      margin-block-end: 1rem;
    }

    .calendar-tab {
      padding: 0.375rem 0.875rem;
      border-radius: var(--gggs-radius-sm, 0.375rem);
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #fff);
      cursor: pointer;
      font-weight: 600;
    }

    .calendar-tab--active {
      background: var(--gggs-color-primary, #1d4ed8);
      color: #fff;
      border-color: transparent;
    }

    .inline-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
    }

    .checkbox-field {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.875rem;
    }

    .field-error {
      flex-basis: 100%;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .holidays-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .holidays-table th,
    .holidays-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }
  `,
})
export class HolidaysPage {
  private readonly api = inject(LeaveApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly calendars = signal<readonly HolidayCalendarListItem[]>([]);
  protected readonly selectedCalendar = signal<HolidayCalendarListItem | null>(null);
  protected readonly holidays = signal<readonly HolidayListItem[]>([]);
  protected readonly isSavingCalendar = signal(false);
  protected readonly isSavingHoliday = signal(false);
  protected readonly holidayErrorMessage = signal<string | null>(null);

  protected readonly calendarForm = this.formBuilder.nonNullable.group({
    year: [new Date().getFullYear(), [Validators.required, Validators.min(2000)]],
    countryCode: ['IN', [Validators.required, Validators.maxLength(2)]],
    stateCode: [''],
  });

  protected readonly holidayForm = this.formBuilder.nonNullable.group({
    holidayDate: ['', Validators.required],
    name: ['', [Validators.required, Validators.maxLength(150)]],
    isOptional: [false],
  });

  constructor() {
    void this.loadCalendars();
  }

  private async loadCalendars(): Promise<void> {
    const calendars = await this.api.listHolidayCalendars();
    this.calendars.set(calendars);

    const mostRecent = [...calendars].sort((a, b) => b.year - a.year)[0];
    if (mostRecent) {
      await this.selectCalendar(mostRecent);
    }
  }

  protected async selectCalendar(calendar: HolidayCalendarListItem): Promise<void> {
    this.selectedCalendar.set(calendar);
    this.holidays.set(await this.api.listHolidays(calendar.id));
  }

  protected async createCalendar(): Promise<void> {
    if (this.calendarForm.invalid || this.isSavingCalendar()) {
      this.calendarForm.markAllAsTouched();
      return;
    }

    this.isSavingCalendar.set(true);

    try {
      const { year, countryCode, stateCode } = this.calendarForm.getRawValue();
      const calendar = await this.api.createHolidayCalendar({
        name: `${countryCode} Holidays ${year}`,
        year,
        countryCode,
        stateCode: stateCode || undefined,
      });

      this.calendars.update((calendars) => [...calendars, calendar]);
      await this.selectCalendar(calendar);
      this.calendarForm.reset({ year: new Date().getFullYear(), countryCode: 'IN', stateCode: '' });
    } finally {
      this.isSavingCalendar.set(false);
    }
  }

  protected async addHoliday(): Promise<void> {
    const calendar = this.selectedCalendar();
    if (!calendar || this.holidayForm.invalid || this.isSavingHoliday()) {
      this.holidayForm.markAllAsTouched();
      return;
    }

    this.isSavingHoliday.set(true);
    this.holidayErrorMessage.set(null);

    try {
      const { holidayDate, name, isOptional } = this.holidayForm.getRawValue();
      const holiday = await this.api.addHoliday(calendar.id, {
        holidayDate,
        name,
        isOptional,
        isRestricted: false,
      });

      this.holidays.update((holidays) => [...holidays, holiday]);
      this.holidayForm.reset({ holidayDate: '', name: '', isOptional: false });
    } catch (error) {
      this.holidayErrorMessage.set(
        error instanceof ApiError ? error.message : 'The holiday could not be added.',
      );
    } finally {
      this.isSavingHoliday.set(false);
    }
  }
}
