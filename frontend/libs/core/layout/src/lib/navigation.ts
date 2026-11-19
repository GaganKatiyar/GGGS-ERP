/**
 * The navigation manifest.
 *
 * The sidebar is generated from this rather than hand-written, so a module appears for exactly the
 * people who can use it. Adding a screen means adding an entry with its permission - there is no
 * second place to remember, and no hand-maintained menu that drifts from the routes.
 */
export interface NavItem {
  readonly label: string;
  /** A shorter label for the app launcher's tile, when the full one does not fit two short lines. */
  readonly railLabel?: string;
  readonly icon: string;
  readonly route?: string;
  /** Any one of these permissions reveals the item. */
  readonly permissions?: readonly string[];
  /** Feature the tenant's plan must include. */
  readonly feature?: string;
  /** A second plan feature it also needs, for a page that depends on another module (Billing on Projects). */
  readonly alsoNeedsFeature?: string;
  /**
   * The page is about the user's own employee record, so an account not linked to one (an
   * auditor, an outside recruiter) has nothing to see there even with the permission.
   */
  readonly needsEmployee?: boolean;
  readonly children?: readonly NavItem[];
  /** Named counter shown as a badge, e.g. pending approvals. */
  readonly badge?: 'approvals' | 'notifications';
  /** On the roadmap but not built: never shown. See {@link isBuilt}. */
  readonly planned?: boolean;
  /** The roadmap milestone a planned entry ships in (docs/roadmap.md). */
  readonly milestone?: string;
  /** A Settings entry's category (see {@link SETTINGS_SECTIONS}); its tabs are the category's pages. */
  readonly section?: SettingsSectionKey;
  /** On a module: the Settings category its set-up pages live in, linked from the end of its tabs. */
  readonly settingsSection?: SettingsSectionKey;
  /** On a page of a department menu: the heading it sits under; its tabs are that heading's pages. */
  readonly heading?: string;
}

export type SettingsSectionKey = 'organization' | 'time' | 'payroll' | 'hiring' | 'exits' | 'projects' | 'lookups' | 'access' | 'platform';

/** A category on the Settings home: a card there, and one row of tabs inside it. */
export interface SettingsSection {
  readonly key: SettingsSectionKey;
  readonly label: string;
  readonly icon: string;
  readonly description: string;
}

/**
 * Set-up lives in one place: every master and settings page is a child of the Settings group,
 * grouped by these categories, rather than a tab at the end of each module.
 */
export const SETTINGS_SECTIONS: readonly SettingsSection[] = [
  { key: 'organization', label: 'Organization', icon: 'pi pi-sitemap', description: 'Companies, cost centers, departments, locations, roles, designations and levels.' },
  { key: 'time', label: 'Time & attendance', icon: 'pi pi-clock', description: 'Shifts, leave policies, holidays and timesheets.' },
  { key: 'payroll', label: 'Payroll & finance', icon: 'pi pi-wallet', description: 'Pay settings, salary structures and bands, loans and statutory rules.' },
  { key: 'hiring', label: 'Hiring', icon: 'pi pi-user-plus', description: 'How the recruitment pipeline works: stages, rules and agencies.' },
  { key: 'exits', label: 'Joiners & leavers', icon: 'pi pi-sign-out', description: 'Onboarding checklists for joiners; separation policy, offboarding checklists and the exit survey for leavers.' },
  { key: 'projects', label: 'Projects', icon: 'pi pi-objects-column', description: 'Project types, stages and delivery defaults.' },
  { key: 'lookups', label: 'Lookups', icon: 'pi pi-database', description: 'The values behind every dropdown.' },
  { key: 'access', label: 'Access & security', icon: 'pi pi-lock', description: 'Users, roles, who can see what, and the audit log. Single sign-on is in Studio.' },
  { key: 'platform', label: 'Platform', icon: 'pi pi-sliders-h', description: 'General settings, the dashboard banner, imports and what the system runs on.' },
];

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

/**
 * The pages a module offers as tabs above the content: every module with more than one page. In
 * Settings, where forty pages share one group, the tabs are the current page's category - and the
 * Settings home itself has none. In a department menu they are the current page's heading.
 */
export function moduleTabs(group: NavItem | null | undefined, entry?: NavItem | null): readonly NavItem[] {
  const children = group?.children ?? [];
  if (children.some((child) => child.section)) {
    const section = entry?.section;
    const inSection = section ? children.filter((child) => child.section === section) : [];
    return inSection.length >= 2 ? inSection : [];
  }
  if (children.some((child) => child.heading)) {
    const inHeading = entry?.heading ? children.filter((child) => child.heading === entry.heading) : [];
    return inHeading.length >= 2 ? inHeading : [];
  }
  return children.length >= 2 ? children : [];
}

/** A run of a module's pages under one caption; the caption is absent for an ordinary module. */
export interface NavSection {
  readonly key: string;
  readonly label?: string;
  readonly pages: readonly NavItem[];
}

/**
 * A module's pages as the expanded menu and the rail's pop-out list them: one run for an ordinary
 * module, for Settings one run per category, in {@link SETTINGS_SECTIONS} order, under its name, and
 * for a department menu one run per heading, in the order they are written.
 */
export function sectionsOf(group: NavItem): readonly NavSection[] {
  const pages = group.children ?? [];
  if (pages.some((page) => page.heading)) {
    return [...new Set(pages.map((page) => page.heading))].map((heading) => ({ key: heading ?? group.label, label: heading, pages: pages.filter((page) => page.heading === heading) }));
  }

  if (!pages.some((page) => page.section)) {
    return pages.length ? [{ key: group.label, pages }] : [];
  }

  return SETTINGS_SECTIONS.map((section) => ({ key: section.key, label: section.label, pages: pages.filter((page) => page.section === section.key) })).filter(
    (section) => section.pages.length > 0,
  );
}

/**
 * The expanded menu's search: a module whose name matches keeps all its pages, otherwise only the
 * pages whose names or headings match - and a module left with nothing is dropped. An empty search
 * is the menu.
 */
export function filterMenu(items: readonly NavItem[], query: string): readonly NavItem[] {
  const term = query.trim().toLowerCase();
  if (!term) {
    return items;
  }

  const matches = matcher(term);
  return items.flatMap((item) => {
    if (matches(item)) {
      return [item];
    }
    const pages = (item.children ?? []).filter(matches);
    return pages.length ? [{ ...item, children: pages }] : [];
  });
}

/** Whether an entry answers a search, by its name, its short name or the heading it sits under. */
function matcher(term: string): (item: NavItem) => boolean {
  return (item) => [item.label, item.railLabel, item.heading].some((text) => text?.toLowerCase().includes(term));
}

/** Where a rail icon or an app tile leads: the entry's own page, else the first page of its module. */
export function entryRoute(item: NavItem): string | undefined {
  return item.route ?? item.children?.find((child) => child.route !== undefined)?.route;
}

/** A page found by the app launcher's search, with the module it belongs to. */
export interface NavPageMatch {
  readonly page: NavItem;
  readonly group: NavItem;
}

/** What the app launcher shows for a search. */
export interface NavSearchResult {
  readonly apps: readonly NavItem[];
  readonly pages: readonly NavPageMatch[];
}

/**
 * The app launcher's search over the (already filtered) navigation: modules whose name matches,
 * and - from two characters, so one letter does not list half the product - the pages that match,
 * each with its module, up to `pageLimit`. An empty query is every module and no pages.
 */
export function searchNavigation(items: readonly NavItem[], query: string, pageLimit = 12): NavSearchResult {
  const term = query.trim().toLowerCase();
  if (!term) {
    return { apps: items, pages: [] };
  }

  const matches = matcher(term);
  const pages =
    term.length < 2
      ? []
      : items.flatMap((group) => (group.children ?? []).filter(matches).map((page) => ({ page, group }))).slice(0, pageLimit);

  return { apps: items.filter(matches), pages };
}

/**
 * Whether an entry's module has been built yet.
 *
 * The manifest describes the whole of Nuts and Bolts (spec §117) so the sidebar's shape is decided once,
 * but modules ship milestone by milestone (docs/roadmap.md). A planned entry has no route behind it
 * yet, so it never reaches the sidebar whatever the user's permissions or the tenant's plan.
 * Shipping a module means adding its route and deleting its `planned` flag - nothing else.
 */
export const isBuilt = (item: NavItem): boolean => item.planned !== true;

/**
 * Organization masters appear in Settings for the people who can change them. Anyone who can view
 * the structure still reaches these pages (their route needs only the view permission) and browses
 * it from HR Portal > Org structure.
 */
const ORG_MANAGE = ['org.structure.manage', 'org.legal-entity.manage', 'org.department.manage'];

/** Pages under one heading of a department menu, with what they share; a page's own value wins. */
function under(shared: Pick<NavItem, 'heading' | 'feature' | 'alsoNeedsFeature' | 'settingsSection'>, pages: readonly NavItem[]): NavItem[] {
  return pages.map((page) => ({ ...shared, ...page }));
}

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
  // Employee self-service (spec §16), split into five areas so no tab bar runs to two dozen pages.
  {
    label: 'My Profile & Documents',
    railLabel: 'My profile',
    icon: 'pi pi-id-card',
    children: [
      { label: 'My profile', icon: 'pi pi-id-card', route: '/me/profile', permissions: ['self.profile.view'], needsEmployee: true },
      { label: 'My documents', icon: 'pi pi-file', route: '/me/documents', permissions: ['self.document.view'], feature: 'Documents', needsEmployee: true },
      { label: 'My assets', icon: 'pi pi-desktop', route: '/me/assets', permissions: ['self.asset.view'], feature: 'Assets', needsEmployee: true },
      { label: 'My policies', icon: 'pi pi-book', route: '/me/policies', permissions: ['self.policy.view'], needsEmployee: true },
      { label: 'My preferences', icon: 'pi pi-sliders-v', route: '/me/preferences', permissions: ['notification.inbox.view'], needsEmployee: true },
    ],
  },
  {
    label: 'My Time & Leave',
    icon: 'pi pi-clock',
    children: [
      { label: 'My attendance', icon: 'pi pi-clock', route: '/me/attendance', permissions: ['self.attendance.view'], feature: 'Time', needsEmployee: true },
      { label: 'My leave', icon: 'pi pi-calendar', route: '/me/leave', permissions: ['self.leave.view'], feature: 'Time', needsEmployee: true },
      { label: 'My timesheet', icon: 'pi pi-stopwatch', route: '/me/timesheet', permissions: ['self.timesheet.submit'], feature: 'Timesheets', needsEmployee: true },
    ],
  },
  {
    label: 'My Pay & Benefits',
    icon: 'pi pi-wallet',
    children: [
      { label: 'My payslips', icon: 'pi pi-wallet', route: '/me/payslips', permissions: ['self.payslip.view'], feature: 'Payroll', needsEmployee: true },
      { label: 'My tax declaration', icon: 'pi pi-file-edit', route: '/me/tax-declaration', permissions: ['self.tax-declaration.manage'], feature: 'Payroll', needsEmployee: true },
      { label: 'My compensation', icon: 'pi pi-chart-pie', route: '/me/compensation', permissions: ['self.compensation.view'], feature: 'Compensation', needsEmployee: true },
      { label: 'My benefits', icon: 'pi pi-heart', route: '/me/benefits', permissions: ['self.benefit.manage'], feature: 'Benefits', needsEmployee: true },
      { label: 'My expenses', icon: 'pi pi-receipt', route: '/me/expenses', permissions: ['self.expense-claim.create', 'self.travel-request.create'], feature: 'Expenses', needsEmployee: true },
      { label: 'My loans', icon: 'pi pi-money-bill', route: '/me/loans', permissions: ['self.loan.apply'], needsEmployee: true },
    ],
  },
  {
    // The employee's side of the appraisal: their goals, their reviews (and those they write as a
    // manager), and the 360 feedback colleagues ask them for.
    label: 'My Appraisal',
    icon: 'pi pi-star',
    children: [
      { label: 'My goals', icon: 'pi pi-bullseye', route: '/me/goals', permissions: ['performance.goal.manage-own'], feature: 'Performance', needsEmployee: true },
      { label: 'My reviews', icon: 'pi pi-chart-line', route: '/me/reviews', permissions: ['performance.review.manage-own'], feature: 'Performance', needsEmployee: true },
      { label: 'My feedback', icon: 'pi pi-comment', route: '/me/feedback', permissions: ['self.feedback.give'], feature: 'Performance', needsEmployee: true },
    ],
  },
  {
    label: 'My Growth',
    icon: 'pi pi-chart-line',
    children: [
      { label: 'My skills', icon: 'pi pi-bolt', route: '/me/skills', permissions: ['self.skill.manage'], needsEmployee: true },
      { label: 'My career', icon: 'pi pi-arrow-up-right', route: '/me/career', permissions: ['self.career.view'], feature: 'Career', needsEmployee: true },
    ],
  },
  {
    // Recognition in one place: an employee sees their own points and redemptions, HR also runs the
    // programs, the catalogue and the redemptions. Each sees only the pages they may open.
    label: 'Star Reward',
    icon: 'pi pi-gift',
    feature: 'Rewards',
    children: [
      { label: 'My rewards', icon: 'pi pi-star', route: '/me/rewards', permissions: ['self.reward.view'], needsEmployee: true },
      { label: 'Manage rewards', icon: 'pi pi-gift', route: '/rewards', permissions: ['reward.recognition.view', 'reward.program.manage', 'reward.catalog.manage', 'reward.redemption.manage'] },
    ],
  },
  {
    label: 'My Workplace',
    icon: 'pi pi-briefcase',
    children: [
      { label: 'My onboarding', icon: 'pi pi-sign-in', route: '/me/onboarding', permissions: ['self.onboarding.view'], feature: 'Onboarding', needsEmployee: true },
      { label: 'Jobs & referrals', icon: 'pi pi-briefcase', route: '/me/jobs', permissions: ['self.referral.create'], feature: 'Recruitment', needsEmployee: true },
      { label: 'My interviews', icon: 'pi pi-comments', route: '/me/interviews', permissions: ['self.interview.feedback'], feature: 'Recruitment', needsEmployee: true },
      { label: 'My surveys', icon: 'pi pi-heart', route: '/me/surveys', permissions: ['self.survey.respond'], feature: 'Engagement', needsEmployee: true },
      { label: 'My tickets', icon: 'pi pi-question-circle', route: '/me/tickets', permissions: ['self.ticket.create'], feature: 'Helpdesk', needsEmployee: true },
      { label: 'My resignation', icon: 'pi pi-sign-out', route: '/me/separation', permissions: ['self.resignation.submit'], feature: 'Separation', needsEmployee: true },
    ],
  },
  {
    // Manager self-service (spec §17).
    label: 'My Team',
    icon: 'pi pi-users',
    children: [
      { label: 'Team overview', icon: 'pi pi-chart-bar', route: '/team', permissions: ['employee.profile.view'], needsEmployee: true },
      { label: 'Team attendance', icon: 'pi pi-clock', route: '/team/attendance', permissions: ['attendance.record.view'], feature: 'Time' },
      { label: 'Team leave', icon: 'pi pi-calendar', route: '/team/leave', permissions: ['leave.request.view'], feature: 'Time' },
      { label: 'Team timesheets', icon: 'pi pi-stopwatch', route: '/team/timesheets', permissions: ['timesheet.entry.view'], feature: 'Timesheets' },
    ],
  },
  {
    // One flat list without headings: the menu lists every page, and so do the tabs above the content.
    label: 'HR Portal',
    icon: 'pi pi-users',
    children: [
      ...under({ settingsSection: 'organization' }, [
        { label: 'Directory', icon: 'pi pi-list', route: '/employees', permissions: ['employee.profile.view'] },
        { label: 'Org chart', icon: 'pi pi-sitemap', route: '/employees/org-chart', permissions: ['employee.profile.view'] },
        // The structure to browse; changing it is under Settings > Organization.
        { label: 'Org structure', icon: 'pi pi-share-alt', route: '/org/structure', permissions: ['org.structure.view'] },
      ]),
      // Onboarding is HR's: it starts once someone is an employee, whether they came through recruitment or not.
      { label: 'Onboarding', icon: 'pi pi-sign-in', route: '/onboarding', permissions: ['onboarding.case.view'], feature: 'Onboarding', settingsSection: 'exits' },
      ...under({ feature: 'Time', settingsSection: 'time' }, [
        { label: 'Attendance', icon: 'pi pi-check-circle', route: '/attendance', permissions: ['attendance.record.view'] },
        { label: 'Leave requests', icon: 'pi pi-calendar-minus', route: '/leave/requests', permissions: ['leave.request.view'] },
      ]),
      { label: 'Skills', icon: 'pi pi-bolt', route: '/skills', permissions: ['skill.skill.view'] },
      { label: 'Career paths', icon: 'pi pi-arrow-up-right', route: '/career', permissions: ['career.path.view'], feature: 'Career' },
      { label: 'Engagement', icon: 'pi pi-heart', route: '/engagement', permissions: ['engagement.survey.view'], feature: 'Engagement' },
      { label: 'Work anniversaries', icon: 'pi pi-calendar', route: '/engagement/anniversaries', permissions: ['engagement.survey.manage'], feature: 'Engagement' },
      { label: 'Birthday wishes', icon: 'pi pi-gift', route: '/engagement/birthdays', permissions: ['engagement.survey.manage'], feature: 'Engagement' },
      { label: 'Policies', icon: 'pi pi-book', route: '/policies', permissions: ['policy.policy.view'] },
    ],
  },
  {
    // HR's side of the appraisal: running review cycles and calibration, the goals people are
    // scored against, and improvement plans for those who fall short.
    label: 'Appraisal Admin',
    icon: 'pi pi-chart-bar',
    children: [
      ...under({ feature: 'Performance' }, [
        { label: 'Review cycles', icon: 'pi pi-chart-line', route: '/performance', permissions: ['performance.cycle.view'] },
        { label: 'Goals', icon: 'pi pi-bullseye', route: '/goals', permissions: ['performance.goal.manage', 'performance.review.view'] },
        { label: 'Improvement plans', icon: 'pi pi-flag', route: '/performance/pips', permissions: ['performance.pip.view', 'performance.pip.manage'] },
      ]),
      // What an appraisal leads to: who moves up, and who is ready to step into key roles.
      ...under({ feature: 'Career' }, [
        { label: 'Promotions', icon: 'pi pi-angle-double-up', route: '/career/promotions', permissions: ['career.promotion.view', 'career.promotion.manage'] },
        { label: 'Succession', icon: 'pi pi-users', route: '/career/succession', permissions: ['career.succession.view', 'career.succession.manage'] },
      ]),
    ],
  },
  {
    // Learning in one place: an employee sees their own courses; HR also keeps the catalogue, assigns
    // courses and follows completion and certificates. Each sees only the pages they may open.
    label: 'Learning & Development',
    railLabel: 'Learning',
    icon: 'pi pi-book',
    feature: 'Learning',
    children: [
      { label: 'My learning', icon: 'pi pi-book', route: '/me/learning', permissions: ['self.learning.view'], needsEmployee: true },
      { label: 'Learning overview', icon: 'pi pi-chart-bar', route: '/learning', permissions: ['learning.enrollment.view'] },
      { label: 'Courses', icon: 'pi pi-list-check', route: '/learning/courses', permissions: ['learning.course.view'] },
      { label: 'Enrollments', icon: 'pi pi-users', route: '/learning/enrollments', permissions: ['learning.enrollment.view'] },
      { label: 'Certificates', icon: 'pi pi-verified', route: '/learning/certificates', permissions: ['learning.certification.view'] },
    ],
  },
  {
    // One flat list without headings: the menu lists every page, and so do the tabs above the content.
    label: 'Recruitment',
    icon: 'pi pi-user-plus',
    children: [
      { label: 'Manpower requests', icon: 'pi pi-user-plus', route: '/workforce/requests', permissions: ['workforce.request.view', 'workforce.request.manage'], feature: 'Workforce' },
      ...under({ feature: 'Recruitment', settingsSection: 'hiring' }, [
        { label: 'Requirements', icon: 'pi pi-file-edit', route: '/recruitment/requirements', permissions: ['recruitment.requisition.view'] },
        { label: 'Candidates', icon: 'pi pi-id-card', route: '/recruitment/candidates', permissions: ['recruitment.application.view'] },
        { label: 'Interviews', icon: 'pi pi-comments', route: '/recruitment/interviews', permissions: ['recruitment.application.view'] },
        { label: 'Offers', icon: 'pi pi-envelope', route: '/recruitment/offers', permissions: ['recruitment.offer.view'] },
        { label: 'Referrals & incentives', icon: 'pi pi-share-alt', route: '/recruitment/referrals', permissions: ['recruitment.application.view'] },
      ]),
      { label: 'Recruitment analytics', icon: 'pi pi-chart-line', route: '/recruitment/analytics', permissions: ['recruitment.analytics.view'], feature: 'Recruitment', settingsSection: 'hiring' },
    ],
  },
  {
    label: 'Finance & Accounts',
    icon: 'pi pi-wallet',
    children: [
      ...under({ heading: 'Payroll', feature: 'Payroll', settingsSection: 'payroll' }, [
        { label: 'Payroll overview', icon: 'pi pi-chart-bar', route: '/payroll', permissions: ['payroll.run.view'] },
        { label: 'Payroll runs', icon: 'pi pi-play', route: '/payroll/runs', permissions: ['payroll.run.view'] },
        { label: 'Salaries', icon: 'pi pi-wallet', route: '/payroll/salaries', permissions: ['payroll.employee-salary.view'] },
        { label: 'Inputs', icon: 'pi pi-plus-circle', route: '/payroll/inputs', permissions: ['payroll.input.manage'] },
        { label: 'Loans', icon: 'pi pi-money-bill', route: '/loans', permissions: ['loan.loan.view'] },
        { label: 'Payslips', icon: 'pi pi-file', route: '/payroll/payslips', permissions: ['payroll.run.view'] },
        { label: 'Tax declarations', icon: 'pi pi-file-edit', route: '/payroll/declarations', permissions: ['payroll.tax-declaration.view'] },
      ]),
      ...under({ heading: 'Compensation', feature: 'Compensation', settingsSection: 'payroll' }, [
        { label: 'Salary revisions', icon: 'pi pi-arrow-up-right', route: '/compensation/revisions', permissions: ['compensation.revision.view'] },
        { label: 'Variable pay', icon: 'pi pi-star', route: '/compensation/variable-pay', permissions: ['compensation.variable-pay.view'] },
        { label: 'Equity', icon: 'pi pi-chart-line', route: '/compensation/equity', permissions: ['compensation.equity.view'] },
      ]),
      ...under({ heading: 'Expenses' }, [
        { label: 'Expenses', icon: 'pi pi-receipt', route: '/expenses', permissions: ['spend.claim.view', 'spend.travel.view'], feature: 'Expenses' },
      ]),
      ...under({ heading: 'Project finance', feature: 'ProjectFinancials', alsoNeedsFeature: 'Projects', settingsSection: 'projects' }, [
        { label: 'Financials', icon: 'pi pi-money-bill', route: '/projects/financials', permissions: ['project.financials.view'] },
        { label: 'Billing', icon: 'pi pi-file-edit', route: '/projects/billing', permissions: ['project.financials.view'] },
      ]),
      ...under({ heading: 'Compliance', feature: 'Compliance', settingsSection: 'payroll' }, [
        { label: 'Compliance overview', icon: 'pi pi-chart-bar', route: '/compliance', permissions: ['compliance.rule.view', 'compliance.filing.view'] },
        { label: 'Filing calendar', icon: 'pi pi-calendar', route: '/compliance/filings', permissions: ['compliance.filing.view'] },
        { label: 'Licences and certificates', icon: 'pi pi-verified', route: '/compliance/registrations', permissions: ['compliance.filing.view'] },
      ]),
    ],
  },
  {
    label: 'General Administration',
    railLabel: 'General admin',
    icon: 'pi pi-briefcase',
    children: [
      ...under({ heading: 'Inventory & assets' }, [
        { label: 'Assets', icon: 'pi pi-desktop', route: '/assets', permissions: ['asset.asset.view'], feature: 'Assets' },
      ]),
      ...under({ heading: 'Support', feature: 'Helpdesk' }, [
        { label: 'Helpdesk', icon: 'pi pi-question-circle', route: '/helpdesk', permissions: ['helpdesk.ticket.view'] },
        { label: 'Knowledge base', icon: 'pi pi-book', route: '/helpdesk/knowledge', permissions: ['helpdesk.knowledge.manage'] },
      ]),
      ...under({ heading: 'Records' }, [
        { label: 'Documents', icon: 'pi pi-folder-open', route: '/documents', permissions: ['document.file.view'], feature: 'Documents' },
      ]),
    ],
  },
  {
    label: 'Exits',
    settingsSection: 'exits',
    icon: 'pi pi-sign-out',
    children: [
      { label: 'Separations', icon: 'pi pi-sign-out', route: '/separation', permissions: ['separation.case.view'], feature: 'Separation' },
      { label: 'Clearance', icon: 'pi pi-check-square', route: '/separation/clearance', permissions: ['separation.clearance.manage'], feature: 'Separation' },
      { label: 'Offboarding', icon: 'pi pi-list-check', route: '/separation/offboarding', permissions: ['separation.offboarding.manage', 'separation.clearance.manage', 'separation.case.view'], feature: 'Separation' },
      { label: 'Disciplinary', icon: 'pi pi-file-edit', route: '/separation/disciplinary', permissions: ['separation.termination.manage'], feature: 'Separation' },
      { label: 'Absconding', icon: 'pi pi-user-minus', route: '/separation/absconding', permissions: ['separation.termination.manage'], feature: 'Separation' },
      { label: 'Retrenchment', icon: 'pi pi-users', route: '/separation/retrenchment', permissions: ['separation.termination.manage'], feature: 'Separation' },
      { label: 'Attrition', icon: 'pi pi-chart-line', route: '/separation/analytics', permissions: ['separation.analytics.view'], feature: 'Separation' },
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
    settingsSection: 'projects',
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
      { label: 'Portfolio', icon: 'pi pi-chart-bar', route: '/projects/portfolio', permissions: ['project.financials.view'], feature: 'ProjectFinancials' },
      { label: 'Service levels', icon: 'pi pi-stopwatch', route: '/projects/service-levels', permissions: ['project.task.view'] },
    ],
  },
  {
    label: 'Resources',
    settingsSection: 'time',
    icon: 'pi pi-chart-line',
    feature: 'Resources',
    children: [
      { label: 'Resource directory', icon: 'pi pi-address-book', route: '/resources', permissions: ['resource.profile.view'] },
      { label: 'Requests', icon: 'pi pi-inbox', route: '/resources/requests', permissions: ['resource.request.view'] },
      { label: 'Allocation calendar', icon: 'pi pi-calendar', route: '/resources/calendar', permissions: ['resource.allocation.view'] },
      { label: 'Capacity & demand', icon: 'pi pi-chart-bar', route: '/resources/capacity', permissions: ['resource.allocation.view'] },
      { label: 'Bench', icon: 'pi pi-pause-circle', route: '/resources/bench', permissions: ['resource.profile.view'] },
      { label: 'Utilization', icon: 'pi pi-gauge', route: '/resources/utilization', permissions: ['resource.utilization.view'] },
      { label: 'Timesheet approvals', icon: 'pi pi-stopwatch', route: '/timesheets/approvals', permissions: ['timesheet.entry.approve', 'workflow.task.act'], feature: 'Timesheets' },
      { label: 'Transfers', icon: 'pi pi-arrow-right-arrow-left', route: '/resources/transfers', permissions: ['resource.allocation.view'] },
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
    // Every master and setting, one category per card on the Settings home (/settings).
    label: 'Settings',
    icon: 'pi pi-cog',
    route: '/settings',
    children: [
      { label: 'Companies', icon: 'pi pi-building', route: '/org/companies', permissions: ORG_MANAGE, section: 'organization' },
      { label: 'Cost centers', icon: 'pi pi-percentage', route: '/org/cost-centers', permissions: ORG_MANAGE, section: 'organization' },
      { label: 'Business units', icon: 'pi pi-briefcase', route: '/org/business-units', permissions: ORG_MANAGE, section: 'organization' },
      { label: 'Departments & functions', icon: 'pi pi-users', route: '/org/departments', permissions: ORG_MANAGE, section: 'organization' },
      { label: 'Locations', icon: 'pi pi-map-marker', route: '/org/locations', permissions: ORG_MANAGE, section: 'organization' },
      { label: 'Roles', icon: 'pi pi-tags', route: '/org/roles', permissions: ORG_MANAGE, section: 'organization' },
      { label: 'Designations', icon: 'pi pi-id-card', route: '/org/designations', permissions: ORG_MANAGE, section: 'organization' },
      { label: 'Levels', icon: 'pi pi-sort-amount-up', route: '/org/levels', permissions: ORG_MANAGE, section: 'organization' },
      { label: 'Shifts & rosters', icon: 'pi pi-calendar-clock', route: '/attendance/shifts', permissions: ['attendance.shift.manage', 'attendance.roster.manage'], feature: 'Time', section: 'time' },
      { label: 'Leave types', icon: 'pi pi-tags', route: '/leave/types', permissions: ['leave.type.manage', 'leave.policy.manage'], feature: 'Time', section: 'time' },
      { label: 'Leave policies', icon: 'pi pi-book', route: '/leave/policies', permissions: ['leave.policy.manage'], feature: 'Time', section: 'time' },
      { label: 'Leave policy assignment', icon: 'pi pi-users', route: '/leave/policy-assignment', permissions: ['leave.policy.manage'], feature: 'Time', section: 'time' },
      { label: 'Holidays', icon: 'pi pi-sun', route: '/leave/holidays', permissions: ['leave.holiday.manage'], feature: 'Time', section: 'time' },
      { label: 'Timesheet settings', icon: 'pi pi-cog', route: '/timesheets/settings', permissions: ['timesheet.settings.manage'], feature: 'Timesheets', section: 'time' },
      { label: 'Payroll settings', icon: 'pi pi-cog', route: '/payroll/settings', permissions: ['payroll.settings.manage'], feature: 'Payroll', section: 'payroll' },
      { label: 'Salary structures', icon: 'pi pi-sitemap', route: '/payroll/structures', permissions: ['payroll.structure.manage', 'payroll.component.manage'], feature: 'Payroll', section: 'payroll' },
      { label: 'Salary bands', icon: 'pi pi-sliders-h', route: '/compensation/bands', permissions: ['compensation.band.view'], feature: 'Compensation', section: 'payroll' },
      { label: 'Loan types', icon: 'pi pi-list', route: '/loans/types', permissions: ['loan.type.manage'], feature: 'Payroll', section: 'payroll' },
      { label: 'Statutory rules', icon: 'pi pi-book', route: '/compliance/rules', permissions: ['compliance.rule.view'], feature: 'Compliance', section: 'payroll' },
      { label: 'Recruitment settings', icon: 'pi pi-cog', route: '/recruitment/settings', permissions: ['recruitment.settings.manage'], feature: 'Recruitment', section: 'hiring' },
      { label: 'Onboarding checklists', icon: 'pi pi-list-check', route: '/onboarding/templates', permissions: ['onboarding.template.view'], feature: 'Onboarding', section: 'exits' },
      { label: 'Separation policy', icon: 'pi pi-sliders-h', route: '/separation/policy', permissions: ['separation.policy.manage'], feature: 'Separation', section: 'exits' },
      { label: 'Offboarding checklists', icon: 'pi pi-clipboard', route: '/separation/offboarding-checklists', permissions: ['separation.offboarding.manage'], feature: 'Separation', section: 'exits' },
      { label: 'Exit survey', icon: 'pi pi-comments', route: '/separation/exit-survey', permissions: ['separation.policy.manage'], feature: 'Separation', section: 'exits' },
      { label: 'Project settings', icon: 'pi pi-cog', route: '/projects/settings', permissions: ['project.settings.manage'], feature: 'Projects', section: 'projects' },
      { label: 'Master data', icon: 'pi pi-database', route: '/admin/master-data', permissions: ['master-data.lookup.manage'], section: 'lookups' },
      { label: 'Users', icon: 'pi pi-user', route: '/admin/users', permissions: ['admin.user.view'], section: 'access' },
      { label: 'Roles', icon: 'pi pi-lock', route: '/admin/roles', permissions: ['admin.role.view'], section: 'access' },
      { label: 'Access overview', icon: 'pi pi-eye', route: '/admin/access', permissions: ['admin.user.view'], section: 'access' },
      { label: 'Audit log', icon: 'pi pi-history', route: '/admin/audit', permissions: ['audit.log.view'], section: 'access' },
      { label: 'General settings', icon: 'pi pi-sliders-h', route: '/admin/settings', permissions: ['admin.setting.view'], section: 'platform' },
      { label: 'Dashboard banner', icon: 'pi pi-megaphone', route: '/engagement/banner', permissions: ['engagement.survey.manage'], section: 'platform' },
      { label: 'Imports', icon: 'pi pi-upload', route: '/admin/data/imports', permissions: ['datahub.import.run'], section: 'platform' },
      // Bulk-loading employees is set-up work, so it lives here rather than in HR Portal.
      { label: 'Import employees', icon: 'pi pi-user-plus', route: '/employees/import', permissions: ['employee.profile.create'], section: 'platform' },
      { label: 'About Nuts and Bolts', icon: 'pi pi-info-circle', route: '/admin/about', permissions: ['admin.setting.view'], section: 'platform' },
    ],
  },
  {
    // How Nuts and Bolts talks to the outside world: who each module's email comes from, what it says and
    // what happened to it, sign-in, connected tools, approval chains and keys for the organisation's own systems.
    label: 'Studio',
    icon: 'pi pi-bolt',
    children: [
      { label: 'Overview', icon: 'pi pi-th-large', route: '/studio', permissions: ['datahub.integration.view', 'admin.user.view'] },
      { label: 'Email senders', icon: 'pi pi-envelope', route: '/studio/email-senders', permissions: ['datahub.integration.view'] },
      { label: 'Email templates', icon: 'pi pi-bell', route: '/admin/notifications', permissions: ['notification.template.view'] },
      { label: 'Email log', icon: 'pi pi-inbox', route: '/studio/email-log', permissions: ['datahub.integration.view'] },
      { label: 'Text messages', icon: 'pi pi-comments', route: '/studio/sending', permissions: ['datahub.integration.view'] },
      { label: 'Single sign-on', icon: 'pi pi-key', route: '/admin/identity-providers', permissions: ['admin.identity-provider.manage'] },
      { label: 'Password policy', icon: 'pi pi-lock', route: '/admin/password-policy', permissions: ['admin.setting.view'] },
      { label: 'Connectors', icon: 'pi pi-link', route: '/studio/connectors', permissions: ['datahub.integration.view'] },
      { label: 'Work accounts', icon: 'pi pi-users', route: '/studio/accounts', permissions: ['datahub.integration.view'] },
      { label: 'Automations', icon: 'pi pi-sitemap', route: '/studio/automations', permissions: ['datahub.integration.view'] },
      { label: 'Automation log', icon: 'pi pi-send', route: '/studio/deliveries', permissions: ['datahub.integration.view'] },
      { label: 'Workflows', icon: 'pi pi-share-alt', route: '/admin/workflows', permissions: ['workflow.definition.view'] },
      { label: 'API keys', icon: 'pi pi-key', route: '/studio/api-keys', permissions: ['admin.user.view'] },
    ],
  },
  {
    // Guides to every screen and the help assistant. For everyone: no permission, no plan feature.
    label: 'Help & Wiki',
    icon: 'pi pi-book',
    route: '/help',
  },
];
