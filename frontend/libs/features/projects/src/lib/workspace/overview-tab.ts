import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { HealthRating, ProjectAbilities, ProjectDashboard } from '@gggs/shared/models';
import { Chart, EmptyState, KpiCard, Skeleton, StatusBadge, type ChartSeries } from '@gggs/shared/ui';

interface Dimension {
  readonly key: string;
  readonly label: string;
  readonly rating: HealthRating;
}

/** The project dashboard (spec §79, §120): progress against time, health by dimension, sprint, milestones and RAID. */
@Component({
  selector: 'gggs-overview-tab',
  imports: [DatePipe, DecimalPipe, KpiCard, StatusBadge, Chart, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (dashboard(); as d) {
      <div class="stack">
        <div class="kpi-row">
          <gggs-kpi-card label="Progress" [value]="(d.project.progressPercent | number: '1.0-0') + '%'" [hint]="(d.elapsedPercent | number: '1.0-0') + '% of time used'" icon="pi pi-chart-line" />
          <gggs-kpi-card label="Open tasks" [value]="d.tasks.open" [hint]="d.tasks.overdue + ' overdue'" icon="pi pi-check-square" />
          <gggs-kpi-card label="Milestones" [value]="d.milestonesAchieved + ' / ' + d.milestonesTotal" hint="achieved" icon="pi pi-flag" />
          <gggs-kpi-card label="Open risks" [value]="d.raid.openRisks" [hint]="d.raid.highRisks + ' high'" icon="pi pi-exclamation-triangle" />
          <gggs-kpi-card label="Open issues" [value]="d.raid.openIssues" [hint]="d.raid.criticalIssues + ' critical'" icon="pi pi-bolt" />
          <gggs-kpi-card label="Team" [value]="d.project.teamSize" icon="pi pi-users" />
        </div>

        <div class="overview__grid">
          <section class="panel stack">
            <div class="toolbar">
              <h2 class="overview__heading">Health</h2>
              <gggs-status-badge [status]="d.health.current.overall" />
              @if (can().manage) {
                <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="assess()">Record today</button>
              }
            </div>
            <div class="overview__dimensions">
              @for (dimension of dimensions(); track dimension.key) {
                <div class="overview__dimension">
                  <span>{{ dimension.label }}</span>
                  <gggs-status-badge [status]="dimension.rating" />
                </div>
              }
            </div>
            <ul class="overview__reasons">
              @for (reason of d.health.current.reasons; track reason) {
                <li>{{ reason }}</li>
              }
            </ul>
            @if (d.health.trend.length > 1) {
              <gggs-chart kind="line" title="Health score over time (lower is better)" [labels]="trendLabels()" [series]="trendSeries()" height="10rem" />
            }
          </section>

          <section class="panel stack">
            <h2 class="overview__heading">{{ d.activeSprint ? d.activeSprint.name : 'Sprint' }}</h2>
            @if (d.activeSprint; as sprint) {
              <p class="muted overview__goal">{{ sprint.goal ?? 'No goal set' }} · ends {{ sprint.endDate | date: 'd MMM' }}</p>
              <p>{{ sprint.donePoints }} of {{ sprint.plannedPoints }} points done · {{ sprint.doneTaskCount }} of {{ sprint.taskCount }} tasks</p>
              @if (d.burndown.length > 0) {
                <gggs-chart kind="line" title="Burndown" [labels]="burndownLabels()" [series]="burndownSeries()" height="12rem" />
              }
            } @else if (d.velocity.length > 0) {
              <gggs-chart kind="bar" title="Velocity" [labels]="velocityLabels()" [series]="velocitySeries()" height="12rem" />
            } @else {
              <gggs-empty-state icon="pi pi-replay" title="No sprint running" />
            }
          </section>

          <section class="panel stack">
            <h2 class="overview__heading">Next milestones</h2>
            @for (milestone of d.milestones; track milestone.id) {
              <div class="overview__row">
                <strong>{{ milestone.name }}</strong>
                <span [class.overview__late]="milestone.daysOverdue > 0">{{ milestone.plannedDate | date: 'd MMM y' }}{{ milestone.daysOverdue > 0 ? ' · ' + milestone.daysOverdue + 'd late' : '' }}</span>
                <span>{{ milestone.measuredPercent | number: '1.0-0' }}%</span>
              </div>
            } @empty {
              <p class="muted">No open milestones.</p>
            }
          </section>

          <section class="panel stack">
            <h2 class="overview__heading">Plan</h2>
            @for (phase of d.phases; track phase.id) {
              <div class="overview__phase">
                <div class="overview__row">
                  <strong>{{ phase.name }}</strong>
                  <span class="muted">{{ phase.startDate ? (phase.startDate | date: 'd MMM') : '?' }} – {{ phase.endDate ? (phase.endDate | date: 'd MMM') : '?' }}</span>
                  <gggs-status-badge [status]="phase.status" />
                </div>
                <span class="overview__bar"><span [style.width.%]="phase.measuredProgress"></span></span>
              </div>
            } @empty {
              <p class="muted">No phases planned.</p>
            }
            <div class="overview__facts">
              <span>Tasks by stage:</span>
              @for (entry of categoryEntries(); track entry[0]) {
                <span class="muted">{{ entry[0] }} {{ entry[1] }}</span>
              }
            </div>
            <div class="overview__facts">
              <span>Changes awaiting approval: {{ d.pendingChangeRequests }}</span>
              <span>Open meeting actions: {{ d.openActions }}</span>
            </div>
          </section>
        </div>
      </div>
    } @else {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .kpi-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .overview__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(22rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .overview__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .overview__dimensions {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: var(--gggs-space-2);
    }

    .overview__dimension {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .overview__reasons {
      margin: 0;
      padding-inline-start: var(--gggs-space-4);
      font-size: var(--gggs-font-size-sm);
      color: var(--gggs-color-text-muted);
    }

    .overview__goal {
      margin: 0;
    }

    .overview__row {
      display: grid;
      grid-template-columns: 1fr auto auto;
      gap: var(--gggs-space-3);
      align-items: center;
    }

    .overview__late {
      color: var(--gggs-color-danger);
    }

    .overview__phase {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
    }

    .overview__bar {
      display: block;
      height: 0.4rem;
      border-radius: 999px;
      background: var(--gggs-color-border);
      overflow: hidden;
    }

    .overview__bar span {
      display: block;
      height: 100%;
      background: var(--gggs-color-primary);
    }

    .overview__facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      font-size: var(--gggs-font-size-sm);
    }
  `,
})
export class OverviewTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly can = input.required<ProjectAbilities>();
  readonly changed = output<void>();

  protected readonly dashboard = signal<ProjectDashboard | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly dimensions = computed<readonly Dimension[]>(() => {
    const health = this.dashboard()?.health.current;
    if (!health) {
      return [];
    }
    return [
      { key: 'schedule', label: 'Schedule', rating: health.schedule },
      { key: 'budget', label: 'Budget', rating: health.budget },
      { key: 'resources', label: 'Resources', rating: health.resources },
      { key: 'scope', label: 'Scope', rating: health.scope },
      { key: 'quality', label: 'Quality', rating: health.quality },
      { key: 'risk', label: 'Risk', rating: health.risk },
      { key: 'issues', label: 'Issues', rating: health.issues },
      { key: 'client', label: 'Client', rating: health.clientSatisfaction },
    ];
  });

  protected readonly categoryEntries = computed(() => Object.entries(this.dashboard()?.tasks.byCategory ?? {}));
  protected readonly burndownLabels = computed(() => (this.dashboard()?.burndown ?? []).map((point) => point.date.slice(5)));
  protected readonly burndownSeries = computed<readonly ChartSeries[]>(() => {
    const points = this.dashboard()?.burndown ?? [];
    return [
      { label: 'Ideal', values: points.map((point) => point.ideal) },
      { label: 'Remaining', values: points.map((point) => point.remaining ?? Number.NaN) },
    ];
  });
  protected readonly velocityLabels = computed(() => (this.dashboard()?.velocity ?? []).map((point) => point.name));
  protected readonly velocitySeries = computed<readonly ChartSeries[]>(() => {
    const points = this.dashboard()?.velocity ?? [];
    return [
      { label: 'Committed', values: points.map((point) => point.committed) },
      { label: 'Completed', values: points.map((point) => point.completed) },
    ];
  });
  protected readonly trendLabels = computed(() => (this.dashboard()?.health.trend ?? []).map((point) => point.assessedOn.slice(5)));
  protected readonly trendSeries = computed<readonly ChartSeries[]>(() => [{ label: 'Score', values: (this.dashboard()?.health.trend ?? []).map((point) => point.score) }]);

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected async assess(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.assessHealth(this.projectId());
      await this.load(this.projectId());
      this.changed.emit();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Health could not be recorded.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(projectId: string): Promise<void> {
    this.error.set(null);
    try {
      this.dashboard.set(await this.api.dashboard(projectId));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The dashboard could not be loaded.');
    }
  }
}
