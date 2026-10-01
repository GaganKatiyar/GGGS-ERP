import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { EmployeeListItem, EmployeeSalary, PayslipListItem, SalaryComponent, SalaryStructure } from '@gggs/shared/models';
import { PayrollApi } from '../data/payroll-api';

/** Look up an employee, see their current salary breakdown if one is assigned, and assign a
 * structure + CTC if not. Sensitive: gated by payroll.employee-salary.view/manage. */
@Component({
  selector: 'gggs-employee-salary-page',
  imports: [DecimalPipe, ReactiveFormsModule, HasPermissionDirective, EmployeePicker, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Employee salary" subtitle="Assign a structure and CTC, and view the computed breakdown." />

    <gggs-card title="Employee" class="section">
      <span class="field__label" id="employee-label">Look up an employee</span>
      <gggs-employee-picker aria-labelledby="employee-label" (employeeSelected)="onEmployeeSelected($event)" />
      @if (selectedEmployeeName()) {
        <p class="selected-name">{{ selectedEmployeeName() }}</p>
      }
    </gggs-card>

    @if (selectedEmployeeId()) {
      @if (isLoading()) {
        <p class="muted">Loading…</p>
      } @else if (salary(); as s) {
        <gggs-card title="Current salary" class="section">
          <dl class="salary-grid">
            <dt>Annual CTC</dt>
            <dd>{{ s.currency }} {{ s.annualCtc | number: '1.2-2' }}</dd>
            <dt>Effective from</dt>
            <dd>{{ s.effectiveFrom }}</dd>
          </dl>

          <table class="breakdown-table">
            <thead>
              <tr>
                <th scope="col">Component</th>
                <th scope="col">Monthly</th>
                <th scope="col">Annual</th>
              </tr>
            </thead>
            <tbody>
              @for (component of s.components; track component.id) {
                <tr>
                  <td>{{ componentName(component.componentId) }}</td>
                  <td>{{ s.currency }} {{ component.monthlyAmount | number: '1.2-2' }}</td>
                  <td>{{ s.currency }} {{ component.annualAmount | number: '1.2-2' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </gggs-card>
      } @else {
        <gggs-card title="Assign a salary" class="section">
          <form
            *gggsHasPermission="'payroll.employee-salary.manage'"
            [formGroup]="form"
            (ngSubmit)="submit()"
            novalidate
            class="assign-form"
          >
            <label class="field">
              <span class="field__label">Structure</span>
              <select formControlName="structureId" required>
                <option value="" disabled>Select a structure</option>
                @for (structure of activeStructures(); track structure.id) {
                  <option [value]="structure.id">{{ structure.name }}</option>
                }
              </select>
            </label>

            <label class="field">
              <span class="field__label">Annual CTC</span>
              <input type="number" formControlName="annualCtc" min="1" required />
            </label>

            <label class="field">
              <span class="field__label">Currency</span>
              <input type="text" formControlName="currency" maxlength="3" required />
            </label>

            <label class="field">
              <span class="field__label">Effective from</span>
              <input type="date" formControlName="effectiveFrom" required />
            </label>

            @if (errorMessage()) {
              <p class="field__error" role="alert">{{ errorMessage() }}</p>
            }

            <gggs-button type="submit" size="sm" [disabled]="isSaving()">
              {{ isSaving() ? 'Assigning…' : 'Assign salary' }}
            </gggs-button>
          </form>
        </gggs-card>
      }

      <gggs-card title="Payslip history" *gggsHasPermission="'payroll.run.view'">
        @if (payslips().length > 0) {
          <table class="breakdown-table">
            <thead>
              <tr>
                <th scope="col">Run</th>
                <th scope="col">Pay date</th>
                <th scope="col">Net pay</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (payslip of payslips(); track payslip.id) {
                <tr>
                  <td>{{ payslip.runNo }}</td>
                  <td>{{ payslip.payDate }}</td>
                  <td>{{ payslip.netPay | number: '1.2-2' }}</td>
                  <td>
                    <gggs-button size="sm" variant="ghost" (click)="downloadPayslipPdf(payslip.id, payslip.runNo)">Download PDF</gggs-button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No payslips published for this employee yet.</p>
        }
      </gggs-card>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .field__label {
      font-weight: 600;
      font-size: 0.875rem;
    }

    .selected-name {
      margin: 0.5rem 0 0;
      font-size: 0.8125rem;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .salary-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.25rem 0.75rem;
      margin: 0 0 1rem;
      font-size: 0.875rem;
    }

    .salary-grid dt {
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .salary-grid dd {
      margin: 0;
    }

    .breakdown-table {
      width: 100%;
      border-collapse: collapse;
    }

    .breakdown-table th,
    .breakdown-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .assign-form {
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

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }
  `,
})
export class EmployeeSalaryPage {
  private readonly api = inject(PayrollApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly selectedEmployeeId = signal<string | null>(null);
  protected readonly selectedEmployeeName = signal<string | null>(null);
  protected readonly salary = signal<EmployeeSalary | null>(null);
  protected readonly isLoading = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly activeStructures = signal<readonly SalaryStructure[]>([]);
  protected readonly components = signal<readonly SalaryComponent[]>([]);
  protected readonly payslips = signal<readonly PayslipListItem[]>([]);

  protected readonly form = this.formBuilder.group({
    structureId: ['', Validators.required],
    annualCtc: [0, [Validators.required, Validators.min(1)]],
    currency: ['INR', [Validators.required, Validators.maxLength(3)]],
    effectiveFrom: ['', Validators.required],
  });

  constructor() {
    void this.loadStructures();
    void this.loadComponents();
  }

  protected componentName(componentId: string): string {
    const component = this.components().find((c) => c.id === componentId);
    return component ? `${component.name} (${component.code})` : componentId;
  }

  protected async downloadPayslipPdf(id: string, runNo: string): Promise<void> {
    const blob = await this.api.getPayslipPdf(id);
    const url = URL.createObjectURL(blob);
    try {
      const link = document.createElement('a');
      link.href = url;
      link.download = `payslip-${runNo}.pdf`;
      link.click();
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  protected async onEmployeeSelected(employee: EmployeeListItem): Promise<void> {
    this.selectedEmployeeId.set(employee.id);
    this.selectedEmployeeName.set(employee.displayName);
    this.isLoading.set(true);

    try {
      this.salary.set(await this.api.getEmployeeSalary(employee.id));
      this.payslips.set(await this.api.listEmployeePayslips(employee.id));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    const employeeId = this.selectedEmployeeId();

    if (!employeeId || this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      this.salary.set(
        await this.api.assignEmployeeSalary({
          employeeId,
          structureId: value.structureId ?? '',
          annualCtc: value.annualCtc ?? 0,
          currency: (value.currency ?? 'INR').toUpperCase(),
          effectiveFrom: value.effectiveFrom ?? '',
        }),
      );
    } catch {
      this.errorMessage.set('The salary could not be assigned. The structure may have no Basic component configured.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async loadStructures(): Promise<void> {
    const structures = await this.api.listStructures();
    this.activeStructures.set(structures.filter((s) => s.isActive));
  }

  private async loadComponents(): Promise<void> {
    this.components.set(await this.api.listComponents());
  }
}
