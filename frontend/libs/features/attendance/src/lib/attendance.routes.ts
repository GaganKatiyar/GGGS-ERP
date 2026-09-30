import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { AttendanceDaysPage } from './days/attendance-days-page';
import { DevicesPage } from './devices/devices-page';
import { MyAttendancePage } from './my-attendance/my-attendance-page';
import { ShiftsPage } from './shifts/shifts-page';
import { TeamAttendancePage } from './team/team-attendance-page';

/**
 * Lazily loaded by the shell at `/attendance/*`; see `app.routes.ts` and `NAVIGATION`'s "Time"
 * entry.
 */
export const ATTENDANCE_ROUTES: Routes = [
  { path: '', component: AttendanceDaysPage, title: 'Attendance' },
  {
    path: 'shifts',
    ...withPermissions(['attendance.shift.manage']),
    component: ShiftsPage,
    title: 'Shifts',
  },
  {
    path: 'devices',
    ...withPermissions(['attendance.device.manage']),
    component: DevicesPage,
    title: 'Biometric devices',
  },
];

/** Mounted directly at `/me/attendance` - see `NAVIGATION`'s "My Space" entry. */
export const MY_ATTENDANCE_ROUTES: Routes = [
  { path: '', component: MyAttendancePage, title: 'My attendance' },
];

/** Mounted directly at `/team/attendance` - see `NAVIGATION`'s "My Team" entry. */
export const TEAM_ATTENDANCE_ROUTES: Routes = [
  { path: '', component: TeamAttendancePage, title: 'Team attendance' },
];
