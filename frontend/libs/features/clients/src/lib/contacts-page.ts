import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { ClientsApi } from '@gggs/shared/delivery';
import type { ContactItem, PagedResult } from '@gggs/shared/models';
import { PageHeader } from '@gggs/shared/ui';

/** Every client contact in one list, for finding the right person fast. */
@Component({
  selector: 'gggs-contacts-page',
  imports: [RouterLink, PageHeader, DataTable, DataTableCell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Client contacts" subtitle="Everyone we talk to at our clients.">
      <a class="btn" routerLink="..">Back to clients</a>
    </gggs-page-header>

    <section class="panel toolbar contacts__filters">
      <label class="field contacts__search">
        <span class="field__label">Search</span>
        <input type="search" placeholder="Name, email or client" [value]="search()" (input)="onSearch($any($event.target).value)" />
      </label>
    </section>

    <gggs-data-table [result]="result()" [columns]="columns" [loading]="loading()" exportName="client-contacts" emptyTitle="No contacts yet" (queryChange)="onQuery($event)" (rowClick)="open($event)">
      <ng-template gggsCell="name" let-row>
        <strong>{{ row.name }}</strong>
        @if (row.isPrimary) {
          <span class="muted">primary</span>
        }
      </ng-template>
      <ng-template gggsCell="email" let-row>
        @if (row.email) {
          <a [href]="'mailto:' + row.email" (click)="$event.stopPropagation()">{{ row.email }}</a>
        }
      </ng-template>
    </gggs-data-table>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .contacts__filters {
      margin-block-end: var(--gggs-space-3);
    }

    .contacts__search {
      flex: 1;
      min-width: 14rem;
    }
  `,
})
export class ContactsPage {
  private readonly api = inject(ClientsApi);
  private readonly router = inject(Router);

  protected readonly columns: readonly DataTableColumn<ContactItem>[] = [
    { field: 'name', header: 'Name' },
    { field: 'designation', header: 'Designation' },
    { field: 'clientName', header: 'Client' },
    { field: 'department', header: 'Department' },
    { field: 'email', header: 'Email' },
    { field: 'phone', header: 'Phone' },
  ];

  protected readonly result = signal<PagedResult<ContactItem> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  private query: DataTableQuery = { page: 1, pageSize: 25 };
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    void this.load();
  }

  protected onQuery(query: DataTableQuery): void {
    this.query = query;
    void this.load();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.query = { ...this.query, page: 1 };
      void this.load();
    }, 300);
  }

  protected open(contact: ContactItem): void {
    void this.router.navigate(['/clients', contact.clientId]);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.result.set(await this.api.contacts({ search: this.search(), page: this.query.page, pageSize: this.query.pageSize }));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Contacts could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
