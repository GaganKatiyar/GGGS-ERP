import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { HealthRating, PagedResult, PortfolioSummary, ProjectDetail, ProjectItem, ProjectStage, ProjectStatus } from '@gggs/shared/models';
import { PROJECT_STAGES, STAGE_LABELS } from '@gggs/shared/models';
import { KpiCard, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { ProjectForm } from './project-form';

/** The project portfolio (spec §39, §79): every project in reach, with health at a glance. */
@Component({
  selector: 'gggs-projects-page',
  imports: [DatePipe, DecimalPipe, RouterLink, HasPermissionDirective, PageHeader, KpiCard, DataTable, DataTableCell, StatusBadge, ProjectForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Projects" subtitle="The portfolio: progress, health and what needs attention.">
      <div class="toolbar">
        <a class="btn" routerLink="tasks"><i class="pi pi-check-square" aria-hidden="true"></i> My tasks</a>
        <button type="button" class="btn btn--primary" *gggsHasPermission="'project.project.create'" (click)="formOpen.set(true)">New project</button>
      </div>
    </gggs-page-header>

    @if (summary(); as s) {
      <div class="kpi-row projects__kpis">
        <gggs-kpi-card label="Active projects" [value]="count(s.byStatus, 'Active')" icon="pi pi-folder-open" />
        <gggs-kpi-card label="Red health" [value]="count(s.byHealth, 'Red')" icon="pi pi-heart" [hint]="count(s.byHealth, 'Amber') + ' amber'" />
        <gggs-kpi-card label="Overdue milestones" [value]="s.overdueMilestones" icon="pi pi-flag" />
        <gggs-kpi-card label="High risks" [value]="s.openHighRisks" icon="pi pi-exclamation-triangle" />
        <gggs-kpi-card label="Changes awaiting approval" [value]="s.pendingChangeRequests" icon="pi pi-arrow-right-arrow-left" />
      </div>
    }

    <section class="panel toolbar projects__filters">
      <label class="field projects__search">
        <span class="field__label">Search</span>
        <input type="search" placeholder="Name or code" [value]="search()" (input)="onSearch($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="set('status', $any($event.target).value)">
          <option value="">Open</option>
          <option value="Active">Active</option>
          <option value="OnHold">On hold</option>
          <option value="Completed">Completed</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      </label>
      <label class="field">
        <span class="field__label">Stage</span>
        <select [value]="stage()" (change)="set('stage', $any($event.target).value)">
          <option value="">Any</option>
          @for (option of stages; track option) {
            <option [value]="option">{{ stageLabels[option] }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Health</span>
        <select [value]="health()" (change)="set('health', $any($event.target).value)">
          <option value="">Any</option>
          <option value="Green">Green</option>
          <option value="Amber">Amber</option>
          <option value="Red">Red</option>
          <option value="NotAssessed">Not assessed</option>
        </select>
      </label>
      <label class="field">
        <span><input type="checkbox" [checked]="mine()" (change)="toggleMine()" /> Only mine</span>
      </label>
    </section>

    <gggs-data-table [result]="result()" [columns]="columns" [loading]="loading()" exportName="projects" emptyTitle="No projects" [emptyDescription]="emptyHint()" (queryChange)="onQuery($event)" (rowClick)="open($event)">
      <ng-template gggsCell="name" let-row><strong>{{ row.code }}</strong> {{ row.name }}</ng-template>
      <ng-template gggsCell="stage" let-row>{{ stageLabel(row.stage) }}@if (row.status !== 'Active') { <gggs-status-badge [status]="row.status" /> }</ng-template>
      <ng-template gggsCell="healthStatus" let-row><gggs-status-badge [status]="row.healthStatus" /></ng-template>
      <ng-template gggsCell="progressPercent" let-row>
        <div class="projects__progress" [attr.aria-label]="row.progressPercent + '% done'">
          <span class="projects__bar"><span [style.width.%]="row.progressPercent"></span></span>
          {{ row.progressPercent | number: '1.0-0' }}%
        </div>
      </ng-template>
      <ng-template gggsCell="endDate" let-row>{{ row.endDate | date: 'd MMM y' }}</ng-template>
    </gggs-data-table>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-project-form [(visible)]="formOpen" (saved)="created($event)" />
  `,
  styles: `
    .kpi-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(9rem, 45%), 1fr));
      gap: var(--gggs-space-3);
    }

    .projects__kpis,
    .projects__filters {
      margin-block-end: var(--gggs-space-3);
    }

    .projects__filters {
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .projects__search {
      flex: 1;
      min-width: 14rem;
    }

    .projects__progress {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    .projects__bar {
      display: inline-block;
      width: 5rem;
      height: 0.4rem;
      border-radius: 999px;
      background: var(--gggs-color-border);
      overflow: hidden;
    }

    .projects__bar span {
      display: block;
      height: 100%;
      background: var(--gggs-color-primary);
    }
  `,
})
export class ProjectsPage {
  private readonly api = inject(ProjectsApi);
  private readonly router = inject(Router);

  protected readonly stages = PROJECT_STAGES;
  protected readonly stageLabels = STAGE_LABELS;
  protected readonly columns: readonly DataTableColumn<ProjectItem>[] = [
    { field: 'name', header: 'Project', sortable: true },
    { field: 'clientName', header: 'Client' },
    { field: 'projectManagerName', header: 'Manager' },
    { field: 'stage', header: 'Stage', sortable: true },
    { field: 'healthStatus', header: 'Health', sortable: true, width: '8rem' },
    { field: 'progressPercent', header: 'Progress', width: '10rem' },
    { field: 'teamSize', header: 'Team', align: 'right', width: '5rem' },
    { field: 'openTasks', header: 'Open tasks', align: 'right', width: '7rem', hidden: true },
    { field: 'endDate', header: 'Ends', sortable: true, width: '8rem' },
  ];

  protected readonly result = signal<PagedResult<ProjectItem> | null>(null);
  protected readonly summary = signal<PortfolioSummary | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly status = signal<ProjectStatus | ''>('');
  protected readonly stage = signal<ProjectStage | ''>('');
  protected readonly health = signal<HealthRating | ''>('');
  protected readonly mine = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly emptyHint = computed(() => (this.mine() ? 'You are not on any open project yet.' : undefined));
  private query: DataTableQuery = { page: 1, pageSize: 25 };
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    void this.load();
    void this.api.summary().then((summary) => this.summary.set(summary), () => this.summary.set(null));
  }

  protected stageLabel(stage: string): string {
    return STAGE_LABELS[stage as ProjectStage] ?? stage;
  }

  protected count(counts: Readonly<Record<string, number>>, key: string): number {
    return counts[key] || 0;
  }

  protected onQuery(query: DataTableQuery): void {
    this.query = query;
    void this.load();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.query = { ...this.query, page: 1 };
      void this.load();
    }, 300);
  }

  protected set(filter: 'status' | 'stage' | 'health', value: string): void {
    if (filter === 'status') {
      this.status.set(value as ProjectStatus | '');
    } else if (filter === 'stage') {
      this.stage.set(value as ProjectStage | '');
    } else {
      this.health.set(value as HealthRating | '');
    }
    this.query = { ...this.query, page: 1 };
    void this.load();
  }

  protected toggleMine(): void {
    this.mine.update((value) => !value);
    void this.load();
  }

  protected open(project: ProjectItem): void {
    void this.router.navigate(['/projects', project.id]);
  }

  protected created(detail: ProjectDetail): void {
    void this.router.navigate(['/projects', detail.project.id]);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.result.set(
        await this.api.list({
          search: this.search(),
          status: this.status(),
          stage: this.stage(),
          health: this.health(),
          mine: this.mine() || undefined,
          page: this.query.page,
          pageSize: this.query.pageSize,
          sort: this.query.sort,
        }),
      );
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Projects could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
