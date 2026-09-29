import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import type { LeaveTypeListItem, LeaveUnit } from '@gggs/shared/models';
import { LeaveApi } from '../leave-api';

/** Leave type catalog - `leave.type.manage`'s screen. The policy page's rules key off these. */
@Component({
  selector: 'gggs-leave-types-page',
  imports: [ReactiveFormsModule, PageHeader, Card, Button, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Leave types" subtitle="The catalog every leave policy rule draws from." />

    <gggs-card title="New leave type" class="section">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="form-grid">
        <gggs-form-field label="Code" required>
          <input type="text" formControlName="code" maxlength="30" required />
        </gggs-form-field>

        <gggs-form-field label="Name" required>
          <input type="text" formControlName="name" maxlength="100" required />
        </gggs-form-field>

        <gggs-form-field label="Unit" required>
          <select formControlName="unit">
            <option value="Day">Day</option>
            <option value="HalfDay">Half day</option>
            <option value="Hour">Hour</option>
          </select>
        </gggs-form-field>

        <gggs-form-field label="Color" required>
          <input type="color" formControlName="colorHex" />
        </gggs-form-field>

        <label class="checkbox-field">
          <input type="checkbox" formControlName="isPaid" />
          Paid
        </label>

        <label class="checkbox-field">
          <input type="checkbox" formControlName="isCompOff" />
          Comp-off
        </label>

        <label class="checkbox-field">
          <input type="checkbox" formControlName="isLop" />
          Loss of pay
        </label>

        @if (errorMessage()) {
          <p class="field-error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" [loading]="isSaving()">Create</gggs-button>
      </form>
    </gggs-card>

    <gggs-card title="Existing types">
      @if (leaveTypes().length === 0) {
        <p class="muted">No leave types yet.</p>
      } @else {
        <table class="types-table">
          <thead>
            <tr>
              <th scope="col">Code</th>
              <th scope="col">Name</th>
              <th scope="col">Unit</th>
              <th scope="col">Paid</th>
            </tr>
          </thead>
          <tbody>
            @for (type of leaveTypes(); track type.id) {
              <tr>
                <td>{{ type.code }}</td>
                <td>
                  <span class="color-dot" [style.background]="type.colorHex"></span>
                  {{ type.name }}
                </td>
                <td>{{ type.unit }}</td>
                <td>{{ type.isPaid ? 'Yes' : 'No' }}</td>
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

    .types-table {
      width: 100%;
      border-collapse: collapse;
    }

    .types-table th,
    .types-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .color-dot {
      display: inline-block;
      width: 0.625rem;
      height: 0.625rem;
      border-radius: 50%;
      margin-inline-end: 0.375rem;
    }
  `,
})
export class LeaveTypesPage {
  private readonly api = inject(LeaveApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly leaveTypes = signal<readonly LeaveTypeListItem[]>([]);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(100)]],
    unit: ['Day' as LeaveUnit],
    colorHex: ['#6b7280', Validators.required],
    isPaid: [true],
    isCompOff: [false],
    isLop: [false],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.leaveTypes.set(await this.api.listLeaveTypes());
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    try {
      const type = await this.api.createLeaveType(this.form.getRawValue());
      this.leaveTypes.update((types) => [...types, type]);
      this.form.reset({
        unit: 'Day',
        colorHex: '#6b7280',
        isPaid: true,
        isCompOff: false,
        isLop: false,
      });
    } catch (error) {
      this.errorMessage.set(
        error instanceof ApiError && error.errorCode === 'LEAVE_TYPE_CODE_IN_USE'
          ? 'That code is already in use.'
          : 'The leave type could not be created.',
      );
    } finally {
      this.isSaving.set(false);
    }
  }
}
