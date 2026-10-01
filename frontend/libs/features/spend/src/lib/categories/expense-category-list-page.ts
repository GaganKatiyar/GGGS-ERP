import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { ExpenseCategory } from '@gggs/shared/models';
import { SpendApi } from '../data/spend-api';

/** Every expense category this tenant has defined, plus a form to add a new one. A claim line is
 * built against this catalog - see ClaimDetailPage. */
@Component({
  selector: 'gggs-expense-category-list-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Expense categories" subtitle="Travel, meals and the rest - the catalog a claim line is built from." />

    <gggs-card title="Categories">
      @if (categories(); as items) {
        @if (items.length > 0) {
          <table class="categories-table">
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Name</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (category of items; track category.id) {
                <tr>
                  <td>{{ category.code }}</td>
                  <td>{{ category.name }}</td>
                  <td>{{ category.isActive ? 'Active' : 'Inactive' }}</td>
                  <td class="category-actions" *gggsHasPermission="'spend.category.manage'">
                    @if (category.isActive) {
                      <gggs-button size="sm" (click)="deactivate(category.id)" [disabled]="isActing()">Deactivate</gggs-button>
                    } @else {
                      <gggs-button size="sm" (click)="reactivate(category.id)" [disabled]="isActing()">Reactivate</gggs-button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No categories yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'spend.category.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Code</span>
          <input type="text" formControlName="code" maxlength="30" required />
        </label>

        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" formControlName="name" maxlength="100" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Create category' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .categories-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .categories-table th,
    .categories-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .category-actions {
      display: flex;
      gap: 0.5rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
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
  `,
})
export class ExpenseCategoryListPage {
  private readonly api = inject(SpendApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly categories = signal<readonly ExpenseCategory[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    code: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(100)]],
  });

  constructor() {
    void this.load();
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      await this.api.createCategory({ code: value.code ?? '', name: value.name ?? '' });
      this.form.reset();
      await this.load();
    } catch {
      this.errorMessage.set('The category could not be created. A code may already be in use.');
    } finally {
      this.isSaving.set(false);
    }
  }

  protected async deactivate(id: string): Promise<void> {
    await this.act(() => this.api.deactivateCategory(id));
  }

  protected async reactivate(id: string): Promise<void> {
    await this.act(() => this.api.reactivateCategory(id));
  }

  private async act(run: () => Promise<unknown>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);

    try {
      await run();
      await this.load();
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.categories.set(await this.api.listCategories());
  }
}
