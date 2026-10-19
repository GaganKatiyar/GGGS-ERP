import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ManpowerRequestPage, ManpowerRequestsPage, WorkforceDashboardPage, WorkforcePlanPage, WorkforcePlansPage } from './workforce-pages';

/** Lazily loaded by the shell at `/workforce/*`. Request pages are also where approvers land from their task. */
export const WORKFORCE_ROUTES: Routes = [
  { path: '', component: WorkforceDashboardPage, title: 'Workforce planning', ...withPermissions(['workforce.plan.view']) },
  { path: 'plans', component: WorkforcePlansPage, title: 'Workforce plans', ...withPermissions(['workforce.plan.view']) },
  { path: 'plans/:id', component: WorkforcePlanPage, title: 'Workforce plan', ...withPermissions(['workforce.plan.view', 'workflow.task.act']) },
  { path: 'requests', component: ManpowerRequestsPage, title: 'Manpower requests', ...withPermissions(['workforce.request.view', 'workforce.request.manage']) },
  { path: 'requests/:id', component: ManpowerRequestPage, title: 'Manpower request', ...withPermissions(['workforce.request.view', 'workforce.request.manage', 'workflow.task.act']) },
];
