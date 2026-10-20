import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { BenefitType, EnrollmentItem, EnrollmentStatus, MyBenefits, PlanDetails, PlanItem } from '@gggs/shared/models';
import { BENEFIT_TYPES, humanize } from '@gggs/shared/models';
import { BenefitsApi, MoneyPipe } from '@gggs/shared/money';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const blankPlan = (): PlanDetails => ({
  name: '',
  type: 'Health',
  provider: null,
  description: null,
  coverageAmount: null,
  currency: 'INR',
  employeeContribution: 0,
  employerContribution: 0,
  dependentContribution: 0,
  allowsDependents: false,
  maxDependents: 0,
  eligibleEmploymentTypes: [],
  minServiceMonths: 0,
  autoEnroll: false,
  mandatory: false,
  requiresApproval: false,
  enrollmentOpen: true,
  employeeShareTaxDeductible: false,
});

const ICONS: Readonly<Record<BenefitType, string>> = {
  Health: 'pi pi-heart',
  Life: 'pi pi-shield',
  Accident: 'pi pi-exclamation-triangle',
  Meal: 'pi pi-shopping-bag',
  Transport: 'pi pi-car',
  Phone: 'pi pi-mobile',
  Internet: 'pi pi-wifi',
  Wellness: 'pi pi-sun',
  Retirement: 'pi pi-briefcase',
};

/** Benefit plans (spec §24): cover, contributions, eligibility and how people join. */
@Component({
  selector: 'gggs-benefit-plans-page',
  imports: [MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Benefit plans" subtitle="What the company offers, what each plan costs, and who is in it.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> New plan</button>
      }
    </gggs-page-header>
    <section class="ben__cards">
      @for (plan of plans(); track plan.id) {
        <article class="panel stack ben__card">
          <div class="toolbar">
            <i [class]="icon(plan.type)" aria-hidden="true"></i>
            <h2 class="ben__heading">{{ plan.name }}</h2>
            <span class="ben__spacer"></span>
            <gggs-status-badge [status]="plan.isActive ? 'Active' : 'Inactive'" />
          </div>
          <p class="muted">{{ plan.code }} · {{ humanize(plan.type) }}{{ plan.provider ? ' · ' + plan.provider : '' }}{{ plan.coverageAmount ? ' · cover ' + (plan.coverageAmount | gggsMoney: plan.currency) : '' }}</p>
          @if (plan.description) {
            <p>{{ plan.description }}</p>
          }
          <p>Employee {{ plan.employeeContribution | gggsMoney: plan.currency }}{{ plan.dependentContribution ? ' + ' + (plan.dependentContribution | gggsMoney: plan.currency) + ' a dependent' : '' }} · employer {{ plan.employerContribution | gggsMoney: plan.currency }} a month</p>
          <p class="muted">{{ rules(plan) }}</p>
          <p><strong>{{ plan.enrolled }}</strong> enrolled · {{ plan.monthlyEmployerCost | gggsMoney: plan.currency }} a month to the employer</p>
          @if (canManage) {
            <div class="toolbar">
              <button type="button" class="btn btn--sm" (click)="edit(plan)">Edit</button>
              <button type="button" class="btn btn--link btn--sm" (click)="toggle(plan)">{{ plan.isActive ? 'Close plan' : 'Reopen plan' }}</button>
            </div>
          }
        </article>
      } @empty {
        <gggs-empty-state icon="pi pi-heart" title="No plans yet" />
      }
    </section>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing() ? 'Edit plan' : 'New plan'" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().name.trim() || (!editing() && !code().trim())" (save)="save()" size="lg">
      <div class="ben__grid">
        @if (!editing()) {
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" maxlength="30" [value]="code()" (input)="code.set($any($event.target).value.toUpperCase())" />
          </label>
        }
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Type</span>
          <select [value]="form().type" (change)="patch({ type: $any($event.target).value })">
            @for (option of types; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Provider</span>
          <input type="text" maxlength="200" [value]="form().provider ?? ''" (input)="patch({ provider: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Cover amount</span>
          <input type="number" min="0" [value]="form().coverageAmount ?? ''" (input)="patch({ coverageAmount: $any($event.target).value === '' ? null : +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Employee share a month</span>
          <input type="number" min="0" [value]="form().employeeContribution" (input)="patch({ employeeContribution: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Employer share a month</span>
          <input type="number" min="0" [value]="form().employerContribution" (input)="patch({ employerContribution: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Months of service to join</span>
          <input type="number" min="0" max="120" [value]="form().minServiceMonths" (input)="patch({ minServiceMonths: +$any($event.target).value })" />
        </label>
      </div>
      <label class="ben__flag">
        <input type="checkbox" [checked]="form().allowsDependents" (change)="patch({ allowsDependents: $any($event.target).checked })" />
        <span>Dependents can be covered</span>
      </label>
      @if (form().allowsDependents) {
        <div class="ben__grid">
          <label class="field">
            <span class="field__label">Up to</span>
            <input type="number" min="0" max="20" [value]="form().maxDependents" (input)="patch({ maxDependents: +$any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Each dependent adds (employee, a month)</span>
            <input type="number" min="0" [value]="form().dependentContribution" (input)="patch({ dependentContribution: +$any($event.target).value })" />
          </label>
        </div>
      }
      <div class="ben__grid">
        <label class="ben__flag"><input type="checkbox" [checked]="form().autoEnroll" (change)="patch({ autoEnroll: $any($event.target).checked })" /> <span>Enroll joiners automatically</span></label>
        <label class="ben__flag"><input type="checkbox" [checked]="form().mandatory" (change)="patch({ mandatory: $any($event.target).checked, autoEnroll: $any($event.target).checked || form().autoEnroll })" /> <span>Mandatory (no opting out)</span></label>
        <label class="ben__flag"><input type="checkbox" [checked]="form().enrollmentOpen" (change)="patch({ enrollmentOpen: $any($event.target).checked })" /> <span>Open for self-enrollment</span></label>
        <label class="ben__flag"><input type="checkbox" [checked]="form().requiresApproval" (change)="patch({ requiresApproval: $any($event.target).checked })" /> <span>Self-enrollment needs approval</span></label>
        <label class="ben__flag"><input type="checkbox" [checked]="form().employeeShareTaxDeductible" (change)="patch({ employeeShareTaxDeductible: $any($event.target).checked })" /> <span>Employee share is tax-deductible</span></label>
      </div>
      <label class="field">
        <span class="field__label">Employment types (codes, comma-separated; empty for everyone)</span>
        <input type="text" [value]="(form().eligibleEmploymentTypes ?? []).join(', ')" (change)="patch({ eligibleEmploymentTypes: split($any($event.target).value) })" />
      </label>
      <label class="field">
        <span class="field__label">Description</span>
        <textarea rows="3" maxlength="2000" [value]="form().description ?? ''" (input)="patch({ description: $any($event.target).value })"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './benefits.css',
})
export class BenefitPlansPage {
  private readonly api = inject(BenefitsApi);

  protected readonly canManage = inject(PermissionStore).has('benefit.plan.manage');
  protected readonly types = BENEFIT_TYPES;
  protected readonly humanize = humanize;
  protected readonly plans = signal<readonly PlanItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<PlanItem | null>(null);
  protected readonly code = signal('');
  protected readonly form = signal<PlanDetails>(blankPlan());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected icon(type: BenefitType): string {
    return ICONS[type];
  }

  protected rules(plan: PlanItem): string {
    return [
      plan.mandatory ? 'mandatory' : plan.autoEnroll ? 'automatic on joining' : 'opt in',
      plan.enrollmentOpen ? '' : 'enrollment closed',
      plan.requiresApproval ? 'needs approval' : '',
      plan.minServiceMonths ? `after ${plan.minServiceMonths} months` : '',
      plan.allowsDependents ? `up to ${plan.maxDependents} dependents` : 'employee only',
      plan.eligibleEmploymentTypes.length ? plan.eligibleEmploymentTypes.join(', ') : '',
    ]
      .filter(Boolean)
      .join(' · ');
  }

  protected split(value: string): string[] {
    return value
      .split(',')
      .map((part) => part.trim().toUpperCase())
      .filter(Boolean);
  }

  protected edit(plan: PlanItem | null): void {
    this.editing.set(plan);
    this.code.set(plan?.code ?? '');
    this.form.set(plan ? { ...plan } : blankPlan());
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected patch(change: Partial<PlanDetails>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.savePlan(this.editing()?.id ?? null, this.code().trim() || null, this.form());
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The plan could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggle(plan: PlanItem): Promise<void> {
    try {
      await this.api.setPlanActive(plan.id, !plan.isActive);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The plan could not be changed.'));
    }
  }

  private async load(): Promise<void> {
    try {
      this.plans.set(await this.api.plans(true));
    } catch (error) {
      this.error.set(message(error, 'Plans could not be loaded.'));
    }
  }
}

/** Enrollments across plans (spec §24), and enrolling someone on their behalf. */
@Component({
  selector: 'gggs-benefit-enrollments-page',
  imports: [DatePipe, FormsModule, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Enrollments" subtitle="Who is covered by what, from when, and what it costs.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openNew()"><i class="pi pi-plus" aria-hidden="true"></i> Enroll someone</button>
      }
    </gggs-page-header>
    <section class="panel toolbar ben__filters">
      <label class="field">
        <span class="field__label">Plan</span>
        <select [value]="planId()" (change)="planId.set($any($event.target).value); load()">
          <option value="">All</option>
          @for (plan of plans(); track plan.id) {
            <option [value]="plan.id">{{ plan.name }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          <option value="Active">Active</option>
          <option value="Pending">Pending</option>
          <option value="Cancelled">Cancelled</option>
          <option value="Rejected">Rejected</option>
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (enrollment of enrollments(); track enrollment.id) {
        <div class="ben__row">
          <span><strong>{{ enrollment.employeeName ?? enrollment.employeeCode }}</strong> <span class="muted">{{ enrollment.employeeCode }}</span></span>
          <span>{{ enrollment.planName }}{{ enrollment.dependents.length ? ' · ' + enrollment.dependents.length + ' dependent(s)' : '' }}</span>
          <span class="muted">{{ enrollment.effectiveFrom | date: 'd MMM y' }}{{ enrollment.effectiveTo ? ' – ' + (enrollment.effectiveTo | date: 'd MMM y') : '' }} · {{ enrollment.employeeMonthly | gggsMoney }} / {{ enrollment.employerMonthly | gggsMoney }}</span>
          <span class="toolbar">
            <gggs-status-badge [status]="enrollment.status" />
            @if (canManage && (enrollment.status === 'Active' || enrollment.status === 'Pending') && !enrollment.effectiveTo) {
              <button type="button" class="btn btn--link btn--sm" (click)="end(enrollment)">End</button>
            }
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-users" title="No enrollments here" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" title="Enroll someone" saveLabel="Enroll" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId() || !newPlan()" (save)="enroll()">
      <label class="field">
        <span class="field__label">Employee</span>
        <gggs-employee-select ariaLabel="Employee" [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
      </label>
      <label class="field">
        <span class="field__label">Plan</span>
        <select [value]="newPlan()" (change)="newPlan.set($any($event.target).value)">
          <option value="">Choose</option>
          @for (plan of plans(); track plan.id) {
            @if (plan.isActive) {
              <option [value]="plan.id">{{ plan.name }}</option>
            }
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">From</span>
        <input type="date" [value]="effectiveFrom()" (change)="effectiveFrom.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './benefits.css',
})
export class BenefitEnrollmentsPage {
  private readonly api = inject(BenefitsApi);

  protected readonly canManage = inject(PermissionStore).has('benefit.enrollment.manage');
  protected readonly plans = signal<readonly PlanItem[]>([]);
  protected readonly enrollments = signal<readonly EnrollmentItem[]>([]);
  protected readonly planId = signal('');
  protected readonly status = signal<EnrollmentStatus | ''>('Active');
  protected readonly dialogOpen = signal(false);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly newPlan = signal('');
  protected readonly effectiveFrom = signal(new Date().toISOString().slice(0, 10));
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.plans(true).then(
      (plans) => this.plans.set(plans),
      () => undefined,
    );
    void this.load();
  }

  protected openNew(): void {
    this.employeeId.set(null);
    this.newPlan.set('');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async enroll(): Promise<void> {
    const employeeId = this.employeeId();
    if (!employeeId) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.enroll({ employeeId, planId: this.newPlan(), effectiveFrom: this.effectiveFrom() });
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The enrollment could not be made.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async end(enrollment: EnrollmentItem): Promise<void> {
    try {
      await this.api.end(enrollment.id, null);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The enrollment could not be ended.'));
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.enrollments.set(await this.api.enrollments(this.planId() || undefined, this.status()));
    } catch (error) {
      this.error.set(message(error, 'Enrollments could not be loaded.'));
    }
  }
}

/** My benefits (spec §24 self-service): what I am covered by, what I can join, and who is covered with me. */
@Component({
  selector: 'gggs-my-benefits-page',
  imports: [DatePipe, MoneyPipe, PageHeader, KpiCard, StatusBadge, Skeleton, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My benefits" subtitle="Your cover, what you pay, what the company pays, and plans you can join." />
    @if (mine(); as m) {
      <div class="ben__kpis">
        <gggs-kpi-card label="You pay a month" [value]="m.monthlyEmployeeCost | gggsMoney" />
        <gggs-kpi-card label="The company pays a month" [value]="m.monthlyEmployerCost | gggsMoney" />
        <gggs-kpi-card label="Dependents on record" [value]="m.dependents.length" />
      </div>
      <section class="ben__cards">
        @for (offer of m.plans; track offer.plan.id) {
          <article class="panel stack ben__card" [class.ben__card--on]="offer.enrollment?.status === 'Active'">
            <div class="toolbar">
              <i [class]="icon(offer.plan.type)" aria-hidden="true"></i>
              <h2 class="ben__heading">{{ offer.plan.name }}</h2>
              <span class="ben__spacer"></span>
              @if (offer.enrollment; as enrollment) {
                <gggs-status-badge [status]="enrollment.status" />
              }
            </div>
            @if (offer.plan.description) {
              <p>{{ offer.plan.description }}</p>
            }
            <p class="muted">
              {{ offer.plan.coverageAmount ? 'Cover ' + (offer.plan.coverageAmount | gggsMoney) + ' · ' : '' }}you {{ offer.plan.employeeContribution | gggsMoney }}{{ offer.plan.dependentContribution ? ' + ' + (offer.plan.dependentContribution | gggsMoney) + ' a dependent' : '' }} · company {{ offer.plan.employerContribution | gggsMoney }} a month
            </p>
            @if (offer.enrollment; as enrollment) {
              <p>Since {{ enrollment.effectiveFrom | date: 'd MMM y' }}{{ enrollment.effectiveTo ? ', until ' + (enrollment.effectiveTo | date: 'd MMM y') : '' }}{{ enrollment.dependents.length ? ' · covering ' + names(enrollment) : '' }}</p>
              <div class="toolbar">
                @if (offer.plan.allowsDependents && enrollment.status === 'Active' && !enrollment.effectiveTo) {
                  <button type="button" class="btn btn--sm" (click)="openDependents(offer.plan, enrollment)">Dependents</button>
                }
                @if (!enrollment.mandatory && !enrollment.effectiveTo) {
                  <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="cancel(enrollment)">{{ enrollment.status === 'Pending' ? 'Withdraw' : 'Leave at month end' }}</button>
                }
              </div>
            } @else if (offer.ineligibility) {
              <p class="muted">{{ offer.ineligibility }}</p>
            } @else {
              <button type="button" class="btn btn--primary btn--sm" (click)="openJoin(offer.plan)">Join</button>
            }
          </article>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (notice()) {
      <p class="alert alert--success" role="status">{{ notice() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="(enrollmentId() ? 'Dependents · ' : 'Join · ') + (plan()?.name ?? '')" [saveLabel]="enrollmentId() ? 'Save' : 'Join'" [busy]="busy()" [error]="formError()" (save)="saveDialog()">
      @if (plan()?.allowsDependents) {
        <fieldset class="ben__fieldset">
          <legend class="field__label">Cover with me (up to {{ plan()?.maxDependents }})</legend>
          @for (dependent of mine()?.dependents ?? []; track dependent.id) {
            <label class="ben__flag">
              <input type="checkbox" [checked]="chosen().includes(dependent.id)" (change)="toggle(dependent.id, $any($event.target).checked)" />
              <span>{{ dependent.fullName }} <span class="muted">{{ humanize(dependent.relationshipCode) }}</span></span>
            </label>
          } @empty {
            <p class="muted">No dependents on your profile. Add family under My profile first.</p>
          }
        </fieldset>
        <p>You will pay {{ (plan()?.employeeContribution ?? 0) + (plan()?.dependentContribution ?? 0) * chosen().length | gggsMoney }} a month.</p>
      } @else {
        <p>You will pay {{ plan()?.employeeContribution ?? 0 | gggsMoney }} a month from your salary, starting next month{{ plan()?.requiresApproval ? ', once approved' : '' }}.</p>
      }
    </gggs-form-dialog>
  `,
  styleUrl: './benefits.css',
})
export class MyBenefitsPage {
  private readonly api = inject(BenefitsApi);

  protected readonly humanize = humanize;
  protected readonly mine = signal<MyBenefits | null>(null);
  protected readonly dialogOpen = signal(false);
  protected readonly plan = signal<PlanItem | null>(null);
  protected readonly enrollmentId = signal<string | null>(null);
  protected readonly chosen = signal<readonly string[]>([]);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected icon(type: BenefitType): string {
    return ICONS[type];
  }

  protected names(enrollment: EnrollmentItem): string {
    return enrollment.dependents.map((dependent) => dependent.fullName).join(', ');
  }

  protected openJoin(plan: PlanItem): void {
    this.plan.set(plan);
    this.enrollmentId.set(null);
    this.chosen.set([]);
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected openDependents(plan: PlanItem, enrollment: EnrollmentItem): void {
    this.plan.set(plan);
    this.enrollmentId.set(enrollment.id);
    this.chosen.set(enrollment.dependents.map((dependent) => dependent.id));
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected toggle(id: string, on: boolean): void {
    this.chosen.update((chosen) => (on ? [...chosen, id] : chosen.filter((candidate) => candidate !== id)));
  }

  protected async saveDialog(): Promise<void> {
    const plan = this.plan();
    if (!plan) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      const enrollmentId = this.enrollmentId();
      const result = enrollmentId ? await this.api.updateMyDependents(enrollmentId, this.chosen()) : await this.api.enrollMe(plan.id, this.chosen());
      this.dialogOpen.set(false);
      this.notice.set(result.status === 'Pending' ? 'Requested - waiting for approval.' : enrollmentId ? 'Dependents updated.' : `Enrolled in ${plan.name}.`);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'That could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(enrollment: EnrollmentItem): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.cancelMine(enrollment.id);
      this.notice.set('Done.');
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.mine.set(await this.api.mine());
    } catch (error) {
      this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : message(error, 'Your benefits could not be loaded.'));
    }
  }
}
