import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ResourcesApi, SkillsApi } from '@gggs/shared/delivery';
import type { CapacityState, ResourceDashboard, ResourceRow, SkillItem } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, KpiCard, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { message } from './allocation-dialogs';

/**
 * Resource directory (spec §55-56): everyone who can be staffed, with skills and how much of this
 * month is already spoken for - and the headline numbers a resource manager watches.
 */
@Component({
  selector: 'gggs-resources-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, KpiCard, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Resources" subtitle="Who is available, who is stretched, and who is about to free up." />

    @if (dashboard(); as d) {
      <div class="res__kpis">
        <gggs-kpi-card label="People" [value]="d.totalPeople" icon="pi pi-users" />
        <gggs-kpi-card label="Billable" [value]="d.billable" [hint]="d.nonBillable + ' non-billable'" />
        <gggs-kpi-card label="On bench" [value]="d.onBench" [hint]="d.benchCostPerDay ? (d.benchCostPerDay | number: '1.0-0') + ' a day' : undefined" />
        <gggs-kpi-card label="Over-allocated" [value]="d.overallocated" />
        <gggs-kpi-card label="Partly available" [value]="d.partiallyAvailable" />
        <gggs-kpi-card label="Open requests" [value]="d.openRequests" [hint]="d.openDemand + ' seats'" />
        <gggs-kpi-card label="Releasing in 30 days" [value]="d.upcomingReleases" />
      </div>
      @if (d.releases.length) {
        <section class="panel stack res__releases">
          <h2 class="res__heading">Coming off projects</h2>
          @for (release of d.releases; track release.employeeId + release.projectCode) {
            <a class="res__row res__row--release" [routerLink]="['/resources', release.employeeId]">
              <strong>{{ release.employeeName }}</strong>
              <span>{{ release.projectCode }} · {{ release.allocationPercent }}%</span>
              <span>{{ release.endDate | date: 'd MMM y' }}</span>
              <span class="muted">{{ release.nextAssignment ? 'next: ' + release.nextAssignment : 'nothing lined up' }}</span>
            </a>
          }
        </section>
      }
    }

    <section class="panel toolbar res__filters">
      <label class="field">
        <span class="field__label">Search</span>
        <input type="search" [value]="search()" (input)="search.set($any($event.target).value)" (keyup.enter)="load()" (search)="load()" />
      </label>
      <label class="field">
        <span class="field__label">Skill</span>
        <select [value]="skillId()" (change)="skillId.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (skill of skills(); track skill.id) {
            <option [value]="skill.id">{{ skill.name }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Availability</span>
        <select [value]="state()" (change)="state.set($any($event.target).value); load()">
          <option value="">Anyone</option>
          @for (option of states; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
    </section>

    <section class="panel stack">
      @for (row of rows(); track row.employeeId) {
        <a class="res__row" [routerLink]="['/resources', row.employeeId]">
          <span><strong>{{ row.employeeName }}</strong> <span class="muted">{{ row.employeeCode }}</span><br /><span class="muted">{{ row.designation ?? '' }}{{ row.departmentName ? ' · ' + row.departmentName : '' }}</span></span>
          <span class="res__skills">
            @for (skill of row.skills.slice(0, 4); track skill.skillId) {
              <span class="res__chip" [class.res__chip--primary]="skill.isPrimary">{{ skill.name }} {{ skill.level }}</span>
            }
          </span>
          <span>{{ row.currentProjects.join(', ') || '—' }}</span>
          <span>{{ row.allocatedPercent | number: '1.0-0' }}% allocated{{ row.exitDate ? ' · leaving ' + (row.exitDate | date: 'd MMM') : '' }}</span>
          <gggs-status-badge [status]="row.state" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-address-book" title="No one matches" />
      }
      @if (hasMore()) {
        <button type="button" class="btn" (click)="more()">Show more</button>
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './resources.css',
})
export class ResourcesPage {
  private readonly api = inject(ResourcesApi);
  private readonly skillsApi = inject(SkillsApi);

  protected readonly states: readonly CapacityState[] = ['Bench', 'PartiallyAvailable', 'FullyAllocated', 'Overallocated', 'Leave'];
  protected readonly humanize = humanize;
  protected readonly dashboard = signal<ResourceDashboard | null>(null);
  protected readonly skills = signal<readonly SkillItem[]>([]);
  protected readonly rows = signal<readonly ResourceRow[]>([]);
  protected readonly search = signal('');
  protected readonly skillId = signal('');
  protected readonly state = signal<CapacityState | ''>('');
  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.dashboard().then(
      (dashboard) => this.dashboard.set(dashboard),
      () => undefined,
    );
    this.skillsApi.catalog().then(
      (skills) => this.skills.set(skills),
      () => undefined,
    );
    void this.load();
  }

  protected async load(append = false): Promise<void> {
    this.error.set(null);
    if (!append) {
      this.page.set(1);
    }
    try {
      const result = await this.api.list({ search: this.search(), skillId: this.skillId(), state: this.state(), page: this.page(), pageSize: 50 });
      this.rows.set(append ? [...this.rows(), ...result.items] : result.items);
      this.hasMore.set(result.hasNextPage ?? result.items.length === 50);
    } catch (error) {
      this.error.set(message(error, 'Resources could not be loaded.'));
    }
  }

  protected more(): void {
    this.page.update((page) => page + 1);
    void this.load(true);
  }
}
