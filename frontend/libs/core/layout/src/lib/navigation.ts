/**
 * The navigation manifest.
 *
 * The sidebar is generated from this rather than hand-written, so a module appears for exactly the
 * people who can use it. Adding a screen means adding an entry with its permission - there is no
 * second place to remember, and no hand-maintained menu that drifts from the routes.
 */
export interface NavItem {
  readonly label: string;
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

/**
 * Whether an entry's module has been built yet.
 *
 * The manifest describes the whole of PeopleOS (spec §117) so the sidebar's shape is decided once,
 * but modules ship milestone by milestone (docs/roadmap.md). A planned entry has no route behind it
 * yet, so it never reaches the sidebar whatever the user's permissions or the tenant's plan.
 * Shipping a module means adding its route and deleting its `planned` flag - nothing else.
 */
export const isBuilt = (item: NavItem): boolean => item.planned !== true;

/** Shorthand for a module that is on the roadmap but not built. */
const planned = (milestone: string) => ({ planned: true, milestone }) as const;

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
    ...planned('M3'),
  },
  {
    // Employee self-service (spec §16).
    label: 'My Space',
    icon: 'pi pi-user',
    children: [
      { label: 'My profile', icon: 'pi pi-id-card', route: '/me/profile', permissions: ['self.profile.view'] },
      { label: 'My attendance', icon: 'pi pi-clock', route: '/me/attendance', permissions: ['self.attendance.view'], feature: 'Time', ...planned('M4') },
      { label: 'My leave', icon: 'pi pi-calendar', route: '/me/leave', permissions: ['self.leave.view'], feature: 'Time', ...planned('M4') },
      { label: 'My timesheet', icon: 'pi pi-stopwatch', route: '/me/timesheet', permissions: ['self.timesheet.submit'], feature: 'Timesheets', ...planned('M6') },
      { label: 'My payslips', icon: 'pi pi-wallet', route: '/me/payslips', permissions: ['self.payslip.view'], feature: 'Payroll', ...planned('M7') },
      { label: 'My expenses', icon: 'pi pi-receipt', route: '/me/expenses', permissions: ['self.expense-claim.create'], feature: 'Expenses', ...planned('M10') },
      { label: 'My rewards', icon: 'pi pi-star', route: '/me/rewards', permissions: ['self.reward.view'], feature: 'Rewards', ...planned('M10') },
      { label: 'My assets', icon: 'pi pi-desktop', route: '/me/assets', permissions: ['self.asset.view'], feature: 'Assets', ...planned('M10') },
      { label: 'My documents', icon: 'pi pi-file', route: '/me/documents', permissions: ['self.document.view'], feature: 'Documents', ...planned('M3') },
      { label: 'My tickets', icon: 'pi pi-question-circle', route: '/me/tickets', permissions: ['self.ticket.create'], feature: 'Helpdesk', ...planned('M10') },
    ],
  },
  {
    // Manager self-service (spec §17).
    label: 'My Team',
    icon: 'pi pi-users',
    children: [
      { label: 'Team overview', icon: 'pi pi-chart-bar', route: '/team', permissions: ['employee.profile.view'] },
      { label: 'Team attendance', icon: 'pi pi-clock', route: '/team/attendance', permissions: ['attendance.record.view'], feature: 'Time', ...planned('M4') },
      { label: 'Team leave', icon: 'pi pi-calendar', route: '/team/leave', permissions: ['leave.request.view'], feature: 'Time', ...planned('M4') },
      { label: 'Team timesheets', icon: 'pi pi-stopwatch', route: '/team/timesheets', permissions: ['timesheet.entry.approve'], feature: 'Timesheets', ...planned('M6') },
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
      { label: 'Workforce planning', icon: 'pi pi-compass', route: '/workforce/plans', permissions: ['workforce.plan.view'], ...planned('M8') },
      { label: 'Manpower requests', icon: 'pi pi-user-plus', route: '/workforce/requests', permissions: ['workforce.request.view'], ...planned('M8') },
    ],
  },
  {
    label: 'Recruitment',
    icon: 'pi pi-search',
    feature: 'Recruitment',
    children: [
      { label: 'Requisitions', icon: 'pi pi-file-edit', route: '/recruitment/requisitions', permissions: ['recruitment.requisition.view'], ...planned('M8') },
      { label: 'Job postings', icon: 'pi pi-megaphone', route: '/recruitment/postings', permissions: ['recruitment.posting.view'], ...planned('M8') },
      { label: 'Candidates', icon: 'pi pi-id-card', route: '/recruitment/candidates', permissions: ['recruitment.application.view'], ...planned('M8') },
      { label: 'Interviews', icon: 'pi pi-comments', route: '/recruitment/interviews', permissions: ['recruitment.application.view'], ...planned('M8') },
      { label: 'Offers', icon: 'pi pi-envelope', route: '/recruitment/offers', permissions: ['recruitment.offer.view'], ...planned('M8') },
      { label: 'Onboarding', icon: 'pi pi-sign-in', route: '/onboarding', permissions: ['onboarding.case.view'], feature: 'Onboarding', ...planned('M8') },
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
      { label: 'Attendance', icon: 'pi pi-check-circle', route: '/attendance', permissions: ['attendance.record.view'], ...planned('M4') },
      { label: 'Regularization', icon: 'pi pi-pencil', route: '/attendance/regularizations', permissions: ['attendance.regularization.view'], ...planned('M4') },
      { label: 'Shifts & rosters', icon: 'pi pi-calendar-clock', route: '/attendance/shifts', permissions: ['attendance.shift.manage'], ...planned('M4') },
      { label: 'Leave requests', icon: 'pi pi-calendar-minus', route: '/leave/requests', permissions: ['leave.request.view'], ...planned('M4') },
      { label: 'Leave policies', icon: 'pi pi-book', route: '/leave/policies', permissions: ['leave.policy.manage'], ...planned('M4') },
      { label: 'Holidays', icon: 'pi pi-sun', route: '/leave/holidays', permissions: ['leave.holiday.manage'], ...planned('M4') },
    ],
  },
  {
    label: 'Clients',
    icon: 'pi pi-globe',
    feature: 'Projects',
    children: [
      { label: 'Clients', icon: 'pi pi-building', route: '/clients', permissions: ['client.client.view'], ...planned('M5') },
      { label: 'Contracts', icon: 'pi pi-file-check', route: '/contracts', permissions: ['contract.contract.view'], ...planned('M5') },
    ],
  },
  {
    label: 'Projects',
    icon: 'pi pi-objects-column',
    feature: 'Projects',
    children: [
      { label: 'Projects', icon: 'pi pi-folder', route: '/projects', permissions: ['project.project.view'], ...planned('M5') },
      { label: 'Board', icon: 'pi pi-th-large', route: '/projects/board', permissions: ['project.task.view'], ...planned('M5') },
      { label: 'Milestones', icon: 'pi pi-flag', route: '/projects/milestones', permissions: ['project.milestone.view'], ...planned('M5') },
      { label: 'Sprints', icon: 'pi pi-replay', route: '/projects/sprints', permissions: ['project.sprint.view'], ...planned('M5') },
      { label: 'Change requests', icon: 'pi pi-arrow-right-arrow-left', route: '/projects/change-requests', permissions: ['project.change-request.view'], ...planned('M5') },
      { label: 'RAID log', icon: 'pi pi-exclamation-triangle', route: '/projects/raid', permissions: ['project.raid.view'], ...planned('M5') },
      { label: 'Financials', icon: 'pi pi-money-bill', route: '/projects/financials', permissions: ['project.financials.view'], feature: 'ProjectFinancials', ...planned('M7') },
    ],
  },
  {
    label: 'Resources',
    icon: 'pi pi-users',
    feature: 'Resources',
    children: [
      { label: 'Resource directory', icon: 'pi pi-address-book', route: '/resources', permissions: ['resource.profile.view'], ...planned('M6') },
      { label: 'Requests', icon: 'pi pi-inbox', route: '/resources/requests', permissions: ['resource.request.view'], ...planned('M6') },
      { label: 'Allocation calendar', icon: 'pi pi-calendar', route: '/resources/calendar', permissions: ['resource.allocation.view'], ...planned('M6') },
      { label: 'Capacity & demand', icon: 'pi pi-chart-bar', route: '/resources/capacity', permissions: ['resource.allocation.view'], ...planned('M6') },
      { label: 'Bench', icon: 'pi pi-pause-circle', route: '/resources/bench', permissions: ['resource.profile.view'], ...planned('M6') },
      { label: 'Utilization', icon: 'pi pi-gauge', route: '/resources/utilization', permissions: ['resource.utilization.view'], ...planned('M6') },
      { label: 'Timesheet approvals', icon: 'pi pi-stopwatch', route: '/timesheets/approvals', permissions: ['timesheet.entry.approve'], feature: 'Timesheets', ...planned('M6') },
    ],
  },
  {
    label: 'Payroll',
    icon: 'pi pi-wallet',
    feature: 'Payroll',
    children: [
      { label: 'Salary structures', icon: 'pi pi-sitemap', route: '/payroll/structures', permissions: ['payroll.structure.manage'], ...planned('M7') },
      { label: 'Payroll runs', icon: 'pi pi-play', route: '/payroll/runs', permissions: ['payroll.run.view'], ...planned('M7') },
      { label: 'Payslips', icon: 'pi pi-file', route: '/payroll/payslips', permissions: ['payroll.run.view'], ...planned('M7') },
      { label: 'Statutory', icon: 'pi pi-shield', route: '/payroll/statutory', permissions: ['payroll.statutory.manage'], feature: 'Compliance', ...planned('M7') },
    ],
  },
  {
    label: 'Talent',
    icon: 'pi pi-star',
    children: [
      { label: 'Performance', icon: 'pi pi-chart-line', route: '/performance', permissions: ['performance.cycle.view'], feature: 'Performance', ...planned('M9') },
      { label: 'Goals', icon: 'pi pi-bullseye', route: '/goals', permissions: ['performance.goal.manage'], feature: 'Performance', ...planned('M9') },
      { label: 'Learning', icon: 'pi pi-book', route: '/learning', permissions: ['learning.course.view'], feature: 'Learning', ...planned('M9') },
      { label: 'Skills', icon: 'pi pi-bolt', route: '/skills', permissions: ['skill.skill.view'], ...planned('M6') },
      { label: 'Career & succession', icon: 'pi pi-arrow-up-right', route: '/career', permissions: ['career.path.view'], feature: 'Career', ...planned('M9') },
      { label: 'Rewards', icon: 'pi pi-gift', route: '/rewards', permissions: ['reward.catalog.view'], feature: 'Rewards', ...planned('M10') },
      { label: 'Engagement', icon: 'pi pi-heart', route: '/engagement', permissions: ['engagement.survey.view'], feature: 'Engagement', ...planned('M10') },
    ],
  },
  {
    label: 'Services',
    icon: 'pi pi-briefcase',
    children: [
      { label: 'Expenses', icon: 'pi pi-receipt', route: '/expenses', permissions: ['spend.claim.view'], feature: 'Expenses', ...planned('M10') },
      { label: 'Assets', icon: 'pi pi-desktop', route: '/assets', permissions: ['asset.asset.view'], feature: 'Assets', ...planned('M10') },
      { label: 'Helpdesk', icon: 'pi pi-question-circle', route: '/helpdesk', permissions: ['helpdesk.ticket.view'], feature: 'Helpdesk', ...planned('M10') },
      { label: 'Documents', icon: 'pi pi-folder-open', route: '/documents', permissions: ['document.file.view'], feature: 'Documents', ...planned('M3') },
      { label: 'Policies', icon: 'pi pi-book', route: '/policies', permissions: ['policy.policy.view'], ...planned('M3') },
      { label: 'Compliance', icon: 'pi pi-shield', route: '/compliance', permissions: ['compliance.rule.view'], feature: 'Compliance', ...planned('M7') },
      { label: 'Separation', icon: 'pi pi-sign-out', route: '/separation', permissions: ['employee.exit.manage'], feature: 'Separation', ...planned('M10') },
    ],
  },
  {
    label: 'Analytics',
    icon: 'pi pi-chart-pie',
    feature: 'Analytics',
    route: '/analytics',
    permissions: ['report.report.view'],
    ...planned('M11'),
  },
  {
    label: 'AI Assistant',
    icon: 'pi pi-sparkles',
    feature: 'AI',
    route: '/ai',
    permissions: ['ai.assistant.use'],
    ...planned('M12'),
  },
  {
    label: 'Administration',
    icon: 'pi pi-cog',
    children: [
      { label: 'Users', icon: 'pi pi-user', route: '/admin/users', permissions: ['admin.user.view'] },
      { label: 'Roles', icon: 'pi pi-lock', route: '/admin/roles', permissions: ['admin.role.view'] },
      { label: 'Audit log', icon: 'pi pi-history', route: '/admin/audit', permissions: ['audit.log.view'] },
      { label: 'Settings', icon: 'pi pi-sliders-h', route: '/admin/settings', permissions: ['admin.setting.view'] },
      { label: 'Workflows', icon: 'pi pi-share-alt', route: '/admin/workflows', permissions: ['workflow.definition.view'], ...planned('M3') },
      { label: 'Notifications', icon: 'pi pi-bell', route: '/admin/notifications', permissions: ['notification.template.view'], ...planned('M3') },
      { label: 'Master data', icon: 'pi pi-database', route: '/admin/master-data', permissions: ['master-data.lookup.manage'] },
    ],
  },
];
