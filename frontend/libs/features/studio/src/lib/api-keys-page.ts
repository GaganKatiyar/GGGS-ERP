import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { AdminApi } from '@gggs/shared/identity';
import type { ApiKeyItem, Role } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton } from '@gggs/shared/ui';
import { EXPIRY_CHOICES } from './studio-model';

/**
 * API keys: for outside systems that call Nuts and Bolts. Each key acts as a machine account holding the
 * roles chosen for it - it can do what they allow and nothing more - and is shown in full once.
 */
@Component({
  selector: 'gggs-api-keys-page',
  imports: [DatePipe, PageHeader, EmptyState, FormDialog, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="API keys" subtitle="Let your own systems call Nuts and Bolts. A key can do what its roles allow, and nothing more.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openCreate()"><i class="pi pi-plus" aria-hidden="true"></i> New key</button>
      }
    </gggs-page-header>

    @if (created(); as value) {
      <section class="panel stack keys__reveal" role="status" aria-labelledby="reveal-heading">
        <h2 id="reveal-heading" class="keys__heading"><i class="pi pi-key" aria-hidden="true"></i> Copy your new key now</h2>
        <p class="muted">This is the only time it is shown. Store it in your system's secret store; if it is lost, revoke it and make another.</p>
        <div class="keys__value">
          <code>{{ value }}</code>
          <button type="button" class="btn btn--sm" (click)="copy(value)">{{ copied() ? 'Copied' : 'Copy' }}</button>
        </div>
        <p class="muted">Send it in the <code>X-Api-Key</code> header: <code>curl -H "X-Api-Key: {{ value.slice(0, 18) }}…" {{ origin }}/api/v1/employees</code></p>
        <div><button type="button" class="btn btn--link btn--sm" (click)="created.set(null)">I have stored it</button></div>
      </section>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="5" />
    } @else if (keys().length === 0) {
      <gggs-empty-state icon="pi pi-key" title="No API keys yet" description="Make one for each system that calls Nuts and Bolts, with only the roles it needs." />
    } @else {
      <div class="panel panel--flush table-wrap" tabindex="0" role="region" aria-label="API keys">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Key</th>
              <th scope="col">Roles</th>
              <th scope="col">Last used</th>
              <th scope="col">Expires</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (key of keys(); track key.id) {
              <tr>
                <td>
                  <strong>{{ key.name }}</strong>
                  <span class="keys__meta"><code>…{{ key.prefix }}</code> · {{ key.createdBy ? 'by ' + key.createdBy + ', ' : '' }}{{ key.createdDate | date: 'd MMM y' }}</span>
                </td>
                <td>{{ roleNames(key) }}</td>
                <td>{{ key.lastUsedAt ? (key.lastUsedAt | date: 'd MMM, HH:mm') : 'Never' }}</td>
                <td>{{ key.expiresOn ? (key.expiresOn | date: 'd MMM y') : 'Never' }}</td>
                <td><span class="keys__status" [attr.data-status]="key.status">{{ key.status }}</span></td>
                <td>
                  @if (canManage && key.status !== 'Revoked') {
                    <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Revoke ' + key.name" (click)="revoke(key)">Revoke</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="createOpen" title="New API key" saveLabel="Create key" [busy]="busy()" [error]="formError()" [saveDisabled]="!name().trim() || !roleIds().length" (save)="create()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" placeholder="Payroll export to SAP" [value]="name()" (input)="name.set($any($event.target).value)" />
        </label>
        <div class="field">
          <span class="field__label" id="key-roles">Roles * <span class="muted">(the key can do what these allow)</span></span>
          <div class="keys__roles" role="group" aria-labelledby="key-roles">
            @for (role of roles(); track role.id) {
              <label><input type="checkbox" [checked]="roleIds().includes(role.id)" (change)="toggleRole(role.id, $any($event.target).checked)" /> {{ role.name }}</label>
            }
          </div>
        </div>
        <label class="field">
          <span class="field__label">Expires after</span>
          <select (change)="expiry.set($any($event.target).value === '' ? null : +$any($event.target).value)">
            @for (choice of expiryChoices; track choice.label) {
              <option [value]="choice.days ?? ''" [selected]="choice.days === expiry()">{{ choice.label }}</option>
            }
          </select>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .keys__heading { display: flex; align-items: center; gap: var(--gggs-space-2); margin: 0; font-size: var(--gggs-font-size-md); }
    .keys__reveal { border-color: var(--gggs-color-success); }
    .keys__reveal p { margin: 0; }
    .keys__value { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); }
    .keys__value code { padding: var(--gggs-space-2) var(--gggs-space-3); border-radius: var(--gggs-radius-md); background: var(--gggs-color-surface-sunken); overflow-wrap: anywhere; }
    .keys__meta { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .keys__roles { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(14rem, 100%), 1fr)); gap: var(--gggs-space-2); max-height: 16rem; overflow-y: auto; }
    .keys__status { padding: 0 var(--gggs-space-2); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-sunken); font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); }
    .keys__status[data-status='Active'] { background: var(--gggs-color-success-subtle); color: var(--gggs-color-success); }
  `,
})
export class ApiKeysPage {
  private readonly api = inject(AdminApi);

  protected readonly canManage = inject(PermissionStore).has('admin.user.manage');
  protected readonly origin = globalThis.location?.origin ?? '';
  protected readonly expiryChoices = EXPIRY_CHOICES;
  protected readonly loading = signal(true);
  protected readonly keys = signal<readonly ApiKeyItem[]>([]);
  protected readonly roles = signal<readonly Role[]>([]);
  protected readonly error = signal<string | null>(null);

  protected readonly createOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly roleIds = signal<readonly string[]>([]);
  protected readonly expiry = signal<number | null>(90);
  protected readonly created = signal<string | null>(null);
  protected readonly copied = signal(false);

  constructor() {
    void this.load();
  }

  protected roleNames(key: ApiKeyItem): string {
    return key.roles.map((role) => role.name).join(', ') || '—';
  }

  protected async openCreate(): Promise<void> {
    if (!this.roles().length) {
      this.roles.set(await this.api.listRoles().catch(() => []));
    }
    this.name.set('');
    this.roleIds.set([]);
    this.expiry.set(90);
    this.formError.set(null);
    this.createOpen.set(true);
  }

  protected toggleRole(id: string, on: boolean): void {
    this.roleIds.update((ids) => (on ? [...ids, id] : ids.filter((value) => value !== id)));
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const result = await this.api.createApiKey({ name: this.name().trim(), roleIds: this.roleIds(), expiresInDays: this.expiry() });
      this.createOpen.set(false);
      this.copied.set(false);
      this.created.set(result.value);
      await this.load();
    } catch (error) {
      this.formError.set(error instanceof ApiError ? error.message : 'The key could not be created.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.copied.set(true);
    } catch {
      this.copied.set(false);
    }
  }

  protected async revoke(key: ApiKeyItem): Promise<void> {
    if (!confirm(`Revoke "${key.name}"? Systems using it stop working at once.`)) {
      return;
    }
    this.error.set(null);
    try {
      await this.api.revokeApiKey(key.id);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The key could not be revoked.');
    }
  }

  private async load(): Promise<void> {
    try {
      this.keys.set(await this.api.apiKeys());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'API keys could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
