import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import type { ChannelSendOutcome, EmailSenderItem, EmailSendersView } from '@gggs/shared/models';
import { NotificationsApi } from '@gggs/shared/notifications';
import { PageHeader, Skeleton } from '@gggs/shared/ui';
import { senderStatus, type SenderStatus } from './email-model';

/**
 * Who each module's emails come from. A module without its own sender uses the default sender; a blank
 * field takes the default sender's value. The server decides all of this when an email goes; this page
 * only shows and saves it.
 */
@Component({
  selector: 'gggs-email-senders-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Email senders" subtitle="Choose who each module's emails come from. A module without its own sender uses the default sender." />

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="8" />
    } @else if (view(); as data) {
      @if (defaultProblem(); as problem) {
        <p class="alert alert--warning" role="status">
          <i class="pi pi-exclamation-triangle" aria-hidden="true"></i>
          <span><strong>Email is not reaching anyone.</strong> {{ problem }} Choose a connection for the default sender below, or add one under <a routerLink="../connectors">Connectors</a>.</span>
        </p>
      }

      <p class="senders__rule muted">
        <strong>How an email picks its sender:</strong>
        <span class="senders__step">1 · the template's own sender</span> →
        <span class="senders__step">2 · its module's sender</span> →
        <span class="senders__step">3 · the default sender</span>
      </p>

      <div class="senders">
        <section class="panel senders__list" aria-label="Senders">
          <div class="senders__scroll">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">Module</th>
                  <th scope="col">Sends as</th>
                  <th scope="col">Through</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                @for (row of data.senders; track row.key) {
                  <tr class="senders__row" [class.senders__row--on]="row.key === selectedKey()" (click)="select(row.key)">
                    <td>
                      <button type="button" class="senders__pick" [attr.aria-pressed]="row.key === selectedKey()" (click)="select(row.key); $event.stopPropagation()">{{ row.name }}</button>
                      <span class="muted senders__small">{{ row.templates }} {{ row.templates === 1 ? 'template' : 'templates' }}</span>
                    </td>
                    <td>
                      <span>{{ sendsAs(row).name }}</span>
                      <span class="muted senders__small">{{ sendsAs(row).address }}</span>
                    </td>
                    <td><span class="senders__small">{{ through(row) }}</span></td>
                    <td><span class="senders__chip" [attr.data-status]="status(row)">{{ status(row) }}</span></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>

        @if (selected(); as row) {
          <section class="panel senders__editor" [attr.aria-label]="'Sender for ' + row.name">
            <div>
              <span class="muted senders__eyebrow">Sender for</span>
              <h2 class="senders__title">{{ row.name }}</h2>
              <p class="muted senders__small">{{ row.description }}</p>
            </div>

            @if (row.problem && row.key !== 'default') {
              <p class="alert alert--warning senders__small"><i class="pi pi-info-circle" aria-hidden="true"></i><span>{{ row.problem }}</span></p>
            }

            @if (row.key !== 'default') {
              <div class="senders__choices" role="radiogroup" aria-label="Which sender">
                <label class="senders__choice" [class.senders__choice--on]="!ownSender()">
                  <input type="radio" name="mode" [checked]="!ownSender()" [disabled]="!canManage" (change)="ownSender.set(false)" />
                  <span><strong>Use the default sender</strong><br /><span class="muted senders__small">{{ defaultLine() }}</span></span>
                </label>
                <label class="senders__choice" [class.senders__choice--on]="ownSender()">
                  <input type="radio" name="mode" [checked]="ownSender()" [disabled]="!canManage" (change)="ownSender.set(true)" />
                  <span><strong>Give this module its own sender</strong><br /><span class="muted senders__small">Its own name, address, reply-to and signature. Blank fields take the default sender's.</span></span>
                </label>
              </div>
            }

            @if (row.key === 'default' || ownSender()) {
              <div class="form-grid" [formGroup]="form">
                <label class="field form-grid__full">
                  <span class="field__label">Send through</span>
                  <select formControlName="connectionId">
                    <option value="">{{ row.key === 'default' ? noConnectionLabel() : "The default sender's connection" }}</option>
                    @for (option of data.connections; track option.id) {
                      <option [value]="option.id">{{ option.name }} ({{ option.adapterName }}){{ option.isActive ? '' : ' - switched off' }}</option>
                    }
                  </select>
                  <span class="field__hint">
                    @if (!data.connections.length) {
                      No email connection yet. Add an SMTP server, Microsoft 365 or SendGrid under <a routerLink="../connectors">Connectors</a>.
                    } @else {
                      The address below must be one this connection may send as: a "Send mail as" alias in Gmail, or Send As rights in Microsoft 365.
                    }
                  </span>
                </label>
                <label class="field">
                  <span class="field__label">Sender name</span>
                  <input type="text" formControlName="fromName" maxlength="200" placeholder="e.g. GGGS Star Rewards" />
                </label>
                <label class="field">
                  <span class="field__label">Send from</span>
                  <input type="email" formControlName="fromAddress" maxlength="256" placeholder="The connection's own address" />
                </label>
                <label class="field">
                  <span class="field__label">Replies go to</span>
                  <input type="email" formControlName="replyTo" maxlength="256" placeholder="Optional" />
                </label>
                <label class="field">
                  <span class="field__label">Also copy (BCC)</span>
                  <input type="email" formControlName="bcc" maxlength="256" placeholder="Optional, e.g. an HR archive" />
                </label>
                <label class="field form-grid__full">
                  <span class="field__label">Signature</span>
                  <textarea rows="3" formControlName="signature" maxlength="1000" placeholder="Added under every email from this sender"></textarea>
                </label>
                <div class="field">
                  <span class="field__label" id="colour-label">Header colour</span>
                  <span class="senders__colour">
                    <input type="color" aria-labelledby="colour-label" [value]="form.controls.brandColor.value || row.moduleColor" [disabled]="!canManage" (input)="form.controls.brandColor.setValue($any($event.target).value)" />
                    <input type="text" formControlName="brandColor" maxlength="7" [placeholder]="row.moduleColor" aria-label="Header colour as #rrggbb" />
                  </span>
                </div>
              </div>
            }

            @if (row.key !== 'default' && ownSender()) {
              <label class="senders__pause">
                <input type="checkbox" [formControl]="form.controls.isPaused" />
                <span><strong>Pause email for this module</strong><br /><span class="muted senders__small">In-app notifications still go. Notices people must get, such as absence notices, still go by email.</span></span>
              </label>
            }

            @if (canManage) {
              <div class="senders__actions">
                <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="save(row)">{{ busy() ? 'Saving…' : 'Save sender' }}</button>
              </div>

              <form class="senders__test" (submit)="$event.preventDefault(); test(row)">
                <label class="field">
                  <span class="field__label">Send a test to</span>
                  <input type="email" [value]="testTo()" (input)="testTo.set($any($event.target).value)" placeholder="you@example.com" />
                </label>
                <button type="submit" class="btn" [disabled]="testing() || !testTo().trim()"><i class="pi pi-send" aria-hidden="true"></i> {{ testing() ? 'Sending…' : 'Send test' }}</button>
              </form>
              <p class="muted senders__small">The test goes as the sender is saved, with its header and signature.</p>
              @if (outcome(); as result) {
                <p class="alert" [class.alert--success]="result.succeeded" [class.alert--danger]="!result.succeeded" role="status">{{ result.message }}</p>
              }
            }
          </section>
        }
      </div>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .senders { display: flex; flex-wrap: wrap; gap: var(--gggs-space-4); align-items: flex-start; }
    .senders__list { flex: 999 1 36rem; min-width: 0; padding: 0; overflow: hidden; }
    .senders__scroll { overflow-x: auto; }
    .senders__editor { flex: 1 1 22rem; max-width: 32rem; display: flex; flex-direction: column; gap: var(--gggs-space-3); }
    .senders__row { cursor: pointer; }
    .senders__row--on { background: var(--gggs-color-primary-soft, rgba(176, 16, 42, 0.06)); box-shadow: inset 3px 0 0 var(--gggs-color-primary); }
    .senders__pick { all: unset; cursor: pointer; font-weight: var(--gggs-font-weight-semibold); display: block; }
    .senders__pick:focus-visible { outline: 2px solid var(--gggs-color-primary); outline-offset: 2px; }
    .senders__small { display: block; font-size: var(--gggs-font-size-sm); }
    .senders__eyebrow { font-size: var(--gggs-font-size-xs); text-transform: uppercase; letter-spacing: 0.05em; font-weight: var(--gggs-font-weight-semibold); }
    .senders__title { margin: var(--gggs-space-1) 0; font-size: var(--gggs-font-size-lg); }
    .senders__rule { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-2); margin: 0; }
    .senders__step { padding: 2px 10px; border-radius: 999px; background: var(--gggs-color-surface-2, #f1f5f9); color: var(--gggs-color-text); font-size: var(--gggs-font-size-sm); }
    .senders__chip { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); white-space: nowrap; background: #f1f5f9; color: #334155; }
    .senders__chip[data-status='Own sender'] { background: #dcfce7; color: #166534; }
    .senders__chip[data-status='Paused'] { background: #fef3c7; color: #92400e; }
    .senders__chip[data-status='Needs attention'] { background: #fee2e2; color: #991b1b; }
    .senders__choices { display: flex; flex-direction: column; gap: var(--gggs-space-2); }
    .senders__choice, .senders__pause { display: flex; gap: var(--gggs-space-2); align-items: flex-start; padding: var(--gggs-space-3); border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-md); cursor: pointer; }
    .senders__pause { border: 0; padding: 0; }
    .senders__choice--on { border-color: var(--gggs-color-primary); }
    .senders__colour { display: flex; gap: var(--gggs-space-2); align-items: center; }
    .senders__colour input[type='color'] { width: 2.75rem; height: 2.75rem; padding: 0; border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-sm); background: none; }
    .senders__colour input[type='text'] { flex: 1; min-width: 0; }
    .senders__actions { display: flex; justify-content: flex-end; gap: var(--gggs-space-2); }
    .senders__test { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--gggs-space-2); padding-top: var(--gggs-space-3); border-top: 1px solid var(--gggs-color-border); }
    .senders__test .field { flex: 1 1 14rem; min-width: 0; }
  `,
})
export class EmailSendersPage {
  private readonly api = inject(NotificationsApi);

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly testing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly view = signal<EmailSendersView | null>(null);
  protected readonly selectedKey = signal('default');
  protected readonly ownSender = signal(false);
  protected readonly testTo = signal('');
  protected readonly outcome = signal<ChannelSendOutcome | null>(null);

  protected readonly selected = computed(() => this.view()?.senders.find((row) => row.key === this.selectedKey()) ?? null);
  protected readonly fallback = computed(() => this.view()?.senders.find((row) => row.key === 'default') ?? null);
  protected readonly defaultProblem = computed(() => this.fallback()?.problem ?? null);
  protected readonly defaultLine = computed(() => {
    const row = this.fallback();
    const name = row?.fromName ?? 'Nuts and Bolts';
    return row?.fromAddress ? `${name} <${row.fromAddress}>` : name;
  });
  protected readonly noConnectionLabel = computed(() => {
    const data = this.view();
    const route = data?.connections.find((option) => option.id === data.routeConnectionId);
    if (route) {
      return `Studio's email route (${route.name})`;
    }
    return data?.systemSenderDelivers ? "The system's mail server" : 'None: emails are only written to the server log';
  });

  protected readonly form = new FormGroup({
    connectionId: new FormControl('', { nonNullable: true }),
    fromName: new FormControl('', { nonNullable: true }),
    fromAddress: new FormControl('', { nonNullable: true }),
    replyTo: new FormControl('', { nonNullable: true }),
    bcc: new FormControl('', { nonNullable: true }),
    signature: new FormControl('', { nonNullable: true }),
    brandColor: new FormControl('', { nonNullable: true }),
    isPaused: new FormControl(false, { nonNullable: true }),
  });

  constructor() {
    if (!this.canManage) {
      this.form.disable();
    }
    void this.load();
  }

  protected status(row: EmailSenderItem): SenderStatus {
    return senderStatus(row);
  }

  /** What the row's emails go out as: its own values where it has them, the default sender's otherwise. */
  protected sendsAs(row: EmailSenderItem): { name: string; address: string } {
    const fallback = this.fallback();
    const own = row.key === 'default' || row.hasOwn;
    const name = (own ? row.fromName : undefined) ?? fallback?.fromName ?? 'Nuts and Bolts';
    const address = (own ? row.fromAddress : undefined) ?? fallback?.fromAddress ?? this.connectionAddress(row);
    return { name, address: own || row.key === 'default' ? address : `${address} (default)` };
  }

  /** With no address of its own, an email goes from the one saved on its connection. */
  private connectionAddress(row: EmailSenderItem): string {
    const data = this.view();
    const id = (row.key !== 'default' && row.connectionId) || this.fallback()?.connectionId || data?.routeConnectionId;
    const option = data?.connections.find((candidate) => candidate.id === id);
    return option ? `${option.name}'s own address` : "The system's address";
  }

  protected through(row: EmailSenderItem): string {
    const data = this.view();
    if (!data) {
      return '';
    }
    if (row.key !== 'default' && !row.connectionId) {
      return 'Default';
    }
    const id = row.connectionId ?? data.routeConnectionId;
    const option = data.connections.find((candidate) => candidate.id === id);
    if (option) {
      return option.isActive ? option.name : `${option.name} (switched off)`;
    }
    if (id) {
      return 'A deleted connection';
    }
    return data.systemSenderDelivers ? "System's mail server" : 'Server log only';
  }

  protected select(key: string): void {
    const row = this.view()?.senders.find((candidate) => candidate.key === key);
    if (!row) {
      return;
    }
    this.selectedKey.set(key);
    this.ownSender.set(row.key === 'default' || row.hasOwn);
    this.outcome.set(null);
    this.form.reset({
      connectionId: row.connectionId ?? '',
      fromName: row.fromName ?? '',
      fromAddress: row.fromAddress ?? '',
      replyTo: row.replyTo ?? '',
      bcc: row.bcc ?? '',
      signature: row.signature ?? '',
      brandColor: row.brandColor ?? '',
      isPaused: row.isPaused,
    });
  }

  protected async save(row: EmailSenderItem): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.outcome.set(null);
    try {
      const value = this.form.getRawValue();
      const blank = (text: string): string | null => text.trim() || null;
      const view =
        row.key !== 'default' && !this.ownSender()
          ? await this.api.removeSender(row.key)
          : await this.api.saveSender(row.key, {
              connectionId: value.connectionId || null,
              fromName: blank(value.fromName),
              fromAddress: blank(value.fromAddress),
              replyTo: blank(value.replyTo),
              bcc: blank(value.bcc),
              signature: blank(value.signature),
              brandColor: blank(value.brandColor),
              isPaused: row.key !== 'default' && value.isPaused,
            });
      this.view.set(view);
      this.select(row.key);
      this.outcome.set({ succeeded: true, message: 'Saved. Emails from now on go this way.' });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The sender could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async test(row: EmailSenderItem): Promise<void> {
    this.testing.set(true);
    this.outcome.set(null);
    try {
      this.outcome.set(await this.api.testSender(row.key, this.testTo().trim()));
    } catch (error) {
      this.outcome.set({ succeeded: false, message: error instanceof ApiError ? error.message : 'The test could not be sent.' });
    } finally {
      this.testing.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.view.set(await this.api.senders());
      this.select(this.selectedKey());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Email senders could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
