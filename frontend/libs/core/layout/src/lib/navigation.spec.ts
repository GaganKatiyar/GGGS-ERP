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
    expect(labels).toContain('My Space');
    // Nothing administrative leaks into an employee's sidebar.
    expect(labels).not.toContain('Settings');
    expect(labels).not.toContain('Employees');
  });

  it('shows an account without an employee record no My Space, since every page there needs one', () => {
    const visible = filterNavigation(NAVIGATION, (item) => item.needsEmployee !== true);

    expect(visible.find((item) => item.label === 'My Space')).toBeUndefined();
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
      '/me/attendance',
      '/me/leave',
      '/me/timesheet',
      '/me/skills',
      '/me/payslips',
      '/me/tax-declaration',
      '/me/compensation',
      '/me/benefits',
      '/me/goals',
      '/me/reviews',
      '/me/feedback',
      '/me/learning',
      '/me/career',
      '/me/onboarding',
      '/me/jobs',
      '/me/interviews',
      '/me/expenses',
      '/me/loans',
      '/me/rewards',
      '/me/surveys',
      '/me/assets',
      '/me/documents',
      '/me/policies',
      '/me/tickets',
      '/me/preferences',
      '/me/separation',
      '/team',
      '/team/attendance',
      '/team/leave',
      '/team/timesheets',
      '/workforce',
      '/workforce/plans',
      '/workforce/requests',
      '/recruitment/requisitions',
      '/recruitment/postings',
      '/recruitment/candidates',
      '/recruitment/interviews',
      '/recruitment/offers',
      '/recruitment/analytics',
      '/onboarding',
      '/employees',
      '/employees/org-chart',
      '/org/structure',
      '/employees/import',
      '/attendance',
      '/attendance/regularizations',
      '/leave/requests',
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
      '/projects/financials',
      '/resources',
      '/resources/requests',
      '/resources/calendar',
      '/resources/capacity',
      '/resources/bench',
      '/resources/utilization',
      '/timesheets/approvals',
      '/resources/transfers',
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
      '/benefits',
      '/benefits/enrollments',
      '/compliance',
      '/compliance/filings',
      '/compliance/registrations',
      '/performance',
      '/performance/pips',
      '/goals',
      '/learning',
      '/learning/courses',
      '/skills',
      '/career',
      '/career/promotions',
      '/career/succession',
      '/career/talent-review',
      '/rewards',
      '/engagement',
      '/expenses',
      '/assets',
      '/helpdesk',
      '/helpdesk/knowledge',
      '/documents',
      '/policies',
      '/separation',
      '/separation/clearance',
      '/separation/offboarding',
      '/separation/disciplinary',
      '/separation/absconding',
      '/separation/retrenchment',
      '/separation/analytics',
      '/analytics',
      '/calendar',
      '/ai',
      '/settings',
      '/org/companies',
      '/org/business-units',
      '/org/divisions',
      '/org/departments',
      '/org/locations',
      '/org/jobs',
      '/org/positions',
      '/org/grades',
      '/org/job-levels',
      '/org/cost-centers',
      '/org/profit-centers',
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
      '/admin/identity-providers',
      '/admin/audit',
      '/admin/settings',
      '/admin/workflows',
      '/admin/notifications',
      '/admin/data/imports',
      '/studio',
      '/studio/connectors',
      '/studio/sending',
      '/studio/accounts',
      '/studio/automations',
      '/studio/deliveries',
      '/studio/api-keys',
      '/admin/about',
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
    expect(location?.group.label).toBe('Payroll');
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

  it('lists organization masters for those who can change them, while viewers browse the structure from Employees', () => {
    const viewer = filterNavigation(NAVIGATION, (item) => isBuilt(item) && (item.permissions === undefined || item.permissions.includes('org.structure.view')));
    expect(viewer.find((group) => group.label === 'Settings')).toBeUndefined();
    expect(viewer.find((group) => group.label === 'Employees')?.children?.map((page) => page.route)).toEqual(['/org/structure']);
  });

  it('keeps set-up pages out of the modules, and every route in one place only', () => {
    const routes = nav.flatMap((group) => [group.route, ...(group.children ?? []).map((page) => page.route)]).filter(Boolean);
    expect(new Set(routes).size).toBe(routes.length);
    expect(nav.find((group) => group.label === 'Organization')).toBeUndefined();
    expect(nav.find((group) => group.label === 'Payroll')!.children!.map((page) => page.route)).not.toContain('/payroll/settings');
  });

  it('points each module that gave up pages at their category', () => {
    for (const group of nav.filter((candidate) => candidate.settingsSection)) {
      expect(settings.children!.some((page) => page.section === group.settingsSection), group.label).toBe(true);
    }
  });

  it('shows the tabs of the current page’s category only, and none on the Settings home', () => {
    const departments = settings.children!.find((page) => page.route === '/org/departments')!;
    expect(moduleTabs(settings, departments).every((page) => page.section === 'organization')).toBe(true);
    expect(moduleTabs(settings, departments).length).toBe(11);
    expect(moduleTabs(settings, settings)).toEqual([]);
    expect(locate(nav, '/settings?section=time')?.entry).toBe(settings);
    expect(locate(nav, '/admin/roles/42')?.entry.section).toBe('access');
  });
});

describe('sectionsOf', () => {
  const nav = filterNavigation(NAVIGATION, isBuilt);

  it('gives an ordinary module one run of pages, without a caption', () => {
    const payroll = nav.find((group) => group.label === 'Payroll')!;
    const sections = sectionsOf(payroll);
    expect(sections).toHaveLength(1);
    expect(sections[0]!.label).toBeUndefined();
    expect(sections[0]!.pages).toBe(payroll.children);
  });

  it('gives Settings one captioned run per category, in category order', () => {
    const sections = sectionsOf(nav.find((group) => group.label === 'Settings')!);
    expect(sections.map((section) => section.label)).toEqual(SETTINGS_SECTIONS.map((section) => section.label));
    expect(sections[0]!.pages.every((page) => page.section === 'organization')).toBe(true);
  });

  it('has nothing to list for a module that is a page itself', () => {
    expect(sectionsOf({ label: 'Dashboard', icon: 'pi', route: '/dashboard' })).toEqual([]);
  });
});

describe('filterMenu', () => {
  const nav = filterNavigation(NAVIGATION, isBuilt);

  it('keeps a module whose name matches whole, and otherwise only its matching pages', () => {
    const payroll = filterMenu(nav, 'payroll').find((group) => group.label === 'Payroll');
    expect(payroll?.children).toBe(nav.find((group) => group.label === 'Payroll')!.children);

    const result = filterMenu(nav, 'payslip');
    expect(result.find((group) => group.label === 'My Space')?.children?.map((page) => page.label)).toEqual(['My payslips']);
    expect(result.find((group) => group.label === 'Talent')).toBeUndefined();
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
    expect(searchNavigation(visible, 'PAY').apps.map((item) => item.label)).toContain('Payroll');
    expect(searchNavigation(visible, 'comp &').apps.map((item) => item.label)).toEqual(['Compensation & benefits']);
  });

  it('finds pages from two characters, each with its module', () => {
    expect(searchNavigation(visible, 'p').pages).toEqual([]);
    const pages = searchNavigation(visible, 'payslip').pages.map((match) => `${match.group.label} > ${match.page.label}`);
    expect(pages).toEqual(expect.arrayContaining(['My Space > My payslips', 'Payroll > Payslips']));
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
