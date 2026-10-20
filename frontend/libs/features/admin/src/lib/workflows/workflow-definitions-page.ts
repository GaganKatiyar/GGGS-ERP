import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { ApproverKind, ApproverRule, ConditionOperator, StageMode, WorkflowDefinition, WorkflowStage, WorkflowSubject } from '@gggs/shared/models';
import { APPROVER_KIND_LABELS } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { WorkflowApi } from '@gggs/shared/workflow';

interface ApproverDraft {
  kind: ApproverKind;
  employeeId: string | null;
}

interface StageDraft {
  name: string;
  mode: StageMode;
  approvers: ApproverDraft[];
  hasCondition: boolean;
  conditionField: string;
  conditionOperator: ConditionOperator;
  conditionValue: string;
  slaHours: number | null;
  escalate: boolean;
  escalateTo: ApproverDraft;
}

const OPERATORS: readonly { readonly value: ConditionOperator; readonly label: string }[] = [
  { value: 'Equals', label: 'is' },
  { value: 'NotEquals', label: 'is not' },
  { value: 'GreaterThan', label: 'is more than' },
  { value: 'GreaterThanOrEqual', label: 'is at least' },
  { value: 'LessThan', label: 'is less than' },
  { value: 'LessThanOrEqual', label: 'is at most' },
  { value: 'In', label: 'is one of (comma separated)' },
];

const blankStage = (): StageDraft => ({
  name: 'Manager approval',
  mode: 'All',
  approvers: [{ kind: 'ReportingManager', employeeId: null }],
  hasCondition: false,
  conditionField: '',
  conditionOperator: 'GreaterThan',
  conditionValue: '',
  slaHours: 48,
  escalate: false,
  escalateTo: { kind: 'SkipLevelManager', employeeId: null },
});

/**
 * How each kind of request is approved (spec §109). Stages run in order; approvers within a stage
 * work in parallel; a condition can leave a stage out; an SLA moves an unanswered task up a level.
 * One definition per kind of request is active at a time; requests already running keep the
 * stages they started with.
 */
@Component({
  selector: 'gggs-workflow-definitions-page',
  imports: [FormsModule, HasPermissionDirective, PageHeader, FormDialog, EmployeeSelect, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Workflows" subtitle="Who approves what, in which order, and how quickly.">
      <button type="button" class="btn btn--primary" *gggsHasPermission="'workflow.definition.manage'" (click)="openCreate()">
        <i class="pi pi-plus" aria-hidden="true"></i> New workflow
      </button>
    </gggs-page-header>

    @if (loading()) {
      <gggs-skeleton [lines]="5" />
    } @else if (definitions().length === 0) {
      <gggs-empty-state
        icon="pi pi-share-alt"
        title="No workflows yet"
        description="Without a workflow, requests are approved automatically. Add one to require approval."
      />
    } @else {
      <div class="workflows">
        @for (definition of definitions(); track definition.id) {
          <section class="panel stack">
            <div class="toolbar">
              <h2 class="workflows__name">{{ definition.name }}</h2>
              <gggs-status-badge [status]="definition.isActive ? 'Active' : 'Inactive'" />
            </div>
            <p class="muted">{{ definition.subjectName }} · version {{ definition.version }} · <code>{{ definition.code }}</code></p>
            <ol class="workflows__stages">
              @for (stage of definition.stages; track $index) {
                <li>
                  <strong>{{ stage.name }}</strong>:
                  {{ describeApprovers(stage) }}
                  @if (stage.condition; as condition) {
                    <span class="muted">· when {{ condition.field }} {{ operatorLabel(condition.operator) }} {{ condition.value }}</span>
                  }
                  @if (stage.slaHours) {
                    <span class="muted">· {{ stage.slaHours }}h SLA</span>
                  }
                </li>
              }
            </ol>
            <div class="toolbar" *gggsHasPermission="'workflow.definition.manage'">
              <button type="button" class="btn btn--sm" (click)="openEdit(definition)">Edit</button>
              <button type="button" class="btn btn--sm" [class.btn--primary]="!definition.isActive" (click)="toggle(definition)">
                {{ definition.isActive ? 'Deactivate' : 'Activate' }}
              </button>
            </div>
          </section>
        }
      </div>
    }
    @if (listError()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ listError() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing() ? 'Edit workflow' : 'New workflow'" width="min(60rem, 96vw)" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="stack">
        <div class="form-grid">
          <label class="field">
            <span class="field__label">Kind of request *</span>
            <select [disabled]="!!editing()" [ngModel]="subjectType()" (ngModelChange)="subjectType.set($event)">
              <option value="">Choose</option>
              @for (subject of subjects(); track subject.code) {
                <option [value]="subject.code">{{ subject.name }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Code *</span>
            <input type="text" maxlength="32" [disabled]="!!editing()" [ngModel]="code()" (ngModelChange)="code.set($event)" />
          </label>
          <label class="field">
            <span class="field__label">Name *</span>
            <input type="text" maxlength="200" [ngModel]="name()" (ngModelChange)="name.set($event)" />
          </label>
          <label class="field">
            <span class="field__label">Fallback approver</span>
            <gggs-employee-select ariaLabel="Fallback approver" [ngModel]="fallback()" (ngModelChange)="fallback.set($event)" />
            <span class="field__hint">Used when a stage finds nobody - no manager, no department head.</span>
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Description</span>
            <input type="text" maxlength="1000" [ngModel]="description()" (ngModelChange)="description.set($event)" />
          </label>
        </div>

        @for (stage of stages(); track $index; let i = $index) {
          <fieldset class="stage">
            <legend>Stage {{ i + 1 }}</legend>
            <div class="form-grid">
              <label class="field">
                <span class="field__label">Name *</span>
                <input type="text" maxlength="100" [(ngModel)]="stage.name" />
              </label>
              <label class="field">
                <span class="field__label">With several approvers</span>
                <select [(ngModel)]="stage.mode">
                  <option value="All">Everyone must approve</option>
                  <option value="Any">The first answer decides</option>
                </select>
              </label>
              <label class="field">
                <span class="field__label">SLA (hours)</span>
                <input type="number" min="1" max="720" [(ngModel)]="stage.slaHours" />
              </label>
            </div>

            <div class="stage__approvers">
              <span class="field__label">Approvers</span>
              @for (approver of stage.approvers; track $index; let j = $index) {
                <div class="stage__approver">
                  <select [attr.aria-label]="'Approver ' + (j + 1)" [(ngModel)]="approver.kind">
                    @for (kind of kinds; track kind) {
                      <option [value]="kind">{{ kindLabels[kind] }}</option>
                    }
                  </select>
                  @if (approver.kind === 'Employee') {
                    <gggs-employee-select [ariaLabel]="'Approver ' + (j + 1) + ' person'" [(ngModel)]="approver.employeeId" />
                  }
                  @if (stage.approvers.length > 1) {
                    <button type="button" class="btn btn--link btn--sm" (click)="removeApprover(i, j)">Remove</button>
                  }
                </div>
              }
              <button type="button" class="btn btn--link btn--sm" (click)="addApprover(i)">+ Add approver</button>
            </div>

            <label class="stage__toggle"><input type="checkbox" [(ngModel)]="stage.hasCondition" /> Only when…</label>
            @if (stage.hasCondition) {
              <div class="stage__approver">
                <select aria-label="Condition field" [(ngModel)]="stage.conditionField">
                  <option value="">Field</option>
                  @for (field of fields(); track field.key) {
                    <option [value]="field.key">{{ field.label }}</option>
                  }
                </select>
                <select aria-label="Condition operator" [(ngModel)]="stage.conditionOperator">
                  @for (operator of operators; track operator.value) {
                    <option [value]="operator.value">{{ operator.label }}</option>
                  }
                </select>
                <input type="text" aria-label="Condition value" maxlength="200" [(ngModel)]="stage.conditionValue" />
              </div>
            }

            <label class="stage__toggle"><input type="checkbox" [(ngModel)]="stage.escalate" /> When the SLA passes, escalate to someone specific</label>
            @if (stage.escalate) {
              <div class="stage__approver">
                <select aria-label="Escalate to" [(ngModel)]="stage.escalateTo.kind">
                  @for (kind of kinds; track kind) {
                    <option [value]="kind">{{ kindLabels[kind] }}</option>
                  }
                </select>
                @if (stage.escalateTo.kind === 'Employee') {
                  <gggs-employee-select ariaLabel="Escalate to person" [(ngModel)]="stage.escalateTo.employeeId" />
                }
              </div>
            } @else {
              <p class="muted stage__hint">Overdue tasks go to the approver's own manager.</p>
            }

            <div class="toolbar">
              <button type="button" class="btn btn--link btn--sm" [disabled]="i === 0" (click)="move(i, -1)">Move up</button>
              <button type="button" class="btn btn--link btn--sm" [disabled]="i === stages().length - 1" (click)="move(i, 1)">Move down</button>
              @if (stages().length > 1) {
                <button type="button" class="btn btn--link btn--sm" (click)="removeStage(i)">Remove stage</button>
              }
            </div>
          </fieldset>
        }
        <button type="button" class="btn" (click)="addStage()">+ Add stage</button>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .workflows {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(22rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .workflows__name {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .workflows__stages {
      margin: 0;
      padding-inline-start: 1.25rem;
    }

    .stage {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-3);
      margin: 0;
      padding: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }

    .stage__approvers {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
    }

    .stage__approver {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    .stage__approver gggs-employee-select {
      min-width: 16rem;
    }

    .stage__toggle {
      font-size: var(--gggs-font-size-sm);
    }

    .stage__hint {
      margin: 0;
      font-size: var(--gggs-font-size-xs);
    }
  `,
})
export class WorkflowDefinitionsPage {
  private readonly api = inject(WorkflowApi);

  protected readonly kinds = Object.keys(APPROVER_KIND_LABELS) as ApproverKind[];
  protected readonly kindLabels = APPROVER_KIND_LABELS;
  protected readonly operators = OPERATORS;

  protected readonly definitions = signal<readonly WorkflowDefinition[]>([]);
  protected readonly subjects = signal<readonly WorkflowSubject[]>([]);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);

  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<WorkflowDefinition | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly subjectType = signal('');
  protected readonly code = signal('');
  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly fallback = signal<string | null>(null);
  protected readonly stages = signal<StageDraft[]>([]);

  protected readonly fields = computed(() => this.subjects().find((subject) => subject.code === this.subjectType())?.fields ?? []);

  constructor() {
    void this.load();
    void this.api.subjects().then((subjects) => this.subjects.set(subjects));
  }

  protected describeApprovers(stage: WorkflowStage): string {
    const names = stage.approvers.map((approver) => (approver.kind === 'Employee' ? 'a named person' : this.kindLabels[approver.kind].toLowerCase()));
    return names.join(stage.mode === 'All' ? ' and ' : ' or ');
  }

  protected operatorLabel(operator: ConditionOperator): string {
    return OPERATORS.find((candidate) => candidate.value === operator)?.label ?? operator;
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.subjectType.set(this.subjects()[0]?.code ?? '');
    this.code.set('');
    this.name.set('');
    this.description.set('');
    this.fallback.set(null);
    this.stages.set([blankStage()]);
    this.error.set(null);
    this.dialogOpen.set(true);
  }

  protected openEdit(definition: WorkflowDefinition): void {
    this.editing.set(definition);
    this.subjectType.set(definition.subjectType);
    this.code.set(definition.code);
    this.name.set(definition.name);
    this.description.set(definition.description ?? '');
    this.fallback.set(definition.fallbackApproverEmployeeId ?? null);
    this.stages.set(definition.stages.map((stage) => this.toDraft(stage)));
    this.error.set(null);
    this.dialogOpen.set(true);
  }

  protected addStage(): void {
    this.stages.update((stages) => [...stages, { ...blankStage(), name: `Stage ${stages.length + 1}` }]);
  }

  protected removeStage(index: number): void {
    this.stages.update((stages) => stages.filter((_, i) => i !== index));
  }

  protected move(index: number, by: number): void {
    this.stages.update((stages) => {
      const copy = [...stages];
      const [stage] = copy.splice(index, 1);
      copy.splice(index + by, 0, stage);
      return copy;
    });
  }

  protected addApprover(stageIndex: number): void {
    this.stages.update((stages) => {
      stages[stageIndex].approvers.push({ kind: 'Employee', employeeId: null });
      return [...stages];
    });
  }

  protected removeApprover(stageIndex: number, approverIndex: number): void {
    this.stages.update((stages) => {
      stages[stageIndex].approvers.splice(approverIndex, 1);
      return [...stages];
    });
  }

  protected async save(): Promise<void> {
    if (!this.subjectType() || !this.code().trim() || !this.name().trim()) {
      this.error.set('Choose the kind of request and give the workflow a code and a name.');
      return;
    }

    const stages = this.stages().map((stage) => this.toStage(stage));
    const editing = this.editing();
    const body = {
      code: this.code().trim(),
      name: this.name().trim(),
      description: this.description().trim() || null,
      subjectType: this.subjectType(),
      stages,
      fallbackApproverEmployeeId: this.fallback(),
    };

    this.busy.set(true);
    this.error.set(null);
    try {
      if (editing) {
        await this.api.updateDefinition(editing.id, body);
      } else {
        await this.api.createDefinition(body);
      }
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The workflow could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggle(definition: WorkflowDefinition): Promise<void> {
    this.listError.set(null);
    try {
      await this.api.setDefinitionActive(definition.id, !definition.isActive);
      await this.load();
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'The workflow could not be changed.');
    }
  }

  private toDraft(stage: WorkflowStage): StageDraft {
    return {
      name: stage.name,
      mode: stage.mode,
      approvers: stage.approvers.map((approver) => ({ kind: approver.kind, employeeId: approver.employeeId ?? null })),
      hasCondition: !!stage.condition,
      conditionField: stage.condition?.field ?? '',
      conditionOperator: stage.condition?.operator ?? 'GreaterThan',
      conditionValue: stage.condition?.value ?? '',
      slaHours: stage.slaHours ?? null,
      escalate: !!stage.escalateTo,
      escalateTo: { kind: stage.escalateTo?.kind ?? 'SkipLevelManager', employeeId: stage.escalateTo?.employeeId ?? null },
    };
  }

  private toStage(draft: StageDraft): WorkflowStage {
    const rule = (approver: ApproverDraft): ApproverRule => ({ kind: approver.kind, employeeId: approver.kind === 'Employee' ? approver.employeeId : null });
    return {
      name: draft.name.trim(),
      mode: draft.mode,
      approvers: draft.approvers.map(rule),
      condition: draft.hasCondition ? { field: draft.conditionField, operator: draft.conditionOperator, value: draft.conditionValue.trim() } : null,
      slaHours: draft.slaHours ? Number(draft.slaHours) : null,
      escalateTo: draft.escalate ? rule(draft.escalateTo) : null,
    };
  }

  private async load(): Promise<void> {
    try {
      this.definitions.set(await this.api.definitions());
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'Workflows could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
