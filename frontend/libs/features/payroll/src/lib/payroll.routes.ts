import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { EmployeeSalaryPage } from './employee-salary/employee-salary-page';
import { MyPayslipsPage } from './my-payslips/my-payslips-page';
import { PayGroupListPage } from './pay-groups/pay-group-list-page';
import { PayrollRunDetailPage } from './runs/payroll-run-detail-page';
import { PayrollRunListPage } from './runs/payroll-run-list-page';
import { SalaryComponentListPage } from './components/salary-component-list-page';
import { SalaryStructureDetailPage } from './structures/salary-structure-detail-page';
import { SalaryStructureListPage } from './structures/salary-structure-list-page';

/** Lazily loaded by the shell at `/payroll/*`. */
export const PAYROLL_ROUTES: Routes = [
  { path: '', component: SalaryComponentListPage, title: 'Salary components' },
  {
    path: 'structures',
    component: SalaryStructureListPage,
    title: 'Salary structures',
    ...withPermissions(['payroll.structure.manage']),
  },
  {
    path: 'structures/:id',
    component: SalaryStructureDetailPage,
    title: 'Salary structure',
    ...withPermissions(['payroll.structure.manage']),
  },
  {
    path: 'employee-salary',
    component: EmployeeSalaryPage,
    title: 'Employee salary',
    ...withPermissions(['payroll.employee-salary.view']),
  },
  {
    path: 'pay-groups',
    component: PayGroupListPage,
    title: 'Pay groups',
    ...withPermissions(['payroll.pay-group.manage']),
  },
  {
    path: 'runs',
    component: PayrollRunListPage,
    title: 'Payroll runs',
    ...withPermissions(['payroll.run.view']),
  },
  {
    path: 'runs/:id',
    component: PayrollRunDetailPage,
    title: 'Payroll run',
    ...withPermissions(['payroll.run.view']),
  },
];

/** Mounted directly at `/me/payslips` - see `NAVIGATION`'s "My Space" entry. */
export const MY_PAYSLIPS_ROUTES: Routes = [{ path: '', component: MyPayslipsPage, title: 'My payslips' }];
