import type { Routes } from '@angular/router';
import { PerformanceCycleDetailPage } from './cycles/performance-cycle-detail-page';
import { PerformanceCycleListPage } from './cycles/performance-cycle-list-page';

/** Lazily loaded by the shell at `/performance/*`. */
export const PERFORMANCE_ROUTES: Routes = [
  { path: '', component: PerformanceCycleListPage, title: 'Performance cycles' },
  { path: 'cycles/:id', component: PerformanceCycleDetailPage, title: 'Performance cycle' },
];
