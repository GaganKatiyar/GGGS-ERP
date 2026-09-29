import { SlicePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Card, PageHeader } from '@gggs/shared/ui';
import { EmployeeReferenceApi } from '@gggs/shared/employee-reference';
import {
  ATTENDANCE_DAY_STATUS_LABELS,
  type AttendanceDay,
  type EmployeeListItem,
} from '@gggs/shared/models';
import { AttendanceApi } from '../attendance-api';

interface TeamAttendanceRow {
  readonly employee: EmployeeListItem;
  readonly today: AttendanceDay | undefined;
}

/**
 * Today's attendance for everyone reporting to the caller - `/team/attendance` in the navigation
 * manifest. Fetches the team once from `EmployeeReferenceApi` (shared - see its own remarks on why
 * this feature cannot own employee search itself), then one day-lookup per employee; a team is
 * small enough that N+1 requests here is simpler than a batch endpoint this module does not have
 * yet.
 */
@Component({
  selector: 'gggs-team-attendance-page',
  imports: [Card, PageHeader, SlicePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Team attendance" subtitle="Today's status for everyone reporting to you." />

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
              <th scope="col">Status</th>
              <th scope="col">First in</th>
              <th scope="col">Worked</th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.employee.id) {
              <tr>
                <td>{{ row.employee.displayName }}</td>
                <td>{{ row.today ? statusLabel(row.today) : 'No record' }}</td>
                <td>{{ row.today?.firstIn ? (row.today!.firstIn! | slice: 11 : 16) : '—' }}</td>
                <td>{{ row.today ? row.today.workedMinutes + ' min' : '—' }}</td>
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
  `,
})
export class TeamAttendancePage {
  private readonly employeeApi = inject(EmployeeReferenceApi);
  private readonly attendanceApi = inject(AttendanceApi);

  protected readonly rows = signal<readonly TeamAttendanceRow[]>([]);
  protected readonly isLoading = signal(false);

  constructor() {
    void this.load();
  }

  protected statusLabel(day: AttendanceDay): string {
    return ATTENDANCE_DAY_STATUS_LABELS[day.status];
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const team = await this.employeeApi.listMyTeam();
      const today = new Date().toISOString().slice(0, 10);

      const rows = await Promise.all(
        team.map(async (employee): Promise<TeamAttendanceRow> => {
          const days = await this.attendanceApi.listDays(employee.id, today, today);
          return { employee, today: days[0] };
        }),
      );

      this.rows.set(rows);
    } finally {
      this.isLoading.set(false);
    }
  }
}
