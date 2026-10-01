import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { SalaryComponent, SalaryComponentCalcType, SalaryComponentSlab } from '@gggs/shared/models';
import { PayrollApi } from '../data/payroll-api';

/** Every salary component this tenant has defined, plus a form to add a new one. A structure's
 * lines are built from this catalog - see SalaryStructureDetailPage. */
@Component({
  selector: 'gggs-salary-component-list-page',
  imports: [DecimalPipe, ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      title="Salary components"
      subtitle="Basic, HRA, deductions and the rest - the catalog a structure is built from."
    />

    <gggs-card title="Components">
      @if (components(); as items) {
        @if (items.length > 0) {
          <table class="components-table">
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Name</th>
                <th scope="col">Kind</th>
                <th scope="col">Calculation</th>
                <th scope="col">Taxable</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (component of items; track component.id) {
                <tr>
                  <td>{{ component.code }}</td>
                  <td>{{ component.name }}</td>
                  <td>{{ component.kind }}</td>
                  <td>{{ calcTypeLabel(component.calcType) }}</td>
                  <td>{{ component.isTaxable ? 'Yes' : 'No' }}</td>
                  <td>{{ component.isActive ? 'Active' : 'Inactive' }}</td>
                  <td class="component-actions">
                    @if (component.calcType === 'FlatSlab') {
                      <gggs-button size="sm" variant="ghost" (click)="toggleSlabs(component.id)">
                        {{ expandedComponentId() === component.id ? 'Hide slabs' : 'Slabs' }}
                      </gggs-button>
                    }
                    <span *gggsHasPermission="'payroll.component.manage'">
                      @if (component.isActive) {
                        <gggs-button size="sm" (click)="deactivate(component.id)" [disabled]="isActing()">Deactivate</gggs-button>
                      } @else {
                        <gggs-button size="sm" (click)="reactivate(component.id)" [disabled]="isActing()">Reactivate</gggs-button>
                      }
                    </span>
                  </td>
                </tr>
                @if (expandedComponentId() === component.id) {
                  <tr>
                    <td colspan="7" class="slabs-panel">
                      @if (slabs(); as items2) {
                        @if (items2.length > 0) {
                          <table class="slabs-table">
                            <thead>
                              <tr>
                                <th scope="col">At or above Basic</th>
                                <th scope="col">Amount</th>
                              </tr>
                            </thead>
                            <tbody>
                              @for (slab of items2; track slab.id) {
                                <tr>
                                  <td>{{ slab.thresholdAmount | number: '1.2-2' }}</td>
                                  <td>{{ slab.taxAmount | number: '1.2-2' }}</td>
                                </tr>
                              }
                            </tbody>
                          </table>
                        } @else {
                          <p class="muted">No slabs configured yet.</p>
                        }
                      } @else {
                        <p class="muted">Loading…</p>
                      }

                      <form
                        *gggsHasPermission="'payroll.component.manage'"
                        [formGroup]="slabForm"
                        (ngSubmit)="submitSlab(component.id)"
                        novalidate
                        class="add-slab-form"
                      >
                        <label class="field">
                          <span class="field__label">At or above Basic</span>
                          <input type="number" formControlName="thresholdAmount" min="0" required />
                        </label>

                        <label class="field">
                          <span class="field__label">Amount</span>
                          <input type="number" formControlName="taxAmount" min="0" required />
                        </label>

                        @if (slabErrorMessage()) {
                          <p class="field__error" role="alert">{{ slabErrorMessage() }}</p>
                        }

                        <gggs-button type="submit" size="sm" [disabled]="isSavingSlab()">
                          {{ isSavingSlab() ? 'Adding…' : 'Add slab' }}
                        </gggs-button>
                      </form>
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No components yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'payroll.component.manage'"
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

        <label class="field">
          <span class="field__label">Kind</span>
          <select formControlName="kind" required>
            <option value="Earning">Earning</option>
            <option value="Deduction">Deduction</option>
          </select>
        </label>

        <label class="field">
          <span class="field__label">Calculation</span>
          <select formControlName="calcType" required>
            <option value="Fixed">Fixed monthly amount</option>
            <option value="PercentOfBasic">Percentage of Basic</option>
            <option value="PercentOfBasicCapped">Percentage of Basic, capped base (e.g. PF)</option>
            <option value="PercentOfBasicWithEligibilityCap">Percentage of Basic, eligibility cap (e.g. ESI)</option>
            <option value="FlatSlab">Flat slab lookup (e.g. PT)</option>
          </select>
        </label>

        <label class="field field--checkbox">
          <input type="checkbox" formControlName="isTaxable" />
          <span class="field__label">Taxable</span>
        </label>

        <label class="field">
          <span class="field__label">Display order</span>
          <input type="number" formControlName="displayOrder" min="0" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Create component' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .components-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .components-table th,
    .components-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .component-actions {
      display: flex;
      gap: 0.5rem;
    }

    .slabs-panel {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
      padding: 1rem;
    }

    .slabs-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .slabs-table th,
    .slabs-table td {
      text-align: left;
      padding: 0.375rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.8125rem;
    }

    .add-slab-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
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

    .field--checkbox {
      flex-direction: row;
      align-items: center;
      gap: 0.5rem;
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
export class SalaryComponentListPage {
  private readonly api = inject(PayrollApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly components = signal<readonly SalaryComponent[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly expandedComponentId = signal<string | null>(null);
  protected readonly slabs = signal<readonly SalaryComponentSlab[] | null>(null);
  protected readonly isSavingSlab = signal(false);
  protected readonly slabErrorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    code: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(100)]],
    kind: ['Earning' as 'Earning' | 'Deduction', Validators.required],
    calcType: ['Fixed' as SalaryComponentCalcType, Validators.required],
    isTaxable: [true],
    displayOrder: [1, [Validators.required, Validators.min(0)]],
  });

  protected readonly slabForm = this.formBuilder.group({
    thresholdAmount: [0, [Validators.required, Validators.min(0)]],
    taxAmount: [0, [Validators.required, Validators.min(0)]],
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
      await this.api.createComponent({
        code: value.code ?? '',
        name: value.name ?? '',
        kind: value.kind ?? 'Earning',
        calcType: value.calcType ?? 'Fixed',
        isTaxable: value.isTaxable ?? true,
        displayOrder: value.displayOrder ?? 1,
      });

      this.form.reset({ kind: 'Earning', calcType: 'Fixed', isTaxable: true, displayOrder: 1 });
      await this.load();
    } catch {
      this.errorMessage.set('The component could not be created. A code may already be in use.');
    } finally {
      this.isSaving.set(false);
    }
  }

  protected calcTypeLabel(calcType: SalaryComponentCalcType): string {
    switch (calcType) {
      case 'PercentOfBasicCapped':
        return '% of Basic (capped base)';
      case 'PercentOfBasicWithEligibilityCap':
        return '% of Basic (eligibility cap)';
      case 'PercentOfBasic':
        return '% of Basic';
      case 'FlatSlab':
        return 'Flat slab';
      default:
        return 'Fixed';
    }
  }

  protected async toggleSlabs(componentId: string): Promise<void> {
    if (this.expandedComponentId() === componentId) {
      this.expandedComponentId.set(null);
      return;
    }

    this.expandedComponentId.set(componentId);
    this.slabErrorMessage.set(null);
    this.slabForm.reset({ thresholdAmount: 0, taxAmount: 0 });
    this.slabs.set(null);
    this.slabs.set(await this.api.listComponentSlabs(componentId));
  }

  protected async submitSlab(componentId: string): Promise<void> {
    if (this.slabForm.invalid || this.isSavingSlab()) {
      this.slabForm.markAllAsTouched();
      return;
    }

    this.isSavingSlab.set(true);
    this.slabErrorMessage.set(null);

    const value = this.slabForm.getRawValue();

    try {
      const slab = await this.api.addComponentSlab(componentId, {
        thresholdAmount: value.thresholdAmount ?? 0,
        taxAmount: value.taxAmount ?? 0,
      });

      this.slabs.update((existing) => [...(existing ?? []), slab]);
      this.slabForm.reset({ thresholdAmount: 0, taxAmount: 0 });
    } catch {
      this.slabErrorMessage.set('That slab could not be added. A slab at this threshold may already exist.');
    } finally {
      this.isSavingSlab.set(false);
    }
  }

  protected async deactivate(id: string): Promise<void> {
    await this.act(() => this.api.deactivateComponent(id));
  }

  protected async reactivate(id: string): Promise<void> {
    await this.act(() => this.api.reactivateComponent(id));
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
    this.components.set(await this.api.listComponents());
  }
}
