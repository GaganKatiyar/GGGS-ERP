import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Card, PageHeader } from '@gggs/shared/ui';
import { EmployeeReferenceApi } from '@gggs/shared/employee-reference';
import type { EmployeeListItem, LeaveRequestListItem } from '@gggs/shared/models';
import { LeaveApi } from '../leave-api';

interface TeamLeaveRow {
  readonly employee: EmployeeListItem;
  readonly requests: readonly LeaveRequestListItem[];
}

/**
 * Pending and upcoming leave for everyone reporting to the caller - `/team/leave` in the
 * navigation manifest. Approving happens from the unified approvals inbox
 * (`ApprovalsPage`), same as `LeaveRequestsPage`'s own remarks; this is visibility, not action.
 */
@Component({
  selector: 'gggs-team-leave-page',
  imports: [Card, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Team leave" subtitle="Pending and recent requests from your reports." />

    <gggs-card>
      @if (isLoading()) {
        <p class="muted">Loading…</p>
      } @else if (rows().length === 0) {
        <p class="muted">Nobody reports to you yet.</p>
      } @else {
        <table class="team-table">
          <thead>
            <tr>
              <th scope="col">Employee</th>
              <th scope="col">Request #</th>
              <th scope="col">From</th>
              <th scope="col">To</th>
              <th scope="col">Days</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.employee.id) {
              @if (row.requests.length === 0) {
                <tr>
                  <td>{{ row.employee.displayName }}</td>
                  <td colspan="5" class="muted">No requests</td>
                </tr>
              } @else {
                @for (request of row.requests; track request.id) {
                  <tr>
                    <td>{{ row.employee.displayName }}</td>
                    <td>{{ request.requestNo }}</td>
                    <td>{{ request.fromDate }}</td>
                    <td>{{ request.toDate }}</td>
                    <td>{{ request.totalDays }}</td>
                    <td>{{ request.status }}</td>
                  </tr>
                }
              }
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
  `,
})
export class TeamLeavePage {
  private readonly employeeApi = inject(EmployeeReferenceApi);
  private readonly leaveApi = inject(LeaveApi);

  protected readonly rows = signal<readonly TeamLeaveRow[]>([]);
  protected readonly isLoading = signal(false);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const team = await this.employeeApi.listMyTeam();

      const rows = await Promise.all(
        team.map(async (employee): Promise<TeamLeaveRow> => {
          const result = await this.leaveApi.listRequests({
            page: 1,
            pageSize: 5,
            employeeId: employee.id,
          });

          return { employee, requests: result.items };
        }),
      );

      this.rows.set(rows);
    } finally {
      this.isLoading.set(false);
    }
  }
}
