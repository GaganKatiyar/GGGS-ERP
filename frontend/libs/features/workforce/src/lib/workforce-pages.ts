import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { WorkforceApi } from '@gggs/shared/hiring';
import type {
  ManpowerRequestItem,
  ManpowerRequestStatus,
  WorkforceDashboard,
  WorkforcePlanItem,
  WorkforcePlanLineInput,
  WorkforcePlanView,
} from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { MoneyPipe } from '@gggs/shared/money';
import { OrgSelect } from '@gggs/shared/org-reference';
import { ManpowerRequestForm } from './manpower-request-form';
import { Chart, EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
/** Workforce overview (spec §11): the approved plan against today's headcount, and the demand in flight. */
@Component({
  selector: 'gggs-workforce-dashboard-page',
  imports: [RouterLink, MoneyPipe, PageHeader, KpiCard, Chart, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Workforce planning" subtitle="Planned headcount against the people here today, and the requests on their way to recruitment.">
      <a class="btn" routerLink="/workforce/plans">Plans</a>
      <a class="btn btn--primary" routerLink="/workforce/requests">Manpower requests</a>
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="wfp__kpis">
        <gggs-kpi-card label="Planned headcount" [value]="d.planned" [hint]="d.plan ? d.plan.name + ' (' + d.plan.year + ')' : 'No approved plan'" />
        <gggs-kpi-card label="Headcount today" [value]="d.live" />
        <gggs-kpi-card label="Gap to plan" [value]="d.gap" />
        <gggs-kpi-card label="Open positions" [value]="d.openPositions" hint="Approved, not yet filled" />
        <gggs-kpi-card label="Requests awaiting approval" [value]="d.pendingRequests" />
        <gggs-kpi-card label="Hiring budget" [value]="d.hiringBudget | gggsMoney" />
      </div>
      @if (d.departments.length) {
        <section class="panel">
          <gggs-chart title="Planned and actual headcount by department" [labels]="labels()" [series]="series()" />
        </section>
        <section class="panel stack">
          <h2 class="wfp__heading">Departments</h2>
          <div class="wfp__row wfp__row--head">
            <span>Department</span><span>Planned</span><span>Today</span><span>Gap</span><span>Open</span><span>Pending</span>
          </div>
          @for (row of d.departments; track row.departmentId) {
            <div class="wfp__row">
              <span>{{ row.departmentName ?? '—' }}</span>
              <span>{{ row.planned }}</span>
              <span>{{ row.live }}</span>
              <span [class.wfp__short]="row.gap > 0">{{ row.gap }}</span>
              <span>{{ row.openPositions }}</span>
              <span>{{ row.pendingPositions }}</span>
            </div>
          }
        </section>
      } @else {
        <gggs-empty-state icon="pi pi-compass" title="No plan or demand yet" description="Draft a workforce plan for the year, or raise a manpower request." />
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './workforce.css',
})
export class WorkforceDashboardPage {
  private readonly api = inject(WorkforceApi);

  protected readonly dashboard = signal<WorkforceDashboard | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly labels = computed(() => (this.dashboard()?.departments ?? []).map((row) => row.departmentName ?? '—'));
  protected readonly series = computed(() => {
    const rows = this.dashboard()?.departments ?? [];
    return [
      { label: 'Planned', values: rows.map((row) => row.planned) },
      { label: 'Today', values: rows.map((row) => row.live) },
      { label: 'Open positions', values: rows.map((row) => row.openPositions) },
    ];
  });

  constructor() {
    this.api.dashboard().then(
      (dashboard) => this.dashboard.set(dashboard),
      (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')),
    );
  }
}

/** Workforce plans by year (spec §11). */
@Component({
  selector: 'gggs-workforce-plans-page',
  imports: [DatePipe, FormsModule, RouterLink, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Workforce plans" subtitle="How many people each department should have this year, and the budget behind it.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="open()"><i class="pi pi-plus" aria-hidden="true"></i> New plan</button>
      }
    </gggs-page-header>
    <section class="panel stack">
      @for (plan of plans(); track plan.id) {
        <a class="wfp__row wfp__row--link" [routerLink]="['/workforce/plans', plan.id]">
          <span><strong>{{ plan.name }}</strong> <span class="muted">{{ plan.year }}</span></span>
          <span>{{ plan.currentHeadcount }} → {{ plan.plannedHeadcount }} people</span>
          <span>{{ plan.hiringBudget | gggsMoney }} hiring</span>
          <span class="muted">{{ plan.ownerName ?? '' }} · from {{ plan.effectiveDate | date: 'd MMM y' }}</span>
          <span><gggs-status-badge [status]="plan.status" /></span>
          <span></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-compass" title="No plans yet" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" title="New workforce plan" saveLabel="Create" [busy]="busy()" [error]="formError()" [saveDisabled]="!name().trim()" (save)="create()">
      <label class="field">
        <span class="field__label">Name</span>
        <input type="text" maxlength="200" [value]="name()" (input)="name.set($any($event.target).value)" placeholder="FY27 headcount plan" />
      </label>
      <div class="wfp__grid">
        <label class="field">
          <span class="field__label">Year</span>
          <input type="number" min="2000" max="2100" [value]="year()" (input)="year.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Effective from</span>
          <input type="date" [value]="effectiveDate()" (change)="effectiveDate.set($any($event.target).value)" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Owner</span>
        <gggs-employee-select ariaLabel="Owner" [ngModel]="ownerId()" (ngModelChange)="ownerId.set($event)" />
      </label>
      <label class="field">
        <span class="field__label">Notes</span>
        <textarea rows="3" maxlength="2000" [value]="notes()" (input)="notes.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './workforce.css',
})
export class WorkforcePlansPage {
  private readonly api = inject(WorkforceApi);

  protected readonly canManage = inject(PermissionStore).has('workforce.plan.manage');
  protected readonly plans = signal<readonly WorkforcePlanItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly name = signal('');
  protected readonly year = signal(new Date().getFullYear());
  protected readonly effectiveDate = signal(`${new Date().getFullYear()}-04-01`);
  protected readonly ownerId = signal<string | null>(null);
  protected readonly notes = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected open(): void {
    this.name.set('');
    this.notes.set('');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.savePlan(null, { name: this.name().trim(), year: this.year(), effectiveDate: this.effectiveDate(), ownerEmployeeId: this.ownerId(), notes: this.notes() || null });
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The plan could not be created.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.plans.set(await this.api.plans());
    } catch (error) {
      this.error.set(message(error, 'Plans could not be loaded.'));
    }
  }
}

interface LineDraft {
  departmentId: string | null;
  locationId: string | null;
  jobId: string | null;
  plannedHeadcount: number;
  salaryBudget: number;
  hiringBudget: number;
  notes: string;
}

/** One plan: its lines against live headcount, editing while draft, and submission for approval. */
@Component({
  selector: 'gggs-workforce-plan-page',
  imports: [FormsModule, RouterLink, MoneyPipe, PageHeader, StatusBadge, KpiCard, EmptyState, Skeleton, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.plan.name" [subtitle]="v.plan.year + ' · ' + (v.plan.ownerName ?? 'no owner') + (v.plan.notes ? ' · ' + v.plan.notes : '')">
        <gggs-status-badge [status]="v.plan.status" />
        @if (v.canManage && !editing()) {
          <button type="button" class="btn" (click)="edit(v)">Edit lines</button>
          <button type="button" class="btn btn--primary" [disabled]="busy() || !v.lines.length" (click)="act('submit')">Submit for approval</button>
        }
        @if (canManage && v.plan.status === 'Approved') {
          <button type="button" class="btn" [disabled]="busy()" (click)="act('close')">Close plan</button>
        }
      </gggs-page-header>
      <p><a routerLink="/workforce/plans">← All plans</a></p>
      <div class="wfp__kpis">
        <gggs-kpi-card label="Headcount at planning" [value]="v.plan.currentHeadcount" />
        <gggs-kpi-card label="Planned" [value]="v.plan.plannedHeadcount" />
        <gggs-kpi-card label="Salary budget" [value]="v.plan.salaryBudget | gggsMoney" />
        <gggs-kpi-card label="Hiring budget" [value]="v.plan.hiringBudget | gggsMoney" />
      </div>

      @if (editing()) {
        <section class="panel stack">
          <h2 class="wfp__heading">Lines</h2>
          @for (line of drafts(); track $index; let i = $index) {
            <div class="wfp__edit">
              <gggs-org-select kind="departments" placeholder="Department" ariaLabel="Department" [ngModel]="line.departmentId" (ngModelChange)="patch(i, { departmentId: $event })" />
              <gggs-org-select kind="locations" placeholder="Any location" ariaLabel="Location" [ngModel]="line.locationId" (ngModelChange)="patch(i, { locationId: $event })" />
              <gggs-org-select kind="jobs" placeholder="Any job" ariaLabel="Job" [ngModel]="line.jobId" (ngModelChange)="patch(i, { jobId: $event })" />
              <input type="number" min="0" aria-label="Planned headcount" [value]="line.plannedHeadcount" (input)="patch(i, { plannedHeadcount: +$any($event.target).value })" />
              <input type="number" min="0" aria-label="Salary budget" [value]="line.salaryBudget" (input)="patch(i, { salaryBudget: +$any($event.target).value })" />
              <input type="number" min="0" aria-label="Hiring budget" [value]="line.hiringBudget" (input)="patch(i, { hiringBudget: +$any($event.target).value })" />
              <button type="button" class="btn btn--link btn--sm" (click)="remove(i)" aria-label="Remove line"><i class="pi pi-trash" aria-hidden="true"></i></button>
            </div>
          }
          <p class="muted">Columns: department, location, job, planned headcount, annual salary budget, hiring budget.</p>
          <div class="toolbar">
            <button type="button" class="btn btn--sm" (click)="add()"><i class="pi pi-plus" aria-hidden="true"></i> Add line</button>
            <span class="wfp__spacer"></span>
            <button type="button" class="btn btn--link" (click)="editing.set(false)">Cancel</button>
            <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="saveLines()">Save lines</button>
          </div>
        </section>
      } @else {
        <section class="panel stack">
          <div class="wfp__row wfp__row--head">
            <span>Department · location · job</span><span>At planning → planned</span><span>Today</span><span>Gap</span><span>Open positions</span><span>Budgets</span>
          </div>
          @for (line of v.lines; track line.id) {
            <div class="wfp__row">
              <span><strong>{{ line.departmentName ?? '—' }}</strong> <span class="muted">{{ line.locationName ?? '' }} {{ line.jobName ?? '' }}</span></span>
              <span>{{ line.currentHeadcount }} → {{ line.plannedHeadcount }}</span>
              <span>{{ line.liveHeadcount }}</span>
              <span [class.wfp__short]="line.plannedHeadcount - line.liveHeadcount > 0">{{ line.plannedHeadcount - line.liveHeadcount }}</span>
              <span>{{ line.openPositions }}</span>
              <span class="muted">{{ line.salaryBudget | gggsMoney }} · {{ line.hiringBudget | gggsMoney }}</span>
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-list" title="No lines yet" description="Add the departments this plan covers." />
          }
        </section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './workforce.css',
})
export class WorkforcePlanPage {
  private readonly api = inject(WorkforceApi);

  readonly id = input.required<string>();

  protected readonly canManage = inject(PermissionStore).has('workforce.plan.manage');
  protected readonly view = signal<WorkforcePlanView | null>(null);
  protected readonly editing = signal(false);
  protected readonly drafts = signal<readonly LineDraft[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.id();
      this.api.plan(id).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(message(error, 'The plan could not be loaded.')),
      );
    });
  }

  protected edit(view: WorkforcePlanView): void {
    this.drafts.set(
      view.lines.map((line) => ({
        departmentId: line.departmentId,
        locationId: line.locationId ?? null,
        jobId: line.jobId ?? null,
        plannedHeadcount: line.plannedHeadcount,
        salaryBudget: line.salaryBudget,
        hiringBudget: line.hiringBudget,
        notes: line.notes ?? '',
      })),
    );
    this.editing.set(true);
  }

  protected add(): void {
    this.drafts.update((lines) => [...lines, { departmentId: null, locationId: null, jobId: null, plannedHeadcount: 0, salaryBudget: 0, hiringBudget: 0, notes: '' }]);
  }

  protected remove(index: number): void {
    this.drafts.update((lines) => lines.filter((_, i) => i !== index));
  }

  protected patch(index: number, change: Partial<LineDraft>): void {
    this.drafts.update((lines) => lines.map((line, i) => (i === index ? { ...line, ...change } : line)));
  }

  protected async saveLines(): Promise<void> {
    const lines: WorkforcePlanLineInput[] = [];
    for (const line of this.drafts()) {
      if (!line.departmentId) {
        this.error.set('Every line needs a department.');
        return;
      }

      lines.push({ ...line, departmentId: line.departmentId, notes: line.notes || null });
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      this.view.set(await this.api.saveLines(this.id(), lines));
      this.editing.set(false);
    } catch (error) {
      this.error.set(message(error, 'The lines could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async act(action: 'submit' | 'close'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.view.set(await this.api.planAction(this.id(), action));
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Manpower requests (spec §11): raised by managers, checked against the plan, approved into recruitment. */
@Component({
  selector: 'gggs-manpower-requests-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, ManpowerRequestForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Manpower requests" subtitle="Who is asking for which people, how many, and where each request stands.">
      @if (canRaise) {
        <button type="button" class="btn btn--primary" (click)="formOpen.set(true)"><i class="pi pi-plus" aria-hidden="true"></i> Raise a request</button>
      }
    </gggs-page-header>
    <section class="panel toolbar wfp__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
      <label class="wfp__flag">
        <input type="checkbox" [checked]="mine()" (change)="mine.set($any($event.target).checked); load()" />
        <span>Only mine</span>
      </label>
    </section>
    <section class="panel stack">
      @for (request of requests(); track request.id) {
        <a class="wfp__row wfp__row--link" [routerLink]="['/workforce/requests', request.id]">
          <span><strong>{{ request.title }}</strong> <span class="muted">{{ request.number }}</span></span>
          <span>{{ request.positions }} × · {{ request.departmentName ?? '—' }}</span>
          <span class="muted">{{ request.requesterName ?? '' }} · {{ request.createdDate | date: 'd MMM y' }}</span>
          <span>{{ humanize(request.priority) }} · {{ humanize(request.reason) }}</span>
          <span><gggs-status-badge [status]="request.status" /></span>
          <span class="muted">{{ request.requiredBy ? 'by ' + (request.requiredBy | date: 'd MMM y') : '' }}</span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-user-plus" title="No requests here" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
    <gggs-manpower-request-form [(visible)]="formOpen" (saved)="load()" />
  `,
  styleUrl: './workforce.css',
})
export class ManpowerRequestsPage {
  private readonly api = inject(WorkforceApi);

  protected readonly canRaise = inject(PermissionStore).has('workforce.request.manage');
  protected readonly humanize = humanize;
  protected readonly statuses: readonly ManpowerRequestStatus[] = ['Draft', 'PendingApproval', 'Approved', 'Rejected', 'Fulfilled', 'Cancelled'];
  protected readonly requests = signal<readonly ManpowerRequestItem[]>([]);
  protected readonly status = signal<ManpowerRequestStatus | ''>('');
  protected readonly mine = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.requests.set(await this.api.requests({ status: this.status() || null, mine: this.mine() || null }));
    } catch (error) {
      this.error.set(message(error, 'Requests could not be loaded.'));
    }
  }
}

/** One manpower request, with its fit against the plan - also where approvers land from their task. */
@Component({
  selector: 'gggs-manpower-request-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, StatusBadge, Skeleton, ManpowerRequestForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (request(); as r) {
      <gggs-page-header [title]="r.title" [subtitle]="r.number + ' · raised by ' + (r.requesterName ?? '—') + ' on ' + (r.createdDate | date: 'd MMM y')">
        <gggs-status-badge [status]="r.status" />
        @if (r.canEdit) {
          <button type="button" class="btn" (click)="formOpen.set(true)">Edit</button>
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="act('submit')">Submit for approval</button>
        }
        @if (r.canEdit || (canManage && (r.status === 'PendingApproval' || r.status === 'Approved'))) {
          <button type="button" class="btn btn--link" [disabled]="busy()" (click)="act('cancel')">Cancel request</button>
        }
      </gggs-page-header>
      <p><a routerLink="/workforce/requests">← All requests</a></p>
      @if (r.planFit; as fit) {
        <section class="panel wfp__fit" [class.wfp__fit--over]="!fit.within">
          <i [class]="fit.within ? 'pi pi-check-circle' : 'pi pi-exclamation-triangle'" aria-hidden="true"></i>
          <span>
            {{ fit.within ? 'Within' : 'Beyond' }} {{ fit.planName }}: {{ fit.planned }} planned, {{ fit.current }} here, {{ fit.committed }} already approved -
            room for {{ fit.remaining }}, asking for {{ r.positions }}.
          </span>
        </section>
      } @else {
        <section class="panel wfp__fit wfp__fit--over"><i class="pi pi-info-circle" aria-hidden="true"></i> <span>No approved workforce plan covers this department for the year.</span></section>
      }
      <section class="panel wfp__facts">
        <div><span class="field__label">Department</span><p>{{ r.departmentName ?? '—' }}</p></div>
        <div><span class="field__label">Job · location</span><p>{{ r.jobName ?? 'Any job' }} · {{ r.locationName ?? 'Any location' }}</p></div>
        <div><span class="field__label">Positions</span><p>{{ r.positions }}</p></div>
        <div><span class="field__label">Employment</span><p>{{ humanize(r.employmentTypeCode) }} · {{ humanize(r.workModeCode) }}</p></div>
        <div><span class="field__label">Experience</span><p>{{ r.minExperienceYears ?? 0 }}–{{ r.maxExperienceYears ?? 'any' }} years</p></div>
        <div><span class="field__label">Salary range</span><p>{{ r.salaryMin | gggsMoney }} – {{ r.salaryMax | gggsMoney }}</p></div>
        <div><span class="field__label">Budget</span><p>{{ r.budget | gggsMoney }}</p></div>
        <div><span class="field__label">Priority · needed by</span><p>{{ humanize(r.priority) }} · {{ r.requiredBy ? (r.requiredBy | date: 'd MMM y') : 'no date' }}</p></div>
        <div><span class="field__label">Reason</span><p>{{ humanize(r.reason) }}{{ r.replacementForName ? ' for ' + r.replacementForName : '' }}</p></div>
        <div><span class="field__label">Skills</span><p>{{ r.skills.join(', ') || '—' }}</p></div>
      </section>
      @if (r.justification) {
        <section class="panel"><span class="field__label">Justification</span><p>{{ r.justification }}</p></section>
      }
      <gggs-manpower-request-form [(visible)]="formOpen" [request]="r" (saved)="reload()" />
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './workforce.css',
})
export class ManpowerRequestPage {
  private readonly api = inject(WorkforceApi);

  readonly id = input.required<string>();

  protected readonly canManage = inject(PermissionStore).has('workforce.request.manage');
  protected readonly humanize = humanize;
  protected readonly request = signal<ManpowerRequestItem | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.id();
      void this.reload();
    });
  }

  protected async reload(): Promise<void> {
    try {
      this.request.set(await this.api.request(this.id()));
    } catch (error) {
      this.error.set(message(error, 'The request could not be loaded.'));
    }
  }

  protected async act(action: 'submit' | 'cancel'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.request.set(await this.api.requestAction(this.id(), action));
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}
