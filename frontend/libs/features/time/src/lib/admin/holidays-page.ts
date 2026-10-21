import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { Holiday, HolidayCalendar, OrgUnit } from '@gggs/shared/models';
import { OrgStructureApi } from '@gggs/shared/org-reference';
import { LeaveApi } from '@gggs/shared/time';
import { EmptyState, FormDialog, PageHeader } from '@gggs/shared/ui';

/**
 * Holiday calendars (spec §21): one per group of locations, plus the default for everyone else.
 * Holidays are not working days, so leave across one does not take it.
 */
@Component({
  selector: 'gggs-holidays-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Holidays" subtitle="Public holidays, by location.">
      <div class="toolbar">
        <button type="button" class="btn" (click)="openCalendar(null)">Add calendar</button>
        <a class="btn" routerLink="../requests">Back to leave</a>
      </div>
    </gggs-page-header>

    <div class="holidays">
      <nav class="holidays__calendars" aria-label="Calendars">
        @for (calendar of calendars(); track calendar.id) {
          <button type="button" class="holidays__calendar" [class.holidays__calendar--active]="calendar.id === selected()?.id" (click)="select(calendar)">
            <strong>{{ calendar.name }}</strong>
            <span class="muted">{{ calendar.isDefault ? 'Default' : locationNames(calendar) }}</span>
          </button>
        }
      </nav>

      <section class="panel stack">
        @if (selected(); as calendar) {
          <div class="toolbar">
            <h2 class="holidays__heading">{{ calendar.name }}</h2>
            <label class="field holidays__year">
              <span class="field__label">Year</span>
              <input type="number" min="2000" max="2100" [value]="year()" (change)="setYear($event)" />
            </label>
            <button type="button" class="btn btn--sm" (click)="openCalendar(calendar)">Edit calendar</button>
          </div>

          <form class="toolbar holidays__add" (submit)="add($event)">
            <label class="field">
              <span class="field__label">Date</span>
              <input type="date" [value]="newDate()" (input)="newDate.set($any($event.target).value)" />
            </label>
            <label class="field holidays__name">
              <span class="field__label">Holiday</span>
              <input type="text" maxlength="100" [value]="newName()" (input)="newName.set($any($event.target).value)" placeholder="Independence Day" />
            </label>
            <label class="field">
              <span><input type="checkbox" [checked]="newOptional()" (change)="newOptional.set($any($event.target).checked)" /> Optional</span>
            </label>
            <button type="submit" class="btn btn--primary">Add</button>
          </form>

          @for (holiday of holidays(); track holiday.id) {
            <div class="holidays__row">
              <strong>{{ holiday.date | date: 'EEE d MMM' }}</strong>
              <span>{{ holiday.name }}</span>
              @if (holiday.isOptional) {
                <span class="muted">optional</span>
              }
              <button type="button" class="btn btn--link btn--sm holidays__remove" [attr.aria-label]="'Remove ' + holiday.name" (click)="remove(holiday)">Remove</button>
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-sun" [title]="'No holidays in ' + year()" />
          }
        } @else {
          <gggs-empty-state title="No calendar yet" />
        }
        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
      </section>
    </div>

    <gggs-form-dialog [(visible)]="calendarOpen" [title]="editing() ? 'Edit calendar' : 'Add calendar'" [busy]="busy()" [error]="dialogError()" (save)="saveCalendar()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" maxlength="32" [disabled]="!!editing()" [value]="calendarCode()" (input)="calendarCode.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="calendarName()" (input)="calendarName.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span><input type="checkbox" [checked]="calendarDefault()" (change)="calendarDefault.set($any($event.target).checked)" /> Default for locations without their own calendar</span>
        </label>
        <fieldset class="holidays__locations form-grid__full">
          <legend>Locations</legend>
          @for (location of locations(); track location.id) {
            <label><input type="checkbox" [checked]="calendarLocations().includes(location.id)" (change)="toggleLocation(location.id)" /> {{ location.name }}</label>
          } @empty {
            <span class="muted">No locations defined.</span>
          }
        </fieldset>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .holidays {
      display: grid;
      grid-template-columns: minmax(12rem, 16rem) 1fr;
      gap: var(--gggs-space-4);
      align-items: start;
    }

    @media (max-width: 48rem) {
      .holidays {
        grid-template-columns: 1fr;
      }
    }

    .holidays__calendars {
      display: flex;
      flex-direction: column;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
      overflow: hidden;
    }

    .holidays__calendar {
      display: flex;
      flex-direction: column;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 0;
      border-block-end: 1px solid var(--gggs-color-border);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .holidays__calendar--active {
      background: var(--gggs-color-primary-subtle);
    }

    .holidays__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .holidays__year {
      max-width: 7rem;
    }

    .holidays__add {
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .holidays__name {
      flex: 1;
      min-width: 12rem;
    }

    .holidays__row {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .holidays__remove {
      margin-inline-start: auto;
    }

    .holidays__locations {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      margin: 0;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }
  `,
})
export class HolidaysPage {
  private readonly api = inject(LeaveApi);
  private readonly org = inject(OrgStructureApi);

  protected readonly calendars = signal<readonly HolidayCalendar[]>([]);
  protected readonly selected = signal<HolidayCalendar | null>(null);
  protected readonly holidays = signal<readonly Holiday[]>([]);
  protected readonly locations = signal<readonly OrgUnit[]>([]);
  protected readonly year = signal(new Date().getFullYear());
  protected readonly error = signal<string | null>(null);

  protected readonly newDate = signal('');
  protected readonly newName = signal('');
  protected readonly newOptional = signal(false);

  protected readonly calendarOpen = signal(false);
  protected readonly editing = signal<HolidayCalendar | null>(null);
  protected readonly calendarCode = signal('');
  protected readonly calendarName = signal('');
  protected readonly calendarDefault = signal(false);
  protected readonly calendarLocations = signal<readonly string[]>([]);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  private readonly locationName = computed(() => new Map(this.locations().map((location) => [location.id, location.name])));

  constructor() {
    void this.org.active('locations').then((locations) => this.locations.set(locations));
    void this.loadCalendars();
  }

  protected locationNames(calendar: HolidayCalendar): string {
    return calendar.locationIds.map((id) => this.locationName().get(id) ?? '?').join(', ') || 'No locations';
  }

  protected select(calendar: HolidayCalendar): void {
    this.selected.set(calendar);
    void this.loadHolidays();
  }

  protected setYear(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    if (value >= 2000 && value <= 2100) {
      this.year.set(value);
      void this.loadHolidays();
    }
  }

  protected async add(event: Event): Promise<void> {
    event.preventDefault();
    const calendar = this.selected();
    if (!calendar || !this.newDate() || !this.newName().trim()) {
      this.error.set('Give the date and the name of the holiday.');
      return;
    }
    this.error.set(null);
    try {
      await this.api.addHoliday(calendar.id, { date: this.newDate(), name: this.newName().trim(), isOptional: this.newOptional() });
      this.newDate.set('');
      this.newName.set('');
      this.newOptional.set(false);
      await this.loadHolidays();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The holiday could not be added.');
    }
  }

  protected async remove(holiday: Holiday): Promise<void> {
    try {
      await this.api.removeHoliday(holiday.calendarId, holiday.id);
      await this.loadHolidays();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The holiday could not be removed.');
    }
  }

  protected openCalendar(calendar: HolidayCalendar | null): void {
    this.editing.set(calendar);
    this.calendarCode.set(calendar?.code ?? '');
    this.calendarName.set(calendar?.name ?? '');
    this.calendarDefault.set(calendar?.isDefault ?? false);
    this.calendarLocations.set(calendar?.locationIds ?? []);
    this.dialogError.set(null);
    this.calendarOpen.set(true);
  }

  protected toggleLocation(id: string): void {
    this.calendarLocations.update((ids) => (ids.includes(id) ? ids.filter((value) => value !== id) : [...ids, id]));
  }

  protected async saveCalendar(): Promise<void> {
    if (!this.calendarCode().trim() || !this.calendarName().trim()) {
      this.dialogError.set('Give the calendar a code and a name.');
      return;
    }
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      const saved = await this.api.saveCalendar(this.editing()?.id ?? null, {
        code: this.calendarCode().trim(),
        name: this.calendarName().trim(),
        isDefault: this.calendarDefault(),
        locationIds: this.calendarLocations(),
      });
      this.calendarOpen.set(false);
      await this.loadCalendars(saved.id);
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The calendar could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async loadCalendars(selectId?: string): Promise<void> {
    try {
      const calendars = await this.api.calendars();
      this.calendars.set(calendars);
      const chosen = calendars.find((calendar) => calendar.id === (selectId ?? this.selected()?.id)) ?? calendars[0] ?? null;
      this.selected.set(chosen);
      if (chosen) {
        await this.loadHolidays();
      }
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Calendars could not be loaded.');
    }
  }

  private async loadHolidays(): Promise<void> {
    const calendar = this.selected();
    if (!calendar) {
      return;
    }
    try {
      this.holidays.set(await this.api.holidays(calendar.id, this.year()));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Holidays could not be loaded.');
    }
  }
}
