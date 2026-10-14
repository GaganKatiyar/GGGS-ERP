import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import type { PagedResult, WorkflowRequest, WorkflowRequestStatus } from '@gggs/shared/models';
import { EmptyState, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { WorkflowApi } from '@gggs/shared/workflow';

/** Requests I raised - or, for reviewers holding the tenant-wide grant, everyone's. */
@Component({
  selector: 'gggs-my-requests-page',
  imports: [RouterLink, PageHeader, DataTable, DataTableCell, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header [title]="scope() === 'all' ? 'All requests' : 'My requests'" subtitle="Where each request stands, and its history.">
      <div class="toolbar">
        @if (seesAll) {
          <select aria-label="Whose requests" [value]="scope()" (change)="setScope($event)">
            <option value="mine">Mine</option>
            <option value="all">Everyone's</option>
          </select>
        }
        <select aria-label="Status" [value]="status()" (change)="setStatus($event)">
          <option value="">Any status</option>
          <option value="Pending">Pending</option>
          <option value="SentBack">Sent back</option>
          <option value="Approved">Approved</option>
          <option value="Rejected">Rejected</option>
          <option value="Withdrawn">Withdrawn</option>
        </select>
        <a class="btn" routerLink="..">Back to inbox</a>
      </div>
    </gggs-page-header>

    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="Requests could not be loaded" [description]="error()!" />
    } @else {
      <gggs-data-table
        [result]="result()"
        [columns]="columns"
        [loading]="loading()"
        exportName="requests"
        emptyTitle="No requests"
        emptyDescription="Requests you raise for approval appear here."
        (queryChange)="onQuery($event)"
        (rowClick)="open($event)"
      >
        <ng-template gggsCell="status" let-row>
          <gggs-status-badge [status]="row.status" />
        </ng-template>
      </gggs-data-table>
    }
  `,
})
export class MyRequestsPage {
  private readonly api = inject(WorkflowApi);
  private readonly router = inject(Router);

  protected readonly seesAll = inject(PermissionStore).scopeFor('workflow.instance.view-all') === 'Tenant';

  protected readonly columns: readonly DataTableColumn<WorkflowRequest>[] = [
    { field: 'title', header: 'Request' },
    { field: 'subjectName', header: 'Kind' },
    { field: 'requesterName', header: 'Raised by' },
    { field: 'currentStageName', header: 'Waiting on' },
    { field: 'submittedDate', header: 'Submitted', format: 'date', width: '9rem' },
    { field: 'status', header: 'Status', format: 'status', width: '9rem' },
  ];

  protected readonly scope = signal<'mine' | 'all'>('mine');
  protected readonly status = signal<WorkflowRequestStatus | ''>('');
  protected readonly result = signal<PagedResult<WorkflowRequest> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  private query: DataTableQuery = { page: 1, pageSize: 25 };

  protected onQuery(query: DataTableQuery): void {
    this.query = query;
    void this.load();
  }

  protected setScope(event: Event): void {
    this.scope.set((event.target as HTMLSelectElement).value === 'all' ? 'all' : 'mine');
    this.query = { ...this.query, page: 1 };
    void this.load();
  }

  protected setStatus(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value as WorkflowRequestStatus | '');
    this.query = { ...this.query, page: 1 };
    void this.load();
  }

  protected open(row: WorkflowRequest): void {
    void this.router.navigate(['/approvals/requests', row.id]);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.result.set(await this.api.requests({ scope: this.scope(), status: this.status(), page: this.query.page, pageSize: this.query.pageSize }));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    } finally {
      this.loading.set(false);
    }
  }
}
