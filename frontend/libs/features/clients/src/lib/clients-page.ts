import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { ClientsApi } from '@gggs/shared/delivery';
import type { ClientDetail, ClientItem, ClientStatus, PagedResult } from '@gggs/shared/models';
import { PageHeader, StatusBadge } from '@gggs/shared/ui';
import { ClientForm } from './client-form';

/** Clients (spec §37): who the company works for. */
@Component({
  selector: 'gggs-clients-page',
  imports: [RouterLink, HasPermissionDirective, PageHeader, DataTable, DataTableCell, StatusBadge, ClientForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Clients" subtitle="Who we deliver for, and who to talk to there.">
      <div class="toolbar">
        <a class="btn" routerLink="contacts"><i class="pi pi-address-book" aria-hidden="true"></i> Contacts</a>
        <a class="btn" routerLink="/contracts"><i class="pi pi-file-check" aria-hidden="true"></i> Contracts</a>
        <button type="button" class="btn btn--primary" *gggsHasPermission="'client.client.manage'" (click)="formOpen.set(true)">Add client</button>
      </div>
    </gggs-page-header>

    <section class="panel toolbar clients__filters">
      <label class="field clients__search">
        <span class="field__label">Search</span>
        <input type="search" placeholder="Name, code or city" [value]="search()" (input)="onSearch($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="setStatus($any($event.target).value)">
          <option value="">Any</option>
          <option value="Prospect">Prospect</option>
          <option value="Active">Active</option>
          <option value="Inactive">Inactive</option>
        </select>
      </label>
    </section>

    <gggs-data-table [result]="result()" [columns]="columns" [loading]="loading()" exportName="clients" emptyTitle="No clients yet" (queryChange)="onQuery($event)" (rowClick)="open($event)">
      <ng-template gggsCell="name" let-row><strong>{{ row.name }}</strong> <span class="muted">{{ row.code }}</span></ng-template>
      <ng-template gggsCell="status" let-row><gggs-status-badge [status]="row.status" /></ng-template>
      <ng-template gggsCell="primaryContactName" let-row>
        {{ row.primaryContactName ?? '—' }}
        @if (row.primaryContactEmail) {
          <span class="muted">{{ row.primaryContactEmail }}</span>
        }
      </ng-template>
    </gggs-data-table>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-client-form [(visible)]="formOpen" (saved)="created($event)" />
  `,
  styles: `
    .clients__filters {
      margin-block-end: var(--gggs-space-3);
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .clients__search {
      flex: 1;
      min-width: 14rem;
    }
  `,
})
export class ClientsPage {
  private readonly api = inject(ClientsApi);
  private readonly router = inject(Router);

  protected readonly columns: readonly DataTableColumn<ClientItem>[] = [
    { field: 'name', header: 'Client', sortable: true },
    { field: 'industryName', header: 'Industry' },
    { field: 'city', header: 'City', sortable: true },
    { field: 'primaryContactName', header: 'Primary contact' },
    { field: 'accountManagerName', header: 'Account manager' },
    { field: 'status', header: 'Status', sortable: true, width: '8rem' },
  ];

  protected readonly result = signal<PagedResult<ClientItem> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly status = signal<ClientStatus | ''>('');
  protected readonly formOpen = signal(false);
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

  protected setStatus(value: ClientStatus | ''): void {
    this.status.set(value);
    this.query = { ...this.query, page: 1 };
    void this.load();
  }

  protected open(client: ClientItem): void {
    void this.router.navigate(['/clients', client.id]);
  }

  protected created(detail: ClientDetail): void {
    void this.router.navigate(['/clients', detail.client.id]);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.result.set(await this.api.list({ search: this.search(), status: this.status(), page: this.query.page, pageSize: this.query.pageSize, sort: this.query.sort }));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Clients could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
