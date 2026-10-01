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
        path: 'approvals',
        ...withPermissions(['workflow.task.view']),
        loadChildren: () =>
          import('@gggs/features/workflow').then((m) => m.APPROVALS_ROUTES),
      },
      {
        path: 'leave',
        ...withPermissions(['leave.request.view']),
        loadChildren: () =>
          import('@gggs/features/leave').then((m) => m.LEAVE_ROUTES),
      },
      {
        path: 'holidays',
        ...withPermissions(['leave.holiday.manage']),
        loadChildren: () =>
          import('@gggs/features/leave').then((m) => m.HOLIDAYS_ROUTES),
      },
      {
        path: 'attendance',
        ...withPermissions(['attendance.record.view']),
        loadChildren: () =>
          import('@gggs/features/attendance').then((m) => m.ATTENDANCE_ROUTES),
      },
      {
        path: 'me/leave',
        ...withPermissions(['self.leave.view']),
        loadChildren: () =>
          import('@gggs/features/leave').then((m) => m.MY_LEAVE_ROUTES),
      },
      {
        path: 'me/attendance',
        ...withPermissions(['self.attendance.view']),
        loadChildren: () =>
          import('@gggs/features/attendance').then(
            (m) => m.MY_ATTENDANCE_ROUTES,
          ),
      },
      {
        path: 'me/payslips',
        ...withPermissions(['self.payslip.view']),
        loadChildren: () =>
          import('@gggs/features/payroll').then((m) => m.MY_PAYSLIPS_ROUTES),
      },
      {
        path: 'me/expenses',
        ...withPermissions(['self.expense-claim.create']),
        loadChildren: () =>
          import('@gggs/features/spend').then((m) => m.MY_EXPENSES_ROUTES),
      },
      {
        path: 'me/travel',
        ...withPermissions(['self.travel-request.create']),
        loadChildren: () =>
          import('@gggs/features/spend').then((m) => m.MY_TRAVEL_ROUTES),
      },
      {
        path: 'me/assets',
        ...withPermissions(['self.asset.view']),
        loadChildren: () =>
          import('@gggs/features/assets').then((m) => m.MY_ASSETS_ROUTES),
      },
      {
        path: 'team',
        ...withPermissions(['employee.profile.view']),
        loadChildren: () =>
          import('@gggs/features/employees').then((m) => m.TEAM_ROUTES),
      },
      {
        path: 'team/attendance',
        ...withPermissions(['attendance.record.view']),
        loadChildren: () =>
          import('@gggs/features/attendance').then(
            (m) => m.TEAM_ATTENDANCE_ROUTES,
          ),
      },
      {
        path: 'team/leave',
        ...withPermissions(['leave.request.view']),
        loadChildren: () =>
          import('@gggs/features/leave').then((m) => m.TEAM_LEAVE_ROUTES),
      },
      {
        path: 'reports',
        ...withPermissions(['report.report.view']),
        loadChildren: () =>
          import('@gggs/features/reports').then((m) => m.REPORTS_ROUTES),
      },
      {
        path: 'recruitment/requisitions',
        ...withPermissions(['recruitment.requisition.view']),
        loadChildren: () =>
          import('@gggs/features/recruitment').then((m) => m.RECRUITMENT_ROUTES),
      },
      {
        path: 'onboarding',
        ...withPermissions(['onboarding.template.view', 'onboarding.case.view']),
        loadChildren: () =>
          import('@gggs/features/onboarding').then((m) => m.ONBOARDING_ROUTES),
      },
      {
        path: 'performance',
        ...withPermissions(['performance.cycle.view']),
        loadChildren: () =>
          import('@gggs/features/performance').then((m) => m.PERFORMANCE_ROUTES),
      },
      {
        path: 'learning',
        ...withPermissions(['learning.course.view']),
        loadChildren: () =>
          import('@gggs/features/learning').then((m) => m.LEARNING_ROUTES),
      },
      {
        path: 'payroll',
        ...withPermissions([
          'payroll.component.manage',
          'payroll.structure.manage',
          'payroll.employee-salary.view',
          'payroll.pay-group.manage',
          'payroll.run.view',
        ]),
        loadChildren: () =>
          import('@gggs/features/payroll').then((m) => m.PAYROLL_ROUTES),
      },
      {
        path: 'spend',
        ...withPermissions(['spend.category.manage', 'spend.claim.view', 'spend.travel.view']),
        loadChildren: () =>
          import('@gggs/features/spend').then((m) => m.SPEND_ROUTES),
      },
      {
        path: 'assets',
        ...withPermissions(['asset.category.manage', 'asset.asset.view']),
        loadChildren: () =>
          import('@gggs/features/assets').then((m) => m.ASSETS_ROUTES),
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
        // Not nested inside the admin feature module: features never import one another, and
        // Workflow already needs its own module for the approvals inbox - see that module's own
        // remarks. NAVIGATION's "Administration" > "Workflows" entry points here.
        path: 'admin/workflows',
        ...withPermissions(['workflow.definition.view']),
        loadChildren: () =>
          import('@gggs/features/workflow').then((m) => m.WORKFLOW_ADMIN_ROUTES),
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
