import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Card, PageHeader } from '@gggs/shared/ui';
import { EmployeeReferenceApi } from '@gggs/shared/employee-reference';
import type { EmployeeListItem } from '@gggs/shared/models';

/**
 * "My Team": everyone whose current employment record reports to the caller -
 * `EmployeeReferenceApi.listMyTeam`, backed by `GET /api/v1/employees/my-team`. `/team` in the
 * navigation manifest; `/team/attendance` and `/team/leave` are their own pages in the attendance
 * and leave features, each asking this same list of employee ids for their own module's data.
 */
@Component({
  selector: 'gggs-team-overview-page',
  imports: [PageHeader, Card],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My team" subtitle="Everyone reporting to you." />

    <gggs-card>
      @if (isLoading()) {
        <p class="muted">Loading…</p>
      } @else if (team().length === 0) {
        <p class="muted">Nobody reports to you yet.</p>
      } @else {
        <table class="team-table">
          <thead>
            <tr>
              <th scope="col">Code</th>
              <th scope="col">Name</th>
              <th scope="col">Status</th>
              <th scope="col">Joined</th>
            </tr>
          </thead>
          <tbody>
            @for (employee of team(); track employee.id) {
              <tr (click)="openEmployee(employee)">
                <td>{{ employee.employeeCode }}</td>
                <td>{{ employee.displayName }}</td>
                <td>{{ employee.status }}</td>
                <td>{{ employee.dateOfJoining }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </gggs-card>
  `,
  styles: `
    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .team-table {
      width: 100%;
      border-collapse: collapse;
    }

    .team-table th,
    .team-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .team-table tbody tr {
      cursor: pointer;
    }

    .team-table tbody tr:hover {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
    }
  `,
})
export class TeamOverviewPage {
  private readonly api = inject(EmployeeReferenceApi);
  private readonly router = inject(Router);

  protected readonly team = signal<readonly EmployeeListItem[]>([]);
  protected readonly isLoading = signal(false);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.team.set(await this.api.listMyTeam());
    } finally {
      this.isLoading.set(false);
    }
  }

  protected openEmployee(employee: EmployeeListItem): void {
    void this.router.navigate(['/employees', employee.id]);
  }
}
