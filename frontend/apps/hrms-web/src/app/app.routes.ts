import type { Route } from '@angular/router';
import { authGuard } from '@gggs/core/auth';
import { Shell } from '@gggs/core/layout';
import { withPermissions } from '@gggs/core/permissions';

/**
 * Application routes.
 *
 * Everything sits under the shell behind {@link authGuard}, and each feature is loaded lazily so
 * the initial bundle stays small as the twenty-odd modules land. Feature routes declare the
 * permission they need with `withPermissions`, which keeps the requirement next to the route
 * rather than buried in a component.
 */
export const appRoutes: Route[] = [
  {
    // Outside the shell and outside the guard: the shell needs a session to render its navigation,
    // and a guard on the page you are sent to when you have no session is a redirect loop.
    path: 'sign-in',
    loadComponent: () =>
      import('./pages/sign-in-page').then((m) => m.SignInPage),
    title: 'Sign in',
  },
  {
    // Also outside the shell and the guard, for the same reason: the invitee has no session, and
    // never will until this page succeeds.
    path: 'complete-invitation',
    loadComponent: () =>
      import('./pages/complete-invitation-page').then(
        (m) => m.CompleteInvitationPage,
      ),
    title: 'Set your password',
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadChildren: () =>
          import('@gggs/features/dashboard').then((m) => m.DASHBOARD_ROUTES),
      },
      {
        path: 'org',
        ...withPermissions(['org.structure.view']),
        loadChildren: () =>
          import('@gggs/features/organization').then(
            (m) => m.ORGANIZATION_ROUTES,
          ),
      },
      {
        path: 'employees',
        ...withPermissions(['employee.profile.view']),
        loadChildren: () =>
          import('@gggs/features/employees').then((m) => m.EMPLOYEES_ROUTES),
      },
      {
        path: 'notifications',
        ...withPermissions(['notification.inbox.view']),
        loadChildren: () =>
          import('@gggs/features/notifications').then(
            (m) => m.NOTIFICATIONS_ROUTES,
          ),
      },
      {
        path: 'admin',
        // Matches NAVIGATION's "Administration" group: any one of these lets someone in, and each
        // child route narrows further to the specific permission it actually needs.
        ...withPermissions([
          'admin.user.view',
          'admin.role.view',
          'admin.setting.view',
          'document.file.view',
          'audit.log.view',
        ]),
        loadChildren: () =>
          import('@gggs/features/admin').then((m) => m.ADMIN_ROUTES),
      },
      {
        path: 'forbidden',
        loadComponent: () =>
          import('./pages/forbidden-page').then((m) => m.ForbiddenPage),
        title: 'Not allowed',
      },
      {
        path: 'feature-unavailable',
        loadComponent: () =>
          import('./pages/feature-unavailable-page').then(
            (m) => m.FeatureUnavailablePage,
          ),
        title: 'Not included in your plan',
      },
    ],
  },
  {
    path: '**',
    loadComponent: () =>
      import('./pages/not-found-page').then((m) => m.NotFoundPage),
    title: 'Page not found',
  },
];
