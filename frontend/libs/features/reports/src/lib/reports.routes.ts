import type { Routes } from '@angular/router';
import { ReportsPage } from './reports-page';

/** Lazily loaded by the shell at `/reports`; see `NAVIGATION`'s "Reports" entry. */
export const REPORTS_ROUTES: Routes = [
  { path: '', component: ReportsPage, title: 'Reports' },
];
