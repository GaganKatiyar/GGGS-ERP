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

export const NAVIGATION: readonly NavItem[] = [
  {
    label: 'Home',
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
    label: 'My Space',
    icon: 'pi pi-user',
    permissions: ['self.profile.view'],
    children: [
      {
        label: 'My profile',
        icon: 'pi pi-id-card',
        route: '/me/profile',
        permissions: ['self.profile.view'],
      },
      {
        label: 'My attendance',
        icon: 'pi pi-clock',
        route: '/me/attendance',
        permissions: ['self.attendance.view'],
      },
      {
        label: 'My leave',
        icon: 'pi pi-calendar',
        route: '/me/leave',
        permissions: ['self.leave.view'],
      },
      {
        label: 'My payslips',
        icon: 'pi pi-wallet',
        route: '/me/payslips',
        permissions: ['self.payslip.view'],
        feature: 'Payroll',
      },
      {
        label: 'My documents',
        icon: 'pi pi-file',
        route: '/me/documents',
        permissions: ['self.document.view'],
      },
    ],
  },
  {
    label: 'My Team',
    icon: 'pi pi-users',
    permissions: [
      'employee.profile.view',
      'attendance.record.view',
      'leave.request.view',
    ],
    children: [
      {
        label: 'Team overview',
        icon: 'pi pi-chart-bar',
        route: '/team',
        permissions: ['employee.profile.view'],
      },
      {
        label: 'Team attendance',
        icon: 'pi pi-clock',
        route: '/team/attendance',
        permissions: ['attendance.record.view'],
      },
      {
        label: 'Team leave',
        icon: 'pi pi-calendar',
        route: '/team/leave',
        permissions: ['leave.request.view'],
      },
    ],
  },
  {
    label: 'Organization',
    icon: 'pi pi-sitemap',
    permissions: ['org.structure.view'],
    children: [
      {
        label: 'Legal entities',
        icon: 'pi pi-building',
        route: '/org/legal-entities',
        permissions: ['org.structure.view'],
      },
      {
        label: 'Business units',
        icon: 'pi pi-sitemap',
        route: '/org/business-units',
        permissions: ['org.structure.view'],
      },
      {
        label: 'Branches',
        icon: 'pi pi-map-marker',
        route: '/org/branches',
        permissions: ['org.structure.view'],
      },
      {
        label: 'Locations',
        icon: 'pi pi-map',
        route: '/org/locations',
        permissions: ['org.structure.view'],
      },
      {
        label: 'Departments',
        icon: 'pi pi-sitemap',
        route: '/org/departments',
        permissions: ['org.structure.view'],
      },
      {
        label: 'Designations',
        icon: 'pi pi-briefcase',
        route: '/org/designations',
        permissions: ['org.structure.view'],
      },
      {
        label: 'Grades',
        icon: 'pi pi-star',
        route: '/org/grades',
        permissions: ['org.structure.view'],
      },
      {
        label: 'Cost centers',
        icon: 'pi pi-wallet',
        route: '/org/cost-centers',
        permissions: ['org.structure.view'],
      },
    ],
  },
  {
    label: 'Employees',
    icon: 'pi pi-id-card',
    route: '/employees',
    permissions: ['employee.profile.view'],
  },
  {
    label: 'Notifications',
    icon: 'pi pi-bell',
    route: '/notifications',
    permissions: ['notification.inbox.view'],
  },
  {
    label: 'Time',
    icon: 'pi pi-clock',
    permissions: ['attendance.record.view', 'leave.request.view', 'attendance.device.manage'],
    children: [
      {
        label: 'Attendance',
        icon: 'pi pi-calendar-clock',
        route: '/attendance',
        permissions: ['attendance.record.view'],
      },
      {
        label: 'Shifts',
        icon: 'pi pi-sync',
        route: '/attendance/shifts',
        permissions: ['attendance.shift.manage'],
      },
      {
        label: 'Biometric devices',
        icon: 'pi pi-desktop',
        route: '/attendance/devices',
        permissions: ['attendance.device.manage'],
      },
      {
        label: 'Leave requests',
        icon: 'pi pi-calendar',
        route: '/leave/requests',
        permissions: ['leave.request.view'],
      },
      {
        label: 'Leave policies',
        icon: 'pi pi-book',
        route: '/leave/policies',
        permissions: ['leave.policy.manage'],
      },
      {
        label: 'Holidays',
        icon: 'pi pi-flag',
        route: '/holidays',
        permissions: ['leave.holiday.manage'],
      },
    ],
  },
  {
    label: 'Reports',
    icon: 'pi pi-chart-line',
    route: '/reports',
    permissions: ['report.report.view'],
  },
  {
    label: 'Recruitment',
    icon: 'pi pi-user-plus',
    route: '/recruitment/requisitions',
    permissions: ['recruitment.requisition.view'],
  },
  {
    label: 'Onboarding',
    icon: 'pi pi-clipboard',
    route: '/onboarding',
    permissions: ['onboarding.template.view', 'onboarding.case.view'],
  },
  {
    label: 'Performance',
    icon: 'pi pi-chart-line',
    route: '/performance',
    permissions: ['performance.cycle.view'],
  },
  {
    label: 'Learning',
    icon: 'pi pi-graduation-cap',
    route: '/learning',
    permissions: ['learning.course.view'],
  },
  {
    label: 'Payroll',
    icon: 'pi pi-wallet',
    permissions: ['payroll.component.manage', 'payroll.structure.manage', 'payroll.employee-salary.view'],
    children: [
      {
        label: 'Components',
        icon: 'pi pi-list',
        route: '/payroll',
        permissions: ['payroll.component.manage'],
      },
      {
        label: 'Structures',
        icon: 'pi pi-sitemap',
        route: '/payroll/structures',
        permissions: ['payroll.structure.manage'],
      },
      {
        label: 'Employee salary',
        icon: 'pi pi-money-bill',
        route: '/payroll/employee-salary',
        permissions: ['payroll.employee-salary.view'],
      },
    ],
  },
  {
    label: 'Administration',
    icon: 'pi pi-cog',
    permissions: [
      'admin.user.view',
      'admin.role.view',
      'admin.setting.view',
      'document.file.view',
      'audit.log.view',
    ],
    children: [
      {
        label: 'Users',
        icon: 'pi pi-users',
        route: '/admin/users',
        permissions: ['admin.user.view'],
      },
      {
        label: 'Roles',
        icon: 'pi pi-shield',
        route: '/admin/roles',
        permissions: ['admin.role.view'],
      },
      {
        label: 'Workflows',
        icon: 'pi pi-share-alt',
        route: '/admin/workflows',
        permissions: ['workflow.definition.view'],
      },
      {
        label: 'Custom fields',
        icon: 'pi pi-pencil',
        route: '/admin/custom-fields',
        permissions: ['admin.custom-field.manage'],
      },
      {
        label: 'Audit log',
        icon: 'pi pi-history',
        route: '/admin/audit',
        permissions: ['audit.log.view'],
      },
      {
        label: 'Settings',
        icon: 'pi pi-sliders-h',
        route: '/admin/settings',
        permissions: ['admin.setting.view'],
      },
      {
        label: 'Document types',
        icon: 'pi pi-file',
        route: '/admin/document-types',
        permissions: ['document.file.view'],
      },
    ],
  },
];
