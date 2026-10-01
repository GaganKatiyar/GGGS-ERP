import type { Routes } from '@angular/router';
import { ExpenseCategoryListPage } from './categories/expense-category-list-page';
import { MyClaimsPage } from './my-claims/my-claims-page';

/** Lazily loaded by the shell at `/spend/*`. */
export const SPEND_ROUTES: Routes = [
  { path: '', component: ExpenseCategoryListPage, title: 'Expense categories' },
];

/** Mounted directly at `/me/expenses` - see `NAVIGATION`'s "My Space" entry. */
export const MY_EXPENSES_ROUTES: Routes = [{ path: '', component: MyClaimsPage, title: 'My expense claims' }];
