import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { SalaryComponent, SalaryStructure, SalaryStructureComponent } from '@gggs/shared/models';
import { PayrollApi } from '../data/payroll-api';

/** One structure's component lines, and a form to add another line from the component catalog. A
 * structure needs exactly one Fixed "BASIC" line before it can be assigned to an employee - the
 * backend enforces that, this page just surfaces the rejection if it happens. */
@Component({
  selector: 'gggs-salary-structure-detail-page',
  imports: [RouterLink, ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (structure(); as s) {
      <gggs-page-header [title]="s.name" [subtitle]="s.code">
        <a routerLink="/payroll/structures" class="btn">Back</a>
      </gggs-page-header>

      <gggs-card title="Components" class="section">
        @if (lines().length > 0) {
          <table class="lines-table">
            <thead>
              <tr>
                <th scope="col">Component</th>
                <th scope="col">Calculation</th>
                <th scope="col">Value</th>
                <th scope="col">Wage ceiling</th>
                <th scope="col">Order</th>
              </tr>
            </thead>
            <tbody>
              @for (line of lines(); track line.id) {
                <tr>
                  <td>{{ componentName(line.componentId) }}</td>
                  <td>{{ calcTypeLabel(line.componentId) }}</td>
                  <td>{{ isPercentBased(line.componentId) ? line.value + '%' : line.value }}</td>
                  <td>{{ line.wageCeiling ?? '—' }}</td>
                  <td>{{ line.displayOrder }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No components on this structure yet.</p>
        }

        <form
          *gggsHasPermission="'payroll.structure.manage'"
          [formGroup]="form"
          (ngSubmit)="submit()"
          novalidate
          class="add-form"
        >
          <label class="field">
            <span class="field__label">Component</span>
            <select formControlName="componentId" required>
              <option value="" disabled>Select a component</option>
              @for (component of availableComponents(); track component.id) {
                <option [value]="component.id">{{ component.name }} ({{ component.code }})</option>
              }
            </select>
          </label>

          <label class="field">
            <span class="field__label">{{ selectedIsPercent() ? 'Percentage of Basic' : 'Monthly amount' }}</span>
            <input type="number" formControlName="value" min="0" [max]="selectedIsPercent() ? 100 : null" required />
          </label>

          @if (selectedNeedsWageCeiling()) {
            <label class="field">
              <span class="field__label">Wage ceiling</span>
              <input type="number" formControlName="wageCeiling" min="0.01" required />
            </label>
          }

          <label class="field">
            <span class="field__label">Display order</span>
            <input type="number" formControlName="displayOrder" min="0" required />
          </label>

          @if (errorMessage()) {
            <p class="field__error" role="alert">{{ errorMessage() }}</p>
          }

          <gggs-button type="submit" size="sm" [disabled]="isSaving()">
            {{ isSaving() ? 'Adding…' : 'Add component' }}
          </gggs-button>
        </form>
      </gggs-card>
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .lines-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .lines-table th,
    .lines-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .add-form {
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
    }

    .btn {
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }
  `,
})
export class SalaryStructureDetailPage {
  private readonly api = inject(PayrollApi);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly structure = signal<SalaryStructure | null>(null);
  protected readonly lines = signal<readonly SalaryStructureComponent[]>([]);
  protected readonly components = signal<readonly SalaryComponent[]>([]);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  private readonly structureId = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly availableComponents = computed(() => {
    const usedIds = new Set(this.lines().map((l) => l.componentId));
    return this.components().filter((c) => c.isActive && !usedIds.has(c.id));
  });

  protected readonly form = this.formBuilder.group({
    componentId: ['', Validators.required],
    value: [0, [Validators.required, Validators.min(0)]],
    displayOrder: [1, [Validators.required, Validators.min(0)]],
    wageCeiling: [0, [Validators.min(0.01)]],
  });

  protected readonly selectedIsPercent = computed(() => {
    const id = this.form.get('componentId')?.value;
    return id ? this.isPercentBased(id) : false;
  });

  protected readonly selectedNeedsWageCeiling = computed(() => {
    const id = this.form.get('componentId')?.value;
    const calcType = id ? this.components().find((c) => c.id === id)?.calcType : undefined;
    return calcType === 'PercentOfBasicCapped' || calcType === 'PercentOfBasicWithEligibilityCap';
  });

  constructor() {
    void this.load();
  }

  protected componentName(componentId: string): string {
    const component = this.components().find((c) => c.id === componentId);
    return component ? `${component.name} (${component.code})` : componentId;
  }

  protected isPercentBased(componentId: string): boolean {
    const calcType = this.components().find((c) => c.id === componentId)?.calcType;
    return calcType === 'PercentOfBasic' || calcType === 'PercentOfBasicCapped' || calcType === 'PercentOfBasicWithEligibilityCap';
  }

  protected calcTypeLabel(componentId: string): string {
    const calcType = this.components().find((c) => c.id === componentId)?.calcType;
    switch (calcType) {
      case 'PercentOfBasicCapped':
        return '% of Basic (capped base)';
      case 'PercentOfBasicWithEligibilityCap':
        return '% of Basic (eligibility cap)';
      case 'PercentOfBasic':
        return '% of Basic';
      default:
        return 'Fixed';
    }
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
      const line = await this.api.addStructureComponent(this.structureId, {
        componentId: value.componentId ?? '',
        value: value.value ?? 0,
        displayOrder: value.displayOrder ?? 1,
        ...(this.selectedNeedsWageCeiling() ? { wageCeiling: value.wageCeiling ?? undefined } : {}),
      });

      this.lines.update((lines) => [...lines, line]);
      this.form.reset({ value: 0, displayOrder: this.lines().length + 1, wageCeiling: 0 });
    } catch {
      this.errorMessage.set('That component could not be added. Check it is not already on this structure.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async load(): Promise<void> {
    const [structure, lines, components] = await Promise.all([
      this.api.getStructure(this.structureId),
      this.api.listStructureComponents(this.structureId),
      this.api.listComponents(),
    ]);

    this.structure.set(structure);
    this.lines.set(lines);
    this.components.set(components);
  }
}
