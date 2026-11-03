import { DatePipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { DataHubApi } from '@gggs/shared/insight';
import type { AutomationDelivery, AutomationDeliveryStatus } from '@gggs/shared/models';
import type { BadgeVariant } from '@gggs/shared/ui';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const STATUS_VARIANTS: Readonly<Record<string, BadgeVariant>> = { succeeded: 'success', pending: 'primary', failed: 'warning', abandoned: 'danger' };

/** The delivery log: everything automations tried to send, how each attempt went, and Retry for what failed. */
@Component({
  selector: 'gggs-deliveries-page',
  imports: [DatePipe, KeyValuePipe, PageHeader, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Deliveries" subtitle="What automations sent. A failed send is tried again after 1, 5, 15 and 60 minutes, then left here for you." />

    <label class="field deliveries__filter">
      <span class="field__label">Status</span>
      <select (change)="filter($any($event.target).value)">
        <option value="">All</option>
        @for (choice of statuses; track choice) {
          <option [value]="choice" [selected]="choice === status()">{{ choice }}</option>
        }
      </select>
    </label>

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (deliveries().length === 0) {
      <gggs-empty-state icon="pi pi-send" title="Nothing here yet" description="When an automation acts, what it sent shows up here." />
    } @else {
      <div class="panel panel--flush table-wrap" tabindex="0" role="region" aria-label="Deliveries">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">When</th>
              <th scope="col">Automation</th>
              <th scope="col">Status</th>
              <th scope="col">Attempts</th>
              <th scope="col">Result</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (delivery of deliveries(); track delivery.id) {
              <tr>
                <td>{{ delivery.createdDate | date: 'd MMM, HH:mm' }}</td>
                <td>
                  <strong>{{ delivery.automationName }}</strong>
                  <details class="deliveries__sent">
                    <summary>What was sent</summary>
                    <dl>
                      @for (parameter of delivery.parameters | keyvalue; track parameter.key) {
                        <dt>{{ parameter.key }}</dt>
                        <dd>{{ parameter.value }}</dd>
                      }
                    </dl>
                  </details>
                </td>
                <td><gggs-status-badge [status]="delivery.status" [variants]="variants" /></td>
                <td>{{ delivery.attempts }}</td>
                <td>
                  {{ delivery.lastMessage ?? '—' }}
                  @if (delivery.nextAttemptAt && delivery.status === 'Failed') {
                    <span class="deliveries__next">Next try {{ delivery.nextAttemptAt | date: 'HH:mm' }}</span>
                  }
                </td>
                <td>
                  @if (canManage && delivery.status !== 'Succeeded') {
                    <button type="button" class="btn btn--link btn--sm" [disabled]="retrying() === delivery.id" [attr.aria-label]="'Retry delivery of ' + delivery.automationName" (click)="retry(delivery)">
                      {{ retrying() === delivery.id ? 'Retrying…' : 'Retry' }}
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .deliveries__filter { max-width: 14rem; }
    .deliveries__sent { font-size: var(--gggs-font-size-xs); color: var(--gggs-color-text-muted); }
    .deliveries__sent dl { display: grid; grid-template-columns: auto 1fr; gap: 0 var(--gggs-space-2); margin: var(--gggs-space-1) 0 0; }
    .deliveries__sent dt { font-weight: var(--gggs-font-weight-semibold); }
    .deliveries__sent dd { margin: 0; overflow-wrap: anywhere; }
    .deliveries__next { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
  `,
})
export class DeliveriesPage {
  private readonly api = inject(DataHubApi);
  private readonly automationId = inject(ActivatedRoute).snapshot.queryParamMap.get('automation');

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly variants = STATUS_VARIANTS;
  protected readonly statuses: readonly AutomationDeliveryStatus[] = ['Succeeded', 'Failed', 'Abandoned', 'Pending'];
  protected readonly status = signal<AutomationDeliveryStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly deliveries = signal<readonly AutomationDelivery[]>([]);
  protected readonly retrying = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected filter(value: string): void {
    this.status.set((value || null) as AutomationDeliveryStatus | null);
    void this.load();
  }

  protected async retry(delivery: AutomationDelivery): Promise<void> {
    this.retrying.set(delivery.id);
    this.error.set(null);
    try {
      const updated = await this.api.retryDelivery(delivery.id);
      this.deliveries.update((rows) => rows.map((row) => (row.id === updated.id ? updated : row)));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The delivery could not be retried.');
    } finally {
      this.retrying.set(null);
    }
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      this.deliveries.set(await this.api.automationDeliveries({ automationId: this.automationId, status: this.status() }));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Deliveries could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
