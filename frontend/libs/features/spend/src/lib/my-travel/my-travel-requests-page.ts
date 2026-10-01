import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { TravelRequest } from '@gggs/shared/models';
import { SpendApi } from '../data/spend-api';

/** Self-service: create a travel request and submit it. Advance requests, disbursement and
 * settlement are always an admin/finance action - see the backend's own remarks on
 * spend.travel-advance.manage - so this page only covers the request itself. */
@Component({
  selector: 'gggs-my-travel-requests-page',
  imports: [DecimalPipe, ReactiveFormsModule, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My travel requests" subtitle="Request travel, then submit it for approval." />

    <gggs-card title="My requests" class="section">
      @if (requests(); as items) {
        @if (items.length > 0) {
          <table class="requests-table">
            <thead>
              <tr>
                <th scope="col">Request no</th>
                <th scope="col">Route</th>
                <th scope="col">Dates</th>
                <th scope="col">Estimated cost</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (request of items; track request.id) {
                <tr>
                  <td>{{ request.requestNo }}</td>
                  <td>{{ request.fromCity }} → {{ request.toCity }}</td>
                  <td>{{ request.fromDate }} – {{ request.toDate }}</td>
                  <td>{{ request.currency }} {{ request.estimatedCost | number: '1.2-2' }}</td>
                  <td>{{ request.status }}</td>
                  <td>
                    @if (request.status === 'Draft') {
                      <gggs-button size="sm" variant="ghost" (click)="submit(request.id)" [disabled]="isSubmitting()">Submit</gggs-button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No travel requests yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form [formGroup]="form" (ngSubmit)="create()" novalidate class="create-form">
        <label class="field">
          <span class="field__label">Purpose</span>
          <input type="text" formControlName="purpose" maxlength="200" required />
        </label>

        <label class="field">
          <span class="field__label">From city</span>
          <input type="text" formControlName="fromCity" maxlength="100" required />
        </label>

        <label class="field">
          <span class="field__label">To city</span>
          <input type="text" formControlName="toCity" maxlength="100" required />
        </label>

        <label class="field">
          <span class="field__label">Depart</span>
          <input type="date" formControlName="fromDate" required />
        </label>

        <label class="field">
          <span class="field__label">Return</span>
          <input type="date" formControlName="toDate" required />
        </label>

        <label class="field">
          <span class="field__label">Estimated cost</span>
          <input type="number" formControlName="estimatedCost" min="0.01" step="0.01" required />
        </label>

        <label class="field">
          <span class="field__label">Currency</span>
          <input type="text" formControlName="currency" maxlength="3" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isCreating()">
          {{ isCreating() ? 'Creating…' : 'New request' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .requests-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .requests-table th,
    .requests-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .create-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }
  `,
})
export class MyTravelRequestsPage {
  private readonly api = inject(SpendApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly requests = signal<readonly TravelRequest[] | null>(null);
  protected readonly isCreating = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    purpose: ['', [Validators.required, Validators.maxLength(200)]],
    fromCity: ['', [Validators.required, Validators.maxLength(100)]],
    toCity: ['', [Validators.required, Validators.maxLength(100)]],
    fromDate: ['', Validators.required],
    toDate: ['', Validators.required],
    estimatedCost: [0, [Validators.required, Validators.min(0.01)]],
    currency: ['INR', [Validators.required, Validators.maxLength(3)]],
  });

  constructor() {
    void this.load();
  }

  protected async create(): Promise<void> {
    if (this.form.invalid || this.isCreating()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isCreating.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      // employeeId is ignored by the backend unless the caller also holds spend.travel.view -
      // the signed-in employee is used instead, so this placeholder never actually matters.
      await this.api.createTravelRequest({
        employeeId: '00000000-0000-0000-0000-000000000000',
        requestNo: `TRV-${Date.now()}`,
        purpose: value.purpose ?? '',
        fromCity: value.fromCity ?? '',
        toCity: value.toCity ?? '',
        fromDate: value.fromDate ?? '',
        toDate: value.toDate ?? '',
        estimatedCost: value.estimatedCost ?? 0,
        currency: (value.currency ?? 'INR').toUpperCase(),
      });

      this.form.reset({ currency: 'INR', estimatedCost: 0 });
      await this.load();
    } catch {
      this.errorMessage.set('The request could not be created.');
    } finally {
      this.isCreating.set(false);
    }
  }

  protected async submit(id: string): Promise<void> {
    if (this.isSubmitting()) {
      return;
    }

    this.isSubmitting.set(true);

    try {
      await this.api.submitTravelRequest(id);
      await this.load();
    } finally {
      this.isSubmitting.set(false);
    }
  }

  private async load(): Promise<void> {
    this.requests.set(await this.api.listMyTravelRequests());
  }
}
