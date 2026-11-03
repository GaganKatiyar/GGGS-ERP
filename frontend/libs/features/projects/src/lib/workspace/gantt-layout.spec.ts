import type { GanttTaskRow, GanttView } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { DAY_WIDTH, ROW_HEIGHT, dayNumber, draggedDates, isConflict, isoOf, layoutGantt, spanOf } from './gantt-layout';

const task = (id: string, startDate?: string, dueDate?: string, changes: Partial<GanttTaskRow> = {}): GanttTaskRow => ({
  id,
  code: id,
  title: id,
  type: 'Task',
  startDate,
  dueDate,
  progress: 0,
  isDone: false,
  isOverdue: false,
  ...changes,
});

const view = (changes: Partial<GanttView> = {}): GanttView => ({
  projectStart: '2026-11-02',
  projectEnd: '2026-11-30',
  today: '2026-11-03',
  tasks: [],
  dependencies: [],
  milestones: [],
  truncated: false,
  canEdit: true,
  ...changes,
});

describe('gantt dates', () => {
  it('converts dates without a time zone getting in the way', () => {
    expect(isoOf(dayNumber('2026-11-03'))).toBe('2026-11-03');
    expect(dayNumber('2026-11-04') - dayNumber('2026-10-31')).toBe(4);
    expect(dayNumber('2026-03-01') - dayNumber('2026-02-28')).toBe(1);
  });

  it('treats one date as one day, and none as not on the chart', () => {
    expect(spanOf(task('a', '2026-11-03'))).toEqual({ first: dayNumber('2026-11-03'), last: dayNumber('2026-11-03') });
    expect(spanOf(task('a', undefined, '2026-11-03'))).toEqual({ first: dayNumber('2026-11-03'), last: dayNumber('2026-11-03') });
    expect(spanOf(task('a'))).toBeNull();
  });

  it('moves both ends, or pulls one', () => {
    const bar = task('a', '2026-11-03', '2026-11-06');
    expect(draggedDates(bar, 'move', 2)).toEqual({ startDate: '2026-11-05', dueDate: '2026-11-08' });
    expect(draggedDates(bar, 'move', -3)).toEqual({ startDate: '2026-10-31', dueDate: '2026-11-03' });
    expect(draggedDates(bar, 'end', 1)).toEqual({ startDate: '2026-11-03', dueDate: '2026-11-07' });
    expect(draggedDates(bar, 'start', 1)).toEqual({ startDate: '2026-11-04', dueDate: '2026-11-06' });
  });

  it('stops an end at the other end', () => {
    const bar = task('a', '2026-11-03', '2026-11-06');
    expect(draggedDates(bar, 'end', -10)).toEqual({ startDate: '2026-11-03', dueDate: '2026-11-03' });
    expect(draggedDates(bar, 'start', 10)).toEqual({ startDate: '2026-11-06', dueDate: '2026-11-06' });
  });

  it('leaves a missing date missing', () => {
    expect(draggedDates(task('a', undefined, '2026-11-06'), 'move', 1)).toEqual({ startDate: null, dueDate: '2026-11-07' });
  });

  it('knows when a dependency is broken, for each kind', () => {
    const before = { first: 10, last: 15 };
    expect(isConflict('FinishToStart', 0, before, { first: 15, last: 20 })).toBe(false);
    expect(isConflict('FinishToStart', 0, before, { first: 14, last: 20 })).toBe(true);
    expect(isConflict('FinishToStart', 2, before, { first: 16, last: 20 })).toBe(true);
    expect(isConflict('StartToStart', 0, before, { first: 9, last: 20 })).toBe(true);
    expect(isConflict('FinishToFinish', 0, before, { first: 5, last: 14 })).toBe(true);
    expect(isConflict('StartToFinish', 0, before, { first: 5, last: 9 })).toBe(true);
    expect(isConflict('StartToFinish', 0, before, { first: 5, last: 10 })).toBe(false);
  });
});

describe('gantt layout', () => {
  it('places a bar by its dates, one row each, and leaves undated work to one side', () => {
    const layout = layoutGantt(view({ tasks: [task('a', '2026-11-03', '2026-11-05'), task('b'), task('c', '2026-11-10', '2026-11-10')] }), 'Week');

    expect(layout.bars.map((bar) => bar.task.id)).toEqual(['a', 'c']);
    expect(layout.unscheduled.map((row) => row.id)).toEqual(['b']);
    expect(layout.bars[0].width).toBe(3 * DAY_WIDTH.Week);
    expect(layout.bars[1].top).toBe(ROW_HEIGHT);
    expect(layout.bars[1].left - layout.bars[0].left).toBe(7 * DAY_WIDTH.Week);
  });

  it('fills a bar by progress, and a done task whole', () => {
    const layout = layoutGantt(view({ tasks: [task('a', '2026-11-03', '2026-11-04', { progress: 50 }), task('b', '2026-11-03', '2026-11-04', { progress: 10, isDone: true })] }), 'Week');

    expect(layout.bars[0].doneWidth).toBe(DAY_WIDTH.Week);
    expect(layout.bars[1].doneWidth).toBe(2 * DAY_WIDTH.Week);
  });

  it('shows the baseline where it was, even when the bar has moved', () => {
    const layout = layoutGantt(view({ tasks: [task('a', '2026-11-10', '2026-11-11', { baselineStartDate: '2026-11-03', baselineDueDate: '2026-11-04' })] }), 'Week');
    const bar = layout.bars[0];

    expect(bar.left - (bar.baselineLeft ?? 0)).toBe(7 * DAY_WIDTH.Week);
    expect(bar.baselineWidth).toBe(2 * DAY_WIDTH.Week);
  });

  it('draws dependencies between tasks with dates, and counts the broken ones', () => {
    const tasks = [task('a', '2026-11-03', '2026-11-06'), task('b', '2026-11-05', '2026-11-07'), task('c')];
    const dependency = (id: string, successorTaskId: string) => ({ id, predecessorTaskId: 'a', successorTaskId, type: 'FinishToStart' as const, lagDays: 0 });

    const layout = layoutGantt(view({ tasks, dependencies: [dependency('1', 'b'), dependency('2', 'c')] }), 'Week');

    expect(layout.arrows.map((arrow) => arrow.id)).toEqual(['1']);
    expect(layout.arrows[0].conflict).toBe(true);
    expect(layout.conflicts).toBe(1);
  });

  it('labels weeks, months or quarters by zoom', () => {
    expect(layoutGantt(view(), 'Week').ticks.length).toBeGreaterThan(3);
    expect(layoutGantt(view(), 'Week').ticks[0].label).toMatch(/^\d+ [A-Z][a-z]{2}$/);
    const months = layoutGantt(view({ projectEnd: '2027-03-31' }), 'Month').ticks.map((tick) => tick.label);
    expect(months).toContain('Dec 2026');
    expect(months).toContain('Jan 2027');
    const quarters = layoutGantt(view({ projectEnd: '2027-12-31' }), 'Quarter').ticks.map((tick) => tick.label);
    expect(quarters).toContain('Jan 2027');
    expect(quarters).not.toContain('Feb 2027');
  });

  it('always covers today and the whole project', () => {
    const layout = layoutGantt(view({ today: '2027-02-01' }), 'Month');

    expect(layout.today).toBeLessThan(layout.width);
    expect(layout.projectEnd).toBeLessThan(layout.today);
  });
});
