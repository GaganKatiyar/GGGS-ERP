import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ConfirmEmployeePage } from './confirm/confirm-employee-page';
import { EmployeeDetailPage } from './detail/employee-detail-page';
import { ExitEmployeePage } from './exit/exit-employee-page';
import { HireEmployeePage } from './hire/hire-employee-page';
import { EmployeeListPage } from './list/employee-list-page';
import { TransferEmployeePage } from './transfer/transfer-employee-page';

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
    component: TransferEmployeePage,
    title: 'Transfer employee',
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
