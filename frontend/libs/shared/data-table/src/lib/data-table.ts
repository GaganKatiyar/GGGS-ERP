import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  TemplateRef,
  computed,
  contentChildren,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { EmptyState, StatusBadge } from '@gggs/shared/ui';
import type { PagedResult } from '@gggs/shared/models';
import { TableModule, type TableLazyLoadEvent } from 'primeng/table';

/** How a cell's value is shown when no custom template is given. */
export type DataTableFormat = 'text' | 'date' | 'datetime' | 'number' | 'status' | 'boolean';

const ISO_DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/;

/** How one column is rendered and whether it can be sorted, hidden or exported. */
export interface DataTableColumn<T> {
  /** Property name on the row; also the sort field sent to the server. */
  readonly field: keyof T & string;
  readonly header: string;
  readonly sortable?: boolean;
  readonly width?: string;
  readonly align?: 'left' | 'right' | 'center';
  readonly format?: DataTableFormat;
  /** Starts hidden; the user can turn it on from the column chooser. */
  readonly hidden?: boolean;
  /**
   * Permission required to see this column. Salary and bank columns are hidden without it, and the
   * API omits the values too - this only keeps the UI from showing an empty column.
   */
  readonly permission?: string;
}

/** What the table asks the server for when paging or sorting changes. */
export interface DataTableQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly sort?: string;
}

/**
 * A custom cell for one column:
 * `<ng-template gggsCell="status" let-row>…</ng-template>` inside `<gggs-data-table>`.
 */
@Directive({ selector: 'ng-template[gggsCell]' })
export class DataTableCell {
  readonly gggsCell = input.required<string>();
  readonly template = inject<TemplateRef<{ $implicit: unknown }>>(TemplateRef);
}

/**
 * The list table every screen uses (spec §118): server-side paging and sorting, a column chooser,
 * CSV export, and typed or templated cells.
 *
 * It wraps PrimeNG rather than exposing it (ADR-10): features bind to this API, so an accessibility
 * fix or a change of component kit happens here instead of across two hundred screens.
 *
 * Paging and sorting are server-side by design. Tenants reach tens of thousands of employees, and
 * a table that fetches everything to sort in the browser stops working long before that.
 */
@Component({
  selector: 'gggs-data-table',
  imports: [TableModule, EmptyState, StatusBadge, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="data-table__tools toolbar">
      <ng-content select="[tableToolbar]" />
      <span class="data-table__spacer"></span>

      <div class="data-table__chooser">
        <button
          type="button"
          class="btn btn--sm"
          [attr.aria-expanded]="isChooserOpen()"
          (click)="isChooserOpen.set(!isChooserOpen())"
        >
          <i class="pi pi-table" aria-hidden="true"></i> Columns
        </button>
        @if (isChooserOpen()) {
          <div class="data-table__chooser-panel" role="group" aria-label="Visible columns">
            @for (column of columns(); track column.field) {
              <label>
                <input type="checkbox" [checked]="!hiddenFields().has(column.field)" (change)="toggle(column.field)" />
                {{ column.header }}
              </label>
            }
          </div>
        }
      </div>

      <button type="button" class="btn btn--sm" [disabled]="rows().length === 0 || isExporting()" (click)="exportCsv()">
        <i class="pi pi-download" aria-hidden="true"></i> {{ isExporting() ? 'Exporting…' : 'Export' }}
      </button>
    </div>

    <div class="data-table__frame">
    <p-table
      [value]="rows()"
      [columns]="visibleColumns()"
      [lazy]="true"
      [paginator]="true"
      [rows]="pageSize()"
      [totalRecords]="totalRecords()"
      [loading]="loading()"
      [rowsPerPageOptions]="[25, 50, 100]"
      [showCurrentPageReport]="true"
      currentPageReportTemplate="{first}-{last} of {totalRecords}"
      dataKey="id"
      styleClass="p-datatable-sm"
      [tableStyle]="{ 'min-width': '40rem' }"
      [scrollable]="true"
      (onLazyLoad)="handleLazyLoad($event)"
    >
      <ng-template #header let-columns>
        <tr>
          @for (column of columns; track column.field) {
            <th
              [style.width]="column.width"
              [style.text-align]="alignOf(column)"
              [pSortableColumn]="column.sortable ? column.field : undefined"
              [attr.scope]="'col'"
            >
              {{ column.header }}
              @if (column.sortable) {
                <p-sortIcon [field]="column.field" />
              }
            </th>
          }
        </tr>
      </ng-template>

      <ng-template #body let-row let-columns="columns">
        <tr (click)="rowClick.emit(row)">
          @for (column of columns; track column.field) {
            <td [style.text-align]="alignOf(column)">
              @if (cellTemplates().get(column.field); as template) {
                <ng-container *ngTemplateOutlet="template; context: { $implicit: row }" />
              } @else if (column.format === 'status' && row[column.field]) {
                <gggs-status-badge [status]="row[column.field]" />
              } @else {
                {{ display(row, column) }}
              }
            </td>
          }
        </tr>
      </ng-template>

      <ng-template #emptymessage let-columns>
        <tr>
          <td [attr.colspan]="columns.length">
            <gggs-empty-state [title]="emptyTitle()" [description]="emptyDescription()" />
          </td>
        </tr>
      </ng-template>
    </p-table>
    </div>
  `,
  styles: `
    .data-table__tools {
      margin-block-end: var(--gggs-space-2);
    }

    .data-table__spacer {
      flex: 1;
    }

    .data-table__chooser {
      position: relative;
    }

    .data-table__chooser-panel {
      position: absolute;
      z-index: 10;
      inset-inline-end: 0;
      top: calc(100% + var(--gggs-space-1));
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      min-width: 12rem;
      padding: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-sm);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-md);
      font-size: var(--gggs-font-size-sm);
    }

    .data-table__frame {
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-xs);
      overflow: hidden;
    }

    :host ::ng-deep .p-datatable-thead > tr > th {
      background: var(--gggs-color-surface-sunken);
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-xs);
      font-weight: var(--gggs-font-weight-semibold);
      letter-spacing: 0.04em;
      text-transform: uppercase;
      white-space: nowrap;
    }

    :host ::ng-deep .p-datatable-tbody > tr > td {
      font-variant-numeric: tabular-nums;
    }

    /* Harmless when nobody listens to (rowClick) - just an inert hover on an inert row. */
    :host ::ng-deep .p-datatable-tbody > tr {
      cursor: pointer;
    }

    :host ::ng-deep .p-datatable-tbody > tr:hover {
      background: var(--gggs-color-surface-hover);
    }

    :host ::ng-deep .p-paginator {
      border-block-start: 1px solid var(--gggs-color-border);
    }
  `,
})
export class DataTable<T extends { id: string }> {
  readonly result = input<PagedResult<T> | null>(null);
  readonly columns = input.required<readonly DataTableColumn<T>[]>();
  readonly loading = input(false);
  readonly emptyTitle = input('Nothing to show yet');
  readonly emptyDescription = input<string | undefined>(undefined);
  /** File name for exports, without the extension. */
  readonly exportName = input('export');
  /**
   * Fetches every row matching the current filters, for "export all". Without it, Export writes the
   * rows on screen - the honest fallback when the page has no cheap way to fetch everything.
   */
  readonly fetchAll = input<(() => Promise<readonly T[]>) | undefined>(undefined);

  readonly queryChange = output<DataTableQuery>();

  /** Emits the row's own data when a row is clicked. A caller with no detail screen can ignore it. */
  readonly rowClick = output<T>();

  private readonly cells = contentChildren(DataTableCell);

  protected readonly isChooserOpen = signal(false);
  protected readonly isExporting = signal(false);
  private readonly userHidden = signal<ReadonlySet<string> | null>(null);

  protected readonly hiddenFields = computed<ReadonlySet<string>>(
    () => this.userHidden() ?? new Set(this.columns().filter((column) => column.hidden).map((column) => column.field)),
  );

  // PrimeNG's own [value]/[columns] inputs are typed as mutable arrays; .slice() copies rather
  // than casts, so a caller's readonly array can never actually be mutated out from under it.
  readonly visibleColumns = computed(() => this.columns().filter((column) => !this.hiddenFields().has(column.field)));

  readonly rows = computed(() => (this.result()?.items ?? []).slice());
  readonly pageSize = computed(() => this.result()?.pageSize ?? 25);

  protected readonly cellTemplates = computed(
    () => new Map(this.cells().map((cell) => [cell.gggsCell(), cell.template] as const)),
  );

  /**
   * PrimeNG needs a total to render the pager. When the caller skipped counting, claim one page
   * beyond what we have so "next" stays available rather than the pager vanishing.
   */
  readonly totalRecords = computed(() => {
    const result = this.result();
    if (!result) {
      return 0;
    }

    return result.totalCount ?? result.page * result.pageSize + result.items.length;
  });

  protected toggle(field: string): void {
    const next = new Set(this.hiddenFields());
    if (next.has(field)) {
      next.delete(field);
    } else if (this.visibleColumns().length > 1) {
      next.add(field);
    }
    this.userHidden.set(next);
  }

  /** Numbers line up on the right unless the column says otherwise. */
  protected alignOf(column: DataTableColumn<T>): string {
    return column.align ?? (column.format === 'number' ? 'right' : 'left');
  }

  protected display(row: T, column: DataTableColumn<T>): string {
    return DataTable.format(row[column.field], column.format);
  }

  /** CSV of the visible columns, quoted so commas and quotes inside values survive Excel. */
  protected async exportCsv(): Promise<void> {
    this.isExporting.set(true);

    try {
      const fetchAll = this.fetchAll();
      const rows = fetchAll ? await fetchAll() : this.rows();
      const columns = this.visibleColumns();
      const quote = (value: string) => `"${value.replace(/"/g, '""')}"`;

      const lines = [
        columns.map((column) => quote(column.header)).join(','),
        ...rows.map((row) => columns.map((column) => quote(DataTable.format(row[column.field], column.format))).join(',')),
      ];

      const blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${this.exportName()}.csv`;
      link.click();
      URL.revokeObjectURL(url);
    } finally {
      this.isExporting.set(false);
    }
  }

  protected handleLazyLoad(event: TableLazyLoadEvent): void {
    const pageSize = event.rows ?? this.pageSize();
    const page = Math.floor((event.first ?? 0) / pageSize) + 1;

    this.queryChange.emit({ page, pageSize, sort: DataTable.toSortExpression(event) });
  }

  static format(value: unknown, format: DataTableFormat | undefined): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }

    switch (format) {
      case 'date':
        return new Date(String(value)).toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
      case 'datetime':
        return DataTable.dateTime(String(value));
      case 'number':
        return Number(value).toLocaleString();
      case 'boolean':
        return value ? 'Yes' : 'No';
      case 'status':
        return String(value).replace(/([a-z])([A-Z])/g, '$1 $2');
      default:
        // An untyped column holding a timestamp still reads as a date and time, never as ISO text.
        return typeof value === 'string' && ISO_DATE_TIME.test(value) ? DataTable.dateTime(value) : String(value);
    }
  }

  /** Server timestamps are UTC; one without an offset is read as UTC and shown in local time. */
  private static dateTime(value: string): string {
    const utc = /[zZ]|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`;
    return new Date(utc).toLocaleString(undefined, { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  /** PrimeNG's field plus direction becomes the API's `-field` convention. */
  private static toSortExpression(event: TableLazyLoadEvent): string | undefined {
    if (typeof event.sortField !== 'string' || event.sortField.length === 0) {
      return undefined;
    }

    return event.sortOrder === -1 ? `-${event.sortField}` : event.sortField;
  }
}
