import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, type OnInit, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableColumn } from '@gggs/shared/data-table';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { AttendanceRow, AttendanceDevice, PagedResult, Regularization } from '@gggs/shared/models';
import { formatMinutes, shortTime, toAttendanceRows } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { AttendanceApi, addDays, todayIso } from '@gggs/shared/time';
import { EmptyState, FormDialog, PageHeader, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';

/**
 * Attendance for HR: days over a period for anyone in reach, corrections people asked for, the
 * devices whose punches are imported, and recalculating stored days after a rule change.
 */
@Component({
  selector: 'gggs-attendance-admin-page',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, Tabs, DataTable, DataTableCell, StatusBadge, OrgSelect, EmployeeSelect, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Attendance" subtitle="Days, corrections and devices.">
      <div class="toolbar">
        <a class="btn" routerLink="shifts" *gggsHasPermission="['attendance.shift.manage', 'attendance.roster.manage']"><i class="pi pi-calendar-clock" aria-hidden="true"></i> Shifts &amp; rosters</a>
        <button type="button" class="btn" *gggsHasPermission="'attendance.record.manage'" [disabled]="busy()" (click)="recompute()">Recalculate</button>
      </div>
    </gggs-page-header>

    <gggs-tabs [tabs]="tabs" [(selected)]="tab" (selectedChange)="onTab()" />

    <div class="admin__body">
      @switch (tab()) {
        @case ('days') {
          <section class="panel toolbar admin__filters">
            <label class="field">
              <span class="field__label">From</span>
              <input type="date" [value]="from()" (change)="from.set($any($event.target).value); loadDays()" />
            </label>
            <label class="field">
              <span class="field__label">To</span>
              <input type="date" [value]="to()" (change)="to.set($any($event.target).value); loadDays()" />
            </label>
            <label class="field admin__picker">
              <span class="field__label">Department</span>
              <gggs-org-select kind="departments" placeholder="Everyone" ariaLabel="Department" [formControl]="department" />
            </label>
            <label class="field admin__picker">
              <span class="field__label">Employee</span>
              <gggs-employee-select ariaLabel="Employee" [formControl]="employee" />
            </label>
          </section>
          <gggs-data-table [result]="days()" [columns]="columns" [loading]="loading()" exportName="attendance-days" emptyTitle="No days in this period">
            <ng-template gggsCell="date" let-row>{{ row.date | date: 'EEE d MMM' }}</ng-template>
            <ng-template gggsCell="firstIn" let-row>{{ shortTime(row.firstIn) || '—' }}</ng-template>
            <ng-template gggsCell="lastOut" let-row>{{ shortTime(row.lastOut) || '—' }}</ng-template>
            <ng-template gggsCell="workedMinutes" let-row>{{ formatMinutes(row.workedMinutes) || '—' }}</ng-template>
            <ng-template gggsCell="lateMinutes" let-row>{{ formatMinutes(row.lateMinutes) }}</ng-template>
            <ng-template gggsCell="status" let-row><gggs-status-badge [status]="row.status" /></ng-template>
          </gggs-data-table>
        }
        @case ('corrections') {
          <section class="panel">
            @if (corrections().length === 0) {
              <gggs-empty-state icon="pi pi-pencil" title="No corrections" />
            } @else {
              <table class="table grid">
                <thead>
                  <tr>
                    <th scope="col">Employee</th>
                    <th scope="col">Day</th>
                    <th scope="col">In</th>
                    <th scope="col">Out</th>
                    <th scope="col">Reason</th>
                    <th scope="col">Status</th>
                  </tr>
                </thead>
                <tbody>
                  @for (request of corrections(); track request.id) {
                    <tr>
                      <td>{{ request.employeeName }}</td>
                      <td>{{ request.date | date: 'EEE d MMM' }}</td>
                      <td>{{ shortTime(request.checkIn) || '—' }}</td>
                      <td>{{ shortTime(request.checkOut) || '—' }}</td>
                      <td>{{ request.reason }}</td>
                      <td>
                        <gggs-status-badge [status]="request.status" />
                        @if (request.workflowInstanceId) {
                          <a class="btn btn--link btn--sm" [routerLink]="['/approvals/requests', request.workflowInstanceId]">Approval</a>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        }
        @case ('devices') {
          <section class="panel stack">
            <div class="toolbar">
              <p class="muted admin__hint">Devices send punches to <code>POST /api/v1/attendance/punches/import</code> through an integration account with attendance.device.manage.</p>
              <button type="button" class="btn btn--primary btn--sm" (click)="openDevice(null)">Add device</button>
            </div>
            @if (devices().length === 0) {
              <gggs-empty-state icon="pi pi-tablet" title="No devices registered" />
            } @else {
              <table class="table grid">
                <thead>
                  <tr>
                    <th scope="col">Code</th>
                    <th scope="col">Name</th>
                    <th scope="col">Last seen</th>
                    <th scope="col">Status</th>
                    <th scope="col"><span class="grid__hidden">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (device of devices(); track device.id) {
                    <tr>
                      <td><code>{{ device.code }}</code></td>
                      <td>{{ device.name }}</td>
                      <td>{{ (device.lastSeenDate | date: 'medium') ?? 'Never' }}</td>
                      <td><gggs-status-badge [status]="device.isActive ? 'Active' : 'Inactive'" /></td>
                      <td>
                        <button type="button" class="btn btn--link btn--sm" (click)="openDevice(device)">Edit</button>
                        <button type="button" class="btn btn--link btn--sm" (click)="toggleDevice(device)">{{ device.isActive ? 'Retire' : 'Restore' }}</button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        }
      }
      @if (message()) {
        <p class="admin__message" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>

    <gggs-form-dialog [(visible)]="deviceOpen" [title]="editingDevice() ? 'Edit device' : 'Add device'" [busy]="busy()" [error]="dialogError()" (save)="saveDevice()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" maxlength="32" [disabled]="!!editingDevice()" [value]="deviceCode()" (input)="deviceCode.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="deviceName()" (input)="deviceName.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .admin__body {
      margin-block-start: var(--gggs-space-3);
    }

    .admin__filters {
      flex-wrap: wrap;
      align-items: flex-end;
      margin-block-end: var(--gggs-space-3);
    }

    .admin__picker {
      min-width: 14rem;
    }

    .admin__hint {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .admin__message {
      color: var(--gggs-color-success);
    }



    .grid__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class AttendanceAdminPage implements OnInit {
  private readonly api = inject(AttendanceApi);

  protected readonly formatMinutes = formatMinutes;
  protected readonly shortTime = shortTime;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'days', label: 'Days' },
    { key: 'corrections', label: 'Corrections' },
    { key: 'devices', label: 'Devices' },
  ];
  protected readonly columns: readonly DataTableColumn<AttendanceRow>[] = [
    { field: 'employeeName', header: 'Employee' },
    { field: 'date', header: 'Day', width: '9rem' },
    { field: 'shiftName', header: 'Shift' },
    { field: 'firstIn', header: 'In', width: '6rem' },
    { field: 'lastOut', header: 'Out', width: '6rem' },
    { field: 'workedMinutes', header: 'Worked', width: '7rem' },
    { field: 'lateMinutes', header: 'Late', width: '6rem' },
    { field: 'status', header: 'Status', width: '9rem' },
  ];

  /** Bound from route data: `/attendance/regularizations` opens on the corrections tab. */
  readonly startTab = input<string | undefined>(undefined);

  protected readonly tab = signal('days');
  protected readonly from = signal(addDays(todayIso(), -6));
  protected readonly to = signal(todayIso());
  protected readonly department = new FormControl<string | null>(null);
  protected readonly employee = new FormControl<string | null>(null);
  protected readonly days = signal<PagedResult<AttendanceRow> | null>(null);
  protected readonly corrections = signal<readonly Regularization[]>([]);
  protected readonly devices = signal<readonly AttendanceDevice[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly deviceOpen = signal(false);
  protected readonly editingDevice = signal<AttendanceDevice | null>(null);
  protected readonly deviceCode = signal('');
  protected readonly deviceName = signal('');
  protected readonly dialogError = signal<string | null>(null);

  constructor() {
    this.department.valueChanges.subscribe(() => void this.loadDays());
    this.employee.valueChanges.subscribe(() => void this.loadDays());
    void this.loadDays();
  }

  ngOnInit(): void {
    const start = this.startTab();
    if (start && this.tabs.some((item) => item.key === start)) {
      this.tab.set(start);
      this.onTab();
    }
  }

  protected onTab(): void {
    this.message.set(null);
    this.error.set(null);
    if (this.tab() === 'corrections') {
      void this.api.regularizations().then((rows) => this.corrections.set(rows), (error: unknown) => this.fail(error));
    } else if (this.tab() === 'devices') {
      void this.loadDevices();
    }
  }

  protected async loadDays(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const days = await this.api.days({ from: this.from(), to: this.to(), departmentId: this.department.value, employeeId: this.employee.value });
      this.days.set({ items: toAttendanceRows(days), page: 1, pageSize: days.length || 1, totalCount: days.length, totalPages: 1, hasNextPage: false });
    } catch (error) {
      this.fail(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async recompute(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      const result = await this.api.recompute({ from: this.from(), to: this.to(), departmentId: this.department.value, employeeId: this.employee.value });
      this.message.set(`${result.days} day(s) recalculated and stored.`);
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected openDevice(device: AttendanceDevice | null): void {
    this.editingDevice.set(device);
    this.deviceCode.set(device?.code ?? '');
    this.deviceName.set(device?.name ?? '');
    this.dialogError.set(null);
    this.deviceOpen.set(true);
  }

  protected async saveDevice(): Promise<void> {
    if (!this.deviceCode().trim() || !this.deviceName().trim()) {
      this.dialogError.set('Give the device a code and a name.');
      return;
    }
    this.busy.set(true);
    try {
      await this.api.saveDevice(this.editingDevice()?.id ?? null, { code: this.deviceCode().trim(), name: this.deviceName().trim() });
      this.deviceOpen.set(false);
      await this.loadDevices();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The device could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggleDevice(device: AttendanceDevice): Promise<void> {
    try {
      await this.api.setDeviceActive(device.id, !device.isActive);
      await this.loadDevices();
    } catch (error) {
      this.fail(error);
    }
  }

  private async loadDevices(): Promise<void> {
    try {
      this.devices.set(await this.api.devices());
    } catch (error) {
      this.fail(error);
    }
  }

  private fail(error: unknown): void {
    this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
  }
}
