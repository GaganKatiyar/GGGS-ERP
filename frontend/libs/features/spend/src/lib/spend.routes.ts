import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ExpenseCategoryListPage } from './categories/expense-category-list-page';
import { MyClaimsPage } from './my-claims/my-claims-page';
import { MyTravelRequestsPage } from './my-travel/my-travel-requests-page';
import { TravelRequestDetailPage } from './travel-requests/travel-request-detail-page';
import { TravelRequestListPage } from './travel-requests/travel-request-list-page';

/** Lazily loaded by the shell at `/spend/*`. */
export const SPEND_ROUTES: Routes = [
  { path: '', component: ExpenseCategoryListPage, title: 'Expense categories' },
  {
    path: 'travel-requests',
    component: TravelRequestListPage,
    title: 'Travel requests',
    ...withPermissions(['spend.travel.view']),
  },
  {
    path: 'travel-requests/:id',
    component: TravelRequestDetailPage,
    title: 'Travel request',
    ...withPermissions(['spend.travel.view']),
  },
];

/** Mounted directly at `/me/expenses` - see `NAVIGATION`'s "My Space" entry. */
export const MY_EXPENSES_ROUTES: Routes = [{ path: '', component: MyClaimsPage, title: 'My expense claims' }];

/** Mounted directly at `/me/travel` - see `NAVIGATION`'s "My Space" entry. */
export const MY_TRAVEL_ROUTES: Routes = [{ path: '', component: MyTravelRequestsPage, title: 'My travel requests' }];
