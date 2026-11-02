import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { AccountsPage } from './accounts-page';
import { ApiKeysPage } from './api-keys-page';
import { AutomationsPage } from './automations-page';
import { ConnectorsPage } from './connectors-page';
import { DeliveriesPage } from './deliveries-page';
import { SendingPage } from './sending-page';
import { StudioOverviewPage } from './studio-overview-page';

/** Lazily loaded by the shell at `/studio/*` (Settings → Studio). */
export const STUDIO_ROUTES: Routes = [
  { path: '', component: StudioOverviewPage, title: 'Studio', ...withPermissions(['datahub.integration.view', 'admin.user.view']) },
  { path: 'connectors', component: ConnectorsPage, title: 'Connectors', ...withPermissions(['datahub.integration.view']) },
  { path: 'sending', component: SendingPage, title: 'Sending', ...withPermissions(['datahub.integration.view']) },
  { path: 'accounts', component: AccountsPage, title: 'Accounts', ...withPermissions(['datahub.integration.view']) },
  { path: 'automations', component: AutomationsPage, title: 'Automations', ...withPermissions(['datahub.integration.view']) },
  { path: 'deliveries', component: DeliveriesPage, title: 'Deliveries', ...withPermissions(['datahub.integration.view']) },
  { path: 'api-keys', component: ApiKeysPage, title: 'API keys', ...withPermissions(['admin.user.view']) },
];
