import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { AccountsPage } from './accounts-page';
import { ApiKeysPage } from './api-keys-page';
import { AutomationsPage } from './automations-page';
import { ConnectorsPage } from './connectors-page';
import { DeliveriesPage } from './deliveries-page';
import { EmailLogPage } from './email-log-page';
import { EmailSendersPage } from './email-senders-page';
import { SendingPage } from './sending-page';
import { StudioOverviewPage } from './studio-overview-page';

/** Lazily loaded by the shell at `/studio/*` (the Studio menu). */
export const STUDIO_ROUTES: Routes = [
  { path: '', component: StudioOverviewPage, title: 'Studio', ...withPermissions(['datahub.integration.view', 'admin.user.view']) },
  { path: 'email-senders', component: EmailSendersPage, title: 'Email senders', ...withPermissions(['datahub.integration.view']) },
  { path: 'email-log', component: EmailLogPage, title: 'Email log', ...withPermissions(['datahub.integration.view']) },
  { path: 'connectors', component: ConnectorsPage, title: 'Connectors', ...withPermissions(['datahub.integration.view']) },
  { path: 'sending', component: SendingPage, title: 'Text messages', ...withPermissions(['datahub.integration.view']) },
  { path: 'accounts', component: AccountsPage, title: 'Work accounts', ...withPermissions(['datahub.integration.view']) },
  { path: 'automations', component: AutomationsPage, title: 'Automations', ...withPermissions(['datahub.integration.view']) },
  { path: 'deliveries', component: DeliveriesPage, title: 'Automation log', ...withPermissions(['datahub.integration.view']) },
  { path: 'api-keys', component: ApiKeysPage, title: 'API keys', ...withPermissions(['admin.user.view']) },
];
