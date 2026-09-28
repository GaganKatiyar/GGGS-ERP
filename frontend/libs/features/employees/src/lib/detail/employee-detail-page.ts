import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { Employee, EmploymentRecord } from '@gggs/shared/models';
import { EmployeesApi } from '../data/employees-api';

/**
 * One employee: their core record and their full employment history.
 *
 * The id comes from the route via `withComponentInputBinding()` (app.config.ts already enables
 * it), so this needs no `ActivatedRoute` injection of its own.
 */
@Component({
  selector: 'gggs-employee-detail-page',
  imports: [RouterLink, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (employee(); as employee) {
      <gggs-page-header [title]="employee.displayName" [subtitle]="subtitle(employee)">
        <a [routerLink]="['/employees', employee.id, 'transfer']" class="btn" *gggsHasPermission="'employee.employment.manage'">
          Transfer
        </a>
        @if (employee.status === 'PreBoarding' || employee.status === 'Probation') {
          <a [routerLink]="['/employees', employee.id, 'confirm']" class="btn" *gggsHasPermission="'employee.employment.manage'">
            Confirm
          </a>
        }
        @if (employee.status !== 'Exited' && employee.status !== 'Absconded') {
          <a [routerLink]="['/employees', employee.id, 'exit']" class="btn btn--danger" *gggsHasPermission="'employee.exit.manage'">
            Record exit
          </a>
        }
      </gggs-page-header>

      <section class="panel">
        <h2 class="panel__heading">Profile</h2>
        <dl class="facts">
          <div class="facts__row">
            <dt>Employee code</dt>
            <dd>{{ employee.employeeCode }}</dd>
          </div>
          <div class="facts__row">
            <dt>Work email</dt>
            <dd>{{ employee.workEmail ?? '—' }}</dd>
          </div>
          <div class="facts__row">
            <dt>Date of joining</dt>
            <dd>{{ employee.dateOfJoining }}</dd>
          </div>
          @if (employee.originalHireDate) {
            <div class="facts__row">
              <dt>Original hire date</dt>
              <dd>{{ employee.originalHireDate }}</dd>
            </div>
          }
          @if (employee.confirmationDate) {
            <div class="facts__row">
              <dt>Confirmed on</dt>
              <dd>{{ employee.confirmationDate }}</dd>
            </div>
          }
          @if (employee.exitDate) {
            <div class="facts__row">
              <dt>Exit date</dt>
              <dd>{{ employee.exitDate }}</dd>
            </div>
          }
        </dl>
      </section>

      <section class="panel" *gggsHasPermission="'employee.history.view'">
        <h2 class="panel__heading">Employment history</h2>

        @if (history().length === 0) {
          <gggs-empty-state title="No employment history yet" />
        } @else {
          <table class="list-table">
            <thead>
              <tr>
                <th scope="col">From</th>
                <th scope="col">To</th>
                <th scope="col">Change</th>
                <th scope="col">Employment type</th>
                <th scope="col">Reason</th>
              </tr>
            </thead>
            <tbody>
              @for (record of history(); track record.id) {
                <tr [class.row--current]="record.isCurrent">
                  <td>{{ record.effectiveFrom }}</td>
                  <td>{{ record.effectiveTo ?? (record.isCurrent ? 'Current' : '—') }}</td>
                  <td>{{ record.changeType }}</td>
                  <td>{{ record.employmentType }}</td>
                  <td>{{ record.changeReason ?? '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </section>
    } @else {
      <gggs-empty-state title="Employee not found" />
    }
  `,
  styles: `
    .panel {
      padding: 1rem;
      margin-block-end: 1rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .panel__heading {
      margin: 0 0 0.75rem;
      font-size: 1rem;
    }

    .facts {
      display: grid;
      gap: 0.5rem;
      margin: 0;
    }

    .facts__row {
      display: flex;
      gap: 0.5rem;
    }

    .facts__row dt {
      width: 10rem;
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .facts__row dd {
      margin: 0;
    }

    .list-table {
      width: 100%;
      border-collapse: collapse;
    }

    .list-table th,
    .list-table td {
      padding: 0.5rem 0.75rem;
      text-align: left;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
    }

    .row--current {
      font-weight: 600;
    }

    .btn {
      display: inline-flex;
      align-items: center;
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
    }

    .btn--danger {
      border-color: var(--gggs-color-danger, #b3261e);
      color: var(--gggs-color-danger, #b3261e);
    }
  `,
})
export class EmployeeDetailPage {
  private readonly api = inject(EmployeesApi);

  readonly id = input.required<string>();

  protected readonly isLoading = signal(true);
  protected readonly employee = signal<Employee | null>(null);
  protected readonly history = signal<readonly EmploymentRecord[]>([]);

  constructor() {
    void this.load();
  }

  protected subtitle(employee: Employee): string {
    return `${employee.employeeCode} · ${employee.status}`;
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const [employee, history] = await Promise.all([
        this.api.get(this.id()),
        this.api.getEmploymentHistory(this.id()),
      ]);

      this.employee.set(employee);
      this.history.set(history);
    } catch {
      this.employee.set(null);
    } finally {
      this.isLoading.set(false);
    }
  }
}
