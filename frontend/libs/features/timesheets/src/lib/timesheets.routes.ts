import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { MyTimesheetPage } from './my-timesheet-page';
import { TeamTimesheetsPage } from './team-timesheets-page';
import { TimesheetApprovalsPage } from './timesheet-approvals-page';
import { TimesheetBankPage } from './timesheet-bank-page';
import { TimesheetSettingsPage } from './timesheet-settings-page';

/** Lazily loaded by the shell at `/me/timesheet`. */
export const MY_TIMESHEET_ROUTES: Routes = [
  { path: '', component: MyTimesheetPage, title: 'My timesheet' },
  { path: 'bank', component: TimesheetBankPage, title: 'Timesheet bank' },
];

/** Lazily loaded by the shell at `/team/timesheets`. */
export const TEAM_TIMESHEETS_ROUTES: Routes = [{ path: '', component: TeamTimesheetsPage, title: 'Team timesheets' }];

/** Lazily loaded by the shell at `/timesheets/*`. */
export const TIMESHEETS_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'approvals' },
  { path: 'approvals', component: TimesheetApprovalsPage, title: 'Timesheet approvals', ...withPermissions(['timesheet.entry.approve', 'workflow.task.act']) },
  { path: 'settings', component: TimesheetSettingsPage, title: 'Timesheet settings', ...withPermissions(['timesheet.settings.manage']) },
];
