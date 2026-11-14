import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@gggs/core/auth';
import { RecentPagesStore } from '@gggs/core/layout';
import { PermissionStore } from '@gggs/core/permissions';
import { FeatureBanner, FeatureBannerApi } from '@gggs/shared/experience';
import { AnalyticsApi, InsightApi, formatMetric } from '@gggs/shared/insight';
import type { CalendarEntry, Metric, MyFeatureBanner } from '@gggs/shared/models';
import { AppArt, KpiCard, PageHeader, Skeleton, Tilt, type KpiTrend } from '@gggs/shared/ui';
import { WorkflowApi } from '@gggs/shared/workflow';
import { BANNER_TIPS } from './banner-tips';
import { ProfileNudges } from './profile-nudges';
import { WorkCard } from './work-card';

const iso = (date: Date) => `${date.getFullYear()}-${`${date.getMonth() + 1}`.padStart(2, '0')}-${`${date.getDate()}`.padStart(2, '0')}`;

/** A headline metric's icon, by the analytics area it belongs to. */
const AREA_ICONS: Readonly<Record<string, string>> = {
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

/** A place or a request on the dashboard, shown when the person may use it and the plan includes it. */
interface DashboardLink {
  readonly label: string;
  readonly icon: string;
  readonly route: string;
  readonly permissions: readonly string[];
  readonly feature?: string;
  readonly tone?: 'info' | 'warning' | 'primary';
}

const QUICK_LINKS: readonly DashboardLink[] = [
  { label: 'My profile', icon: 'pi pi-id-card', route: '/me/profile', permissions: ['self.profile.view'] },
  { label: 'My attendance', icon: 'pi pi-clock', route: '/me/attendance', permissions: ['self.attendance.view', 'self.attendance.punch'], feature: 'Time' },
  { label: 'My payslips', icon: 'pi pi-file', route: '/me/payslips', permissions: ['self.payslip.view'], feature: 'Payroll' },
  { label: 'My learning', icon: 'pi pi-book', route: '/me/learning', permissions: ['self.learning.view'], feature: 'Learning' },
  { label: 'My rewards', icon: 'pi pi-heart', route: '/me/rewards', permissions: ['self.reward.view', 'self.recognition.give'], feature: 'Rewards' },
];

const REQUESTS: readonly DashboardLink[] = [
  { label: 'Apply for leave', icon: 'pi pi-calendar-plus', route: '/me/leave', permissions: ['self.leave.apply'], feature: 'Time', tone: 'info' },
  { label: 'Claim an expense', icon: 'pi pi-receipt', route: '/me/expenses', permissions: ['self.expense-claim.create'], feature: 'Expenses', tone: 'warning' },
  { label: 'Ask the helpdesk', icon: 'pi pi-question-circle', route: '/me/tickets', permissions: ['self.ticket.create'], feature: 'Helpdesk', tone: 'primary' },
];

/**
 * The home dashboard, in two parts. The wide one holds the feature banner, recent links and what is
 * coming up in the next two weeks; the narrow one holds what to do now: clock in, approvals waiting
 * and requests to raise. For leaders with executive access the company's headline figures follow
 * across the full width, each with its recent trend and the change on the period before. Each block
 * loads on its own and only for those allowed to see it; the breakdowns live on the executive
 * dashboard one click away.
 */
@Component({
  selector: 'gggs-dashboard-page',
  imports: [RouterLink, AppArt, FeatureBanner, KpiCard, PageHeader, Skeleton, Tilt, WorkCard, ProfileNudges],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="dashboard">
      <gggs-page-header [title]="'Welcome back, ' + session.displayName()" [subtitle]="today" />

      <div class="dashboard__columns" [class.dashboard__columns--whole]="!hasSide">
        <div class="dashboard__col">
          @if (banners().length) {
            <gggs-feature-banner class="dashboard__banner rise-in" [slides]="banners()" />
          }
          @if (!recent.isLoaded() || tiles().length || !isEmployee) {
            <article class="panel dashboard__apps rise-in" style="--i: 1">
              @if (recent.isLoaded()) {
                <h2 class="panel__title">{{ tilesHeading() }}</h2>
                <nav class="dashboard__tiles" [attr.aria-label]="tilesHeading()">
                  @for (link of tiles(); track link.route) {
                    <a [routerLink]="link.route">
                      <span class="dashboard__tile-icon dashboard__tile-icon--art"><gggs-app-art [icon]="link.icon" /></span>
                      {{ link.label }}
                    </a>
                  }
                </nav>
                @if (!isEmployee && !hasRecent()) {
                  <p class="muted">Your account is not linked to an employee record, so there is no self-service to link to.</p>
                }
              } @else {
                <gggs-skeleton [lines]="3" />
              }
            </article>
          }
          <article class="panel dashboard__coming rise-in" style="--i: 2">
            <div class="dashboard__coming-head">
              <h2 class="panel__title">Coming up for you</h2>
              <a class="dashboard__more" routerLink="/calendar">Open the calendar <i class="pi pi-arrow-right" aria-hidden="true"></i></a>
            </div>
            <div class="stack stack--sm">
              @for (entry of upcoming(); track $index) {
                <p class="dashboard__entry">
                  <span class="dashboard__date">{{ day(entry.start) }}</span>
                  @if (entry.link) {
                    <a [routerLink]="entry.link">{{ entry.title }}</a>
                  } @else {
                    <span>{{ entry.title }}</span>
                  }
                </p>
              } @empty {
                <p class="muted">Nothing in the next two weeks.</p>
              }
            </div>
          </article>
        </div>

        @if (hasSide) {
          <div class="dashboard__col">
            @if (canClockIn) {
              <gggs-work-card class="dashboard__work rise-in" />
            }
            @if (canApprove) {
              <a class="panel dashboard__approvals rise-in" routerLink="/approvals">
                <span class="dashboard__tile-icon" aria-hidden="true"><i class="pi pi-check-square"></i></span>
                <span class="dashboard__approvals-text">
                  <strong>Approvals</strong>
                  <span class="dashboard__approvals-hint">{{ approvals() === 0 ? 'Nothing waiting on you' : 'Waiting on you' }}</span>
                </span>
                <strong class="dashboard__approvals-count">{{ approvals() ?? '…' }}</strong>
              </a>
            }
            @if (requests.length) {
              <article class="panel dashboard__requests rise-in" style="--i: 1">
                <h2 class="panel__title">Requests</h2>
                <nav class="dashboard__request-list" aria-label="Requests">
                  @for (request of requests; track request.route) {
                    <a [class]="'dashboard__request dashboard__request--' + request.tone" [routerLink]="request.route">
                      <i [class]="request.icon" aria-hidden="true"></i>
                      {{ request.label }}
                    </a>
                  }
                </nav>
              </article>
            }
          </div>
        }
      </div>

      @if (selfService('self.profile.view')) {
        <gggs-profile-nudges />
      }

      @if (canSeeExecutive) {
        <div class="dashboard__head">
          <h2 class="dashboard__section">Company at a glance</h2>
          <a routerLink="/analytics/executive">Executive dashboard</a>
        </div>
        <div class="dashboard__kpis">
          @for (metric of executive(); track metric.key; let i = $index) {
            <gggs-kpi-card
              gggsTilt
              class="rise-in"
              [style.--i]="i"
              [label]="metric.label"
              [value]="format(metric)"
              [hint]="metric.hint"
              [icon]="iconFor(metric)"
              [series]="trendValues(metric)"
              [trend]="direction(metric)"
              [trendIsGood]="metric.higherIsBetter"
              [progress]="metric.unit === 'Percent' && metric.higherIsBetter ? metric.value : undefined"
            />
          }
        </div>
      }
    </section>
  `,
  styles: `
    .dashboard {
      container: dashboard / inline-size;
    }

    /* Two parts: what to read and where to go, then what to do now. */
    .dashboard__columns {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 21rem;
      gap: var(--gggs-space-4);
    }

    .dashboard__columns--whole {
      grid-template-columns: minmax(0, 1fr);
    }

    /* The last card in each column takes up any slack, so both columns end level. */
    .dashboard__col {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-4);
      min-width: 0;
    }

    .dashboard__col > :last-child {
      flex: 1 1 auto;
    }

    /* Too narrow for two parts: one column, with the clock-in card straight after the banner. */
    @container dashboard (max-width: 60rem) {
      .dashboard__columns {
        grid-template-columns: minmax(0, 1fr);
      }

      .dashboard__col {
        display: contents;
      }

      .dashboard__banner { order: 1; }
      .dashboard__work { order: 2; }
      .dashboard__approvals { order: 3; }
      .dashboard__requests { order: 4; }
      .dashboard__apps { order: 5; }
      .dashboard__coming { order: 6; }
    }

    .dashboard__apps,
    .dashboard__coming,
    .dashboard__requests {
      gap: var(--gggs-space-3);
      padding: var(--gggs-space-4);
    }

    .dashboard__kpis {
      display: grid;
      gap: var(--gggs-space-4);
      grid-template-columns: repeat(auto-fill, minmax(min(16rem, 100%), 1fr));
    }

    .dashboard__tiles {
      display: grid;
      gap: var(--gggs-space-2);
      grid-template-columns: repeat(auto-fit, minmax(5.5rem, 1fr));
    }

    .dashboard__tiles a {
      display: grid;
      justify-items: center;
      align-content: start;
      gap: var(--gggs-space-1);
      padding: var(--gggs-space-1);
      border-radius: var(--gggs-radius-md);
      color: var(--gggs-color-text);
      font-size: var(--gggs-font-size-sm);
      text-align: center;
      text-decoration: none;
    }

    .dashboard__tile-icon {
      display: grid;
      place-items: center;
      flex: none;
      width: 2.5rem;
      height: 2.5rem;
      border-radius: var(--gggs-radius-md);
      background-image: var(--gggs-gradient-tile);
      color: var(--gggs-color-primary);
      font-size: 1.125rem;
      transition: transform var(--gggs-transition-fast), box-shadow var(--gggs-transition-fast);
    }

    .dashboard__tile-icon--art {
      width: 3.5rem;
      height: 3.5rem;
      border-radius: var(--gggs-radius-lg);
    }

    .dashboard__tiles a:hover .dashboard__tile-icon {
      box-shadow: var(--gggs-shadow-2);
      transform: translateY(var(--gggs-lift));
    }

    .dashboard__request-list {
      display: grid;
      flex: 1 1 auto;
      gap: var(--gggs-space-2);
      grid-template-columns: repeat(auto-fit, minmax(min(13rem, 100%), 1fr));
    }

    .dashboard__request {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      min-height: 2.75rem;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border-radius: var(--gggs-radius-md);
      color: var(--gggs-color-text);
      font-weight: var(--gggs-font-weight-medium);
      text-decoration: none;
      transition: transform var(--gggs-transition-fast), box-shadow var(--gggs-transition-fast);
    }

    .dashboard__request:hover {
      box-shadow: var(--gggs-shadow-2);
      transform: translateY(var(--gggs-lift));
    }

    .dashboard__request .pi {
      font-size: 1.125rem;
    }

    .dashboard__approvals {
      flex-direction: row;
      align-items: center;
      gap: var(--gggs-space-3);
      padding: var(--gggs-space-3) var(--gggs-space-4);
      color: inherit;
      text-decoration: none;
    }

    .dashboard__approvals-text {
      display: grid;
      flex: 1 1 auto;
      min-width: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .dashboard__approvals-hint {
      color: var(--gggs-color-text-muted);
    }

    .dashboard__approvals-count {
      font-family: var(--gggs-font-family-display);
      font-size: var(--gggs-font-size-xl);
      font-variant-numeric: tabular-nums;
    }

    .dashboard__request--info { background: var(--gggs-color-info-subtle); }
    .dashboard__request--warning { background: var(--gggs-color-warning-subtle); }
    .dashboard__request--primary { background: var(--gggs-color-primary-subtle); }

    .dashboard__head {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      margin-block: var(--gggs-space-5) var(--gggs-space-3);
    }

    .dashboard__section {
      margin: 0;
      font-size: var(--gggs-font-size-lg);
    }

    .dashboard__entry {
      display: grid;
      grid-template-columns: 4.5rem minmax(0, 1fr);
      gap: var(--gggs-space-2);
      margin: 0;
    }

    .dashboard__date {
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
      font-variant-numeric: tabular-nums;
    }

    .dashboard__coming-head {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      justify-content: space-between;
      gap: var(--gggs-space-1) var(--gggs-space-3);
    }

    .dashboard__more {
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-1);
      font-size: var(--gggs-font-size-sm);
      font-weight: var(--gggs-font-weight-medium);
    }
  `,
})
export class DashboardPage {
  private readonly permissions = inject(PermissionStore);

  protected readonly session = inject(SessionStore);
  protected readonly isEmployee = Boolean(this.session.user()?.employeeId);
  protected readonly canApprove = this.permissions.has('workflow.task.view');
  /** The clock-in card: an employee who punches, in a tenant whose plan includes Time. */
  protected readonly canClockIn = this.selfService('self.attendance.punch') && this.permissions.hasFeature('Time');
  protected readonly canSeeExecutive = this.permissions.has('report.executive.view');
  protected readonly approvals = signal<number | null>(null);
  protected readonly upcoming = signal<readonly CalendarEntry[]>([]);
  /** HR's slides; null until the answer is in, so built-in tips never flash up first. */
  private readonly published = signal<readonly MyFeatureBanner[] | null>(null);
  protected readonly banners = computed(() => {
    const published = this.published();
    return published === null ? [] : published.length ? published : this.tips();
  });
  protected readonly quickLinks: readonly DashboardLink[] = [
    ...this.visible(QUICK_LINKS),
    ...(this.permissions.hasAny(['self.profile.view', 'employee.profile.view']) ? [{ label: 'Calendar', icon: 'pi pi-calendar', route: '/calendar', permissions: [] }] : []),
  ];
  protected readonly recent = inject(RecentPagesStore);
  protected readonly hasRecent = computed(() => this.recent.links().length > 0);
  /** The person's own recent pages; the usual links until they have opened something. */
  protected readonly tiles = computed<readonly Pick<DashboardLink, 'label' | 'icon' | 'route'>[]>(() => (this.hasRecent() ? this.recent.links() : this.quickLinks));
  protected readonly tilesHeading = computed(() => (this.hasRecent() ? 'Recent links' : 'Quick links'));
  protected readonly requests = this.visible(REQUESTS);
  protected readonly hasSide = this.canClockIn || this.canApprove || this.requests.length > 0;
  protected readonly executive = signal<readonly Metric[]>([]);
  protected readonly today = new Date().toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  protected readonly format = formatMetric;

  constructor() {
    if (this.canApprove) {
      inject(WorkflowApi)
        .pendingCount()
        .then((count) => this.approvals.set(count), () => this.approvals.set(0));
    }

    if (!this.permissions.hasAny(['self.profile.view', 'employee.profile.view'])) {
      this.published.set([]);
    } else {
      inject(FeatureBannerApi)
        .mine()
        .then((banners) => this.published.set(banners), () => this.published.set([]));

      const today = new Date();
      const until = new Date(today);
      until.setDate(today.getDate() + 14);
      inject(InsightApi)
        .calendar(iso(today), iso(until), 'mine')
        .then((view) => this.upcoming.set(view.entries.slice(0, 8)), () => undefined);
    }

    if (this.canSeeExecutive) {
      inject(AnalyticsApi)
        .dashboard('executive')
        .then((view) => this.executive.set(view.metrics.slice(0, 8)), () => undefined);
    }
  }

  /** Self-service pages are about the user's own employee record, which some accounts do not have. */
  protected selfService(permission: string): boolean {
    return this.isEmployee && this.permissions.has(permission);
  }

  /** A tip tied to something only shows to an employee who can use it. */
  private tips(): readonly MyFeatureBanner[] {
    return BANNER_TIPS.filter((tip) => !tip.permission || (this.isEmployee && this.permissions.has(tip.permission) && (!tip.feature || this.permissions.hasFeature(tip.feature))));
  }

  private visible(links: readonly DashboardLink[]): readonly DashboardLink[] {
    return links.filter((link) => this.isEmployee && this.permissions.hasAny(link.permissions) && (!link.feature || this.permissions.hasFeature(link.feature)));
  }

  protected day(date: string): string {
    return new Date(`${date}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
  }

  protected iconFor(metric: Metric): string {
    return AREA_ICONS[metric.area] ?? 'pi pi-chart-line';
  }

  protected trendValues(metric: Metric): readonly number[] {
    return (metric.trend ?? []).map((point) => point.value);
  }

  /** The latest period against the one before it, when the metric has a trend. */
  protected direction(metric: Metric): KpiTrend | undefined {
    const trend = metric.trend ?? [];
    if (trend.length < 2) {
      return undefined;
    }

    const change = trend[trend.length - 1]!.value - trend[trend.length - 2]!.value;
    return change > 0 ? 'up' : change < 0 ? 'down' : 'flat';
  }
}
