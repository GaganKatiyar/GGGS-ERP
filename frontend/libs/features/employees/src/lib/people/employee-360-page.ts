import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeAllocation, EmployeeProjects, EmployeeSkills, EmployeeTimesheets } from '@gggs/shared/delivery';
import { EmployeeBenefits, EmployeeCompensation, EmployeeLoans, EmployeePayroll } from '@gggs/shared/money';
import { EmployeeDocuments } from '@gggs/shared/documents';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import { EmployeeOnboarding } from '@gggs/shared/hiring';
import { EmployeeAssets, EmployeeExpenses, EmployeeHelpdesk, EmployeeRewards } from '@gggs/shared/experience';
import { EmployeeCareer, EmployeeGoals, EmployeeLearning, EmployeePerformance } from '@gggs/shared/talent';
import { EmployeeAttendance, EmployeeLeave } from '@gggs/shared/time';
import type { Employee360, Employee360Section } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';
import { AccountsTab } from './tabs/accounts-tab';
import { EducationTab } from './tabs/education-tab';
import { EmploymentTab } from './tabs/employment-tab';
import { ExperienceTab } from './tabs/experience-tab';
import { FamilyTab } from './tabs/family-tab';
import { HistoryTab } from './tabs/history-tab';
import { PersonalTab } from './tabs/personal-tab';
import { StatutoryTab } from './tabs/statutory-tab';

/**
 * Employee 360: one person, every module's view of them. Core HR sections are live; sections owned
 * by modules still on the roadmap are listed with the milestone they arrive in, so the shape of the
 * page is settled now and each module only fills in its tab.
 */
@Component({
  selector: 'gggs-employee-360-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, Tabs, Skeleton, EmptyState, AccountsTab, EmployeeDocuments, EmployeeAttendance, EmployeeLeave, EmployeeProjects, EmployeeSkills, EmployeeAllocation, EmployeeTimesheets, EmployeePayroll, EmployeeCompensation, EmployeeBenefits, EmployeeLoans, EmployeeOnboarding, EmployeePerformance, EmployeeGoals, EmployeeLearning, EmployeeCareer, EmployeeRewards, EmployeeAssets, EmployeeExpenses, EmployeeHelpdesk, EmploymentTab, PersonalTab, FamilyTab, EducationTab, ExperienceTab, StatutoryTab, HistoryTab],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="This employee could not be shown" [description]="error()!" />
    } @else if (!data()) {
      <gggs-skeleton [lines]="8" />
    } @else {
      @let p = data()!.profile;
      <gggs-page-header [title]="p.displayName" [subtitle]="p.employeeCode + (p.assignment.designation ? ' · ' + p.assignment.designation : '')">
        <div class="toolbar">
          <gggs-status-badge [status]="p.status" />
          <a class="btn" routerLink="/employees">Back to directory</a>
        </div>
      </gggs-page-header>

      <!-- Compact facts: four short values, so each card is a single line of label and value beside its icon. -->
      <div class="e360__facts">
        <div class="e360__fact" [attr.title]="((p.assignment.parentDepartment ?? p.assignment.department)?.name ?? '') + (p.assignment.parentDepartment ? ' · function: ' + p.assignment.department?.name : '')">
          <span class="e360__icon" aria-hidden="true"><i class="pi pi-share-alt"></i></span>
          <div class="e360__text">
            <span class="e360__label">Department</span>
            <span class="e360__value">{{ (p.assignment.parentDepartment ?? p.assignment.department)?.name ?? '—' }}</span>
          </div>
        </div>
        <div class="e360__fact" [attr.title]="p.assignment.manager?.name ?? null">
          <span class="e360__icon" aria-hidden="true"><i class="pi pi-user"></i></span>
          <div class="e360__text">
            <span class="e360__label">Manager</span>
            <span class="e360__value">{{ p.assignment.manager?.name ?? '—' }}</span>
          </div>
        </div>
        <div class="e360__fact">
          <span class="e360__icon" aria-hidden="true"><i class="pi pi-calendar"></i></span>
          <div class="e360__text">
            <span class="e360__label">Tenure</span>
            <span class="e360__value">{{ tenure() }}</span>
          </div>
        </div>
        <div class="e360__fact">
          <span class="e360__icon" aria-hidden="true"><i class="pi pi-users"></i></span>
          <div class="e360__text">
            <span class="e360__label">Direct reports</span>
            <span class="e360__value">{{ data()!.directReportCount }}</span>
          </div>
        </div>
      </div>

      <gggs-tabs [tabs]="tabs()" [(selected)]="selected" />

      <div class="e360__body" role="tabpanel">
        @switch (selected()) {
          @case ('overview') {
            <section class="panel">
              <dl class="definition-list">
                <dt>Official email</dt><dd>{{ p.officialEmail ?? '—' }}</dd>
                <dt>Mobile</dt><dd>{{ p.mobilePhone ?? '—' }}</dd>
                <dt>Joined</dt><dd>{{ p.joiningDate | date: 'mediumDate' }}</dd>
                <dt>Probation ends</dt><dd>{{ (p.probationEndDate | date: 'mediumDate') ?? '—' }}</dd>
                <dt>Confirmed</dt><dd>{{ (p.confirmationDate | date: 'mediumDate') ?? 'Not yet' }}</dd>
                <dt>Employment type</dt><dd>{{ p.assignment.employmentType.label }}</dd>
                <dt>Work mode</dt><dd>{{ p.assignment.workMode.label }}</dd>
                <dt>Location</dt><dd>{{ p.assignment.location?.name ?? '—' }}</dd>
                @if (p.noticeStartDate) {
                  <dt>Notice started</dt><dd>{{ p.noticeStartDate | date: 'mediumDate' }}</dd>
                }
                @if (p.exitDate) {
                  <dt>Exit</dt><dd>{{ p.exitDate | date: 'mediumDate' }} · {{ p.exitReason?.label }}</dd>
                }
              </dl>
            </section>
          }
          @case ('employment') {
            <gggs-employment-tab [profile]="p" (changed)="reload()" />
          }
          @case ('organization') {
            <section class="panel">
              <dl class="definition-list">
                <dt>Company</dt><dd>{{ p.assignment.company?.name ?? '—' }}</dd>
                <dt>Business unit</dt><dd>{{ p.assignment.businessUnit?.name ?? '—' }}</dd>
                <dt>Department</dt><dd>{{ (p.assignment.parentDepartment ?? p.assignment.department)?.name ?? '—' }}</dd>
                <dt>Function</dt><dd>{{ p.assignment.parentDepartment ? p.assignment.department?.name : '—' }}</dd>
                <dt>Role</dt><dd>{{ p.assignment.role?.name ?? '—' }}</dd>
                <dt>Designation</dt><dd>{{ p.assignment.designation ?? '—' }}</dd>
                <dt>Level</dt><dd>{{ p.assignment.level?.name ?? '—' }}</dd>
                <dt>Cost center</dt><dd>{{ p.assignment.costCenter?.name ?? '—' }}</dd>
                <dt>Manager</dt>
                <dd>
                  @if (p.assignment.manager; as manager) {
                    <a [routerLink]="['/employees', manager.id]">{{ manager.name }}</a>
                  } @else {
                    —
                  }
                </dd>
                <dt>HR business partner</dt><dd>{{ p.assignment.hrbp?.name ?? '—' }}</dd>
              </dl>
            </section>
          }
          @case ('personal') {
            <gggs-personal-tab [profile]="p" (changed)="reload()" />
          }
          @case ('family') {
            <gggs-family-tab [employeeId]="p.id" />
          }
          @case ('education') {
            <section class="panel"><gggs-education-tab [employeeId]="p.id" [editable]="canEditProfile" /></section>
          }
          @case ('experience') {
            <section class="panel"><gggs-experience-tab [employeeId]="p.id" [editable]="canEditProfile" /></section>
          }
          @case ('statutory') {
            <gggs-statutory-tab [profile]="p" (changed)="reload()" />
          }
          @case ('history') {
            <gggs-history-tab [employeeId]="p.id" />
          }
          @case ('attendance') {
            <gggs-employee-attendance [employeeId]="p.id" />
          }
          @case ('leave') {
            <gggs-employee-leave [employeeId]="p.id" />
          }
          @case ('projects') {
            <gggs-employee-projects [employeeId]="p.id" />
          }
          @case ('resources') {
            <gggs-employee-allocation [employeeId]="p.id" />
          }
          @case ('timesheet') {
            <gggs-employee-timesheets [employeeId]="p.id" />
          }
          @case ('skills') {
            <gggs-employee-skills [employeeId]="p.id" />
          }
          @case ('payroll') {
            <gggs-employee-payroll [employeeId]="p.id" />
          }
          @case ('compensation') {
            <gggs-employee-compensation [employeeId]="p.id" />
          }
          @case ('benefits') {
            <gggs-employee-benefits [employeeId]="p.id" />
          }
          @case ('onboarding') {
            <gggs-employee-onboarding [employeeId]="p.id" />
          }
          @case ('performance') {
            <gggs-employee-performance [employeeId]="p.id" />
          }
          @case ('goals') {
            <gggs-employee-goals [employeeId]="p.id" />
          }
          @case ('learning') {
            <gggs-employee-learning [employeeId]="p.id" />
          }
          @case ('career') {
            <gggs-employee-career [employeeId]="p.id" />
          }
          @case ('rewards') {
            <gggs-employee-rewards [employeeId]="p.id" />
          }
          @case ('assets') {
            <gggs-employee-assets [employeeId]="p.id" />
          }
          @case ('expenses') {
            <gggs-employee-expenses [employeeId]="p.id" />
          }
          @case ('loans') {
            <gggs-employee-loans [employeeId]="p.id" />
          }
          @case ('helpdesk') {
            <gggs-employee-helpdesk [employeeId]="p.id" />
          }
          @case ('accounts') {
            <gggs-accounts-tab [employeeId]="p.id" />
          }
          @case ('documents') {
            <gggs-employee-documents [employeeId]="p.id" [canUpload]="canUploadDocuments" [canArchive]="canArchiveDocuments" />
          }
          @default {
            @if (comingSoon(); as section) {
              <gggs-empty-state icon="pi pi-clock" [title]="section.title + ' arrives in ' + section.milestone" description="This part of Employee 360 is filled in when its module ships." />
            }
          }
        }
      </div>
    }
  `,
  styles: `
    .e360__facts {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: var(--gggs-space-3);
      margin: 0 0 var(--gggs-space-4);
    }

    .e360__fact {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      min-width: 0;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
    }

    .e360__icon {
      display: inline-grid;
      flex: none;
      place-items: center;
      width: 2rem;
      height: 2rem;
      border-radius: var(--gggs-radius-sm);
      background: var(--gggs-color-surface-selected);
      color: var(--gggs-color-primary);
    }

    .e360__text {
      min-width: 0;
    }

    .e360__label,
    .e360__value {
      display: block;
    }

    .e360__label {
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-xs);
      line-height: 1.3;
    }

    .e360__value {
      margin: 0;
      overflow: hidden;
      font-size: var(--gggs-font-size-lg);
      font-weight: var(--gggs-font-weight-semibold);
      line-height: 1.35;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    @media (max-width: 760px) {
      .e360__facts {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }

    .e360__body {
      margin-block-start: var(--gggs-space-4);
    }
  `,
})
export class Employee360Page {
  private readonly api = inject(EmployeeDirectoryApi);
  private readonly permissions = inject(PermissionStore);

  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  /** Bound from the `tab` query parameter: open on a section, e.g. after generating a letter. */
  readonly tab = input<string | undefined>(undefined);

  protected readonly canEditProfile = this.permissions.has('employee.profile.edit');
  protected readonly canUploadDocuments = this.permissions.has('document.file.upload');
  protected readonly canArchiveDocuments = this.permissions.has('document.file.delete');

  protected readonly data = signal<Employee360 | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal('overview');

  /** Sections the caller may open; history hides without its permission rather than failing. */
  private readonly sections = computed<readonly Employee360Section[]>(() =>
    [...(this.data()?.sections ?? []), ...(this.data() && this.permissions.has('datahub.integration.view') ? [{ key: 'accounts', title: 'Accounts', available: true } as Employee360Section] : [])].filter(
      (section) =>
        (section.key !== 'history' || this.permissions.has('employee.history.view')) &&
        (section.key !== 'documents' || this.permissions.has('document.file.view')) &&
        (section.key !== 'attendance' || this.permissions.has('attendance.record.view')) &&
        (section.key !== 'leave' || this.permissions.has('leave.balance.view')) &&
        (section.key !== 'resources' || this.permissions.has('resource.profile.view')) &&
        (section.key !== 'timesheet' || this.permissions.has('timesheet.entry.view')) &&
        (section.key !== 'skills' || this.permissions.has('skill.employee-skill.view')) &&
        (section.key !== 'payroll' || this.permissions.has('payroll.employee-salary.view')) &&
        (section.key !== 'compensation' || this.permissions.has('compensation.revision.view')) &&
        (section.key !== 'benefits' || this.permissions.has('benefit.enrollment.view')) &&
        (section.key !== 'loans' || this.permissions.has('loan.loan.view')) &&
        (section.key !== 'onboarding' || this.permissions.has('onboarding.case.view')) &&
        (section.key !== 'performance' || this.permissions.has('performance.review.view')) &&
        (section.key !== 'goals' || this.permissions.has('performance.goal.manage') || this.permissions.has('performance.review.view')) &&
        (section.key !== 'learning' || this.permissions.has('learning.enrollment.view')) &&
        (section.key !== 'career' || this.permissions.has('career.promotion.view')),
    ),
  );

  protected readonly tabs = computed<readonly TabItem[]>(() =>
    this.sections().map((section) => ({
      key: section.key,
      label: section.title,
      disabled: !section.available,
      note: section.available ? undefined : section.milestone,
    })),
  );

  protected readonly comingSoon = computed(() => this.sections().find((section) => section.key === this.selected() && !section.available));

  protected readonly tenure = computed(() => {
    const months = this.data()?.tenureMonths ?? 0;
    const years = Math.floor(months / 12);
    const rest = months % 12;
    return years ? `${years}y ${rest}m` : `${rest}m`;
  });

  constructor() {
    effect(() => {
      const id = this.id();
      this.data.set(null);
      this.selected.set(this.tab() ?? 'overview');
      void this.load(id);
    });
  }

  protected reload(): void {
    void this.load(this.id());
  }

  private async load(id: string): Promise<void> {
    this.error.set(null);
    try {
      this.data.set(await this.api.get360(id));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    }
  }
}
