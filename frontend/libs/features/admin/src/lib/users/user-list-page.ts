import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { Button, PageHeader } from '@gggs/shared/ui';
import type { PagedResult, UserListItem, UserStatus } from '@gggs/shared/models';
import { AdminApi } from '@gggs/shared/identity';

/** Everyone who can sign in to this tenant. */
@Component({
  selector: 'gggs-user-list-page',
  imports: [HasPermissionDirective, PageHeader, DataTable, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Users" subtitle="Everyone who can sign in to this tenant.">
      <gggs-button link="/admin/users/invite" *gggsHasPermission="'admin.user.invite'">
        Invite user
      </gggs-button>
    </gggs-page-header>

    <label class="filter">
      <span class="filter__label">Status</span>
      <select [value]="statusFilter()" (change)="onStatusChange($event)">
        <option value="">All</option>
        <option value="Invited">Invited</option>
        <option value="Active">Active</option>
        <option value="Locked">Locked</option>
        <option value="Disabled">Disabled</option>
      </select>
    </label>

    @if (actionError()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ actionError() }}</span></p>
    }

    <gggs-data-table
      [result]="result()"
      [columns]="columns"
      [loading]="isLoading()"
      emptyTitle="No users yet"
      emptyDescription="Invite the first one to see them here."
      (queryChange)="load($event)"
    />

    @if (result(); as result) {
      <table class="table action-table">
        <thead>
          <tr>
            <th scope="col">User</th>
            <th scope="col" *gggsHasPermission="'admin.user.manage'">Roles</th>
            <th scope="col" *gggsHasPermission="'admin.user.lock'">Actions</th>
          </tr>
        </thead>
        <tbody>
          @for (user of result.items; track user.id) {
            <tr>
              <td>{{ user.displayName }} · {{ user.email }}</td>
              <td *gggsHasPermission="'admin.user.manage'">
                <gggs-button
                  variant="secondary"
                  size="sm"
                  [link]="['/admin/users', user.id, 'roles']"
                >
                  Manage roles
                </gggs-button>
              </td>
              <td *gggsHasPermission="'admin.user.lock'">
                @if (user.status === 'Locked') {
                  <gggs-button
                    variant="secondary"
                    size="sm"
                    [disabled]="isActing()"
                    (click)="unlock(user)"
                  >
                    Unlock
                  </gggs-button>
                }
                @if (user.status === 'Disabled') {
                  <gggs-button
                    variant="secondary"
                    size="sm"
                    [disabled]="isActing()"
                    (click)="enable(user)"
                  >
                    Enable
                  </gggs-button>
                }
                @if (user.status === 'Active' || user.status === 'Invited') {
                  <gggs-button
                    variant="danger"
                    size="sm"
                    [disabled]="isActing()"
                    (click)="disable(user)"
                  >
                    Disable
                  </gggs-button>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: `
    .filter {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-2, 0.5rem);
      margin-block-end: var(--gggs-space-4, 1rem);
      font-size: var(--gggs-font-size-base, 0.875rem);
    }

    .filter__label {
      font-weight: var(--gggs-font-weight-semibold, 600);
    }

    .filter select {
      font: inherit;
      color: var(--gggs-color-text);
      background: var(--gggs-color-surface-raised);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-sm, 0.375rem);
      padding: var(--gggs-space-1, 0.25rem) var(--gggs-space-2, 0.5rem);
    }

    .field__error {
      margin: 0 0 var(--gggs-space-4, 1rem);
      color: var(--gggs-color-danger);
      font-size: var(--gggs-font-size-base, 0.875rem);
    }

    /* The data-table's own columns are read-only (see DataTableColumn); actions that need a
       button or a link live in this second, plain table right below it, keyed by the same rows. */
    .action-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-start: -1px;
    }

  `,
})
export class UserListPage {
  private readonly api = inject(AdminApi);
  private lastQuery: DataTableQuery = { page: 1, pageSize: 25 };

  protected readonly isLoading = signal(false);
  protected readonly isActing = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly result = signal<PagedResult<UserListItem> | null>(null);
  protected readonly statusFilter = signal<UserStatus | ''>('');

  protected readonly columns: readonly DataTableColumn<UserListItem>[] = [
    { field: 'displayName', header: 'Name', sortable: true },
    { field: 'email', header: 'Email' },
    { field: 'status', header: 'Status', width: '8rem' },
    { field: 'userType', header: 'Type', width: '8rem' },
    { field: 'lastLoginDate', header: 'Last sign-in', width: '10rem' },
  ];

  constructor() {
    void this.load(this.lastQuery);
  }

  protected onStatusChange(event: Event): void {
    this.statusFilter.set((event.target as HTMLSelectElement).value as UserStatus | '');
    void this.load({ ...this.lastQuery, page: 1 });
  }

  protected async load(query: DataTableQuery): Promise<void> {
    this.lastQuery = query;
    this.isLoading.set(true);

    try {
      this.result.set(
        await this.api.listUsers({
          page: query.page,
          pageSize: query.pageSize,
          status: this.statusFilter() || undefined,
        }),
      );
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async disable(user: UserListItem): Promise<void> {
    await this.runAction(() => this.api.disableUser(user.id));
  }

  protected async enable(user: UserListItem): Promise<void> {
    await this.runAction(() => this.api.enableUser(user.id));
  }

  protected async unlock(user: UserListItem): Promise<void> {
    await this.runAction(() => this.api.unlockUser(user.id));
  }

  private async runAction(action: () => Promise<void>): Promise<void> {
    this.isActing.set(true);
    this.actionError.set(null);

    try {
      await action();
      await this.load(this.lastQuery);
    } catch {
      this.actionError.set(
        'That could not be completed - you may be trying to disable your own account or the last remaining administrator.',
      );
    } finally {
      this.isActing.set(false);
    }
  }
}
