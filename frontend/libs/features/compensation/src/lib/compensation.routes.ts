import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { EquityPage, MyCompensationPage, RevisionsPage, SalaryBandsPage, VariablePayPage } from './compensation-pages';

/** Lazily loaded by the shell at `/compensation/*`. */
export const COMPENSATION_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'revisions' },
  { path: 'bands', component: SalaryBandsPage, title: 'Salary bands', ...withPermissions(['compensation.band.view']) },
  { path: 'revisions', component: RevisionsPage, title: 'Salary revisions', ...withPermissions(['compensation.revision.view']) },
  { path: 'variable-pay', component: VariablePayPage, title: 'Variable pay', ...withPermissions(['compensation.variable-pay.view']) },
  { path: 'equity', component: EquityPage, title: 'Equity', ...withPermissions(['compensation.equity.view']) },
];

/** Lazily loaded by the shell at `/me/compensation`. */
export const MY_COMPENSATION_ROUTES: Routes = [{ path: '', component: MyCompensationPage, title: 'My compensation' }];
