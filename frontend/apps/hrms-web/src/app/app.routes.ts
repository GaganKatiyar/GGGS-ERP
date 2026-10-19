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
        path: 'team/timesheets',
        ...withPermissions(['timesheet.entry.view']),
        loadChildren: () =>
          import('@gggs/features/timesheets').then((m) => m.TEAM_TIMESHEETS_ROUTES),
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
        path: 'me/timesheet',
        ...withPermissions(['self.timesheet.submit']),
        loadChildren: () =>
          import('@gggs/features/timesheets').then((m) => m.MY_TIMESHEET_ROUTES),
      },
      {
        path: 'timesheets',
        ...withPermissions(['timesheet.entry.approve', 'timesheet.settings.manage']),
        loadChildren: () =>
          import('@gggs/features/timesheets').then((m) => m.TIMESHEETS_ROUTES),
      },
      {
        path: 'me/skills',
        ...withPermissions(['self.skill.manage']),
        loadChildren: () =>
          import('@gggs/features/skills').then((m) => m.MY_SKILLS_ROUTES),
      },
      {
        path: 'skills',
        ...withPermissions(['skill.skill.view']),
        loadChildren: () =>
          import('@gggs/features/skills').then((m) => m.SKILLS_ROUTES),
      },
      {
        path: 'resources',
        ...withPermissions([
          'resource.profile.view',
          'resource.request.view',
          'resource.allocation.view',
          'resource.utilization.view',
        ]),
        loadChildren: () =>
          import('@gggs/features/resources').then((m) => m.RESOURCES_ROUTES),
      },
      {
        path: 'me/payslips',
        ...withPermissions(['self.payslip.view']),
        loadChildren: () =>
          import('@gggs/features/payroll').then((m) => m.MY_PAYSLIPS_ROUTES),
      },
      {
        path: 'me/tax-declaration',
        ...withPermissions(['self.tax-declaration.manage']),
        loadChildren: () =>
          import('@gggs/features/payroll').then((m) => m.MY_TAX_DECLARATION_ROUTES),
      },
      {
        path: 'me/compensation',
        ...withPermissions(['self.compensation.view']),
        loadChildren: () =>
          import('@gggs/features/compensation').then((m) => m.MY_COMPENSATION_ROUTES),
      },
      {
        path: 'me/benefits',
        ...withPermissions(['self.benefit.manage']),
        loadChildren: () =>
          import('@gggs/features/benefits').then((m) => m.MY_BENEFITS_ROUTES),
      },
      {
        path: 'me/goals',
        ...withPermissions(['performance.goal.manage-own']),
        loadChildren: () =>
          import('@gggs/features/performance').then((m) => m.MY_GOALS_ROUTES),
      },
      {
        path: 'me/reviews',
        ...withPermissions(['performance.review.manage-own']),
        loadChildren: () =>
          import('@gggs/features/performance').then((m) => m.MY_REVIEWS_ROUTES),
      },
      {
        path: 'me/feedback',
        ...withPermissions(['self.feedback.give']),
        loadChildren: () =>
          import('@gggs/features/performance').then((m) => m.MY_FEEDBACK_ROUTES),
      },
      {
        path: 'me/learning',
        ...withPermissions(['self.learning.view']),
        loadChildren: () =>
          import('@gggs/features/learning').then((m) => m.MY_LEARNING_ROUTES),
      },
      {
        path: 'me/career',
        ...withPermissions(['self.career.view']),
        loadChildren: () =>
          import('@gggs/features/career').then((m) => m.MY_CAREER_ROUTES),
      },
      {
        path: 'performance',
        ...withPermissions(['performance.cycle.view', 'performance.review.manage-own', 'performance.review.view', 'performance.review.manage-as-manager', 'performance.pip.view', 'performance.pip.manage']),
        loadChildren: () =>
          import('@gggs/features/performance').then((m) => m.PERFORMANCE_ROUTES),
      },
      {
        path: 'goals',
        ...withPermissions(['performance.goal.manage', 'performance.goal.manage-own', 'performance.review.view']),
        loadChildren: () =>
          import('@gggs/features/performance').then((m) => m.GOALS_ROUTES),
      },
      {
        path: 'learning',
        ...withPermissions(['learning.course.view', 'learning.enrollment.view', 'learning.certification.view']),
        loadChildren: () =>
          import('@gggs/features/learning').then((m) => m.LEARNING_ROUTES),
      },
      {
        path: 'career',
        ...withPermissions(['career.path.view', 'career.promotion.view', 'career.promotion.manage', 'career.succession.view', 'career.succession.manage', 'career.talent-review.manage', 'workflow.task.act']),
        loadChildren: () =>
          import('@gggs/features/career').then((m) => m.CAREER_ROUTES),
      },
      {
        path: 'me/assets',
        ...withPermissions(['self.asset.view']),
        loadChildren: () =>
          import('@gggs/features/assets').then((m) => m.MY_ASSETS_ROUTES),
      },
      {
        path: 'assets',
        ...withPermissions(['asset.asset.view']),
        loadChildren: () =>
          import('@gggs/features/assets').then((m) => m.ASSETS_ROUTES),
      },
      {
        path: 'me/expenses',
        ...withPermissions(['self.expense-claim.create', 'self.travel-request.create']),
        loadChildren: () =>
          import('@gggs/features/expenses').then((m) => m.MY_EXPENSES_ROUTES),
      },
      {
        path: 'expenses',
        ...withPermissions(['spend.claim.view', 'spend.travel.view', 'self.expense-claim.create', 'self.travel-request.create', 'workflow.task.act']),
        loadChildren: () =>
          import('@gggs/features/expenses').then((m) => m.EXPENSES_ROUTES),
      },
      {
        path: 'me/tickets',
        ...withPermissions(['self.ticket.create']),
        loadChildren: () =>
          import('@gggs/features/helpdesk').then((m) => m.MY_TICKETS_ROUTES),
      },
      {
        path: 'helpdesk',
        ...withPermissions(['helpdesk.ticket.view', 'helpdesk.knowledge.manage', 'self.ticket.create']),
        loadChildren: () =>
          import('@gggs/features/helpdesk').then((m) => m.HELPDESK_ROUTES),
      },
      {
        path: 'me/surveys',
        ...withPermissions(['self.survey.respond']),
        loadChildren: () =>
          import('@gggs/features/engagement').then((m) => m.MY_SURVEYS_ROUTES),
      },
      {
        path: 'engagement',
        ...withPermissions(['engagement.survey.view']),
        loadChildren: () =>
          import('@gggs/features/engagement').then((m) => m.ENGAGEMENT_ROUTES),
      },
      {
        path: 'me/rewards',
        ...withPermissions(['self.reward.view']),
        loadChildren: () =>
          import('@gggs/features/rewards').then((m) => m.MY_REWARDS_ROUTES),
      },
      {
        path: 'rewards',
        ...withPermissions(['reward.recognition.view', 'reward.program.manage', 'reward.catalog.manage', 'reward.redemption.manage']),
        loadChildren: () =>
          import('@gggs/features/rewards').then((m) => m.REWARDS_ROUTES),
      },
      {
        path: 'me/separation',
        ...withPermissions(['self.resignation.submit']),
        loadChildren: () =>
          import('@gggs/features/separation').then((m) => m.MY_SEPARATION_ROUTES),
      },
      {
        path: 'separation',
        ...withPermissions(['separation.case.view', 'separation.clearance.manage', 'separation.case.manage', 'workflow.task.act', 'self.resignation.submit']),
        loadChildren: () =>
          import('@gggs/features/separation').then((m) => m.SEPARATION_ROUTES),
      },
      {
        path: 'analytics',
        ...withPermissions(['report.report.view', 'report.executive.view']),
        loadChildren: () =>
          import('@gggs/features/analytics').then((m) => m.ANALYTICS_ROUTES),
      },
      {
        path: 'calendar',
        ...withPermissions(['self.profile.view', 'employee.profile.view']),
        loadChildren: () =>
          import('@gggs/features/calendar').then((m) => m.CALENDAR_ROUTES),
      },
      {
        path: 'admin/data',
        ...withPermissions(['datahub.import.run', 'datahub.integration.view']),
        loadChildren: () =>
          import('@gggs/features/data-hub').then((m) => m.DATA_HUB_ROUTES),
      },
      {
        path: 'me/onboarding',
        ...withPermissions(['self.onboarding.view']),
        loadChildren: () =>
          import('@gggs/features/onboarding').then((m) => m.MY_ONBOARDING_ROUTES),
      },
      {
        path: 'me/jobs',
        ...withPermissions(['self.referral.create']),
        loadChildren: () =>
          import('@gggs/features/recruitment').then((m) => m.JOB_BOARD_ROUTES),
      },
      {
        path: 'me/interviews',
        ...withPermissions(['self.interview.feedback']),
        loadChildren: () =>
          import('@gggs/features/recruitment').then((m) => m.MY_INTERVIEWS_ROUTES),
      },
      {
        path: 'workforce',
        ...withPermissions(['workforce.plan.view', 'workforce.request.view', 'workforce.request.manage', 'workflow.task.act']),
        loadChildren: () =>
          import('@gggs/features/workforce').then((m) => m.WORKFORCE_ROUTES),
      },
      {
        path: 'recruitment',
        ...withPermissions([
          'recruitment.requisition.view',
          'recruitment.posting.view',
          'recruitment.application.view',
          'recruitment.offer.view',
          'recruitment.analytics.view',
          'recruitment.settings.manage',
          'workflow.task.act',
        ]),
        loadChildren: () =>
          import('@gggs/features/recruitment').then((m) => m.RECRUITMENT_ROUTES),
      },
      {
        path: 'onboarding',
        ...withPermissions(['onboarding.case.view', 'onboarding.template.view', 'onboarding.case.manage']),
        loadChildren: () =>
          import('@gggs/features/onboarding').then((m) => m.ONBOARDING_ROUTES),
      },
      {
        path: 'payroll',
        ...withPermissions(['payroll.run.view', 'payroll.employee-salary.view', 'payroll.structure.manage', 'payroll.component.manage', 'payroll.input.manage', 'payroll.tax-declaration.view', 'payroll.settings.manage']),
        loadChildren: () =>
          import('@gggs/features/payroll').then((m) => m.PAYROLL_ROUTES),
      },
      {
        path: 'compliance',
        ...withPermissions(['compliance.rule.view', 'compliance.filing.view']),
        loadChildren: () =>
          import('@gggs/features/compliance').then((m) => m.COMPLIANCE_ROUTES),
      },
      {
        path: 'compensation',
        ...withPermissions(['compensation.band.view', 'compensation.revision.view', 'compensation.variable-pay.view', 'compensation.equity.view']),
        loadChildren: () =>
          import('@gggs/features/compensation').then((m) => m.COMPENSATION_ROUTES),
      },
      {
        path: 'benefits',
        ...withPermissions(['benefit.plan.view', 'benefit.enrollment.view']),
        loadChildren: () =>
          import('@gggs/features/benefits').then((m) => m.BENEFITS_ROUTES),
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
