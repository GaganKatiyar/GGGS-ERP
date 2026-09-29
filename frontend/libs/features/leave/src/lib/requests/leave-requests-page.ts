import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, PageHeader } from '@gggs/shared/ui';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import type { LeaveRequestListItem, PagedResult } from '@gggs/shared/models';
import { LeaveApi } from '../leave-api';

/**
 * Every leave request visible to the caller's data scope - `leave.request.view`'s screen,
 * `/leave/requests` in the navigation manifest. Approving happens from the unified approvals
 * inbox (architecture.md §12.1), not here; this is the record-keeping view.
 */
@Component({
  selector: 'gggs-leave-requests-page',
  imports: [HasPermissionDirective, PageHeader, Button, DataTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      title="Leave requests"
      subtitle="Every request visible to you, across every employee you can see."
    />

    <gggs-data-table
      [result]="result()"
      [columns]="columns"
      [loading]="isLoading()"
      emptyTitle="No leave requests"
      emptyDescription="Requests submitted by employees you can see will appear here."
      (queryChange)="load($event)"
      (rowClick)="selectedRequest.set($event)"
    />

    @if (selectedRequest(); as request) {
      <div class="detail-panel">
        <div class="detail-panel__row">
          <span class="detail-panel__label">Request</span>
          <span>{{ request.requestNo }}</span>
        </div>
        <div class="detail-panel__row">
          <span class="detail-panel__label">Status</span>
          <span>{{ request.status }}</span>
        </div>
        @if (request.status === 'Pending' || request.status === 'Approved') {
          <gggs-button
            variant="danger"
            size="sm"
            [loading]="isCancelling()"
            *gggsHasPermission="'leave.request.cancel'"
            (click)="cancel(request)"
          >
            Cancel request
          </gggs-button>
        }
      </div>
    }
  `,
  styles: `
    .detail-panel {
      margin-block-start: 1rem;
      padding: 1rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: var(--gggs-radius-md, 0.5rem);
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      align-items: flex-start;
    }

    .detail-panel__row {
      display: flex;
      gap: 0.5rem;
    }

    .detail-panel__label {
      font-weight: 600;
    }
  `,
})
export class LeaveRequestsPage {
  private readonly api = inject(LeaveApi);

  protected readonly result = signal<PagedResult<LeaveRequestListItem> | null>(null);
  protected readonly isLoading = signal(false);
  protected readonly isCancelling = signal(false);
  protected readonly selectedRequest = signal<LeaveRequestListItem | null>(null);

  protected readonly columns: readonly DataTableColumn<LeaveRequestListItem>[] = [
    { field: 'requestNo', header: 'Request #', width: '10rem' },
    { field: 'fromDate', header: 'From', width: '8rem' },
    { field: 'toDate', header: 'To', width: '8rem' },
    { field: 'totalDays', header: 'Days', width: '6rem', align: 'right' },
    { field: 'status', header: 'Status', width: '8rem' },
  ];

  constructor() {
    void this.load({ page: 1, pageSize: 25 });
  }

  protected async load(query: DataTableQuery): Promise<void> {
    this.isLoading.set(true);

    try {
      this.result.set(
        await this.api.listRequests({ page: query.page, pageSize: query.pageSize }),
      );
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async cancel(request: LeaveRequestListItem): Promise<void> {
    this.isCancelling.set(true);

    try {
      await this.api.cancelRequest(request.id);
      this.selectedRequest.set(null);
      await this.load({ page: this.result()?.page ?? 1, pageSize: this.result()?.pageSize ?? 25 });
    } finally {
      this.isCancelling.set(false);
    }
  }
}
