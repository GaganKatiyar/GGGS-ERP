import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import type {
  LeaveBalance,
  LeaveDaySession,
  LeaveRequestListItem,
  LeaveTypeListItem,
  PagedResult,
} from '@gggs/shared/models';
import { LeaveApi } from '../leave-api';

/**
 * Self-service leave: balances by type, a balance-aware apply form, and the employee's own
 * request history with cancel - architecture.md §12.1's Employee dashboard leave widgets, given a
 * full page of their own. `/me/leave` in the navigation manifest.
 */
@Component({
  selector: 'gggs-my-leave-page',
  imports: [ReactiveFormsModule, PageHeader, Card, Button, FormField, DataTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My leave" subtitle="Balances, requests, and applying for time off." />

    <gggs-card title="Balances" class="section">
      @if (balances().length === 0) {
        <p class="muted">No leave balances yet.</p>
      } @else {
        <div class="balance-grid">
          @for (balance of balances(); track balance.leaveTypeId) {
            <div class="balance-card">
              <span class="balance-card__type">{{ leaveTypeName(balance.leaveTypeId) }}</span>
              <span class="balance-card__available">{{ balance.available }}</span>
              <span class="balance-card__label">days available</span>
              @if (balance.pendingApproval > 0) {
                <span class="balance-card__pending">{{ balance.pendingApproval }} pending</span>
              }
            </div>
          }
        </div>
      }
    </gggs-card>

    <gggs-card title="Apply for leave" class="section">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="apply-form">
        <gggs-form-field label="Leave type" required>
          <select formControlName="leaveTypeId" required>
            <option value="" disabled>Select a leave type</option>
            @for (type of leaveTypes(); track type.id) {
              <option [value]="type.id">{{ type.name }}</option>
            }
          </select>
        </gggs-form-field>

        <gggs-form-field label="From date" required>
          <input type="date" formControlName="fromDate" required />
        </gggs-form-field>

        <gggs-form-field label="From session">
          <select formControlName="fromSession">
            <option value="Full">Full day</option>
            <option value="First">First half</option>
            <option value="Second">Second half</option>
          </select>
        </gggs-form-field>

        <gggs-form-field label="To date" required>
          <input type="date" formControlName="toDate" required />
        </gggs-form-field>

        <gggs-form-field label="To session">
          <select formControlName="toSession">
            <option value="Full">Full day</option>
            <option value="First">First half</option>
            <option value="Second">Second half</option>
          </select>
        </gggs-form-field>

        <gggs-form-field label="Reason" class="apply-form__reason">
          <textarea formControlName="reason" maxlength="1000" rows="2"></textarea>
        </gggs-form-field>

        @if (errorMessage()) {
          <p class="field-error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" [loading]="isSubmitting()">Submit request</gggs-button>
      </form>
    </gggs-card>

    <gggs-card title="My requests">
      <gggs-data-table
        [result]="requests()"
        [columns]="columns"
        [loading]="isLoadingRequests()"
        emptyTitle="No leave requests yet"
        emptyDescription="Requests you submit will show up here."
        (queryChange)="loadRequests($event)"
      />
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

    .balance-grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
    }

    .balance-card {
      display: flex;
      flex-direction: column;
      padding: 0.75rem 1rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: var(--gggs-radius-md, 0.5rem);
    }

    .balance-card__type {
      font-weight: 600;
      font-size: 0.875rem;
    }

    .balance-card__available {
      font-size: 1.5rem;
      font-weight: 700;
      line-height: 1.4;
    }

    .balance-card__label {
      font-size: 0.75rem;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .balance-card__pending {
      margin-top: 0.25rem;
      font-size: 0.75rem;
      color: var(--gggs-color-warning, #9a6700);
    }

    .apply-form {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      align-items: end;
    }

    .apply-form__reason {
      grid-column: 1 / -1;
    }

    .field-error {
      grid-column: 1 / -1;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }
  `,
})
export class MyLeavePage {
  private readonly api = inject(LeaveApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly balances = signal<readonly LeaveBalance[]>([]);
  protected readonly leaveTypes = signal<readonly LeaveTypeListItem[]>([]);
  protected readonly requests = signal<PagedResult<LeaveRequestListItem> | null>(null);
  protected readonly isLoadingRequests = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly columns: readonly DataTableColumn<LeaveRequestListItem>[] = [
    { field: 'requestNo', header: 'Request #', width: '10rem' },
    { field: 'fromDate', header: 'From', width: '8rem' },
    { field: 'toDate', header: 'To', width: '8rem' },
    { field: 'totalDays', header: 'Days', width: '6rem', align: 'right' },
    { field: 'status', header: 'Status', width: '8rem' },
  ];

  protected readonly form = this.formBuilder.nonNullable.group({
    leaveTypeId: ['', Validators.required],
    fromDate: ['', Validators.required],
    fromSession: ['Full' as LeaveDaySession],
    toDate: ['', Validators.required],
    toSession: ['Full' as LeaveDaySession],
    reason: [''],
  });

  constructor() {
    void this.loadBalances();
    void this.loadLeaveTypes();
    void this.loadRequests({ page: 1, pageSize: 10 });
  }

  protected leaveTypeName(leaveTypeId: string): string {
    return (
      this.leaveTypes().find((type) => type.id === leaveTypeId)?.name ?? 'Unknown'
    );
  }

  private async loadBalances(): Promise<void> {
    this.balances.set(await this.api.getMyBalances());
  }

  private async loadLeaveTypes(): Promise<void> {
    this.leaveTypes.set(await this.api.listLeaveTypes());
  }

  protected async loadRequests(query: DataTableQuery): Promise<void> {
    this.isLoadingRequests.set(true);

    try {
      this.requests.set(
        await this.api.listRequests({ page: query.page, pageSize: query.pageSize }),
      );
    } finally {
      this.isLoadingRequests.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const { leaveTypeId, fromDate, fromSession, toDate, toSession, reason } =
      this.form.getRawValue();

    try {
      await this.api.submitRequest({
        leaveTypeId,
        fromDate,
        fromSession,
        toDate,
        toSession,
        reason: reason || undefined,
      });

      this.form.reset({ fromSession: 'Full', toSession: 'Full' });
      await this.loadBalances();
      await this.loadRequests({ page: 1, pageSize: this.requests()?.pageSize ?? 10 });
    } catch (error) {
      this.errorMessage.set(describeSubmitError(error));
    } finally {
      this.isSubmitting.set(false);
    }
  }
}

function describeSubmitError(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.errorCode) {
      case 'LEAVE_INSUFFICIENT_BALANCE':
        return 'You do not have enough balance for this request.';
      case 'LEAVE_OVERLAPS_EXISTING_REQUEST':
        return 'This date range overlaps an existing leave request.';
      case 'LEAVE_INSUFFICIENT_NOTICE':
        return 'This leave type requires more advance notice.';
      case 'LEAVE_HALF_DAY_NOT_ALLOWED':
        return 'This leave type does not allow half-day requests.';
      case 'LEAVE_NOT_ELIGIBLE':
        return 'You are not yet eligible for this leave type.';
      case 'LEAVE_NO_POLICY_CONFIGURED':
        return 'No leave policy is configured for this leave type.';
      default:
        return error.message;
    }
  }

  return 'The leave request could not be submitted. Please try again.';
}
