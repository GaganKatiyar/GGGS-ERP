import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type {
  CareerPathItem,
  CareerStepInput,
  CareerTrack,
  MyCareer,
  NineBoxView,
  PromotionDetailsInput,
  PromotionItem,
  PromotionStatus,
  SuccessionInput,
  SuccessionItem,
  TalentLevel,
  TalentRow,
} from '@gggs/shared/models';
import { CRITICALITIES, READINESS, TALENT_LEVELS, humanize } from '@gggs/shared/models';
import { MoneyPipe } from '@gggs/shared/money';
import { OrgSelect } from '@gggs/shared/org-reference';
import { CareerApi } from '@gggs/shared/talent';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const split = (value: string) =>
  value
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean);
const blankStep = (): CareerStepInput => ({ title: '', jobId: null, gradeId: null, minMonthsInRole: 12, skills: [], description: null });

/** Career paths: the steps from one role to the next, by job or grade. */
@Component({
  selector: 'gggs-career-paths-page',
  imports: [FormsModule, RouterLink, PageHeader, EmptyState, FormDialog, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Career paths" subtitle="How people grow from one role to the next, and what each step asks for.">
      <a class="btn" routerLink="/career/promotions">Promotions</a>
      <a class="btn" routerLink="/career/succession">Succession</a>
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> New path</button>
      }
    </gggs-page-header>
    @for (path of paths(); track path.id) {
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="car__heading">{{ path.name }}</h2>
          <span class="muted">{{ humanize(path.track) }} track{{ path.departmentName ? ' · ' + path.departmentName : '' }}</span>
          <span class="car__spacer"></span>
          @if (canManage) {
            <button type="button" class="btn btn--sm" (click)="edit(path)">Edit</button>
          }
        </div>
        <ol class="car__ladder">
          @for (step of path.steps; track step.index) {
            <li class="car__step">
              <strong>{{ step.title }}</strong>
              <span class="muted">{{ step.jobName ?? step.gradeName ?? '' }}{{ step.minMonthsInRole ? ' · ' + step.minMonthsInRole + ' months in role' : '' }}</span>
              @if (step.skills.length) {
                <span class="muted">{{ step.skills.join(', ') }}</span>
              }
            </li>
          }
        </ol>
      </section>
    } @empty {
      <gggs-empty-state icon="pi pi-arrow-up-right" title="No career paths yet" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    <gggs-form-dialog [(visible)]="open" [title]="editing() ? 'Edit path' : 'New career path'" [busy]="busy()" [error]="formError()" [saveDisabled]="!name().trim() || steps().length < 2" (save)="save()" width="min(60rem, 96vw)">
      <div class="car__grid">
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" [value]="name()" (input)="name.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Track</span>
          <select [value]="track()" (change)="track.set($any($event.target).value)">
            <option value="Individual">Individual contributor</option>
            <option value="Management">Management</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Department</span>
          <gggs-org-select kind="departments" placeholder="Any" ariaLabel="Department" [ngModel]="departmentId()" (ngModelChange)="departmentId.set($event)" />
        </label>
      </div>
      <h3 class="car__heading">Steps, from the first role up</h3>
      @for (step of steps(); track $index; let i = $index) {
        <div class="car__edit">
          <input type="text" maxlength="120" placeholder="Title" aria-label="Title" [value]="step.title" (input)="patch(i, { title: $any($event.target).value })" />
          <gggs-org-select kind="jobs" placeholder="Job" ariaLabel="Job" [ngModel]="step.jobId ?? null" (ngModelChange)="patch(i, { jobId: $event })" />
          <gggs-org-select kind="grades" placeholder="Grade" ariaLabel="Grade" [ngModel]="step.gradeId ?? null" (ngModelChange)="patch(i, { gradeId: $event })" />
          <input type="number" min="0" max="240" aria-label="Months in role" [value]="step.minMonthsInRole" (input)="patch(i, { minMonthsInRole: +$any($event.target).value })" />
          <input type="text" placeholder="Skills, comma-separated" aria-label="Skills" [value]="step.skills.join(', ')" (change)="patch(i, { skills: split($any($event.target).value) })" />
          <button type="button" class="btn btn--link btn--sm" aria-label="Remove step" (click)="remove(i)"><i class="pi pi-trash" aria-hidden="true"></i></button>
        </div>
      }
      <button type="button" class="btn btn--sm" (click)="add()"><i class="pi pi-plus" aria-hidden="true"></i> Add step</button>
    </gggs-form-dialog>
  `,
  styleUrl: './career.css',
})
export class CareerPathsPage {
  private readonly api = inject(CareerApi);

  protected readonly canManage = inject(PermissionStore).has('career.path.manage');
  protected readonly humanize = humanize;
  protected readonly split = split;
  protected readonly paths = signal<readonly CareerPathItem[]>([]);
  protected readonly open = signal(false);
  protected readonly editing = signal<CareerPathItem | null>(null);
  protected readonly name = signal('');
  protected readonly track = signal<CareerTrack>('Individual');
  protected readonly departmentId = signal<string | null>(null);
  protected readonly steps = signal<readonly CareerStepInput[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.paths().then(
      (paths) => this.paths.set(paths),
      (error: unknown) => this.error.set(message(error, 'Career paths could not be loaded.')),
    );
  }

  protected edit(path: CareerPathItem | null): void {
    this.editing.set(path);
    this.name.set(path?.name ?? '');
    this.track.set(path?.track ?? 'Individual');
    this.departmentId.set(path?.departmentId ?? null);
    this.steps.set(
      path
        ? path.steps.map((step) => ({ title: step.title, jobId: step.jobId ?? null, gradeId: step.gradeId ?? null, minMonthsInRole: step.minMonthsInRole, skills: step.skills, description: step.description ?? null }))
        : [blankStep(), blankStep()],
    );
    this.formError.set(null);
    this.open.set(true);
  }

  protected patch(index: number, change: Partial<CareerStepInput>): void {
    this.steps.update((steps) => steps.map((step, i) => (i === index ? { ...step, ...change } : step)));
  }

  protected add(): void {
    this.steps.update((steps) => [...steps, blankStep()]);
  }

  protected remove(index: number): void {
    this.steps.update((steps) => steps.filter((_, i) => i !== index));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.paths.set(await this.api.savePath(this.editing()?.id ?? null, { name: this.name(), track: this.track(), departmentId: this.departmentId(), steps: this.steps(), isActive: true }));
      this.open.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The path could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

const blankPromotion = (): PromotionDetailsInput => ({
  proposedDesignation: '',
  proposedJobId: null,
  proposedGradeId: null,
  proposedPositionId: null,
  proposedAnnualCtc: null,
  effectiveDate: new Date().toISOString().slice(0, 10),
  justification: '',
});

/** Promotion nominations: proposed by a manager, approved through the workflow, applied on their date. */
@Component({
  selector: 'gggs-promotions-page',
  imports: [DatePipe, FormsModule, RouterLink, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Promotions" subtitle="Nominations, their approval, and the new role and pay applied on the effective date.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openNominate()"><i class="pi pi-plus" aria-hidden="true"></i> Nominate</button>
      }
    </gggs-page-header>
    <section class="panel toolbar car__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (promotion of promotions(); track promotion.id) {
        <a class="car__row" [routerLink]="['/career/promotions', promotion.id]">
          <span><strong>{{ promotion.employeeName ?? '—' }}</strong> <span class="muted">{{ promotion.departmentName ?? '' }}</span></span>
          <span>{{ promotion.currentDesignation ?? '—' }} → {{ promotion.proposedDesignation }}</span>
          <span>{{ promotion.proposedAnnualCtc | gggsMoney }}{{ promotion.increasePercent !== undefined ? ' (+' + promotion.increasePercent + '%)' : '' }}</span>
          <span class="muted">from {{ promotion.effectiveDate | date: 'd MMM y' }}{{ promotion.rating ? ' · rated ' + promotion.rating + '/5' : '' }}</span>
          <span><gggs-status-badge [status]="promotion.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-arrow-up-right" title="No promotions here" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
    <gggs-form-dialog [(visible)]="nominateOpen" title="Nominate for promotion" [saveLabel]="submit() ? 'Nominate and submit' : 'Save draft'" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId() || !form().proposedDesignation.trim() || !form().justification.trim()" (save)="nominate()" size="lg">
      <label class="field">
        <span class="field__label">Employee</span>
        <gggs-employee-select ariaLabel="Employee" [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
      </label>
      <div class="car__grid">
        <label class="field">
          <span class="field__label">New designation</span>
          <input type="text" maxlength="200" [value]="form().proposedDesignation" (input)="patch({ proposedDesignation: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">New job</span>
          <gggs-org-select kind="jobs" placeholder="Unchanged" ariaLabel="Job" [ngModel]="form().proposedJobId ?? null" (ngModelChange)="patch({ proposedJobId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">New grade</span>
          <gggs-org-select kind="grades" placeholder="Unchanged" ariaLabel="Grade" [ngModel]="form().proposedGradeId ?? null" (ngModelChange)="patch({ proposedGradeId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">New annual CTC</span>
          <input type="number" min="0" [value]="form().proposedAnnualCtc ?? ''" (input)="patch({ proposedAnnualCtc: $any($event.target).value === '' ? null : +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Effective from</span>
          <input type="date" [value]="form().effectiveDate" (change)="patch({ effectiveDate: $any($event.target).value })" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Why</span>
        <textarea rows="3" maxlength="4000" [value]="form().justification" (input)="patch({ justification: $any($event.target).value })"></textarea>
      </label>
      <label class="car__flag"><input type="checkbox" [checked]="submit()" (change)="submit.set($any($event.target).checked)" /> <span>Submit for approval now</span></label>
    </gggs-form-dialog>
  `,
  styleUrl: './career.css',
})
export class PromotionsPage {
  private readonly api = inject(CareerApi);
  private readonly router = inject(Router);

  protected readonly canManage = inject(PermissionStore).has('career.promotion.manage');
  protected readonly humanize = humanize;
  protected readonly statuses: readonly PromotionStatus[] = ['Draft', 'PendingApproval', 'Approved', 'Applied', 'Rejected', 'Withdrawn'];
  protected readonly promotions = signal<readonly PromotionItem[]>([]);
  protected readonly status = signal<PromotionStatus | ''>('');
  protected readonly nominateOpen = signal(false);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly form = signal<PromotionDetailsInput>(blankPromotion());
  protected readonly submit = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected patch(change: Partial<PromotionDetailsInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected openNominate(): void {
    this.employeeId.set(null);
    this.form.set(blankPromotion());
    this.formError.set(null);
    this.nominateOpen.set(true);
  }

  protected async nominate(): Promise<void> {
    const employeeId = this.employeeId();
    if (!employeeId) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      const promotion = await this.api.nominate(employeeId, this.form(), this.submit());
      this.nominateOpen.set(false);
      void this.router.navigate(['/career/promotions', promotion.id]);
    } catch (error) {
      this.formError.set(message(error, 'The nomination could not be made.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.promotions.set(await this.api.promotions(this.status() || null));
    } catch (error) {
      this.error.set(message(error, 'Promotions could not be loaded.'));
    }
  }
}

/** One nomination - also where approvers land from their workflow task. */
@Component({
  selector: 'gggs-promotion-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, StatusBadge, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (promotion(); as p) {
      <gggs-page-header [title]="(p.employeeName ?? 'Promotion') + ' → ' + p.proposedDesignation" [subtitle]="'Nominated by ' + (p.nominatedByName ?? '—') + ' on ' + (p.createdDate | date: 'd MMM y')">
        <gggs-status-badge [status]="p.status" />
        @if (p.canEdit) {
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="act('submit')">Submit for approval</button>
        }
        @if (canManage && (p.status === 'Draft' || p.status === 'PendingApproval' || p.status === 'Approved' || p.status === 'Rejected')) {
          <button type="button" class="btn btn--link" [disabled]="busy()" (click)="act('withdraw')">Withdraw</button>
        }
      </gggs-page-header>
      <p><a routerLink="/career/promotions">← Promotions</a></p>
      <section class="panel car__facts">
        <div><span class="field__label">Role</span><p>{{ p.currentDesignation ?? '—' }} → {{ p.proposedDesignation }}</p></div>
        <div><span class="field__label">Job · grade</span><p>{{ p.proposedJobName ?? 'unchanged' }} · {{ p.proposedGradeName ?? 'unchanged' }}</p></div>
        <div><span class="field__label">Annual CTC</span><p>{{ p.currentAnnualCtc | gggsMoney }} → {{ p.proposedAnnualCtc | gggsMoney }}{{ p.increasePercent !== undefined ? ' (+' + p.increasePercent + '%)' : '' }}</p></div>
        <div><span class="field__label">Effective</span><p>{{ p.effectiveDate | date: 'd MMM y' }}{{ p.appliedOn ? ' · applied ' + (p.appliedOn | date: 'd MMM y') : '' }}</p></div>
        <div><span class="field__label">Latest rating</span><p>{{ p.rating ? p.rating + ' / 5' : 'none released' }}</p></div>
      </section>
      <section class="panel"><span class="field__label">Why</span><p class="car__pre">{{ p.justification }}</p></section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './career.css',
})
export class PromotionPage {
  private readonly api = inject(CareerApi);

  readonly id = input.required<string>();

  protected readonly canManage = inject(PermissionStore).has('career.promotion.manage');
  protected readonly promotion = signal<PromotionItem | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.promotion(this.id()).then(
        (promotion) => this.promotion.set(promotion),
        (error: unknown) => this.error.set(message(error, 'The promotion could not be loaded.')),
      );
    });
  }

  protected async act(action: 'submit' | 'withdraw'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.promotion.set(await this.api.promotionAction(this.id(), action));
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

const blankSuccession = (): SuccessionInput => ({ roleTitle: '', positionId: null, departmentId: null, incumbentEmployeeId: null, criticality: 'High', vacancyRisk: 'Medium', notes: null, successors: [] });

/** Succession plans for key roles. */
@Component({
  selector: 'gggs-succession-page',
  imports: [FormsModule, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Succession" subtitle="Key roles, how exposed each is, and who could step in.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> New plan</button>
      }
    </gggs-page-header>
    @for (plan of plans(); track plan.id) {
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="car__heading">{{ plan.roleTitle }}</h2>
          <span class="muted">{{ plan.incumbentName ?? 'vacant' }}{{ plan.departmentName ? ' · ' + plan.departmentName : '' }}</span>
          <span class="car__spacer"></span>
          <gggs-status-badge [status]="humanize(plan.criticality) + ' criticality'" />
          <gggs-status-badge [status]="plan.covered ? 'Covered' : 'Not covered'" />
          @if (plan.canManage) {
            <button type="button" class="btn btn--sm" (click)="edit(plan)">Edit</button>
          }
        </div>
        <p class="muted">Vacancy risk {{ humanize(plan.vacancyRisk) }}{{ plan.notes ? ' · ' + plan.notes : '' }}</p>
        @for (successor of plan.successors; track successor.employeeId) {
          <div class="car__row car__row--static">
            <span><strong>{{ successor.name ?? '—' }}</strong> <span class="muted">{{ successor.designation ?? '' }}</span></span>
            <span>{{ humanize(successor.readiness) }}</span>
            <span class="muted">{{ successor.rating ? 'rated ' + successor.rating + '/5' : 'unrated' }}{{ successor.potential ? ' · ' + successor.potential + ' potential' : '' }}</span>
            <span class="muted">{{ successor.notes ?? '' }}</span>
            <span></span>
          </div>
        } @empty {
          <p class="muted">No successors named.</p>
        }
      </section>
    } @empty {
      <gggs-empty-state icon="pi pi-users" title="No succession plans" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    <gggs-form-dialog [(visible)]="open" [title]="editing() ? 'Edit succession plan' : 'New succession plan'" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().roleTitle.trim()" (save)="save()" size="lg">
      <div class="car__grid">
        <label class="field">
          <span class="field__label">Role</span>
          <input type="text" maxlength="200" [value]="form().roleTitle" (input)="patch({ roleTitle: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Department</span>
          <gggs-org-select kind="departments" placeholder="None" ariaLabel="Department" [ngModel]="form().departmentId ?? null" (ngModelChange)="patch({ departmentId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Held by</span>
          <gggs-employee-select ariaLabel="Incumbent" [ngModel]="form().incumbentEmployeeId ?? null" (ngModelChange)="patch({ incumbentEmployeeId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Criticality</span>
          <select [value]="form().criticality" (change)="patch({ criticality: $any($event.target).value })">
            @for (option of criticalities; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Vacancy risk</span>
          <select [value]="form().vacancyRisk" (change)="patch({ vacancyRisk: $any($event.target).value })">
            @for (option of criticalities; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>
        </label>
      </div>
      <h3 class="car__heading">Successors</h3>
      @for (successor of form().successors; track successor.employeeId; let i = $index) {
        <div class="toolbar">
          <span class="muted">Successor {{ i + 1 }}</span>
          <select aria-label="Readiness" [value]="successor.readiness" (change)="setReadiness(i, $any($event.target).value)">
            @for (option of readiness; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
          <button type="button" class="btn btn--link btn--sm" (click)="removeSuccessor(i)">Remove</button>
        </div>
      }
      <gggs-employee-select ariaLabel="Add a successor" [ngModel]="null" (ngModelChange)="addSuccessor($event)" />
      <label class="field">
        <span class="field__label">Notes</span>
        <textarea rows="2" maxlength="2000" [value]="form().notes ?? ''" (input)="patch({ notes: $any($event.target).value || null })"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './career.css',
})
export class SuccessionPage {
  private readonly api = inject(CareerApi);

  protected readonly canManage = inject(PermissionStore).has('career.succession.manage');
  protected readonly humanize = humanize;
  protected readonly criticalities = CRITICALITIES;
  protected readonly readiness = READINESS;
  protected readonly plans = signal<readonly SuccessionItem[]>([]);
  protected readonly open = signal(false);
  protected readonly editing = signal<SuccessionItem | null>(null);
  protected readonly form = signal<SuccessionInput>(blankSuccession());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.succession().then(
      (plans) => this.plans.set(plans),
      (error: unknown) => this.error.set(message(error, 'Succession plans could not be loaded.')),
    );
  }

  protected edit(plan: SuccessionItem | null): void {
    this.editing.set(plan);
    this.form.set(
      plan
        ? {
            roleTitle: plan.roleTitle,
            positionId: plan.positionId ?? null,
            departmentId: plan.departmentId ?? null,
            incumbentEmployeeId: plan.incumbentEmployeeId ?? null,
            criticality: plan.criticality,
            vacancyRisk: plan.vacancyRisk,
            notes: plan.notes ?? null,
            successors: plan.successors.map((successor) => ({ employeeId: successor.employeeId, readiness: successor.readiness, notes: successor.notes ?? null })),
          }
        : blankSuccession(),
    );
    this.formError.set(null);
    this.open.set(true);
  }

  protected patch(change: Partial<SuccessionInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected addSuccessor(id: string | null): void {
    if (id && !this.form().successors.some((successor) => successor.employeeId === id)) {
      this.patch({ successors: [...this.form().successors, { employeeId: id, readiness: 'OneToTwoYears', notes: null }] });
    }
  }

  protected setReadiness(index: number, readiness: SuccessionInput['successors'][number]['readiness']): void {
    this.patch({ successors: this.form().successors.map((successor, i) => (i === index ? { ...successor, readiness } : successor)) });
  }

  protected removeSuccessor(index: number): void {
    this.patch({ successors: this.form().successors.filter((_, i) => i !== index) });
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.plans.set(await this.api.saveSuccession(this.editing()?.id ?? null, this.form()));
      this.open.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The plan could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** The 9-box talent review: performance from released ratings against assessed potential. */
@Component({
  selector: 'gggs-talent-review-page',
  imports: [FormsModule, PageHeader, KpiCard, Skeleton, FormDialog, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Talent review" subtitle="Performance (the latest released rating) against potential. Assess potential to place someone." />
    <section class="panel toolbar car__filters">
      <label class="field">
        <span class="field__label">Department</span>
        <gggs-org-select kind="departments" placeholder="Everyone" ariaLabel="Department" [ngModel]="departmentId()" (ngModelChange)="departmentId.set($event); load()" />
      </label>
    </section>
    @if (view(); as v) {
      <div class="car__kpis">
        <gggs-kpi-card label="People" [value]="v.people.length" />
        <gggs-kpi-card label="Not yet assessed" [value]="v.unassessed" />
        <gggs-kpi-card label="No released rating" [value]="v.unrated" />
      </div>
      <div class="car__ninebox" role="grid" aria-label="9-box">
        @for (box of order; track box) {
          <div class="car__box" [class]="'car__box car__box--' + box" role="gridcell">
            <span class="car__box-title">{{ labelOf(box) }} <span class="muted">{{ inBox(v, box).length }}</span></span>
            @for (person of inBox(v, box); track person.employeeId) {
              <button type="button" class="car__chip" (click)="openAssess(person)">{{ person.employeeName ?? '—' }}</button>
            }
          </div>
        }
      </div>
      <p class="muted car__axis">Rows: potential (high at the top). Columns: performance (high on the right).</p>
      <section class="panel stack">
        <h2 class="car__heading">Not placed yet</h2>
        @for (person of unplaced(); track person.employeeId) {
          <div class="car__row car__row--static">
            <span><strong>{{ person.employeeName ?? '—' }}</strong> <span class="muted">{{ person.departmentName ?? '' }}</span></span>
            <span>{{ person.rating ? 'Rated ' + person.rating + '/5' : 'No released rating' }}</span>
            <span class="muted">{{ person.potential ? person.potential + ' potential' : 'Potential not assessed' }}</span>
            <span><button type="button" class="btn btn--sm" (click)="openAssess(person)">Assess</button></span>
            <span></span>
          </div>
        } @empty {
          <p class="muted">Everyone is placed.</p>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    <gggs-form-dialog [(visible)]="open" [title]="'Assess · ' + (person()?.employeeName ?? '')" [busy]="busy()" [error]="formError()" (save)="assess()">
      <p class="muted">{{ person()?.rating ? 'Latest rating ' + person()?.rating + '/5.' : 'No released rating yet - they will be placed once one is released.' }}</p>
      <label class="field">
        <span class="field__label">Potential</span>
        <select [value]="potential()" (change)="potential.set($any($event.target).value)">
          @for (level of levels; track level) {
            <option [value]="level">{{ level }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Flight risk</span>
        <select [value]="flightRisk()" (change)="flightRisk.set($any($event.target).value)">
          @for (level of levels; track level) {
            <option [value]="level">{{ level }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Notes</span>
        <textarea rows="2" maxlength="1000" [value]="notes()" (input)="notes.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './career.css',
})
export class TalentReviewPage {
  private readonly api = inject(CareerApi);

  /** Rows by potential, high first; columns by performance, low first. */
  protected readonly order = [7, 8, 9, 4, 5, 6, 1, 2, 3];
  protected readonly levels = TALENT_LEVELS;
  protected readonly view = signal<NineBoxView | null>(null);
  protected readonly departmentId = signal<string | null>(null);
  protected readonly open = signal(false);
  protected readonly person = signal<TalentRow | null>(null);
  protected readonly potential = signal<TalentLevel>('Medium');
  protected readonly flightRisk = signal<TalentLevel>('Low');
  protected readonly notes = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly unplaced = computed(() => (this.view()?.people ?? []).filter((row) => !row.box));

  private static readonly Labels: Readonly<Record<number, string>> = {
    1: 'Underperformer',
    2: 'Effective',
    3: 'Trusted professional',
    4: 'Inconsistent',
    5: 'Core player',
    6: 'High performer',
    7: 'Rough diamond',
    8: 'Emerging star',
    9: 'Star',
  };

  constructor() {
    void this.load();
  }

  protected labelOf(box: number): string {
    return TalentReviewPage.Labels[box] ?? '';
  }

  protected inBox(view: NineBoxView, box: number): readonly TalentRow[] {
    return view.people.filter((row) => row.box === box);
  }

  protected openAssess(row: TalentRow): void {
    this.person.set(row);
    this.potential.set(row.potential ?? 'Medium');
    this.flightRisk.set(row.flightRisk ?? 'Low');
    this.notes.set('');
    this.formError.set(null);
    this.open.set(true);
  }

  protected async assess(): Promise<void> {
    const row = this.person();
    if (!row) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.assess({ employeeId: row.employeeId, potential: this.potential(), flightRisk: this.flightRisk(), notes: this.notes() || null });
      this.open.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The assessment could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.view.set(await this.api.talentReview(this.departmentId()));
    } catch (error) {
      this.error.set(message(error, 'The talent review could not be loaded.'));
    }
  }
}

/** My career: where I stand on my path, the next step, the skills to build, and my promotions. */
@Component({
  selector: 'gggs-my-career-page',
  imports: [DatePipe, PageHeader, KpiCard, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My career" subtitle="Your path, your next step and what it takes." />
    @if (mine(); as m) {
      <div class="car__kpis">
        <gggs-kpi-card label="Role" [value]="m.designation ?? '—'" [hint]="(m.jobName ?? '') + (m.gradeName ? ' · ' + m.gradeName : '')" />
        <gggs-kpi-card label="Latest rating" [value]="m.latestRating ? m.latestRating.rating + ' / 5' : '—'" [hint]="m.latestRating ? m.latestRating.label + ' · ' + m.latestRating.cycleName : 'No released review yet'" />
        <gggs-kpi-card label="With us since" [value]="m.joiningDate ? (m.joiningDate | date: 'MMM y') : '—'" />
      </div>
      @for (position of m.paths; track position.path.id) {
        <section class="panel stack">
          <h2 class="car__heading">{{ position.path.name }}</h2>
          <ol class="car__ladder">
            @for (step of position.path.steps; track step.index) {
              <li class="car__step" [class.car__step--here]="step.index === position.currentStep" [class.car__step--done]="step.index < position.currentStep">
                <strong>{{ step.title }}</strong>
                <span class="muted">{{ step.index === position.currentStep ? 'You are here' : step.index === position.currentStep + 1 ? 'Next step' : '' }}</span>
              </li>
            }
          </ol>
          @if (position.next; as next) {
            <p>
              Next: <strong>{{ next.title }}</strong>.
              {{ position.eligibleByTime ? 'You have the time in role.' : 'Usually after ' + position.path.steps[position.currentStep].minMonthsInRole + ' months in your current role (' + position.monthsInRole + ' so far).' }}
            </p>
            @if (position.skillsToBuild.length) {
              <p>Skills to build: {{ position.skillsToBuild.join(', ') }}.</p>
            } @else {
              <p class="muted">You already have the skills the next step asks for.</p>
            }
          } @else {
            <p class="muted">You are at the top of this path.</p>
          }
        </section>
      } @empty {
        <gggs-empty-state icon="pi pi-arrow-up-right" title="No career path for your role yet" description="Ask HR or your manager about the paths open to you." />
      }
      @if (m.promotions.length) {
        <section class="panel stack">
          <h2 class="car__heading">Promotions</h2>
          @for (promotion of m.promotions; track promotion.id) {
            <div class="car__row car__row--static">
              <span>{{ promotion.currentDesignation ?? '—' }} → <strong>{{ promotion.proposedDesignation }}</strong></span>
              <span class="muted">{{ promotion.effectiveDate | date: 'd MMM y' }}</span>
              <span></span>
              <span></span>
              <span><gggs-status-badge [status]="promotion.status" /></span>
            </div>
          }
        </section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './career.css',
})
export class MyCareerPage {
  protected readonly mine = signal<MyCareer | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(CareerApi)
      .mine()
      .then(
        (mine) => this.mine.set(mine),
        (error: unknown) => this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : message(error, 'Your career could not be loaded.')),
      );
  }
}
