import { CdkDrag, type CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { BoardColumn, ProjectTask, Sprint, TaskInput } from '@gggs/shared/models';
import { TASK_TYPE_ICONS } from '@gggs/shared/models';
import { EmptyState, Skeleton } from '@gggs/shared/ui';
import { TaskEditor } from './task-editor';

/**
 * A project's Kanban board (spec §45): cards move between the project's own columns by drag and
 * drop (or the keyboard menu on each card). Arriving in a done column completes a task.
 */
@Component({
  selector: 'gggs-project-board',
  imports: [DatePipe, CdkDropListGroup, CdkDropList, CdkDrag, EmptyState, Skeleton, TaskEditor],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toolbar board__filters">
      <label class="field">
        <span class="field__label">Sprint</span>
        <select [value]="sprintId()" (change)="setSprint($any($event.target).value)">
          <option value="">All work</option>
          @for (sprint of sprints(); track sprint.id) {
            <option [value]="sprint.id">{{ sprint.name }}{{ sprint.status === 'Active' ? ' (running)' : '' }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span><input type="checkbox" [checked]="hideBacklog()" (change)="hideBacklog.set($any($event.target).checked)" /> Hide backlog</span>
      </label>
      <span class="board__spacer"></span>
      @if (canEdit()) {
        <button type="button" class="btn btn--primary btn--sm" (click)="create()">Add task</button>
      }
    </div>

    @if (loading() && columns().length === 0) {
      <gggs-skeleton [lines]="6" />
    } @else {
      <div class="board" cdkDropListGroup>
        @for (column of visibleColumns(); track column.id) {
          <section class="board__column" [class.board__column--over]="isOverLimit(column)" [attr.aria-label]="column.name">
            <header class="board__head">
              <strong>{{ column.name }}</strong>
              <span class="muted">{{ cards(column.id).length }}{{ column.wipLimit ? ' / ' + column.wipLimit : '' }}</span>
            </header>
            <div
              class="board__list"
              cdkDropList
              [id]="column.id"
              [cdkDropListData]="column.id"
              [cdkDropListDisabled]="!canEdit()"
              (cdkDropListDropped)="drop($event)"
            >
              @for (task of cards(column.id); track task.id) {
                <article class="board__card" cdkDrag [cdkDragData]="task" [class.board__card--overdue]="task.isOverdue">
                  <button type="button" class="board__open" (click)="open(task)">
                    <span class="board__type"><i [class]="icons[task.type]" aria-hidden="true"></i> {{ task.code }}</span>
                    <span class="board__title">{{ task.title }}</span>
                  </button>
                  <footer class="board__meta">
                    <span>{{ task.assigneeName ?? 'Unassigned' }}</span>
                    @if (task.storyPoints) {
                      <span class="board__points">{{ task.storyPoints }} pt</span>
                    }
                    @if (task.dueDate) {
                      <span [class.board__late]="task.isOverdue">{{ task.dueDate | date: 'd MMM' }}</span>
                    }
                    @if (task.openPredecessors > 0) {
                      <span class="board__blocked" title="Waiting on other tasks"><i class="pi pi-lock" aria-hidden="true"></i> {{ task.openPredecessors }}</span>
                    }
                  </footer>
                  @if (canEdit()) {
                    <label class="board__move">
                      <span class="board__hidden">Move {{ task.code }} to</span>
                      <select [value]="task.columnId" (change)="moveTo(task, $any($event.target).value)">
                        @for (target of columns(); track target.id) {
                          <option [value]="target.id">{{ target.name }}</option>
                        }
                      </select>
                    </label>
                  }
                </article>
              } @empty {
                <p class="muted board__empty">Nothing here</p>
              }
            </div>
          </section>
        } @empty {
          <gggs-empty-state title="This board has no columns" />
        }
      </div>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-task-editor [(visible)]="editorOpen" [projectId]="projectId()" [task]="editing()" [defaults]="defaults()" [readOnly]="!canEdit()" (saved)="reload()" />
  `,
  styles: `
    .board__filters {
      margin-block-end: var(--gggs-space-3);
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .board__spacer {
      flex: 1;
    }

    .board {
      display: grid;
      grid-auto-flow: column;
      grid-auto-columns: minmax(15rem, 1fr);
      gap: var(--gggs-space-3);
      overflow-x: auto;
      padding-block-end: var(--gggs-space-2);
    }

    .board__column {
      display: flex;
      flex-direction: column;
      min-height: 20rem;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-sunken, var(--gggs-color-surface-raised));
    }

    .board__column--over {
      border-color: var(--gggs-color-warning);
    }

    .board__head {
      display: flex;
      justify-content: space-between;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .board__list {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
      padding: var(--gggs-space-2);
      min-height: 4rem;
    }

    .board__card {
      padding: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
      cursor: grab;
    }

    .board__card--overdue {
      border-inline-start: 3px solid var(--gggs-color-danger);
    }

    .board__open {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      width: 100%;
      padding: 0;
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .board__type {
      font-size: var(--gggs-font-size-xs);
      color: var(--gggs-color-text-muted);
    }

    .board__meta {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      margin-block-start: var(--gggs-space-2);
      font-size: var(--gggs-font-size-xs);
      color: var(--gggs-color-text-muted);
    }

    .board__points {
      font-weight: var(--gggs-font-weight-semibold);
    }

    .board__late,
    .board__blocked {
      color: var(--gggs-color-danger);
    }

    .board__move {
      display: block;
      margin-block-start: var(--gggs-space-2);
    }

    .board__move select {
      width: 100%;
      font-size: var(--gggs-font-size-xs);
    }

    .board__empty {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
      text-align: center;
    }

    .board__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }

    .cdk-drag-preview {
      box-shadow: 0 6px 16px rgb(0 0 0 / 20%);
    }

    .cdk-drag-placeholder {
      opacity: 0.4;
    }
  `,
})
export class ProjectBoard {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canEdit = input(false);

  protected readonly icons = TASK_TYPE_ICONS;
  protected readonly columns = signal<readonly BoardColumn[]>([]);
  protected readonly tasks = signal<readonly ProjectTask[]>([]);
  protected readonly sprints = signal<readonly Sprint[]>([]);
  protected readonly sprintId = signal('');
  protected readonly hideBacklog = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly editing = signal<ProjectTask | null>(null);
  protected readonly defaults = signal<Partial<TaskInput>>({});

  protected readonly visibleColumns = computed(() => this.columns().filter((column) => !this.hideBacklog() || column.category !== 'Backlog'));

  private readonly byColumn = computed(() => {
    const groups = new Map<string, ProjectTask[]>();
    for (const task of [...this.tasks()].sort((a, b) => a.rank - b.rank)) {
      groups.set(task.columnId, [...(groups.get(task.columnId) ?? []), task]);
    }
    return groups;
  });

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      this.sprintId.set('');
      void this.api.sprints(projectId).then(
        (sprints) => {
          const open = sprints.filter((sprint) => sprint.status === 'Active' || sprint.status === 'Planned');
          this.sprints.set(open);
          const running = open.find((sprint) => sprint.status === 'Active');
          if (running) {
            this.sprintId.set(running.id);
          }
          void this.reload();
        },
        () => void this.reload(),
      );
    });
  }

  protected cards(columnId: string): readonly ProjectTask[] {
    return this.byColumn().get(columnId) ?? [];
  }

  protected isOverLimit(column: BoardColumn): boolean {
    return !!column.wipLimit && this.cards(column.id).length > column.wipLimit;
  }

  protected setSprint(id: string): void {
    this.sprintId.set(id);
    void this.reload();
  }

  protected open(task: ProjectTask): void {
    this.editing.set(task);
    this.editorOpen.set(true);
  }

  protected create(): void {
    this.editing.set(null);
    this.defaults.set({ type: 'Story', sprintId: this.sprintId() || null });
    this.editorOpen.set(true);
  }

  protected async drop(event: CdkDragDrop<string>): Promise<void> {
    const task = event.item.data as ProjectTask;
    const columnId = event.container.data;
    if (event.previousContainer === event.container && event.previousIndex === event.currentIndex) {
      return;
    }

    // Show the move at once; the server's answer replaces it.
    this.tasks.update((tasks) => tasks.map((item) => (item.id === task.id ? { ...item, columnId, rank: this.rankAt(columnId, event.currentIndex, task.id) } : item)));
    await this.persist(task, columnId, event.currentIndex);
  }

  protected async moveTo(task: ProjectTask, columnId: string): Promise<void> {
    if (columnId !== task.columnId) {
      await this.persist(task, columnId, null);
    }
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const board = await this.api.board(this.projectId(), this.sprintId() || null);
      this.columns.set(board.columns);
      this.tasks.set(board.tasks);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The board could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }

  private rankAt(columnId: string, index: number, movingId: string): number {
    const others = this.cards(columnId).filter((task) => task.id !== movingId);
    if (others.length === 0) {
      return 1000;
    }
    if (index <= 0) {
      return others[0].rank - 1000;
    }
    if (index >= others.length) {
      return others[others.length - 1].rank + 1000;
    }
    return (others[index - 1].rank + others[index].rank) / 2;
  }

  private async persist(task: ProjectTask, columnId: string, index: number | null): Promise<void> {
    try {
      const moved = await this.api.moveTask(this.projectId(), task.id, columnId, index);
      this.tasks.update((tasks) => tasks.map((item) => (item.id === moved.id ? moved : item)));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The task could not be moved.');
      await this.reload();
    }
  }
}
