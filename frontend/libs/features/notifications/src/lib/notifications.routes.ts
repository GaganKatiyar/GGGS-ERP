import type { Routes } from '@angular/router';
import { NotificationInboxPage } from './notification-inbox-page';

/** Lazily loaded by the shell at `/notifications`. */
export const NOTIFICATIONS_ROUTES: Routes = [{ path: '', component: NotificationInboxPage, title: 'Notifications' }];
