import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PlatformApi, type AuditItem } from '@gggs/platform/data-access';
import { PageHeader, Skeleton, dateTime } from '@gggs/shared/ui';
import { messageOf } from './platform-permissions';

/** Everything platform staff have done in the console, newest first. */
@Component({
  selector: 'gggs-console-activity-page',
  imports: [FormsModule, RouterLink, PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Activity log" subtitle="Every change made in this console: who, what, to which organisation and why." />

    <div class="filter-toolbar">
      <input class="search-input" type="search" placeholder="Filter by person, organisation or text" [ngModel]="filter()" (ngModelChange)="filter.set($event)" aria-label="Filter activity" />
    </div>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (items()) {
      <div class="table-wrap panel panel--flush">
        <table class="table">
          <thead><tr><th>When</th><th>Who</th><th>Organisation</th><th>What</th></tr></thead>
          <tbody>
            @for (item of shown(); track item.id) {
              <tr>
                <td class="nowrap">{{ when(item.occurredDate) }}</td>
                <td>{{ item.staffName }}</td>
                <td>
                  @if (item.tenantId) { <a [routerLink]="['/organisations', item.tenantId]">{{ item.tenantCode }}</a> } @else { <span class="muted">Platform</span> }
                </td>
                <td>{{ item.summary }}@if (item.reason) { <div class="muted text-sm">Reason: {{ item.reason }}</div> }</td>
              </tr>
            } @empty {
              <tr><td colspan="4" class="muted">Nothing recorded.</td></tr>
            }
          </tbody>
        </table>
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .search-input { min-width: 18rem; }
  `,
})
export class ConsoleActivityPage {
  private readonly api = inject(PlatformApi);

  protected readonly items = signal<AuditItem[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly filter = signal('');
  protected readonly shown = computed(() => {
    const term = this.filter().trim().toLowerCase();
    const items = this.items() ?? [];
    return term
      ? items.filter((item) => `${item.staffName} ${item.tenantCode ?? ''} ${item.summary} ${item.reason ?? ''}`.toLowerCase().includes(term))
      : items;
  });

  constructor() {
    this.api.audit(500).then(
      (items) => this.items.set(items),
      (error: unknown) => this.error.set(messageOf(error, 'The activity could not be loaded.')),
    );
  }

  protected when(value: string): string {
    return dateTime(value);
  }
}
