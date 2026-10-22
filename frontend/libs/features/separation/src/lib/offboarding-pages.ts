import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { SeparationApi } from '@gggs/shared/experience';
import type { HandoverItem, OffboardingOwner, OffboardingStep, OffboardingTask, OffboardingTemplate, SeparationCaseView } from '@gggs/shared/models';
import { OFFBOARDING_OWNERS } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** Who an offboarding step falls to, in words. */
export const ownerLabel = (owner: OffboardingOwner): string =>
  ({ Manager: 'Manager', It: 'IT', Admin: 'Admin', Finance: 'Finance', Hr: 'HR', Employee: 'Leaver', Successor: 'Successor' })[owner];

const kindIcon = (item: HandoverItem): string =>
  ({ DirectReport: 'pi pi-users', ProjectAllocation: 'pi pi-briefcase', ProjectTasks: 'pi pi-list-check', Approvals: 'pi pi-inbox' })[item.kind];

const offsetLabel = (days: number): string => (days === 0 ? 'On the last day' : days < 0 ? `${-days} day${days === -1 ? '' : 's'} before` : `${days} day${days === 1 ? '' : 's'} after`);

/**
 * A separation's offboarding: the checklist and, for HR, the handover of reports, project work and
 * approvals to a successor, and the leaver's sign-in. Used on the case page and on My resignation.
 */
@Component({
  selector: 'gggs-offboarding-panel',
  imports: [FormsModule, DatePipe, StatusBadge, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view().offboarding; as o) {
      @if (mode() === 'hr' && (open() || o.handover.length || o.successorEmployeeId || o.accessDisabledAt)) {
        <section class="panel stack">
          <div class="toolbar">
            <h2 class="xp__heading">Handover</h2>
            @if (canManage() && open()) {
              <button type="button" class="btn btn--sm" (click)="openSuccessor()">{{ o.successorEmployeeId ? 'Change successor' : 'Name successor' }}</button>
              <button type="button" class="btn btn--sm btn--link" [disabled]="busy()" (click)="refresh()">Look again</button>
            }
          </div>
          <div class="xp__grid">
            <div><span class="muted">Successor</span><br />{{ o.successorName ?? 'Not named yet' }}</div>
            <div>
              <span class="muted">Sign-in</span><br />
              @if (o.accessDisabledAt) {
                Switched off {{ o.accessDisabledAt | date: 'd MMM y, HH:mm' }}
              } @else {
                {{ o.accountStatus ?? 'No account' }}{{ o.accountStatus ? ' · switched off automatically after the last day' : '' }}
              }
            </div>
            @if (o.handoverNote) {
              <div class="xp__wide"><span class="muted">Handover note</span><br />{{ o.handoverNote }}</div>
            }
          </div>
          @if (canManage() && open() && !o.accessDisabledAt && o.accountStatus && o.accountStatus !== 'Disabled') {
            <div><button type="button" class="btn btn--sm btn--danger" [disabled]="busy()" (click)="disableAccess()">Switch off access now</button></div>
          }
          @for (item of o.handover; track item.id) {
            <div class="xp__row">
              <span><i [class]="icon(item)" aria-hidden="true"></i>&ngsp;<strong>{{ item.title }}</strong></span>
              <span class="muted">{{ item.outcome ?? item.detail ?? '' }}</span>
              <span>{{ item.needsRecipient ? 'To ' + (item.toName ?? 'nobody yet') : '' }}</span>
              <span class="toolbar">
                @if (canManage() && open() && item.status === 'Open') {
                  <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="act(item)">{{ item.needsRecipient ? 'Hand over' : 'Release' }}</button>
                  <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="skip(item)">Skip</button>
                }
              </span>
              <gggs-status-badge [status]="item.status" />
            </div>
          } @empty {
            <p class="muted">Nothing found to hand over: no reports, project seats, open tasks or pending approvals.</p>
          }
        </section>
      }

      @if (o.tasks.length) {
        <section class="panel stack">
          <div class="toolbar">
            <h2 class="xp__heading">{{ mode() === 'hr' ? 'Offboarding checklist' : 'Your offboarding checklist' }}</h2>
            <span class="muted">{{ done() }} of {{ o.tasks.length }} done</span>
          </div>
          @for (task of visibleTasks(); track task.id) {
            <div class="xp__row">
              <span>
                <strong>{{ task.title }}</strong>
                @if (task.description) {
                  <br /><span class="muted">{{ task.description }}</span>
                }
              </span>
              <span>{{ owner(task.owner) }}{{ task.assigneeName ? ' · ' + task.assigneeName : task.owner === 'It' || task.owner === 'Admin' || task.owner === 'Finance' ? ' team' : '' }}</span>
              <span [class.xp__danger]="task.overdue">{{ task.status === 'Pending' ? 'Due ' + (task.dueDate | date: 'd MMM') : (task.completedByName ? 'by ' + task.completedByName : '') }}{{ task.notes ? ' · ' + task.notes : '' }}</span>
              <span class="toolbar">
                @if (task.canAct) {
                  <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="finish(task, false)">Done</button>
                  @if (!task.required) {
                    <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="finish(task, true)">Skip</button>
                  }
                }
              </span>
              <gggs-status-badge [status]="task.overdue ? 'Overdue' : task.status" />
            </div>
          }
        </section>
      }
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="successorOpen" title="Successor" description="Takes over the leaver's work. Their offboarding steps and the handover of tasks and approvals go to them." [busy]="busy()" [error]="formError()" (save)="saveSuccessor()">
      <div class="stack">
        <label class="field"><span class="field__label">Successor</span><gggs-employee-select [ngModel]="successor()" (ngModelChange)="successor.set($event)" /></label>
        <label class="field"><span class="field__label">Handover note</span><textarea rows="3" maxlength="2000" [value]="note()" (input)="note.set($any($event.target).value)"></textarea></label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="actOpen" title="Hand over" [description]="acting()?.title ?? ''" saveLabel="Hand over" [busy]="busy()" [error]="formError()" [saveDisabled]="!recipient()" (save)="saveAct()">
      <label class="field"><span class="field__label">To</span><gggs-employee-select [ngModel]="recipient()" (ngModelChange)="recipient.set($event)" /></label>
    </gggs-form-dialog>
  `,
})
export class OffboardingPanel {
  private readonly api = inject(SeparationApi);

  readonly view = input.required<SeparationCaseView>();
  readonly mode = input<'hr' | 'self'>('hr');
  readonly canManage = input(false);
  readonly changed = output<SeparationCaseView>();

  protected readonly owner = ownerLabel;
  protected readonly icon = kindIcon;
  protected readonly open = computed(() => this.view().case.status === 'Approved');
  protected readonly done = computed(() => this.view().offboarding?.tasks.filter((task) => task.status !== 'Pending').length ?? 0);
  protected readonly visibleTasks = computed(() => this.view().offboarding?.tasks ?? []);
  protected readonly successorOpen = signal(false);
  protected readonly successor = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly actOpen = signal(false);
  protected readonly acting = signal<HandoverItem | null>(null);
  protected readonly recipient = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  protected openSuccessor(): void {
    const offboarding = this.view().offboarding;
    this.successor.set(offboarding?.successorEmployeeId ?? null);
    this.note.set(offboarding?.handoverNote ?? '');
    this.formError.set(null);
    this.successorOpen.set(true);
  }

  protected saveSuccessor(): Promise<void> {
    return this.run(async () => {
      this.changed.emit(await this.api.setHandover(this.view().case.id, this.successor(), this.note().trim() || null));
      this.successorOpen.set(false);
    });
  }

  protected refresh(): Promise<void> {
    return this.run(async () => this.changed.emit(await this.api.refreshHandover(this.view().case.id)));
  }

  protected act(item: HandoverItem): Promise<void> {
    if (!item.needsRecipient) {
      return confirm(`${item.title}? The seat ends on the last working day.`) ? this.run(async () => this.changed.emit(await this.api.actOnHandover(item.id, { toEmployeeId: null, skip: false, note: null }))) : Promise.resolve();
    }

    this.acting.set(item);
    this.recipient.set(item.toEmployeeId ?? this.view().offboarding?.successorEmployeeId ?? null);
    this.formError.set(null);
    this.actOpen.set(true);
    return Promise.resolve();
  }

  protected saveAct(): Promise<void> {
    const item = this.acting();
    return item
      ? this.run(async () => {
          this.changed.emit(await this.api.actOnHandover(item.id, { toEmployeeId: this.recipient(), skip: false, note: null }));
          this.actOpen.set(false);
        })
      : Promise.resolve();
  }

  protected skip(item: HandoverItem): Promise<void> {
    return this.run(async () => this.changed.emit(await this.api.actOnHandover(item.id, { toEmployeeId: null, skip: true, note: null })));
  }

  protected disableAccess(): Promise<void> {
    return confirm('Switch off their sign-in now? Every open session ends at once.') ? this.run(async () => this.changed.emit(await this.api.disableAccess(this.view().case.id))) : Promise.resolve();
  }

  protected finish(task: OffboardingTask, skipped: boolean): Promise<void> {
    return this.run(async () => {
      const updated = await this.api.finishOffboardingTask(task.id, skipped, null);
      // The leaver's own view hides handover; keep the shape they had.
      this.changed.emit(this.mode() === 'self' && updated.offboarding ? { ...updated, offboarding: { ...updated.offboarding, handover: [] } } : updated);
    });
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.formError.set(null);
    try {
      await work();
    } catch (error) {
      const text = message(error, 'That did not work.');
      this.error.set(text);
      this.formError.set(text);
    } finally {
      this.busy.set(false);
    }
  }
}

/** Offboarding steps waiting on me: my own, my team's queue (IT, admin, finance), or all unassigned for HR. */
@Component({
  selector: 'gggs-offboarding-tasks-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Offboarding tasks" subtitle="Steps for people who are leaving that fall to you or your team." />
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (tasks(); as list) {
      <section class="panel stack">
        @for (task of list; track task.id) {
          <div class="xp__row">
            <span><strong>{{ task.title }}</strong><br /><a class="muted" [routerLink]="['/separation/cases', task.caseId]">{{ task.employeeName ?? 'Leaver' }}</a></span>
            <span>{{ owner(task.owner) }}{{ task.assigneeName ? ' · ' + task.assigneeName : '' }}</span>
            <span [class.xp__danger]="task.overdue">Due {{ task.dueDate | date: 'd MMM y' }}</span>
            <span class="toolbar">
              @if (task.canAct) {
                <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="finish(task, false)">Done</button>
                @if (!task.required) {
                  <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="finish(task, true)">Skip</button>
                }
              }
            </span>
            <gggs-status-badge [status]="task.overdue ? 'Overdue' : task.status" />
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-check-circle" title="Nothing waiting" description="No offboarding steps are open for you." />
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
  `,
})
export class OffboardingTasksPage {
  private readonly api = inject(SeparationApi);

  protected readonly owner = ownerLabel;
  protected readonly tasks = signal<readonly OffboardingTask[] | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async finish(task: OffboardingTask, skipped: boolean): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.finishOffboardingTask(task.id, skipped, null);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.tasks.set(await this.api.offboardingTasks());
    } catch (error) {
      this.error.set(message(error, 'Your offboarding tasks could not be loaded.'));
    }
  }
}

const blankStep = (): OffboardingStep => ({ title: '', description: null, owner: 'Manager', dueOffsetDays: 0, required: true });

/** Offboarding checklists: the default, and ones for an employment type. */
@Component({
  selector: 'gggs-offboarding-templates-page',
  imports: [PageHeader, EmptyState, Skeleton, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Offboarding checklists" subtitle="The steps laid out for a leaver when their separation is approved, due relative to the last working day.">
      <button type="button" class="btn btn--primary" (click)="edit(null)">New checklist</button>
    </gggs-page-header>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (templates(); as list) {
      @for (template of list; track template.id) {
        <section class="panel stack">
          <div class="toolbar">
            <h2 class="xp__heading">{{ template.name }}</h2>
            <span class="muted">{{ template.isDefault ? 'Default for everyone' : template.employmentTypeCode ? 'For ' + template.employmentTypeCode : 'Specific' }}{{ template.isActive ? '' : ' · inactive' }}</span>
            <button type="button" class="btn btn--sm" (click)="edit(template)">Edit</button>
            @if (!template.isDefault) {
              <button type="button" class="btn btn--sm btn--link" [disabled]="busy()" (click)="remove(template)">Remove</button>
            }
          </div>
          @if (template.description) {
            <p class="muted">{{ template.description }}</p>
          }
          @for (step of template.steps; track $index) {
            <div class="xp__row">
              <span><strong>{{ step.title }}</strong></span>
              <span>{{ owner(step.owner) }}</span>
              <span>{{ offset(step.dueOffsetDays) }}</span>
              <span class="muted">{{ step.required ? 'Required' : 'Optional' }}</span>
              <span></span>
            </div>
          }
        </section>
      } @empty {
        <gggs-empty-state icon="pi pi-list-check" title="No checklists" description="Create one to lay out offboarding steps." />
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }

    <gggs-form-dialog [(visible)]="editOpen" [title]="editingId() ? 'Edit checklist' : 'New checklist'" size="lg" [busy]="busy()" [error]="formError()" [saveDisabled]="!name().trim() || !steps().length" (save)="save()">
      <div class="stack">
        <div class="form-grid">
          <label class="field"><span class="field__label">Name</span><input type="text" maxlength="200" [value]="name()" (input)="name.set($any($event.target).value)" /></label>
          <label class="field"><span class="field__label">Employment type (optional)</span><input type="text" maxlength="64" placeholder="CONTRACT" [disabled]="isDefault()" [value]="employmentType()" (input)="employmentType.set($any($event.target).value)" /></label>
          <label class="field form-grid__full"><span class="field__label">Description</span><input type="text" maxlength="1000" [value]="description()" (input)="description.set($any($event.target).value)" /></label>
          <label class="xp__flag"><input type="checkbox" [checked]="isDefault()" (change)="isDefault.set($any($event.target).checked)" /> Default for everyone</label>
          <label class="xp__flag"><input type="checkbox" [checked]="isActive()" (change)="isActive.set($any($event.target).checked)" /> Active</label>
        </div>
        <h3 class="xp__heading">Steps</h3>
        @for (step of steps(); track $index; let i = $index) {
          <div class="toolbar">
            <label class="field"><span class="field__label">Step</span><input type="text" maxlength="200" [value]="step.title" (input)="patch(i, { title: $any($event.target).value })" /></label>
            <label class="field">
              <span class="field__label">Owner</span>
              <select (change)="patch(i, { owner: $any($event.target).value })">
                @for (option of owners; track option) {
                  <option [value]="option" [selected]="option === step.owner">{{ owner(option) }}</option>
                }
              </select>
            </label>
            <label class="field"><span class="field__label">Days from last day</span><input type="number" min="-90" max="30" [value]="step.dueOffsetDays" (input)="patch(i, { dueOffsetDays: +$any($event.target).value })" /></label>
            <label class="xp__flag"><input type="checkbox" [checked]="step.required" (change)="patch(i, { required: $any($event.target).checked })" /> Required</label>
            <button type="button" class="btn btn--sm btn--link" [attr.aria-label]="'Remove step ' + (i + 1)" (click)="removeStep(i)">Remove</button>
          </div>
        }
        <div><button type="button" class="btn btn--sm" (click)="steps.set([...steps(), blank()])">Add step</button></div>
      </div>
    </gggs-form-dialog>
  `,
})
export class OffboardingTemplatesPage {
  private readonly api = inject(SeparationApi);

  protected readonly owner = ownerLabel;
  protected readonly offset = offsetLabel;
  protected readonly owners = OFFBOARDING_OWNERS;
  protected readonly blank = blankStep;
  protected readonly templates = signal<readonly OffboardingTemplate[] | null>(null);
  protected readonly editOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly employmentType = signal('');
  protected readonly departmentId = signal<string | null>(null);
  protected readonly locationId = signal<string | null>(null);
  protected readonly isDefault = signal(false);
  protected readonly isActive = signal(true);
  protected readonly steps = signal<readonly OffboardingStep[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected edit(template: OffboardingTemplate | null): void {
    this.editingId.set(template?.id ?? null);
    this.name.set(template?.name ?? '');
    this.description.set(template?.description ?? '');
    this.employmentType.set(template?.employmentTypeCode ?? '');
    this.departmentId.set(template?.departmentId ?? null);
    this.locationId.set(template?.locationId ?? null);
    this.isDefault.set(template?.isDefault ?? false);
    this.isActive.set(template?.isActive ?? true);
    this.steps.set(template ? [...template.steps] : [blankStep()]);
    this.formError.set(null);
    this.editOpen.set(true);
  }

  protected patch(index: number, change: Partial<OffboardingStep>): void {
    this.steps.set(this.steps().map((step, i) => (i === index ? { ...step, ...change } : step)));
  }

  protected removeStep(index: number): void {
    this.steps.set(this.steps().filter((_, i) => i !== index));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const isDefault = this.isDefault();
      await this.api.saveOffboardingTemplate(this.editingId(), {
        name: this.name().trim(),
        description: this.description().trim() || null,
        departmentId: isDefault ? null : this.departmentId(),
        locationId: isDefault ? null : this.locationId(),
        employmentTypeCode: isDefault ? null : this.employmentType().trim() || null,
        isDefault,
        isActive: this.isActive(),
        steps: this.steps().map((step) => ({ ...step, title: step.title.trim() })),
      });
      this.editOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The checklist could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(template: OffboardingTemplate): Promise<void> {
    if (!confirm(`Remove ${template.name}?`)) {
      return;
    }

    this.busy.set(true);
    try {
      await this.api.removeOffboardingTemplate(template.id);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The checklist could not be removed.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.templates.set(await this.api.offboardingTemplates());
    } catch (error) {
      this.error.set(message(error, 'The checklists could not be loaded.'));
    }
  }
}
