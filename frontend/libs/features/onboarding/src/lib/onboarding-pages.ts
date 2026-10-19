import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { OnboardingApi } from '@gggs/shared/hiring';
import { LookupSelect } from '@gggs/shared/master-data';
import type {
  OnboardingCaseItem,
  OnboardingCaseStatus,
  OnboardingCaseView,
  OnboardingDashboard,
  OnboardingOwners,
  OnboardingTaskItem,
  OnboardingTemplateInput,
  OnboardingTemplateItem,
  OnboardingTemplateTask,
} from '@gggs/shared/models';
import { LOOKUP_LISTS, ONBOARDING_ASSIGNEES, ONBOARDING_CATEGORIES, humanize } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const CATEGORY_ICONS: Readonly<Record<string, string>> = {
  Documents: 'pi pi-file',
  Verification: 'pi pi-shield',
  EmployeeSetup: 'pi pi-id-card',
  Email: 'pi pi-envelope',
  Laptop: 'pi pi-desktop',
  Software: 'pi pi-download',
  Vpn: 'pi pi-lock',
  Access: 'pi pi-key',
  IdCard: 'pi pi-tag',
  Induction: 'pi pi-users',
  Training: 'pi pi-book',
  ManagerIntroduction: 'pi pi-comments',
  Other: 'pi pi-circle',
};

const icon = (category: string) => CATEGORY_ICONS[category] ?? 'pi pi-circle';

/** The checklist of one onboarding: tasks by due date, who owns each, and closing them. */
@Component({
  selector: 'gggs-onboarding-tasks',
  imports: [DatePipe, FormsModule, StatusBadge, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="onb__tasks">
      @for (task of tasks(); track task.id) {
        <li class="onb__task" [class.onb__task--done]="task.status !== 'Pending'" [class.onb__task--late]="task.overdue">
          <i [class]="icon(task.category)" aria-hidden="true"></i>
          <div>
            <strong>{{ task.title }}</strong>{{ task.required ? '' : ' (optional)' }}
            <div class="muted">
              {{ humanize(task.assigneeRole) }}{{ task.assigneeName ? ' · ' + task.assigneeName : ' · no owner yet' }} · due {{ task.dueDate | date: 'd MMM y' }}{{ task.overdue ? ' · overdue' : '' }}
              @if (task.completedDate) {
                · {{ task.status === 'Skipped' ? 'skipped' : 'done' }} {{ task.completedDate | date: 'd MMM' }}{{ task.completedByName ? ' by ' + task.completedByName : '' }}
              }
            </div>
            @if (task.description) {
              <div class="muted onb__small">{{ task.description }}</div>
            }
            @if (task.notes) {
              <div class="onb__small">{{ task.notes }}</div>
            }
          </div>
          <span class="toolbar">
            <gggs-status-badge [status]="task.status" />
            @if (task.canAct && task.status === 'Pending') {
              <button type="button" class="btn btn--primary btn--sm" [disabled]="busy()" (click)="finish(task, false)">Done</button>
              @if (!task.required) {
                <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="finish(task, true)">Skip</button>
              }
            }
            @if (task.canAct && task.status !== 'Pending') {
              <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="reopen(task)">Reopen</button>
            }
            @if (canManage() && task.status === 'Pending') {
              <button type="button" class="btn btn--link btn--sm" (click)="openChange(task)">Reassign</button>
            }
          </span>
        </li>
      } @empty {
        <li class="muted">Nothing here.</li>
      }
    </ul>
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
    <gggs-form-dialog [(visible)]="changeOpen" [title]="'Reassign · ' + (changing()?.title ?? '')" [busy]="busy()" [error]="formError()" (save)="saveChange()">
      <label class="field">
        <span class="field__label">Owner</span>
        <gggs-employee-select ariaLabel="Owner" [ngModel]="assignee()" (ngModelChange)="assignee.set($event)" />
      </label>
      <label class="field">
        <span class="field__label">Due</span>
        <input type="date" [value]="due()" (change)="due.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './onboarding.css',
})
export class OnboardingTasks {
  private readonly api = inject(OnboardingApi);

  readonly tasks = input.required<readonly OnboardingTaskItem[]>();
  readonly canManage = input(false);
  /** Asks the page to reload after a change. */
  readonly changed = input<() => void>(() => undefined);

  protected readonly humanize = humanize;
  protected readonly icon = icon;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly changeOpen = signal(false);
  protected readonly changing = signal<OnboardingTaskItem | null>(null);
  protected readonly assignee = signal<string | null>(null);
  protected readonly due = signal('');

  protected async finish(task: OnboardingTaskItem, skipped: boolean): Promise<void> {
    await this.run(() => this.api.finishTask(task.id, skipped, null));
  }

  protected async reopen(task: OnboardingTaskItem): Promise<void> {
    await this.run(() => this.api.reopenTask(task.id));
  }

  protected openChange(task: OnboardingTaskItem): void {
    this.changing.set(task);
    this.assignee.set(task.assigneeEmployeeId ?? null);
    this.due.set(task.dueDate);
    this.formError.set(null);
    this.changeOpen.set(true);
  }

  protected async saveChange(): Promise<void> {
    const task = this.changing();
    if (!task) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.changeTask(task.id, { assigneeEmployeeId: this.assignee(), dueDate: this.due() || null });
      this.changeOpen.set(false);
      this.changed()();
    } catch (error) {
      this.formError.set(message(error, 'The task could not be changed.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      this.changed()();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Onboarding overview (spec §13): who is joining, how far along each joiner is, and what is late. */
@Component({
  selector: 'gggs-onboarding-dashboard-page',
  imports: [DatePipe, FormsModule, RouterLink, PageHeader, KpiCard, StatusBadge, EmptyState, Skeleton, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Onboarding" subtitle="Pre-boarding and onboarding for every joiner, from documents to the 30-day check-in.">
      @if (canTemplates) {
        <a class="btn" routerLink="/onboarding/templates">Checklists</a>
      }
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="startOpen.set(true)"><i class="pi pi-plus" aria-hidden="true"></i> Start onboarding</button>
      }
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="onb__kpis">
        <gggs-kpi-card label="In progress" [value]="d.open" />
        <gggs-kpi-card label="Pre-boarding" [value]="d.preBoarding" hint="Not joined yet" />
        <gggs-kpi-card label="Joining in 30 days" [value]="d.joiningNext30Days" />
        <gggs-kpi-card label="Completed (30 days)" [value]="d.completedLast30Days" />
        <gggs-kpi-card label="Overdue tasks" [value]="d.overdueTasks" />
      </div>
    }
    <section class="panel toolbar onb__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="open">Open</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
          <option value="">Any</option>
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (item of cases(); track item.id) {
        <a class="onb__row" [routerLink]="['/onboarding/cases', item.id]">
          <span><strong>{{ item.employeeName ?? '—' }}</strong> <span class="muted">{{ item.employeeCode ?? '' }}</span></span>
          <span>{{ item.designation ?? '' }}{{ item.departmentName ? ' · ' + item.departmentName : '' }}</span>
          <span class="muted">joins {{ item.joiningDate | date: 'd MMM y' }}{{ item.managerName ? ' · ' + item.managerName : '' }}</span>
          <span class="onb__progress" [attr.aria-label]="item.progressPercent + '% done'">
            <span class="onb__bar"><span [style.width.%]="item.progressPercent"></span></span>
            {{ item.done }}/{{ item.tasks }}{{ item.overdue ? ' · ' + item.overdue + ' late' : '' }}
          </span>
          <span><gggs-status-badge [status]="item.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-sign-in" title="No joiners here" description="Onboarding starts automatically when someone is hired." />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
    @if (!dashboard() && !error()) {
      <gggs-skeleton [lines]="6" />
    }

    <gggs-form-dialog [(visible)]="startOpen" title="Start onboarding" saveLabel="Start" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId()" (save)="start()">
      <p class="muted">New hires get onboarding automatically. Start one by hand for someone hired before checklists were set up.</p>
      <label class="field">
        <span class="field__label">Employee</span>
        <gggs-employee-select ariaLabel="Employee" [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
      </label>
      <label class="field">
        <span class="field__label">Buddy</span>
        <gggs-employee-select ariaLabel="Buddy" [ngModel]="buddyId()" (ngModelChange)="buddyId.set($event)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './onboarding.css',
})
export class OnboardingDashboardPage {
  private readonly api = inject(OnboardingApi);
  private readonly router = inject(Router);
  private readonly permissions = inject(PermissionStore);

  protected readonly canManage = this.permissions.has('onboarding.case.manage');
  protected readonly canTemplates = this.permissions.has('onboarding.template.view');
  protected readonly humanize = humanize;
  protected readonly statuses: readonly OnboardingCaseStatus[] = ['PreBoarding', 'InProgress', 'Completed', 'Cancelled'];
  protected readonly dashboard = signal<OnboardingDashboard | null>(null);
  protected readonly cases = signal<readonly OnboardingCaseItem[]>([]);
  protected readonly status = signal<string>('open');
  protected readonly startOpen = signal(false);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly buddyId = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.dashboard().then(
      (dashboard) => this.dashboard.set(dashboard),
      (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')),
    );
    void this.load();
  }

  protected async load(): Promise<void> {
    const status = this.status();
    try {
      this.cases.set(await this.api.cases(status === 'open' ? { open: true } : { status: (status || null) as OnboardingCaseStatus | null }));
    } catch (error) {
      this.error.set(message(error, 'Joiners could not be loaded.'));
    }
  }

  protected async start(): Promise<void> {
    const employeeId = this.employeeId();
    if (!employeeId) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      const view = await this.api.start({ employeeId, buddyEmployeeId: this.buddyId() });
      this.startOpen.set(false);
      void this.router.navigate(['/onboarding/cases', view.case.id]);
    } catch (error) {
      this.formError.set(message(error, 'Onboarding could not be started.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One joiner's onboarding. */
@Component({
  selector: 'gggs-onboarding-case-page',
  imports: [DatePipe, FormsModule, RouterLink, PageHeader, StatusBadge, KpiCard, Skeleton, FormDialog, EmployeeSelect, OnboardingTasks],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.case.employeeName ?? 'Joiner'" [subtitle]="(v.case.designation ?? '') + (v.case.departmentName ? ' · ' + v.case.departmentName : '') + ' · ' + v.case.templateName">
        <gggs-status-badge [status]="v.case.status" />
        @if (v.canManage && (v.case.status === 'PreBoarding' || v.case.status === 'InProgress')) {
          <button type="button" class="btn" (click)="buddyOpen.set(true)">{{ v.case.buddyName ? 'Change buddy' : 'Assign a buddy' }}</button>
          <button type="button" class="btn btn--link" [disabled]="busy()" (click)="cancel()">Cancel onboarding</button>
        }
      </gggs-page-header>
      <p><a routerLink="/onboarding">← All joiners</a> · <a [routerLink]="['/employees', v.case.employeeId]">Employee record</a></p>
      <div class="onb__kpis">
        <gggs-kpi-card label="Joining" [value]="v.case.joiningDate | date: 'd MMM y'" />
        <gggs-kpi-card label="Progress" [value]="v.case.progressPercent + '%'" [hint]="v.case.done + ' of ' + v.case.tasks + ' tasks'" />
        <gggs-kpi-card label="Overdue" [value]="v.case.overdue" />
        <gggs-kpi-card label="Manager · buddy" [value]="v.case.managerName ?? '—'" [hint]="v.case.buddyName ?? 'No buddy'" />
      </div>
      <section class="panel">
        <gggs-onboarding-tasks [tasks]="v.tasks" [canManage]="v.canManage" [changed]="reload" />
      </section>
      <gggs-form-dialog [(visible)]="buddyOpen" title="Buddy" [busy]="busy()" [error]="formError()" (save)="saveBuddy()">
        <p class="muted">The buddy is told about their tasks and helps the joiner settle in.</p>
        <gggs-employee-select ariaLabel="Buddy" [ngModel]="buddyId()" (ngModelChange)="buddyId.set($event)" />
      </gggs-form-dialog>
    } @else if (!error()) {
      <gggs-skeleton [lines]="10" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './onboarding.css',
})
export class OnboardingCasePage {
  private readonly api = inject(OnboardingApi);

  readonly id = input.required<string>();

  protected readonly view = signal<OnboardingCaseView | null>(null);
  protected readonly buddyOpen = signal(false);
  protected readonly buddyId = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly reload = () => void this.load();

  constructor() {
    effect(() => {
      this.id();
      void this.load();
    });
  }

  protected async saveBuddy(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.view.set(await this.api.assignBuddy(this.id(), this.buddyId()));
      this.buddyOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The buddy could not be assigned.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    this.busy.set(true);
    try {
      this.view.set(await this.api.cancel(this.id()));
    } catch (error) {
      this.error.set(message(error, 'Onboarding could not be cancelled.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const view = await this.api.case(this.id());
      this.view.set(view);
      this.buddyId.set(view.case.buddyEmployeeId ?? null);
    } catch (error) {
      this.error.set(message(error, 'The onboarding could not be loaded.'));
    }
  }
}

const blankTask = (): OnboardingTemplateTask => ({ title: '', description: null, category: 'Other', assignee: 'Hr', dueOffsetDays: 0, required: true });

/** Configurable checklists (spec §13) and who owns the HR, IT and admin tasks. */
@Component({
  selector: 'gggs-onboarding-templates-page',
  imports: [FormsModule, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect, OrgSelect, LookupSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Onboarding checklists" subtitle="The tasks every joiner goes through. The most specific checklist for their department, location and employment type is used.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> New checklist</button>
      }
    </gggs-page-header>
    @if (owners(); as o) {
      <section class="panel stack">
        <h2 class="onb__heading">Task owners</h2>
        <p class="muted">HR, IT and admin tasks go to these people; manager and buddy tasks go to the joiner's own.</p>
        <div class="onb__grid">
          <label class="field">
            <span class="field__label">HR</span>
            <gggs-employee-select ariaLabel="HR owner" [disabled]="!canManage" [ngModel]="hr()" (ngModelChange)="hr.set($event)" />
          </label>
          <label class="field">
            <span class="field__label">IT</span>
            <gggs-employee-select ariaLabel="IT owner" [disabled]="!canManage" [ngModel]="it()" (ngModelChange)="it.set($event)" />
          </label>
          <label class="field">
            <span class="field__label">Admin</span>
            <gggs-employee-select ariaLabel="Admin owner" [disabled]="!canManage" [ngModel]="admin()" (ngModelChange)="admin.set($event)" />
          </label>
        </div>
        @if (canManage) {
          <div class="toolbar">
            <button type="button" class="btn" [disabled]="busy()" (click)="saveOwners()">Save owners</button>
            @if (notice()) {
              <span class="muted" role="status">{{ notice() }}</span>
            }
          </div>
        }
        <span class="muted onb__small">Currently: HR {{ o.hrOwnerName ?? '—' }} · IT {{ o.itOwnerName ?? '—' }} · Admin {{ o.adminOwnerName ?? '—' }}</span>
      </section>
    }
    @for (template of templates(); track template.id) {
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="onb__heading">{{ template.name }}</h2>
          @if (template.isDefault) {
            <span class="onb__pill">Default</span>
          }
          <span class="onb__spacer"></span>
          <gggs-status-badge [status]="template.isActive ? 'Active' : 'Inactive'" />
          @if (canManage) {
            <button type="button" class="btn btn--sm" (click)="edit(template)">Edit</button>
          }
        </div>
        <p class="muted">{{ scope(template) }}{{ template.description ? ' · ' + template.description : '' }}</p>
        <ol class="onb__steps">
          @for (task of template.tasks; track $index) {
            <li><strong>{{ task.title }}</strong> <span class="muted">{{ humanize(task.assignee) }} · {{ offset(task.dueOffsetDays) }}{{ task.required ? '' : ' · optional' }}</span></li>
          }
        </ol>
      </section>
    } @empty {
      <gggs-empty-state icon="pi pi-list-check" title="No checklists" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing() ? 'Edit checklist' : 'New checklist'" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().name.trim() || !form().tasks.length" (save)="save()" width="min(60rem, 96vw)">
      <div class="onb__grid">
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Department</span>
          <gggs-org-select kind="departments" placeholder="Any" ariaLabel="Department" [ngModel]="form().departmentId ?? null" (ngModelChange)="patch({ departmentId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Location</span>
          <gggs-org-select kind="locations" placeholder="Any" ariaLabel="Location" [ngModel]="form().locationId ?? null" (ngModelChange)="patch({ locationId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Employment type</span>
          <gggs-lookup-select [list]="lists.employmentType" placeholder="Any" ariaLabel="Employment type" [ngModel]="form().employmentTypeCode ?? null" (ngModelChange)="patch({ employmentTypeCode: $event })" />
        </label>
      </div>
      <div class="onb__grid">
        <label class="onb__flag"><input type="checkbox" [checked]="form().isDefault" (change)="patch({ isDefault: $any($event.target).checked })" /> <span>Default for everyone (leave the filters empty)</span></label>
        <label class="onb__flag"><input type="checkbox" [checked]="active()" (change)="active.set($any($event.target).checked)" /> <span>Active</span></label>
      </div>
      <h3 class="onb__heading">Tasks <span class="muted onb__small">due day counts from joining; negative is before day one</span></h3>
      @for (task of form().tasks; track $index; let i = $index) {
        <div class="onb__edit">
          <input type="text" maxlength="200" aria-label="Task" placeholder="Task" [value]="task.title" (input)="patchTask(i, { title: $any($event.target).value })" />
          <select aria-label="Category" [value]="task.category" (change)="patchTask(i, { category: $any($event.target).value })">
            @for (option of categories; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
          <select aria-label="Owner" [value]="task.assignee" (change)="patchTask(i, { assignee: $any($event.target).value })">
            @for (option of assignees; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
          <input type="number" min="-90" max="180" aria-label="Due day" [value]="task.dueOffsetDays" (input)="patchTask(i, { dueOffsetDays: +$any($event.target).value })" />
          <label class="onb__flag"><input type="checkbox" [checked]="task.required" (change)="patchTask(i, { required: $any($event.target).checked })" /> <span>Required</span></label>
          <button type="button" class="btn btn--link btn--sm" aria-label="Remove task" (click)="removeTask(i)"><i class="pi pi-trash" aria-hidden="true"></i></button>
        </div>
      }
      <button type="button" class="btn btn--sm" (click)="addTask()"><i class="pi pi-plus" aria-hidden="true"></i> Add task</button>
    </gggs-form-dialog>
  `,
  styleUrl: './onboarding.css',
})
export class OnboardingTemplatesPage {
  private readonly api = inject(OnboardingApi);

  protected readonly canManage = inject(PermissionStore).has('onboarding.template.manage');
  protected readonly humanize = humanize;
  protected readonly lists = LOOKUP_LISTS;
  protected readonly categories = ONBOARDING_CATEGORIES;
  protected readonly assignees = ONBOARDING_ASSIGNEES;
  protected readonly templates = signal<readonly OnboardingTemplateItem[]>([]);
  protected readonly owners = signal<OnboardingOwners | null>(null);
  protected readonly hr = signal<string | null>(null);
  protected readonly it = signal<string | null>(null);
  protected readonly admin = signal<string | null>(null);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<OnboardingTemplateItem | null>(null);
  protected readonly form = signal<OnboardingTemplateInput>({ name: '', isDefault: false, tasks: [] });
  protected readonly active = signal(true);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    Promise.all([this.api.templates(), this.api.owners()]).then(
      ([templates, owners]) => {
        this.templates.set(templates);
        this.showOwners(owners);
      },
      (error: unknown) => this.error.set(message(error, 'Checklists could not be loaded.')),
    );
  }

  protected scope(template: OnboardingTemplateItem): string {
    const parts = [template.departmentName, template.locationName, template.employmentTypeCode ? humanize(template.employmentTypeCode) : undefined].filter(Boolean);
    return template.isDefault ? 'Everyone without a more specific checklist' : parts.length ? parts.join(' · ') : 'Not narrowed';
  }

  protected offset(days: number): string {
    return days === 0 ? 'joining day' : days < 0 ? `${-days} day(s) before joining` : `day ${days}`;
  }

  protected edit(template: OnboardingTemplateItem | null): void {
    this.editing.set(template);
    this.form.set(
      template
        ? {
            name: template.name,
            description: template.description ?? null,
            departmentId: template.departmentId ?? null,
            locationId: template.locationId ?? null,
            employmentTypeCode: template.employmentTypeCode ?? null,
            isDefault: template.isDefault,
            tasks: template.tasks,
          }
        : { name: '', isDefault: false, tasks: [blankTask()] },
    );
    this.active.set(template?.isActive ?? true);
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected patch(change: Partial<OnboardingTemplateInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected patchTask(index: number, change: Partial<OnboardingTemplateTask>): void {
    this.form.update((form) => ({ ...form, tasks: form.tasks.map((task, i) => (i === index ? { ...task, ...change } : task)) }));
  }

  protected addTask(): void {
    this.form.update((form) => ({ ...form, tasks: [...form.tasks, blankTask()] }));
  }

  protected removeTask(index: number): void {
    this.form.update((form) => ({ ...form, tasks: form.tasks.filter((_, i) => i !== index) }));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.templates.set(await this.api.saveTemplate(this.editing()?.id ?? null, this.form(), this.active()));
      this.dialogOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The checklist could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveOwners(): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);
    this.error.set(null);
    try {
      this.showOwners(await this.api.saveOwners({ hrOwnerEmployeeId: this.hr(), itOwnerEmployeeId: this.it(), adminOwnerEmployeeId: this.admin() }));
      this.notice.set('Saved. Open tasks without an owner have been handed over.');
    } catch (error) {
      this.error.set(message(error, 'The owners could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private showOwners(owners: OnboardingOwners): void {
    this.owners.set(owners);
    this.hr.set(owners.hrOwnerEmployeeId ?? null);
    this.it.set(owners.itOwnerEmployeeId ?? null);
    this.admin.set(owners.adminOwnerEmployeeId ?? null);
  }
}

/** My onboarding: what I need to do before and after I join, and what others are doing for me. */
@Component({
  selector: 'gggs-my-onboarding-page',
  imports: [DatePipe, RouterLink, PageHeader, KpiCard, EmptyState, Skeleton, OnboardingTasks],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My onboarding" subtitle="Everything that happens around your joining, and what is waiting on you.">
      <a class="btn" routerLink="/me/onboarding/tasks">Tasks assigned to me</a>
    </gggs-page-header>
    @if (loaded()) {
      @if (view(); as v) {
        <div class="onb__kpis">
          <gggs-kpi-card label="Joining" [value]="v.case.joiningDate | date: 'd MMM y'" />
          <gggs-kpi-card label="Progress" [value]="v.case.progressPercent + '%'" [hint]="v.case.done + ' of ' + v.case.tasks + ' tasks'" />
          <gggs-kpi-card label="Your manager" [value]="v.case.managerName ?? '—'" />
          <gggs-kpi-card label="Your buddy" [value]="v.case.buddyName ?? 'Not assigned yet'" />
        </div>
        <section class="panel stack">
          <h2 class="onb__heading">For you to do</h2>
          <gggs-onboarding-tasks [tasks]="mine()" [changed]="reload" />
        </section>
        <section class="panel stack">
          <h2 class="onb__heading">Being done for you</h2>
          <gggs-onboarding-tasks [tasks]="others()" [changed]="reload" />
        </section>
      } @else {
        <gggs-empty-state icon="pi pi-sign-in" title="No onboarding" description="You have no onboarding in progress." />
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './onboarding.css',
})
export class MyOnboardingPage {
  private readonly api = inject(OnboardingApi);

  protected readonly view = signal<OnboardingCaseView | null>(null);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly mine = computed(() => (this.view()?.tasks ?? []).filter((task) => task.assigneeRole === 'Employee'));
  protected readonly others = computed(() => (this.view()?.tasks ?? []).filter((task) => task.assigneeRole !== 'Employee'));
  protected readonly reload = () => void this.load();

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.view.set(await this.api.mine());
      this.loaded.set(true);
    } catch (error) {
      this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : message(error, 'Your onboarding could not be loaded.'));
    }
  }
}

/** Onboarding tasks that fall to me across every joiner - as HR, IT, admin, manager or buddy. */
@Component({
  selector: 'gggs-my-onboarding-tasks-page',
  imports: [PageHeader, Skeleton, OnboardingTasks],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Onboarding tasks for me" subtitle="What new joiners are waiting on you for.">
      <label class="onb__flag">
        <input type="checkbox" [checked]="includeDone()" (change)="includeDone.set($any($event.target).checked); load()" />
        <span>Show done</span>
      </label>
    </gggs-page-header>
    @if (tasks(); as list) {
      <section class="panel">
        <gggs-onboarding-tasks [tasks]="list" [changed]="reload" />
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './onboarding.css',
})
export class MyOnboardingTasksPage {
  private readonly api = inject(OnboardingApi);

  protected readonly tasks = signal<readonly OnboardingTaskItem[] | null>(null);
  protected readonly includeDone = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly reload = () => void this.load();

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.tasks.set(await this.api.myTasks(this.includeDone()));
    } catch (error) {
      this.error.set(message(error, 'Your tasks could not be loaded.'));
    }
  }
}
