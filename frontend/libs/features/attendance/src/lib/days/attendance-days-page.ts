import { SlicePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import { ATTENDANCE_DAY_STATUS_LABELS, type AttendanceDay } from '@gggs/shared/models';
import { AttendanceApi } from '../attendance-api';

/**
 * Team/org attendance - `attendance.record.view`'s screen, `/attendance` in the navigation
 * manifest. No employee picker yet (v1 has none shared across features - see this file's own
 * remarks), so a manager looks a specific employee up by id for now; a picker lands with the team
 * dashboard.
 */
@Component({
  selector: 'gggs-attendance-days-page',
  imports: [ReactiveFormsModule, PageHeader, Card, Button, FormField, SlicePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Attendance" subtitle="Look up an employee's computed attendance days." />

    <gggs-card class="section">
      <form [formGroup]="form" (ngSubmit)="load()" novalidate class="search-form">
        <gggs-form-field label="Employee ID" required>
          <input type="text" formControlName="employeeId" required />
        </gggs-form-field>

        <gggs-form-field label="From date" required>
          <input type="date" formControlName="fromDate" required />
        </gggs-form-field>

        <gggs-form-field label="To date" required>
          <input type="date" formControlName="toDate" required />
        </gggs-form-field>

        <gggs-button type="submit" [loading]="isLoading()">Look up</gggs-button>
      </form>
    </gggs-card>

    <gggs-card>
      @if (days().length === 0) {
        <p class="muted">
          @if (hasSearched()) {
            No attendance days found for that range.
          } @else {
            Enter an employee id and date range above.
          }
        </p>
      } @else {
        <table class="days-table">
          <thead>
            <tr>
              <th scope="col">Date</th>
              <th scope="col">Status</th>
              <th scope="col">First in</th>
              <th scope="col">Last out</th>
              <th scope="col">Worked</th>
              <th scope="col">Late</th>
              <th scope="col">Regularized</th>
            </tr>
          </thead>
          <tbody>
            @for (day of days(); track day.id) {
              <tr>
                <td>{{ day.attendanceDate }}</td>
                <td>{{ statusLabel(day) }}</td>
                <td>{{ day.firstIn ? (day.firstIn | slice: 11 : 16) : '—' }}</td>
                <td>{{ day.lastOut ? (day.lastOut | slice: 11 : 16) : '—' }}</td>
                <td>{{ day.workedMinutes }} min</td>
                <td>{{ day.lateMinutes }} min</td>
                <td>{{ day.isRegularized ? 'Yes' : 'No' }}</td>
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

    .search-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .days-table {
      width: 100%;
      border-collapse: collapse;
    }

    .days-table th,
    .days-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }
  `,
})
export class AttendanceDaysPage {
  private readonly api = inject(AttendanceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly days = signal<readonly AttendanceDay[]>([]);
  protected readonly isLoading = signal(false);
  protected readonly hasSearched = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    employeeId: ['', Validators.required],
    fromDate: [defaultFromDate(), Validators.required],
    toDate: [defaultToDate(), Validators.required],
  });

  protected statusLabel(day: AttendanceDay): string {
    return ATTENDANCE_DAY_STATUS_LABELS[day.status];
  }

  protected async load(): Promise<void> {
    if (this.form.invalid || this.isLoading()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isLoading.set(true);

    try {
      const { employeeId, fromDate, toDate } = this.form.getRawValue();
      this.days.set(await this.api.listDays(employeeId, fromDate, toDate));
      this.hasSearched.set(true);
    } finally {
      this.isLoading.set(false);
    }
  }
}

function defaultFromDate(): string {
  const date = new Date();
  date.setDate(date.getDate() - 13);
  return date.toISOString().slice(0, 10);
}

function defaultToDate(): string {
  return new Date().toISOString().slice(0, 10);
}
