import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { AdminApi } from '@gggs/shared/identity';
import { DataHubApi } from '@gggs/shared/insight';
import type { ApiKeyItem, Automation, AutomationDelivery, DirectoryAccount, IntegrationAdapter, IntegrationConnection } from '@gggs/shared/models';
import { localIsoDate, PageHeader } from '@gggs/shared/ui';
import { accountsNeedingAttention, expiringSoon, failedRecently, healthOf } from './studio-model';

/** Studio home: what is connected and how it is doing, and the keys outside systems use. */
@Component({
  selector: 'gggs-studio-overview-page',
  imports: [RouterLink, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Studio" subtitle="Connect Nuts and Bolts to the tools your organisation already uses." />
    <div class="overview">
      @if (canSeeConnectors) {
        <a class="overview__card elevated" routerLink="connectors">
          <span class="overview__tile" aria-hidden="true"><i class="pi pi-link"></i></span>
          <strong>Connectors</strong>
          <span class="overview__figure">{{ connections().length }} connected</span>
          <span class="muted">
            @if (failing()) {
              <span class="overview__warn">{{ failing() }} failing</span> ·
            }
            {{ adapters().length }} available in the catalogue
          </span>
        </a>
      }
      @if (canSeeConnectors) {
        <a class="overview__card elevated" routerLink="automations">
          <span class="overview__tile" aria-hidden="true"><i class="pi pi-sitemap"></i></span>
          <strong>Automations</strong>
          <span class="overview__figure">{{ activeAutomations() }} active</span>
          <span class="muted">When something happens in Nuts and Bolts, act in another tool</span>
        </a>
        <a class="overview__card elevated" routerLink="deliveries">
          <span class="overview__tile" aria-hidden="true"><i class="pi pi-send"></i></span>
          <strong>Deliveries</strong>
          <span class="overview__figure">{{ failedDeliveries().length }} failed</span>
          <span class="muted">In the last 7 days; open to see what was sent and retry</span>
        </a>
      }
      @if (canSeeConnectors) {
        <a class="overview__card elevated" routerLink="accounts">
          <span class="overview__tile" aria-hidden="true"><i class="pi pi-users"></i></span>
          <strong>Accounts</strong>
          <span class="overview__figure">{{ directoryAccounts().length }} managed</span>
          <span class="muted">
            @if (accountsAttention().length) {
              <span class="overview__warn">{{ accountsAttention().length }} {{ accountsAttention().length === 1 ? 'needs' : 'need' }} you</span>
            } @else {
              Work accounts for joiners and leavers
            }
          </span>
        </a>
      }
      @if (canSeeKeys) {
        <a class="overview__card elevated" routerLink="api-keys">
          <span class="overview__tile" aria-hidden="true"><i class="pi pi-key"></i></span>
          <strong>API keys</strong>
          <span class="overview__figure">{{ activeKeys() }} active</span>
          <span class="muted">
            @if (expiring().length) {
              <span class="overview__warn">{{ expiring().length }} expiring within 30 days</span>
            } @else {
              For your own systems to call Nuts and Bolts
            }
          </span>
        </a>
      }
    </div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .overview { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(18rem, 100%), 1fr)); gap: var(--gggs-space-4); }
    .overview__card { display: flex; flex-direction: column; gap: var(--gggs-space-2); padding: var(--gggs-space-5); color: inherit; text-decoration: none; transition: transform var(--gggs-transition), box-shadow var(--gggs-transition); }
    .overview__card:hover { transform: translateY(var(--gggs-lift)); box-shadow: var(--gggs-shadow-3); }
    .overview__tile { display: grid; place-items: center; width: 2.5rem; height: 2.5rem; border-radius: var(--gggs-radius-md); background: var(--gggs-gradient-tile); color: var(--gggs-color-primary); }
    .overview__figure { font-family: var(--gggs-font-family-display); font-size: var(--gggs-font-size-xl); font-weight: var(--gggs-font-weight-bold); }
    .overview__warn { color: var(--gggs-color-danger); font-weight: var(--gggs-font-weight-semibold); }
  `,
})
export class StudioOverviewPage {
  private readonly permissions = inject(PermissionStore);

  protected readonly canSeeConnectors = this.permissions.has('datahub.integration.view');
  protected readonly canSeeKeys = this.permissions.has('admin.user.view');
  protected readonly adapters = signal<readonly IntegrationAdapter[]>([]);
  protected readonly connections = signal<readonly IntegrationConnection[]>([]);
  protected readonly keys = signal<readonly ApiKeyItem[]>([]);
  protected readonly automations = signal<readonly Automation[]>([]);
  protected readonly deliveries = signal<readonly AutomationDelivery[]>([]);
  protected readonly directoryAccounts = signal<readonly DirectoryAccount[]>([]);

  protected readonly failing = computed(() => this.connections().filter((connection) => healthOf(connection) === 'Failing').length);
  protected readonly activeAutomations = computed(() => this.automations().filter((automation) => automation.isActive).length);
  protected readonly failedDeliveries = computed(() => failedRecently(this.deliveries(), new Date()));
  protected readonly accountsAttention = computed(() => accountsNeedingAttention(this.directoryAccounts()));
  protected readonly activeKeys = computed(() => this.keys().filter((key) => key.status === 'Active').length);
  protected readonly expiring = computed(() => expiringSoon(this.keys(), localIsoDate()));

  constructor() {
    if (this.canSeeConnectors) {
      const hub = inject(DataHubApi);
      hub.adapters().then((adapters) => this.adapters.set(adapters), () => undefined);
      hub.connections().then((connections) => this.connections.set(connections), () => undefined);
      hub.automations().then((automations) => this.automations.set(automations), () => undefined);
      hub.automationDeliveries().then((deliveries) => this.deliveries.set(deliveries), () => undefined);
      hub.directoryAccounts().then((accounts) => this.directoryAccounts.set(accounts), () => undefined);
    }
    if (this.canSeeKeys) {
      inject(AdminApi).apiKeys().then((keys) => this.keys.set(keys), () => undefined);
    }
  }
}
