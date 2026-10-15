import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ClientDetailPage } from './client-detail-page';
import { ClientsPage } from './clients-page';
import { ContactsPage } from './contacts-page';
import { ContractsPage } from './contracts-page';

/** Lazily loaded by the shell at `/clients/*`. */
export const CLIENTS_ROUTES: Routes = [
  { path: '', component: ClientsPage, title: 'Clients' },
  { path: 'contacts', component: ContactsPage, title: 'Client contacts' },
  { path: ':id', component: ClientDetailPage, title: 'Client' },
];

/** Lazily loaded by the shell at `/contracts`. */
export const CONTRACTS_ROUTES: Routes = [{ path: '', component: ContractsPage, title: 'Contracts', ...withPermissions(['contract.contract.view']) }];
