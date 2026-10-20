import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { ResourceProfileView, SkillProfile, TimesheetRow } from '@gggs/shared/models';
import { SKILL_LEVEL_LABELS } from '@gggs/shared/models';
import { EmptyState, KpiCard, StatusBadge } from '@gggs/shared/ui';
import { ResourcesApi, SkillsApi, TimesheetsApi } from './resourcing-apis';

const message = (error: unknown, fallback: string) => (error instanceof Error && error.message ? error.message : fallback);

/** Employee 360 skills tab: skills with level, target and gap, and certifications. */
@Component({
  selector: 'gggs-employee-skills',
  imports: [DatePipe, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (skill of profile()?.skills ?? []; track skill.id) {
        <div class="e-res__row">
          <strong>{{ skill.skillName }}</strong>
          <span class="muted">{{ skill.category }}{{ skill.isPrimary ? ' · primary' : '' }}</span>
          <span class="e-res__level" [attr.aria-label]="'Level ' + skill.currentLevel + ' of 5'">
            @for (dot of dots; track dot) {
              <span class="e-res__dot" [class.e-res__dot--on]="dot <= skill.currentLevel" [class.e-res__dot--target]="dot > skill.currentLevel && dot <= (skill.targetLevel ?? 0)"></span>
            }
            {{ levels[skill.currentLevel] }}
          </span>
          <span class="muted">{{ skill.gap ? 'gap ' + skill.gap : '' }}{{ skill.yearsExperience ? ' · ' + skill.yearsExperience + ' yrs' : '' }}</span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-bolt" title="No skills recorded" />
      }
      @if (profile()?.certifications?.length) {
        <h3 class="e-res__heading">Certifications</h3>
        @for (certification of profile()?.certifications ?? []; track certification.id) {
          <div class="e-res__row">
            <strong>{{ certification.name }}</strong>
            <span class="muted">{{ certification.issuer ?? '' }}</span>
            <span>{{ certification.expiresOn ? 'expires ' + (certification.expiresOn | date: 'd MMM y') : 'no expiry' }}</span>
            <span [class.e-res__expired]="!certification.isCurrent">{{ certification.isCurrent ? '' : 'expired' }}</span>
          </div>
        }
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styleUrl: './employee-resourcing.css',
})
export class EmployeeSkills {
  private readonly api = inject(SkillsApi);

  readonly employeeId = input.required<string>();

  protected readonly dots = [1, 2, 3, 4, 5];
  protected readonly levels = SKILL_LEVEL_LABELS;
  protected readonly profile = signal<SkillProfile | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.profile(this.employeeId()).then(
        (profile) => this.profile.set(profile),
        (error: unknown) => this.error.set(message(error, 'Skills could not be loaded.')),
      );
    });
  }
}

/** Employee 360 allocation tab: current and upcoming allocations, and the next months' capacity. */
@Component({
  selector: 'gggs-employee-allocation',
  imports: [DatePipe, DecimalPipe, RouterLink, KpiCard, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (profile(); as p) {
      <div class="stack">
        <div class="kpi-row">
          <gggs-kpi-card label="Allocated this month" [value]="(p.capacity[0]?.allocatedPercent ?? 0 | number: '1.0-0') + '%'" />
          <gggs-kpi-card label="Available this month" [value]="(p.capacity[0]?.availablePercent ?? 0 | number: '1.0-0') + '%'" />
          <gggs-kpi-card label="Current projects" [value]="p.current.length" />
        </div>
        @for (allocation of p.current.concat(p.upcoming); track allocation.memberId) {
          <div class="e-res__row">
            <a [routerLink]="['/projects', allocation.projectId]"><strong>{{ allocation.projectCode }}</strong> {{ allocation.projectName }}</a>
            <span>{{ allocation.roleName }} · {{ allocation.allocationPercent }}%</span>
            <span class="muted">{{ allocation.startDate | date: 'd MMM y' }} – {{ allocation.endDate ? (allocation.endDate | date: 'd MMM y') : 'ongoing' }}</span>
            <span>{{ allocation.billable ? 'billable' : 'non-billable' }}</span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-calendar" title="Not allocated to any project" />
        }
        <div class="e-res__months">
          @for (cell of p.capacity; track cell.from) {
            <div class="e-res__month">
              <span class="muted">{{ cell.from | date: 'MMM' }}</span>
              <strong>{{ cell.allocatedPercent | number: '1.0-0' }}%</strong>
              <gggs-status-badge [status]="cell.state" />
            </div>
          }
        </div>
      </div>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './employee-resourcing.css',
})
export class EmployeeAllocation {
  private readonly api = inject(ResourcesApi);

  readonly employeeId = input.required<string>();

  protected readonly profile = signal<ResourceProfileView | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.profile(this.employeeId()).then(
        (profile) => this.profile.set(profile),
        (error: unknown) => this.error.set(message(error, 'Allocation could not be loaded.')),
      );
    });
  }
}

/** Employee 360 timesheet tab: the person's recent weeks. */
@Component({
  selector: 'gggs-employee-timesheets',
  imports: [DatePipe, DecimalPipe, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (row of rows(); track row.id) {
        <div class="e-res__row">
          <strong>Week of {{ row.weekStart | date: 'd MMM y' }}</strong>
          <span>{{ row.totalHours | number: '1.0-2' }} h · {{ row.billableHours | number: '1.0-2' }} billable</span>
          <span class="muted">{{ row.isLate ? 'late' : '' }}{{ row.correctionCount ? ' · corrected' : '' }}</span>
          <gggs-status-badge [status]="row.status" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-stopwatch" title="No timesheets yet" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styleUrl: './employee-resourcing.css',
})
export class EmployeeTimesheets {
  private readonly api = inject(TimesheetsApi);

  readonly employeeId = input.required<string>();

  protected readonly rows = signal<readonly TimesheetRow[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.list({ employeeId: this.employeeId(), pageSize: 12 }).then(
        (page) => this.rows.set(page.items),
        (error: unknown) => this.error.set(message(error, 'Timesheets could not be loaded.')),
      );
    });
  }
}
