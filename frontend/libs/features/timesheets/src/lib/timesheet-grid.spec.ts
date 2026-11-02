import type { TimesheetDay, TimesheetEntryItem, TimesheetTarget } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { attendanceLabel, buildRows, copyableRows, copyInto, dayTotal, defaultCategory, groupRows, mondayOf, rowTotal, toEntries, weekTotal, workedLabel } from './timesheet-grid';

const days: readonly TimesheetDay[] = ['2026-11-02', '2026-11-03', '2026-11-04', '2026-11-05', '2026-11-06', '2026-11-07', '2026-11-08'].map((date, index) => ({
  date,
  isWorkingDay: index < 5,
  hours: 0,
  leaveHours: 0,
  holidayHours: 0,
}));

const general: TimesheetTarget = {
  projectId: 'general',
  projectCode: 'D_ENG',
  projectName: 'Engineering (default)',
  billable: false,
  isDefault: true,
  tasks: [
    { taskId: 'standup', code: 'D_ENG-2', title: 'Stand-up meetings', activity: 'Meetings' },
    { taskId: 'training', code: 'D_ENG-8', title: 'Training', activity: 'Learning' },
  ],
};
const client: TimesheetTarget = { projectId: 'apollo', projectCode: 'APOLLO', projectName: 'Apollo', billable: true, tasks: [] };

const entry = (overrides: Partial<TimesheetEntryItem>): TimesheetEntryItem => ({ id: 'e', date: '2026-11-02', category: 'Internal', hours: 1, ...overrides });

const counter = () => {
  let next = 0;
  return () => next++;
};

const build = (entries: readonly TimesheetEntryItem[] = [], targets: readonly TimesheetTarget[] = [client, general]) => buildRows({ targets, entries, days }, counter());

describe('timesheet grid', () => {
  it('offers a line for everything the person can book to, before anything is booked', () => {
    const rows = build();

    expect(rows.map((row) => row.title)).toEqual(['Project work', 'Stand-up meetings', 'Training']);
    expect(weekTotal(rows)).toBe(0);
  });

  it('starts each line as the right kind of hour', () => {
    expect(defaultCategory(client)).toBe('Billable');
    expect(defaultCategory({ ...client, billable: false })).toBe('NonBillable');
    expect(defaultCategory(general, general.tasks[0])).toBe('Internal');
    expect(defaultCategory(general, general.tasks[1])).toBe('Training');
    expect(defaultCategory(undefined)).toBe('Internal');
  });

  it('lays booked entries over the matching line, keeping notes by day', () => {
    const rows = build([entry({ projectId: 'general', taskId: 'standup', hours: 0.5, description: 'Daily sync' }), entry({ date: '2026-11-03', projectId: 'general', taskId: 'standup', hours: 0.5 })]);
    const standup = rows.find((row) => row.taskId === 'standup');

    expect(rows).toHaveLength(3);
    expect(standup?.cells[0]).toEqual({ hours: 0.5, note: 'Daily sync' });
    expect(standup?.cells[1].hours).toBe(0.5);
    expect(standup && rowTotal(standup)).toBe(1);
  });

  it('gives a second kind of hour on the same task its own line', () => {
    const rows = build([
      entry({ projectId: 'apollo', category: 'Billable', hours: 4 }),
      entry({ projectId: 'apollo', category: 'NonBillable', hours: 2 }),
    ]);

    expect(rows.filter((row) => row.projectId === 'apollo').map((row) => [row.category, rowTotal(row)])).toEqual([['Billable', 4], ['NonBillable', 2]]);
  });

  it('keeps entries on work no longer offered, and time with no project', () => {
    const rows = build([entry({ projectId: 'old', projectCode: 'OLD', taskId: 'gone', taskCode: 'OLD-7', hours: 3 }), entry({ category: 'Bench', hours: 2 })], [general]);

    expect(rows.find((row) => row.projectId === 'old')).toMatchObject({ projectLabel: 'OLD', title: 'OLD-7' });
    expect(rows.find((row) => row.projectId === null)).toMatchObject({ projectLabel: 'Other time', category: 'Bench' });
  });

  it('groups lines by project, in order', () => {
    const groups = groupRows(build());

    expect(groups.map((group) => [group.label, group.isDefault, group.rows.length])).toEqual([['APOLLO · Apollo', false, 1], ['D_ENG · Engineering (default)', true, 2]]);
  });

  it('adds up by day and week', () => {
    const rows = build([entry({ projectId: 'apollo', category: 'Billable', hours: 6 }), entry({ projectId: 'general', taskId: 'standup', hours: 2 }), entry({ date: '2026-11-03', projectId: 'apollo', category: 'Billable', hours: 8 })]);

    expect(dayTotal(rows, 0)).toBe(8);
    expect(dayTotal(rows, 1)).toBe(8);
    expect(dayTotal(rows, 2)).toBe(0);
    expect(weekTotal(rows)).toBe(16);
  });

  it('sends only the days with hours, with their notes', () => {
    const entries = toEntries(build([entry({ projectId: 'general', taskId: 'standup', hours: 0.5, description: 'Daily sync' })]), days);

    expect(entries).toEqual([{ date: '2026-11-02', projectId: 'general', taskId: 'standup', category: 'Internal', hours: 0.5, activity: null, description: 'Daily sync' }]);
  });

  describe('copying a week', () => {
    const source = () =>
      buildRows(
        {
          targets: [general],
          days,
          entries: [
            entry({ projectId: 'general', taskId: 'standup', hours: 0.5, description: 'Daily sync' }),
            entry({ projectId: 'finished', projectCode: 'DONE', category: 'Billable', hours: 8 }),
          ],
        },
        counter(),
      );

    it('offers only lines with hours, on work this week still offers', () => {
      expect(copyableRows(source(), [general]).map((row) => row.taskId)).toEqual(['standup']);
    });

    it('copies hours only, or hours and notes', () => {
      const copied = copyableRows(source(), [general]);
      const standup = (withNotes: boolean) => copyInto(build([], [general]), copied, withNotes, counter()).find((row) => row.taskId === 'standup');

      expect(standup(false)?.cells[0]).toEqual({ hours: 0.5, note: '' });
      expect(standup(true)?.cells[0]).toEqual({ hours: 0.5, note: 'Daily sync' });
    });

    it('leaves days the source left empty as they are', () => {
      const rows = build([entry({ date: '2026-11-03', projectId: 'general', taskId: 'standup', hours: 1 })], [general]);
      const result = copyInto(rows, copyableRows(source(), [general]), false, counter()).find((row) => row.taskId === 'standup');

      expect(result?.cells[0].hours).toBe(0.5);
      expect(result?.cells[1].hours).toBe(1);
    });
  });

  it('finds the Monday of any day', () => {
    expect(mondayOf('2026-11-02')).toBe('2026-11-02');
    expect(mondayOf('2026-11-08')).toBe('2026-11-02');
  });

  it('labels the office row and worked time', () => {
    expect(attendanceLabel({ ...days[0], attendanceStatus: 'MissingPunch' })).toBe('Missing punch');
    expect(attendanceLabel(days[0])).toBe('—');
    expect(attendanceLabel(days[6])).toBe('Weekly off');
    expect(attendanceLabel({ ...days[0], holiday: 'Diwali' })).toBe('Holiday');
    expect(workedLabel(485)).toBe('08:05');
    expect(workedLabel(undefined)).toBe('');
  });
});
