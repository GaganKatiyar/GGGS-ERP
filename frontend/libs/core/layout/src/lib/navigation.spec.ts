import { describe, expect, it } from 'vitest';
import { NAVIGATION, SETTINGS_SECTIONS, entryRoute, filterMenu, filterNavigation, isBuilt, isUnder, locate, moduleTabs, searchNavigation, sectionsOf, type NavItem } from './navigation';

/** Everything visible, for tests about structure rather than permissions. */
const showAll = () => true;

describe('filterNavigation', () => {
  it('hides entries the user cannot reach', () => {
    const items: NavItem[] = [
      {
        label: 'Employees',
        icon: 'pi',
        route: '/employees',
        permissions: ['employee.profile.view'],
      },
      {
        label: 'Payroll',
        icon: 'pi',
        route: '/payroll',
        permissions: ['payroll.run.view'],
      },
    ];

    const visible = filterNavigation(
      items,
      (item) => item.permissions?.includes('employee.profile.view') ?? true,
    );

    expect(visible.map((item) => item.label)).toEqual(['Employees']);
  });

  it('drops a group once all of its children are hidden', () => {
    // An "Administration" heading that opens onto nothing is worse than no heading at all.
    const items: NavItem[] = [
      {
        label: 'Administration',
        icon: 'pi',
        children: [
          {
            label: 'Users',
            icon: 'pi',
            route: '/admin/users',
            permissions: ['admin.user.view'],
          },
          {
            label: 'Roles',
            icon: 'pi',
            route: '/admin/roles',
            permissions: ['admin.role.view'],
          },
        ],
      },
    ];

    const visible = filterNavigation(
      items,
      (item) => item.children !== undefined,
    );

    expect(visible).toEqual([]);
  });

  it('keeps a group when at least one child survives', () => {
    const items: NavItem[] = [
      {
        label: 'Administration',
        icon: 'pi',
        children: [
          {
            label: 'Users',
            icon: 'pi',
            route: '/admin/users',
            permissions: ['admin.user.view'],
          },
          {
            label: 'Roles',
            icon: 'pi',
            route: '/admin/roles',
            permissions: ['admin.role.view'],
          },
        ],
      },
    ];

    const visible = filterNavigation(
      items,
      (item) => item.children !== undefined || item.label === 'Users',
    );

    expect(visible).toHaveLength(1);
    expect(visible[0].children?.map((child) => child.label)).toEqual(['Users']);
  });

  it('leaves the source manifest untouched', () => {
    // The manifest is a module-level constant shared by every user in the tab.
    const before = JSON.stringify(NAVIGATION);

    filterNavigation(NAVIGATION, () => false);

    expect(JSON.stringify(NAVIGATION)).toEqual(before);
  });

  it('gives an employee with only self-service permissions a short menu', () => {
    const selfServiceOnly = new Set([
      'self.profile.view',
      'self.attendance.view',
      'self.leave.view',
    ]);

    const visible = filterNavigation(
      NAVIGATION,
      (item) =>
        item.permissions === undefined ||
        item.permissions.some((permission) => selfServiceOnly.has(permission)),
    );

    const labels = visible.map((item) => item.label);
    expect(labels).toContain('Dashboard');
    expect(labels).toContain('My Profile & Documents');
    expect(labels).toContain('My Time & Leave');
    // Nothing administrative leaks into an employee's sidebar.
    expect(labels).not.toContain('Settings');
    expect(labels).not.toContain('Finance & Accounts');
  });

  it('shows an account without an employee record no self-service areas, since every page there needs one', () => {
    const visible = filterNavigation(NAVIGATION, (item) => item.needsEmployee !== true);

    for (const label of ['My Profile & Documents', 'My Time & Leave', 'My Pay & Benefits', 'My Appraisal', 'My Growth', 'My Workplace']) {
      expect(visible.find((item) => item.label === label), label).toBeUndefined();
    }
  });

  it('shows every top-level area to a user who can do everything', () => {
    const visible = filterNavigation(NAVIGATION, showAll);

    expect(visible.map((item) => item.label)).toEqual(
      NAVIGATION.map((item) => item.label),
    );
  });

  it('never shows a module that has not been built, whatever the permissions', () => {
    // Planned entries have no route behind them yet; an Enterprise tenant licensed for everything
    // must still not be offered a link to a page that does not exist.
    const visible = filterNavigation(NAVIGATION, isBuilt);

    const reachable = (items: readonly NavItem[]): string[] =>
      items.flatMap((item) => [...(item.route ? [item.route] : []), ...reachable(item.children ?? [])]);

    expect(reachable(visible)).toEqual([
      '/dashboard',
      '/approvals',
      '/me/profile',
      '/me/documents',
      '/me/assets',
      '/me/policies',
      '/me/preferences',
      '/me/attendance',
      '/me/leave',
      '/me/timesheet',
      '/me/payslips',
      '/me/tax-declaration',
      '/me/compensation',
      '/me/benefits',
      '/me/expenses',
      '/me/loans',
      '/me/goals',
      '/me/reviews',
      '/me/feedback',
      '/me/skills',
      '/me/career',
      '/me/rewards',
      '/rewards',
      '/me/onboarding',
      '/me/jobs',
      '/me/interviews',
      '/me/surveys',
      '/me/tickets',
      '/me/separation',
      '/team',
      '/team/attendance',
      '/team/leave',
      '/team/timesheets',
      '/employees',
      '/employees/org-chart',
      '/org/structure',
      '/onboarding',
      '/attendance',
      '/leave/requests',
      '/skills',
      '/career',
      '/engagement',
      '/engagement/anniversaries',
      '/engagement/birthdays',
      '/policies',
      '/performance',
      '/goals',
      '/performance/pips',
      '/career/promotions',
      '/career/succession',
      '/me/learning',
      '/learning',
      '/learning/courses',
      '/learning/enrollments',
      '/learning/certificates',
      '/workforce/requests',
      '/recruitment/requirements',
      '/recruitment/candidates',
      '/recruitment/interviews',
      '/recruitment/offers',
      '/recruitment/referrals',
      '/recruitment/analytics',
      '/payroll',
      '/payroll/runs',
      '/payroll/salaries',
      '/payroll/inputs',
      '/loans',
      '/payroll/payslips',
      '/payroll/declarations',
      '/compensation/revisions',
      '/compensation/variable-pay',
      '/compensation/equity',
      '/expenses',
      '/projects/financials',
      '/projects/billing',
      '/compliance',
      '/compliance/filings',
      '/compliance/registrations',
      '/assets',
      '/helpdesk',
      '/helpdesk/knowledge',
      '/documents',
      '/separation',
      '/separation/clearance',
      '/separation/offboarding',
      '/separation/disciplinary',
      '/separation/absconding',
      '/separation/retrenchment',
      '/separation/analytics',
      '/clients',
      '/clients/contacts',
      '/contracts',
      '/projects',
      '/projects/tasks',
      '/projects/board',
      '/projects/milestones',
      '/projects/sprints',
      '/projects/change-requests',
      '/projects/raid',
      '/projects/portfolio',
      '/projects/service-levels',
      '/resources',
      '/resources/requests',
      '/resources/calendar',
      '/resources/capacity',
      '/resources/bench',
      '/resources/utilization',
      '/timesheets/approvals',
      '/resources/transfers',
      '/analytics',
      '/calendar',
      '/ai',
      '/settings',
      '/org/companies',
      '/org/cost-centers',
      '/org/business-units',
      '/org/departments',
      '/org/locations',
      '/org/roles',
      '/org/designations',
      '/org/levels',
      '/attendance/shifts',
      '/leave/types',
      '/leave/policies',
      '/leave/policy-assignment',
      '/leave/holidays',
      '/timesheets/settings',
      '/payroll/settings',
      '/payroll/structures',
      '/compensation/bands',
      '/loans/types',
      '/compliance/rules',
      '/recruitment/settings',
      '/onboarding/templates',
      '/separation/policy',
      '/separation/offboarding-checklists',
      '/separation/exit-survey',
      '/projects/settings',
      '/admin/master-data',
      '/admin/users',
      '/admin/roles',
      '/admin/access',
      '/admin/audit',
      '/admin/settings',
      '/engagement/banner',
      '/admin/data/imports',
      '/employees/import',
      '/admin/about',
      '/studio',
      '/studio/email-senders',
      '/admin/notifications',
      '/studio/email-log',
      '/studio/sending',
      '/admin/identity-providers',
      '/admin/password-policy',
      '/studio/connectors',
      '/studio/accounts',
      '/studio/automations',
      '/studio/deliveries',
      '/admin/workflows',
      '/studio/api-keys',
      '/help',
    ]);
  });

  it('names a milestone for every planned entry', () => {
    const plannedWithoutMilestone = (items: readonly NavItem[]): string[] =>
      items.flatMap((item) => [
        ...(item.planned && !item.milestone ? [item.label] : []),
        ...plannedWithoutMilestone(item.children ?? []),
      ]);

    expect(plannedWithoutMilestone(NAVIGATION)).toEqual([]);
  });
});

describe('locate', () => {
  const nav = filterNavigation(NAVIGATION, isBuilt);

  it('puts a top-level page in its own group', () => {
    const location = locate(nav, '/dashboard');
    expect(location?.group.label).toBe('Dashboard');
    expect(location?.entry).toBe(location?.group);
  });

  it('prefers the longest matching route', () => {
    const location = locate(nav, '/payroll/runs/123?tab=items');
    expect(location?.group.label).toBe('Finance & Accounts');
    expect(location?.entry.route).toBe('/payroll/runs');
  });

  it('does not let a shorter sibling route steal a page', () => {
    expect(locate(nav, '/me/leave')?.entry.route).toBe('/me/leave');
    expect(locate(nav, '/me/leavers')).toBeNull();
  });

  it('finds nothing for a page outside the navigation', () => {
    expect(locate(nav, '/feature-unavailable')).toBeNull();
  });
});

describe('isUnder', () => {
  it('matches the route itself and pages beneath it only', () => {
    expect(isUnder('/projects', '/projects')).toBe(true);
    expect(isUnder('/projects/', '/projects')).toBe(true);
    expect(isUnder('/projects/42#risks', '/projects')).toBe(true);
    expect(isUnder('/projects-archive', '/projects')).toBe(false);
  });
});

describe('moduleTabs', () => {
  it('offers tabs for every module with more than one page, however many it has', () => {
    const page = (label: string): NavItem => ({ label, icon: 'pi', route: `/${label}` });
    expect(moduleTabs({ label: 'One', icon: 'pi', children: [page('a')] })).toEqual([]);
    expect(moduleTabs({ label: 'Few', icon: 'pi', children: [page('a'), page('b')] })).toHaveLength(2);
    expect(moduleTabs({ label: 'Many', icon: 'pi', children: Array.from({ length: 20 }, (_, i) => page(`p${i}`)) })).toHaveLength(20);
    expect(moduleTabs(null)).toEqual([]);
  });
});

describe('department menus', () => {
  const nav = filterNavigation(NAVIGATION, isBuilt);
  const finance = nav.find((group) => group.label === 'Finance & Accounts')!;

  it('are in the agreed order among the top-level entries', () => {
    expect(nav.map((group) => group.label)).toEqual([
      'Dashboard', 'Approvals', 'My Profile & Documents', 'My Time & Leave', 'My Pay & Benefits', 'My Appraisal', 'My Growth', 'Star Reward', 'My Workplace', 'My Team', 'HR Portal', 'Appraisal Admin', 'Learning & Development', 'Recruitment', 'Finance & Accounts', 'General Administration',
      'Exits', 'Clients', 'Projects', 'Resources', 'Analytics', 'Calendar', 'AI Assistant', 'Settings', 'Studio', 'Help & Wiki',
    ]);
  });

  it('gather everything that talks to the outside world into Studio, right below Settings, every page a tab', () => {
    const studio = nav.find((group) => group.label === 'Studio')!;
    const settings = nav.find((group) => group.label === 'Settings')!;
    expect(sectionsOf(studio)).toHaveLength(1);
    expect(moduleTabs(studio, studio.children![0]).map((page) => page.label)).toEqual([
      'Overview', 'Email senders', 'Email templates', 'Email log', 'Text messages', 'Single sign-on', 'Password policy', 'Connectors', 'Work accounts', 'Automations', 'Automation log', 'Workflows', 'API keys',
    ]);
    expect(entryRoute(studio)).toBe('/studio');
    expect(locate(nav, '/admin/identity-providers')?.group.label).toBe('Studio');
    expect(locate(nav, '/studio/email-log')?.group.label).toBe('Studio');
    expect(settings.children!.map((page) => page.route)).not.toContain('/admin/notifications');
  });

  it('give every page a heading', () => {
    for (const label of ['Finance & Accounts', 'General Administration']) {
      expect(nav.find((group) => group.label === label)!.children!.every((page) => page.heading), label).toBe(true);
    }
  });

  it('list Recruitment as one flat run, with every page as a tab', () => {
    const recruitment = nav.find((group) => group.label === 'Recruitment')!;
    const requests = recruitment.children!.find((page) => page.route === '/workforce/requests')!;
    expect(recruitment.children!.some((page) => page.heading)).toBe(false);
    expect(sectionsOf(recruitment)).toHaveLength(1);
    expect(moduleTabs(recruitment, requests).map((page) => page.label)).toEqual([
      'Manpower requests', 'Requirements', 'Candidates', 'Interviews', 'Offers', 'Referrals & incentives', 'Recruitment analytics',
    ]);
    expect(locate(nav, '/recruitment/candidates/42')?.group.label).toBe('Recruitment');
  });

  it('gather the appraisal into its own menus: the employee’s side and HR’s side', () => {
    const routesOf = (label: string): (string | undefined)[] => nav.find((group) => group.label === label)!.children!.map((page) => page.route);
    expect(routesOf('My Appraisal')).toEqual(['/me/goals', '/me/reviews', '/me/feedback']);
    expect(routesOf('Appraisal Admin')).toEqual(['/performance', '/goals', '/performance/pips', '/career/promotions', '/career/succession']);
    expect(routesOf('My Growth')).not.toContain('/me/reviews');
    expect(routesOf('HR Portal')).not.toContain('/performance');
    expect(locate(nav, '/performance/cycles/42')?.group.label).toBe('Appraisal Admin');
  });

  it('gather learning into Learning & Development, where an employee sees only their own courses', () => {
    const learning = nav.find((group) => group.label === 'Learning & Development')!;
    expect(learning.children!.map((page) => page.route)).toEqual(['/me/learning', '/learning', '/learning/courses', '/learning/enrollments', '/learning/certificates']);
    expect(nav.find((group) => group.label === 'My Growth')!.children!.map((page) => page.route)).not.toContain('/me/learning');
    expect(locate(nav, '/learning/courses')?.group.label).toBe('Learning & Development');

    const employee = filterNavigation(NAVIGATION, (item) => isBuilt(item) && (item.permissions === undefined || item.permissions.includes('self.learning.view')));
    expect(employee.find((group) => group.label === 'Learning & Development')?.children?.map((page) => page.label)).toEqual(['My learning']);
  });

  it('gather rewards into Star Reward, where an employee sees only their own page', () => {
    const star = nav.find((group) => group.label === 'Star Reward')!;
    expect(star.children!.map((page) => page.route)).toEqual(['/me/rewards', '/rewards']);
    expect(nav.find((group) => group.label === 'HR Portal')!.children!.map((page) => page.route)).not.toContain('/rewards');
    expect(nav.find((group) => group.label === 'My Growth')!.children!.map((page) => page.route)).not.toContain('/me/rewards');

    const employee = filterNavigation(NAVIGATION, (item) => isBuilt(item) && (item.permissions === undefined || item.permissions.includes('self.reward.view')));
    expect(employee.find((group) => group.label === 'Star Reward')?.children?.map((page) => page.label)).toEqual(['My rewards']);
  });

  it('list HR Portal as one flat run, with every page as a tab', () => {
    const hr = nav.find((group) => group.label === 'HR Portal')!;
    const attendance = hr.children!.find((page) => page.route === '/attendance')!;
    expect(hr.children!.some((page) => page.heading)).toBe(false);
    expect(sectionsOf(hr)).toHaveLength(1);
    expect(sectionsOf(hr)[0]!.label).toBeUndefined();
    expect(moduleTabs(hr, attendance)).toBe(hr.children);
    expect(attendance.settingsSection).toBe('time');
  });

  it('offer the current heading as tabs, none on a heading with one page', () => {
    const salaries = finance.children!.find((page) => page.route === '/payroll/salaries')!;
    expect(moduleTabs(finance, salaries).every((page) => page.heading === 'Payroll')).toBe(true);
    expect(moduleTabs(finance, salaries)).toHaveLength(7);
    expect(moduleTabs(finance, finance.children!.find((page) => page.route === '/expenses'))).toEqual([]);
    expect(moduleTabs(finance, finance)).toEqual([]);
  });

  it('keep what a page needs from its plan after leaving its old module', () => {
    const billing = finance.children!.find((page) => page.route === '/projects/billing')!;
    expect([billing.feature, billing.alsoNeedsFeature]).toEqual(['ProjectFinancials', 'Projects']);
    expect(finance.children!.find((page) => page.route === '/payroll/runs')!.feature).toBe('Payroll');
  });
});

describe('Settings', () => {
  const nav = filterNavigation(NAVIGATION, isBuilt);
  const settings = nav.find((group) => group.label === 'Settings')!;

  it('gathers every set-up page into one group, each in a known category', () => {
    const keys = new Set(SETTINGS_SECTIONS.map((section) => section.key));
    expect(settings.route).toBe('/settings');
    expect(settings.children!.length).toBeGreaterThan(30);
    expect(settings.children!.every((page) => page.section && keys.has(page.section))).toBe(true);
    // Each category has something in it, or its card would be an empty promise.
    expect([...keys].every((key) => settings.children!.some((page) => page.section === key))).toBe(true);
  });

  it('lists organization masters for those who can change them, while viewers browse the structure from HR Portal', () => {
    const viewer = filterNavigation(NAVIGATION, (item) => isBuilt(item) && (item.permissions === undefined || item.permissions.includes('org.structure.view')));
    expect(viewer.find((group) => group.label === 'Settings')).toBeUndefined();
    expect(viewer.find((group) => group.label === 'HR Portal')?.children?.map((page) => page.route)).toEqual(['/org/structure']);
  });

  it('keeps set-up pages out of the modules, and every route in one place only', () => {
    const routes = nav.flatMap((group) => [group.route, ...(group.children ?? []).map((page) => page.route)]).filter(Boolean);
    expect(new Set(routes).size).toBe(routes.length);
    expect(nav.find((group) => group.label === 'Organization')).toBeUndefined();
    expect(nav.find((group) => group.label === 'Finance & Accounts')!.children!.map((page) => page.route)).not.toContain('/payroll/settings');
  });

  it('points each module that gave up pages at their category', () => {
    for (const item of nav.flatMap((group) => [group, ...(group.children ?? [])]).filter((candidate) => candidate.settingsSection)) {
      expect(settings.children!.some((page) => page.section === item.settingsSection), item.label).toBe(true);
    }
  });

  it('shows the tabs of the current page’s category only, and none on the Settings home', () => {
    const departments = settings.children!.find((page) => page.route === '/org/departments')!;
    expect(moduleTabs(settings, departments).every((page) => page.section === 'organization')).toBe(true);
    expect(moduleTabs(settings, departments).length).toBe(8);
    expect(moduleTabs(settings, settings)).toEqual([]);
    expect(locate(nav, '/settings?section=time')?.entry).toBe(settings);
    expect(locate(nav, '/admin/roles/42')?.entry.section).toBe('access');
  });
});

describe('sectionsOf', () => {
  const nav = filterNavigation(NAVIGATION, isBuilt);

  it('gives an ordinary module one run of pages, without a caption', () => {
    const team = nav.find((group) => group.label === 'My Team')!;
    const sections = sectionsOf(team);
    expect(sections).toHaveLength(1);
    expect(sections[0]!.label).toBeUndefined();
    expect(sections[0]!.pages).toBe(team.children);
  });

  it('gives Settings one captioned run per category, in category order', () => {
    const sections = sectionsOf(nav.find((group) => group.label === 'Settings')!);
    expect(sections.map((section) => section.label)).toEqual(SETTINGS_SECTIONS.map((section) => section.label));
    expect(sections[0]!.pages.every((page) => page.section === 'organization')).toBe(true);
  });

  it('gives a department menu one captioned run per heading, in the order written', () => {
    const sections = sectionsOf(nav.find((group) => group.label === 'General Administration')!);
    expect(sections.map((section) => section.label)).toEqual(['Inventory & assets', 'Support', 'Records']);
    expect(sections[1]!.pages.map((page) => page.label)).toEqual(['Helpdesk', 'Knowledge base']);
  });

  it('has nothing to list for a module that is a page itself', () => {
    expect(sectionsOf({ label: 'Dashboard', icon: 'pi', route: '/dashboard' })).toEqual([]);
  });
});

describe('filterMenu', () => {
  const nav = filterNavigation(NAVIGATION, isBuilt);

  it('keeps a module whose name matches whole, and otherwise only its matching pages', () => {
    const finance = filterMenu(nav, 'finance').find((group) => group.label === 'Finance & Accounts');
    expect(finance?.children).toBe(nav.find((group) => group.label === 'Finance & Accounts')!.children);

    const result = filterMenu(nav, 'payslip');
    expect(result.find((group) => group.label === 'My Pay & Benefits')?.children?.map((page) => page.label)).toEqual(['My payslips']);
    expect(result.find((group) => group.label === 'HR Portal')).toBeUndefined();
  });

  it('finds every page under a heading by the heading, as it did when the heading was a module', () => {
    const pages = filterMenu(nav, 'payroll').find((group) => group.label === 'Finance & Accounts')?.children?.map((page) => page.label);
    expect(pages).toEqual(expect.arrayContaining(['Salaries', 'Loans', 'Payslips']));
  });

  it('is the whole menu for an empty search and nothing for a term nothing matches', () => {
    expect(filterMenu(nav, '  ')).toBe(nav);
    expect(filterMenu(nav, 'zzz')).toEqual([]);
  });
});

describe('entryRoute', () => {
  it('opens a page directly and a module on its first page', () => {
    expect(entryRoute({ label: 'Dashboard', icon: 'pi', route: '/dashboard' })).toBe('/dashboard');
    expect(entryRoute({ label: 'Payroll', icon: 'pi', children: [{ label: 'Overview', icon: 'pi', route: '/payroll' }, { label: 'Runs', icon: 'pi', route: '/payroll/runs' }] })).toBe('/payroll');
    expect(entryRoute({ label: 'Empty', icon: 'pi', children: [] })).toBeUndefined();
  });

  it('gives every module in the manifest somewhere to go', () => {
    for (const item of NAVIGATION.filter(isBuilt)) {
      expect(entryRoute(item), item.label).toBeDefined();
    }
  });
});

describe('searchNavigation', () => {
  const visible = filterNavigation(NAVIGATION, isBuilt);

  it('shows every module and no pages before anything is typed', () => {
    const result = searchNavigation(visible, '  ');
    expect(result.apps).toEqual(visible);
    expect(result.pages).toEqual([]);
  });

  it('finds modules by name or short name, ignoring case', () => {
    expect(searchNavigation(visible, 'FINANCE').apps.map((item) => item.label)).toContain('Finance & Accounts');
    expect(searchNavigation(visible, 'general adm').apps.map((item) => item.label)).toEqual(['General Administration']);
  });

  it('finds pages from two characters, each with its module', () => {
    expect(searchNavigation(visible, 'p').pages).toEqual([]);
    const pages = searchNavigation(visible, 'payslip').pages.map((match) => `${match.group.label} > ${match.page.label}`);
    expect(pages).toEqual(expect.arrayContaining(['My Pay & Benefits > My payslips', 'Finance & Accounts > Payslips']));
  });

  it('caps the pages it lists', () => {
    expect(searchNavigation(visible, 'my', 5).pages).toHaveLength(5);
  });

  it('finds nothing for a term nothing matches', () => {
    const result = searchNavigation(visible, 'zzz');
    expect(result.apps).toEqual([]);
    expect(result.pages).toEqual([]);
  });
});
