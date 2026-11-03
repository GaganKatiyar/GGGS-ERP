import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { PagedResult, ProjectTask, SavedTaskView, Sprint, TaskType, TaskViewFilters, TeamMember, WorkPriority } from '@gggs/shared/models';
import { TASK_TYPES, TASK_TYPE_ICONS, humanize } from '@gggs/shared/models';
import { StatusBadge } from '@gggs/shared/ui';
import { TaskBulkDialog } from './task-bulk-dialog';
import { TaskEditor } from './task-editor';
import { TaskImportDialog } from './task-import-dialog';

const NOBODY = '__nobody';
const BACKLOG = '__backlog';

/** Every task in the project, filterable, with the editor one click away (spec §44). */
@Component({
  selector: 'gggs-tasks-tab',
  imports: [DatePipe, DataTable, DataTableCell, StatusBadge, TaskEditor, TaskBulkDialog, TaskImportDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel toolbar tasks__filters">
      <label class="field tasks__search">
        <span class="field__label">Search</span>
        <input type="search" placeholder="Title or code" [value]="search()" (input)="onSearch($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Type</span>
        <select [value]="type()" (change)="type.set($any($event.target).value); reload()">
          <option value="">Any</option>
          @for (option of types; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Assignee</span>
        <select [value]="assignee()" (change)="assignee.set($any($event.target).value); reload()">
          <option value="">Anyone</option>
          <option [value]="nobody">Nobody</option>
          @for (member of team(); track member.id) {
            <option [value]="member.employeeId">{{ member.employeeName }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Priority</span>
        <select [value]="priority()" (change)="priority.set($any($event.target).value); reload()">
          <option value="">Any</option>
          @for (option of priorities; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Sprint</span>
        <select [value]="sprint()" (change)="sprint.set($any($event.target).value); reload()">
          <option value="">Any</option>
          <option [value]="backlogOnly">Backlog</option>
          @for (option of sprints(); track option.id) {
            <option [value]="option.id">{{ option.name }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span><input type="checkbox" [checked]="overdue()" (change)="overdue.set($any($event.target).checked); reload()" /> Late only</span>
      </label>
      <label class="field">
        <span><input type="checkbox" [checked]="includeDone()" (change)="includeDone.set($any($event.target).checked); reload()" /> Include done</span>
      </label>
      @if (canEdit()) {
        <button type="button" class="btn" (click)="importOpen.set(true)"><i class="pi pi-upload" aria-hidden="true"></i> Import</button>
        <button type="button" class="btn btn--primary" (click)="open(null)">Add task</button>
      }
    </section>

    <section class="panel toolbar tasks__views">
      <label class="field">
        <span class="field__label">Saved views</span>
        <select [value]="viewId()" (change)="applyView($any($event.target).value)">
          <option value="">Choose a view</option>
          @for (view of views(); track view.id) {
            <option [value]="view.id">{{ view.name }}{{ view.isShared ? ' (shared)' : '' }}</option>
          }
        </select>
      </label>
      @if (currentView()?.canDelete) {
        <button type="button" class="btn btn--link btn--sm" (click)="deleteView()">Delete this view</button>
      }
      <label class="field">
        <span class="field__label">Save these filters as</span>
        <input type="text" maxlength="100" placeholder="View name" [value]="viewName()" (input)="viewName.set($any($event.target).value)" />
      </label>
      @if (canEdit()) {
        <label class="field">
          <span><input type="checkbox" [checked]="viewShared()" (change)="viewShared.set($any($event.target).checked)" /> Share with the project</span>
        </label>
      }
      <button type="button" class="btn btn--sm" [disabled]="!viewName().trim()" (click)="saveView()">Save view</button>
    </section>

    @if (canEdit() && picked().size) {
      <section class="panel toolbar tasks__bulk" role="status">
        <strong>{{ picked().size }} selected</strong>
        <button type="button" class="btn btn--sm btn--primary" (click)="bulkOpen.set(true)">Edit selected</button>
        <button type="button" class="btn btn--link btn--sm" (click)="pickPage()">Select this page</button>
        <button type="button" class="btn btn--link btn--sm" (click)="picked.set(emptySet)">Clear</button>
      </section>
    }

    <gggs-data-table [result]="result()" [columns]="tableColumns()" [loading]="loading()" exportName="project-tasks" emptyTitle="No tasks" (queryChange)="onQuery($event)" (rowClick)="open($event)">
      <ng-template gggsCell="id" let-row>
        <input type="checkbox" [attr.aria-label]="'Select ' + row.code" [checked]="picked().has(row.id)" (click)="$event.stopPropagation()" (change)="pick(row.id, $any($event.target).checked)" />
      </ng-template>
      <ng-template gggsCell="code" let-row><i [class]="iconOf(row.type)" aria-hidden="true"></i> {{ row.code }}</ng-template>
      <ng-template gggsCell="title" let-row>
        {{ row.title }}
        @if (row.parentCode) {
          <span class="muted"> · {{ row.parentCode }}</span>
        }
        @if (row.openPredecessors > 0) {
          <i class="pi pi-lock tasks__blocked" title="Waiting on other tasks" aria-label="Blocked"></i>
        }
      </ng-template>
      <ng-template gggsCell="columnName" let-row><gggs-status-badge [status]="row.columnName ?? ''" /></ng-template>
      <ng-template gggsCell="dueDate" let-row>
        <span [class.tasks__late]="row.isOverdue">{{ row.dueDate ? (row.dueDate | date: 'd MMM') : '' }}</span>
      </ng-template>
    </gggs-data-table>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-task-editor [(visible)]="editorOpen" [projectId]="projectId()" [task]="editing()" [readOnly]="!canEdit()" (saved)="reload()" />
    <gggs-task-bulk-dialog [(visible)]="bulkOpen" [projectId]="projectId()" [taskIds]="pickedIds()" (done)="afterBulk()" />
    <gggs-task-import-dialog [(visible)]="importOpen" [projectId]="projectId()" (imported)="reload()" />
  `,
  styles: `
    .tasks__filters {
      margin-block-end: var(--gggs-space-3);
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .tasks__search {
      flex: 1;
      min-width: 12rem;
    }

    .tasks__late,
    .tasks__blocked {
      color: var(--gggs-color-danger);
    }
  `,
})
export class TasksTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canEdit = input(false);

  /** Opens this task when the tab loads - a link from a notification. */
  readonly openTaskId = input<string | undefined>(undefined);

  protected readonly types = TASK_TYPES;
  protected readonly icons = TASK_TYPE_ICONS;
  protected readonly humanize = humanize;
  protected readonly nobody = NOBODY;
  protected readonly backlogOnly = BACKLOG;
  protected readonly priorities: readonly WorkPriority[] = ['Critical', 'High', 'Medium', 'Low'];
  protected readonly emptySet: ReadonlySet<string> = new Set<string>();
  protected readonly columns: readonly DataTableColumn<ProjectTask>[] = [
    { field: 'code', header: 'Code', sortable: true, width: '9rem' },
    { field: 'title', header: 'Title', sortable: true },
    { field: 'assigneeName', header: 'Assignee' },
    { field: 'columnName', header: 'Status', width: '9rem' },
    { field: 'priority', header: 'Priority', sortable: true, width: '7rem' },
    { field: 'storyPoints', header: 'Points', align: 'right', width: '6rem' },
    { field: 'estimatedHours', header: 'Estimate', align: 'right', width: '7rem', hidden: true },
    { field: 'dueDate', header: 'Due', sortable: true, width: '7rem' },
  ];

  protected readonly result = signal<PagedResult<ProjectTask> | null>(null);
  protected readonly team = signal<readonly TeamMember[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly type = signal<TaskType | ''>('');
  protected readonly assignee = signal('');
  protected readonly includeDone = signal(false);
  protected readonly priority = signal<WorkPriority | ''>('');
  protected readonly sprint = signal('');
  protected readonly overdue = signal(false);
  protected readonly sprints = signal<readonly Sprint[]>([]);
  protected readonly views = signal<readonly SavedTaskView[]>([]);
  protected readonly viewId = signal('');
  protected readonly viewName = signal('');
  protected readonly viewShared = signal(false);
  protected readonly picked = signal<ReadonlySet<string>>(this.emptySet);
  protected readonly pickedIds = computed(() => [...this.picked()]);
  protected readonly currentView = computed(() => this.views().find((view) => view.id === this.viewId()));
  protected readonly tableColumns = computed<readonly DataTableColumn<ProjectTask>[]>(() => (this.canEdit() ? [{ field: 'id', header: '', width: '3rem' }, ...this.columns] : this.columns));
  protected readonly bulkOpen = signal(false);
  protected readonly importOpen = signal(false);
  protected readonly editorOpen = signal(false);
  protected readonly editing = signal<ProjectTask | null>(null);
  private query: DataTableQuery = { page: 1, pageSize: 50 };
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      void this.api.team(projectId).then((team) => this.team.set(team.filter((member) => member.status === 'Active')), () => this.team.set([]));
      void this.api.sprints(projectId).then((sprints) => this.sprints.set(sprints.filter((sprint) => sprint.status === 'Planned' || sprint.status === 'Active')), () => undefined);
      void this.api.taskViews(projectId).then((views) => this.views.set(views), () => undefined);
      this.picked.set(this.emptySet);
      void this.reload();
    });

    effect(() => {
      const taskId = this.openTaskId();
      if (taskId) {
        void this.api.task(this.projectId(), taskId).then((task) => this.open(task), () => undefined);
      }
    });
  }

  protected iconOf(type: string): string {
    return TASK_TYPE_ICONS[type as TaskType] ?? '';
  }

  protected onQuery(query: DataTableQuery): void {
    this.query = query;
    void this.reload();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.reload(), 300);
  }

  protected pick(id: string, on: boolean): void {
    this.picked.update((picked) => {
      const next = new Set(picked);
      if (on) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  protected pickPage(): void {
    const page = this.result()?.items ?? [];
    this.picked.update((picked) => new Set([...picked, ...page.map((task) => task.id)]));
  }

  protected afterBulk(): void {
    this.picked.set(this.emptySet);
    void this.reload();
  }

  protected applyView(id: string): void {
    this.viewId.set(id);
    const filters = this.views().find((view) => view.id === id)?.filters;
    if (filters) {
      this.search.set(filters.search ?? '');
      this.type.set(filters.type ?? '');
      this.assignee.set(filters.unassigned ? NOBODY : (filters.assigneeEmployeeId ?? ''));
      this.priority.set(filters.priority ?? '');
      this.sprint.set(filters.backlog ? BACKLOG : (filters.sprintId ?? ''));
      this.overdue.set(filters.overdue ?? false);
      this.includeDone.set(filters.includeDone ?? false);
      void this.reload();
    }
  }

  protected async saveView(): Promise<void> {
    const assignee = this.assignee();
    const sprint = this.sprint();
    const filters: TaskViewFilters = {
      search: this.search() || null,
      type: this.type() || null,
      assigneeEmployeeId: assignee && assignee !== NOBODY ? assignee : null,
      unassigned: assignee === NOBODY || null,
      priority: this.priority() || null,
      sprintId: sprint && sprint !== BACKLOG ? sprint : null,
      backlog: sprint === BACKLOG || null,
      overdue: this.overdue() || null,
      includeDone: this.includeDone() || null,
    };
    try {
      this.views.set(await this.api.saveTaskView(this.projectId(), this.viewName().trim(), filters, this.viewShared()));
      this.viewName.set('');
      this.viewShared.set(false);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The view could not be saved.');
    }
  }

  protected async deleteView(): Promise<void> {
    try {
      this.views.set(await this.api.deleteTaskView(this.projectId(), this.viewId()));
      this.viewId.set('');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The view could not be deleted.');
    }
  }

  protected open(task: ProjectTask | null): void {
    this.editing.set(task);
    this.editorOpen.set(true);
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.result.set(
        await this.api.tasks(this.projectId(), {
          search: this.search(),
          type: this.type(),
          assigneeEmployeeId: this.assignee() && this.assignee() !== NOBODY ? this.assignee() : null,
          unassigned: this.assignee() === NOBODY || undefined,
          priority: this.priority(),
          sprintId: this.sprint() && this.sprint() !== BACKLOG ? this.sprint() : null,
          backlog: this.sprint() === BACKLOG || undefined,
          overdue: this.overdue() || undefined,
          includeDone: this.includeDone(),
          page: this.query.page,
          pageSize: this.query.pageSize,
          sort: this.query.sort,
        }),
      );
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Tasks could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
