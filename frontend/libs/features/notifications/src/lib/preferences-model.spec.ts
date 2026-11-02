import type { NotificationPreference } from '@gggs/shared/notifications';
import { describe, expect, it } from 'vitest';
import { changedChoices, isSwitchedOff, moduleList, moduleName, toRows, type PreferenceRow } from './preferences-model';

const preference = (code: string, extra: Partial<NotificationPreference> = {}): NotificationPreference => ({
  code,
  name: code,
  module: code.slice(0, code.indexOf('.')),
  templateSendsEmail: true,
  templateSendsInApp: true,
  templateSendsSms: false,
  emailEnabled: true,
  inAppEnabled: true,
  smsEnabled: true,
  isMandatory: false,
  ...extra,
});

describe('preferences', () => {
  it('saves a text-message choice, and counts it as off only where texts are sent', () => {
    const rows = toRows([preference('workflow.approved', { templateSendsSms: true }), preference('asset.assigned')]);
    const edited = rows.map((row) => ({ ...row, sms: false }));
    expect(changedChoices(edited)).toEqual([
      { code: 'workflow.approved', emailEnabled: true, inAppEnabled: true, smsEnabled: false },
      { code: 'asset.assigned', emailEnabled: true, inAppEnabled: true, smsEnabled: false },
    ]);
    expect(isSwitchedOff(edited[0] as PreferenceRow)).toBe(true);
    expect(isSwitchedOff(edited[1] as PreferenceRow)).toBe(false);
  });

  it('names areas as people call them', () => {
    expect(moduleName('workflow')).toBe('Approvals');
    expect(moduleName('payroll')).toBe('Payroll');
  });

  it('sends only the rows whose switches moved, never a mandatory one', () => {
    const rows = toRows([preference('workflow.approved'), preference('payroll.payslip-published'), preference('identity.user-invited', { isMandatory: true })]);
    const edited = rows.map((row) => (row.code === 'workflow.approved' || row.isMandatory ? { ...row, email: false } : row));
    expect(changedChoices(edited)).toEqual([{ code: 'workflow.approved', emailEnabled: false, inAppEnabled: true, smsEnabled: true }]);
  });

  it('counts a row as switched off only for a channel its template actually sends', () => {
    const emailOnly = toRows([preference('payroll.payslip-published', { templateSendsInApp: false, inAppEnabled: false })])[0] as PreferenceRow;
    expect(isSwitchedOff(emailOnly)).toBe(false);
    expect(isSwitchedOff({ ...emailOnly, email: false })).toBe(true);
  });

  it('lists modules alphabetically by name, with their switched-off counts', () => {
    const rows = toRows([preference('workflow.approved', { emailEnabled: false }), preference('asset.assigned')]);
    expect(moduleList(rows)).toEqual([
      { module: 'workflow', label: 'Approvals', off: 1 },
      { module: 'asset', label: 'Assets', off: 0 },
    ]);
  });
});
