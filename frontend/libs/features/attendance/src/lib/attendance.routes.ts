import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { AttendanceDaysPage } from './days/attendance-days-page';
import { MyAttendancePage } from './my-attendance/my-attendance-page';
import { ShiftsPage } from './shifts/shifts-page';

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
];

/** Mounted directly at `/me/attendance` - see `NAVIGATION`'s "My Space" entry. */
export const MY_ATTENDANCE_ROUTES: Routes = [
  { path: '', component: MyAttendancePage, title: 'My attendance' },
];
