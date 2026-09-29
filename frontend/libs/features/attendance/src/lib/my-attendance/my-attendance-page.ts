import { SlicePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import { ATTENDANCE_DAY_STATUS_LABELS, type AttendanceDay } from '@gggs/shared/models';
import { AttendanceApi } from '../attendance-api';

/**
 * Self-service attendance: check-in/check-out and the employee's own recent days -
 * architecture.md §12.1's Employee dashboard "check-in/out card". `/me/attendance` in the
 * navigation manifest.
 */
@Component({
  selector: 'gggs-my-attendance-page',
  imports: [Button, Card, PageHeader, SlicePipe, ReactiveFormsModule, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My attendance" subtitle="Check in, check out, and your recent days." />

    <gggs-card class="section">
      <div class="check-actions">
        <gggs-button [loading]="isPunching()" (click)="checkIn()">Check in</gggs-button>
        <gggs-button variant="secondary" [loading]="isPunching()" (click)="checkOut()">
          Check out
        </gggs-button>
      </div>

      @if (punchMessage()) {
        <p class="punch-message">{{ punchMessage() }}</p>
      }
      @if (errorMessage()) {
        <p class="field-error" role="alert">{{ errorMessage() }}</p>
      }
    </gggs-card>

    <gggs-card title="Recent days" class="section">
      @if (days().length === 0) {
        <p class="muted">No attendance recorded in the last 14 days yet.</p>
      } @else {
        <table class="days-table">
          <thead>
            <tr>
              <th scope="col">Date</th>
              <th scope="col">Status</th>
              <th scope="col">First in</th>
              <th scope="col">Last out</th>
              <th scope="col">Worked</th>
              <th scope="col"></th>
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
                <td>
                  @if (!day.isRegularized) {
                    <gggs-button variant="ghost" size="sm" (click)="startCorrection(day)">
                      Request correction
                    </gggs-button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </gggs-card>

    @if (correctionDate(); as date) {
      <gggs-card title="Request a correction for {{ date }}">
        <form [formGroup]="correctionForm" (ngSubmit)="submitCorrection()" novalidate class="correction-form">
          <gggs-form-field label="Actual in time" required>
            <input type="time" formControlName="requestedIn" required />
          </gggs-form-field>

          <gggs-form-field label="Actual out time" required>
            <input type="time" formControlName="requestedOut" required />
          </gggs-form-field>

          <gggs-form-field label="Reason" required>
            <select formControlName="reasonCode" required>
              <option value="FORGOT_PUNCH">Forgot to punch</option>
              <option value="DEVICE_FAILURE">Device failure</option>
              <option value="OUTSIDE_WORK">Working outside office</option>
              <option value="OTHER">Other</option>
            </select>
          </gggs-form-field>

          <gggs-form-field label="Remarks" class="correction-form__remarks">
            <textarea formControlName="remarks" maxlength="1000" rows="2"></textarea>
          </gggs-form-field>

          @if (correctionErrorMessage()) {
            <p class="field-error" role="alert">{{ correctionErrorMessage() }}</p>
          }

          <div class="correction-form__actions">
            <gggs-button type="submit" [loading]="isSubmittingCorrection()">Submit</gggs-button>
            <gggs-button type="button" variant="ghost" (click)="cancelCorrection()">Cancel</gggs-button>
          </div>
        </form>
      </gggs-card>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .check-actions {
      display: flex;
      gap: 0.75rem;
    }

    .punch-message {
      margin: 0.75rem 0 0;
      color: var(--gggs-color-success, #1e7d32);
      font-size: 0.875rem;
    }

    .field-error {
      margin: 0.75rem 0 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
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

    .correction-form {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      align-items: end;
    }

    .correction-form__remarks {
      grid-column: 1 / -1;
    }

    .correction-form__actions {
      display: flex;
      gap: 0.5rem;
    }
  `,
})
export class MyAttendancePage {
  private readonly api = inject(AttendanceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly days = signal<readonly AttendanceDay[]>([]);
  protected readonly isPunching = signal(false);
  protected readonly punchMessage = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly correctionDate = signal<string | null>(null);
  protected readonly isSubmittingCorrection = signal(false);
  protected readonly correctionErrorMessage = signal<string | null>(null);

  protected readonly correctionForm = this.formBuilder.nonNullable.group({
    requestedIn: ['09:00', Validators.required],
    requestedOut: ['18:00', Validators.required],
    reasonCode: ['FORGOT_PUNCH', Validators.required],
    remarks: [''],
  });

  constructor() {
    void this.loadDays();
  }

  protected statusLabel(day: AttendanceDay): string {
    return ATTENDANCE_DAY_STATUS_LABELS[day.status];
  }

  private async loadDays(): Promise<void> {
    const today = new Date();
    const twoWeeksAgo = new Date(today);
    twoWeeksAgo.setDate(today.getDate() - 13);

    this.days.set(
      await this.api.listMyDays(toDateOnly(twoWeeksAgo), toDateOnly(today)),
    );
  }

  protected async checkIn(): Promise<void> {
    await this.punch(() => this.api.checkIn(), 'Checked in.');
  }

  protected async checkOut(): Promise<void> {
    await this.punch(() => this.api.checkOut(), 'Checked out.');
  }

  private async punch(action: () => Promise<unknown>, successMessage: string): Promise<void> {
    this.isPunching.set(true);
    this.punchMessage.set(null);
    this.errorMessage.set(null);

    try {
      await action();
      this.punchMessage.set(successMessage);
      await this.loadDays();
    } catch (error) {
      this.errorMessage.set(
        error instanceof ApiError ? error.message : 'The punch could not be recorded.',
      );
    } finally {
      this.isPunching.set(false);
    }
  }

  protected startCorrection(day: AttendanceDay): void {
    this.correctionDate.set(day.attendanceDate);
    this.correctionErrorMessage.set(null);
    this.correctionForm.reset({
      requestedIn: '09:00',
      requestedOut: '18:00',
      reasonCode: 'FORGOT_PUNCH',
      remarks: '',
    });
  }

  protected cancelCorrection(): void {
    this.correctionDate.set(null);
  }

  protected async submitCorrection(): Promise<void> {
    const date = this.correctionDate();
    if (!date || this.correctionForm.invalid || this.isSubmittingCorrection()) {
      this.correctionForm.markAllAsTouched();
      return;
    }

    this.isSubmittingCorrection.set(true);
    this.correctionErrorMessage.set(null);

    const { requestedIn, requestedOut, reasonCode, remarks } = this.correctionForm.getRawValue();

    try {
      await this.api.submitRegularization({
        attendanceDate: date,
        requestedIn: `${date}T${requestedIn}:00Z`,
        requestedOut: `${date}T${requestedOut}:00Z`,
        reasonCode,
        remarks: remarks || undefined,
      });

      this.correctionDate.set(null);
      this.punchMessage.set('Correction submitted for approval.');
    } catch (error) {
      this.correctionErrorMessage.set(
        error instanceof ApiError ? error.message : 'The correction could not be submitted.',
      );
    } finally {
      this.isSubmittingCorrection.set(false);
    }
  }
}

function toDateOnly(date: Date): string {
  return date.toISOString().slice(0, 10);
}
