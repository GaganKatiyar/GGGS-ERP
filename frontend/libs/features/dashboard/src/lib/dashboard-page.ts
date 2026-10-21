import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { AnalyticsApi, InsightApi, MetricPanel } from '@gggs/shared/insight';
import type { CalendarEntry, Metric } from '@gggs/shared/models';
import { WorkflowApi } from '@gggs/shared/workflow';

const iso = (date: Date) => `${date.getFullYear()}-${`${date.getMonth() + 1}`.padStart(2, '0')}-${`${date.getDate()}`.padStart(2, '0')}`;

/**
 * The home dashboard: what is waiting for the user, what is coming up for them in the next two
 * weeks, and - for leaders with executive access - the company's headline figures. Each block loads
 * on its own and only for those allowed to see it.
 */
@Component({
  selector: 'gggs-dashboard-page',
  imports: [RouterLink, MetricPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="dashboard">
      <h1 class="dashboard__title">Welcome back, {{ session.displayName() }}</h1>

      <div class="dashboard__grid">
        @if (canApprove) {
          <a class="card card--link" routerLink="/approvals">
            <h2 class="card__title">Approvals</h2>
            <p class="card__big">{{ approvals() ?? '…' }}</p>
            <p class="card__body">{{ approvals() === 0 ? 'Nothing waiting on you.' : 'requests waiting on you' }}</p>
          </a>
        }
        <article class="card dashboard__upcoming">
          <h2 class="card__title">Coming up for you</h2>
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
            <p class="card__body">Nothing in the next two weeks.</p>
          }
          <a class="card__more" routerLink="/calendar">Open the calendar</a>
        </article>
        <article class="card">
          <h2 class="card__title">Quick links</h2>
          <nav class="dashboard__links">
            @if (has('self.leave.apply')) {
              <a routerLink="/me/leave">Apply for leave</a>
            }
            @if (has('self.attendance.punch')) {
              <a routerLink="/me/attendance">Mark attendance</a>
            }
            @if (has('self.payslip.view')) {
              <a routerLink="/me/payslips">My payslips</a>
            }
            @if (has('self.expense-claim.create')) {
              <a routerLink="/me/expenses">Claim an expense</a>
            }
            @if (has('self.ticket.create')) {
              <a routerLink="/me/tickets">Ask the helpdesk</a>
            }
            @if (has('self.recognition.give')) {
              <a routerLink="/me/rewards">Recognise a colleague</a>
            }
          </nav>
        </article>
      </div>

      @if (canSeeExecutive) {
        <div class="dashboard__head">
          <h2 class="dashboard__section">Company at a glance</h2>
          <a routerLink="/analytics/executive">Executive dashboard</a>
        </div>
        <div class="dashboard__metrics">
          @for (metric of executive(); track metric.key) {
            <gggs-metric-panel [metric]="metric" />
          }
        </div>
      }
    </section>
  `,
  styles: `
    .dashboard__title {
      margin-block: 0 1rem;
      font-size: 1.375rem;
    }

    .dashboard__grid,
    .dashboard__metrics {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fill, minmax(min(16rem, 100%), 1fr));
    }

    .dashboard__metrics {
      grid-template-columns: repeat(auto-fill, minmax(min(18rem, 100%), 1fr));
    }

    .dashboard__head {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      margin-block: 1.5rem 0.75rem;
    }

    .dashboard__section {
      margin: 0;
      font-size: 1.1rem;
    }

    .card {
      display: grid;
      align-content: start;
      gap: 0.5rem;
      padding: 1rem;
      background: var(--gggs-color-surface-raised);
      border: 1px solid var(--gggs-color-border);
      border-radius: 0.5rem;
    }

    .card--link {
      color: inherit;
      text-decoration: none;
    }

    .card__title {
      margin: 0;
      font-size: 1rem;
    }

    .card__big {
      margin: 0;
      font-size: 2rem;
      font-weight: 600;
    }

    .card__body {
      margin: 0;
      color: var(--gggs-color-text-muted);
    }

    .dashboard__entry {
      margin: 0;
      display: grid;
      grid-template-columns: 4.5rem minmax(0, 1fr);
      gap: 0.5rem;
    }

    .dashboard__date {
      color: var(--gggs-color-text-muted);
      font-size: 0.85rem;
    }

    .dashboard__links {
      display: grid;
      gap: 0.4rem;
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
}
