import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { DataHubApi } from '@gggs/shared/insight';
import type { AccountConnection, DirectoryAccount } from '@gggs/shared/models';
import { EmptyState, Skeleton, StatusBadge } from '@gggs/shared/ui';

/** This person's accounts in outside directories, and making one for someone who joined before a directory was connected. */
@Component({
  selector: 'gggs-accounts-tab',
  imports: [EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (loading()) {
      <gggs-skeleton [lines]="3" />
    } @else {
      <section class="panel stack" aria-label="Work accounts">
        @for (account of accounts(); track account.id) {
          <div class="tab-accounts__row">
            <div class="tab-accounts__who">
              <strong>{{ account.login ?? 'Not created yet' }}</strong>
              <span class="muted">{{ account.connectionName }}{{ account.lastMessage ? ' · ' + account.lastMessage : '' }}</span>
            </div>
            <gggs-status-badge [status]="account.status" />
            @if (canManage && account.status === 'Active') {
              <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" [attr.aria-label]="'Disable ' + account.login" (click)="disable(account)">Disable</button>
            }
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-users" title="No work accounts" description="Accounts made through Studio show up here." />
        }

        @if (canManage) {
          @for (connection of available(); track connection.connectionId) {
            <div>
              <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="create(connection)"><i class="pi pi-plus" aria-hidden="true"></i> Create or link an account in {{ connection.name }}</button>
            </div>
          }
        }
        @if (message()) {
          <p class="muted" role="status">{{ message() }}</p>
        }
      </section>
    }
  `,
  styles: `
    .tab-accounts__row { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); }
    .tab-accounts__who { display: flex; flex: 1 1 16rem; flex-direction: column; min-width: 0; }
  `,
})
export class AccountsTab {
  private readonly api = inject(DataHubApi);

  readonly employeeId = input.required<string>();

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly accounts = signal<readonly DirectoryAccount[]>([]);
  protected readonly available = signal<readonly AccountConnection[]>([]);

  constructor() {
    effect(() => void this.load(this.employeeId()));
  }

  protected async create(connection: AccountConnection): Promise<void> {
    if (!confirm(`Make this person a real account in ${connection.name}? If one already carries their employee code, it is linked instead.`)) {
      return;
    }
    await this.act(() => this.api.requestAccount(this.employeeId(), connection.connectionId), 'Asked. The account appears here within a minute or so.', 'The account could not be requested.');
  }

  protected async disable(account: DirectoryAccount): Promise<void> {
    if (!confirm(`Disable ${account.login}? They are signed out, and it is not deleted.`)) {
      return;
    }
    await this.act(() => this.api.disableAccount(account.id), 'Asked. It is disabled within a minute or so.', 'The account could not be disabled.');
  }

  private async act(work: () => Promise<unknown>, done: string, failure: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await work();
      this.message.set(done);
      await this.load(this.employeeId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : failure);
    } finally {
      this.busy.set(false);
    }
  }

  private async load(employeeId: string): Promise<void> {
    try {
      const [accounts, connections] = await Promise.all([this.api.directoryAccounts(employeeId), this.api.accountConnections()]);
      this.accounts.set(accounts);
      this.available.set(connections.filter((connection) => connection.isActive && !accounts.some((account) => account.connectionId === connection.connectionId)));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Accounts could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
