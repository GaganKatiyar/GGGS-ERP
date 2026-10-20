import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { ChangeRequest, ChangeRequestStatus, Milestone, RaidItem, RaidKind, Sprint } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, PageHeader, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** Milestones across the portfolio (spec §46): what is due soon and what is late. */
@Component({
  selector: 'gggs-milestones-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Milestones" subtitle="Due soon and overdue, across projects." />
    <section class="panel toolbar register__filters">
      <label class="field">
        <span class="field__label">Show</span>
        <select [value]="window()" (change)="window.set($any($event.target).value); load()">
          <option value="30">Due in 30 days</option>
          <option value="90">Due in 90 days</option>
          <option value="overdue">Overdue only</option>
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (milestone of milestones(); track milestone.id) {
        <a class="register__row" [routerLink]="['/projects', milestone.projectId]" [queryParams]="{ tab: 'plan' }">
          <span><strong>{{ milestone.projectCode }}</strong> <span class="muted">{{ milestone.projectName }}</span></span>
          <strong>{{ milestone.name }}</strong>
          <span [class.register__late]="milestone.daysOverdue > 0">{{ milestone.plannedDate | date: 'd MMM y' }}{{ milestone.daysOverdue > 0 ? ' · ' + milestone.daysOverdue + 'd late' : '' }}</span>
          <span>{{ milestone.measuredPercent | number: '1.0-0' }}%</span>
          <span class="muted">{{ milestone.ownerName ?? '' }}</span>
          <gggs-status-badge [status]="milestone.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-flag" title="No milestones in this window" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styleUrl: './registers.css',
})
export class MilestonesPage {
  private readonly api = inject(ProjectsApi);

  protected readonly milestones = signal<readonly Milestone[]>([]);
  protected readonly window = signal('30');
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.milestones.set(
        await this.api.portfolioMilestones(this.window() === 'overdue' ? { overdueOnly: true } : { withinDays: Number(this.window()) }),
      );
    } catch (error) {
      this.error.set(message(error, 'Milestones could not be loaded.'));
    }
  }
}

/** Sprints running across the portfolio (spec §48). */
@Component({
  selector: 'gggs-sprints-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Sprints" subtitle="What every team is working through." />
    <section class="panel toolbar register__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="Active">Running</option>
          <option value="Planned">Planned</option>
          <option value="Completed">Completed</option>
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (sprint of sprints(); track sprint.id) {
        <a class="register__row" [routerLink]="['/projects', sprint.projectId]" [queryParams]="{ tab: 'sprints' }">
          <span><strong>{{ sprint.projectCode }}</strong> <span class="muted">{{ sprint.projectName }}</span></span>
          <strong>{{ sprint.name }}</strong>
          <span class="muted">{{ sprint.startDate | date: 'd MMM' }} – {{ sprint.endDate | date: 'd MMM' }}</span>
          <span>{{ sprint.donePoints }} / {{ sprint.plannedPoints }} pt</span>
          <span class="muted register__goal">{{ sprint.goal ?? '' }}</span>
          <gggs-status-badge [status]="sprint.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-replay" title="No sprints here" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styleUrl: './registers.css',
})
export class SprintsPage {
  private readonly api = inject(ProjectsApi);

  protected readonly sprints = signal<readonly Sprint[]>([]);
  protected readonly status = signal<'Active' | 'Planned' | 'Completed'>('Active');
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.sprints.set(await this.api.portfolioSprints(this.status()));
    } catch (error) {
      this.error.set(message(error, 'Sprints could not be loaded.'));
    }
  }
}

/** Change requests across the portfolio (spec §50): what is waiting on whom. */
@Component({
  selector: 'gggs-change-requests-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Change requests" subtitle="Scope changes across projects, with their cost and schedule impact." />
    <section class="panel toolbar register__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">In flight</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (request of requests(); track request.id) {
        <a class="register__row" [routerLink]="['/projects', request.projectId]" [queryParams]="{ tab: 'changes' }">
          <span><strong>{{ request.projectCode }}</strong> <span class="muted">{{ request.projectName }}</span></span>
          <span><strong>{{ request.code }}</strong> {{ request.title }}</span>
          <span>{{ request.estimatedCost ?? 0 | number: '1.0-0' }}</span>
          <span class="muted">{{ request.scheduleImpactDays ?? 0 }}d · {{ request.priority }}</span>
          <span class="muted">{{ request.createdDate | date: 'd MMM' }}</span>
          <gggs-status-badge [status]="request.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-arrow-right-arrow-left" title="No change requests here" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styleUrl: './registers.css',
})
export class ChangeRequestsPage {
  private readonly api = inject(ProjectsApi);

  protected readonly humanize = humanize;
  protected readonly statuses: readonly ChangeRequestStatus[] = ['Draft', 'PendingApproval', 'Approved', 'Implementing', 'Testing', 'Closed', 'Rejected', 'Withdrawn'];
  protected readonly requests = signal<readonly ChangeRequest[]>([]);
  protected readonly status = signal<ChangeRequestStatus | ''>('');
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.requests.set(await this.api.portfolioChangeRequests(this.status()));
    } catch (error) {
      this.error.set(message(error, 'Change requests could not be loaded.'));
    }
  }
}

/** The RAID log across the portfolio (spec §53): open risks, issues, assumptions and dependencies. */
@Component({
  selector: 'gggs-raid-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="RAID log" subtitle="Open risks, assumptions, issues and dependencies across projects." />
    <section class="panel toolbar register__filters">
      <label class="field">
        <span class="field__label">Register</span>
        <select [value]="kind()" (change)="kind.set($any($event.target).value); load()">
          <option value="">All</option>
          <option value="Risk">Risks</option>
          <option value="Assumption">Assumptions</option>
          <option value="Issue">Issues</option>
          <option value="Dependency">Dependencies</option>
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (item of items(); track item.id) {
        <a class="register__row" [routerLink]="['/projects', item.projectId]" [queryParams]="{ tab: 'raid' }">
          <span><strong>{{ item.projectCode }}</strong> <span class="muted">{{ item.projectName }}</span></span>
          <span><strong>{{ item.code }}</strong> {{ item.title }}</span>
          <span [class.register__late]="(item.score ?? 0) >= 15 || item.severity === 'Critical'">{{ item.kind === 'Risk' ? 'Score ' + item.score : item.severity ?? item.kind }}</span>
          <span class="muted">{{ item.ownerName ?? '' }}</span>
          <span [class.register__late]="item.isOverdue">{{ item.dueDate ? (item.dueDate | date: 'd MMM') : '' }}</span>
          <gggs-status-badge [status]="item.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-exclamation-triangle" title="Nothing open" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styleUrl: './registers.css',
})
export class RaidPage {
  private readonly api = inject(ProjectsApi);

  protected readonly items = signal<readonly RaidItem[]>([]);
  protected readonly kind = signal<RaidKind | ''>('');
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.items.set(await this.api.portfolioRaid({ kind: this.kind(), openOnly: true }));
    } catch (error) {
      this.error.set(message(error, 'The RAID log could not be loaded.'));
    }
  }
}
