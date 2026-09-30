import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import type { BiometricDevice, DeviceUserMap, EmployeeListItem } from '@gggs/shared/models';
import { AttendanceApi } from '../attendance-api';

/**
 * Biometric device administration - `attendance.device.manage`'s screen, `/attendance/devices`.
 * Registering or rotating a key shows the plaintext once: the API never returns it again after
 * this response, so the operator has to copy it down here.
 */
@Component({
  selector: 'gggs-devices-page',
  imports: [ReactiveFormsModule, PageHeader, Card, Button, FormField, EmployeePicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Biometric devices" subtitle="Attendance-punch terminals that push directly to this tenant." />

    @if (revealedApiKey(); as key) {
      <gggs-card title="Device API key" class="section key-card">
        <p class="key-warning">
          This key will not be shown again. Copy it into the device's configuration now.
        </p>
        <code class="key-value">{{ key }}</code>
        <gggs-button size="sm" variant="ghost" (click)="dismissApiKey()">Done</gggs-button>
      </gggs-card>
    }

    <gggs-card title="Register a device" class="section">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="form-grid">
        <gggs-form-field label="Serial number" required>
          <input type="text" formControlName="serialNumber" maxlength="100" required />
        </gggs-form-field>

        <gggs-form-field label="Name" required>
          <input type="text" formControlName="name" maxlength="200" required />
        </gggs-form-field>

        <gggs-form-field label="Branch ID" required>
          <input type="text" formControlName="branchId" required />
        </gggs-form-field>

        @if (errorMessage()) {
          <p class="field-error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" [loading]="isSaving()">Register</gggs-button>
      </form>
    </gggs-card>

    <gggs-card title="Registered devices">
      @if (devices().length === 0) {
        <p class="muted">No devices registered yet.</p>
      } @else {
        <table class="devices-table">
          <thead>
            <tr>
              <th scope="col">Serial</th>
              <th scope="col">Name</th>
              <th scope="col">Status</th>
              <th scope="col">Last seen</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (device of devices(); track device.id) {
              <tr>
                <td>{{ device.serialNumber }}</td>
                <td>{{ device.name }}</td>
                <td>{{ device.status }}</td>
                <td>{{ device.lastSeenDate || '—' }}</td>
                <td class="device-actions">
                  <gggs-button size="sm" variant="ghost" (click)="rotateKey(device.id)" [disabled]="isActing()">
                    Rotate key
                  </gggs-button>
                  @if (device.status === 'Active') {
                    <gggs-button size="sm" variant="ghost" (click)="deactivate(device.id)" [disabled]="isActing()">
                      Deactivate
                    </gggs-button>
                  } @else {
                    <gggs-button size="sm" variant="ghost" (click)="reactivate(device.id)" [disabled]="isActing()">
                      Reactivate
                    </gggs-button>
                  }
                  <gggs-button size="sm" variant="ghost" (click)="toggleUserMaps(device.id)">
                    {{ expandedDeviceId() === device.id ? 'Hide mappings' : 'User mappings' }}
                  </gggs-button>
                </td>
              </tr>
              @if (expandedDeviceId() === device.id) {
                <tr>
                  <td colspan="5" class="user-maps-cell">
                    @if (userMaps().length > 0) {
                      <table class="user-maps-table">
                        <thead>
                          <tr>
                            <th scope="col">Device user code</th>
                            <th scope="col">Employee</th>
                          </tr>
                        </thead>
                        <tbody>
                          @for (map of userMaps(); track map.id) {
                            <tr>
                              <td>{{ map.deviceUserCode }}</td>
                              <td>{{ map.employeeId }}</td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    } @else {
                      <p class="muted">No employees mapped to this device yet.</p>
                    }

                    <form [formGroup]="userMapForm" (ngSubmit)="submitUserMap(device.id)" novalidate class="user-map-form">
                      <gggs-form-field label="Device user code" required>
                        <input type="text" formControlName="deviceUserCode" maxlength="50" required />
                      </gggs-form-field>

                      <gggs-form-field label="Employee" required>
                        <gggs-employee-picker (employeeSelected)="onEmployeeSelected($event)" />
                      </gggs-form-field>

                      @if (selectedEmployee(); as employee) {
                        <p class="selected-employee">Selected: {{ employee.displayName }}</p>
                      }

                      @if (userMapErrorMessage()) {
                        <p class="field-error" role="alert">{{ userMapErrorMessage() }}</p>
                      }

                      <gggs-button type="submit" size="sm" [loading]="isSavingUserMap()" [disabled]="!selectedEmployee()">
                        Map
                      </gggs-button>
                    </form>
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>
      }
    </gggs-card>
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .key-card {
      border: 1px solid var(--gggs-color-warning, #b45309);
    }

    .key-warning {
      margin: 0 0 0.5rem;
      font-size: 0.875rem;
      color: var(--gggs-color-warning, #b45309);
    }

    .key-value {
      display: block;
      padding: 0.5rem 0.75rem;
      margin: 0 0 0.75rem;
      background: var(--gggs-color-surface-sunken, #f5f6f8);
      border-radius: 0.25rem;
      font-size: 0.8125rem;
      word-break: break-all;
    }

    .form-grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      align-items: end;
    }

    .field-error {
      grid-column: 1 / -1;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .devices-table,
    .user-maps-table {
      width: 100%;
      border-collapse: collapse;
    }

    .devices-table th,
    .devices-table td,
    .user-maps-table th,
    .user-maps-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .device-actions {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
    }

    .user-maps-cell {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
      padding: 0.75rem 1rem;
    }

    .user-map-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      margin-top: 0.75rem;
      flex-wrap: wrap;
    }

    .selected-employee {
      margin: 0;
      font-size: 0.8125rem;
      color: var(--gggs-color-text-muted, #5b6472);
    }
  `,
})
export class DevicesPage {
  private readonly api = inject(AttendanceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly devices = signal<readonly BiometricDevice[]>([]);
  protected readonly isSaving = signal(false);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly revealedApiKey = signal<string | null>(null);

  protected readonly expandedDeviceId = signal<string | null>(null);
  protected readonly userMaps = signal<readonly DeviceUserMap[]>([]);
  protected readonly isSavingUserMap = signal(false);
  protected readonly userMapErrorMessage = signal<string | null>(null);
  protected readonly selectedEmployee = signal<EmployeeListItem | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    serialNumber: ['', [Validators.required, Validators.maxLength(100)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    branchId: ['', Validators.required],
  });

  protected readonly userMapForm = this.formBuilder.nonNullable.group({
    deviceUserCode: ['', [Validators.required, Validators.maxLength(50)]],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.devices.set(await this.api.listDevices());
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    try {
      const registered = await this.api.registerDevice(this.form.getRawValue());
      this.devices.update((devices) => [...devices, registered.device]);
      this.revealedApiKey.set(registered.apiKey);
      this.form.reset();
    } catch (error) {
      this.errorMessage.set(
        error instanceof ApiError && error.errorCode === 'BIOMETRIC_DEVICE_SERIAL_ALREADY_EXISTS'
          ? 'A device with that serial number is already registered.'
          : 'The device could not be registered.',
      );
    } finally {
      this.isSaving.set(false);
    }
  }

  protected dismissApiKey(): void {
    this.revealedApiKey.set(null);
  }

  protected async rotateKey(deviceId: string): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);

    try {
      const registered = await this.api.rotateDeviceApiKey(deviceId);
      this.revealedApiKey.set(registered.apiKey);
    } finally {
      this.isActing.set(false);
    }
  }

  protected async deactivate(deviceId: string): Promise<void> {
    await this.setStatus(deviceId, () => this.api.deactivateDevice(deviceId));
  }

  protected async reactivate(deviceId: string): Promise<void> {
    await this.setStatus(deviceId, () => this.api.reactivateDevice(deviceId));
  }

  private async setStatus(deviceId: string, run: () => Promise<BiometricDevice>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);

    try {
      const updated = await run();
      this.devices.update((devices) => devices.map((d) => (d.id === deviceId ? updated : d)));
    } finally {
      this.isActing.set(false);
    }
  }

  protected async toggleUserMaps(deviceId: string): Promise<void> {
    if (this.expandedDeviceId() === deviceId) {
      this.expandedDeviceId.set(null);
      return;
    }

    this.expandedDeviceId.set(deviceId);
    this.userMapErrorMessage.set(null);
    this.selectedEmployee.set(null);
    this.userMapForm.reset();
    this.userMaps.set(await this.api.listDeviceUserMaps(deviceId));
  }

  protected onEmployeeSelected(employee: EmployeeListItem): void {
    this.selectedEmployee.set(employee);
  }

  protected async submitUserMap(deviceId: string): Promise<void> {
    const employee = this.selectedEmployee();

    if (!employee || this.userMapForm.invalid || this.isSavingUserMap()) {
      this.userMapForm.markAllAsTouched();
      return;
    }

    this.isSavingUserMap.set(true);
    this.userMapErrorMessage.set(null);

    try {
      const map = await this.api.createDeviceUserMap(deviceId, {
        deviceUserCode: this.userMapForm.getRawValue().deviceUserCode,
        employeeId: employee.id,
      });

      this.userMaps.update((maps) => [...maps, map]);
      this.userMapForm.reset();
      this.selectedEmployee.set(null);
    } catch (error) {
      this.userMapErrorMessage.set(
        error instanceof ApiError && error.errorCode === 'DEVICE_USER_MAP_ALREADY_EXISTS'
          ? 'That device user code is already mapped.'
          : 'The mapping could not be saved.',
      );
    } finally {
      this.isSavingUserMap.set(false);
    }
  }
}
