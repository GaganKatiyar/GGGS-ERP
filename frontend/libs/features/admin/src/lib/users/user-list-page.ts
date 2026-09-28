import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { PageHeader } from '@gggs/shared/ui';
import type { PagedResult, UserListItem, UserStatus } from '@gggs/shared/models';
import { AdminApi } from '@gggs/shared/identity';

/** Everyone who can sign in to this tenant. */
@Component({
  selector: 'gggs-user-list-page',
  imports: [RouterLink, HasPermissionDirective, PageHeader, DataTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Users" subtitle="Everyone who can sign in to this tenant.">
      <a routerLink="/admin/users/invite" class="btn btn--primary" *gggsHasPermission="'admin.user.invite'">
        Invite user
      </a>
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
      <p class="field__error" role="alert">{{ actionError() }}</p>
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
      <table class="action-table">
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
                <a [routerLink]="['/admin/users', user.id, 'roles']" class="btn">Manage roles</a>
              </td>
              <td *gggsHasPermission="'admin.user.lock'">
                @if (user.status === 'Locked') {
                  <button type="button" class="btn" [disabled]="isActing()" (click)="unlock(user)">
                    Unlock
                  </button>
                }
                @if (user.status === 'Disabled') {
                  <button type="button" class="btn" [disabled]="isActing()" (click)="enable(user)">
                    Enable
                  </button>
                }
                @if (user.status === 'Active' || user.status === 'Invited') {
                  <button type="button" class="btn btn--danger" [disabled]="isActing()" (click)="disable(user)">
                    Disable
                  </button>
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
      gap: 0.5rem;
      margin-block-end: 1rem;
      font-size: 0.875rem;
    }

    .filter__label {
      font-weight: 600;
    }

    .field__error {
      margin: 0 0 1rem;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    /* The data-table's own columns are read-only (see DataTableColumn); actions that need a
       button or a link live in this second, plain table right below it, keyed by the same rows. */
    .action-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-start: -1px;
    }

    .action-table th,
    .action-table td {
      padding: 0.5rem 0.75rem;
      text-align: left;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
    }

    .btn {
      display: inline-flex;
      align-items: center;
      padding: 0.375rem 0.75rem;
      font-weight: 600;
      font-family: inherit;
      font-size: 0.8125rem;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #2f5fd6);
      border-color: transparent;
      color: #fff;
    }

    .btn--danger {
      border-color: var(--gggs-color-danger, #b3261e);
      color: var(--gggs-color-danger, #b3261e);
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
