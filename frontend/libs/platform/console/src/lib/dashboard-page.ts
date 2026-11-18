import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlatformApi, type DashboardView } from '@gggs/platform/data-access';
import { KpiCard, PageHeader, Skeleton, dateTime } from '@gggs/shared/ui';
import { messageOf } from './platform-permissions';

/** The platform at a glance: organisations, usage today, what needs a look, and what is coming up. */
@Component({
  selector: 'gggs-console-dashboard-page',
  imports: [RouterLink, PageHeader, KpiCard, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Dashboard" subtitle="Every organisation on the platform, and what needs attention today." />

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (view(); as v) {
      <div class="kpi-row">
        <gggs-kpi-card label="Active organisations" [value]="count(v.byStatus, 'Active')" icon="pi pi-building" [hint]="totalOrganisations() + ' in all'" />
        <gggs-kpi-card label="Employees" [value]="v.employees" icon="pi pi-users" hint="Across active organisations" />
        <gggs-kpi-card label="Signed in today" [value]="v.signedInToday" icon="pi pi-sign-in" />
        <gggs-kpi-card label="Emails today" [value]="v.emailsSentToday" icon="pi pi-envelope" [hint]="v.emailsFailedToday + ' failed'" />
        <gggs-kpi-card label="Failed jobs" [value]="v.failedJobs" icon="pi pi-exclamation-triangle" [hint]="v.failedJobs ? 'See System health' : 'All clear'" />
      </div>

      <div class="grid-2">
        <section class="panel">
          <header class="panel__header"><h2 class="panel__title">Needs attention</h2></header>
          @if (v.attention.length === 0) {
            <p class="muted">Nothing needs a look right now.</p>
          } @else {
            <ul class="list attention">
              @for (item of v.attention; track $index) {
                <li>
                  <span class="badge" [class.badge--danger]="item.kind === 'Suspended' || item.kind === 'System' || item.kind === 'Email'" [class.badge--warning]="item.kind === 'Trial' || item.kind === 'Offboarding'" [class.badge--info]="item.kind === 'Setting up'">{{ item.kind }}</span>
                  @if (item.tenantId) {
                    <a [routerLink]="['/organisations', item.tenantId]">{{ item.text }}</a>
                  } @else {
                    <a routerLink="/health">{{ item.text }}</a>
                  }
                </li>
              }
            </ul>
          }
        </section>

        <section class="panel">
          <header class="panel__header"><h2 class="panel__title">Coming up in 60 days</h2></header>
          @if (v.upcoming.length === 0) {
            <p class="muted">No trials ending or renewals due.</p>
          } @else {
            <div class="table-wrap">
              <table class="table table--compact">
                <thead><tr><th>Organisation</th><th>What</th><th>When</th><th>Plan</th></tr></thead>
                <tbody>
                  @for (item of v.upcoming; track item.tenantId + item.what) {
                    <tr>
                      <td><a [routerLink]="['/organisations', item.tenantId]">{{ item.name }}</a></td>
                      <td>{{ item.what }}</td>
                      <td class="nowrap">{{ day(item.when) }}</td>
                      <td>{{ item.planName ?? '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>

        <section class="panel">
          <header class="panel__header"><h2 class="panel__title">Organisations by plan</h2></header>
          <ul class="bars">
            @for (row of v.byPlan; track row.key) {
              <li>
                <span class="bars__label">{{ row.key }}</span>
                <span class="bars__track"><span class="bars__fill" [style.width.%]="share(row.count, maxPlan())"></span></span>
                <span class="num">{{ row.count }}</span>
              </li>
            } @empty {
              <li class="muted">No organisations yet.</li>
            }
          </ul>
          <h3 class="sub">By status</h3>
          <p class="statuses">
            @for (row of v.byStatus; track row.key) {
              <span class="badge">{{ row.key }}: {{ row.count }}</span>
            }
          </p>
        </section>

        <section class="panel">
          <header class="panel__header"><h2 class="panel__title">New organisations per month</h2></header>
          <ul class="bars">
            @for (row of v.newPerMonth; track row.month) {
              <li>
                <span class="bars__label">{{ row.month }}</span>
                <span class="bars__track"><span class="bars__fill" [style.width.%]="share(row.count, maxMonth())"></span></span>
                <span class="num">{{ row.count }}</span>
              </li>
            }
          </ul>
        </section>
      </div>

      <section class="panel">
        <header class="panel__header">
          <h2 class="panel__title">Recent activity</h2>
          <a routerLink="/activity" class="btn btn--ghost btn--sm">All activity</a>
        </header>
        @if (v.recent.length === 0) {
          <p class="muted">Nothing has been done in the console yet.</p>
        } @else {
          <ul class="list">
            @for (item of v.recent; track item.id) {
              <li class="recent">
                <span class="subtle text-sm nowrap">{{ when(item.occurredDate) }}</span>
                <span><strong>{{ item.staffName }}</strong> · {{ item.summary }}@if (item.tenantCode) { <span class="muted"> ({{ item.tenantCode }})</span>}</span>
              </li>
            }
          </ul>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .attention li { display: flex; gap: var(--gggs-space-2); align-items: baseline; }
    .bars { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--gggs-space-2); }
    .bars li { display: grid; grid-template-columns: 8rem 1fr 2.5rem; gap: var(--gggs-space-2); align-items: center; }
    .bars__track { height: 0.5rem; border-radius: 999px; background: var(--gggs-color-surface-hover); overflow: hidden; }
    .bars__fill { display: block; height: 100%; border-radius: 999px; background: #4f46e5; }
    .bars__label { font-size: var(--gggs-font-size-sm); }
    .sub { margin: var(--gggs-space-4) 0 var(--gggs-space-2); font-size: var(--gggs-font-size-sm); }
    .statuses { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); margin: 0; }
    .recent { display: grid; grid-template-columns: 9rem 1fr; gap: var(--gggs-space-3); }
  `,
})
export class ConsoleDashboardPage {
  private readonly api = inject(PlatformApi);

  protected readonly view = signal<DashboardView | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly totalOrganisations = computed(() => (this.view()?.byStatus ?? []).reduce((sum, row) => sum + row.count, 0));
  protected readonly maxPlan = computed(() => Math.max(1, ...(this.view()?.byPlan ?? []).map((row) => row.count)));
  protected readonly maxMonth = computed(() => Math.max(1, ...(this.view()?.newPerMonth ?? []).map((row) => row.count)));

  constructor() {
    this.api.dashboard().then(
      (view) => this.view.set(view),
      (error: unknown) => this.error.set(messageOf(error, 'The dashboard could not be loaded.')),
    );
  }

  protected count(rows: readonly { key: string; count: number }[], key: string): number {
    return rows.find((row) => row.key === key)?.count ?? 0;
  }

  protected share(value: number, max: number): number {
    return Math.round((value / max) * 100);
  }

  protected day(value: string): string {
    return dateTime(value, 'd MMM y');
  }

  protected when(value: string): string {
    return dateTime(value);
  }
}
