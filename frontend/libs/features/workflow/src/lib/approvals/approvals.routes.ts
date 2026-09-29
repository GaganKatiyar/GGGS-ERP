import type { Routes } from '@angular/router';
import { ApprovalsPage } from './approvals-page';

/** Mounted directly at `/approvals` - see `NAVIGATION`'s "Approvals" entry. */
export const APPROVALS_ROUTES: Routes = [
  { path: '', component: ApprovalsPage, title: 'Approvals' },
];
