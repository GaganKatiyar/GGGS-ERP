import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { CreateJobRequisitionPage } from './create/create-job-requisition-page';
import { JobRequisitionDetailPage } from './detail/job-requisition-detail-page';
import { JobRequisitionListPage } from './list/job-requisition-list-page';

/**
 * Lazily loaded by the shell at `/recruitment/requisitions/*`. `new` is declared before `:id`, so
 * the router does not mistake it for an id - the usual ordering trap with a catch-all parameterized
 * route (see `EMPLOYEES_ROUTES`'s own remarks).
 */
export const RECRUITMENT_ROUTES: Routes = [
  { path: '', component: JobRequisitionListPage, title: 'Job requisitions' },
  {
    path: 'new',
    component: CreateJobRequisitionPage,
    title: 'New requisition',
    ...withPermissions(['recruitment.requisition.manage']),
  },
  { path: ':id', component: JobRequisitionDetailPage, title: 'Requisition' },
];
