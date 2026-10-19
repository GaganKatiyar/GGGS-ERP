import type { Routes } from '@angular/router';
import { AnalyticsHomePage, DashboardViewPage } from './analytics-pages';

/** Lazily loaded by the shell at `/analytics/*`. */
export const ANALYTICS_ROUTES: Routes = [
  { path: '', component: AnalyticsHomePage, title: 'Analytics' },
  { path: ':area', component: DashboardViewPage, title: 'Analytics' },
];
