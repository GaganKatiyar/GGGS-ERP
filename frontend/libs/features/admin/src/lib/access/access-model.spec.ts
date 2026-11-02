import type { PermissionCatalogItem } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { codeSet, diffPermissions, groupByModule, menuPages, permissionLabel, reachLabel } from './access-model';

const item = (code: string): PermissionCatalogItem => {
  const [module, resource, action] = code.split('.');
  return { code, module: module!, resource: resource!, action: action!, isSensitive: false };
};

describe('labels', () => {
  it('reads a permission code as module and "Resource · Action"', () => {
    expect(permissionLabel('employee.profile.view')).toEqual({ module: 'Employee', label: 'Profile · View' });
    expect(permissionLabel('master-data.lookup.manage')).toEqual({ module: 'Master data', label: 'Lookup · Manage' });
    expect(permissionLabel('self.expense-claim.create').label).toBe('Expense claim · Create');
  });

  it('names each reach in a short and a long form', () => {
    expect(reachLabel('DirectReports')).toBe('Direct reports');
    expect(reachLabel('Tenant', 'short')).toBe('All');
  });
});

describe('groupByModule', () => {
  it('groups the catalog by module, sorted by what people read', () => {
    const groups = groupByModule([item('payroll.run.view'), item('employee.profile.view'), item('employee.bank.view')]);
    expect(groups.map((group) => group.label)).toEqual(['Employee', 'Payroll']);
    expect(groups[0]!.items.map((permission) => permission.code)).toEqual(['employee.bank.view', 'employee.profile.view']);
  });
});

describe('diffPermissions', () => {
  it('counts what a save would add, remove and re-reach', () => {
    const diff = diffPermissions(
      [{ permissionCode: 'leave.request.view', scope: 'DirectReports' }, { permissionCode: 'employee.profile.view', scope: 'Tenant' }],
      new Map([['leave.request.view', 'Department'], ['admin.role.view', 'Tenant']] as const),
    );
    expect(diff).toEqual({ added: ['admin.role.view'], removed: ['employee.profile.view'], changed: ['leave.request.view'], count: 3 });
  });

  it('is empty when nothing changed', () => {
    expect(diffPermissions([{ permissionCode: 'Leave.Request.View', scope: 'Own' }], new Map([['leave.request.view', 'Own']] as const)).count).toBe(0);
  });
});

describe('menuPages', () => {
  const always = () => true;

  it('opens the pages a permission set reaches, each with the permission that opens it', () => {
    const pages = menuPages(codeSet(['self.profile.view', 'self.leave.view']), always, true);
    const profile = pages.find((entry) => entry.page.route === '/me/profile');
    expect(profile?.group.label).toBe('My Space');
    expect(profile?.unlockedBy).toBe('self.profile.view');
    expect(pages.some((entry) => entry.page.route === '/dashboard')).toBe(true);
    expect(pages.some((entry) => entry.page.route === '/admin/users')).toBe(false);
  });

  it('leaves out self-service for an account without an employee record, and modules the plan lacks', () => {
    expect(menuPages(codeSet(['self.profile.view']), always, false).some((entry) => entry.page.route === '/me/profile')).toBe(false);
    expect(menuPages(codeSet(['self.leave.view']), (feature) => feature !== 'Time', true).some((entry) => entry.page.route === '/me/leave')).toBe(false);
  });
});
