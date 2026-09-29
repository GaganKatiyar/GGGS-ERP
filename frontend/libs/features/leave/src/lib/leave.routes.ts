import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { HolidaysPage } from './holidays/holidays-page';
import { LeavePoliciesPage } from './policies/leave-policies-page';
import { LeaveRequestsPage } from './requests/leave-requests-page';
import { LeaveTypesPage } from './types/leave-types-page';
import { MyLeavePage } from './my-leave/my-leave-page';

/**
 * Lazily loaded by the shell at `/leave/*`; see `app.routes.ts` and `NAVIGATION`'s "Time" entry.
 */
export const LEAVE_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'requests' },
  { path: 'requests', component: LeaveRequestsPage, title: 'Leave requests' },
  {
    path: 'policies',
    ...withPermissions(['leave.policy.manage']),
    component: LeavePoliciesPage,
    title: 'Leave policies',
  },
  {
    path: 'types',
    ...withPermissions(['leave.type.manage']),
    component: LeaveTypesPage,
    title: 'Leave types',
  },
];

/** Mounted directly at `/me/leave` - see `NAVIGATION`'s "My Space" entry. */
export const MY_LEAVE_ROUTES: Routes = [
  { path: '', component: MyLeavePage, title: 'My leave' },
];

/** Mounted directly at `/holidays` - see `NAVIGATION`'s "Time" entry. */
export const HOLIDAYS_ROUTES: Routes = [
  { path: '', component: HolidaysPage, title: 'Holidays' },
];
