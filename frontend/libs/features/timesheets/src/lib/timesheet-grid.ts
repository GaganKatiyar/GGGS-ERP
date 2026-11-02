import { addDaysIso } from '@gggs/shared/delivery';
import type { HourCategory, TimesheetDay, TimesheetEntryInput, TimesheetTarget, TimesheetTask, TimesheetView } from '@gggs/shared/models';

/** One day of one line: its hours, and the note saved with them. */
export interface GridCell {
  readonly hours: number | null;
  readonly note: string;
}

/** One line of the week: a task (or a project, or neither) and a kind of hour, with a cell for each day. */
export interface GridRow {
  readonly id: number;
  readonly projectId: string | null;
  readonly projectLabel: string;
  readonly isDefault: boolean;
  readonly taskId: string | null;
  readonly title: string;
  readonly activityName: string | null;
  readonly category: HourCategory;
  readonly activity: string;
  readonly cells: readonly GridCell[];
}

/** A project's lines together; lines with no project form a group of their own. */
export interface GridGroup {
  readonly key: string;
  readonly label: string;
  readonly isDefault: boolean;
  readonly rows: readonly GridRow[];
}

const NO_TASK = 'Project work';
const NO_PROJECT = 'Other time';

export const blankCells = (): readonly GridCell[] => Array.from({ length: 7 }, () => ({ hours: null, note: '' }));

export const mondayOf = (iso: string): string => {
  const [year, month, day] = iso.split('-').map(Number);
  return addDaysIso(iso, -((new Date(year, month - 1, day).getDay() + 6) % 7));
};

const targetLabel = (target: TimesheetTarget): string => `${target.projectCode} · ${target.projectName}`;

/**
 * The kind of hour a line starts as. Client work follows the person's place on the project. On a default
 * project, Learning and Bench lines keep utilization honest; the person can still change any line.
 */
export function defaultCategory(target: TimesheetTarget | undefined, task?: TimesheetTask): HourCategory {
  if (!target) {
    return 'Internal';
  }

  if (target.isDefault) {
    return task?.activity === 'Learning' ? 'Training' : task?.activity === 'Bench' ? 'Bench' : 'Internal';
  }

  return target.billable ? 'Billable' : 'NonBillable';
}

/**
 * The week as lines: one for everything the person can book to, so there is nothing to add first, and
 * the entries already booked laid over them. Entries that no longer match a target keep their own line.
 */
export function buildRows(view: Pick<TimesheetView, 'targets' | 'entries' | 'days'>, nextId: () => number): readonly GridRow[] {
  const rows: GridRow[] = view.targets.flatMap((target): GridRow[] => {
    const base = { projectId: target.projectId, projectLabel: targetLabel(target), isDefault: !!target.isDefault, activity: '', cells: blankCells() };
    return target.tasks.length
      ? target.tasks.map((task) => ({ ...base, id: nextId(), taskId: task.taskId, title: task.title, activityName: task.activity ?? null, category: defaultCategory(target, task) }))
      : [{ ...base, id: nextId(), taskId: null, title: NO_TASK, activityName: null, category: defaultCategory(target) }];
  });

  for (const entry of view.entries) {
    const day = view.days.findIndex((candidate) => candidate.date === entry.date);
    if (day < 0) {
      continue;
    }

    const projectId = entry.projectId ?? null;
    const taskId = entry.taskId ?? null;
    const activity = entry.activity ?? '';
    const sameWork = (row: GridRow) => row.projectId === projectId && row.taskId === taskId;
    let at = rows.findIndex((row) => sameWork(row) && row.category === entry.category && row.activity === activity);
    if (at < 0) {
      at = rows.findIndex((row) => sameWork(row) && rowTotal(row) === 0);
    }

    if (at < 0) {
      const target = view.targets.find((candidate) => candidate.projectId === projectId);
      rows.push({
        id: nextId(),
        projectId,
        projectLabel: target ? targetLabel(target) : (entry.projectCode ?? NO_PROJECT),
        isDefault: !!target?.isDefault,
        taskId,
        title: target?.tasks.find((task) => task.taskId === taskId)?.title ?? entry.taskCode ?? (projectId ? NO_TASK : NO_PROJECT),
        activityName: target?.tasks.find((task) => task.taskId === taskId)?.activity ?? null,
        category: entry.category,
        activity,
        cells: blankCells(),
      });
      at = rows.length - 1;
    }

    const row = rows[at];
    rows[at] = {
      ...row,
      category: entry.category,
      activity,
      cells: row.cells.map((cell, index) =>
        index === day ? { hours: (cell.hours ?? 0) + entry.hours, note: [cell.note, entry.description ?? ''].filter(Boolean).join('; ') } : cell,
      ),
    };
  }

  return rows;
}

/** Lines by project, in the order the projects first appear. */
export function groupRows(rows: readonly GridRow[]): readonly GridGroup[] {
  const groups = new Map<string, GridRow[]>();
  for (const row of rows) {
    const key = row.projectId ?? '';
    groups.set(key, [...(groups.get(key) ?? []), row]);
  }

  return [...groups].map(([key, members]) => ({ key, label: members[0].projectLabel, isDefault: members[0].isDefault, rows: members }));
}

export function rowTotal(row: Pick<GridRow, 'cells'>): number {
  return row.cells.reduce((sum, cell) => sum + (cell.hours ?? 0), 0);
}

export function dayTotal(rows: readonly GridRow[], day: number): number {
  return rows.reduce((sum, row) => sum + (row.cells[day]?.hours ?? 0), 0);
}

export function weekTotal(rows: readonly GridRow[]): number {
  return rows.reduce((sum, row) => sum + rowTotal(row), 0);
}

/** What goes back to the server: one entry per day with hours. Empty cells are simply not sent. */
export function toEntries(rows: readonly GridRow[], days: readonly TimesheetDay[]): readonly TimesheetEntryInput[] {
  return rows.flatMap((row) =>
    row.cells.flatMap((cell, day) =>
      (cell.hours ?? 0) > 0
        ? [{ date: days[day].date, projectId: row.projectId, taskId: row.taskId, category: row.category, hours: cell.hours ?? 0, activity: row.activity || null, description: cell.note || null }]
        : [],
    ),
  );
}

/** Lines of another week that can be copied here: those with hours, on work this week still offers. */
export function copyableRows(source: readonly GridRow[], targets: readonly TimesheetTarget[]): readonly GridRow[] {
  return source.filter((row) => {
    if (rowTotal(row) === 0) {
      return false;
    }

    if (row.projectId === null) {
      return true;
    }

    const target = targets.find((candidate) => candidate.projectId === row.projectId);
    return !!target && (row.taskId === null || target.tasks.some((task) => task.taskId === row.taskId));
  });
}

/** Lays copied lines over the week, day for day. Days the source left empty keep what they have. */
export function copyInto(rows: readonly GridRow[], source: readonly GridRow[], withNotes: boolean, nextId: () => number): readonly GridRow[] {
  const result = [...rows];
  for (const copied of source) {
    const sameWork = (row: GridRow) => row.projectId === copied.projectId && row.taskId === copied.taskId;
    let at = result.findIndex((row) => sameWork(row) && row.category === copied.category);
    if (at < 0) {
      at = result.findIndex((row) => sameWork(row) && rowTotal(row) === 0);
    }

    if (at < 0) {
      result.push({ ...copied, id: nextId(), cells: blankCells() });
      at = result.length - 1;
    }

    result[at] = {
      ...result[at],
      category: copied.category,
      activity: copied.activity,
      cells: result[at].cells.map((cell, day) => {
        const from = copied.cells[day];
        return (from.hours ?? 0) > 0 ? { hours: from.hours, note: withNotes ? from.note : cell.note } : cell;
      }),
    };
  }

  return result;
}

const ATTENDANCE_LABELS: Readonly<Record<string, string>> = {
  Present: 'Present',
  HalfDay: 'Half day',
  Absent: 'Absent',
  WeeklyOff: 'Weekly off',
  Holiday: 'Holiday',
  OnLeave: 'On leave',
  MissingPunch: 'Missing punch',
};

/** A day in the office row: what attendance judged it, else what the calendar says. */
export function attendanceLabel(day: TimesheetDay): string {
  if (day.attendanceStatus) {
    return ATTENDANCE_LABELS[day.attendanceStatus] ?? day.attendanceStatus;
  }

  return day.holiday ? 'Holiday' : day.leave ? 'On leave' : day.isWorkingDay ? '—' : 'Weekly off';
}

/** Worked minutes as hh:mm. */
export function workedLabel(minutes: number | undefined): string {
  if (minutes === undefined) {
    return '';
  }

  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}
