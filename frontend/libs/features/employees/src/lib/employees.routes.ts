import type { Route, Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ChangeAssignmentPage } from './change-assignment/change-assignment-page';
import { ChangeManagerPage } from './change-manager/change-manager-page';
import { ConfirmEmployeePage } from './confirm/confirm-employee-page';
import { EmployeeDetailPage } from './detail/employee-detail-page';
import { ExitEmployeePage } from './exit/exit-employee-page';
import { HireEmployeePage } from './hire/hire-employee-page';
import { EmployeeListPage } from './list/employee-list-page';
import { RehireEmployeePage } from './rehire/rehire-employee-page';

/**
 * `withPermissions` sets its own `data`; a route that also needs `data` (the transfer/promote
 * split) has to merge the two rather than declare `data` twice in one object literal, where the
 * later key would silently win and drop whichever the earlier one carried.
 */
function withPermissionsAndData(
  permissions: readonly string[],
  data: Record<string, unknown>,
): Pick<Route, 'canActivate' | 'data'> {
  const guard = withPermissions(permissions);
  return { ...guard, data: { ...guard.data, ...data } };
}

/**
 * Lazily loaded by the shell at `/employees/*`.
 *
 * More specific paths (`new`, `:id/transfer`, …) are declared before `:id`, so the router does not
 * mistake `new` for an id - the usual ordering trap with a catch-all parameterized route.
 */
export const EMPLOYEES_ROUTES: Routes = [
  { path: '', component: EmployeeListPage, title: 'Employees' },
  {
    path: 'new',
    component: HireEmployeePage,
    title: 'Hire employee',
    ...withPermissions(['employee.profile.create']),
  },
  {
    path: ':id/transfer',
    component: ChangeAssignmentPage,
    title: 'Transfer employee',
    ...withPermissionsAndData(['employee.employment.manage'], { mode: 'transfer' }),
  },
  {
    path: ':id/promote',
    component: ChangeAssignmentPage,
    title: 'Promote employee',
    ...withPermissionsAndData(['employee.employment.manage'], { mode: 'promote' }),
  },
  {
    path: ':id/change-manager',
    component: ChangeManagerPage,
    title: 'Change reporting manager',
    ...withPermissions(['employee.employment.manage']),
  },
  {
    path: ':id/rehire',
    component: RehireEmployeePage,
    title: 'Rehire employee',
    ...withPermissions(['employee.employment.manage']),
  },
  {
    path: ':id/confirm',
    component: ConfirmEmployeePage,
    title: 'Confirm employee',
    ...withPermissions(['employee.employment.manage']),
  },
  {
    path: ':id/exit',
    component: ExitEmployeePage,
    title: 'Record exit',
    ...withPermissions(['employee.exit.manage']),
  },
  { path: ':id', component: EmployeeDetailPage, title: 'Employee' },
];
