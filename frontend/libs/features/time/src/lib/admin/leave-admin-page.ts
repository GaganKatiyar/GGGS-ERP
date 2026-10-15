import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective, PermissionStore } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { LeaveBalance, LeavePreview, LeaveRequest, LeaveRequestStatus, LeaveType, LedgerEntry, PagedResult } from '@gggs/shared/models';
import { LeaveApi, todayIso } from '@gggs/shared/time';
import { EmptyState, FormDialog, PageHeader, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';

/**
 * Leave for HR (spec §20): every request in reach, applying on someone's behalf, and each person's
 * balances and ledger, with adjustments.
 */
@Component({
  selector: 'gggs-leave-admin-page',
  imports: [DatePipe, DecimalPipe, ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, Tabs, DataTable, DataTableCell, StatusBadge, EmployeeSelect, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Leave" subtitle="Requests, balances and adjustments.">
      <div class="toolbar">
        <a class="btn" routerLink="../policies" *gggsHasPermission="'leave.policy.manage'"><i class="pi pi-book" aria-hidden="true"></i> Types &amp; policies</a>
        <a class="btn" routerLink="../holidays" *gggsHasPermission="'leave.holiday.manage'"><i class="pi pi-sun" aria-hidden="true"></i> Holidays</a>
        <button type="button" class="btn btn--primary" *gggsHasPermission="'leave.request.create-on-behalf'" (click)="openApply()">Apply for someone</button>
      </div>
    </gggs-page-header>

    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    <div class="leave__body">
      @switch (tab()) {
        @case ('requests') {
          <section class="panel toolbar leave__filters">
            <label class="field">
              <span class="field__label">Status</span>
              <select [value]="status()" (change)="setStatus($event)">
                <option value="">Any</option>
                <option value="Pending">Pending</option>
                <option value="Approved">Approved</option>
                <option value="Rejected">Rejected</option>
                <option value="Cancelled">Cancelled</option>
              </select>
            </label>
          </section>
          <gggs-data-table [result]="requests()" [columns]="columns" [loading]="loading()" exportName="leave-requests" emptyTitle="No leave requests" (queryChange)="onQuery($event)">
            <ng-template gggsCell="leaveTypeName" let-row><span class="leave__dot" [style.background]="row.color" aria-hidden="true"></span> {{ row.leaveTypeName }}</ng-template>
            <ng-template gggsCell="startDate" let-row>{{ row.startDate | date: 'd MMM' }} – {{ row.endDate | date: 'd MMM yyyy' }}</ng-template>
            <ng-template gggsCell="days" let-row>{{ row.days | number: '1.0-1' }}</ng-template>
            <ng-template gggsCell="status" let-row>
              <gggs-status-badge [status]="row.status" />
              @if ((row.status === 'Pending' || row.status === 'Approved') && canCancel) {
                <button type="button" class="btn btn--link btn--sm" (click)="cancel(row); $event.stopPropagation()">Cancel</button>
              }
            </ng-template>
          </gggs-data-table>
        }
        @case ('balances') {
          <section class="panel toolbar leave__filters">
            <label class="field leave__picker">
              <span class="field__label">Employee</span>
              <gggs-employee-select ariaLabel="Employee" [formControl]="balanceEmployee" />
            </label>
            @if (balanceEmployee.value) {
              <button type="button" class="btn" *gggsHasPermission="'leave.balance.adjust'" (click)="openAdjust()">Adjust balance</button>
            }
          </section>
          @if (!balanceEmployee.value) {
            <gggs-empty-state icon="pi pi-search" title="Choose someone" description="Search for an employee to see their balances and ledger." />
          } @else {
            <div class="stack">
              <section class="panel">
                <table class="grid">
                  <thead>
                    <tr>
                      <th scope="col">Type</th>
                      <th scope="col">Balance</th>
                      <th scope="col">Pending</th>
                      <th scope="col">Available</th>
                      <th scope="col">Yearly entitlement</th>
                      <th scope="col">Taken this year</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (balance of balances(); track balance.leaveTypeId) {
                      <tr>
                        <td><span class="leave__dot" [style.background]="balance.color" aria-hidden="true"></span> {{ balance.name }}</td>
                        <td>{{ balance.balance | number: '1.0-2' }}</td>
                        <td>{{ balance.pending | number: '1.0-2' }}</td>
                        <td><strong>{{ balance.available | number: '1.0-2' }}</strong></td>
                        <td>{{ balance.annualEntitlement | number: '1.0-2' }}</td>
                        <td>{{ balance.takenThisYear | number: '1.0-2' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </section>
              <section class="panel">
                <h2 class="leave__heading">Ledger</h2>
                <table class="grid">
                  <thead>
                    <tr>
                      <th scope="col">Date</th>
                      <th scope="col">Movement</th>
                      <th scope="col">Days</th>
                      <th scope="col">Note</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (entry of ledger(); track entry.id) {
                      <tr>
                        <td>{{ entry.entryDate | date: 'd MMM yyyy' }}</td>
                        <td>{{ entry.kind }}</td>
                        <td [class.leave__minus]="entry.days < 0">{{ entry.days > 0 ? '+' : '' }}{{ entry.days | number: '1.0-2' }}</td>
                        <td>{{ entry.note ?? '' }}</td>
                      </tr>
                    } @empty {
                      <tr><td colspan="4" class="muted">No movements yet.</td></tr>
                    }
                  </tbody>
                </table>
              </section>
            </div>
          }
        }
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </div>

    <gggs-form-dialog [(visible)]="applyOpen" title="Apply for someone" saveLabel="Apply" [busy]="busy()" [saveDisabled]="!preview() || preview()!.problems.length > 0" [error]="dialogError()" (save)="apply()">
      <div class="stack">
        <div class="form-grid">
          <label class="field form-grid__full">
            <span class="field__label">Employee *</span>
            <gggs-employee-select ariaLabel="Employee" [formControl]="applyEmployee" />
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Type *</span>
            <select [value]="applyType()" (change)="applyType.set($any($event.target).value); refreshPreview()">
              @for (type of types(); track type.id) {
                <option [value]="type.id">{{ type.name }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">From *</span>
            <input type="date" [value]="applyStart()" (change)="applyStart.set($any($event.target).value); refreshPreview()" />
          </label>
          <label class="field">
            <span class="field__label">To *</span>
            <input type="date" [value]="applyEnd()" (change)="applyEnd.set($any($event.target).value); refreshPreview()" />
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Reason</span>
            <input type="text" maxlength="500" [value]="applyReason()" (input)="applyReason.set($any($event.target).value)" />
          </label>
        </div>
        @if (preview(); as p) {
          <div class="leave__preview" role="status">
            <strong>{{ p.days | number: '1.0-1' }} day(s)</strong> · {{ p.available | number: '1.0-1' }} available
            @for (problem of p.problems; track problem) {
              <div class="field__error">{{ problem }}</div>
            }
          </div>
        }
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="adjustOpen" title="Adjust balance" saveLabel="Adjust" [busy]="busy()" [error]="dialogError()" (save)="adjust()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Type *</span>
          <select [value]="adjustType()" (change)="adjustType.set($any($event.target).value)">
            @for (type of types(); track type.id) {
              <option [value]="type.id">{{ type.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Days * <span class="muted">(negative to deduct)</span></span>
          <input type="number" step="0.5" [value]="adjustDays()" (input)="adjustDays.set(+$any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Why *</span>
          <input type="text" maxlength="300" [value]="adjustNote()" (input)="adjustNote.set($any($event.target).value)" placeholder="Compensatory off for weekend release" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .leave__body {
      margin-block-start: var(--gggs-space-3);
    }

    .leave__filters {
      align-items: flex-end;
      margin-block-end: var(--gggs-space-3);
    }

    .leave__picker {
      min-width: 18rem;
    }

    .leave__heading {
      margin: 0 0 var(--gggs-space-2);
      font-size: var(--gggs-font-size-md);
    }

    .leave__dot {
      display: inline-block;
      width: 0.6rem;
      height: 0.6rem;
      border-radius: 50%;
    }

    .leave__minus {
      color: var(--gggs-color-danger);
    }

    .leave__preview {
      padding: var(--gggs-space-3);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-primary-subtle);
    }

    .grid {
      width: 100%;
      border-collapse: collapse;
    }

    .grid th,
    .grid td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }
  `,
})
export class LeaveAdminPage {
  private readonly api = inject(LeaveApi);
  protected readonly canCancel = inject(PermissionStore).has('leave.request.cancel');

  protected readonly tabs: readonly TabItem[] = [
    { key: 'requests', label: 'Requests' },
    { key: 'balances', label: 'Balances' },
  ];
  protected readonly columns: readonly DataTableColumn<LeaveRequest>[] = [
    { field: 'employeeName', header: 'Employee' },
    { field: 'leaveTypeName', header: 'Type' },
    { field: 'startDate', header: 'Dates' },
    { field: 'days', header: 'Days', width: '6rem' },
    { field: 'reason', header: 'Reason' },
    { field: 'status', header: 'Status', width: '12rem' },
  ];

  protected readonly tab = signal('requests');
  protected readonly status = signal<LeaveRequestStatus | ''>('');
  protected readonly requests = signal<PagedResult<LeaveRequest> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly types = signal<readonly LeaveType[]>([]);
  private query: DataTableQuery = { page: 1, pageSize: 25 };

  protected readonly balanceEmployee = new FormControl<string | null>(null);
  protected readonly balances = signal<readonly LeaveBalance[]>([]);
  protected readonly ledger = signal<readonly LedgerEntry[]>([]);

  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly applyOpen = signal(false);
  protected readonly applyEmployee = new FormControl<string | null>(null);
  protected readonly applyType = signal('');
  protected readonly applyStart = signal(todayIso());
  protected readonly applyEnd = signal(todayIso());
  protected readonly applyReason = signal('');
  protected readonly preview = signal<LeavePreview | null>(null);

  protected readonly adjustOpen = signal(false);
  protected readonly adjustType = signal('');
  protected readonly adjustDays = signal(1);
  protected readonly adjustNote = signal('');

  constructor() {
    void this.api.types().then((types) => this.types.set(types));
    this.balanceEmployee.valueChanges.subscribe(() => void this.loadBalances());
    this.applyEmployee.valueChanges.subscribe(() => this.refreshPreview());
  }

  protected onQuery(query: DataTableQuery): void {
    this.query = query;
    void this.loadRequests();
  }

  protected setStatus(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value as LeaveRequestStatus | '');
    this.query = { ...this.query, page: 1 };
    void this.loadRequests();
  }

  protected async cancel(request: LeaveRequest): Promise<void> {
    this.error.set(null);
    try {
      await this.api.cancel(request.id, 'Cancelled by HR');
      await this.loadRequests();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request could not be cancelled.');
    }
  }

  protected openApply(): void {
    this.applyEmployee.setValue(null, { emitEvent: false });
    this.applyType.set(this.types()[0]?.id ?? '');
    this.applyStart.set(todayIso());
    this.applyEnd.set(todayIso());
    this.applyReason.set('');
    this.preview.set(null);
    this.dialogError.set(null);
    this.applyOpen.set(true);
  }

  protected async refreshPreview(): Promise<void> {
    const employeeId = this.applyEmployee.value;
    if (!employeeId || !this.applyType()) {
      this.preview.set(null);
      return;
    }
    try {
      this.preview.set(await this.api.previewOnBehalf(this.application(employeeId)));
      this.dialogError.set(null);
    } catch (error) {
      this.preview.set(null);
      this.dialogError.set(error instanceof ApiError ? error.message : 'Those dates could not be checked.');
    }
  }

  protected async apply(): Promise<void> {
    const employeeId = this.applyEmployee.value;
    if (!employeeId) {
      return;
    }
    await this.inDialog(async () => {
      await this.api.applyOnBehalf(this.application(employeeId));
      this.applyOpen.set(false);
      await this.loadRequests();
    });
  }

  protected openAdjust(): void {
    this.adjustType.set(this.types()[0]?.id ?? '');
    this.adjustDays.set(1);
    this.adjustNote.set('');
    this.dialogError.set(null);
    this.adjustOpen.set(true);
  }

  protected async adjust(): Promise<void> {
    const employeeId = this.balanceEmployee.value;
    if (!employeeId || !this.adjustNote().trim() || !this.adjustDays()) {
      this.dialogError.set('Give the days and the reason.');
      return;
    }
    await this.inDialog(async () => {
      await this.api.adjust({ employeeId, leaveTypeId: this.adjustType(), days: this.adjustDays(), note: this.adjustNote().trim() });
      this.adjustOpen.set(false);
      await this.loadBalances();
    });
  }

  private application(employeeId: string) {
    return {
      employeeId,
      leaveTypeId: this.applyType(),
      startDate: this.applyStart(),
      endDate: this.applyEnd(),
      startsSecondHalf: false,
      endsFirstHalf: false,
      reason: this.applyReason().trim() || null,
    };
  }

  private async inDialog(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await call();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async loadRequests(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.requests.set(await this.api.requests({ status: this.status(), page: this.query.page, pageSize: this.query.pageSize }));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Requests could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadBalances(): Promise<void> {
    const employeeId = this.balanceEmployee.value;
    if (!employeeId) {
      return;
    }
    this.error.set(null);
    try {
      const [balances, ledger] = await Promise.all([this.api.balances(employeeId), this.api.ledger(employeeId)]);
      this.balances.set(balances);
      this.ledger.set(ledger);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Balances could not be loaded.');
    }
  }
}
