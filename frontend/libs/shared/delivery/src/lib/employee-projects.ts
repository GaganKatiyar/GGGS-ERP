import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { ProjectMembership } from '@gggs/shared/models';
import { EmptyState, KpiCard, StatusBadge } from '@gggs/shared/ui';
import { ProjectsApi } from './delivery-apis';

/** Employee 360 projects tab: where this person works now and has worked before, with the share of their time. */
@Component({
  selector: 'gggs-employee-projects',
  imports: [DatePipe, RouterLink, KpiCard, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      <div class="kpi-row">
        <gggs-kpi-card label="Current projects" [value]="current().length" />
        <gggs-kpi-card label="Allocated now" [value]="allocation() + '%'" />
        <gggs-kpi-card label="Projects to date" [value]="projectCount()" />
      </div>
      @for (membership of memberships(); track membership.memberId) {
        <div class="e-projects__row">
          <a [routerLink]="['/projects', membership.projectId]"><strong>{{ membership.projectCode }}</strong> {{ membership.projectName }}</a>
          <span>{{ membership.roleName }}</span>
          <span>{{ membership.allocationPercent }}%{{ membership.billable ? '' : ' · non-billable' }}</span>
          <span class="muted">{{ membership.startDate | date: 'd MMM y' }} – {{ membership.endDate ? (membership.endDate | date: 'd MMM y') : 'ongoing' }}</span>
          <gggs-status-badge [status]="membership.isActive ? 'Active' : 'Released'" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-folder" title="Not on any project yet" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </div>
  `,
  styles: `
    .kpi-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .e-projects__row {
      display: grid;
      grid-template-columns: 2fr 1fr 8rem 12rem 7rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    @media (max-width: 48rem) {
      .e-projects__row {
        grid-template-columns: 1fr 1fr;
      }
    }
  `,
})
export class EmployeeProjects {
  private readonly api = inject(ProjectsApi);

  readonly employeeId = input.required<string>();

  protected readonly memberships = signal<readonly ProjectMembership[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly current = computed(() => this.memberships().filter((membership) => membership.isActive));
  protected readonly allocation = computed(() => this.current().reduce((total, membership) => total + membership.allocationPercent, 0));
  protected readonly projectCount = computed(() => new Set(this.memberships().map((membership) => membership.projectId)).size);

  constructor() {
    effect(() => {
      const employeeId = this.employeeId();
      this.api.byEmployee(employeeId).then(
        (rows) => this.memberships.set(rows),
        (error: unknown) => this.error.set(error instanceof Error ? error.message : 'Projects could not be loaded.'),
      );
    });
  }
}
