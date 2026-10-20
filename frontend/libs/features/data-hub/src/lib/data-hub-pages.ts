import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { DataHubApi, downloadFile } from '@gggs/shared/insight';
import type {
  ImportHandlerInfo,
  ImportOutcome,
  ImportRunItem,
  IntegrationAdapter,
  IntegrationConnection,
  IntegrationOperation,
  IntegrationRunItem,
  IntegrationRunOutcome,
} from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** Bulk imports: choose what to import, download its template, upload, preview every row, then commit the valid ones. */
@Component({
  selector: 'gggs-imports-page',
  imports: [DatePipe, PageHeader, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Imports" subtitle="Bring data in from spreadsheets: check every row first, then import the valid ones." />
    <div class="hub">
      <section class="panel stack">
        <h2 class="xp__heading">What to import</h2>
        @for (handler of handlers(); track handler.key) {
          <button type="button" class="xp__card hub__choice" [class.hub__choice--active]="handler.key === selected()?.key" (click)="choose(handler)">
            <strong>{{ handler.title }}</strong>
            <span class="muted">{{ handler.description }}</span>
          </button>
        } @empty {
          <gggs-empty-state icon="pi pi-upload" title="Nothing you can import" />
        }
      </section>
      <section class="panel stack">
        @if (selected(); as handler) {
          <div class="toolbar">
            <h2 class="xp__heading">{{ handler.title }}</h2>
            <button type="button" class="btn btn--sm" (click)="template(handler)"><i class="pi pi-download" aria-hidden="true"></i> Template</button>
          </div>
          <table class="table hub__columns">
            <thead><tr><th>Column</th><th>Required</th><th>Example</th><th>Meaning</th></tr></thead>
            <tbody>
              @for (column of handler.columns; track column.name) {
                <tr><td><code>{{ column.name }}</code></td><td>{{ column.required ? 'Yes' : '' }}</td><td class="muted">{{ column.example ?? '' }}</td><td>{{ column.description }}</td></tr>
              }
            </tbody>
          </table>
          <label class="field">
            <span class="field__label">CSV file</span>
            <input type="file" accept=".csv,text/csv" (change)="read($any($event.target).files?.[0])" />
          </label>
          <label class="field">
            <span class="field__label">Or paste CSV</span>
            <textarea rows="6" spellcheck="false" [value]="csv()" (input)="csv.set($any($event.target).value); outcome.set(null)"></textarea>
          </label>
          <div class="toolbar">
            <button type="button" class="btn" [disabled]="!csv().trim() || busy()" (click)="preview(handler)">Check</button>
            <button type="button" class="btn btn--primary" [disabled]="!outcome() || !outcome()!.valid || committed() || busy()" (click)="commit(handler)">Import {{ outcome()?.valid ?? 0 }} valid row(s)</button>
          </div>
          @if (outcome(); as o) {
            <p [class.xp__notice]="committed()">
              {{ o.rows }} row(s): {{ o.valid }} valid, {{ o.rows - o.valid }} with problems{{ committed() ? '; ' + o.imported + ' imported.' : '.' }}
            </p>
            @for (result of problems(); track $index) {
              <div class="xp__row">
                <span>{{ result.row ? 'Row ' + result.row : 'Note' }}</span>
                <span class="xp__danger">{{ result.errors.join(' ') }}</span>
              </div>
            }
            @for (result of good(); track $index) {
              <div class="xp__row">
                <span>Row {{ result.row }}</span>
                <span>{{ result.summary }}</span>
              </div>
            }
          }
        } @else {
          <p class="muted">Choose what to import.</p>
        }
        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
      </section>
    </div>
    <section class="panel stack">
      <h2 class="xp__heading">Recent imports</h2>
      @for (run of history(); track run.id) {
        <div class="xp__row">
          <span><strong>{{ titleOf(run.handlerKey) }}</strong> <span class="muted">{{ run.fileName }}</span></span>
          <span>{{ run.imported }} of {{ run.rows }} row(s) imported</span>
          <span class="muted">{{ run.createdDate | date: 'd MMM y, HH:mm' }}</span>
          <gggs-status-badge [status]="run.imported === run.rows ? 'Completed' : 'Warning'" />
        </div>
      } @empty {
        <p class="muted">No imports yet.</p>
      }
    </section>
  `,
  styles: `
    .hub {
      display: grid;
      grid-template-columns: 18rem 1fr;
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .hub__choice {
      text-align: start;
      background: transparent;
      color: inherit;
      cursor: pointer;
    }

    .hub__choice--active {
      border-color: var(--gggs-color-primary);
      outline: 1px solid var(--gggs-color-primary);
    }



    textarea {
      font-family: var(--gggs-font-family-mono, monospace);
    }

    @media (max-width: 64rem) {
      .hub {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class ImportsPage {
  private readonly api = inject(DataHubApi);

  protected readonly handlers = signal<readonly ImportHandlerInfo[]>([]);
  protected readonly history = signal<readonly ImportRunItem[]>([]);
  protected readonly selected = signal<ImportHandlerInfo | null>(null);
  protected readonly csv = signal('');
  protected readonly fileName = signal<string | null>(null);
  protected readonly outcome = signal<ImportOutcome | null>(null);
  protected readonly committed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly problems = computed(() => (this.outcome()?.results ?? []).filter((result) => result.errors.length > 0));
  protected readonly good = computed(() => (this.outcome()?.results ?? []).filter((result) => result.errors.length === 0).slice(0, 50));

  constructor() {
    this.api.imports().then((handlers) => this.handlers.set(handlers), (error: unknown) => this.error.set(message(error, 'Imports could not be loaded.')));
    this.loadHistory();
  }

  protected titleOf(key: string): string {
    return this.handlers().find((handler) => handler.key === key)?.title ?? key;
  }

  protected choose(handler: ImportHandlerInfo): void {
    this.selected.set(handler);
    this.csv.set('');
    this.fileName.set(null);
    this.outcome.set(null);
    this.committed.set(false);
    this.error.set(null);
  }

  protected async template(handler: ImportHandlerInfo): Promise<void> {
    downloadFile(`${handler.key}-template.csv`, 'text/csv', new Blob([await this.api.template(handler.key)], { type: 'text/csv' }));
  }

  protected async read(file: File | undefined): Promise<void> {
    if (file) {
      this.fileName.set(file.name);
      this.csv.set(await file.text());
      this.outcome.set(null);
      this.committed.set(false);
    }
  }

  protected preview(handler: ImportHandlerInfo): Promise<void> {
    return this.run(async () => {
      this.committed.set(false);
      this.outcome.set(await this.api.preview(handler.key, this.csv(), this.fileName()));
    });
  }

  protected commit(handler: ImportHandlerInfo): Promise<void> {
    return this.run(async () => {
      this.outcome.set(await this.api.run(handler.key, this.csv(), this.fileName()));
      this.committed.set(true);
      this.loadHistory();
    });
  }

  private loadHistory(): void {
    this.api.history().then((history) => this.history.set(history), () => undefined);
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Integrations: connections to banks, ledgers, directories and webhooks; their tests, operations and run log. */
@Component({
  selector: 'gggs-integrations-page',
  imports: [DatePipe, PageHeader, EmptyState, StatusBadge, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Integrations" subtitle="Connect PeopleOS to banks, accounting, identity providers and other tools. Secrets are encrypted and never shown again." />
    <section class="panel stack">
      <h2 class="xp__heading">Connections</h2>
      @for (connection of connections(); track connection.id) {
        <div class="xp__row">
          <span><strong>{{ connection.name }}</strong> <span class="muted">{{ connection.adapterName }}</span></span>
          <span class="muted">{{ settingsSummary(connection) }}</span>
          <span class="muted">{{ connection.lastTestedAt ? 'tested ' + (connection.lastTestedAt | date: 'd MMM, HH:mm') + ': ' + (connection.lastMessage ?? '') : 'not tested' }}</span>
          <span class="toolbar">
            @if (canManage) {
              <button type="button" class="btn btn--sm" (click)="test(connection)">Test</button>
              @if (adapterOf(connection)?.operations?.length) {
                <button type="button" class="btn btn--sm btn--primary" (click)="openRun(connection)">Run</button>
              }
              <button type="button" class="btn btn--link btn--sm" (click)="edit(connection)">Edit</button>
            }
          </span>
          <gggs-status-badge [status]="!connection.isActive ? 'Inactive' : connection.lastTestSucceeded === undefined ? 'Draft' : connection.lastTestSucceeded ? 'Ok' : 'Error'" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-link" title="No connections yet" description="Add one from the catalogue below." />
      }
    </section>
    <section class="panel stack">
      <h2 class="xp__heading">Available integrations</h2>
      <div class="xp__cards">
        @for (adapter of adapters(); track adapter.key) {
          <div class="xp__card">
            <span class="muted">{{ adapter.category }}</span>
            <strong>{{ adapter.name }}</strong>
            <span>{{ adapter.description }}</span>
            @if (canManage) {
              <div><button type="button" class="btn btn--sm" (click)="create(adapter)">Connect</button></div>
            }
          </div>
        }
      </div>
    </section>
    <section class="panel stack">
      <h2 class="xp__heading">Recent runs</h2>
      @for (run of runs(); track run.id) {
        <div class="xp__row">
          <span><strong>{{ nameOf(run.connectionId) }}</strong> <span class="muted">{{ run.operation }}</span></span>
          <span>{{ run.message }}</span>
          <span class="muted">{{ run.createdDate | date: 'd MMM y, HH:mm' }}{{ run.fileName ? ' · ' + run.fileName : '' }}</span>
          <gggs-status-badge [status]="run.succeeded ? 'Ok' : 'Failed'" />
        </div>
      } @empty {
        <p class="muted">Nothing has run yet.</p>
      }
      @if (notice()) {
        <p class="alert alert--success" role="status">{{ notice() }}</p>
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="editOpen" [title]="(editing() ? 'Edit ' : 'Connect ') + (adapter()?.name ?? '')" [busy]="busy()" [error]="formError()" [saveDisabled]="!name().trim()" (save)="save()">
      @if (adapter(); as a) {
        <div class="stack">
          <label class="field"><span class="field__label">Connection name</span><input type="text" maxlength="100" [value]="name()" (input)="name.set($any($event.target).value)" /></label>
          @for (field of a.fields; track field.key) {
            <label class="field">
              <span class="field__label">{{ field.label }}{{ field.required ? '' : ' (optional)' }}</span>
              <input [type]="field.secret ? 'password' : 'text'" autocomplete="off" [placeholder]="placeholder(field.key, field.secret, field.hint)" [value]="setting(field.key)" (input)="setSetting(field.key, $any($event.target).value)" />
            </label>
          }
          @if (editing()) {
            <label class="xp__flag"><input type="checkbox" [checked]="active()" (change)="active.set($any($event.target).checked)" /> Switched on</label>
          }
        </div>
      }
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="runOpen" [title]="'Run ' + (running()?.name ?? '')" saveLabel="Run" [busy]="busy()" [error]="formError()" [saveDisabled]="!operation()" (save)="run()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Operation</span>
          <select [value]="operation()?.key ?? ''" (change)="pickOperation($any($event.target).value)">
            @for (op of operations(); track op.key) {
              <option [value]="op.key">{{ op.label }}</option>
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
})
export class IntegrationsPage {
  private readonly api = inject(DataHubApi);

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly adapters = signal<readonly IntegrationAdapter[]>([]);
  protected readonly connections = signal<readonly IntegrationConnection[]>([]);
  protected readonly runs = signal<readonly IntegrationRunItem[]>([]);
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
  protected readonly operations = computed(() => (this.running() ? this.adapterOf(this.running()!)?.operations ?? [] : []));
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.adapters().then((adapters) => this.adapters.set(adapters), (error: unknown) => this.error.set(message(error, 'Integrations could not be loaded.')));
    this.reload();
  }

  protected adapterOf(connection: IntegrationConnection): IntegrationAdapter | undefined {
    return this.adapters().find((adapter) => adapter.key === connection.adapterKey);
  }

  protected nameOf(id: string): string {
    return this.connections().find((connection) => connection.id === id)?.name ?? 'Connection';
  }

  protected settingsSummary(connection: IntegrationConnection): string {
    return Object.entries(connection.settings)
      .slice(0, 3)
      .map(([key, value]) => `${key}: ${value}`)
      .join(' · ');
  }

  protected setting(key: string): string {
    return this.settings()[key] ?? '';
  }

  protected setSetting(key: string, value: string): void {
    this.settings.update((settings) => ({ ...settings, [key]: value }));
  }

  /** A saved secret shows its masked hint as a placeholder; typing replaces it, leaving it blank keeps it. */
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
      await this.api.save(this.editing()?.id ?? null, { adapterKey: adapter.key, name: this.name(), settings: this.settings(), isActive: this.active() });
      this.editOpen.set(false);
      this.reload();
    } catch (error) {
      this.formError.set(message(error, 'The connection could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async test(connection: IntegrationConnection): Promise<void> {
    try {
      const tested = await this.api.test(connection.id);
      this.notice.set(`${tested.name}: ${tested.lastMessage ?? ''}`);
      this.reload();
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

      this.reload();
    } catch (error) {
      this.formError.set(message(error, 'The operation could not run.'));
    } finally {
      this.busy.set(false);
    }
  }

  private reload(): void {
    this.api.connections().then((connections) => this.connections.set(connections), () => undefined);
    this.api.runs().then((runs) => this.runs.set(runs), () => undefined);
  }
}
