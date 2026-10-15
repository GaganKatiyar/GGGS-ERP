import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type {
  Dependency,
  DependencyType,
  Milestone,
  Phase,
  ProjectTask,
  Requirement,
  Sprint,
  TaskInput,
  TaskType,
  TeamMember,
  WorkPriority,
} from '@gggs/shared/models';
import { DEPENDENCY_LABELS, TASK_TYPES, humanize } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';

type Draft = { -readonly [K in keyof TaskInput]: TaskInput[K] };

const blank = (type: TaskType): Draft => ({
  type,
  parentTaskId: null,
  phaseId: null,
  milestoneId: null,
  sprintId: null,
  requirementId: null,
  columnId: null,
  title: '',
  description: null,
  assigneeEmployeeId: null,
  priority: 'Medium',
  startDate: null,
  dueDate: null,
  estimatedHours: null,
  actualHours: null,
  storyPoints: null,
  progress: 0,
  billable: true,
});

/**
 * Creating or editing a task (spec §44): what it is, where it sits in the hierarchy, who does it,
 * when and how big - and, for an existing task, what it waits on (spec §47).
 */
@Component({
  selector: 'gggs-task-editor',
  imports: [FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="task() ? task()?.code + ' · ' + task()?.title : 'New ' + describe(draft().type)" width="min(52rem, 96vw)" [busy]="busy()" [error]="error()" [saveDisabled]="readOnly()" (save)="save()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Type</span>
          <select [value]="draft().type" [disabled]="readOnly()" (change)="patch('type', $any($event.target).value)">
            @for (type of types; track type) {
              <option [value]="type">{{ describe(type) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Parent</span>
          <select [value]="draft().parentTaskId ?? ''" [disabled]="readOnly()" (change)="patch('parentTaskId', $any($event.target).value || null)">
            <option value="">None</option>
            @for (parent of parents(); track parent.id) {
              <option [value]="parent.id">{{ parent.code }} {{ parent.title }}</option>
            }
          </select>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Title *</span>
          <input type="text" maxlength="300" [value]="draft().title" [disabled]="readOnly()" (input)="patch('title', $any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Description</span>
          <textarea rows="3" maxlength="8000" [value]="draft().description ?? ''" [disabled]="readOnly()" (input)="patch('description', $any($event.target).value || null)"></textarea>
        </label>
        <label class="field">
          <span class="field__label">Assignee</span>
          <select [value]="draft().assigneeEmployeeId ?? ''" [disabled]="readOnly()" (change)="patch('assigneeEmployeeId', $any($event.target).value || null)">
            <option value="">Unassigned</option>
            @for (member of people(); track member.employeeId) {
              <option [value]="member.employeeId">{{ member.employeeName }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Priority</span>
          <select [value]="draft().priority" [disabled]="readOnly()" (change)="patch('priority', $any($event.target).value)">
            @for (priority of priorities; track priority) {
              <option [value]="priority">{{ priority }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Sprint</span>
          <select [value]="draft().sprintId ?? ''" [disabled]="readOnly()" (change)="patch('sprintId', $any($event.target).value || null)">
            <option value="">Backlog</option>
            @for (sprint of sprints(); track sprint.id) {
              <option [value]="sprint.id">{{ sprint.name }} ({{ sprint.status.toLowerCase() }})</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Milestone</span>
          <select [value]="draft().milestoneId ?? ''" [disabled]="readOnly()" (change)="patch('milestoneId', $any($event.target).value || null)">
            <option value="">None</option>
            @for (milestone of milestones(); track milestone.id) {
              <option [value]="milestone.id">{{ milestone.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Phase</span>
          <select [value]="draft().phaseId ?? ''" [disabled]="readOnly()" (change)="patch('phaseId', $any($event.target).value || null)">
            <option value="">None</option>
            @for (phase of phases(); track phase.id) {
              <option [value]="phase.id">{{ phase.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Requirement</span>
          <select [value]="draft().requirementId ?? ''" [disabled]="readOnly()" (change)="patch('requirementId', $any($event.target).value || null)">
            <option value="">None</option>
            @for (requirement of requirements(); track requirement.id) {
              <option [value]="requirement.id">{{ requirement.code }} {{ requirement.title }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Starts</span>
          <input type="date" [value]="draft().startDate ?? ''" [disabled]="readOnly()" (input)="patch('startDate', $any($event.target).value || null)" />
        </label>
        <label class="field">
          <span class="field__label">Due</span>
          <input type="date" [value]="draft().dueDate ?? ''" [disabled]="readOnly()" (input)="patch('dueDate', $any($event.target).value || null)" />
        </label>
        <label class="field">
          <span class="field__label">Story points</span>
          <input type="number" min="0" max="1000" [value]="draft().storyPoints ?? ''" [disabled]="readOnly()" (input)="patch('storyPoints', number($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">Estimate (hours)</span>
          <input type="number" min="0" step="0.5" [value]="draft().estimatedHours ?? ''" [disabled]="readOnly()" (input)="patch('estimatedHours', number($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">Actual (hours)</span>
          <input type="number" min="0" step="0.5" [value]="draft().actualHours ?? ''" [disabled]="readOnly()" (input)="patch('actualHours', number($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">Progress %</span>
          <input type="number" min="0" max="100" [value]="draft().progress ?? 0" [disabled]="readOnly()" (input)="patch('progress', number($any($event.target).value) ?? 0)" />
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="draft().billable ?? true" [disabled]="readOnly()" (change)="patch('billable', $any($event.target).checked)" /> Billable</span>
        </label>
      </div>

      @if (task(); as current) {
        <section class="task-editor__deps">
          <h3 class="task-editor__heading">Waits on</h3>
          @for (dependency of waitsOn(); track dependency.id) {
            <div class="task-editor__dep">
              <span [class.task-editor__done]="dependency.predecessorDone">{{ dependency.predecessorCode }} {{ dependency.predecessorTitle }}</span>
              <span class="muted">{{ dependencyLabels[dependency.type] }}{{ dependency.lagDays ? ' +' + dependency.lagDays + 'd' : '' }}</span>
              @if (!readOnly()) {
                <button type="button" class="btn btn--link btn--sm" (click)="removeDependency(dependency)">Remove</button>
              }
            </div>
          } @empty {
            <p class="muted task-editor__none">Nothing; it can start any time.</p>
          }
          @if (!readOnly()) {
            <div class="toolbar task-editor__add">
              <select aria-label="Task it waits on" [value]="newPredecessor()" (change)="newPredecessor.set($any($event.target).value)">
                <option value="">Add a task it waits on…</option>
                @for (candidate of candidates(); track candidate.id) {
                  @if (candidate.id !== current.id) {
                    <option [value]="candidate.id">{{ candidate.code }} {{ candidate.title }}</option>
                  }
                }
              </select>
              <select aria-label="Dependency type" [value]="newType()" (change)="newType.set($any($event.target).value)">
                @for (type of dependencyTypes; track type) {
                  <option [value]="type">{{ dependencyLabels[type] }}</option>
                }
              </select>
              <button type="button" class="btn btn--sm" [disabled]="!newPredecessor()" (click)="addDependency()">Add</button>
            </div>
          }
        </section>
      }
    </gggs-form-dialog>
  `,
  styles: `
    .task-editor__deps {
      margin-block-start: var(--gggs-space-4);
      padding-block-start: var(--gggs-space-3);
      border-block-start: 1px solid var(--gggs-color-border);
    }

    .task-editor__heading {
      margin: 0 0 var(--gggs-space-2);
      font-size: var(--gggs-font-size-md);
    }

    .task-editor__dep {
      display: flex;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-1);
    }

    .task-editor__done {
      text-decoration: line-through;
      color: var(--gggs-color-text-muted);
    }

    .task-editor__none {
      margin: 0;
    }

    .task-editor__add {
      margin-block-start: var(--gggs-space-2);
      flex-wrap: wrap;
    }
  `,
})
export class TaskEditor {
  private readonly api = inject(ProjectsApi);

  readonly visible = model(false);
  readonly projectId = input.required<string>();

  /** The task to edit; null to create one. */
  readonly task = input<ProjectTask | null>(null);

  /** For a new task: its type, sprint and parent to start from. */
  readonly defaults = input<Partial<TaskInput>>({});
  readonly readOnly = input(false);
  readonly saved = output<ProjectTask>();

  protected readonly types = TASK_TYPES;
  protected readonly priorities: readonly WorkPriority[] = ['Low', 'Medium', 'High', 'Critical'];
  protected readonly dependencyLabels = DEPENDENCY_LABELS;
  protected readonly dependencyTypes = Object.keys(DEPENDENCY_LABELS) as DependencyType[];
  protected readonly draft = signal<Draft>(blank('Task'));
  protected readonly team = signal<readonly TeamMember[]>([]);
  protected readonly sprints = signal<readonly Sprint[]>([]);
  protected readonly milestones = signal<readonly Milestone[]>([]);
  protected readonly phases = signal<readonly Phase[]>([]);
  protected readonly requirements = signal<readonly Requirement[]>([]);
  protected readonly candidates = signal<readonly ProjectTask[]>([]);
  protected readonly dependencies = signal<readonly Dependency[]>([]);
  protected readonly newPredecessor = signal('');
  protected readonly newType = signal<DependencyType>('FinishToStart');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly people = computed(() => {
    const seen = new Set<string>();
    return this.team().filter((member) => member.status === 'Active' && !seen.has(member.employeeId) && seen.add(member.employeeId));
  });

  /** Parents allowed by the hierarchy: epics hold stories, stories hold tasks, tasks hold subtasks. */
  protected readonly parents = computed(() => {
    const type = this.draft().type;
    const allowed: readonly TaskType[] =
      type === 'Epic' ? [] : type === 'Story' ? ['Epic'] : type === 'Subtask' ? ['Story', 'Task', 'Bug', 'ChangeRequest'] : ['Epic', 'Story'];
    return this.candidates().filter((candidate) => allowed.includes(candidate.type) && candidate.id !== this.task()?.id);
  });

  protected readonly waitsOn = computed(() => this.dependencies().filter((dependency) => dependency.successorTaskId === this.task()?.id));

  constructor() {
    effect(() => {
      if (!this.visible()) {
        return;
      }

      const task = this.task();
      this.error.set(null);
      this.newPredecessor.set('');
      this.draft.set(
        task
          ? {
              type: task.type,
              parentTaskId: task.parentTaskId ?? null,
              phaseId: task.phaseId ?? null,
              milestoneId: task.milestoneId ?? null,
              sprintId: task.sprintId ?? null,
              requirementId: task.requirementId ?? null,
              columnId: null,
              title: task.title,
              description: task.description ?? null,
              assigneeEmployeeId: task.assigneeEmployeeId ?? null,
              priority: task.priority,
              startDate: task.startDate ?? null,
              dueDate: task.dueDate ?? null,
              estimatedHours: task.estimatedHours ?? null,
              actualHours: task.actualHours ?? null,
              storyPoints: task.storyPoints ?? null,
              progress: task.progress,
              billable: task.billable,
            }
          : { ...blank('Task'), ...this.defaults() },
      );
      void this.loadOptions(this.projectId(), task);
    });
  }

  protected describe(type: TaskType): string {
    return humanize(type).toLowerCase();
  }

  protected number(value: string): number | null {
    return value === '' ? null : Number(value);
  }

  protected patch<K extends keyof Draft>(field: K, value: Draft[K]): void {
    this.draft.update((draft) => ({ ...draft, [field]: value }));
  }

  protected async save(): Promise<void> {
    if (!this.draft().title.trim()) {
      this.error.set('Give the task a title.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      const saved = await this.api.saveTask(this.projectId(), this.task()?.id ?? null, { ...this.draft(), title: this.draft().title.trim() });
      this.visible.set(false);
      this.saved.emit(saved);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The task could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async addDependency(): Promise<void> {
    const task = this.task();
    if (!task || !this.newPredecessor()) {
      return;
    }

    this.error.set(null);
    try {
      await this.api.addDependency(this.projectId(), task.id, this.newPredecessor(), this.newType(), 0);
      this.newPredecessor.set('');
      this.dependencies.set(await this.api.dependencies(this.projectId(), task.id));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The dependency could not be added.');
    }
  }

  protected async removeDependency(dependency: Dependency): Promise<void> {
    try {
      await this.api.removeDependency(this.projectId(), dependency.id);
      this.dependencies.update((list) => list.filter((item) => item.id !== dependency.id));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The dependency could not be removed.');
    }
  }

  private async loadOptions(projectId: string, task: ProjectTask | null): Promise<void> {
    const settle = <T>(promise: Promise<T>, fallback: T) => promise.catch(() => fallback);
    const [team, sprints, milestones, phases, requirements, candidates, dependencies] = await Promise.all([
      settle(this.api.team(projectId), [] as readonly TeamMember[]),
      settle(this.api.sprints(projectId), [] as readonly Sprint[]),
      settle(this.api.milestones(projectId), [] as readonly Milestone[]),
      settle(this.api.phases(projectId), [] as readonly Phase[]),
      settle(this.api.requirements(projectId), [] as readonly Requirement[]),
      settle(this.api.tasks(projectId, { pageSize: 200, includeDone: true }).then((page) => page.items), [] as readonly ProjectTask[]),
      task ? settle(this.api.dependencies(projectId, task.id), [] as readonly Dependency[]) : Promise.resolve([] as readonly Dependency[]),
    ]);
    this.team.set(team);
    this.sprints.set(sprints.filter((sprint) => sprint.status === 'Planned' || sprint.status === 'Active' || sprint.id === task?.sprintId));
    this.milestones.set(milestones);
    this.phases.set(phases);
    this.requirements.set(requirements);
    this.candidates.set(candidates);
    this.dependencies.set(dependencies);
  }
}
