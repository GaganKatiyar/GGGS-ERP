import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { AnalyticsApi, InsightApi, formatMetric } from '@gggs/shared/insight';
import type { CalendarEntry, Metric } from '@gggs/shared/models';
import { KpiCard, PageHeader, Tilt, type KpiTrend } from '@gggs/shared/ui';
import { WorkflowApi } from '@gggs/shared/workflow';

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

/**
 * The home dashboard: what is waiting for the user, what is coming up for them in the next two
 * weeks, and - for leaders with executive access - the company's headline figures, each with its
 * recent trend and the change on the period before. Each block loads on its own and only for
 * those allowed to see it; the breakdowns live on the executive dashboard one click away.
 */
@Component({
  selector: 'gggs-dashboard-page',
  imports: [RouterLink, KpiCard, PageHeader, Tilt],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="dashboard">
      <gggs-page-header [title]="'Welcome back, ' + session.displayName()" [subtitle]="today" />

      <div class="dashboard__grid">
        @if (canApprove) {
          <a class="dashboard__tile rise-in" routerLink="/approvals">
            <gggs-kpi-card
              label="Approvals"
              icon="pi pi-check-square"
              [value]="approvals() ?? '…'"
              [hint]="approvals() === 0 ? 'Nothing waiting on you.' : 'Requests waiting on you'"
            />
          </a>
        }
        <article class="panel rise-in" style="--i: 1">
          <h2 class="panel__title">Coming up for you</h2>
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
          <a class="dashboard__more" routerLink="/calendar">Open the calendar <i class="pi pi-arrow-right" aria-hidden="true"></i></a>
        </article>
        <article class="panel rise-in" style="--i: 2">
          <h2 class="panel__title">Quick links</h2>
          <nav class="dashboard__links" aria-label="Quick links">
            @if (has('self.leave.apply')) {
              <a routerLink="/me/leave"><i class="pi pi-calendar-plus" aria-hidden="true"></i>Apply for leave</a>
            }
            @if (has('self.attendance.punch')) {
              <a routerLink="/me/attendance"><i class="pi pi-clock" aria-hidden="true"></i>Mark attendance</a>
            }
            @if (has('self.payslip.view')) {
              <a routerLink="/me/payslips"><i class="pi pi-file" aria-hidden="true"></i>My payslips</a>
            }
            @if (has('self.expense-claim.create')) {
              <a routerLink="/me/expenses"><i class="pi pi-receipt" aria-hidden="true"></i>Claim an expense</a>
            }
            @if (has('self.ticket.create')) {
              <a routerLink="/me/tickets"><i class="pi pi-question-circle" aria-hidden="true"></i>Ask the helpdesk</a>
            }
            @if (has('self.recognition.give')) {
              <a routerLink="/me/rewards"><i class="pi pi-heart" aria-hidden="true"></i>Recognise a colleague</a>
            }
          </nav>
        </article>
      </div>

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
    .dashboard__grid,
    .dashboard__kpis {
      display: grid;
      gap: var(--gggs-space-4);
      grid-template-columns: repeat(auto-fill, minmax(min(16rem, 100%), 1fr));
    }

    /* A grid, so the card inside stretches to the row like the panels beside it. */
    .dashboard__tile {
      display: grid;
      border-radius: var(--gggs-radius-lg);
      color: inherit;
      text-decoration: none;
    }

    .dashboard__head {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      margin-block: var(--gggs-space-8) var(--gggs-space-3);
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

    .dashboard__more {
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-1);
      margin-block-start: auto;
      font-weight: var(--gggs-font-weight-medium);
    }

    .dashboard__links {
      display: grid;
      gap: var(--gggs-space-1);
    }

    .dashboard__links a {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      padding: var(--gggs-space-2);
      border-radius: var(--gggs-radius-md);
      text-decoration: none;
      transition: background-color var(--gggs-transition-fast);
    }

    .dashboard__links a:hover {
      background: var(--gggs-color-surface-hover);
    }

    .dashboard__links .pi {
      color: var(--gggs-color-text-subtle);
    }
  `,
})
export class DashboardPage {
  private readonly permissions = inject(PermissionStore);

  protected readonly session = inject(SessionStore);
  protected readonly canApprove = this.permissions.has('workflow.task.view');
  protected readonly canSeeExecutive = this.permissions.has('report.executive.view');
  protected readonly approvals = signal<number | null>(null);
  protected readonly upcoming = signal<readonly CalendarEntry[]>([]);
  protected readonly executive = signal<readonly Metric[]>([]);
  protected readonly today = new Date().toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  protected readonly format = formatMetric;

  constructor() {
    if (this.canApprove) {
      inject(WorkflowApi)
        .pendingCount()
        .then((count) => this.approvals.set(count), () => this.approvals.set(0));
    }

    if (this.permissions.hasAny(['self.profile.view', 'employee.profile.view'])) {
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

  protected has(permission: string): boolean {
    return this.permissions.has(permission);
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
