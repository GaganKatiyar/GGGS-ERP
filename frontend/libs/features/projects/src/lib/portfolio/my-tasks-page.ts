import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { PagedResult, ProjectTask, TaskType } from '@gggs/shared/models';
import { TASK_TYPE_ICONS } from '@gggs/shared/models';
import { PageHeader, StatusBadge } from '@gggs/shared/ui';

/** Tasks across every project (spec §44): what is on my plate, or everyone's, soonest due first. */
@Component({
  selector: 'gggs-my-tasks-page',
  imports: [DatePipe, RouterLink, PageHeader, DataTable, DataTableCell, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Tasks" subtitle="Work across projects, soonest due first.">
      <a class="btn" routerLink="/projects">Projects</a>
    </gggs-page-header>

    <section class="panel toolbar my-tasks__filters">
      <label class="field my-tasks__search">
        <span class="field__label">Search</span>
        <input type="search" placeholder="Title or code" [value]="search()" (input)="onSearch($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Whose</span>
        <select [value]="mine() ? 'mine' : 'all'" (change)="mine.set($any($event.target).value === 'mine'); reload()">
          <option value="mine">Assigned to me</option>
          <option value="all">Everyone's I can see</option>
        </select>
      </label>
      <label class="field">
        <span class="field__label">Show</span>
        <select [value]="show()" (change)="show.set($any($event.target).value); reload()">
          <option value="open">Open</option>
          <option value="overdue">Overdue</option>
          <option value="done">Done</option>
        </select>
      </label>
    </section>

    <gggs-data-table [result]="result()" [columns]="columns" [loading]="loading()" exportName="tasks" emptyTitle="Nothing here" emptyDescription="Tasks assigned to you on any project show up here." (queryChange)="onQuery($event)" (rowClick)="open($event)">
      <ng-template gggsCell="code" let-row><i [class]="iconOf(row.type)" aria-hidden="true"></i> {{ row.code }}</ng-template>
      <ng-template gggsCell="projectName" let-row>{{ row.projectCode }} <span class="muted">{{ row.projectName }}</span></ng-template>
      <ng-template gggsCell="columnName" let-row><gggs-status-badge [status]="row.columnName ?? ''" /></ng-template>
      <ng-template gggsCell="dueDate" let-row><span [class.my-tasks__late]="row.isOverdue">{{ row.dueDate ? (row.dueDate | date: 'd MMM y') : '' }}</span></ng-template>
    </gggs-data-table>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .my-tasks__filters {
      margin-block-end: var(--gggs-space-3);
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .my-tasks__search {
      flex: 1;
      min-width: 12rem;
    }

    .my-tasks__late {
      color: var(--gggs-color-danger);
    }
  `,
})
export class MyTasksPage {
  private readonly api = inject(ProjectsApi);
  private readonly router = inject(Router);

  protected readonly icons = TASK_TYPE_ICONS;
  protected readonly columns: readonly DataTableColumn<ProjectTask>[] = [
    { field: 'code', header: 'Code', width: '9rem' },
    { field: 'title', header: 'Title' },
    { field: 'projectName', header: 'Project' },
    { field: 'assigneeName', header: 'Assignee' },
    { field: 'columnName', header: 'Status', width: '9rem' },
    { field: 'priority', header: 'Priority', width: '7rem' },
    { field: 'dueDate', header: 'Due', width: '8rem' },
  ];

  protected readonly result = signal<PagedResult<ProjectTask> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly mine = signal(true);
  protected readonly show = signal<'open' | 'overdue' | 'done'>('open');
  private query: DataTableQuery = { page: 1, pageSize: 25 };
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    void this.reload();
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

  protected open(task: ProjectTask): void {
    void this.router.navigate(['/projects', task.projectId], { queryParams: { tab: 'tasks', task: task.id } });
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.result.set(
        await this.api.portfolioTasks({
          assignedToMe: this.mine(),
          done: this.show() === 'done',
          overdue: this.show() === 'overdue' || undefined,
          search: this.search(),
          page: this.query.page,
          pageSize: this.query.pageSize,
        }),
      );
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Tasks could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
