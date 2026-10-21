/**
 * The navigation manifest.
 *
 * The sidebar is generated from this rather than hand-written, so a module appears for exactly the
 * people who can use it. Adding a screen means adding an entry with its permission - there is no
 * second place to remember, and no hand-maintained menu that drifts from the routes.
 */
export interface NavItem {
  readonly label: string;
  /** A shorter label for the narrow module rail, when the full one does not fit two short lines. */
  readonly railLabel?: string;
  readonly icon: string;
  readonly route?: string;
  /** Any one of these permissions reveals the item. */
  readonly permissions?: readonly string[];
  /** Feature the tenant's plan must include. */
  readonly feature?: string;
  readonly children?: readonly NavItem[];
  /** Named counter shown as a badge, e.g. pending approvals. */
  readonly badge?: 'approvals' | 'notifications';
  /** On the roadmap but not built: never shown. See {@link isBuilt}. */
  readonly planned?: boolean;
  /** The roadmap milestone a planned entry ships in (docs/roadmap.md). */
  readonly milestone?: string;
}

/** Decides whether one navigation entry is usable by the current user. */
export type NavVisibilityCheck = (item: NavItem) => boolean;

/**
 * Filters the manifest down to what the user can actually reach.
 *
 * A group whose children are all hidden is dropped rather than left as a dead heading - an
 * "Administration" menu that opens onto nothing is worse than no menu at all.
 *
 * Pure on purpose: this is the rule that decides what an HR admin versus an employee sees, so it
 * is testable without a component, a TestBed or a browser.
 */
export function filterNavigation(
  items: readonly NavItem[],
  isVisible: NavVisibilityCheck,
): NavItem[] {
  const visible: NavItem[] = [];

  for (const item of items) {
    if (!isVisible(item)) {
      continue;
    }

    if (item.children === undefined) {
      visible.push(item);
      continue;
    }

    const children = filterNavigation(item.children, isVisible);
    if (children.length > 0) {
      visible.push({ ...item, children });
    }
  }

  return visible;
}

/** Where the current URL sits in the navigation: its rail group and the entry it belongs to. */
export interface NavLocation {
  /** The top-level entry shown as active in the rail. */
  readonly group: NavItem;
  /** The deepest entry whose route the URL is on or under; the group itself for a top-level page. */
  readonly entry: NavItem;
}

/** True when `url` is `route` itself or a page beneath it (`/payroll/runs/123` is under `/payroll/runs`). */
export function isUnder(url: string, route: string): boolean {
  const path = url.split(/[?#]/)[0].replace(/\/+$/, '') || '/';
  return path === route || path.startsWith(route === '/' ? '/' : `${route}/`);
}

/**
 * Finds where a URL belongs in the (already filtered) navigation. The longest matching route wins,
 * so `/payroll/runs/42` lands on "Payroll runs" rather than the "Payroll" overview, and a group
 * that shares a prefix with another (`/me/leave` and `/me`) never steals its sibling's pages.
 */
export function locate(items: readonly NavItem[], url: string): NavLocation | null {
  let best: NavLocation | null = null;
  let bestLength = -1;

  for (const group of items) {
    for (const entry of [group, ...(group.children ?? [])]) {
      if (entry.route && entry.route.length > bestLength && isUnder(url, entry.route)) {
        best = { group, entry };
        bestLength = entry.route.length;
      }
    }
  }

  return best;
}

/** The pages a module offers as tabs above the content: every module with more than one page. */
export function moduleTabs(group: NavItem | null | undefined): readonly NavItem[] {
  const children = group?.children ?? [];
  return children.length >= 2 ? children : [];
}

/**
 * Whether an entry's module has been built yet.
 *
 * The manifest describes the whole of PeopleOS (spec §117) so the sidebar's shape is decided once,
 * but modules ship milestone by milestone (docs/roadmap.md). A planned entry has no route behind it
 * yet, so it never reaches the sidebar whatever the user's permissions or the tenant's plan.
 * Shipping a module means adding its route and deleting its `planned` flag - nothing else.
 */
export const isBuilt = (item: NavItem): boolean => item.planned !== true;

export const NAVIGATION: readonly NavItem[] = [
  {
    label: 'Dashboard',
    icon: 'pi pi-home',
    route: '/dashboard',
  },
  {
    label: 'Approvals',
    icon: 'pi pi-check-square',
    route: '/approvals',
    permissions: ['workflow.task.view'],
    badge: 'approvals',
  },
  {
    // Employee self-service (spec §16).
    label: 'My Space',
    icon: 'pi pi-user',
    children: [
      { label: 'My profile', icon: 'pi pi-id-card', route: '/me/profile', permissions: ['self.profile.view'] },
      { label: 'Sign-in security', icon: 'pi pi-shield', route: '/me/security', permissions: ['self.profile.view'] },
      { label: 'My attendance', icon: 'pi pi-clock', route: '/me/attendance', permissions: ['self.attendance.view'], feature: 'Time' },
      { label: 'My leave', icon: 'pi pi-calendar', route: '/me/leave', permissions: ['self.leave.view'], feature: 'Time' },
      { label: 'My timesheet', icon: 'pi pi-stopwatch', route: '/me/timesheet', permissions: ['self.timesheet.submit'], feature: 'Timesheets' },
      { label: 'My skills', icon: 'pi pi-bolt', route: '/me/skills', permissions: ['self.skill.manage'] },
      { label: 'My payslips', icon: 'pi pi-wallet', route: '/me/payslips', permissions: ['self.payslip.view'], feature: 'Payroll' },
      { label: 'My tax declaration', icon: 'pi pi-file-edit', route: '/me/tax-declaration', permissions: ['self.tax-declaration.manage'], feature: 'Payroll' },
      { label: 'My compensation', icon: 'pi pi-chart-pie', route: '/me/compensation', permissions: ['self.compensation.view'], feature: 'Compensation' },
      { label: 'My benefits', icon: 'pi pi-heart', route: '/me/benefits', permissions: ['self.benefit.manage'], feature: 'Benefits' },
      { label: 'My goals', icon: 'pi pi-bullseye', route: '/me/goals', permissions: ['performance.goal.manage-own'], feature: 'Performance' },
      { label: 'My reviews', icon: 'pi pi-chart-line', route: '/me/reviews', permissions: ['performance.review.manage-own'], feature: 'Performance' },
      { label: 'My feedback', icon: 'pi pi-comment', route: '/me/feedback', permissions: ['self.feedback.give'], feature: 'Performance' },
      { label: 'My learning', icon: 'pi pi-book', route: '/me/learning', permissions: ['self.learning.view'], feature: 'Learning' },
      { label: 'My career', icon: 'pi pi-arrow-up-right', route: '/me/career', permissions: ['self.career.view'], feature: 'Career' },
      { label: 'My onboarding', icon: 'pi pi-sign-in', route: '/me/onboarding', permissions: ['self.onboarding.view'], feature: 'Onboarding' },
      { label: 'Jobs & referrals', icon: 'pi pi-briefcase', route: '/me/jobs', permissions: ['self.referral.create'], feature: 'Recruitment' },
      { label: 'My interviews', icon: 'pi pi-comments', route: '/me/interviews', permissions: ['self.interview.feedback'], feature: 'Recruitment' },
      { label: 'My expenses', icon: 'pi pi-receipt', route: '/me/expenses', permissions: ['self.expense-claim.create', 'self.travel-request.create'], feature: 'Expenses' },
      { label: 'My rewards', icon: 'pi pi-star', route: '/me/rewards', permissions: ['self.reward.view'], feature: 'Rewards' },
      { label: 'My surveys', icon: 'pi pi-heart', route: '/me/surveys', permissions: ['self.survey.respond'], feature: 'Engagement' },
      { label: 'My assets', icon: 'pi pi-desktop', route: '/me/assets', permissions: ['self.asset.view'], feature: 'Assets' },
      { label: 'My documents', icon: 'pi pi-file', route: '/me/documents', permissions: ['self.document.view'], feature: 'Documents' },
      { label: 'My policies', icon: 'pi pi-book', route: '/me/policies', permissions: ['self.policy.view'] },
      { label: 'My tickets', icon: 'pi pi-question-circle', route: '/me/tickets', permissions: ['self.ticket.create'], feature: 'Helpdesk' },
      { label: 'My resignation', icon: 'pi pi-sign-out', route: '/me/separation', permissions: ['self.resignation.submit'], feature: 'Separation' },
    ],
  },
  {
    // Manager self-service (spec §17).
    label: 'My Team',
    icon: 'pi pi-users',
    children: [
      { label: 'Team overview', icon: 'pi pi-chart-bar', route: '/team', permissions: ['employee.profile.view'] },
      { label: 'Team attendance', icon: 'pi pi-clock', route: '/team/attendance', permissions: ['attendance.record.view'], feature: 'Time' },
      { label: 'Team leave', icon: 'pi pi-calendar', route: '/team/leave', permissions: ['leave.request.view'], feature: 'Time' },
      { label: 'Team timesheets', icon: 'pi pi-stopwatch', route: '/team/timesheets', permissions: ['timesheet.entry.view'], feature: 'Timesheets' },
    ],
  },
  {
    label: 'Organization',
    icon: 'pi pi-sitemap',
    children: [
      { label: 'Structure', icon: 'pi pi-share-alt', route: '/org/structure', permissions: ['org.structure.view'] },
      { label: 'Companies', icon: 'pi pi-building', route: '/org/companies', permissions: ['org.structure.view'] },
      { label: 'Business units', icon: 'pi pi-briefcase', route: '/org/business-units', permissions: ['org.structure.view'] },
      { label: 'Divisions', icon: 'pi pi-clone', route: '/org/divisions', permissions: ['org.structure.view'] },
      { label: 'Departments', icon: 'pi pi-users', route: '/org/departments', permissions: ['org.structure.view'] },
      { label: 'Locations', icon: 'pi pi-map-marker', route: '/org/locations', permissions: ['org.structure.view'] },
      { label: 'Jobs', icon: 'pi pi-tags', route: '/org/jobs', permissions: ['org.structure.view'] },
      { label: 'Positions', icon: 'pi pi-th-large', route: '/org/positions', permissions: ['org.structure.view'] },
      { label: 'Grades', icon: 'pi pi-sort-amount-up', route: '/org/grades', permissions: ['org.structure.view'] },
      { label: 'Job levels', icon: 'pi pi-bars', route: '/org/job-levels', permissions: ['org.structure.view'] },
      { label: 'Cost centers', icon: 'pi pi-percentage', route: '/org/cost-centers', permissions: ['org.structure.view'] },
      { label: 'Profit centers', icon: 'pi pi-chart-line', route: '/org/profit-centers', permissions: ['org.structure.view'] },
    ],
  },
  {
    label: 'Workforce',
    icon: 'pi pi-chart-line',
    feature: 'Workforce',
    children: [
      { label: 'Workforce overview', icon: 'pi pi-chart-bar', route: '/workforce', permissions: ['workforce.plan.view'] },
      { label: 'Workforce plans', icon: 'pi pi-compass', route: '/workforce/plans', permissions: ['workforce.plan.view'] },
      { label: 'Manpower requests', icon: 'pi pi-user-plus', route: '/workforce/requests', permissions: ['workforce.request.view', 'workforce.request.manage'] },
    ],
  },
  {
    label: 'Recruitment',
    icon: 'pi pi-search',
    feature: 'Recruitment',
    children: [
      { label: 'Requisitions', icon: 'pi pi-file-edit', route: '/recruitment/requisitions', permissions: ['recruitment.requisition.view'] },
      { label: 'Job postings', icon: 'pi pi-megaphone', route: '/recruitment/postings', permissions: ['recruitment.posting.view'] },
      { label: 'Candidates', icon: 'pi pi-id-card', route: '/recruitment/candidates', permissions: ['recruitment.application.view'] },
      { label: 'Interviews', icon: 'pi pi-comments', route: '/recruitment/interviews', permissions: ['recruitment.application.view'] },
      { label: 'Offers', icon: 'pi pi-envelope', route: '/recruitment/offers', permissions: ['recruitment.offer.view'] },
      { label: 'Recruitment analytics', icon: 'pi pi-chart-line', route: '/recruitment/analytics', permissions: ['recruitment.analytics.view'] },
      { label: 'Recruitment settings', icon: 'pi pi-cog', route: '/recruitment/settings', permissions: ['recruitment.settings.manage'] },
      { label: 'Onboarding', icon: 'pi pi-sign-in', route: '/onboarding', permissions: ['onboarding.case.view'], feature: 'Onboarding' },
      { label: 'Onboarding checklists', icon: 'pi pi-list-check', route: '/onboarding/templates', permissions: ['onboarding.template.view'], feature: 'Onboarding' },
    ],
  },
  {
    label: 'Employees',
    icon: 'pi pi-users',
    children: [
      { label: 'Directory', icon: 'pi pi-list', route: '/employees', permissions: ['employee.profile.view'] },
      { label: 'Org chart', icon: 'pi pi-sitemap', route: '/employees/org-chart', permissions: ['employee.profile.view'] },
      { label: 'Import', icon: 'pi pi-upload', route: '/employees/import', permissions: ['employee.profile.create'] },
    ],
  },
  {
    label: 'Time',
    icon: 'pi pi-clock',
    feature: 'Time',
    children: [
      { label: 'Attendance', icon: 'pi pi-check-circle', route: '/attendance', permissions: ['attendance.record.view'] },
      { label: 'Regularization', icon: 'pi pi-pencil', route: '/attendance/regularizations', permissions: ['attendance.regularization.view'] },
      { label: 'Shifts & rosters', icon: 'pi pi-calendar-clock', route: '/attendance/shifts', permissions: ['attendance.shift.manage', 'attendance.roster.manage'] },
      { label: 'Leave requests', icon: 'pi pi-calendar-minus', route: '/leave/requests', permissions: ['leave.request.view'] },
      { label: 'Leave policies', icon: 'pi pi-book', route: '/leave/policies', permissions: ['leave.type.manage', 'leave.policy.manage'] },
      { label: 'Holidays', icon: 'pi pi-sun', route: '/leave/holidays', permissions: ['leave.holiday.manage'] },
    ],
  },
  {
    label: 'Clients',
    icon: 'pi pi-globe',
    feature: 'Projects',
    children: [
      { label: 'Clients', icon: 'pi pi-building', route: '/clients', permissions: ['client.client.view'] },
      { label: 'Contacts', icon: 'pi pi-address-book', route: '/clients/contacts', permissions: ['client.client.view'] },
      { label: 'Contracts', icon: 'pi pi-file-check', route: '/contracts', permissions: ['contract.contract.view'] },
    ],
  },
  {
    label: 'Projects',
    icon: 'pi pi-objects-column',
    feature: 'Projects',
    children: [
      { label: 'Projects', icon: 'pi pi-folder', route: '/projects', permissions: ['project.project.view'] },
      { label: 'Tasks', icon: 'pi pi-check-square', route: '/projects/tasks', permissions: ['project.task.view'] },
      { label: 'Board', icon: 'pi pi-th-large', route: '/projects/board', permissions: ['project.task.view'] },
      { label: 'Milestones', icon: 'pi pi-flag', route: '/projects/milestones', permissions: ['project.milestone.view'] },
      { label: 'Sprints', icon: 'pi pi-replay', route: '/projects/sprints', permissions: ['project.sprint.view'] },
      { label: 'Change requests', icon: 'pi pi-arrow-right-arrow-left', route: '/projects/change-requests', permissions: ['project.change-request.view'] },
      { label: 'RAID log', icon: 'pi pi-exclamation-triangle', route: '/projects/raid', permissions: ['project.raid.view'] },
      { label: 'Project settings', icon: 'pi pi-cog', route: '/projects/settings', permissions: ['project.settings.manage'] },
      { label: 'Financials', icon: 'pi pi-money-bill', route: '/projects/financials', permissions: ['project.financials.view'], feature: 'ProjectFinancials' },
    ],
  },
  {
    label: 'Resources',
    icon: 'pi pi-users',
    feature: 'Resources',
    children: [
      { label: 'Resource directory', icon: 'pi pi-address-book', route: '/resources', permissions: ['resource.profile.view'] },
      { label: 'Requests', icon: 'pi pi-inbox', route: '/resources/requests', permissions: ['resource.request.view'] },
      { label: 'Allocation calendar', icon: 'pi pi-calendar', route: '/resources/calendar', permissions: ['resource.allocation.view'] },
      { label: 'Capacity & demand', icon: 'pi pi-chart-bar', route: '/resources/capacity', permissions: ['resource.allocation.view'] },
      { label: 'Bench', icon: 'pi pi-pause-circle', route: '/resources/bench', permissions: ['resource.profile.view'] },
      { label: 'Utilization', icon: 'pi pi-gauge', route: '/resources/utilization', permissions: ['resource.utilization.view'] },
      { label: 'Timesheet approvals', icon: 'pi pi-stopwatch', route: '/timesheets/approvals', permissions: ['timesheet.entry.approve'], feature: 'Timesheets' },
      { label: 'Transfers', icon: 'pi pi-arrow-right-arrow-left', route: '/resources/transfers', permissions: ['resource.allocation.view'] },
      { label: 'Timesheet settings', icon: 'pi pi-cog', route: '/timesheets/settings', permissions: ['timesheet.settings.manage'], feature: 'Timesheets' },
    ],
  },
  {
    label: 'Payroll',
    icon: 'pi pi-wallet',
    feature: 'Payroll',
    children: [
      { label: 'Payroll overview', icon: 'pi pi-chart-bar', route: '/payroll', permissions: ['payroll.run.view'] },
      { label: 'Payroll runs', icon: 'pi pi-play', route: '/payroll/runs', permissions: ['payroll.run.view'] },
      { label: 'Salaries', icon: 'pi pi-wallet', route: '/payroll/salaries', permissions: ['payroll.employee-salary.view'] },
      { label: 'Inputs', icon: 'pi pi-plus-circle', route: '/payroll/inputs', permissions: ['payroll.input.manage'] },
      { label: 'Payslips', icon: 'pi pi-file', route: '/payroll/payslips', permissions: ['payroll.run.view'] },
      { label: 'Tax declarations', icon: 'pi pi-file-edit', route: '/payroll/declarations', permissions: ['payroll.tax-declaration.view'] },
      { label: 'Salary structures', icon: 'pi pi-sitemap', route: '/payroll/structures', permissions: ['payroll.structure.manage', 'payroll.component.manage'] },
      { label: 'Payroll settings', icon: 'pi pi-cog', route: '/payroll/settings', permissions: ['payroll.settings.manage'] },
    ],
  },
  {
    label: 'Compensation & benefits',
    railLabel: 'Comp & benefits',
    icon: 'pi pi-gift',
    children: [
      { label: 'Salary revisions', icon: 'pi pi-arrow-up-right', route: '/compensation/revisions', permissions: ['compensation.revision.view'], feature: 'Compensation' },
      { label: 'Salary bands', icon: 'pi pi-sliders-h', route: '/compensation/bands', permissions: ['compensation.band.view'], feature: 'Compensation' },
      { label: 'Variable pay', icon: 'pi pi-star', route: '/compensation/variable-pay', permissions: ['compensation.variable-pay.view'], feature: 'Compensation' },
      { label: 'Equity', icon: 'pi pi-chart-line', route: '/compensation/equity', permissions: ['compensation.equity.view'], feature: 'Compensation' },
      { label: 'Benefit plans', icon: 'pi pi-heart', route: '/benefits', permissions: ['benefit.plan.view'], feature: 'Benefits' },
      { label: 'Benefit enrollments', icon: 'pi pi-users', route: '/benefits/enrollments', permissions: ['benefit.enrollment.view'], feature: 'Benefits' },
    ],
  },
  {
    label: 'Compliance',
    icon: 'pi pi-shield',
    feature: 'Compliance',
    children: [
      { label: 'Compliance overview', icon: 'pi pi-chart-bar', route: '/compliance', permissions: ['compliance.rule.view', 'compliance.filing.view'] },
      { label: 'Statutory rules', icon: 'pi pi-book', route: '/compliance/rules', permissions: ['compliance.rule.view'] },
      { label: 'Filing calendar', icon: 'pi pi-calendar', route: '/compliance/filings', permissions: ['compliance.filing.view'] },
      { label: 'Licences and certificates', icon: 'pi pi-verified', route: '/compliance/registrations', permissions: ['compliance.filing.view'] },
    ],
  },
  {
    label: 'Talent',
    icon: 'pi pi-star',
    children: [
      { label: 'Performance', icon: 'pi pi-chart-line', route: '/performance', permissions: ['performance.cycle.view'], feature: 'Performance' },
      { label: 'Improvement plans', icon: 'pi pi-flag', route: '/performance/pips', permissions: ['performance.pip.view', 'performance.pip.manage'], feature: 'Performance' },
      { label: 'Goals', icon: 'pi pi-bullseye', route: '/goals', permissions: ['performance.goal.manage', 'performance.review.view'], feature: 'Performance' },
      { label: 'Learning', icon: 'pi pi-book', route: '/learning', permissions: ['learning.enrollment.view'], feature: 'Learning' },
      { label: 'Courses', icon: 'pi pi-list', route: '/learning/courses', permissions: ['learning.course.view'], feature: 'Learning' },
      { label: 'Skills', icon: 'pi pi-bolt', route: '/skills', permissions: ['skill.skill.view'] },
      { label: 'Career paths', icon: 'pi pi-arrow-up-right', route: '/career', permissions: ['career.path.view'], feature: 'Career' },
      { label: 'Promotions', icon: 'pi pi-angle-double-up', route: '/career/promotions', permissions: ['career.promotion.view', 'career.promotion.manage'], feature: 'Career' },
      { label: 'Succession', icon: 'pi pi-users', route: '/career/succession', permissions: ['career.succession.view', 'career.succession.manage'], feature: 'Career' },
      { label: 'Talent review', icon: 'pi pi-th-large', route: '/career/talent-review', permissions: ['career.talent-review.manage'], feature: 'Career' },
      { label: 'Rewards', icon: 'pi pi-gift', route: '/rewards', permissions: ['reward.recognition.view', 'reward.program.manage', 'reward.catalog.manage', 'reward.redemption.manage'], feature: 'Rewards' },
      { label: 'Engagement', icon: 'pi pi-heart', route: '/engagement', permissions: ['engagement.survey.view'], feature: 'Engagement' },
    ],
  },
  {
    label: 'Services',
    icon: 'pi pi-briefcase',
    children: [
      { label: 'Expenses', icon: 'pi pi-receipt', route: '/expenses', permissions: ['spend.claim.view', 'spend.travel.view'], feature: 'Expenses' },
      { label: 'Assets', icon: 'pi pi-desktop', route: '/assets', permissions: ['asset.asset.view'], feature: 'Assets' },
      { label: 'Helpdesk', icon: 'pi pi-question-circle', route: '/helpdesk', permissions: ['helpdesk.ticket.view'], feature: 'Helpdesk' },
      { label: 'Knowledge base', icon: 'pi pi-book', route: '/helpdesk/knowledge', permissions: ['helpdesk.knowledge.manage'], feature: 'Helpdesk' },
      { label: 'Documents', icon: 'pi pi-folder-open', route: '/documents', permissions: ['document.file.view'], feature: 'Documents' },
      { label: 'Policies', icon: 'pi pi-book', route: '/policies', permissions: ['policy.policy.view'] },
      { label: 'Separation', icon: 'pi pi-sign-out', route: '/separation', permissions: ['separation.case.view'], feature: 'Separation' },
      { label: 'Exit clearance', icon: 'pi pi-check-square', route: '/separation/clearance', permissions: ['separation.clearance.manage'], feature: 'Separation' },
      { label: 'Offboarding tasks', icon: 'pi pi-list-check', route: '/separation/offboarding', permissions: ['separation.offboarding.manage', 'separation.clearance.manage', 'separation.case.view'], feature: 'Separation' },
      { label: 'Offboarding checklists', icon: 'pi pi-clipboard', route: '/separation/offboarding-checklists', permissions: ['separation.offboarding.manage'], feature: 'Separation' },
      { label: 'Disciplinary', icon: 'pi pi-file-edit', route: '/separation/disciplinary', permissions: ['separation.termination.manage'], feature: 'Separation' },
      { label: 'Absconding', icon: 'pi pi-user-minus', route: '/separation/absconding', permissions: ['separation.termination.manage'], feature: 'Separation' },
      { label: 'Retrenchment', icon: 'pi pi-users', route: '/separation/retrenchment', permissions: ['separation.termination.manage'], feature: 'Separation' },
      { label: 'Separation policy', icon: 'pi pi-sliders-h', route: '/separation/policy', permissions: ['separation.policy.manage'], feature: 'Separation' },
    ],
  },
  {
    label: 'Analytics',
    icon: 'pi pi-chart-pie',
    feature: 'Analytics',
    route: '/analytics',
    permissions: ['report.report.view', 'report.executive.view'],
  },
  {
    label: 'Calendar',
    icon: 'pi pi-calendar',
    route: '/calendar',
    permissions: ['self.profile.view', 'employee.profile.view'],
  },
  {
    label: 'AI Assistant',
    icon: 'pi pi-sparkles',
    feature: 'AI',
    route: '/ai',
    permissions: ['ai.assistant.use'],
  },
  {
    label: 'Administration',
    icon: 'pi pi-cog',
    children: [
      { label: 'Users', icon: 'pi pi-user', route: '/admin/users', permissions: ['admin.user.view'] },
      { label: 'Roles', icon: 'pi pi-lock', route: '/admin/roles', permissions: ['admin.role.view'] },
      { label: 'Audit log', icon: 'pi pi-history', route: '/admin/audit', permissions: ['audit.log.view'] },
      { label: 'Settings', icon: 'pi pi-sliders-h', route: '/admin/settings', permissions: ['admin.setting.view'] },
      { label: 'Workflows', icon: 'pi pi-share-alt', route: '/admin/workflows', permissions: ['workflow.definition.view'] },
      { label: 'Notifications', icon: 'pi pi-bell', route: '/admin/notifications', permissions: ['notification.template.view'] },
      { label: 'Master data', icon: 'pi pi-database', route: '/admin/master-data', permissions: ['master-data.lookup.manage'] },
      { label: 'Imports', icon: 'pi pi-upload', route: '/admin/data/imports', permissions: ['datahub.import.run'] },
      { label: 'Integrations', icon: 'pi pi-link', route: '/admin/data/integrations', permissions: ['datahub.integration.view'] },
      { label: 'Single sign-on', icon: 'pi pi-key', route: '/admin/identity-providers', permissions: ['admin.identity-provider.manage'] },
    ],
  },
];
