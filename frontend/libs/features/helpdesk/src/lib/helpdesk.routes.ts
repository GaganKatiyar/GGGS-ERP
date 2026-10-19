import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { HelpdeskPage, KnowledgePage, MyTicketsPage, TicketPage } from './helpdesk-pages';

/** Lazily loaded by the shell at `/helpdesk/*`. */
export const HELPDESK_ROUTES: Routes = [
  { path: '', component: HelpdeskPage, title: 'Helpdesk', ...withPermissions(['helpdesk.ticket.view']) },
  { path: 'knowledge', component: KnowledgePage, title: 'Knowledge base' },
  { path: 'tickets/:id', component: TicketPage, title: 'Ticket' },
];

/** Lazily loaded by the shell at `/me/tickets`. */
export const MY_TICKETS_ROUTES: Routes = [
  { path: '', component: MyTicketsPage, title: 'My tickets' },
  { path: ':id', component: TicketPage, title: 'Ticket' },
];
