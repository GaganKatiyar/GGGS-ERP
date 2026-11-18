import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import {
  PlatformApi,
  moduleName,
  statusTone,
  type AuditItem,
  type OrganisationAdministrator,
  type OrganisationDetail,
  type PlanView,
} from '@gggs/platform/data-access';
import { FormDialog, PageHeader, Skeleton, dateTime } from '@gggs/shared/ui';
import { OrganisationDatabaseTab } from './organisation-database-tab';
import { PLATFORM, messageOf } from './platform-permissions';

type Tab = 'overview' | 'subscription' | 'admins' | 'database' | 'activity';

/** One organisation: its state, subscription and modules, administrators and the history of changes. */
@Component({
  selector: 'gggs-console-organisation-page',
  imports: [FormsModule, RouterLink, PageHeader, Skeleton, FormDialog, OrganisationDatabaseTab],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/organisations" class="back"><i class="pi pi-arrow-left" aria-hidden="true"></i> Organisations</a>

    @if (org(); as o) {
      <gggs-page-header [title]="o.name" [subtitle]="o.code + ' · ' + (o.subscription?.planName ?? 'No plan') + ' · region ' + o.dataRegion.toUpperCase()" />

      @if (created()) {
        <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>Created. Its administrator has been emailed an invitation; it may take a minute while the organisation is set up.</span></p>
      }
      @if (notice()) {
        <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>{{ notice() }}</span></p>
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }

      <div class="toolbar">
        <span [class]="'badge badge--' + tone(o.status)">{{ o.status }}</span>
        @if (o.suspensionReason) { <span class="muted text-sm">{{ o.suspensionReason }}</span> }
        <span class="toolbar__spacer"></span>
        @if (o.status === 'Provisioning' && canManage) {
          <button type="button" class="btn btn--primary btn--sm" (click)="run(api.activate(o.id), 'Activated: its people can now sign in.')">Activate</button>
        }
        @if (o.status === 'Active' && canSuspend) {
          <button type="button" class="btn btn--secondary btn--sm" (click)="suspendOpen.set(true)">Suspend</button>
        }
        @if (o.status === 'Suspended' && canSuspend) {
          <button type="button" class="btn btn--primary btn--sm" (click)="run(api.reactivate(o.id), 'Reactivated: its people can sign in again.')">Reactivate</button>
        }
        @if ((o.status === 'Active' || o.status === 'Suspended') && canManage) {
          <button type="button" class="btn btn--danger btn--sm" (click)="offboardOpen.set(true)">Offboard…</button>
        }
      </div>

      <div class="tabs" role="tablist">
        @for (t of tabs; track t.key) {
          <button type="button" role="tab" class="tabs__tab" [class.tabs__tab--active]="tab() === t.key" [attr.aria-selected]="tab() === t.key" (click)="tab.set(t.key)">{{ t.label }}</button>
        }
      </div>

      @switch (tab()) {
        @case ('overview') {
          <div class="grid-2">
            <section class="panel">
              <header class="panel__header"><h2 class="panel__title">Usage</h2></header>
              @if (o.usage; as u) {
                <dl class="definition-list">
                  <dt>Employees</dt><dd>{{ u.employees }}@if (o.subscription?.seatLimit) { of {{ o.subscription!.seatLimit }} seats }</dd>
                  <dt>Users</dt><dd>{{ u.users }}</dd>
                  <dt>Signed in today</dt><dd>{{ u.signedInToday }}</dd>
                  <dt>Last sign-in</dt><dd>{{ u.lastSignIn ? when(u.lastSignIn) : 'Never' }}</dd>
                  <dt>Emails today</dt><dd>{{ u.emailsSentToday }} sent · {{ u.emailsFailedToday }} failed · {{ u.emailsLoggedToday }} only logged</dd>
                  <dt>Email goes through</dt><dd>{{ u.emailChannel ?? '—' }}</dd>
                  <dt>People sign in with</dt><dd>{{ u.signIn ?? '—' }}</dd>
                </dl>
                <p class="subtle text-xs">Measured {{ when(u.measuredDate) }}. Updated every hour.</p>
              } @else {
                <p class="muted">Not measured yet. Figures appear within the hour once the organisation is active.</p>
              }
            </section>
            <section class="panel">
              <header class="panel__header"><h2 class="panel__title">Details</h2></header>
              <dl class="definition-list">
                <dt>Legal name</dt><dd>{{ o.legalName ?? '—' }}</dd>
                <dt>Country</dt><dd>{{ o.countryCode }}</dd>
                <dt>Time zone</dt><dd>{{ o.defaultTimeZone }}</dd>
                <dt>Currency · locale</dt><dd>{{ o.defaultCurrency }} · {{ o.defaultLocale }}</dd>
                <dt>Hosting</dt><dd>{{ o.isolationTier === 'Pooled' ? 'Shared' : 'Dedicated' }}</dd>
                <dt>Created</dt><dd>{{ when(o.createdDate) }}</dd>
                @if (o.activatedDate) { <dt>Activated</dt><dd>{{ when(o.activatedDate) }}</dd> }
                @if (o.purgeEligibleDate) { <dt>Data deleted from</dt><dd>{{ when(o.purgeEligibleDate) }}</dd> }
              </dl>
            </section>
          </div>
        }
        @case ('subscription') {
          <section class="panel stack">
            <header class="panel__header"><h2 class="panel__title">Subscription</h2></header>
            <div class="form-grid">
              <label class="field">
                <span class="field__label">Plan</span>
                <select [(ngModel)]="sub.planCode" [disabled]="!canSubscribe">
                  @for (plan of plans(); track plan.code) {
                    <option [value]="plan.code">{{ plan.name }}{{ plan.maxEmployees ? ' (up to ' + plan.maxEmployees + ')' : '' }}</option>
                  }
                </select>
              </label>
              <label class="field">
                <span class="field__label">Seat limit</span>
                <input type="number" min="1" [(ngModel)]="sub.seatLimit" [disabled]="!canSubscribe" placeholder="The plan's limit" />
              </label>
              <label class="field">
                <span class="field__label">Status</span>
                <select [(ngModel)]="sub.paid" [disabled]="!canSubscribe || o.subscription?.status !== 'Trial'">
                  <option [ngValue]="false">Trial</option>
                  <option [ngValue]="true">Paid</option>
                </select>
                <span class="field__hint">Now {{ o.subscription?.status ?? '—' }}. A paid subscription cannot go back to a trial.</span>
              </label>
              @if (!sub.paid && o.subscription?.status === 'Trial') {
                <label class="field">
                  <span class="field__label">Trial ends on</span>
                  <input type="date" [(ngModel)]="sub.trialEndsOn" [disabled]="!canSubscribe" />
                </label>
              }
              <label class="field">
                <span class="field__label">Renews on</span>
                <input type="date" [(ngModel)]="sub.renewsOn" [disabled]="!canSubscribe" />
              </label>
              <label class="field">
                <span class="field__label">Billing reference</span>
                <input [(ngModel)]="sub.billingReference" maxlength="100" [disabled]="!canSubscribe" />
              </label>
            </div>
            @if (canSubscribe) {
              <div class="toolbar"><button type="button" class="btn btn--primary" [disabled]="busy()" (click)="saveSubscription(o.id)">Save subscription</button></div>
            }
          </section>

          <section class="panel stack">
            <header class="panel__header">
              <h2 class="panel__title">Modules</h2>
              <span class="muted text-sm">{{ enabledCount() }} of {{ o.modules.length }} on</span>
            </header>
            <div class="modules">
              @for (m of o.modules; track m.code) {
                <label class="module" [class.module--extra]="modules()[m.code] && !m.inPlan" [class.module--off]="!modules()[m.code] && m.inPlan">
                  <input type="checkbox" [checked]="modules()[m.code]" [disabled]="!canModules" (change)="toggleModule(m.code)" />
                  <span>{{ name(m.code) }}</span>
                  @if (modules()[m.code] && !m.inPlan) { <span class="badge badge--primary">Add-on</span> }
                  @if (!modules()[m.code] && m.inPlan) { <span class="badge badge--warning">Switched off</span> }
                </label>
              }
            </div>
            @if (canModules) {
              <div class="toolbar"><button type="button" class="btn btn--primary" [disabled]="busy()" (click)="saveModules(o.id)">Save modules</button></div>
            }
          </section>
        }
        @case ('admins') {
          <section class="panel stack">
            <header class="panel__header">
              <h2 class="panel__title">Administrators</h2>
              @if (canManage) {
                <button type="button" class="btn btn--secondary btn--sm" (click)="addOpen.set(true)"><i class="pi pi-user-plus" aria-hidden="true"></i> Add administrator</button>
              }
            </header>
            <p class="muted text-sm">People with the organisation's Administrator role. Platform staff can only re-send an invitation nobody has used yet, or invite another administrator; neither gives access to the organisation's data. How their people sign in (password rules, single sign-on) is the organisation's own setting.</p>
            @if (admins(); as list) {
              <div class="table-wrap">
                <table class="table">
                  <thead><tr><th>Name</th><th>Email</th><th>Status</th><th>Last sign-in</th><th></th></tr></thead>
                  <tbody>
                    @for (a of list; track a.userId) {
                      <tr>
                        <td>{{ a.displayName }}</td>
                        <td>{{ a.email }}</td>
                        <td>
                          {{ a.status === 'Invited' ? 'Invitation sent' : a.status }}
                          @if (a.invitationExpiresDate) { <div class="subtle text-xs">Link valid until {{ when(a.invitationExpiresDate) }}</div> }
                        </td>
                        <td>{{ a.lastLoginDate ? when(a.lastLoginDate) : 'Never' }}</td>
                        <td class="actions">
                          @if (a.status === 'Invited' && canManage) {
                            <button type="button" class="btn btn--ghost btn--sm" [disabled]="busy()" (click)="resend(o.id, a)">Re-send invitation</button>
                          }
                        </td>
                      </tr>
                    } @empty {
                      <tr><td colspan="5" class="muted">No administrator yet. The organisation may still be setting up.</td></tr>
                    }
                  </tbody>
                </table>
              </div>
            } @else {
              <gggs-skeleton [lines]="3" />
            }
          </section>
        }
        @case ('database') {
          <gggs-console-organisation-database-tab [tenantId]="o.id" />
        }
        @case ('activity') {
          <section class="panel">
            <header class="panel__header"><h2 class="panel__title">Activity</h2></header>
            @if (activity(); as items) {
              <ul class="list">
                @for (item of items; track item.id) {
                  <li class="entry">
                    <span class="subtle text-sm nowrap">{{ when(item.occurredDate) }}</span>
                    <span><strong>{{ item.staffName }}</strong> · {{ item.summary }}@if (item.reason) { <span class="muted"> Reason: {{ item.reason }}</span>}</span>
                  </li>
                } @empty {
                  <li class="muted">Nothing recorded yet.</li>
                }
              </ul>
            } @else {
              <gggs-skeleton [lines]="4" />
            }
          </section>
        }
      }

      <gggs-form-dialog [(visible)]="suspendOpen" title="Suspend organisation" saveLabel="Suspend" [danger]="true" [busy]="busy()" (save)="suspend(o.id)"
        description="Nobody in the organisation can sign in while it is suspended. Their data is kept and they can be reactivated at any time.">
        <label class="field">
          <span class="field__label">Reason *</span>
          <textarea rows="3" [(ngModel)]="reason" maxlength="500" placeholder="e.g. Invoice 1042 unpaid for 60 days"></textarea>
        </label>
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="offboardOpen" title="Offboard organisation" saveLabel="Start offboarding" [danger]="true" [busy]="busy()" (save)="offboard(o.id)"
        description="The organisation becomes read-only while their export is prepared. After the grace period their data may be deleted. This cannot be undone from the console.">
        <label class="field">
          <span class="field__label">Grace period (days)</span>
          <input type="number" min="7" max="365" [(ngModel)]="graceDays" />
        </label>
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="addOpen" title="Add administrator" saveLabel="Send invitation" [busy]="busy()" (save)="addAdmin(o.id)"
        description="They get an email with a link to choose a password and sign in as an administrator of this organisation.">
        <div class="stack">
          <label class="field">
            <span class="field__label">Email *</span>
            <input type="email" [(ngModel)]="newAdminEmail" maxlength="256" />
          </label>
          <label class="field">
            <span class="field__label">Name</span>
            <input [(ngModel)]="newAdminName" maxlength="200" />
          </label>
        </div>
      </gggs-form-dialog>
    } @else if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    } @else {
      <gggs-skeleton [lines]="10" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .back { display: inline-flex; gap: var(--gggs-space-2); align-items: center; font-size: var(--gggs-font-size-sm); }
    .tabs { display: flex; gap: var(--gggs-space-1); border-bottom: 1px solid var(--gggs-color-border); overflow-x: auto; }
    .tabs__tab { padding: var(--gggs-space-2) var(--gggs-space-3); border: 0; border-bottom: 2px solid transparent; background: none; font: inherit; color: var(--gggs-color-text-muted); cursor: pointer; white-space: nowrap; }
    .tabs__tab--active { border-bottom-color: #4f46e5; color: var(--gggs-color-text); font-weight: 600; }
    .modules { display: grid; grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)); gap: var(--gggs-space-2); }
    .module { display: flex; align-items: center; gap: var(--gggs-space-2); padding: var(--gggs-space-2) var(--gggs-space-3); border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-sm); }
    .module--extra { border-color: #a5b4fc; }
    .module--off { border-style: dashed; }
    .entry { display: grid; grid-template-columns: 10rem 1fr; gap: var(--gggs-space-3); }
  `,
})
export class ConsoleOrganisationPage {
  protected readonly api = inject(PlatformApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();
  readonly created = input<string | undefined>(undefined);

  protected readonly canManage = this.permissions.has(PLATFORM.tenantManage);
  protected readonly canSuspend = this.permissions.has(PLATFORM.tenantSuspend);
  protected readonly canSubscribe = this.permissions.has(PLATFORM.subscriptionManage);
  protected readonly canModules = this.permissions.has(PLATFORM.featureManage);

  protected readonly tabs: readonly { key: Tab; label: string }[] = [
    { key: 'overview', label: 'Overview' },
    { key: 'subscription', label: 'Subscription & modules' },
    { key: 'admins', label: 'Administrators' },
    ...(this.permissions.has(PLATFORM.databaseManage) ? [{ key: 'database' as Tab, label: 'Database' }] : []),
    { key: 'activity', label: 'Activity' },
  ];
  protected readonly tab = signal<Tab>('overview');
  protected readonly org = signal<OrganisationDetail | null>(null);
  protected readonly plans = signal<PlanView[]>([]);
  protected readonly admins = signal<OrganisationAdministrator[] | null>(null);
  protected readonly activity = signal<AuditItem[] | null>(null);
  protected readonly modules = signal<Record<string, boolean>>({});
  protected readonly enabledCount = computed(() => Object.values(this.modules()).filter(Boolean).length);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly suspendOpen = signal(false);
  protected readonly offboardOpen = signal(false);
  protected readonly addOpen = signal(false);

  protected reason = '';
  protected graceDays = 30;
  protected newAdminEmail = '';
  protected newAdminName = '';
  protected sub = { planCode: '', seatLimit: null as number | null, paid: false, trialEndsOn: null as string | null, renewsOn: null as string | null, billingReference: null as string | null };

  constructor() {
    this.api.plans().then((view) => this.plans.set(view.plans.filter((plan) => plan.isActive)), () => undefined);
    effect(() => {
      void this.load(this.id());
    });
    effect(() => {
      const tab = this.tab();
      const id = this.id();
      if (tab === 'admins' && this.admins() === null) void this.loadAdmins(id);
      if (tab === 'activity' && this.activity() === null) void this.loadActivity(id);
    });
  }

  protected tone(status: string): string {
    return statusTone(status);
  }

  protected name(code: string): string {
    return moduleName(code);
  }

  protected when(value: string): string {
    return dateTime(value);
  }

  protected toggleModule(code: string): void {
    this.modules.update((current) => ({ ...current, [code]: !current[code] }));
  }

  protected async run(work: Promise<unknown>, message: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await work;
      this.notice.set(message);
      this.activity.set(null);
      await this.load(this.id());
    } catch (error) {
      this.error.set(messageOf(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async suspend(id: string): Promise<void> {
    if (!this.reason.trim()) {
      this.error.set('Give a reason for suspending.');
      return;
    }
    this.suspendOpen.set(false);
    await this.run(this.api.suspend(id, this.reason.trim()), 'Suspended: nobody in the organisation can sign in.');
    this.reason = '';
  }

  protected async offboard(id: string): Promise<void> {
    this.offboardOpen.set(false);
    await this.run(this.api.offboard(id, this.graceDays), 'Offboarding started.');
  }

  protected async saveSubscription(id: string): Promise<void> {
    await this.run(
      this.api.updateSubscription(id, {
        planCode: this.sub.planCode,
        seatLimit: this.sub.seatLimit || null,
        paid: this.sub.paid,
        trialEndsOn: this.sub.paid ? null : this.sub.trialEndsOn || null,
        renewsOn: this.sub.renewsOn || null,
        billingReference: this.sub.billingReference?.trim() || null,
      }),
      'Subscription saved. The organisation sees the change on its next page load.',
    );
  }

  protected async saveModules(id: string): Promise<void> {
    const enabled = Object.entries(this.modules())
      .filter(([, on]) => on)
      .map(([code]) => code);
    await this.run(this.api.updateModules(id, enabled), 'Modules saved. The organisation’s menu changes on its next page load.');
  }

  protected async resend(id: string, admin: OrganisationAdministrator): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const sent = await this.api.resendAdministratorInvitation(id, admin.email);
      this.notice.set(`A new invitation was emailed to ${sent.email}. The link works until ${this.when(sent.expiresDate)}.`);
      await this.loadAdmins(id);
    } catch (error) {
      this.error.set(messageOf(error, 'The invitation could not be sent.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async addAdmin(id: string): Promise<void> {
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.newAdminEmail.trim())) {
      this.error.set('Enter the new administrator’s email address.');
      return;
    }
    this.addOpen.set(false);
    this.busy.set(true);
    this.error.set(null);
    try {
      const sent = await this.api.addAdministrator(id, this.newAdminEmail.trim(), this.newAdminName.trim() || null);
      this.notice.set(`${sent.email} was invited as an administrator.`);
      this.newAdminEmail = '';
      this.newAdminName = '';
      await this.loadAdmins(id);
    } catch (error) {
      this.error.set(messageOf(error, 'The administrator could not be added.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    try {
      const org = await this.api.organisation(id);
      this.org.set(org);
      this.modules.set(Object.fromEntries(org.modules.map((m) => [m.code, m.enabled])));
      const s = org.subscription;
      this.sub = {
        planCode: s?.planCode ?? '',
        seatLimit: s?.seatLimit ?? null,
        paid: s ? s.status !== 'Trial' : false,
        trialEndsOn: s?.trialEndsOn ?? null,
        renewsOn: s?.renewsOn ?? null,
        billingReference: s?.billingReference ?? null,
      };
    } catch (error) {
      this.error.set(messageOf(error, 'The organisation could not be loaded.'));
    }
  }

  private async loadAdmins(id: string): Promise<void> {
    try {
      this.admins.set(await this.api.administrators(id));
    } catch (error) {
      this.admins.set([]);
      this.error.set(messageOf(error, 'The administrators could not be loaded.'));
    }
  }

  private async loadActivity(id: string): Promise<void> {
    try {
      this.activity.set(await this.api.activity(id));
    } catch (error) {
      this.activity.set([]);
      this.error.set(messageOf(error, 'The activity could not be loaded.'));
    }
  }
}
