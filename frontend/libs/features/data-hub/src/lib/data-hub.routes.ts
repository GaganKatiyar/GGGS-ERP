import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ImportsPage, IntegrationsPage } from './data-hub-pages';

/** Lazily loaded by the shell at `/admin/data/*`. */
export const DATA_HUB_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'imports' },
  { path: 'imports', component: ImportsPage, title: 'Imports', ...withPermissions(['datahub.import.run']) },
  { path: 'integrations', component: IntegrationsPage, title: 'Integrations', ...withPermissions(['datahub.integration.view']) },
];
