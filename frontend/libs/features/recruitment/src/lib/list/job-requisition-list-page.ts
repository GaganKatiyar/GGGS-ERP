import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { Button, PageHeader } from '@gggs/shared/ui';
import type { JobRequisitionListItem, PagedResult } from '@gggs/shared/models';
import { RecruitmentApi } from '../data/recruitment-api';

/** Job requisitions: every open (and past) request to hire this tenant has raised. */
@Component({
  selector: 'gggs-job-requisition-list-page',
  imports: [HasPermissionDirective, PageHeader, DataTable, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Job requisitions" subtitle="Requests to hire, from draft through to close.">
      <gggs-button link="new" *gggsHasPermission="'recruitment.requisition.manage'">
        New requisition
      </gggs-button>
    </gggs-page-header>

    <gggs-data-table
      [result]="result()"
      [columns]="columns"
      [loading]="isLoading()"
      emptyTitle="No requisitions yet"
      emptyDescription="Raise the first one to see it here."
      (queryChange)="load($event)"
      (rowClick)="openRequisition($event)"
    />
  `,
})
export class JobRequisitionListPage {
  private readonly api = inject(RecruitmentApi);
  private readonly router = inject(Router);

  protected readonly isLoading = signal(false);
  protected readonly result = signal<PagedResult<JobRequisitionListItem> | null>(null);

  protected readonly columns: readonly DataTableColumn<JobRequisitionListItem>[] = [
    { field: 'requisitionNo', header: 'No.', width: '12rem' },
    { field: 'title', header: 'Title', sortable: true },
    { field: 'openings', header: 'Openings', width: '8rem' },
    { field: 'status', header: 'Status', width: '10rem' },
  ];

  constructor() {
    void this.load({ page: 1, pageSize: 25 });
  }

  protected async load(query: DataTableQuery): Promise<void> {
    this.isLoading.set(true);

    try {
      this.result.set(await this.api.list({ page: query.page, pageSize: query.pageSize }));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected openRequisition(requisition: JobRequisitionListItem): void {
    void this.router.navigate(['/recruitment/requisitions', requisition.id]);
  }
}
