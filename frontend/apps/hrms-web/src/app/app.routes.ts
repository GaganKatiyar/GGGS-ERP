import type { Route } from '@angular/router';
import { authGuard } from '@gggs/core/auth';
import { Shell } from '@gggs/core/layout';
import { withPermissions } from '@gggs/core/permissions';

/**
 * Application routes.
 *
 * Everything sits under the shell behind {@link authGuard}, and each feature is loaded lazily so
 * the initial bundle stays small as the PeopleOS modules land. Feature routes declare the
 * permission they need with `withPermissions`, which keeps the requirement next to the route
 * rather than buried in a component.
 *
 * Only modules with a working backend have a route. The rest are listed in the navigation
 * manifest as planned (see `isBuilt` in @gggs/core/layout) and gain a route here when they ship -
 * milestone by milestone, per docs/roadmap.md.
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
        path: 'me/profile',
        ...withPermissions(['self.profile.view']),
        loadChildren: () =>
          import('@gggs/features/employees').then((m) => m.MY_PROFILE_ROUTES),
      },
      {
        path: 'team/attendance',
        ...withPermissions(['attendance.record.view']),
        loadChildren: () =>
          import('@gggs/features/time').then((m) => m.TEAM_ATTENDANCE_ROUTES),
      },
      {
        path: 'team/leave',
        ...withPermissions(['leave.request.view']),
        loadChildren: () =>
          import('@gggs/features/time').then((m) => m.TEAM_LEAVE_ROUTES),
      },
      {
        path: 'team',
        ...withPermissions(['employee.profile.view']),
        loadChildren: () =>
          import('@gggs/features/employees').then((m) => m.TEAM_ROUTES),
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
        path: 'approvals',
        ...withPermissions(['workflow.task.view', 'workflow.instance.view-all']),
        loadChildren: () =>
          import('@gggs/features/approvals').then((m) => m.APPROVALS_ROUTES),
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
        path: 'documents',
        ...withPermissions(['document.file.view']),
        loadChildren: () =>
          import('@gggs/features/documents').then((m) => m.DOCUMENTS_ROUTES),
      },
      {
        path: 'me/documents',
        ...withPermissions(['self.document.view']),
        loadChildren: () =>
          import('@gggs/features/documents').then((m) => m.MY_DOCUMENTS_ROUTES),
      },
      {
        path: 'policies',
        ...withPermissions(['policy.policy.view']),
        loadChildren: () =>
          import('@gggs/features/policies').then((m) => m.POLICIES_ROUTES),
      },
      {
        path: 'me/policies',
        ...withPermissions(['self.policy.view']),
        loadChildren: () =>
          import('@gggs/features/policies').then((m) => m.MY_POLICIES_ROUTES),
      },
      {
        path: 'me/attendance',
        ...withPermissions(['self.attendance.view']),
        loadChildren: () =>
          import('@gggs/features/time').then((m) => m.MY_ATTENDANCE_ROUTES),
      },
      {
        path: 'me/leave',
        ...withPermissions(['self.leave.view']),
        loadChildren: () =>
          import('@gggs/features/time').then((m) => m.MY_LEAVE_ROUTES),
      },
      {
        path: 'attendance',
        ...withPermissions(['attendance.record.view']),
        loadChildren: () =>
          import('@gggs/features/time').then((m) => m.ATTENDANCE_ROUTES),
      },
      {
        path: 'leave',
        ...withPermissions(['leave.request.view', 'leave.type.manage', 'leave.policy.manage', 'leave.holiday.manage']),
        loadChildren: () =>
          import('@gggs/features/time').then((m) => m.LEAVE_ROUTES),
      },
      {
        path: 'clients',
        ...withPermissions(['client.client.view']),
        loadChildren: () =>
          import('@gggs/features/clients').then((m) => m.CLIENTS_ROUTES),
      },
      {
        path: 'contracts',
        ...withPermissions(['contract.contract.view']),
        loadChildren: () =>
          import('@gggs/features/clients').then((m) => m.CONTRACTS_ROUTES),
      },
      {
        path: 'projects',
        ...withPermissions(['project.project.view']),
        loadChildren: () =>
          import('@gggs/features/projects').then((m) => m.PROJECTS_ROUTES),
      },
      {
        path: 'admin',
        // Matches NAVIGATION's "Administration" group: any one of these lets someone in, and each
        // child route narrows further to the specific permission it actually needs.
        ...withPermissions([
          'admin.user.view',
          'admin.role.view',
          'admin.setting.view',
          'audit.log.view',
          'master-data.lookup.manage',
          'workflow.definition.view',
          'notification.template.view',
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
