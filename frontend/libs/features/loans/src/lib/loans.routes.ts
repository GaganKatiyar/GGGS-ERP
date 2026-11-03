import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { LoanPage, LoanTypesPage, LoansPage, MyLoansPage } from './loans-pages';

/** Lazily loaded by the shell at `/loans/*`. */
export const LOANS_ROUTES: Routes = [
  { path: '', component: LoansPage, title: 'Loans', ...withPermissions(['loan.loan.view']) },
  { path: 'types', component: LoanTypesPage, title: 'Loan types', ...withPermissions(['loan.type.manage']) },
  { path: ':id', component: LoanPage, title: 'Loan' },
];

/** Lazily loaded by the shell at `/me/loans`. */
export const MY_LOANS_ROUTES: Routes = [{ path: '', component: MyLoansPage, title: 'My loans' }];
