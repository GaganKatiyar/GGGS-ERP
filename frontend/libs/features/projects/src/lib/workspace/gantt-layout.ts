import type { DependencyType, GanttTaskRow, GanttView } from '@gggs/shared/models';

export type GanttZoom = 'Week' | 'Month' | 'Quarter';
export type DragMode = 'move' | 'start' | 'end';

export const DAY_WIDTH: Readonly<Record<GanttZoom, number>> = { Week: 28, Month: 10, Quarter: 4 };
export const ROW_HEIGHT = 36;

const DAY_MS = 86_400_000;
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** A date as a whole number of days, free of time zones. */
export const dayNumber = (iso: string): number => {
  const [year, month, day] = iso.slice(0, 10).split('-').map(Number);
  return Date.UTC(year, month - 1, day) / DAY_MS;
};

export const isoOf = (day: number): string => new Date(day * DAY_MS).toISOString().slice(0, 10);

/** The days a task covers, inclusive. A task with one date covers that day alone; with none it is not on the chart. */
export function spanOf(task: { readonly startDate?: string; readonly dueDate?: string }): { readonly first: number; readonly last: number } | null {
  const first = task.startDate ?? task.dueDate;
  const last = task.dueDate ?? task.startDate;
  return first && last ? { first: dayNumber(first), last: dayNumber(last) } : null;
}

/** The dates a drag gives. Moving keeps the length; pulling an end stops at the other end. */
export function draggedDates(
  task: { readonly startDate?: string; readonly dueDate?: string },
  mode: DragMode,
  delta: number,
): { readonly startDate: string | null; readonly dueDate: string | null } {
  const start = task.startDate ? dayNumber(task.startDate) : null;
  const due = task.dueDate ? dayNumber(task.dueDate) : null;
  const iso = (day: number | null) => (day === null ? null : isoOf(day));
  switch (mode) {
    case 'move':
      return { startDate: iso(start === null ? null : start + delta), dueDate: iso(due === null ? null : due + delta) };
    case 'start':
      return { startDate: iso(start === null ? null : due === null ? start + delta : Math.min(start + delta, due)), dueDate: iso(due) };
    default:
      return { startDate: iso(start), dueDate: iso(due === null ? null : start === null ? due + delta : Math.max(due + delta, start)) };
  }
}

/** Whether a dependency's dates break it. */
export function isConflict(
  type: DependencyType,
  lagDays: number,
  before: { readonly first: number; readonly last: number },
  after: { readonly first: number; readonly last: number },
): boolean {
  switch (type) {
    case 'FinishToStart':
      return after.first < before.last + lagDays;
    case 'StartToStart':
      return after.first < before.first + lagDays;
    case 'FinishToFinish':
      return after.last < before.last + lagDays;
    default:
      return after.last < before.first + lagDays;
  }
}

export interface GanttBar {
  readonly task: GanttTaskRow;
  readonly top: number;
  readonly left: number;
  readonly width: number;
  readonly doneWidth: number;
  readonly baselineLeft: number | null;
  readonly baselineWidth: number;
}

export interface GanttArrow {
  readonly id: string;
  readonly path: string;
  readonly conflict: boolean;
}

export interface GanttTick {
  readonly left: number;
  readonly label: string;
}

export interface GanttMark {
  readonly left: number;
  readonly label: string;
  readonly done: boolean;
}

export interface GanttLayout {
  readonly width: number;
  readonly height: number;
  readonly bars: readonly GanttBar[];
  readonly unscheduled: readonly GanttTaskRow[];
  readonly arrows: readonly GanttArrow[];
  readonly ticks: readonly GanttTick[];
  readonly milestones: readonly GanttMark[];
  readonly today: number;
  readonly projectEnd: number;
  readonly baselineEnd: number | null;
  readonly conflicts: number;
}

function ticksFor(zoom: GanttZoom, first: number, last: number, x: (day: number) => number): GanttTick[] {
  const ticks: GanttTick[] = [];
  if (zoom === 'Week') {
    const offset = (new Date(first * DAY_MS).getUTCDay() + 6) % 7;
    for (let day = offset === 0 ? first : first + 7 - offset; day <= last; day += 7) {
      const date = new Date(day * DAY_MS);
      ticks.push({ left: x(day), label: `${date.getUTCDate()} ${MONTHS[date.getUTCMonth()]}` });
    }

    return ticks;
  }

  const every = zoom === 'Month' ? 1 : 3;
  const start = new Date(first * DAY_MS);
  let year = start.getUTCFullYear();
  let month = Math.ceil((start.getUTCMonth() + 1) / every) * every;
  for (let day = Date.UTC(year, month, 1) / DAY_MS; day <= last; day = Date.UTC(year, month, 1) / DAY_MS) {
    const date = new Date(day * DAY_MS);
    ticks.push({ left: x(day), label: `${MONTHS[date.getUTCMonth()]} ${date.getUTCFullYear()}` });
    month += every;
    if (month >= 12) {
      month -= 12;
      year += 1;
    }
  }

  return ticks;
}

/** Everything the chart draws, in pixels. Pure, so dragging can redraw it with one task's dates changed. */
export function layoutGantt(view: Pick<GanttView, 'tasks' | 'dependencies' | 'milestones' | 'projectStart' | 'projectEnd' | 'baselineEndDate' | 'today'>, zoom: GanttZoom): GanttLayout {
  const scale = DAY_WIDTH[zoom];
  const scheduled = view.tasks.filter((task) => spanOf(task));
  const days = [
    dayNumber(view.projectStart),
    dayNumber(view.projectEnd),
    dayNumber(view.today),
    ...scheduled.flatMap((task) => {
      const span = spanOf(task) as { first: number; last: number };
      const baseline = spanOf({ startDate: task.baselineStartDate, dueDate: task.baselineDueDate });
      return [span.first, span.last, ...(baseline ? [baseline.first, baseline.last] : [])];
    }),
    ...view.milestones.map((milestone) => dayNumber(milestone.actualDate ?? milestone.plannedDate)),
  ];
  const first = Math.min(...days) - 7;
  const last = Math.max(...days) + 14;
  const x = (day: number) => (day - first) * scale;

  const rowOf = new Map(scheduled.map((task, index) => [task.id, index]));
  const bars = scheduled.map((task, index): GanttBar => {
    const span = spanOf(task) as { first: number; last: number };
    const baseline = spanOf({ startDate: task.baselineStartDate, dueDate: task.baselineDueDate });
    const width = (span.last - span.first + 1) * scale;
    return {
      task,
      top: index * ROW_HEIGHT,
      left: x(span.first),
      width,
      doneWidth: (width * (task.isDone ? 100 : task.progress)) / 100,
      baselineLeft: baseline ? x(baseline.first) : null,
      baselineWidth: baseline ? (baseline.last - baseline.first + 1) * scale : 0,
    };
  });

  const byId = new Map(scheduled.map((task) => [task.id, task]));
  const arrows: GanttArrow[] = [];
  let conflicts = 0;
  for (const link of view.dependencies) {
    const before = byId.get(link.predecessorTaskId);
    const after = byId.get(link.successorTaskId);
    const beforeSpan = before ? spanOf(before) : null;
    const afterSpan = after ? spanOf(after) : null;
    if (!before || !after || !beforeSpan || !afterSpan) {
      continue;
    }

    const conflict = isConflict(link.type, link.lagDays, beforeSpan, afterSpan);
    conflicts += conflict ? 1 : 0;
    const fromEnd = link.type === 'FinishToStart' || link.type === 'FinishToFinish';
    const toStart = link.type === 'FinishToStart' || link.type === 'StartToStart';
    const fromX = fromEnd ? x(beforeSpan.last + 1) : x(beforeSpan.first);
    const toX = toStart ? x(afterSpan.first) : x(afterSpan.last + 1);
    const fromY = (rowOf.get(before.id) ?? 0) * ROW_HEIGHT + ROW_HEIGHT / 2;
    const toY = (rowOf.get(after.id) ?? 0) * ROW_HEIGHT + ROW_HEIGHT / 2;
    const out = fromX + (fromEnd ? 8 : -8);
    const into = toX + (toStart ? -8 : 8);
    arrows.push({ id: link.id, conflict, path: `M${fromX} ${fromY}H${out}V${(fromY + toY) / 2}H${into}V${toY}H${toX}` });
  }

  return {
    width: x(last),
    height: Math.max(scheduled.length, 1) * ROW_HEIGHT,
    bars,
    unscheduled: view.tasks.filter((task) => !spanOf(task)),
    arrows,
    ticks: ticksFor(zoom, first, last, x),
    milestones: view.milestones.map((milestone) => ({
      left: x(dayNumber(milestone.actualDate ?? milestone.plannedDate)),
      label: milestone.name,
      done: milestone.status === 'Achieved',
    })),
    today: x(dayNumber(view.today)),
    projectEnd: x(dayNumber(view.projectEnd) + 1),
    baselineEnd: view.baselineEndDate ? x(dayNumber(view.baselineEndDate) + 1) : null,
    conflicts,
  };
}
