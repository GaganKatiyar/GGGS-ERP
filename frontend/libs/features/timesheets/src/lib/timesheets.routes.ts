import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { MyTimesheetPage } from './my-timesheet-page';
import { TeamTimesheetsPage } from './team-timesheets-page';
import { TimesheetSettingsPage } from './timesheet-settings-page';

/** Lazily loaded by the shell at `/me/timesheet`. */
export const MY_TIMESHEET_ROUTES: Routes = [{ path: '', component: MyTimesheetPage, title: 'My timesheet' }];

/** Lazily loaded by the shell at `/team/timesheets`. */
export const TEAM_TIMESHEETS_ROUTES: Routes = [{ path: '', component: TeamTimesheetsPage, title: 'Team timesheets' }];

/** Lazily loaded by the shell at `/timesheets/*`. */
export const TIMESHEETS_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'approvals' },
  {
    path: 'approvals',
    component: TeamTimesheetsPage,
    title: 'Timesheet approvals',
    canActivate: withPermissions(['timesheet.entry.approve']).canActivate,
    data: { ...withPermissions(['timesheet.entry.approve']).data, approvals: true },
  },
  { path: 'settings', component: TimesheetSettingsPage, title: 'Timesheet settings', ...withPermissions(['timesheet.settings.manage']) },
];
