import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, type ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { GanttTaskRow, GanttView } from '@gggs/shared/models';
import { EmptyState, Skeleton } from '@gggs/shared/ui';
import { DAY_WIDTH, ROW_HEIGHT, draggedDates, layoutGantt, type DragMode, type GanttBar, type GanttZoom } from './gantt-layout';
import { TaskEditor } from './task-editor';

interface Drag {
  readonly taskId: string;
  readonly mode: DragMode;
  readonly fromX: number;
  readonly delta: number;
}

const OF = (value: string | null): string | undefined => value ?? undefined;

/**
 * The project over time: a bar per task, arrows for what depends on what, milestones, and where the baseline
 * was. A bar can be dragged, its ends pulled, or moved a day at a time from the keyboard. Tasks that depend on
 * a moved one stay where they are; a clash shows red rather than quietly moving someone's work.
 */
@Component({
  selector: 'gggs-gantt-tab',
  imports: [DatePipe, EmptyState, Skeleton, TaskEditor],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <div class="stack">
        <div class="toolbar">
          <fieldset class="gt__zoom">
            <legend class="sr-only">Zoom</legend>
            @for (option of zooms; track option) {
              <label class="gt__choice"><input type="radio" name="gantt-zoom" [checked]="zoom() === option" (change)="zoom.set(option)" /> {{ option }}</label>
            }
          </fieldset>
          <span class="gt__spacer"></span>
          @if (layout().conflicts) {
            <span class="gt__clash" role="status"><i class="pi pi-exclamation-triangle" aria-hidden="true"></i> {{ layout().conflicts }} {{ layout().conflicts === 1 ? 'dependency is' : 'dependencies are' }} broken by these dates</span>
          }
          @if (v.canEdit) {
            <span class="muted gt__small">Drag a bar to move it, or its ends to resize. With a bar focused, the arrow keys move it a day; Shift+arrow changes its due date.</span>
          }
        </div>

        @if (layout().bars.length) {
          <div class="gt__frame">
            <ul class="gt__names">
              @for (bar of layout().bars; track bar.task.id) {
                <li class="gt__name" [title]="bar.task.title">
                  <button type="button" class="gt__open" (click)="open(bar.task)"><strong>{{ bar.task.code }}</strong> {{ bar.task.title }}</button>
                </li>
              }
            </ul>
            <div #scroller class="gt__scroll" tabindex="0" role="region" aria-label="Timeline">
              <div class="gt__head" [style.width.px]="layout().width">
                @for (tick of layout().ticks; track tick.left) {
                  <span class="gt__tick" [style.left.px]="tick.left">{{ tick.label }}</span>
                }
              </div>
              <div class="gt__plot" [style.width.px]="layout().width" [style.height.px]="layout().height">
                @for (tick of layout().ticks; track tick.left) {
                  <span class="gt__grid" [style.left.px]="tick.left"></span>
                }
                @for (mark of layout().milestones; track mark.left + mark.label) {
                  <span class="gt__mile" [class.gt__mile--done]="mark.done" [style.left.px]="mark.left" [title]="mark.label"></span>
                }
                @if (layout().baselineEnd !== null) {
                  <span class="gt__line gt__line--base" [style.left.px]="layout().baselineEnd" title="Baseline end"></span>
                }
                <span class="gt__line gt__line--end" [style.left.px]="layout().projectEnd" title="Project end"></span>
                <span class="gt__line gt__line--today" [style.left.px]="layout().today" title="Today"></span>
                <svg class="gt__arrows" [attr.width]="layout().width" [attr.height]="layout().height" aria-hidden="true">
                  @for (arrow of layout().arrows; track arrow.id) {
                    <path [attr.d]="arrow.path" [class.gt__arrow--bad]="arrow.conflict" class="gt__arrow" />
                  }
                </svg>
                @for (bar of layout().bars; track bar.task.id) {
                  @if (bar.baselineLeft !== null) {
                    <span class="gt__ghost" [style.left.px]="bar.baselineLeft" [style.width.px]="bar.baselineWidth" [style.top.px]="bar.top + 26" title="Baseline"></span>
                  }
                  <div
                    class="gt__bar"
                    role="button"
                    [attr.tabindex]="v.canEdit ? 0 : -1"
                    [attr.aria-label]="label(bar)"
                    [class.gt__bar--done]="bar.task.isDone"
                    [class.gt__bar--late]="bar.task.isOverdue"
                    [class.gt__bar--drag]="drag()?.taskId === bar.task.id"
                    [style.left.px]="bar.left"
                    [style.width.px]="bar.width"
                    [style.top.px]="bar.top + 6"
                    (pointerdown)="begin($event, bar.task, 'move')"
                    (pointermove)="during($event)"
                    (pointerup)="end($event)"
                    (pointercancel)="cancel()"
                    (keydown)="onKey($event, bar.task)"
                  >
                    <span class="gt__fill" [style.width.px]="bar.doneWidth"></span>
                    @if (v.canEdit) {
                      <span class="gt__grip gt__grip--start" (pointerdown)="begin($event, bar.task, 'start')"></span>
                      <span class="gt__grip gt__grip--end" (pointerdown)="begin($event, bar.task, 'end')"></span>
                    }
                  </div>
                }
              </div>
            </div>
          </div>
        } @else {
          <section class="panel">
            <gggs-empty-state icon="pi pi-calendar" title="No tasks with dates yet" description="Give tasks a start or due date and they appear here." />
          </section>
        }

        <p class="muted gt__small gt__key">
          <span class="gt__swatch gt__swatch--today"></span> Today
          <span class="gt__swatch gt__swatch--end"></span> Project end
          @if (v.baselinedOn) {
            <span class="gt__swatch gt__swatch--base"></span> Baseline, set {{ v.baselinedOn | date: 'd MMM y' }}
          } @else {
            · No baseline set yet: set one on the Overview tab to see where tasks started out.
          }
        </p>

        @if (v.truncated) {
          <p class="alert alert--warning" role="status"><i class="pi pi-info-circle" aria-hidden="true"></i><span>Only the first 1,000 tasks are shown.</span></p>
        }

        @if (layout().unscheduled.length) {
          <section class="panel">
            <h3 class="gt__heading">No dates yet ({{ layout().unscheduled.length }})</h3>
            <ul class="gt__loose">
              @for (task of layout().unscheduled; track task.id) {
                <li><button type="button" class="btn btn--link btn--sm" (click)="open(task)">{{ task.code }} {{ task.title }}</button></li>
              }
            </ul>
          </section>
        }
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-task-editor [(visible)]="editorOpen" [projectId]="projectId()" [task]="editing()" [readOnly]="!view()?.canEdit" (saved)="load()" />
  `,
  styles: `
    .gt__zoom {
      display: flex;
      gap: var(--gggs-space-3);
      margin: 0;
      padding: 0;
      border: 0;
    }

    .gt__choice {
      display: flex;
      gap: var(--gggs-space-1);
      align-items: center;
    }

    .gt__spacer {
      flex: 1;
    }

    .gt__small {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .gt__clash {
      color: var(--gggs-color-danger);
      font-size: var(--gggs-font-size-sm);
    }

    .gt__frame {
      display: grid;
      grid-template-columns: minmax(10rem, 20rem) minmax(0, 1fr);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      overflow: hidden;
    }

    .gt__names {
      margin: 0;
      padding: 2rem 0 0;
      list-style: none;
      border-inline-end: 1px solid var(--gggs-color-border);
    }

    .gt__name {
      block-size: 36px;
      overflow: hidden;
      border-block-start: 1px solid var(--gggs-color-border);
    }

    .gt__open {
      inline-size: 100%;
      block-size: 100%;
      padding-inline: var(--gggs-space-2);
      border: 0;
      background: none;
      color: inherit;
      font-size: var(--gggs-font-size-sm);
      text-align: start;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      cursor: pointer;
    }

    .gt__scroll {
      overflow-x: auto;
    }

    .gt__head {
      position: relative;
      block-size: 2rem;
    }

    .gt__tick {
      position: absolute;
      inset-block-start: 0.4rem;
      padding-inline-start: 0.25rem;
      font-size: var(--gggs-font-size-sm);
      color: var(--gggs-color-text-muted);
      white-space: nowrap;
    }

    .gt__plot {
      position: relative;
      background-image: linear-gradient(to bottom, var(--gggs-color-border) 1px, transparent 1px);
      background-size: 100% 36px;
    }

    .gt__grid,
    .gt__line {
      position: absolute;
      inset-block: 0;
      inline-size: 1px;
      background: var(--gggs-color-border);
    }

    .gt__line {
      inline-size: 2px;
    }

    .gt__line--today {
      background: var(--gggs-color-primary);
    }

    .gt__line--end {
      background: var(--gggs-color-text-muted);
    }

    .gt__line--base {
      background: var(--gggs-color-warning);
    }

    .gt__mile {
      position: absolute;
      inset-block-start: 0;
      inline-size: 10px;
      block-size: 10px;
      margin-inline-start: -5px;
      background: var(--gggs-color-warning);
      transform: rotate(45deg);
    }

    .gt__mile--done {
      background: var(--gggs-color-success);
    }

    .gt__arrows {
      position: absolute;
      inset: 0;
      pointer-events: none;
    }

    .gt__arrow {
      fill: none;
      stroke: var(--gggs-color-text-muted);
      stroke-width: 1.5;
    }

    .gt__arrow--bad {
      stroke: var(--gggs-color-danger);
      stroke-width: 2;
    }

    .gt__ghost {
      position: absolute;
      block-size: 4px;
      background: var(--gggs-color-warning);
      opacity: 0.7;
    }

    .gt__bar {
      position: absolute;
      block-size: 18px;
      min-inline-size: 4px;
      border-radius: 4px;
      background: color-mix(in srgb, var(--gggs-color-primary) 30%, var(--gggs-color-surface));
      cursor: grab;
      touch-action: none;
      overflow: hidden;
    }

    .gt__bar:focus-visible {
      outline: 2px solid var(--gggs-color-primary);
      outline-offset: 1px;
    }

    .gt__bar--late {
      background: color-mix(in srgb, var(--gggs-color-danger) 35%, var(--gggs-color-surface));
    }

    .gt__bar--done {
      background: color-mix(in srgb, var(--gggs-color-success) 35%, var(--gggs-color-surface));
    }

    .gt__bar--drag {
      cursor: grabbing;
      opacity: 0.8;
    }

    .gt__fill {
      display: block;
      block-size: 100%;
      background: var(--gggs-color-primary);
      opacity: 0.7;
    }

    .gt__bar--done .gt__fill {
      background: var(--gggs-color-success);
    }

    .gt__grip {
      position: absolute;
      inset-block: 0;
      inline-size: 6px;
      cursor: ew-resize;
    }

    .gt__grip--start {
      inset-inline-start: 0;
    }

    .gt__grip--end {
      inset-inline-end: 0;
    }

    .gt__key {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .gt__swatch {
      display: inline-block;
      inline-size: 12px;
      block-size: 3px;
    }

    .gt__swatch--today {
      background: var(--gggs-color-primary);
    }

    .gt__swatch--end {
      background: var(--gggs-color-text-muted);
    }

    .gt__swatch--base {
      background: var(--gggs-color-warning);
    }

    .gt__heading {
      margin: 0 0 var(--gggs-space-2);
      font-size: var(--gggs-font-size-md);
    }

    .gt__loose {
      display: flex;
      flex-wrap: wrap;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    @media (max-width: 56rem) {
      .gt__frame {
        grid-template-columns: 7rem minmax(0, 1fr);
      }
    }
  `,
})
export class GanttTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();

  protected readonly zooms: readonly GanttZoom[] = ['Week', 'Month', 'Quarter'];
  protected readonly rowHeight = ROW_HEIGHT;
  protected readonly zoom = signal<GanttZoom>('Week');
  protected readonly view = signal<GanttView | null>(null);
  protected readonly drag = signal<Drag | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly editing = signal<never | null>(null);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private shownZoom: GanttZoom | null = null;

  /** What is drawn: the saved dates, with the bar being dragged where the pointer has it. */
  protected readonly layout = computed(() => {
    const view = this.view();
    const drag = this.drag();
    const empty = { tasks: [], dependencies: [], milestones: [], projectStart: '2000-01-01', projectEnd: '2000-01-01', today: '2000-01-01' };
    if (!view) {
      return layoutGantt(empty, this.zoom());
    }

    return layoutGantt(
      !drag || drag.delta === 0
        ? view
        : {
            ...view,
            tasks: view.tasks.map((task) => {
              if (task.id !== drag.taskId) {
                return task;
              }

              const dates = draggedDates(task, drag.mode, drag.delta);
              return { ...task, startDate: OF(dates.startDate), dueDate: OF(dates.dueDate) };
            }),
          },
      this.zoom(),
    );
  });

  constructor() {
    effect(() => {
      this.projectId();
      untracked(() => void this.load());
    });

    // Open on today rather than on the start of the chart, and again when the scale changes.
    effect(() => {
      const element = this.scroller()?.nativeElement;
      const zoom = this.zoom();
      if (element && zoom !== this.shownZoom) {
        this.shownZoom = zoom;
        element.scrollLeft = Math.max(0, untracked(() => this.layout().today) - 14 * DAY_WIDTH[zoom]);
      }
    });
  }

  protected label(bar: GanttBar): string {
    const { code, title, startDate, dueDate } = bar.task;
    return `${code} ${title}: ${startDate ?? 'no start'} to ${dueDate ?? 'no due date'}`;
  }

  protected async open(task: GanttTaskRow): Promise<void> {
    try {
      this.editing.set((await this.api.task(this.projectId(), task.id)) as never);
      this.editorOpen.set(true);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The task could not be opened.');
    }
  }

  protected begin(event: PointerEvent, task: GanttTaskRow, mode: DragMode): void {
    if (!this.view()?.canEdit || event.button !== 0) {
      return;
    }

    // A grip sits on the bar: the grip takes it, and the bar must not start a move as well.
    event.stopPropagation();
    event.preventDefault();
    const bar = (event.target as HTMLElement).closest('.gt__bar') as HTMLElement;
    bar.setPointerCapture(event.pointerId);
    this.drag.set({ taskId: task.id, mode, fromX: event.clientX, delta: 0 });
  }

  protected during(event: PointerEvent): void {
    const drag = this.drag();
    if (drag) {
      const delta = Math.round((event.clientX - drag.fromX) / DAY_WIDTH[this.zoom()]);
      if (delta !== drag.delta) {
        this.drag.set({ ...drag, delta });
      }
    }
  }

  protected async end(event: PointerEvent): Promise<void> {
    const drag = this.drag();
    if (!drag) {
      return;
    }

    (event.target as HTMLElement).closest('.gt__bar')?.releasePointerCapture?.(event.pointerId);
    const task = this.view()?.tasks.find((row) => row.id === drag.taskId);
    if (!task || drag.delta === 0) {
      this.drag.set(null);
      return;
    }

    await this.schedule(task, drag.mode, drag.delta);
    this.drag.set(null);
  }

  protected cancel(): void {
    this.drag.set(null);
  }

  protected async onKey(event: KeyboardEvent, task: GanttTaskRow): Promise<void> {
    if (event.key === 'Enter') {
      event.preventDefault();
      await this.open(task);
      return;
    }

    if (!this.view()?.canEdit || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) {
      return;
    }

    event.preventDefault();
    await this.schedule(task, event.shiftKey ? 'end' : 'move', event.key === 'ArrowLeft' ? -1 : 1);
  }

  protected async load(): Promise<void> {
    try {
      this.view.set(await this.api.gantt(this.projectId()));
      this.error.set(null);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The timeline could not be loaded.');
    }
  }

  private async schedule(task: GanttTaskRow, mode: DragMode, delta: number): Promise<void> {
    const dates = draggedDates(task, mode, delta);
    this.error.set(null);
    try {
      await this.api.scheduleTask(this.projectId(), task.id, dates.startDate, dates.dueDate);
      this.view.update((view) => view && { ...view, tasks: view.tasks.map((row) => (row.id === task.id ? { ...row, startDate: OF(dates.startDate), dueDate: OF(dates.dueDate) } : row)) });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The dates could not be changed.');
    }
  }
}
