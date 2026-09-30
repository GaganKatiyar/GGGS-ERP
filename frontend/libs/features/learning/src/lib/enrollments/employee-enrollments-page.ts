import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import { Card, PageHeader } from '@gggs/shared/ui';
import type { EmployeeListItem } from '@gggs/shared/models';
import { EmployeeEnrollmentsPanel } from './employee-enrollments-panel';

/** Look up an employee, then manage their enrollments. */
@Component({
  selector: 'gggs-employee-enrollments-page',
  imports: [EmployeePicker, PageHeader, Card, EmployeeEnrollmentsPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Enrollments" subtitle="Assign courses to an employee and track their progress." />

    <gggs-card title="Employee" class="section">
      <span class="field__label" id="employee-label">Look up an employee</span>
      <gggs-employee-picker aria-labelledby="employee-label" (employeeSelected)="onEmployeeSelected($event)" />
      @if (selectedEmployeeName()) {
        <p class="selected-name">{{ selectedEmployeeName() }}</p>
      }
    </gggs-card>

    @if (selectedEmployeeId(); as employeeId) {
      <gggs-card title="Enrollments">
        <gggs-employee-enrollments-panel [employeeId]="employeeId" />
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
  `,
})
export class EmployeeEnrollmentsPage {
  protected readonly selectedEmployeeId = signal<string | null>(null);
  protected readonly selectedEmployeeName = signal<string | null>(null);

  protected onEmployeeSelected(employee: EmployeeListItem): void {
    this.selectedEmployeeId.set(employee.id);
    this.selectedEmployeeName.set(employee.displayName);
  }
}
