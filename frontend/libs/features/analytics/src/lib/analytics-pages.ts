import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { AnalyticsApi, MetricPanel, downloadCsv, formatMetric } from '@gggs/shared/insight';
import type { AnalyticsArea, DashboardView, DigestFrequency, Metric, ReportSubscription } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const ICONS: Record<string, string> = {
  executive: 'pi pi-chart-bar',
  people: 'pi pi-users',
  workforce: 'pi pi-sitemap',
  time: 'pi pi-clock',
  projects: 'pi pi-briefcase',
  resources: 'pi pi-th-large',
  payroll: 'pi pi-wallet',
  hiring: 'pi pi-user-plus',
  talent: 'pi pi-star',
  experience: 'pi pi-heart',
};

/** Every dashboard the caller may open, and the digests they receive. */
@Component({
  selector: 'gggs-analytics-home-page',
  imports: [RouterLink, DatePipe, PageHeader, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Analytics" subtitle="Dashboards for every part of the organization, within your report access." />
    @if (areas(); as list) {
      <div class="xp__cards">
        @for (area of list; track area.key) {
          <a class="xp__card analytics__card" [routerLink]="['/analytics', area.key]">
            <i [class]="icon(area.key)" aria-hidden="true"></i>
            <strong>{{ area.title }}</strong>
            <span class="muted">{{ area.description }}</span>
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-chart-pie" title="No dashboards" description="Ask an administrator for report access." />
        }
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (canSchedule) {
      <section class="panel stack">
        <h2 class="xp__heading">My report digests</h2>
        @for (subscription of subscriptions(); track subscription.id) {
          <div class="xp__row">
            <span><strong>{{ subscription.title }}</strong></span>
            <span>{{ subscription.frequency === 'Weekly' ? 'Every Monday' : 'On the 1st of each month' }}</span>
            <span class="muted">{{ subscription.lastSentOn ? 'last sent ' + (subscription.lastSentOn | date: 'd MMM y') : 'not sent yet' }}</span>
            <button type="button" class="btn btn--link btn--sm" (click)="unsubscribe(subscription.id)">Stop</button>
          </div>
        } @empty {
          <p class="muted">Open a dashboard and choose "Email me" to get its figures weekly or monthly.</p>
        }
      </section>
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styles: `
    .analytics__card {
      color: inherit;
      text-decoration: none;
    }

    .analytics__card i {
      font-size: 1.5rem;
      color: var(--gggs-color-primary);
    }
  `,
  styleUrl: './xp.css',
})
export class AnalyticsHomePage {
  private readonly api = inject(AnalyticsApi);

  protected readonly canSchedule = inject(PermissionStore).has('report.schedule.manage');
  protected readonly areas = signal<readonly AnalyticsArea[] | null>(null);
  protected readonly subscriptions = signal<readonly ReportSubscription[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.areas().then((areas) => this.areas.set(areas), (error: unknown) => this.error.set(message(error, 'Dashboards could not be loaded.')));
    if (this.canSchedule) {
      this.api.subscriptions().then((subscriptions) => this.subscriptions.set(subscriptions), () => undefined);
    }
  }

  protected icon(key: string): string {
    return ICONS[key] ?? 'pi pi-chart-line';
  }

  protected async unsubscribe(id: string): Promise<void> {
    try {
      this.subscriptions.set(await this.api.unsubscribe(id));
    } catch (error) {
      this.error.set(message(error, 'The digest could not be stopped.'));
    }
  }
}

/** One dashboard: headline figures, breakdowns and trends from every module that reports on the area. */
@Component({
  selector: 'gggs-dashboard-view-page',
  imports: [RouterLink, PageHeader, EmptyState, Skeleton, MetricPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header [title]="(view()?.title ?? 'Dashboard') + ' analytics'" [subtitle]="subtitle()">
      <a class="btn" routerLink="/analytics">All dashboards</a>
      @if (view(); as v) {
        <button type="button" class="btn" (click)="refresh()"><i class="pi pi-refresh" aria-hidden="true"></i> Refresh</button>
        @if (canExport) {
          <button type="button" class="btn" (click)="export(v)"><i class="pi pi-download" aria-hidden="true"></i> Export CSV</button>
        }
        @if (canSchedule) {
          <select class="analytics__digest" aria-label="Email me this dashboard" [value]="digest()" (change)="setDigest($any($event.target).value)">
            <option value="">Email me: never</option>
            <option value="Weekly">Email me: weekly</option>
            <option value="Monthly">Email me: monthly</option>
          </select>
        }
      }
    </gggs-page-header>
    <nav class="toolbar analytics__tabs" aria-label="Dashboards">
      @for (area of areas(); track area.key) {
        <a class="btn btn--sm" [class.btn--primary]="area.key === area_()" [routerLink]="['/analytics', area.key]">{{ area.title }}</a>
      }
    </nav>
    @if (view(); as v) {
      @if (!v.tenantWide) {
        <p class="muted">Figures cover the people in your report access. Company-wide figures such as payroll and utilization appear for company-wide viewers only.</p>
      }
      <div class="analytics__grid">
        @for (metric of v.metrics; track metric.key) {
          <gggs-metric-panel [metric]="metric" [class.analytics__wide]="wide(metric)" />
        } @empty {
          <gggs-empty-state icon="pi pi-chart-pie" title="Nothing to show yet" description="Figures appear as the modules behind this dashboard are used." />
        }
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="10" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styles: `
    .analytics__grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .analytics__wide {
      grid-column: span 2;
    }

    .analytics__tabs {
      flex-wrap: wrap;
      margin-block-end: var(--gggs-space-3);
    }

    .analytics__digest {
      width: auto;
    }

    @media (max-width: 48rem) {
      .analytics__wide {
        grid-column: auto;
      }
    }
  `,
  styleUrl: './xp.css',
})
export class DashboardViewPage {
  private readonly api = inject(AnalyticsApi);
  private readonly permissions = inject(PermissionStore);

  readonly area = input.required<string>();

  protected readonly canExport = this.permissions.has('report.report.export');
  protected readonly canSchedule = this.permissions.has('report.schedule.manage');
  protected readonly view = signal<DashboardView | null>(null);
  protected readonly areas = signal<readonly AnalyticsArea[]>([]);
  protected readonly subscriptions = signal<readonly ReportSubscription[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly area_ = computed(() => this.area());
  protected readonly digest = computed(() => this.subscriptions().find((subscription) => subscription.area === this.area())?.frequency ?? '');
  protected readonly subtitle = computed(() => {
    const view = this.view();
    return view ? `As of ${new Date(view.generatedAt).toLocaleString()}` : 'Loading figures...';
  });

  constructor() {
    this.api.areas().then((areas) => this.areas.set(areas), () => undefined);
    if (this.canSchedule) {
      this.api.subscriptions().then((subscriptions) => this.subscriptions.set(subscriptions), () => undefined);
    }

    effect(() => {
      this.area();
      this.refresh();
    });
  }

  /** Long trends get two columns so the line is readable. */
  protected wide(metric: Metric): boolean {
    return (metric.trend?.length ?? 0) > 6;
  }

  protected refresh(): void {
    this.view.set(null);
    this.error.set(null);
    this.api.dashboard(this.area()).then(
      (view) => this.view.set(view),
      (error: unknown) => this.error.set(message(error, 'The dashboard could not be loaded.')),
    );
  }

  protected export(view: DashboardView): void {
    const rows: (string | number)[][] = [['Metric', 'Value', 'Unit', 'Detail', 'Label', 'Amount']];
    for (const metric of view.metrics) {
      rows.push([metric.label, formatMetric(metric), metric.unit, metric.hint ?? '', '', '']);
      for (const point of metric.breakdown ?? []) {
        rows.push([metric.label, '', metric.unit, 'breakdown', point.label, point.value]);
      }

      for (const point of metric.trend ?? []) {
        rows.push([metric.label, '', metric.unit, 'trend', point.label, point.value]);
      }
    }

    downloadCsv(`${view.area}-analytics-${new Date().toISOString().slice(0, 10)}.csv`, rows);
  }

  protected async setDigest(frequency: DigestFrequency | ''): Promise<void> {
    try {
      const existing = this.subscriptions().find((subscription) => subscription.area === this.area());
      if (frequency === '') {
        if (existing) {
          this.subscriptions.set(await this.api.unsubscribe(existing.id));
        }
      } else {
        this.subscriptions.set(await this.api.subscribe(this.area(), frequency));
      }
    } catch (error) {
      this.error.set(message(error, 'The digest could not be changed.'));
    }
  }
}
