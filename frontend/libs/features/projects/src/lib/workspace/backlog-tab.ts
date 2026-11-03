import { CdkDrag, type CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { ProjectTask, Sprint, TaskType, WorkPriority } from '@gggs/shared/models';
import { TASK_TYPE_ICONS } from '@gggs/shared/models';
import { EmptyState, Skeleton } from '@gggs/shared/ui';
import { TaskEditor } from './task-editor';

const RANK: Readonly<Record<WorkPriority, number>> = { Critical: 0, High: 1, Medium: 2, Low: 3 };

/** The backlog beside the open sprints: drag work into a sprint, or pick one from the list on each card. */
@Component({
  selector: 'gggs-backlog-tab',
  imports: [CdkDrag, CdkDropList, CdkDropListGroup, NgTemplateOutlet, EmptyState, Skeleton, TaskEditor],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loaded()) {
      <div class="bl" cdkDropListGroup>
        <section class="panel bl__lane">
          <header class="bl__head">
            <h3 class="bl__title">Backlog</h3>
            <span class="muted bl__small">{{ backlog().length }} tasks · {{ points(backlog()) }} points</span>
          </header>
          <label class="field">
            <span class="sr-only">Search the backlog</span>
            <input type="search" placeholder="Search the backlog" [value]="search()" (input)="search.set($any($event.target).value)" />
          </label>
          <div class="bl__list" cdkDropList [cdkDropListData]="null" [cdkDropListDisabled]="!canPlan()" (cdkDropListDropped)="drop($any($event))">
            @for (task of shownBacklog(); track task.id) {
              <!-- The drag belongs directly in the list: a directive in the shared template could not find its list. -->
              <article class="bl__card" cdkDrag [cdkDragData]="task">
                <ng-container *ngTemplateOutlet="card; context: { $implicit: task, from: null }" />
              </article>
            } @empty {
              <p class="muted bl__small">{{ search() ? 'Nothing matches that.' : 'The backlog is empty.' }}</p>
            }
          </div>
        </section>

        <div class="bl__sprints">
          @for (sprint of sprints(); track sprint.id) {
            <section class="panel bl__lane">
              <header class="bl__head">
                <h3 class="bl__title">{{ sprint.name }}</h3>
                <span class="muted bl__small">{{ sprint.status }} · {{ inSprint(sprint.id).length }} tasks · {{ points(inSprint(sprint.id)) }} points</span>
              </header>
              <div class="bl__list" cdkDropList [cdkDropListData]="sprint.id" [cdkDropListDisabled]="!canPlan()" (cdkDropListDropped)="drop($any($event))">
                @for (task of inSprint(sprint.id); track task.id) {
                  <article class="bl__card" cdkDrag [cdkDragData]="task">
                    <ng-container *ngTemplateOutlet="card; context: { $implicit: task, from: sprint.id }" />
                  </article>
                } @empty {
                  <p class="muted bl__small">Drag tasks here to plan them.</p>
                }
              </div>
            </section>
          } @empty {
            <section class="panel">
              <gggs-empty-state icon="pi pi-replay" title="No open sprints" description="Create one on the Sprints tab, then plan work into it here." />
            </section>
          }
        </div>
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }

    <ng-template #card let-task let-from="from">
        <button type="button" class="bl__open" (click)="open(task)">
          <i [class]="iconOf(task.type)" aria-hidden="true"></i> <strong>{{ task.code }}</strong> {{ task.title }}
        </button>
        <footer class="bl__meta">
          <span>{{ task.priority }}</span>
          @if (task.storyPoints) {
            <span>{{ task.storyPoints }} pt</span>
          }
          <span>{{ task.assigneeName ?? 'Unassigned' }}</span>
          @if (canPlan() && task.type !== 'Epic') {
            <select class="bl__move" [attr.aria-label]="'Move ' + task.code" (change)="moveTo(task, from, $any($event.target).value || null)">
              <option value="" [selected]="!from">Backlog</option>
              @for (sprint of sprints(); track sprint.id) {
                <option [value]="sprint.id" [selected]="sprint.id === from">{{ sprint.name }}</option>
              }
            </select>
          }
        </footer>
    </ng-template>

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-task-editor [(visible)]="editorOpen" [projectId]="projectId()" [task]="editing()" (saved)="load()" />
  `,
  styles: `
    .bl {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .bl__sprints {
      display: grid;
      gap: var(--gggs-space-3);
    }

    .bl__lane {
      display: grid;
      gap: var(--gggs-space-2);
    }

    .bl__head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      align-items: baseline;
      justify-content: space-between;
    }

    .bl__title {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .bl__small {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .bl__list {
      display: grid;
      gap: var(--gggs-space-2);
      min-block-size: 4rem;
    }

    .bl__card {
      padding: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface);
    }

    .bl__open {
      inline-size: 100%;
      padding: 0;
      border: 0;
      background: none;
      color: inherit;
      text-align: start;
      cursor: pointer;
    }

    .bl__meta {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      align-items: center;
      margin-block-start: var(--gggs-space-1);
      font-size: var(--gggs-font-size-sm);
      color: var(--gggs-color-text-muted);
    }

    .bl__move {
      margin-inline-start: auto;
      max-inline-size: 10rem;
    }

    .cdk-drag-preview {
      box-shadow: 0 4px 12px rgb(0 0 0 / 20%);
    }

    .cdk-drag-placeholder {
      opacity: 0.4;
    }

    @media (max-width: 56rem) {
      .bl {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class BacklogTab {
  private readonly api = inject(ProjectsApi);
  private readonly permissions = inject(PermissionStore);

  readonly projectId = input.required<string>();

  protected iconOf(type: TaskType): string {
    return TASK_TYPE_ICONS[type];
  }

  protected readonly canPlan = computed(() => this.permissions.has('project.sprint.manage'));
  protected readonly loaded = signal(false);
  protected readonly backlog = signal<readonly ProjectTask[]>([]);
  protected readonly sprints = signal<readonly Sprint[]>([]);
  protected readonly planned = signal<ReadonlyMap<string, readonly ProjectTask[]>>(new Map());
  protected readonly search = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly editing = signal<ProjectTask | null>(null);

  protected readonly shownBacklog = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.backlog().filter((task) => !term || task.title.toLowerCase().includes(term) || task.code.toLowerCase().includes(term));
  });

  constructor() {
    effect(() => {
      this.projectId();
      untracked(() => void this.load());
    });
  }

  protected inSprint(id: string): readonly ProjectTask[] {
    return this.planned().get(id) ?? [];
  }

  protected points(tasks: readonly ProjectTask[]): number {
    return tasks.reduce((sum, task) => sum + (task.storyPoints ?? 0), 0);
  }

  protected open(task: ProjectTask): void {
    this.editing.set(task);
    this.editorOpen.set(true);
  }

  protected async drop(event: CdkDragDrop<string | null>): Promise<void> {
    if (event.previousContainer !== event.container) {
      await this.moveTo(event.item.data as ProjectTask, event.previousContainer.data, event.container.data);
    }
  }

  protected async moveTo(task: ProjectTask, from: string | null, to: string | null): Promise<void> {
    if (from === to) {
      return;
    }

    this.error.set(null);
    try {
      if (to) {
        await this.api.planSprint(this.projectId(), to, [task.id]);
      } else if (from) {
        await this.api.unplanTask(this.projectId(), from, task.id);
      }
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That task could not be moved.');
    }

    await this.load();
  }

  protected async load(): Promise<void> {
    const id = this.projectId();
    try {
      const [backlog, sprints] = await Promise.all([this.api.tasks(id, { backlog: true, pageSize: 200 }), this.api.sprints(id)]);
      const open = sprints.filter((sprint) => sprint.status === 'Planned' || sprint.status === 'Active');
      const pages = await Promise.all(open.map((sprint) => this.api.tasks(id, { sprintId: sprint.id, includeDone: true, pageSize: 200 })));
      this.backlog.set([...backlog.items].filter((task) => task.type !== 'Epic').sort((first, second) => RANK[first.priority] - RANK[second.priority] || first.number - second.number));
      this.sprints.set(open);
      this.planned.set(new Map(open.map((sprint, index) => [sprint.id, pages[index].items])));
      this.loaded.set(true);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The backlog could not be loaded.');
    }
  }
}
