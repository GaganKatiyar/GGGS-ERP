import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { AttendanceAdminPage } from './admin/attendance-admin-page';
import { HolidaysPage } from './admin/holidays-page';
import { LeaveAdminPage } from './admin/leave-admin-page';
import { LeaveSetupPage } from './admin/leave-setup-page';
import { ShiftsPage } from './admin/shifts-page';
import { MyAttendancePage } from './me/my-attendance-page';
import { MyLeavePage } from './me/my-leave-page';
import { TeamAttendancePage } from './team/team-attendance-page';
import { TeamLeavePage } from './team/team-leave-page';

const regularizations = withPermissions(['attendance.regularization.view']);

/** Lazily loaded by the shell at `/attendance/*`. */
export const ATTENDANCE_ROUTES: Routes = [
  { path: '', component: AttendanceAdminPage, title: 'Attendance' },
  {
    path: 'regularizations',
    component: AttendanceAdminPage,
    title: 'Regularization',
    canActivate: regularizations.canActivate,
    data: { ...regularizations.data, startTab: 'corrections' },
  },
  { path: 'shifts', component: ShiftsPage, title: 'Shifts & roster', ...withPermissions(['attendance.shift.manage', 'attendance.roster.manage']) },
];

/** Lazily loaded by the shell at `/leave/*`. */
export const LEAVE_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'requests' },
  { path: 'requests', component: LeaveAdminPage, title: 'Leave' },
  { path: 'policies', component: LeaveSetupPage, title: 'Leave types & policies', ...withPermissions(['leave.type.manage', 'leave.policy.manage']) },
  { path: 'holidays', component: HolidaysPage, title: 'Holidays', ...withPermissions(['leave.holiday.manage']) },
];

/** Mounted at `/me/attendance`: self-service. */
export const MY_ATTENDANCE_ROUTES: Routes = [{ path: '', component: MyAttendancePage, title: 'My attendance' }];

/** Mounted at `/me/leave`: self-service. */
export const MY_LEAVE_ROUTES: Routes = [{ path: '', component: MyLeavePage, title: 'My leave' }];

/** Mounted at `/team/attendance`: a manager's reports. */
export const TEAM_ATTENDANCE_ROUTES: Routes = [{ path: '', component: TeamAttendancePage, title: 'Team attendance' }];

/** Mounted at `/team/leave`: a manager's reports. */
export const TEAM_LEAVE_ROUTES: Routes = [{ path: '', component: TeamLeavePage, title: 'Team leave' }];
