import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi, addDaysIso, isoToday } from '@gggs/shared/delivery';
import type { BurndownPoint, ProjectTask, Sprint, VelocityPoint } from '@gggs/shared/models';
import { Chart, EmptyState, FormDialog, StatusBadge, type ChartSeries } from '@gggs/shared/ui';

/** Sprints (spec §48): planning from the backlog, running, closing with velocity, burndown and retrospective. */
@Component({
  selector: 'gggs-sprints-tab',
  imports: [DatePipe, Chart, EmptyState, FormDialog, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sprints">
      <section class="panel stack sprints__list">
        <div class="toolbar">
          <h2 class="sprints__heading">Sprints</h2>
          @if (canEdit()) {
            <button type="button" class="btn btn--primary btn--sm" (click)="edit(null)">Plan sprint</button>
          }
        </div>
        @for (sprint of sprints(); track sprint.id) {
          <button type="button" class="sprints__item" [class.sprints__item--active]="sprint.id === selectedId()" (click)="select(sprint)">
            <span class="sprints__name"><strong>{{ sprint.name }}</strong> <gggs-status-badge [status]="sprint.status" /></span>
            <span class="muted">{{ sprint.startDate | date: 'd MMM' }} – {{ sprint.endDate | date: 'd MMM' }} · {{ sprint.status === 'Completed' ? sprint.completedPoints + ' pt done' : sprint.plannedPoints + ' pt planned' }}</span>
          </button>
        } @empty {
          <gggs-empty-state icon="pi pi-replay" title="No sprints yet" />
        }
        @if (velocity().length > 0) {
          <gggs-chart kind="bar" title="Velocity" [labels]="velocityLabels()" [series]="velocitySeries()" height="11rem" />
        }
      </section>

      <section class="panel stack sprints__detail">
        @if (selected(); as sprint) {
          <div class="toolbar">
            <h2 class="sprints__heading">{{ sprint.name }}</h2>
            @if (canEdit()) {
              <button type="button" class="btn btn--sm" (click)="edit(sprint)">{{ sprint.status === 'Completed' ? 'Review notes' : 'Edit' }}</button>
              @if (sprint.status === 'Planned') {
                <button type="button" class="btn btn--primary btn--sm" (click)="start(sprint)">Start</button>
              }
              @if (sprint.status === 'Active') {
                <button type="button" class="btn btn--primary btn--sm" (click)="completeOpen.set(true)">Complete</button>
              }
              @if (sprint.status === 'Planned' || sprint.status === 'Active') {
                <button type="button" class="btn btn--link btn--sm" (click)="cancel(sprint)">Cancel sprint</button>
              }
            }
          </div>
          <p class="muted sprints__goal">{{ sprint.goal ?? 'No goal set' }}</p>
          <p>
            {{ sprint.donePoints }} of {{ sprint.plannedPoints }} points done
            @if (sprint.capacityPoints) {
              · capacity {{ sprint.capacityPoints }}
            }
            @if (sprint.committedPoints !== undefined && sprint.committedPoints !== null) {
              · committed {{ sprint.committedPoints }}
            }
          </p>
          @if (burndown().length > 0) {
            <gggs-chart kind="line" title="Burndown" [labels]="burndownLabels()" [series]="burndownSeries()" height="12rem" />
          }

          <h3 class="sprints__sub">In this sprint</h3>
          @for (task of sprintTasks(); track task.id) {
            <div class="sprints__task">
              <span [class.sprints__done]="!!task.completedDate">{{ task.code }} {{ task.title }}</span>
              <span class="muted">{{ task.assigneeName ?? 'Unassigned' }} · {{ task.storyPoints ?? 0 }} pt</span>
              @if (canEdit() && (sprint.status === 'Planned' || sprint.status === 'Active') && !task.completedDate) {
                <button type="button" class="btn btn--link btn--sm" (click)="unplan(task)">Remove</button>
              }
            </div>
          } @empty {
            <p class="muted">No work planned yet.</p>
          }

          @if (canEdit() && (sprint.status === 'Planned' || sprint.status === 'Active')) {
            <h3 class="sprints__sub">Backlog</h3>
            @for (task of backlog(); track task.id) {
              <label class="sprints__task">
                <span><input type="checkbox" [checked]="picked().has(task.id)" (change)="toggle(task.id)" /> {{ task.code }} {{ task.title }}</span>
                <span class="muted">{{ task.storyPoints ?? 0 }} pt</span>
              </label>
            } @empty {
              <p class="muted">The backlog is empty.</p>
            }
            @if (backlog().length > 0) {
              <button type="button" class="btn btn--primary btn--sm sprints__plan" [disabled]="picked().size === 0" (click)="plan(sprint)">Add {{ picked().size }} to sprint ({{ pickedPoints() }} pt)</button>
            }
          }

          @if (sprint.status === 'Completed' && (sprint.reviewNotes || sprint.retrospectiveNotes)) {
            <h3 class="sprints__sub">Review</h3>
            <p class="sprints__notes">{{ sprint.reviewNotes }}</p>
            <h3 class="sprints__sub">Retrospective</h3>
            <p class="sprints__notes">{{ sprint.retrospectiveNotes }}</p>
          }
        } @else {
          <gggs-empty-state icon="pi pi-replay" title="Choose a sprint" />
        }
        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
      </section>
    </div>

    <gggs-form-dialog [(visible)]="formOpen" [title]="editing() ? 'Edit ' + editing()?.name : 'Plan a sprint'" [busy]="busy()" [error]="dialogError()" (save)="save()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="name()" (input)="name.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Starts *</span>
          <input type="date" [value]="startDate()" (input)="startDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Ends *</span>
          <input type="date" [value]="endDate()" (input)="endDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Capacity (points)</span>
          <input type="number" min="0" [value]="capacity() ?? ''" (input)="capacity.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Goal</span>
          <input type="text" maxlength="1000" [value]="goal()" (input)="goal.set($any($event.target).value)" />
        </label>
        @if (editing()) {
          <label class="field form-grid__full">
            <span class="field__label">Review notes</span>
            <textarea rows="3" maxlength="4000" [value]="review()" (input)="review.set($any($event.target).value)"></textarea>
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Retrospective</span>
            <textarea rows="3" maxlength="4000" [value]="retro()" (input)="retro.set($any($event.target).value)"></textarea>
          </label>
        }
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="completeOpen" title="Complete the sprint" saveLabel="Complete" [busy]="busy()" [error]="dialogError()" (save)="complete()">
      <div class="stack">
        <p>Unfinished work ({{ openCount() }} task(s)) goes to:</p>
        <select aria-label="Where unfinished work goes" [value]="moveTo()" (change)="moveTo.set($any($event.target).value)">
          <option value="">The backlog</option>
          @for (sprint of plannedSprints(); track sprint.id) {
            <option [value]="sprint.id">{{ sprint.name }}</option>
          }
        </select>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .sprints {
      display: grid;
      grid-template-columns: minmax(16rem, 22rem) minmax(0, 1fr);
      gap: var(--gggs-space-3);
      align-items: start;
    }

    @media (max-width: 56rem) {
      .sprints {
        grid-template-columns: minmax(0, 1fr);
      }
    }

    .sprints__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .sprints__item {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      padding: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .sprints__item--active {
      border-color: var(--gggs-color-primary);
      background: var(--gggs-color-primary-subtle);
    }

    .sprints__name {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .sprints__goal {
      margin: 0;
    }

    .sprints__sub {
      margin: var(--gggs-space-2) 0 0;
      font-size: var(--gggs-font-size-sm);
    }

    .sprints__task {
      display: flex;
      justify-content: space-between;
      gap: var(--gggs-space-3);
      padding-block: var(--gggs-space-1);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .sprints__done {
      text-decoration: line-through;
      color: var(--gggs-color-text-muted);
    }

    .sprints__plan {
      align-self: flex-start;
    }

    .sprints__notes {
      margin: 0;
      white-space: pre-wrap;
    }
  `,
})
export class SprintsTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canEdit = input(false);

  protected readonly sprints = signal<readonly Sprint[]>([]);
  protected readonly velocity = signal<readonly VelocityPoint[]>([]);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly sprintTasks = signal<readonly ProjectTask[]>([]);
  protected readonly backlog = signal<readonly ProjectTask[]>([]);
  protected readonly burndown = signal<readonly BurndownPoint[]>([]);
  protected readonly picked = signal<ReadonlySet<string>>(new Set());
  protected readonly error = signal<string | null>(null);

  protected readonly formOpen = signal(false);
  protected readonly editing = signal<Sprint | null>(null);
  protected readonly name = signal('');
  protected readonly startDate = signal(isoToday());
  protected readonly endDate = signal(addDaysIso(isoToday(), 13));
  protected readonly capacity = signal<number | null>(null);
  protected readonly goal = signal('');
  protected readonly review = signal('');
  protected readonly retro = signal('');
  protected readonly completeOpen = signal(false);
  protected readonly moveTo = signal('');
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly selected = computed(() => this.sprints().find((sprint) => sprint.id === this.selectedId()) ?? null);
  protected readonly plannedSprints = computed(() => this.sprints().filter((sprint) => sprint.status === 'Planned' && sprint.id !== this.selectedId()));
  protected readonly openCount = computed(() => this.sprintTasks().filter((task) => !task.completedDate).length);
  protected readonly pickedPoints = computed(() => this.backlog().filter((task) => this.picked().has(task.id)).reduce((sum, task) => sum + (task.storyPoints ?? 0), 0));
  protected readonly burndownLabels = computed(() => this.burndown().map((point) => point.date.slice(5)));
  protected readonly burndownSeries = computed<readonly ChartSeries[]>(() => [
    { label: 'Ideal', values: this.burndown().map((point) => point.ideal) },
    { label: 'Remaining', values: this.burndown().map((point) => point.remaining ?? Number.NaN) },
  ]);
  protected readonly velocityLabels = computed(() => this.velocity().map((point) => point.name));
  protected readonly velocitySeries = computed<readonly ChartSeries[]>(() => [
    { label: 'Committed', values: this.velocity().map((point) => point.committed) },
    { label: 'Completed', values: this.velocity().map((point) => point.completed) },
  ]);

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected select(sprint: Sprint): void {
    this.selectedId.set(sprint.id);
    void this.loadSprint(sprint);
  }

  protected toggle(taskId: string): void {
    this.picked.update((set) => {
      const next = new Set(set);
      if (!next.delete(taskId)) {
        next.add(taskId);
      }
      return next;
    });
  }

  protected edit(sprint: Sprint | null): void {
    const count = this.sprints().length + 1;
    this.editing.set(sprint);
    this.name.set(sprint?.name ?? `Sprint ${count}`);
    this.startDate.set(sprint?.startDate ?? isoToday());
    this.endDate.set(sprint?.endDate ?? addDaysIso(isoToday(), 13));
    this.capacity.set(sprint?.capacityPoints ?? null);
    this.goal.set(sprint?.goal ?? '');
    this.review.set(sprint?.reviewNotes ?? '');
    this.retro.set(sprint?.retrospectiveNotes ?? '');
    this.dialogError.set(null);
    this.formOpen.set(true);
  }

  protected async save(): Promise<void> {
    await this.dialog(async () => {
      const saved = await this.api.saveSprint(this.projectId(), this.editing()?.id ?? null, {
        name: this.name().trim(),
        startDate: this.startDate(),
        endDate: this.endDate(),
        capacityPoints: this.capacity(),
        goal: this.goal() || null,
        reviewNotes: this.review() || null,
        retrospectiveNotes: this.retro() || null,
      });
      this.formOpen.set(false);
      this.selectedId.set(saved.id);
    });
  }

  protected async start(sprint: Sprint): Promise<void> {
    await this.act(() => this.api.startSprint(this.projectId(), sprint.id));
  }

  protected async complete(): Promise<void> {
    const sprint = this.selected();
    if (sprint) {
      await this.dialog(async () => {
        await this.api.completeSprint(this.projectId(), sprint.id, this.moveTo() || null);
        this.completeOpen.set(false);
      });
    }
  }

  protected async cancel(sprint: Sprint): Promise<void> {
    await this.act(() => this.api.cancelSprint(this.projectId(), sprint.id));
  }

  protected async plan(sprint: Sprint): Promise<void> {
    const ids = [...this.picked()];
    await this.act(async () => {
      await this.api.planSprint(this.projectId(), sprint.id, ids);
      this.picked.set(new Set());
    });
  }

  protected async unplan(task: ProjectTask): Promise<void> {
    const sprint = this.selected();
    if (sprint) {
      await this.act(() => this.api.unplanTask(this.projectId(), sprint.id, task.id));
    }
  }

  private async act(action: () => Promise<unknown>): Promise<void> {
    this.error.set(null);
    try {
      await action();
      await this.load(this.projectId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    }
  }

  private async dialog(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await action();
      await this.load(this.projectId());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(projectId: string): Promise<void> {
    this.error.set(null);
    try {
      const [sprints, velocity] = await Promise.all([this.api.sprints(projectId), this.api.velocity(projectId)]);
      this.sprints.set(sprints);
      this.velocity.set(velocity);
      const current = sprints.find((sprint) => sprint.id === this.selectedId()) ?? sprints.find((sprint) => sprint.status === 'Active') ?? sprints[0];
      this.selectedId.set(current?.id ?? null);
      if (current) {
        await this.loadSprint(current);
      }
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Sprints could not be loaded.');
    }
  }

  private async loadSprint(sprint: Sprint): Promise<void> {
    const [tasks, backlog, burndown] = await Promise.all([
      this.api.tasks(this.projectId(), { sprintId: sprint.id, includeDone: true, pageSize: 200 }),
      this.api.tasks(this.projectId(), { backlog: true, pageSize: 200 }),
      this.api.burndown(this.projectId(), sprint.id),
    ]);
    this.sprintTasks.set(tasks.items);
    this.backlog.set(backlog.items.filter((task) => task.type !== 'Epic'));
    this.burndown.set(burndown);
  }
}
