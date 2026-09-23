import type { Routes } from '@angular/router';
import { DashboardPage } from './dashboard-page';

/** Lazily loaded by the shell; see `app.routes.ts`. */
export const DASHBOARD_ROUTES: Routes = [
  {
    path: '',
    component: DashboardPage,
    title: 'Dashboard',
  },
];
