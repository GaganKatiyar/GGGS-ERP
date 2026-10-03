import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { ExpenseCategory, ExpenseClaim, ExpenseLine } from '@gggs/shared/models';
import { SpendApi } from '../data/spend-api';

/** Self-service: create a claim, add lines to it while it is Draft, submit it, and see the
 * decision once Workflow's approval runs. No receipts or multi-currency in this v1 slice - see
 * the backend's own remarks. */
@Component({
  selector: 'gggs-my-claims-page',
  imports: [DecimalPipe, ReactiveFormsModule, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My expense claims" subtitle="Create a claim, add lines, and submit it for approval." />

    <gggs-card title="My claims" class="section">
      @if (claims(); as items) {
        @if (items.length > 0) {
          <table class="claims-table">
            <thead>
              <tr>
                <th scope="col">Claim no</th>
                <th scope="col">Title</th>
                <th scope="col">Total</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (claim of items; track claim.id) {
                <tr>
                  <td>{{ claim.claimNo }}</td>
                  <td>{{ claim.title }}</td>
                  <td>{{ claim.currency }} {{ claim.total | number: '1.2-2' }}</td>
                  <td>{{ claim.status }}</td>
                  <td>
                    <gggs-button size="sm" variant="ghost" (click)="view(claim.id)">View</gggs-button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No claims yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form [formGroup]="createForm" (ngSubmit)="createClaim()" novalidate class="create-form">
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" formControlName="title" maxlength="200" required />
        </label>

        <label class="field">
          <span class="field__label">Currency</span>
          <input type="text" formControlName="currency" maxlength="3" required />
        </label>

        @if (createErrorMessage()) {
          <p class="field__error" role="alert">{{ createErrorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isCreating()">
          {{ isCreating() ? 'Creating…' : 'New claim' }}
        </gggs-button>
      </form>
    </gggs-card>

    @if (selected(); as claim) {
      <gggs-card title="{{ claim.claimNo }} - {{ claim.title }}">
        <dl class="claim-grid">
          <dt>Status</dt>
          <dd>{{ claim.status }}</dd>
          <dt>Total</dt>
          <dd>{{ claim.currency }} {{ claim.total | number: '1.2-2' }}</dd>
          @if (claim.submittedDate) {
            <dt>Submitted</dt>
            <dd>{{ claim.submittedDate }}</dd>
          }
          @if (claim.decidedDate) {
            <dt>Decided</dt>
            <dd>{{ claim.decidedDate }}</dd>
          }
        </dl>

        <table class="lines-table">
          <thead>
            <tr>
              <th scope="col">Date</th>
              <th scope="col">Merchant</th>
              <th scope="col">Amount</th>
            </tr>
          </thead>
          <tbody>
            @for (line of lines(); track line.id) {
              <tr>
                <td>{{ line.expenseDate }}</td>
                <td>{{ line.merchant ?? '—' }}</td>
                <td>{{ line.amount | number: '1.2-2' }}</td>
              </tr>
            }
          </tbody>
        </table>

        @if (claim.status === 'Draft') {
          <form [formGroup]="lineForm" (ngSubmit)="addLine(claim.id)" novalidate class="add-line-form">
            <label class="field">
              <span class="field__label">Category</span>
              <select formControlName="categoryId" required>
                <option value="" disabled>Select a category</option>
                @for (category of categories(); track category.id) {
                  <option [value]="category.id">{{ category.name }}</option>
                }
              </select>
            </label>

            <label class="field">
              <span class="field__label">Date</span>
              <input type="date" formControlName="expenseDate" required />
            </label>

            <label class="field">
              <span class="field__label">Amount</span>
              <input type="number" formControlName="amount" min="0.01" step="0.01" required />
            </label>

            <label class="field">
              <span class="field__label">Merchant</span>
              <input type="text" formControlName="merchant" maxlength="200" />
            </label>

            @if (lineErrorMessage()) {
              <p class="field__error" role="alert">{{ lineErrorMessage() }}</p>
            }

            <gggs-button type="submit" size="sm" [disabled]="isAddingLine()">
              {{ isAddingLine() ? 'Adding…' : 'Add line' }}
            </gggs-button>
          </form>

          <gggs-button
            size="sm"
            variant="secondary"
            (click)="submitClaim(claim.id)"
            [disabled]="isSubmitting() || lines().length === 0"
          >
            {{ isSubmitting() ? 'Submitting…' : 'Submit claim' }}
          </gggs-button>
        }
      </gggs-card>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .claims-table,
    .lines-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .claims-table th,
    .claims-table td,
    .lines-table th,
    .lines-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .claim-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.25rem 0.75rem;
      margin: 0 0 1rem;
      font-size: 0.875rem;
    }

    .claim-grid dt {
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .claim-grid dd {
      margin: 0;
    }

    .create-form,
    .add-line-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
      margin-block-end: 1rem;
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
export class MyClaimsPage {
  private readonly api = inject(SpendApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly claims = signal<readonly ExpenseClaim[] | null>(null);
  protected readonly categories = signal<readonly ExpenseCategory[]>([]);
  protected readonly selected = signal<ExpenseClaim | null>(null);
  protected readonly lines = signal<readonly ExpenseLine[]>([]);

  protected readonly isCreating = signal(false);
  protected readonly isAddingLine = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly createErrorMessage = signal<string | null>(null);
  protected readonly lineErrorMessage = signal<string | null>(null);

  protected readonly createForm = this.formBuilder.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    currency: ['INR', [Validators.required, Validators.maxLength(3)]],
  });

  protected readonly lineForm = this.formBuilder.group({
    categoryId: ['', Validators.required],
    expenseDate: ['', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    merchant: [''],
  });

  constructor() {
    void this.load();
    void this.loadCategories();
  }

  protected async view(id: string): Promise<void> {
    this.selected.set(await this.api.getClaim(id));
    this.lines.set(await this.api.listLines(id));
    this.lineForm.reset({ amount: 0 });
  }

  protected async createClaim(): Promise<void> {
    if (this.createForm.invalid || this.isCreating()) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.isCreating.set(true);
    this.createErrorMessage.set(null);

    const value = this.createForm.getRawValue();

    try {
      // employeeId is ignored by the backend unless the caller also holds spend.claim.view -
      // the signed-in employee is used instead, so this placeholder never actually matters.
      await this.api.createClaim({
        employeeId: '00000000-0000-0000-0000-000000000000',
        claimNo: `EXP-${Date.now()}`,
        title: value.title ?? '',
        currency: (value.currency ?? 'INR').toUpperCase(),
      });

      this.createForm.reset({ currency: 'INR' });
      await this.load();
    } catch {
      this.createErrorMessage.set('The claim could not be created.');
    } finally {
      this.isCreating.set(false);
    }
  }

  protected async addLine(claimId: string): Promise<void> {
    if (this.lineForm.invalid || this.isAddingLine()) {
      this.lineForm.markAllAsTouched();
      return;
    }

    this.isAddingLine.set(true);
    this.lineErrorMessage.set(null);

    const value = this.lineForm.getRawValue();

    try {
      const line = await this.api.addLine(claimId, {
        categoryId: value.categoryId ?? '',
        expenseDate: value.expenseDate ?? '',
        amount: value.amount ?? 0,
        merchant: value.merchant || undefined,
      });

      this.lines.update((existing) => [...existing, line]);
      this.lineForm.reset({ amount: 0 });
    } catch {
      this.lineErrorMessage.set('That line could not be added.');
    } finally {
      this.isAddingLine.set(false);
    }
  }

  protected async submitClaim(claimId: string): Promise<void> {
    if (this.isSubmitting()) {
      return;
    }

    this.isSubmitting.set(true);

    try {
      const claim = await this.api.submitClaim(claimId);
      this.selected.set(claim);
      await this.load();
    } finally {
      this.isSubmitting.set(false);
    }
  }

  private async load(): Promise<void> {
    this.claims.set(await this.api.listMyClaims());
  }

  private async loadCategories(): Promise<void> {
    this.categories.set((await this.api.listCategories()).filter((c) => c.isActive));
  }
}
