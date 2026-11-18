import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  PlatformApi,
  type DatabaseAuthentication,
  type DatabaseEncryption,
  type DatabaseTestResult,
  type OrganisationDatabaseView,
} from '@gggs/platform/data-access';
import { FormDialog, Skeleton, dateTime } from '@gggs/shared/ui';
import { messageOf } from './platform-permissions';

type Mode = 'shared' | 'dedicated';

const MASK = '••••••••••••';

/**
 * Where an organisation's data is stored: the shared database it is in, and the details of a
 * dedicated database kept for it. Staff administrators only. The password never reaches the browser
 * unless it is revealed, with a reason, and every reveal, test and change goes in the activity log.
 */
@Component({
  selector: 'gggs-console-organisation-database-tab',
  imports: [FormsModule, Skeleton, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (notice()) {
      <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>{{ notice() }}</span></p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (view(); as v) {
      <section class="panel stack">
        <header class="panel__header">
          <h2 class="panel__title">Where this organisation's data lives</h2>
          <span class="muted text-sm">Only platform staff administrators see this tab.</span>
        </header>
        <div class="modes" role="radiogroup" aria-label="Database">
          <label class="mode" [class.mode--on]="mode() === 'shared'">
            <input type="radio" name="db-mode" [checked]="mode() === 'shared'" (change)="mode.set('shared')" />
            <span><strong>Shared database</strong><br /><span class="muted text-sm">With other organisations in the platform database, kept apart by row-level security.</span></span>
          </label>
          <label class="mode" [class.mode--on]="mode() === 'dedicated'">
            <input type="radio" name="db-mode" [checked]="mode() === 'dedicated'" (change)="mode.set('dedicated')" />
            <span><strong>Dedicated database</strong><br /><span class="muted text-sm">Its own SQL Server database, for a contract or regulator that requires it.</span></span>
          </label>
        </div>
        <p class="alert alert--warning">
          <i class="pi pi-info-circle" aria-hidden="true"></i>
          <span>In use today: the <strong>shared database</strong>. Dedicated database details are saved and can be tested, but the HR application keeps this organisation in the shared database until dedicated hosting is switched on.</span>
        </p>
      </section>

      @if (mode() === 'shared') {
        <section class="panel">
          <header class="panel__header"><h2 class="panel__title">Shared database</h2></header>
          <dl class="definition-list">
            <dt>Server</dt><dd class="mono">{{ v.shared.server }}</dd>
            <dt>Database name</dt><dd class="mono">{{ v.shared.databaseName }}</dd>
            <dt>Shared with</dt><dd>{{ v.shared.otherOrganisations }} other organisation{{ v.shared.otherOrganisations === 1 ? '' : 's' }}</dd>
            <dt>Kept apart by</dt><dd>Row-level security on every table (organisation id <span class="mono">{{ v.shared.tenantId }}</span>)</dd>
            <dt>User and password</dt><dd>The platform's own login, held in the server's configuration. Not shown here: it opens every organisation's data.</dd>
            @if (v.shared.sizeMegabytes !== null) { <dt>Database size</dt><dd>{{ v.shared.sizeMegabytes }} MB (all organisations)</dd> }
          </dl>
          @if (v.dedicated) {
            <div class="toolbar">
              <span class="muted text-sm">Dedicated database details are saved for this organisation.</span>
              <span class="toolbar__spacer"></span>
              <button type="button" class="btn btn--danger btn--sm" [disabled]="busy()" (click)="remove()">Remove dedicated details</button>
            </div>
          }
        </section>
      } @else {
        <div class="grid">
          <section class="panel stack">
            <header class="panel__header"><h2 class="panel__title">Connection</h2></header>
            <div class="form-grid">
              <label class="field">
                <span class="field__label">Server *</span>
                <input class="mono" [(ngModel)]="server" maxlength="256" placeholder="sql-host.example.com,1433" />
                <span class="field__hint">Host name, then a comma and the port if it is not 1433.</span>
              </label>
              <label class="field">
                <span class="field__label">Database name *</span>
                <input class="mono" [(ngModel)]="databaseName" maxlength="128" />
              </label>
              <label class="field">
                <span class="field__label">Sign in with</span>
                <select [(ngModel)]="authentication">
                  <option value="SqlLogin">SQL Server login (user and password)</option>
                  <option value="Integrated">Windows / integrated</option>
                  <option value="ManagedIdentity">Azure managed identity</option>
                </select>
              </label>
              @if (authentication === 'SqlLogin') {
                <label class="field">
                  <span class="field__label">User *</span>
                  <input class="mono" [(ngModel)]="userName" maxlength="128" autocomplete="off" />
                  <span class="field__hint">A login with rights on this database only, not a server administrator.</span>
                </label>
                <div class="field">
                  <span class="field__label">Password *</span>
                  @if (replacing() || !v.dedicated?.hasPassword) {
                    <input class="mono" type="password" [(ngModel)]="password" autocomplete="new-password" aria-label="New password" />
                    <span class="field__hint">Stored encrypted.@if (v.dedicated?.hasPassword) { <button type="button" class="link" (click)="cancelReplace()">Keep the current password</button>}</span>
                  } @else {
                    <div class="row">
                      <input class="mono pw" [value]="revealed() ?? mask" readonly aria-label="Password" />
                      @if (revealed()) {
                        <button type="button" class="btn btn--secondary" (click)="hide()">Hide</button>
                      } @else {
                        <button type="button" class="btn btn--secondary" (click)="revealOpen.set(true)">Reveal…</button>
                      }
                    </div>
                    <span class="field__hint">
                      Stored encrypted.@if (v.dedicated?.passwordChangedDate) { Changed {{ when(v.dedicated!.passwordChangedDate!) }} by {{ v.dedicated!.passwordChangedBy }}. }
                      <button type="button" class="link" (click)="replace()">Replace password…</button>
                    </span>
                  }
                </div>
              }
              <label class="field">
                <span class="field__label">Encryption</span>
                <select [(ngModel)]="encryption">
                  <option value="Strict">Encrypt, verify the certificate</option>
                  <option value="TrustCertificate">Encrypt, trust the certificate (development)</option>
                  <option value="None">Do not encrypt</option>
                </select>
              </label>
            </div>

            @if (v.dedicated) {
              <div class="field">
                <span class="field__label">Connection string (password hidden)</span>
                <code class="conn mono">{{ v.dedicated.connectionStringPreview }}</code>
              </div>
            }

            @if (test(); as t) {
              <p class="alert" [class.alert--success]="t.succeeded" [class.alert--danger]="!t.succeeded" role="status">
                <i class="pi" [class.pi-check]="t.succeeded" [class.pi-times]="!t.succeeded" aria-hidden="true"></i><span>{{ t.message }}</span>
              </p>
            }

            <div class="toolbar">
              <span class="toolbar__spacer"></span>
              <button type="button" class="btn btn--secondary" [disabled]="busy()" (click)="runTest()">{{ testing() ? 'Testing…' : 'Test connection' }}</button>
              <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="save()">Save connection</button>
            </div>
          </section>

          <div class="stack">
            <section class="panel">
              <header class="panel__header"><h2 class="panel__title">Status</h2></header>
              @if (v.dedicated; as d) {
                <dl class="definition-list">
                  <dt>Last test</dt>
                  <dd>
                    @if (d.lastTestedDate) {
                      <span class="badge" [class.badge--success]="d.lastTestSucceeded" [class.badge--danger]="!d.lastTestSucceeded">{{ d.lastTestSucceeded ? 'Reachable' : 'Failed' }}</span>
                      <div class="subtle text-xs">{{ when(d.lastTestedDate) }}</div>
                    } @else { Not tested yet }
                  </dd>
                  @if (d.lastTestResult) { <dt>Result</dt><dd class="text-sm">{{ d.lastTestResult }}</dd> }
                  <dt>Saved</dt><dd>{{ when(d.updatedDate) }} by {{ d.updatedBy }}</dd>
                </dl>
              } @else {
                <p class="muted">Nothing saved yet. Fill in the connection, test it, then save.</p>
              }
            </section>
            <section class="panel">
              <header class="panel__header"><h2 class="panel__title">Who can see this</h2></header>
              <ul class="who">
                <li>Only platform staff administrators open this tab.</li>
                <li>The password is encrypted at rest and only sent to the browser when revealed.</li>
                <li>Every reveal, test and change is in the activity log.</li>
                <li>The organisation's own users never see any of it.</li>
              </ul>
            </section>
          </div>
        </div>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }

    <gggs-form-dialog [(visible)]="revealOpen" title="Reveal the database password?" saveLabel="Reveal for 60 seconds" [busy]="busy()" (save)="reveal()"
      description="It is shown for 60 seconds, and the reveal is recorded in the activity log with your name and reason.">
      <label class="field">
        <span class="field__label">Reason *</span>
        <input [(ngModel)]="reason" maxlength="500" placeholder="e.g. Support ticket 2041: restoring a report" />
      </label>
    </gggs-form-dialog>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .modes { display: flex; flex-wrap: wrap; gap: var(--gggs-space-3); }
    .mode { flex: 1 1 18rem; display: flex; gap: var(--gggs-space-3); align-items: flex-start; padding: var(--gggs-space-3) var(--gggs-space-4); border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-md); cursor: pointer; }
    .mode--on { border-color: #4f46e5; box-shadow: inset 0 0 0 1px #4f46e5; }
    .mode input { margin-top: 0.3rem; }
    .grid { display: grid; grid-template-columns: minmax(0, 2fr) minmax(16rem, 1fr); gap: var(--gggs-space-4); align-items: start; }
    .mono { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
    .pw { flex: 1; min-width: 0; }
    .conn { display: block; padding: var(--gggs-space-3); border-radius: var(--gggs-radius-sm); background: #0f172a; color: #e2e8f0; font-size: var(--gggs-font-size-xs); white-space: pre-wrap; word-break: break-all; }
    .link { border: 0; padding: 0; background: none; color: var(--gggs-color-primary); font: inherit; cursor: pointer; text-decoration: underline; }
    .who { margin: 0; padding-left: 1.1rem; font-size: var(--gggs-font-size-sm); line-height: 1.6; }
    @media (max-width: 900px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class OrganisationDatabaseTab {
  private readonly api = inject(PlatformApi);

  readonly tenantId = input.required<string>();

  protected readonly mask = MASK;
  protected readonly view = signal<OrganisationDatabaseView | null>(null);
  protected readonly mode = signal<Mode>('shared');
  protected readonly busy = signal(false);
  protected readonly testing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly test = signal<DatabaseTestResult | null>(null);
  protected readonly replacing = signal(false);
  protected readonly revealed = signal<string | null>(null);
  protected readonly revealOpen = signal(false);

  protected server = '';
  protected databaseName = '';
  protected authentication: DatabaseAuthentication = 'SqlLogin';
  protected userName = '';
  protected password = '';
  protected encryption: DatabaseEncryption = 'Strict';
  protected reason = '';
  private hideTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => void this.load(this.tenantId()));
    inject(DestroyRef).onDestroy(() => clearTimeout(this.hideTimer));
  }

  protected when(value: string): string {
    return dateTime(value);
  }

  protected replace(): void {
    this.hide();
    this.password = '';
    this.replacing.set(true);
  }

  protected cancelReplace(): void {
    this.password = '';
    this.replacing.set(false);
  }

  protected hide(): void {
    clearTimeout(this.hideTimer);
    this.revealed.set(null);
  }

  protected async reveal(): Promise<void> {
    if (!this.reason.trim()) {
      this.error.set('Say why you need to see the password; it is recorded.');
      return;
    }
    await this.act(async () => {
      const result = await this.api.revealDatabasePassword(this.tenantId(), this.reason.trim());
      this.revealOpen.set(false);
      this.reason = '';
      this.revealed.set(result.password);
      clearTimeout(this.hideTimer);
      this.hideTimer = setTimeout(() => this.revealed.set(null), 60_000);
    });
  }

  protected async runTest(): Promise<void> {
    this.testing.set(true);
    await this.act(async () => {
      this.test.set(await this.api.testDatabase(this.tenantId(), this.body()));
      await this.load(this.tenantId(), false);
    });
    this.testing.set(false);
  }

  protected async save(): Promise<void> {
    if (!this.server.trim() || !this.databaseName.trim() || (this.authentication === 'SqlLogin' && !this.userName.trim())) {
      this.error.set('Fill in the server, the database name and the user.');
      return;
    }
    await this.act(async () => {
      this.apply(await this.api.saveDatabase(this.tenantId(), this.body()));
      this.notice.set('Connection saved. The organisation stays in the shared database until dedicated hosting is switched on.');
    });
  }

  protected async remove(): Promise<void> {
    if (!window.confirm('Remove the saved dedicated database details? The database itself is not touched.')) return;
    await this.act(async () => {
      await this.api.removeDatabase(this.tenantId());
      this.notice.set('Dedicated database details removed.');
      await this.load(this.tenantId());
    });
  }

  private body() {
    return {
      server: this.server.trim(),
      databaseName: this.databaseName.trim(),
      authentication: this.authentication,
      userName: this.authentication === 'SqlLogin' ? this.userName.trim() : null,
      password: this.authentication === 'SqlLogin' && this.password ? this.password : null,
      encryption: this.encryption,
    };
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await work();
    } catch (error) {
      this.error.set(messageOf(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private apply(view: OrganisationDatabaseView): void {
    this.view.set(view);
    this.replacing.set(false);
    this.password = '';
    const d = view.dedicated;
    if (d) {
      this.server = d.server;
      this.databaseName = d.databaseName;
      this.authentication = d.authentication;
      this.userName = d.userName ?? '';
      this.encryption = d.encryption;
    }
  }

  private async load(id: string, resetMode = true): Promise<void> {
    try {
      const view = await this.api.database(id);
      if (resetMode) {
        this.apply(view);
        this.mode.set(view.dedicated ? 'dedicated' : 'shared');
      } else {
        this.view.set(view);
      }
    } catch (error) {
      this.error.set(messageOf(error, 'The database details could not be loaded.'));
    }
  }
}
