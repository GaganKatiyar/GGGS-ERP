import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { DataHubApi } from '@gggs/shared/insight';
import type { AccountConnection, DirectoryAccount } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { ACCOUNT_STATUS_VARIANTS, accountsNeedingAttention, accountStatusLabel } from './studio-model';

/** Accounts in outside directories: what each connection does by itself, and how every account is doing. */
@Component({
  selector: 'gggs-accounts-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Accounts" subtitle="Create work accounts for joiners and shut them off for leavers. Nuts and Bolts disables accounts and never deletes them." />

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else {
      @if (attention().length) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-triangle" aria-hidden="true"></i><span>{{ attention().length }} account{{ attention().length === 1 ? ' has' : 's have' }} stopped trying and need{{ attention().length === 1 ? 's' : '' }} you. An account that could not be disabled is still open.</span></p>
      }

      <section class="panel stack" aria-labelledby="directories-heading">
        <h2 id="directories-heading" class="accounts__heading">Directories</h2>
        @for (connection of connections(); track connection.connectionId) {
          <div class="accounts__connection">
            <div class="accounts__who">
              <strong>{{ connection.name }}</strong>
              <span class="muted">{{ connection.adapterName }}{{ connection.isActive ? '' : ' - switched off, so nothing happens' }}</span>
            </div>
            <label class="accounts__switch">
              <input type="checkbox" role="switch" class="switch" [checked]="connection.provisionOnHire" [disabled]="!canManage || busy()" (change)="set(connection, { provisionOnHire: $any($event.target).checked })" />
              Create on hire
            </label>
            <label class="accounts__switch">
              <input type="checkbox" role="switch" class="switch" [checked]="connection.disableOnExit" [disabled]="!canManage || busy()" (change)="set(connection, { disableOnExit: $any($event.target).checked })" />
              Disable on exit
            </label>
          </div>
        } @empty {
          <p class="muted">Connect Microsoft 365 accounts or Google Workspace accounts under <a routerLink="../connectors">Connectors</a> first.</p>
        }
        @if (connections().length) {
          <p class="muted accounts__note">Both start off. Disabling covers only accounts listed below; someone who already had an account appears here once one is created for them from their Employee 360 page, which links the existing account if it carries their employee code.</p>
        }
      </section>

      @if (accounts().length === 0) {
        <gggs-empty-state icon="pi pi-users" title="No accounts yet" description="Accounts appear here once a directory creates them." />
      } @else {
        <div class="panel panel--flush table-wrap" tabindex="0" role="region" aria-label="Accounts">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Person</th>
                <th scope="col">Account</th>
                <th scope="col">Status</th>
                <th scope="col">Result</th>
                <th scope="col"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (account of accounts(); track account.id) {
                <tr>
                  <td><a [routerLink]="['/employees', account.employeeId]">{{ account.employeeName }}</a><span class="accounts__meta">{{ account.employeeCode }}</span></td>
                  <td>{{ account.login ?? '—' }}<span class="accounts__meta">{{ account.connectionName }}</span></td>
                  <td><gggs-status-badge [status]="statusLabel(account)" [variants]="variants" /></td>
                  <td>
                    {{ account.lastMessage ?? '—' }}
                    @if (account.nextAttemptAt) {
                      <span class="accounts__meta">Next try {{ account.nextAttemptAt | date: 'HH:mm' }}</span>
                    }
                  </td>
                  <td class="accounts__actions">
                    @if (canManage && (account.status === 'Creating' || account.status === 'Disabling')) {
                      <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" [attr.aria-label]="'Retry the account of ' + account.employeeName" (click)="retry(account)">Retry</button>
                    }
                    @if (canManage && account.status === 'Active') {
                      <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" [attr.aria-label]="'Disable the account of ' + account.employeeName" (click)="disable(account)">Disable</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .accounts__heading { margin: 0; font-size: var(--gggs-font-size-md); }
    .accounts__connection { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-4); padding-block: var(--gggs-space-2); border-bottom: 1px solid var(--gggs-color-border); }
    .accounts__who { display: flex; flex: 1 1 16rem; flex-direction: column; min-width: 0; }
    .accounts__switch { display: inline-flex; align-items: center; gap: var(--gggs-space-2); }
    .accounts__note { margin: 0; font-size: var(--gggs-font-size-sm); }
    .accounts__meta { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .accounts__actions { white-space: nowrap; }
  `,
})
export class AccountsPage {
  private readonly api = inject(DataHubApi);

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly variants = ACCOUNT_STATUS_VARIANTS;
  protected readonly statusLabel = accountStatusLabel;
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly connections = signal<readonly AccountConnection[]>([]);
  protected readonly accounts = signal<readonly DirectoryAccount[]>([]);
  protected readonly attention = computed(() => accountsNeedingAttention(this.accounts()));

  constructor() {
    void this.load();
  }

  protected async set(connection: AccountConnection, change: Partial<Pick<AccountConnection, 'provisionOnHire' | 'disableOnExit'>>): Promise<void> {
    const wanted = { ...connection, ...change };
    if (change.provisionOnHire && !confirm(`Create a real ${connection.adapterName} account for everyone hired from now on?`)) {
      await this.load();
      return;
    }
    await this.act(async () => this.connections.set(await this.api.setAccountConnection(connection.connectionId, wanted.provisionOnHire, wanted.disableOnExit)), 'The choice could not be saved.');
  }

  protected async disable(account: DirectoryAccount): Promise<void> {
    if (!confirm(`Disable ${account.login} for ${account.employeeName}? They are signed out, and it is not deleted.`)) {
      return;
    }
    await this.act(async () => {
      await this.api.disableAccount(account.id);
      await this.load();
    }, 'The account could not be disabled.');
  }

  protected async retry(account: DirectoryAccount): Promise<void> {
    await this.act(async () => {
      await this.api.retryAccount(account.id);
      await this.load();
    }, 'The account could not be retried.');
  }

  private async act(work: () => Promise<void>, failure: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : failure);
      await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [connections, accounts] = await Promise.all([this.api.accountConnections(), this.api.directoryAccounts()]);
      this.connections.set(connections);
      this.accounts.set(accounts);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Accounts could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
