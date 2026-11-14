import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TimesheetsApi } from '@gggs/shared/delivery';
import type { TimesheetView } from '@gggs/shared/models';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MyTimesheetPage } from './my-timesheet-page';

const days = ['2026-11-02', '2026-11-03', '2026-11-04', '2026-11-05', '2026-11-06', '2026-11-07', '2026-11-08'].map((date, index) => ({
  date,
  isWorkingDay: index < 5,
  hours: 0,
  leaveHours: 0,
  holidayHours: 0,
}));

const view = (weekStart: string): TimesheetView =>
  ({
    timesheet: { employeeId: 'me', weekStart, weekEnd: weekStart, status: 'Draft', totalHours: 0, billableHours: 0, isLate: false, correctionCount: 0, isLocked: false },
    entries: [],
    days,
    targets: [{ projectId: 'general', projectCode: 'D_ENG', projectName: 'Engineering (default)', billable: false, isDefault: true, tasks: [{ taskId: 'standup', code: 'D_ENG-2', title: 'Stand-up', activity: 'Meetings' }] }],
    issues: [],
    byCategory: {},
    overtimeHours: 0,
    rules: { standardHoursPerDay: 8, minHoursPerDay: 0, maxHoursPerDay: 24, minWeeklyHours: 40, enforceWeeklyMinimum: false, allowWeekendEntries: true, allowHolidayEntries: true, submissionDeadlineDays: 2, requireFromEveryone: false },
    approvals: [],
  }) as unknown as TimesheetView;

/** The page's own methods, which the template calls. */
interface Page {
  weekStart(): string;
  rows(): readonly { id: number; cells: readonly { note: string }[] }[];
  goTo(day: string): boolean;
  setHours(rowId: number, day: number, value: string): void;
  openNote(row: unknown, column: unknown): void;
  noteText: { set(value: string): void };
  saveNote(): void;
}

describe('MyTimesheetPage: leaving a week with unsaved hours', () => {
  let ask: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    ask = vi.fn(() => false);
    vi.stubGlobal('confirm', ask);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: TimesheetsApi, useValue: { week: vi.fn((week?: string) => Promise.resolve(view(week ?? '2026-11-02'))), bank: vi.fn(() => Promise.resolve([])) } },
      ],
    });
    TestBed.overrideComponent(MyTimesheetPage, { set: { template: '', imports: [] } });
  });

  afterEach(() => vi.unstubAllGlobals());

  const open = async (): Promise<Page> => {
    const fixture = TestBed.createComponent(MyTimesheetPage);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.componentInstance as unknown as Page;
  };

  it('moves without asking when nothing has changed', async () => {
    const page = await open();
    expect(page.goTo('2026-10-21')).toBe(true);
    expect(page.weekStart()).toBe('2026-10-19');
    expect(ask).not.toHaveBeenCalled();
  });

  it('asks once hours have changed, and stays when the person says no', async () => {
    const page = await open();
    const start = page.weekStart();
    page.setHours(page.rows()[0].id, 0, '2');
    expect(page.goTo('2026-10-21')).toBe(false);
    expect(ask).toHaveBeenCalledOnce();
    expect(page.weekStart()).toBe(start);

    ask.mockReturnValue(true);
    expect(page.goTo('2026-10-21')).toBe(true);
    expect(page.weekStart()).toBe('2026-10-19');
  });

  it('does not count a note saved unchanged as a change', async () => {
    const page = await open();
    const row = page.rows()[0];
    page.openNote(row, { day: days[0], index: 0 });
    page.noteText.set(row.cells[0].note);
    page.saveNote();
    expect(page.goTo('2026-10-21')).toBe(true);
    expect(ask).not.toHaveBeenCalled();
  });
});
