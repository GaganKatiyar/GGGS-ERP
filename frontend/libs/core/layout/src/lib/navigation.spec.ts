import { describe, expect, it } from 'vitest';
import { NAVIGATION, filterNavigation, isBuilt, type NavItem } from './navigation';

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
    expect(labels).not.toContain('Administration');
    expect(labels).not.toContain('Employees');
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
      '/me/documents',
      '/me/policies',
      '/team',
      '/team/attendance',
      '/team/leave',
      '/org/structure',
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
      '/employees',
      '/employees/org-chart',
      '/employees/import',
      '/attendance',
      '/attendance/regularizations',
      '/attendance/shifts',
      '/leave/requests',
      '/leave/policies',
      '/leave/holidays',
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
      '/projects/settings',
      '/documents',
      '/policies',
      '/admin/users',
      '/admin/roles',
      '/admin/audit',
      '/admin/settings',
      '/admin/workflows',
      '/admin/notifications',
      '/admin/master-data',
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
