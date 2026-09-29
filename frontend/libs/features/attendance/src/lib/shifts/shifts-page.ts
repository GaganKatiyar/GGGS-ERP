import { SlicePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import type { Shift } from '@gggs/shared/models';
import { AttendanceApi } from '../attendance-api';

/** Shift catalog administration - `attendance.shift.manage`'s screen, `/attendance/shifts`. */
@Component({
  selector: 'gggs-shifts-page',
  imports: [ReactiveFormsModule, PageHeader, Card, Button, FormField, SlicePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Shifts" subtitle="Timing templates the roster assigns per employee per day." />

    <gggs-card title="New shift" class="section">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="form-grid">
        <gggs-form-field label="Code" required>
          <input type="text" formControlName="code" maxlength="30" required />
        </gggs-form-field>

        <gggs-form-field label="Name" required>
          <input type="text" formControlName="name" maxlength="100" required />
        </gggs-form-field>

        <gggs-form-field label="Start time" required>
          <input type="time" formControlName="startTime" required />
        </gggs-form-field>

        <gggs-form-field label="End time" required>
          <input type="time" formControlName="endTime" required />
        </gggs-form-field>

        <gggs-form-field label="Break (minutes)" required>
          <input type="number" formControlName="breakMinutes" min="0" required />
        </gggs-form-field>

        <gggs-form-field label="Grace in (minutes)" required>
          <input type="number" formControlName="graceInMinutes" min="0" required />
        </gggs-form-field>

        <gggs-form-field label="Grace out (minutes)" required>
          <input type="number" formControlName="graceOutMinutes" min="0" required />
        </gggs-form-field>

        <gggs-form-field label="Full day (minutes)" required>
          <input type="number" formControlName="fullDayMinMinutes" min="1" required />
        </gggs-form-field>

        <gggs-form-field label="Half day (minutes)" required>
          <input type="number" formControlName="halfDayMinMinutes" min="1" required />
        </gggs-form-field>

        <label class="checkbox-field">
          <input type="checkbox" formControlName="isNightShift" />
          Night shift (crosses midnight)
        </label>

        @if (errorMessage()) {
          <p class="field-error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" [loading]="isSaving()">Create</gggs-button>
      </form>
    </gggs-card>

    <gggs-card title="Existing shifts">
      @if (shifts().length === 0) {
        <p class="muted">No shifts yet.</p>
      } @else {
        <table class="shifts-table">
          <thead>
            <tr>
              <th scope="col">Code</th>
              <th scope="col">Name</th>
              <th scope="col">Timing</th>
              <th scope="col">Full / half day</th>
            </tr>
          </thead>
          <tbody>
            @for (shift of shifts(); track shift.id) {
              <tr>
                <td>{{ shift.code }}</td>
                <td>{{ shift.name }}</td>
                <td>
                  {{ shift.startTime | slice: 0 : 5 }}–{{ shift.endTime | slice: 0 : 5 }}
                  @if (shift.isNightShift) {
                    (night)
                  }
                </td>
                <td>{{ shift.fullDayMinMinutes }} / {{ shift.halfDayMinMinutes }} min</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </gggs-card>
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .form-grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      align-items: end;
    }

    .checkbox-field {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.875rem;
    }

    .field-error {
      grid-column: 1 / -1;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .shifts-table {
      width: 100%;
      border-collapse: collapse;
    }

    .shifts-table th,
    .shifts-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }
  `,
})
export class ShiftsPage {
  private readonly api = inject(AttendanceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly shifts = signal<readonly Shift[]>([]);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(100)]],
    startTime: ['09:00', Validators.required],
    endTime: ['18:00', Validators.required],
    breakMinutes: [60, [Validators.required, Validators.min(0)]],
    graceInMinutes: [10, [Validators.required, Validators.min(0)]],
    graceOutMinutes: [10, [Validators.required, Validators.min(0)]],
    fullDayMinMinutes: [480, [Validators.required, Validators.min(1)]],
    halfDayMinMinutes: [240, [Validators.required, Validators.min(1)]],
    isNightShift: [false],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.shifts.set(await this.api.listShifts());
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const raw = this.form.getRawValue();

    try {
      const shift = await this.api.createShift({
        ...raw,
        startTime: `${raw.startTime}:00`,
        endTime: `${raw.endTime}:00`,
      });

      this.shifts.update((shifts) => [...shifts, shift]);
      this.form.reset({
        startTime: '09:00',
        endTime: '18:00',
        breakMinutes: 60,
        graceInMinutes: 10,
        graceOutMinutes: 10,
        fullDayMinMinutes: 480,
        halfDayMinMinutes: 240,
        isNightShift: false,
      });
    } catch (error) {
      this.errorMessage.set(
        error instanceof ApiError && error.errorCode === 'SHIFT_CODE_IN_USE'
          ? 'That code is already in use.'
          : 'The shift could not be created.',
      );
    } finally {
      this.isSaving.set(false);
    }
  }
}
