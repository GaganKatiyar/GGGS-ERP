import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { BoardColumn, Sprint, TeamMember, WorkPriority } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';

const CLEAR = '__clear';

/** Changes the chosen tasks together. Anything left alone stays as it is on each task. */
@Component({
  selector: 'gggs-task-bulk-dialog',
  imports: [FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="'Edit ' + taskIds().length + ' tasks'" saveLabel="Apply" [busy]="busy()" [error]="error()" [saveDisabled]="!changed()" (save)="apply()">
      <div class="stack">
        <p class="muted bulk__note">Anything left alone stays as it is. If any task cannot take a change, none of them change.</p>
        <div class="bulk__grid">
          <label class="field">
            <span class="field__label">Status</span>
            <select [value]="column()" (change)="column.set($any($event.target).value)">
              <option value="">Leave alone</option>
              @for (option of columns(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Assignee</span>
            <select [value]="assignee()" (change)="assignee.set($any($event.target).value)">
              <option value="">Leave alone</option>
              <option [value]="clear">Unassign</option>
              @for (member of team(); track member.id) {
                <option [value]="member.employeeId">{{ member.employeeName }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Priority</span>
            <select [value]="priority()" (change)="priority.set($any($event.target).value)">
              <option value="">Leave alone</option>
              @for (option of priorities; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Sprint</span>
            <select [value]="sprint()" (change)="sprint.set($any($event.target).value)">
              <option value="">Leave alone</option>
              <option [value]="clear">Back to the backlog</option>
              @for (option of sprints(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Due</span>
            <input type="date" [value]="due()" (change)="due.set($any($event.target).value)" />
          </label>
        </div>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .bulk__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .bulk__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: var(--gggs-space-3);
    }
  `,
})
export class TaskBulkDialog {
  private readonly api = inject(ProjectsApi);

  readonly visible = model(false);
  readonly projectId = input.required<string>();
  readonly taskIds = input.required<readonly string[]>();
  readonly done = output();

  protected readonly clear = CLEAR;
  protected readonly priorities: readonly WorkPriority[] = ['Critical', 'High', 'Medium', 'Low'];
  protected readonly columns = signal<readonly BoardColumn[]>([]);
  protected readonly team = signal<readonly TeamMember[]>([]);
  protected readonly sprints = signal<readonly Sprint[]>([]);
  protected readonly column = signal('');
  protected readonly assignee = signal('');
  protected readonly priority = signal('');
  protected readonly sprint = signal('');
  protected readonly due = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly changed = computed(() => Boolean(this.column() || this.assignee() || this.priority() || this.sprint() || this.due()));

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => void this.open());
      }
    });
  }

  protected async apply(): Promise<void> {
    const assignee = this.assignee();
    const sprint = this.sprint();
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.bulkUpdateTasks(this.projectId(), {
        taskIds: this.taskIds(),
        columnId: this.column() || null,
        assigneeEmployeeId: assignee && assignee !== CLEAR ? assignee : null,
        clearAssignee: assignee === CLEAR,
        priority: (this.priority() || null) as WorkPriority | null,
        sprintId: sprint && sprint !== CLEAR ? sprint : null,
        clearSprint: sprint === CLEAR,
        dueDate: this.due() || null,
      });
      this.visible.set(false);
      this.done.emit();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Those tasks could not be changed.');
    } finally {
      this.busy.set(false);
    }
  }

  private async open(): Promise<void> {
    this.column.set('');
    this.assignee.set('');
    this.priority.set('');
    this.sprint.set('');
    this.due.set('');
    this.error.set(null);
    const id = this.projectId();
    const [board, team, sprints] = await Promise.all([
      this.api.board(id).catch(() => null),
      this.api.team(id).catch(() => [] as readonly TeamMember[]),
      this.api.sprints(id).catch(() => [] as readonly Sprint[]),
    ]);
    this.columns.set(board?.columns ?? []);
    this.team.set(team.filter((member) => member.status === 'Active'));
    this.sprints.set(sprints.filter((sprint) => sprint.status === 'Planned' || sprint.status === 'Active'));
  }
}
