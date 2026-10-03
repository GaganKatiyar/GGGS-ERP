import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import { Card, PageHeader } from '@gggs/shared/ui';
import type { EmployeeListItem, TravelRequest } from '@gggs/shared/models';
import { SpendApi } from '../data/spend-api';

/** Admin: look up an employee to see their travel requests. There is no tenant-wide "list all"
 * endpoint in this v1 slice, the same per-employee lookup shape Payroll's EmployeeSalaryPage
 * already uses. */
@Component({
  selector: 'gggs-travel-request-list-page',
  imports: [DecimalPipe, RouterLink, EmployeePicker, PageHeader, Card],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Travel requests" subtitle="Look up an employee to review and settle their travel." />

    <gggs-card title="Employee" class="section">
      <span class="field__label" id="employee-label">Look up an employee</span>
      <gggs-employee-picker aria-labelledby="employee-label" (employeeSelected)="onEmployeeSelected($event)" />
      @if (selectedEmployeeName()) {
        <p class="selected-name">{{ selectedEmployeeName() }}</p>
      }
    </gggs-card>

    @if (selectedEmployeeId()) {
      <gggs-card title="Travel requests">
        @if (isLoading()) {
          <p class="muted">Loading…</p>
        } @else if (requests().length > 0) {
          <table class="requests-table">
            <thead>
              <tr>
                <th scope="col">Request no</th>
                <th scope="col">Route</th>
                <th scope="col">Estimated cost</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (request of requests(); track request.id) {
                <tr>
                  <td>{{ request.requestNo }}</td>
                  <td>{{ request.fromCity }} → {{ request.toCity }}</td>
                  <td>{{ request.currency }} {{ request.estimatedCost | number: '1.2-2' }}</td>
                  <td>{{ request.status }}</td>
                  <td>
                    <a [routerLink]="['/spend/travel-requests', request.id]">View</a>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No travel requests for this employee yet.</p>
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

    .requests-table {
      width: 100%;
      border-collapse: collapse;
    }

    .requests-table th,
    .requests-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }
  `,
})
export class TravelRequestListPage {
  private readonly api = inject(SpendApi);

  protected readonly selectedEmployeeId = signal<string | null>(null);
  protected readonly selectedEmployeeName = signal<string | null>(null);
  protected readonly requests = signal<readonly TravelRequest[]>([]);
  protected readonly isLoading = signal(false);

  protected async onEmployeeSelected(employee: EmployeeListItem): Promise<void> {
    this.selectedEmployeeId.set(employee.id);
    this.selectedEmployeeName.set(employee.displayName);
    this.isLoading.set(true);

    try {
      this.requests.set(await this.api.listEmployeeTravelRequests(employee.id));
    } finally {
      this.isLoading.set(false);
    }
  }
}
