import type { Routes } from '@angular/router';
import { WorkflowDefinitionsPage } from './definitions/workflow-definitions-page';

/** Mounted directly at `/admin/workflows` - see `NAVIGATION`'s "Administration" entry. */
export const WORKFLOW_ADMIN_ROUTES: Routes = [
  { path: '', component: WorkflowDefinitionsPage, title: 'Workflow definitions' },
];
