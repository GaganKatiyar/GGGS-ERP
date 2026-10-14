import type { Routes } from '@angular/router';
import { MyPoliciesPage, MyPolicyPage } from './my-policies-page';
import { PolicyDetailPage } from './policy-detail-page';
import { PolicyListPage } from './policy-list-page';

/** Lazily loaded by the shell at `/policies/*`. */
export const POLICIES_ROUTES: Routes = [
  { path: '', component: PolicyListPage, title: 'Policies' },
  { path: ':id', component: PolicyDetailPage, title: 'Policy' },
];

/** Mounted at `/me/policies`: self-service. */
export const MY_POLICIES_ROUTES: Routes = [
  { path: '', component: MyPoliciesPage, title: 'My policies' },
  { path: ':id', component: MyPolicyPage, title: 'Policy' },
];
