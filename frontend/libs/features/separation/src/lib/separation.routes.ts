import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ClearancePage, MySeparationPage, SeparationCasePage, SeparationPage } from './separation-pages';
import { SeparationPolicyPage } from './separation-policy-page';

/** Lazily loaded by the shell at `/separation/*`. */
export const SEPARATION_ROUTES: Routes = [
  { path: '', component: SeparationPage, title: 'Separation', ...withPermissions(['separation.case.view']) },
  { path: 'clearance', component: ClearancePage, title: 'My clearance' },
  { path: 'policy', component: SeparationPolicyPage, title: 'Separation policy', ...withPermissions(['separation.policy.manage']) },
  { path: 'cases/:id', component: SeparationCasePage, title: 'Separation' },
];

/** Lazily loaded by the shell at `/me/separation`. */
export const MY_SEPARATION_ROUTES: Routes = [{ path: '', component: MySeparationPage, title: 'My resignation' }];
