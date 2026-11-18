import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import type { EmailLogItem, EmailLogResult, EmailLogSettings, EmailSenderItem, EmailStatus } from '@gggs/shared/models';
import { NotificationsApi } from '@gggs/shared/notifications';
import { EmptyState, PageHeader, Skeleton } from '@gggs/shared/ui';
import { EMAIL_STATUSES, emailStatusLabel, totalOf } from './email-model';

const PAGE_SIZE = 25;

/**
 * Every email Nuts and Bolts tried to send, who it went out as and what happened. Bodies are never shown:
 * an invitation's holds a sign-in link. A failed, logged or paused email can be sent again, as its sender
 * is set now.
 */
@Component({
  selector: 'gggs-email-log-page',
  imports: [DatePipe, RouterLink, PageHeader, Skeleton, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Email log" subtitle="Every email Nuts and Bolts tried to send, who it came from, and what happened to it.">
      @if (canManage) {
        <button type="button" class="btn" [attr.aria-expanded]="editing()" aria-controls="log-settings" (click)="toggleSettings()"><i class="pi pi-cog" aria-hidden="true"></i> Log settings</button>
      }
    </gggs-page-header>

    @if (settings(); as current) {
      <p class="muted log__keep"><i class="pi pi-history" aria-hidden="true"></i> {{ keepText(current) }}</p>
    }

    @if (editing() && settings(); as current) {
      <form id="log-settings" class="panel log__settings" (submit)="$event.preventDefault(); saveSettings(sent.value, problems.value, alert.checked)">
        <label class="field">
          <span class="field__label">Keep sent emails</span>
          <select #sent>
            @for (days of current.sentChoices; track days) {
              <option [value]="days" [selected]="days === current.keepSentDays">{{ days === 0 ? 'Don’t keep (failed only)' : daysLabel(days) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Keep failed and unsent emails</span>
          <select #problems>
            @for (days of current.problemChoices; track days) {
              <option [value]="days" [selected]="days === current.keepProblemDays">{{ daysLabel(days) }}</option>
            }
          </select>
        </label>
        <label class="log__check"><input #alert type="checkbox" [checked]="current.alertOnFailure" /> Tell mail admins in the app when emails fail (at most once an hour)</label>
        <p class="muted log__small">Old emails are removed every night. Sent emails keep who, what and when, but not the message itself.</p>
        <div class="log__actions">
          <button type="submit" class="btn btn--primary" [disabled]="savingSettings()">{{ savingSettings() ? 'Saving…' : 'Save' }}</button>
          <button type="button" class="btn" (click)="toggleSettings()">Cancel</button>
        </div>
      </form>
    }

    <div class="log__filters">
      <div class="log__statuses" role="group" aria-label="Status">
        <button type="button" class="log__pill" [class.log__pill--on]="!status()" [attr.aria-pressed]="!status()" (click)="setStatus(null)">All · {{ total() }}</button>
        @for (entry of statuses; track entry.status) {
          <button type="button" class="log__pill" [class.log__pill--on]="status() === entry.status" [attr.aria-pressed]="status() === entry.status" (click)="setStatus(entry.status)">
            {{ entry.label }} · {{ data()?.counts?.[entry.status] ?? 0 }}
          </button>
        }
      </div>
      <label class="field log__field">
        <span class="field__label">Module</span>
        <select (change)="setModule($any($event.target).value)">
          <option value="">All modules</option>
          @for (module of modules(); track module.key) {
            <option [value]="module.key" [selected]="module.key === module_()">{{ module.name }}</option>
          }
        </select>
      </label>
      <form class="field log__field" (submit)="$event.preventDefault(); setSearch(search.value)">
        <label class="field__label" for="log-search">Search</label>
        <input #search id="log-search" type="search" placeholder="Recipient or subject" (search)="setSearch(search.value)" />
      </form>
    </div>

    @if (showLoggedHelp()) {
      <p class="alert alert--info" role="note">
        <i class="pi pi-info-circle" aria-hidden="true"></i>
        <span><strong>Logged only</strong> means the email was written to the server log and not sent, because no working mail connection was set for its sender. Fix it under <a routerLink="../email-senders">Email senders</a>, then use <strong>Resend</strong>.</span>
      </p>
    }

    @if (canManage && status() === 'Failed' && (data()?.counts?.Failed ?? 0) > 0) {
      <p class="alert alert--info log__bulk" role="note">
        <span>Fixed the cause? Send every failed email again{{ module_() ? ' in this module' : '' }} with today's sender settings.</span>
        <button type="button" class="btn btn--sm" [disabled]="resendingAll()" (click)="resendAll()">{{ resendingAll() ? 'Sending…' : 'Resend all failed' }}</button>
      </p>
    }

    @if (notice()) {
      <p class="alert alert--success" role="status"><i class="pi pi-check-circle" aria-hidden="true"></i><span>{{ notice() }}</span></p>
    }

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (loading() && !data()) {
      <gggs-skeleton [lines]="8" />
    } @else if (!rows().length) {
      <gggs-empty-state icon="pi pi-envelope" title="No emails here" description="Nothing matches these filters yet." />
    } @else {
      <section class="panel log__panel">
        <div class="log__scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">When</th>
                <th scope="col">To</th>
                <th scope="col">Subject</th>
                <th scope="col">Module · sent as</th>
                <th scope="col">Status</th>
                <th scope="col"><span class="log__hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.id) {
                <tr>
                  <td class="log__nowrap">{{ row.createdDate | date: 'd MMM, h:mm a' }}</td>
                  <td>{{ row.to }}</td>
                  <td>{{ row.subject }}</td>
                  <td>
                    {{ row.moduleName }}
                    @if (row.sentAs) {
                      <span class="muted log__small">{{ row.sentAs }}</span>
                    }
                  </td>
                  <td>
                    <span class="log__chip" [attr.data-status]="row.status">{{ label(row.status) }}</span>
                    @if (row.problem) {
                      <span class="log__small log__reason">{{ row.problem }}</span>
                      @if (row.nextAttemptAt) {
                        <span class="muted log__small log__reason">Trying again at {{ row.nextAttemptAt | date: 'h:mm a' }}.</span>
                      } @else if (row.fix) {
                        <span class="muted log__small log__reason">{{ row.fix }}</span>
                      }
                      @if (row.reason && row.failureKind !== 'Other') {
                        <details class="log__small log__reason"><summary>Details</summary>{{ row.reason }}</details>
                      }
                    } @else if (row.reason) {
                      <span class="muted log__small log__reason">{{ row.reason }}</span>
                    }
                  </td>
                  <td class="log__nowrap">
                    @if (canManage && row.canResend && !row.nextAttemptAt) {
                      <button type="button" class="btn btn--sm" [disabled]="resending() === row.id" (click)="resend(row)">{{ resending() === row.id ? 'Sending…' : 'Resend' }}</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <div class="log__pager">
        <span class="muted">Page {{ page() }} of {{ pages() }}</span>
        <button type="button" class="btn btn--sm" [disabled]="page() <= 1 || loading()" (click)="go(page() - 1)">Previous</button>
        <button type="button" class="btn btn--sm" [disabled]="page() >= pages() || loading()" (click)="go(page() + 1)">Next</button>
      </div>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .log__filters { display: flex; flex-wrap: wrap; gap: var(--gggs-space-3); align-items: flex-end; }
    .log__statuses { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); flex: 1 1 28rem; }
    .log__pill { min-height: 2.75rem; padding: 0 var(--gggs-space-4); border-radius: 999px; border: 1px solid var(--gggs-color-border); background: var(--gggs-color-surface); color: var(--gggs-color-text); font: inherit; cursor: pointer; }
    .log__pill--on { background: var(--gggs-color-text); border-color: var(--gggs-color-text); color: var(--gggs-color-surface); font-weight: var(--gggs-font-weight-semibold); }
    .log__field { flex: 0 1 14rem; margin: 0; }
    .log__panel { padding: 0; overflow: hidden; }
    .log__scroll { overflow-x: auto; }
    .log__small { display: block; font-size: var(--gggs-font-size-sm); }
    .log__reason { max-width: 18rem; }
    .log__nowrap { white-space: nowrap; }
    .log__chip { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); white-space: nowrap; background: #f1f5f9; color: #334155; }
    .log__chip[data-status='Sent'] { background: #dcfce7; color: #166534; }
    .log__chip[data-status='Logged'] { background: #dbeafe; color: #1e40af; }
    .log__chip[data-status='Failed'] { background: #fee2e2; color: #991b1b; }
    .log__chip[data-status='Paused'] { background: #fef3c7; color: #92400e; }
    .log__keep { margin: calc(var(--gggs-space-2) * -1) 0 0; display: flex; gap: var(--gggs-space-2); align-items: center; }
    .log__settings { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: var(--gggs-space-3); align-items: end; }
    .log__settings .field { margin: 0; }
    .log__check, .log__settings > p, .log__actions { grid-column: 1 / -1; }
    .log__check { display: flex; gap: var(--gggs-space-2); align-items: center; min-height: 2.75rem; }
    .log__actions { display: flex; gap: var(--gggs-space-2); }
    .log__bulk { display: flex; flex-wrap: wrap; gap: var(--gggs-space-3); align-items: center; justify-content: space-between; }
    .log__pager { display: flex; gap: var(--gggs-space-2); align-items: center; justify-content: flex-end; }
    .log__hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
  `,
})
export class EmailLogPage {
  private readonly api = inject(NotificationsApi);

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly statuses = EMAIL_STATUSES;
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly data = signal<EmailLogResult | null>(null);
  protected readonly status = signal<EmailStatus | null>(null);
  protected readonly module_ = signal('');
  protected readonly searchTerm = signal('');
  protected readonly page = signal(1);
  protected readonly resending = signal<string | null>(null);
  protected readonly modules = signal<readonly EmailSenderItem[]>([]);
  protected readonly settings = signal<EmailLogSettings | null>(null);
  protected readonly editing = signal(false);
  protected readonly savingSettings = signal(false);
  protected readonly resendingAll = signal(false);
  protected readonly notice = signal<string | null>(null);

  protected readonly rows = computed<readonly EmailLogItem[]>(() => this.data()?.page.items ?? []);
  protected readonly total = computed(() => totalOf(this.data()?.counts ?? {}));
  protected readonly pages = computed(() => Math.max(1, this.data()?.page.totalPages ?? 1));
  protected readonly showLoggedHelp = computed(() => (this.data()?.counts.Logged ?? 0) > 0 && (this.status() === null || this.status() === 'Logged'));

  constructor() {
    // The failure notice links here with ?status=Failed.
    const asked = inject(ActivatedRoute).snapshot.queryParamMap.get('status');
    const known = EMAIL_STATUSES.find((entry) => entry.status === asked);
    if (known) {
      this.status.set(known.status);
    }

    this.api.senders().then(
      (view) => this.modules.set(view.senders.filter((row) => row.key !== 'default')),
      () => undefined,
    );
    this.api.emailLogSettings().then(
      (settings) => this.settings.set(settings),
      () => undefined,
    );
    void this.load();
  }

  protected label(status: EmailStatus): string {
    return emailStatusLabel(status);
  }

  protected daysLabel(days: number): string {
    return days === 365 ? '1 year' : `${days} days`;
  }

  protected keepText(settings: EmailLogSettings): string {
    const sent = settings.keepSentDays === 0 ? 'Sent emails are removed the night after they go' : `Sent emails are removed after ${this.daysLabel(settings.keepSentDays)}`;
    return `${sent}; failed and unsent ones are kept for ${this.daysLabel(settings.keepProblemDays)}.`;
  }

  protected toggleSettings(): void {
    this.editing.update((open) => !open);
  }

  protected async saveSettings(sent: string, problems: string, alertOnFailure: boolean): Promise<void> {
    this.savingSettings.set(true);
    this.error.set(null);
    try {
      this.settings.set(await this.api.saveEmailLogSettings({ keepSentDays: Number(sent), keepProblemDays: Number(problems), alertOnFailure }));
      this.editing.set(false);
      this.notice.set('Log settings saved.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The log settings could not be saved.');
    } finally {
      this.savingSettings.set(false);
    }
  }

  protected async resendAll(): Promise<void> {
    this.resendingAll.set(true);
    this.error.set(null);
    try {
      const result = await this.api.resendFailedEmails(this.module_() || undefined);
      const skipped = result.skipped ? ` ${result.skipped} held a one-time link and can't be sent again.` : '';
      this.notice.set(`${result.requeued} ${result.requeued === 1 ? 'email is' : 'emails are'} on the way again.${skipped}`);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The failed emails could not be sent again.');
    } finally {
      this.resendingAll.set(false);
    }
  }

  protected setStatus(status: EmailStatus | null): void {
    this.status.set(status);
    this.go(1);
  }

  protected setModule(key: string): void {
    this.module_.set(key);
    this.go(1);
  }

  protected setSearch(term: string): void {
    this.searchTerm.set(term);
    this.go(1);
  }

  protected go(page: number): void {
    this.page.set(page);
    this.notice.set(null);
    void this.load();
  }

  protected async resend(row: EmailLogItem): Promise<void> {
    this.resending.set(row.id);
    this.error.set(null);
    try {
      await this.api.resendEmail(row.id);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The email could not be sent again.');
    } finally {
      this.resending.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.data.set(
        await this.api.emailLog({ status: this.status() ?? undefined, module: this.module_() || undefined, search: this.searchTerm() || undefined, page: this.page(), pageSize: PAGE_SIZE }),
      );
      this.error.set(null);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The email log could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
