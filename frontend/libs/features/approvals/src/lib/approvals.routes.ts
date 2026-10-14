import type { Routes } from '@angular/router';
import { ApprovalsInboxPage } from './approvals-inbox-page';
import { DelegationsPage } from './delegations-page';
import { MyRequestsPage } from './my-requests-page';
import { RequestDetailPage } from './request-detail-page';

/** Lazily loaded by the shell at `/approvals/*`. */
export const APPROVALS_ROUTES: Routes = [
  { path: '', component: ApprovalsInboxPage, title: 'Approvals' },
  { path: 'requests', component: MyRequestsPage, title: 'My requests' },
  { path: 'requests/:id', component: RequestDetailPage, title: 'Request' },
  { path: 'delegations', component: DelegationsPage, title: 'Delegations' },
];
