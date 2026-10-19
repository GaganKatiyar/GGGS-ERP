import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ClaimPage, ExpensesPage, MyExpensesPage, TripPage } from './expenses-pages';

/** Lazily loaded by the shell at `/expenses/*`. */
export const EXPENSES_ROUTES: Routes = [
  { path: '', component: ExpensesPage, title: 'Expenses', ...withPermissions(['spend.claim.view']) },
  { path: 'claims/:id', component: ClaimPage, title: 'Expense claim' },
  { path: 'travel/:id', component: TripPage, title: 'Travel request' },
];

/** Lazily loaded by the shell at `/me/expenses`. */
export const MY_EXPENSES_ROUTES: Routes = [{ path: '', component: MyExpensesPage, title: 'My expenses' }];
