import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  output,
} from '@angular/core';
import { EmptyState } from '@gggs/shared/ui';
import type { PagedResult } from '@gggs/shared/models';
import { TableModule, type TableLazyLoadEvent } from 'primeng/table';

/** How one column is rendered and whether it can be sorted. */
export interface DataTableColumn<T> {
  /** Property name on the row; also the sort field sent to the server. */
  readonly field: keyof T & string;
  readonly header: string;
  readonly sortable?: boolean;
  readonly width?: string;
  readonly align?: 'left' | 'right' | 'center';
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
 * The list table every screen uses.
 *
 * It wraps PrimeNG rather than exposing it (ADR-10): features bind to this API, so an accessibility
 * fix or a change of component kit happens here instead of across two hundred screens.
 *
 * Paging and sorting are server-side by design. Tenants reach tens of thousands of employees, and
 * a table that fetches everything to sort in the browser stops working long before that.
 */
@Component({
  selector: 'gggs-data-table',
  imports: [TableModule, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
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
      (onLazyLoad)="handleLazyLoad($event)"
    >
      <ng-template #header let-columns>
        <tr>
          @for (column of columns; track column.field) {
            <th
              [style.width]="column.width"
              [style.text-align]="column.align ?? 'left'"
              [pSortableColumn]="column.sortable ? column.field : undefined"
              [attr.scope]="'col'"
            >
              {{ column.header }}
            </th>
          }
        </tr>
      </ng-template>

      <ng-template #body let-row let-columns="columns">
        <tr>
          @for (column of columns; track column.field) {
            <td [style.text-align]="column.align ?? 'left'">
              {{ row[column.field] }}
            </td>
          }
        </tr>
      </ng-template>

      <ng-template #emptymessage let-columns>
        <tr>
          <td [attr.colspan]="columns.length">
            <gggs-empty-state
              [title]="emptyTitle()"
              [description]="emptyDescription()"
            />
          </td>
        </tr>
      </ng-template>
    </p-table>
  `,
})
export class DataTable<T extends { id: string }> {
  readonly result = input<PagedResult<T> | null>(null);
  readonly columns = input.required<readonly DataTableColumn<T>[]>();
  readonly loading = input(false);
  readonly emptyTitle = input('Nothing to show yet');
  readonly emptyDescription = input<string | undefined>(undefined);

  /**
   * Columns the caller has already filtered by permission. Filtering happens in the feature, which
   * knows which permission guards which field.
   */
  readonly visibleColumns = computed(() => this.columns());

  readonly rows = computed(() => this.result()?.items ?? []);
  readonly pageSize = computed(() => this.result()?.pageSize ?? 25);

  /**
   * PrimeNG needs a total to render the pager. When the caller skipped counting, claim one page
   * beyond what we have so "next" stays available rather than the pager vanishing.
   */
  readonly totalRecords = computed(() => {
    const result = this.result();
    if (!result) {
      return 0;
    }

    return (
      result.totalCount ?? result.page * result.pageSize + result.items.length
    );
  });

  readonly queryChange = output<DataTableQuery>();

  protected handleLazyLoad(event: TableLazyLoadEvent): void {
    const pageSize = event.rows ?? this.pageSize();
    const page = Math.floor((event.first ?? 0) / pageSize) + 1;

    this.queryChange.emit({
      page,
      pageSize,
      sort: DataTable.toSortExpression(event),
    });
  }

  /** PrimeNG's field plus direction becomes the API's `-field` convention. */
  private static toSortExpression(
    event: TableLazyLoadEvent,
  ): string | undefined {
    if (typeof event.sortField !== 'string' || event.sortField.length === 0) {
      return undefined;
    }

    return event.sortOrder === -1 ? `-${event.sortField}` : event.sortField;
  }
}
