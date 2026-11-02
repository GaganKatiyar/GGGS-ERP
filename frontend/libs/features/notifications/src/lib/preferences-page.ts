import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { PreferencesApi } from '@gggs/shared/notifications';
import { EmptyState, PageHeader, Skeleton, Tabs, type TabItem } from '@gggs/shared/ui';
import { changedChoices, isSwitchedOff, moduleList, moduleName, toRows, type PreferenceRow } from './preferences-model';

/**
 * My preferences: which notifications reach me by email and in the app, and my time zone. The
 * events are the organisation's active notifications, in its own wording; account and legal
 * notices are always sent and shown locked.
 */
@Component({
  selector: 'gggs-preferences-page',
  imports: [PageHeader, Skeleton, EmptyState, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My preferences" subtitle="Choose how Nuts and Bolts keeps you informed." />
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    @if (tab() === 'notifications') {
      @if (loading()) {
        <gggs-skeleton [lines]="8" />
      } @else if (rows().length === 0) {
        <gggs-empty-state icon="pi pi-bell" title="No notifications to choose from" />
      } @else {
        <div class="prefs-tools">
          <label class="prefs-tools__search">
            <span class="sr-only">Search notifications</span>
            <input type="search" placeholder="Search notifications" [value]="query()" (input)="query.set($any($event.target).value)" />
          </label>
          <label class="prefs-tools__check"><input type="checkbox" [checked]="offOnly()" (change)="offOnly.set($any($event.target).checked)" /> Show switched-off only</label>
        </div>

        <div class="prefs">
          <nav class="prefs__modules" aria-label="Areas">
            <button type="button" class="prefs__module" [class.prefs__module--active]="!module()" [attr.aria-pressed]="!module()" (click)="module.set(null)">All</button>
            @for (entry of modules(); track entry.module) {
              <button type="button" class="prefs__module" [class.prefs__module--active]="module() === entry.module" [attr.aria-pressed]="module() === entry.module" (click)="module.set(entry.module)">
                {{ entry.label }}
                @if (entry.off) {
                  <span class="prefs__off" [attr.aria-label]="entry.off + ' switched off'">{{ entry.off }}</span>
                }
              </button>
            }
          </nav>

          <div class="prefs__table elevated">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">Notification</th>
                  <th scope="col" class="prefs__switch">Email</th>
                  <th scope="col" class="prefs__switch">In app</th>
                  @if (anySms()) {
                    <th scope="col" class="prefs__switch">Text</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of shown(); track row.code) {
                  <tr>
                    <td>
                      {{ row.name }}
                      <span class="prefs__area">{{ area(row.module) }}</span>
                      @if (row.isMandatory) {
                        <span class="prefs__locked"><i class="pi pi-lock" aria-hidden="true"></i> Always sent</span>
                      }
                    </td>
                    <td class="prefs__switch">
                      @if (row.templateSendsEmail) {
                        <input type="checkbox" role="switch" class="switch" [checked]="row.email" [disabled]="row.isMandatory" [attr.aria-label]="'Email for ' + row.name" (change)="set(row.code, 'email', $any($event.target).checked)" />
                      } @else {
                        <span class="muted" title="This notification is not sent by email">—</span>
                      }
                    </td>
                    <td class="prefs__switch">
                      @if (row.templateSendsInApp) {
                        <input type="checkbox" role="switch" class="switch" [checked]="row.inApp" [disabled]="row.isMandatory" [attr.aria-label]="'In-app for ' + row.name" (change)="set(row.code, 'inApp', $any($event.target).checked)" />
                      } @else {
                        <span class="muted" title="This notification is not shown in the app">—</span>
                      }
                    </td>
                    @if (anySms()) {
                      <td class="prefs__switch">
                        @if (row.templateSendsSms) {
                          <input type="checkbox" role="switch" class="switch" [checked]="row.sms" [disabled]="row.isMandatory" [attr.aria-label]="'Text message for ' + row.name" (change)="set(row.code, 'sms', $any($event.target).checked)" />
                        } @else {
                          <span class="muted" title="This notification is not sent by text message">—</span>
                        }
                      </td>
                    }
                  </tr>
                } @empty {
                  <tr><td [attr.colspan]="anySms() ? 4 : 3" class="muted">No notifications match.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </div>

        @if (changes().length) {
          <div class="save-bar elevated" role="region" aria-label="Unsaved changes">
            <span><strong>{{ changes().length }}</strong> unsaved {{ changes().length === 1 ? 'change' : 'changes' }}</span>
            <button type="button" class="btn" [disabled]="busy()" (click)="discard()">Discard</button>
            <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="save()">{{ busy() ? 'Saving…' : 'Save' }}</button>
          </div>
        }
      }
    } @else {
      <section class="elevated zone" aria-labelledby="zone-heading">
        <h2 id="zone-heading">Time zone</h2>
        <p class="muted">Your usual time zone, kept with your profile. Times on screen follow your device's clock, whatever is chosen here.</p>
        <label class="field">
          <span class="field__label">Your time zone</span>
          <select (change)="timeZone.set($any($event.target).value)">
            <option value="" [selected]="!timeZone()">Organisation default</option>
            @for (zone of zones; track zone) {
              <option [value]="zone" [selected]="zone === timeZone()">{{ zone.replace(underscores, ' ') }}</option>
            }
          </select>
        </label>
        <div>
          <button type="button" class="btn btn--primary" [disabled]="busy() || timeZone() === savedTimeZone()" (click)="saveZone()">Save</button>
        </div>
      </section>
    }

    @if (message()) {
      <p class="alert alert--success" role="status">{{ message() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-3); }
    .prefs-tools { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-4); }
    .prefs-tools__search input { min-width: min(22rem, 100%); }
    .prefs-tools__check { display: flex; align-items: center; gap: var(--gggs-space-2); }
    .prefs { display: flex; flex-wrap: wrap; align-items: flex-start; gap: var(--gggs-space-4); }
    .prefs__modules { display: flex; flex: 1 1 12rem; flex-direction: column; gap: var(--gggs-space-1); max-width: 16rem; }
    .prefs__module { display: flex; align-items: center; justify-content: space-between; min-height: 2.5rem; padding: 0 var(--gggs-space-3); border: 0; border-radius: var(--gggs-radius-md); background: transparent; color: var(--gggs-color-text); font: inherit; text-align: start; cursor: pointer; }
    .prefs__module:hover { background: var(--gggs-color-surface-hover); }
    .prefs__module--active { background: var(--gggs-color-primary-subtle); color: var(--gggs-color-primary); font-weight: var(--gggs-font-weight-semibold); }
    .prefs__off { min-width: 1.25rem; padding: 0 var(--gggs-space-1); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-sunken); color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); text-align: center; }
    .prefs__table { flex: 999 1 28rem; min-width: 0; overflow-x: auto; }
    .prefs__switch { width: 6rem; text-align: center; }
    .prefs__area { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .prefs__locked { display: inline-flex; align-items: center; gap: var(--gggs-space-1); margin-inline-start: var(--gggs-space-2); color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .switch { appearance: none; position: relative; width: 2.5rem; height: 1.4rem; margin: 0; border-radius: var(--gggs-radius-pill); background: var(--gggs-color-border-strong); cursor: pointer; transition: background-color var(--gggs-transition-fast); }
    .switch::after { content: ''; position: absolute; inset-block-start: 0.15rem; inset-inline-start: 0.15rem; width: 1.1rem; height: 1.1rem; border-radius: 50%; background: var(--gggs-color-surface-raised); box-shadow: var(--gggs-shadow-1); transition: transform var(--gggs-transition-fast); }
    .switch:checked { background: var(--gggs-color-primary); }
    .switch:checked::after { transform: translateX(1.1rem); }
    .switch:disabled { cursor: not-allowed; opacity: 0.6; }
    .switch:focus-visible { outline: 2px solid var(--gggs-color-focus); outline-offset: 2px; }
    .save-bar { position: sticky; inset-block-end: var(--gggs-space-4); display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); padding: var(--gggs-space-3) var(--gggs-space-4); box-shadow: var(--gggs-shadow-4); }
    .save-bar span { flex: 1; }
    .zone { display: flex; flex-direction: column; gap: var(--gggs-space-3); max-width: 36rem; padding: var(--gggs-space-5); }
    .zone h2 { margin: 0; font-size: var(--gggs-font-size-lg); }
    .zone p { margin: 0; }
  `,
})
export class PreferencesPage {
  private readonly api = inject(PreferencesApi);

  protected readonly tabs: readonly TabItem[] = [
    { key: 'notifications', label: 'Notifications' },
    { key: 'time', label: 'Date and time' },
  ];
  protected readonly tab = signal('notifications');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly rows = signal<readonly PreferenceRow[]>([]);
  protected readonly query = signal('');
  protected readonly offOnly = signal(false);
  protected readonly module = signal<string | null>(null);

  protected readonly timeZone = signal('');
  protected readonly savedTimeZone = signal('');
  protected readonly zones: readonly string[] = supportedZones();
  protected readonly area = moduleName;
  protected readonly underscores = /_/g;

  protected readonly modules = computed(() => moduleList(this.rows()));
  protected readonly changes = computed(() => changedChoices(this.rows()));
  protected readonly anySms = computed(() => this.rows().some((row) => row.templateSendsSms));
  protected readonly shown = computed(() => {
    const term = this.query().trim().toLowerCase();
    return this.rows().filter(
      (row) =>
        (!this.module() || row.module === this.module()) &&
        (!this.offOnly() || isSwitchedOff(row)) &&
        (!term || row.name.toLowerCase().includes(term) || moduleName(row.module).toLowerCase().includes(term)),
    );
  });

  constructor() {
    void this.api.notifications().then(
      (preferences) => this.rows.set(toRows(preferences)),
      () => this.error.set('Your notification preferences could not be loaded.'),
    ).finally(() => this.loading.set(false));
    void this.api.mine().then((mine) => {
      this.timeZone.set(mine.timeZone ?? '');
      this.savedTimeZone.set(mine.timeZone ?? '');
    }, () => undefined);
  }

  protected set(code: string, channel: 'email' | 'inApp' | 'sms', on: boolean): void {
    this.rows.set(this.rows().map((row) => (row.code === code ? { ...row, [channel]: on } : row)));
  }

  protected discard(): void {
    this.rows.set(this.rows().map((row) => ({ ...row, email: row.emailEnabled, inApp: row.inAppEnabled, sms: row.smsEnabled })));
  }

  protected async save(): Promise<void> {
    await this.act(async () => {
      await this.api.saveNotifications(this.changes());
      this.rows.set(toRows(await this.api.notifications()));
      return 'Saved. Notifications will follow your choices from now on.';
    });
  }

  protected async saveZone(): Promise<void> {
    await this.act(async () => {
      const saved = await this.api.save({ timeZone: this.timeZone() || undefined });
      this.savedTimeZone.set(saved.timeZone ?? '');
      return 'Saved. It applies across Nuts and Bolts within a few minutes.';
    });
  }

  private async act(call: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    this.error.set(null);
    try {
      this.message.set(await call());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That could not be saved. Try again.');
    } finally {
      this.busy.set(false);
    }
  }
}

/** Every IANA zone the browser knows; a short list where it cannot say. */
function supportedZones(): readonly string[] {
  try {
    return (Intl as unknown as { supportedValuesOf(key: string): string[] }).supportedValuesOf('timeZone');
  } catch {
    return ['Asia/Kolkata', 'Asia/Dubai', 'Asia/Singapore', 'Europe/London', 'Europe/Berlin', 'America/New_York', 'America/Los_Angeles', 'Australia/Sydney', 'UTC'];
  }
}
