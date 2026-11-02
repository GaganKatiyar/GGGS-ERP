import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { DataHubApi, downloadFile } from '@gggs/shared/insight';
import type { IntegrationAdapter, IntegrationConnection, IntegrationOperation, IntegrationRunItem, IntegrationRunOutcome } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton } from '@gggs/shared/ui';
import { catalogue, categoryOf, healthOf, type ConnectionHealth } from './studio-model';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * Studio connectors: the catalogue of everything Nuts and Bolts can connect to, the organisation's
 * connections with their health, and the operations each can run. Secrets are encrypted and never
 * shown again; leaving one blank keeps it.
 */
@Component({
  selector: 'gggs-connectors-page',
  imports: [DatePipe, PageHeader, EmptyState, FormDialog, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Connectors" subtitle="Connect Nuts and Bolts to messaging, identity, email, payroll and your own systems." />

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else {
      <section class="panel stack" aria-labelledby="connections-heading">
        <h2 id="connections-heading" class="studio__heading">Your connections</h2>
        @for (connection of connections(); track connection.id) {
          <div class="studio__connection">
            <span class="studio__tile" aria-hidden="true"><i [class]="iconOf(connection)"></i></span>
            <span class="studio__main">
              <strong>{{ connection.name }}</strong>
              <span class="muted">{{ connection.adapterName }} · {{ connection.lastTestedAt ? 'tested ' + (connection.lastTestedAt | date: 'd MMM, HH:mm') : 'not tested yet' }}{{ connection.lastMessage ? ' · ' + connection.lastMessage : '' }}</span>
            </span>
            <span class="studio__health" [attr.data-health]="health(connection)">{{ health(connection) }}</span>
            @if (canManage) {
              <span class="studio__actions">
                <button type="button" class="btn btn--sm" (click)="test(connection)">Test</button>
                @if (adapterOf(connection)?.operations?.length) {
                  <button type="button" class="btn btn--sm" (click)="openRun(connection)">Run</button>
                }
                <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Edit ' + connection.name" (click)="edit(connection)">Edit</button>
              </span>
            }
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-link" title="Nothing connected yet" description="Pick a connector from the catalogue below." />
        }
      </section>

      <section class="stack" aria-labelledby="catalogue-heading">
        <div class="toolbar">
          <h2 id="catalogue-heading" class="studio__heading">Catalogue</h2>
          <label class="studio__search">
            <span class="sr-only">Search connectors</span>
            <input type="search" placeholder="Search connectors" [value]="query()" (input)="query.set($any($event.target).value)" />
          </label>
        </div>
        @for (group of groups(); track group.label) {
          <h3 class="studio__group"><i [class]="group.icon" aria-hidden="true"></i> {{ group.label }}</h3>
          <div class="studio__grid">
            @for (item of group.adapters; track item.key) {
              <article class="studio__card elevated">
                <div class="studio__card-head">
                  <span class="studio__tile" aria-hidden="true"><i [class]="group.icon"></i></span>
                  <strong>{{ item.name }}</strong>
                  @if (item.connected) {
                    <span class="studio__connected">Connected{{ item.connected > 1 ? ' ×' + item.connected : '' }}</span>
                  }
                </div>
                <p class="muted">{{ item.description }}</p>
                @if (canManage) {
                  <button type="button" class="btn btn--sm" [attr.aria-label]="'Connect ' + item.name" (click)="create(item)">{{ item.connected ? 'Connect another' : 'Connect' }}</button>
                }
              </article>
            }
          </div>
        } @empty {
          <p class="muted">No connector matches "{{ query() }}".</p>
        }
      </section>

      <section class="panel stack" aria-labelledby="runs-heading">
        <h2 id="runs-heading" class="studio__heading">Recent runs</h2>
        @for (run of runs(); track run.id) {
          <div class="studio__run">
            <strong>{{ nameOf(run.connectionId) }}</strong>
            <span class="muted">{{ run.operation }}</span>
            <span>{{ run.message }}</span>
            <span class="muted">{{ run.createdDate | date: 'd MMM y, HH:mm' }}{{ run.fileName ? ' · ' + run.fileName : '' }}</span>
            <span class="studio__health" [attr.data-health]="run.succeeded ? 'Healthy' : 'Failing'">{{ run.succeeded ? 'Done' : 'Failed' }}</span>
          </div>
        } @empty {
          <p class="muted">Nothing has run yet.</p>
        }
      </section>
    }

    @if (notice()) {
      <p class="alert alert--success" role="status">{{ notice() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="editOpen" [title]="(editing() ? 'Edit ' : 'Connect ') + (adapter()?.name ?? '')" [busy]="busy()" [error]="formError()" [saveDisabled]="!name().trim()" (save)="save()">
      @if (adapter(); as a) {
        <div class="stack">
          <p class="muted">{{ a.description }}</p>
          <label class="field"><span class="field__label">Connection name</span><input type="text" maxlength="100" [value]="name()" (input)="name.set($any($event.target).value)" /></label>
          @for (field of a.fields; track field.key) {
            <label class="field">
              <span class="field__label">{{ field.label }}{{ field.required ? '' : ' (optional)' }}</span>
              <input [type]="field.secret ? 'password' : 'text'" autocomplete="off" [placeholder]="placeholder(field.key, field.secret, field.hint)" [value]="setting(field.key)" (input)="setSetting(field.key, $any($event.target).value)" />
            </label>
          }
          @if (editing()) {
            <label class="field"><span><input type="checkbox" [checked]="active()" (change)="active.set($any($event.target).checked)" /> Switched on</span></label>
          }
          <p class="muted"><i class="pi pi-lock" aria-hidden="true"></i> Secrets are encrypted when saved and never shown again.</p>
        </div>
      }
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="runOpen" [title]="'Run ' + (running()?.name ?? '')" saveLabel="Run" [busy]="busy()" [error]="formError()" [saveDisabled]="!operation()" (save)="run()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Operation</span>
          <select (change)="pickOperation($any($event.target).value)">
            @for (op of operations(); track op.key) {
              <option [value]="op.key" [selected]="op.key === operation()?.key">{{ op.label }}</option>
            }
          </select>
        </label>
        @if (operation(); as op) {
          <p class="muted">{{ op.description }}</p>
          @for (parameter of op.parameters; track parameter.key) {
            <label class="field">
              <span class="field__label">{{ parameter.label }}</span>
              <input type="text" [placeholder]="parameter.hint ?? ''" [value]="parameterValue(parameter.key)" (input)="setParameter(parameter.key, $any($event.target).value)" />
            </label>
          }
        }
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-5); }
    .studio__heading { margin: 0; margin-inline-end: auto; font-size: var(--gggs-font-size-md); }
    .studio__group { display: flex; align-items: center; gap: var(--gggs-space-2); margin: var(--gggs-space-2) 0 0; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-sm); font-weight: var(--gggs-font-weight-semibold); text-transform: uppercase; letter-spacing: 0.04em; }
    .studio__search input { min-width: min(18rem, 100%); }
    .studio__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(17rem, 100%), 1fr)); gap: var(--gggs-space-4); }
    .studio__card { display: flex; flex-direction: column; gap: var(--gggs-space-2); padding: var(--gggs-space-4); }
    .studio__card p { flex: 1; margin: 0; font-size: var(--gggs-font-size-sm); }
    .studio__card button { align-self: flex-start; }
    .studio__card-head { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-2); }
    .studio__tile { display: grid; flex: none; place-items: center; width: 2.25rem; height: 2.25rem; border-radius: var(--gggs-radius-md); background: var(--gggs-gradient-tile); color: var(--gggs-color-primary); }
    .studio__connected { padding: 0 var(--gggs-space-2); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-success-subtle); color: var(--gggs-color-success); font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); }
    .studio__connection, .studio__run { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); padding-block: var(--gggs-space-2); border-block-end: 1px solid var(--gggs-color-border); }
    .studio__connection:last-of-type, .studio__run:last-of-type { border-block-end: 0; }
    .studio__main { display: flex; flex: 1 1 16rem; flex-direction: column; min-width: 0; }
    .studio__actions { display: flex; gap: var(--gggs-space-2); }
    .studio__health { padding: 0 var(--gggs-space-2); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-sunken); color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); }
    .studio__health[data-health='Healthy'] { background: var(--gggs-color-success-subtle); color: var(--gggs-color-success); }
    .studio__health[data-health='Failing'] { background: var(--gggs-color-danger-subtle); color: var(--gggs-color-danger); }
  `,
})
export class ConnectorsPage {
  private readonly api = inject(DataHubApi);

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly loading = signal(true);
  protected readonly adapters = signal<readonly IntegrationAdapter[]>([]);
  protected readonly connections = signal<readonly IntegrationConnection[]>([]);
  protected readonly runs = signal<readonly IntegrationRunItem[]>([]);
  protected readonly query = signal('');
  protected readonly groups = computed(() => catalogue(this.adapters(), this.connections(), this.query()));

  protected readonly editOpen = signal(false);
  protected readonly editing = signal<IntegrationConnection | null>(null);
  protected readonly adapter = signal<IntegrationAdapter | null>(null);
  protected readonly name = signal('');
  protected readonly settings = signal<Readonly<Record<string, string>>>({});
  protected readonly active = signal(true);
  protected readonly runOpen = signal(false);
  protected readonly running = signal<IntegrationConnection | null>(null);
  protected readonly operation = signal<IntegrationOperation | null>(null);
  protected readonly parameters = signal<Readonly<Record<string, string>>>({});
  protected readonly operations = computed(() => {
    const connection = this.running();
    return connection ? (this.adapterOf(connection)?.operations ?? []) : [];
  });
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.adapters().then(
      (adapters) => this.adapters.set(adapters),
      (error: unknown) => this.error.set(message(error, 'Connectors could not be loaded.')),
    );
    void this.reload().finally(() => this.loading.set(false));
  }

  protected adapterOf(connection: IntegrationConnection): IntegrationAdapter | undefined {
    return this.adapters().find((adapter) => adapter.key === connection.adapterKey);
  }

  protected iconOf(connection: IntegrationConnection): string {
    return categoryOf(this.adapterOf(connection)?.category ?? '').icon;
  }

  protected health(connection: IntegrationConnection): ConnectionHealth {
    return healthOf(connection);
  }

  protected nameOf(id: string): string {
    return this.connections().find((connection) => connection.id === id)?.name ?? 'Connection';
  }

  protected setting(key: string): string {
    return this.settings()[key] ?? '';
  }

  protected setSetting(key: string, value: string): void {
    this.settings.update((settings) => ({ ...settings, [key]: value }));
  }

  /** A saved secret shows its masked hint; typing replaces it, leaving it blank keeps it. */
  protected placeholder(key: string, secret: boolean, hint: string | undefined): string {
    const saved = this.editing()?.settings[key];
    return secret && saved ? `${saved} (leave blank to keep)` : (hint ?? '');
  }

  protected create(adapter: IntegrationAdapter): void {
    this.editing.set(null);
    this.adapter.set(adapter);
    this.name.set(adapter.name);
    this.settings.set({});
    this.active.set(true);
    this.formError.set(null);
    this.editOpen.set(true);
  }

  protected edit(connection: IntegrationConnection): void {
    const adapter = this.adapterOf(connection);
    if (!adapter) {
      return;
    }
    this.editing.set(connection);
    this.adapter.set(adapter);
    this.name.set(connection.name);
    this.settings.set(Object.fromEntries(adapter.fields.filter((field) => !field.secret).map((field) => [field.key, connection.settings[field.key] ?? ''])));
    this.active.set(connection.isActive);
    this.formError.set(null);
    this.editOpen.set(true);
  }

  protected async save(): Promise<void> {
    const adapter = this.adapter();
    if (!adapter) {
      return;
    }
    this.busy.set(true);
    try {
      const saved = await this.api.save(this.editing()?.id ?? null, { adapterKey: adapter.key, name: this.name(), settings: this.settings(), isActive: this.active() });
      this.editOpen.set(false);
      this.notice.set(`${saved.name} saved. Test it to check the settings work.`);
      await this.reload();
    } catch (error) {
      this.formError.set(message(error, 'The connection could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async test(connection: IntegrationConnection): Promise<void> {
    this.error.set(null);
    try {
      const tested = await this.api.test(connection.id);
      this.notice.set(`${tested.name}: ${tested.lastMessage ?? ''}`);
      await this.reload();
    } catch (error) {
      this.error.set(message(error, 'The test could not run.'));
    }
  }

  protected openRun(connection: IntegrationConnection): void {
    this.running.set(connection);
    this.operation.set(this.adapterOf(connection)?.operations[0] ?? null);
    const last = new Date();
    last.setMonth(last.getMonth() - 1);
    this.parameters.set({ period: `${last.getFullYear()}-${`${last.getMonth() + 1}`.padStart(2, '0')}` });
    this.formError.set(null);
    this.runOpen.set(true);
  }

  protected pickOperation(key: string): void {
    this.operation.set(this.operations().find((op) => op.key === key) ?? null);
  }

  protected parameterValue(key: string): string {
    return this.parameters()[key] ?? '';
  }

  protected setParameter(key: string, value: string): void {
    this.parameters.update((parameters) => ({ ...parameters, [key]: value }));
  }

  protected async run(): Promise<void> {
    const connection = this.running();
    const operation = this.operation();
    if (!connection || !operation) {
      return;
    }
    this.busy.set(true);
    this.formError.set(null);
    try {
      const outcome: IntegrationRunOutcome = await this.api.runOperation(connection.id, operation.key, this.parameters());
      if (outcome.succeeded && outcome.contentBase64 && outcome.fileName) {
        downloadFile(outcome.fileName, outcome.contentType ?? 'application/octet-stream', outcome.contentBase64);
      }
      if (outcome.succeeded) {
        this.runOpen.set(false);
        this.notice.set(outcome.message);
      } else {
        this.formError.set(outcome.message);
      }
      await this.reload();
    } catch (error) {
      this.formError.set(message(error, 'The operation could not run.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    const [connections, runs] = await Promise.all([this.api.connections().catch(() => this.connections()), this.api.runs().catch(() => this.runs())]);
    this.connections.set(connections);
    this.runs.set(runs);
  }
}
