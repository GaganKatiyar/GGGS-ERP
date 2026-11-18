import type { Routes } from '@angular/router';
import { authGuard } from '@gggs/core/auth';
import { withPermissions } from '@gggs/core/permissions';
import { ConsoleAccountPage } from './account-page';
import { ConsoleActivityPage } from './activity-page';
import { ConsoleShell } from './console-shell';
import { ConsoleDashboardPage } from './dashboard-page';
import { ConsoleHealthPage } from './health-page';
import { ConsoleNewOrganisationPage } from './new-organisation-page';
import { ConsoleOrganisationPage } from './organisation-page';
import { ConsoleOrganisationsPage } from './organisations-page';
import { ConsolePlansPage } from './plans-page';
import { PLATFORM } from './platform-permissions';
import { ConsoleAcceptInvitationPage, ConsoleSetupPage, ConsoleSignInPage } from './signed-out-pages';
import { ConsoleStaffPage } from './staff-page';

/** Every route of the platform console application. */
export const CONSOLE_ROUTES: Routes = [
  { path: 'sign-in', component: ConsoleSignInPage, title: 'Sign in' },
  { path: 'setup', component: ConsoleSetupPage, title: 'Set up' },
  { path: 'accept-invitation', component: ConsoleAcceptInvitationPage, title: 'Join the platform team' },
  {
    path: '',
    component: ConsoleShell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', component: ConsoleDashboardPage, title: 'Dashboard', ...withPermissions([PLATFORM.tenantView]) },
      { path: 'organisations', component: ConsoleOrganisationsPage, title: 'Organisations', ...withPermissions([PLATFORM.tenantView]) },
      { path: 'organisations/new', component: ConsoleNewOrganisationPage, title: 'New organisation', ...withPermissions([PLATFORM.tenantManage]) },
      { path: 'organisations/:id', component: ConsoleOrganisationPage, title: 'Organisation', ...withPermissions([PLATFORM.tenantView]) },
      { path: 'plans', component: ConsolePlansPage, title: 'Plans & modules', ...withPermissions([PLATFORM.tenantView]) },
      { path: 'staff', component: ConsoleStaffPage, title: 'Platform staff', ...withPermissions([PLATFORM.staffView]) },
      { path: 'health', component: ConsoleHealthPage, title: 'System health', ...withPermissions([PLATFORM.healthView]) },
      { path: 'activity', component: ConsoleActivityPage, title: 'Activity log', ...withPermissions([PLATFORM.tenantView]) },
      { path: 'account', component: ConsoleAccountPage, title: 'Change password' },
      {
        path: 'forbidden',
        title: 'Not allowed',
        loadComponent: () => import('./forbidden-page').then((m) => m.ConsoleForbiddenPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
