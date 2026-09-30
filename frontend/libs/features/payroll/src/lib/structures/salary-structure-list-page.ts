import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { SalaryStructure } from '@gggs/shared/models';
import { PayrollApi } from '../data/payroll-api';

/** Every salary structure this tenant has defined, plus a form to add a new one. */
@Component({
  selector: 'gggs-salary-structure-list-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Salary structures" subtitle="A named set of components an employee's salary is assigned from." />

    <gggs-card title="Structures">
      @if (structures(); as items) {
        @if (items.length > 0) {
          <table class="structures-table">
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Name</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (structure of items; track structure.id) {
                <tr (click)="open(structure.id)" class="structure-row">
                  <td>{{ structure.code }}</td>
                  <td>{{ structure.name }}</td>
                  <td>{{ structure.isActive ? 'Active' : 'Inactive' }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No structures yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'payroll.structure.manage'"
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
          {{ isSaving() ? 'Creating…' : 'Create structure' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .structures-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .structures-table th,
    .structures-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .structure-row {
      cursor: pointer;
    }

    .structure-row:hover {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }

    .create-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
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
export class SalaryStructureListPage {
  private readonly api = inject(PayrollApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly structures = signal<readonly SalaryStructure[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    code: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(100)]],
  });

  constructor() {
    void this.load();
  }

  protected open(structureId: string): void {
    void this.router.navigate(['/payroll/structures', structureId]);
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
      const structure = await this.api.createStructure({ code: value.code ?? '', name: value.name ?? '' });
      this.form.reset();
      await this.router.navigate(['/payroll/structures', structure.id]);
    } catch {
      this.errorMessage.set('The structure could not be created. A code may already be in use.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.structures.set(await this.api.listStructures());
  }
}
