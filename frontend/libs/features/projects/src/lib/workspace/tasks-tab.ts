import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { PagedResult, ProjectTask, TaskType, TeamMember } from '@gggs/shared/models';
import { TASK_TYPES, TASK_TYPE_ICONS, humanize } from '@gggs/shared/models';
import { StatusBadge } from '@gggs/shared/ui';
import { TaskEditor } from './task-editor';

/** Every task in the project, filterable, with the editor one click away (spec §44). */
@Component({
  selector: 'gggs-tasks-tab',
  imports: [DatePipe, DataTable, DataTableCell, StatusBadge, TaskEditor],
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
          @for (member of team(); track member.id) {
            <option [value]="member.employeeId">{{ member.employeeName }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span><input type="checkbox" [checked]="includeDone()" (change)="includeDone.set($any($event.target).checked); reload()" /> Include done</span>
      </label>
      @if (canEdit()) {
        <button type="button" class="btn btn--primary" (click)="open(null)">Add task</button>
      }
    </section>

    <gggs-data-table [result]="result()" [columns]="columns" [loading]="loading()" exportName="project-tasks" emptyTitle="No tasks" (queryChange)="onQuery($event)" (rowClick)="open($event)">
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
  protected readonly editorOpen = signal(false);
  protected readonly editing = signal<ProjectTask | null>(null);
  private query: DataTableQuery = { page: 1, pageSize: 50 };
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      void this.api.team(projectId).then((team) => this.team.set(team.filter((member) => member.status === 'Active')), () => this.team.set([]));
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
          assigneeEmployeeId: this.assignee() || null,
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
