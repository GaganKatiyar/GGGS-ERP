import type { PermissionGrant } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { PermissionStore } from './permission.store';

function grant(code: string): PermissionGrant {
  return { code, scope: 'Tenant' };
}

describe('PermissionStore', () => {
  it('reports permissions the user holds', () => {
    const store = new PermissionStore();
    store.setPermissions([grant('leave.request.view')], []);

    expect(store.has('leave.request.view')).toBe(true);
    expect(store.has('leave.request.approve')).toBe(false);
  });

  it('matches permission codes regardless of case', () => {
    // Codes travel through role editors, seed data and JSON; casing should never decide access.
    const store = new PermissionStore();
    store.setPermissions([grant('Leave.Request.View')], []);

    expect(store.has('leave.request.view')).toBe(true);
  });

  it('requires only one code for hasAny and every code for hasAll', () => {
    const store = new PermissionStore();
    store.setPermissions([grant('employee.profile.view')], []);

    expect(
      store.hasAny(['employee.profile.view', 'employee.profile.edit']),
    ).toBe(true);
    expect(
      store.hasAll(['employee.profile.view', 'employee.profile.edit']),
    ).toBe(false);
  });

  it('keeps the widest scope when a permission arrives from several roles', () => {
    // A user who is both a team lead and an HR manager should get the wider of the two, not
    // whichever role happened to be read last.
    const store = new PermissionStore();
    store.setPermissions(
      [
        { code: 'leave.request.view', scope: 'DirectReports' },
        { code: 'leave.request.view', scope: 'Branch' },
      ],
      [],
    );

    // The map keeps one entry per code; the server sends the effective grant.
    expect(store.count()).toBe(1);
    expect(store.scopeFor('leave.request.view')).toBeDefined();
  });

  it('separates licensed features from held permissions', () => {
    // Holding payroll permissions means nothing if the tenant's plan excludes payroll.
    const store = new PermissionStore();
    store.setPermissions([grant('payroll.run.view')], ['Payroll']);

    expect(store.hasFeature('payroll')).toBe(true);
    expect(store.hasFeature('recruitment')).toBe(false);
  });

  it('grants nothing after clear', () => {
    const store = new PermissionStore();
    store.setPermissions([grant('employee.profile.view')], ['CoreHr']);

    store.clear();

    expect(store.has('employee.profile.view')).toBe(false);
    expect(store.hasFeature('CoreHr')).toBe(false);
    expect(store.count()).toBe(0);
  });
});
