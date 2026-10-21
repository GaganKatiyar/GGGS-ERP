import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { PageHeader } from '@gggs/shared/ui';
import type { AuditLogItem, PagedResult } from '@gggs/shared/models';
import { AuditApi } from './audit-api';

/**
 * The tenant audit trail (`Gggs.Platform.Api.Endpoints.AuditEndpoints`).
 *
 * Read-only, on purpose: nothing on this screen writes anything, because nothing in the Platform
 * API does either - the only writer is a background consumer. Filters here cover the two ways
 * architecture.md §16 describes reading it: "history of this one record" (entity type + id) and
 * "what happened in this window" (service, action, date range).
 */
@Component({
  selector: 'gggs-audit-log-page',
  imports: [ReactiveFormsModule, DataTable, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Audit log" subtitle="Every recorded change to this tenant's data." />

    <form class="filters" [formGroup]="filterForm" (ngSubmit)="applyFilters()">
      <label class="field">
        <span class="field__label">Entity type</span>
        <input type="text" formControlName="entityType" placeholder="e.g. LegalEntity" />
      </label>

      <label class="field">
        <span class="field__label">Entity id</span>
        <input type="text" formControlName="entityId" placeholder="GUID" />
      </label>

      <label class="field">
        <span class="field__label">Action</span>
        <select formControlName="action">
          <option value="">Any</option>
          <option value="Created">Created</option>
          <option value="Modified">Modified</option>
          <option value="Deleted">Deleted</option>
        </select>
      </label>

      <label class="field">
        <span class="field__label">Service</span>
        <input type="text" formControlName="service" placeholder="e.g. corehr" />
      </label>

      <label class="field">
        <span class="field__label">From</span>
        <input type="date" formControlName="fromDate" />
      </label>

      <label class="field">
        <span class="field__label">To</span>
        <input type="date" formControlName="toDate" />
      </label>

      <div class="filters__actions">
        <button type="submit" class="btn btn--primary">Apply</button>
        <button type="button" class="btn" (click)="clearFilters()">Clear</button>
      </div>
    </form>

    <gggs-data-table
      [result]="result()"
      [columns]="columns"
      [loading]="isLoading()"
      emptyTitle="No matching audit entries"
      emptyDescription="Nothing recorded yet for this filter, or try widening the date range."
      (queryChange)="load($event)"
      (rowClick)="selected.set($event)"
    />

    @if (selected(); as entry) {
      <section class="panel">
        <div class="panel__header">
          <h2 class="panel__heading">{{ entry.entityType }} · {{ entry.action }}</h2>
          <button type="button" class="btn" (click)="selected.set(null)">Close</button>
        </div>

        <dl class="facts">
          <div class="facts__row">
            <dt>Occurred</dt>
            <dd>{{ entry.occurredDate }}</dd>
          </div>
          <div class="facts__row">
            <dt>Service</dt>
            <dd>{{ entry.service }}</dd>
          </div>
          <div class="facts__row">
            <dt>Entity id</dt>
            <dd>{{ entry.entityId }}</dd>
          </div>
          @if (entry.changedFieldsCsv) {
            <div class="facts__row">
              <dt>Changed fields</dt>
              <dd>{{ entry.changedFieldsCsv }}</dd>
            </div>
          }
        </dl>

        <div class="diff">
          @if (entry.oldValuesJson) {
            <div>
              <h3>Before</h3>
              <pre>{{ formatJson(entry.oldValuesJson) }}</pre>
            </div>
          }
          @if (entry.newValuesJson) {
            <div>
              <h3>After</h3>
              <pre>{{ formatJson(entry.newValuesJson) }}</pre>
            </div>
          }
        </div>
      </section>
    }
  `,
  styles: `
    .filters {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 0.75rem;
      padding: 1rem;
      margin-block-end: 1rem;
      border: 1px solid var(--gggs-color-border);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised);
    }

    .filters__actions {
      display: flex;
      gap: 0.5rem;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    .panel {
      margin-block-start: 1rem;
      padding: 1rem;
      border: 1px solid var(--gggs-color-border);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised);
    }

    .panel__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-block-end: 0.75rem;
    }

    .panel__heading {
      margin: 0;
      font-size: 1rem;
    }

    .facts {
      display: grid;
      gap: 0.5rem;
      margin: 0 0 1rem;
    }

    .facts__row {
      display: flex;
      gap: 0.5rem;
    }

    .facts__row dt {
      width: 8rem;
      font-weight: 600;
      color: var(--gggs-color-text-muted);
    }

    .facts__row dd {
      margin: 0;
      word-break: break-all;
    }

    .diff {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(16rem, 100%), 1fr));
    }

    .diff h3 {
      margin: 0 0 0.25rem;
      font-size: 0.875rem;
    }

    .diff pre {
      margin: 0;
      padding: 0.5rem;
      overflow-x: auto;
      font-size: 0.75rem;
      background: var(--gggs-color-surface);
      border-radius: 0.375rem;
    }

    .btn {
      display: inline-flex;
      align-items: center;
      padding: 0.5rem 1rem;
      font-weight: 600;
      font-family: inherit;
      font-size: inherit;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border);
      background: var(--gggs-color-surface-raised);
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary);
      border-color: transparent;
      color: var(--gggs-color-primary-contrast);
    }
  `,
})
export class AuditLogPage {
  private readonly api = inject(AuditApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isLoading = signal(false);
  protected readonly result = signal<PagedResult<AuditLogItem> | null>(null);
  protected readonly selected = signal<AuditLogItem | null>(null);

  protected readonly columns: readonly DataTableColumn<AuditLogItem>[] = [
    { field: 'occurredDate', header: 'Occurred', width: '11rem', sortable: true },
    { field: 'service', header: 'Service', width: '10rem' },
    { field: 'action', header: 'Action', width: '8rem' },
    { field: 'entityType', header: 'Entity type', width: '12rem' },
    { field: 'entityId', header: 'Entity id' },
  ];

  protected readonly filterForm = this.formBuilder.nonNullable.group({
    entityType: [''],
    entityId: [''],
    action: [''],
    service: [''],
    fromDate: [''],
    toDate: [''],
  });

  private lastQuery: DataTableQuery = { page: 1, pageSize: 25 };

  constructor() {
    void this.load(this.lastQuery);
  }

  protected async load(query: DataTableQuery): Promise<void> {
    this.lastQuery = query;
    this.isLoading.set(true);
    this.selected.set(null);

    try {
      const filters = this.filterForm.getRawValue();
      this.result.set(
        await this.api.list({
          page: query.page,
          pageSize: query.pageSize,
          entityType: filters.entityType || undefined,
          entityId: filters.entityId || undefined,
          action: filters.action || undefined,
          service: filters.service || undefined,
          fromDate: filters.fromDate || undefined,
          toDate: filters.toDate || undefined,
        }),
      );
    } finally {
      this.isLoading.set(false);
    }
  }

  protected applyFilters(): void {
    void this.load({ ...this.lastQuery, page: 1 });
  }

  protected clearFilters(): void {
    this.filterForm.reset({
      entityType: '',
      entityId: '',
      action: '',
      service: '',
      fromDate: '',
      toDate: '',
    });
    void this.load({ ...this.lastQuery, page: 1 });
  }

  protected formatJson(json: string): string {
    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }
}
