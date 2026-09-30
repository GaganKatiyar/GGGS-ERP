import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import type {
  AttendanceSummaryReport,
  HeadcountReport,
  LeaveSummaryReport,
} from '@gggs/shared/models';
import { ReportsApi } from './reports-api';

/**
 * Reporting v1 - `report.report.view`'s screen, `/reports` in the navigation manifest. Three
 * direct-read reports (see `ReportsApi`'s own remarks on why there is no read model yet), each its
 * own card so one failing to load never blocks the others.
 */
@Component({
  selector: 'gggs-reports-page',
  imports: [ReactiveFormsModule, PageHeader, Card, FormField, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Reports" subtitle="Headcount, attendance and leave, as of now." />

    <gggs-card title="Headcount" class="section">
      @if (headcount(); as report) {
        <div class="stat-row">
          <div class="stat">
            <span class="stat__value">{{ report.totalActive }}</span>
            <span class="stat__label">Active</span>
          </div>
          <div class="stat">
            <span class="stat__value">{{ report.joiners }}</span>
            <span class="stat__label">Joiners this month</span>
          </div>
          <div class="stat">
            <span class="stat__value">{{ report.leavers }}</span>
            <span class="stat__label">Leavers this month</span>
          </div>
        </div>

        @if (report.byDepartment.length > 0) {
          <table class="report-table">
            <thead>
              <tr>
                <th scope="col">Department</th>
                <th scope="col">Headcount</th>
              </tr>
            </thead>
            <tbody>
              @for (row of report.byDepartment; track row.departmentId) {
                <tr>
                  <td>{{ row.departmentName }}</td>
                  <td>{{ row.headcount }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      } @else {
        <p class="muted">Loading…</p>
      }
    </gggs-card>

    <gggs-card title="Attendance summary" class="section">
      <form [formGroup]="attendanceForm" (ngSubmit)="loadAttendance()" novalidate class="inline-form">
        <gggs-form-field label="From date">
          <input type="date" formControlName="fromDate" />
        </gggs-form-field>
        <gggs-form-field label="To date">
          <input type="date" formControlName="toDate" />
        </gggs-form-field>
        <gggs-button type="submit" size="sm">Refresh</gggs-button>
      </form>

      @if (attendanceSummary(); as report) {
        <p class="muted">
          {{ report.fromDate }} to {{ report.toDate }} - {{ report.totalDays }} day(s) recorded,
          {{ report.regularizedDays }} regularized.
        </p>

        @if (report.byStatus.length > 0) {
          <table class="report-table">
            <thead>
              <tr>
                <th scope="col">Status</th>
                <th scope="col">Days</th>
              </tr>
            </thead>
            <tbody>
              @for (row of report.byStatus; track row.status) {
                <tr>
                  <td>{{ row.status }}</td>
                  <td>{{ row.days }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      } @else {
        <p class="muted">Loading…</p>
      }
    </gggs-card>

    <gggs-card title="Leave summary">
      <form [formGroup]="leaveForm" (ngSubmit)="loadLeave()" novalidate class="inline-form">
        <gggs-form-field label="Year">
          <input type="number" formControlName="periodYear" />
        </gggs-form-field>
        <gggs-button type="submit" size="sm">Refresh</gggs-button>
      </form>

      @if (leaveSummary(); as report) {
        @if (report.byType.length > 0) {
          <table class="report-table">
            <thead>
              <tr>
                <th scope="col">Leave type</th>
                <th scope="col">Taken</th>
                <th scope="col">Pending</th>
              </tr>
            </thead>
            <tbody>
              @for (row of report.byType; track row.leaveTypeId) {
                <tr>
                  <td>{{ row.leaveTypeName }}</td>
                  <td>{{ row.takenDays }}</td>
                  <td>{{ row.pendingDays }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No leave activity for {{ report.periodYear }} yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }
    </gggs-card>
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .stat-row {
      display: flex;
      gap: 1.5rem;
      margin-block-end: 1rem;
    }

    .stat {
      display: flex;
      flex-direction: column;
    }

    .stat__value {
      font-size: 1.5rem;
      font-weight: 700;
    }

    .stat__label {
      font-size: 0.75rem;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .report-table {
      width: 100%;
      border-collapse: collapse;
    }

    .report-table th,
    .report-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .inline-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      margin-block-end: 1rem;
    }
  `,
})
export class ReportsPage {
  private readonly api = inject(ReportsApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly headcount = signal<HeadcountReport | null>(null);
  protected readonly attendanceSummary = signal<AttendanceSummaryReport | null>(null);
  protected readonly leaveSummary = signal<LeaveSummaryReport | null>(null);

  protected readonly attendanceForm = this.formBuilder.nonNullable.group({
    fromDate: [defaultFromDate()],
    toDate: [defaultToDate()],
  });

  protected readonly leaveForm = this.formBuilder.nonNullable.group({
    periodYear: [new Date().getFullYear(), Validators.required],
  });

  constructor() {
    void this.loadHeadcount();
    void this.loadAttendance();
    void this.loadLeave();
  }

  private async loadHeadcount(): Promise<void> {
    this.headcount.set(await this.api.getHeadcount());
  }

  protected async loadAttendance(): Promise<void> {
    const { fromDate, toDate } = this.attendanceForm.getRawValue();
    this.attendanceSummary.set(await this.api.getAttendanceSummary(fromDate, toDate));
  }

  protected async loadLeave(): Promise<void> {
    const { periodYear } = this.leaveForm.getRawValue();
    this.leaveSummary.set(await this.api.getLeaveSummary(periodYear));
  }
}

function defaultFromDate(): string {
  const today = new Date();
  return new Date(today.getFullYear(), today.getMonth(), 1).toISOString().slice(0, 10);
}

function defaultToDate(): string {
  return new Date().toISOString().slice(0, 10);
}
