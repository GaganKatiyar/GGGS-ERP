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
        path: 'employees',
        ...withPermissions(['employee.profile.view']),
        loadChildren: () =>
          import('@gggs/features/dashboard').then((m) => m.DASHBOARD_ROUTES),
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
