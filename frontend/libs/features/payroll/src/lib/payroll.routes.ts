import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { MyTaxDeclarationPage } from './my-tax-declaration-page';
import { PayrollInputsPage, TaxDeclarationsPage } from './operations-pages';
import { MyPayslipsPage, PayslipPage, PayslipsPage } from './payslip-pages';
import { PayrollRunPage } from './run-page';
import { PayrollDashboardPage, PayrollRunsPage } from './runs-pages';
import { EmployeeSalaryPage, SalariesPage } from './salary-pages';
import { PayrollSettingsPage, SalaryStructuresPage } from './setup-pages';

/** Lazily loaded by the shell at `/payroll/*`. */
export const PAYROLL_ROUTES: Routes = [
  { path: '', component: PayrollDashboardPage, title: 'Payroll', ...withPermissions(['payroll.run.view']) },
  { path: 'runs', component: PayrollRunsPage, title: 'Payroll runs', ...withPermissions(['payroll.run.view']) },
  { path: 'runs/:id', component: PayrollRunPage, title: 'Payroll run', ...withPermissions(['payroll.run.view']) },
  { path: 'salaries', component: SalariesPage, title: 'Salaries', ...withPermissions(['payroll.employee-salary.view']) },
  { path: 'salaries/:employeeId', component: EmployeeSalaryPage, title: 'Salary', ...withPermissions(['payroll.employee-salary.view']) },
  { path: 'structures', component: SalaryStructuresPage, title: 'Salary structures', ...withPermissions(['payroll.structure.manage', 'payroll.component.manage']) },
  { path: 'inputs', component: PayrollInputsPage, title: 'Payroll inputs', ...withPermissions(['payroll.input.manage']) },
  { path: 'declarations', component: TaxDeclarationsPage, title: 'Tax declarations', ...withPermissions(['payroll.tax-declaration.view']) },
  { path: 'payslips', component: PayslipsPage, title: 'Payslips', ...withPermissions(['payroll.run.view']) },
  { path: 'payslips/:id', component: PayslipPage, title: 'Payslip', ...withPermissions(['payroll.run.view']) },
  { path: 'settings', component: PayrollSettingsPage, title: 'Payroll settings', ...withPermissions(['payroll.settings.manage']) },
];

/** Lazily loaded by the shell at `/me/payslips`. */
export const MY_PAYSLIPS_ROUTES: Routes = [
  { path: '', component: MyPayslipsPage, title: 'My payslips' },
  { path: ':id', component: PayslipPage, title: 'Payslip', data: { mine: true } },
];

/** Lazily loaded by the shell at `/me/tax-declaration`. */
export const MY_TAX_DECLARATION_ROUTES: Routes = [{ path: '', component: MyTaxDeclarationPage, title: 'Tax declaration' }];
