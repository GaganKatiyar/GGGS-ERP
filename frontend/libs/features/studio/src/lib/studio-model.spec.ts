import type { ApiKeyItem, AutomationDelivery, DirectoryAccount, IntegrationAdapter, IntegrationConnection } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { accountsNeedingAttention, accountStatusLabel, catalogue, categoryOf, expiringSoon, failedRecently, healthOf, insertPlaceholder, renderTemplate, sampleFields, unknownPlaceholders } from './studio-model';

const adapter = (key: string, category: string, name = key): IntegrationAdapter => ({ key, name, category, description: `${name} connector`, fields: [], operations: [] });

describe('studio', () => {
  const trigger = { fields: [{ key: 'employeeName', label: 'Employee name', sample: 'Priya Sharma' }, { key: 'department', label: 'Department', sample: 'Engineering' }] };

  it('flags an account that has stopped trying', () => {
    expect(accountStatusLabel({ status: 'Disabling', needsAttention: true })).toBe('Needs you');
    expect(accountStatusLabel({ status: 'Disabling', needsAttention: false })).toBe('Disabling');
    expect(accountStatusLabel({ status: 'Active', needsAttention: false })).toBe('Active');
    const account = (id: string, needsAttention: boolean) => ({ id, needsAttention }) as DirectoryAccount;
    expect(accountsNeedingAttention([account('a', true), account('b', false)]).map((row) => row.id)).toEqual(['a']);
  });

  it('previews a template as the server will fill it', () => {
    expect(renderTemplate('{{employeeName}} joined {{ DEPARTMENT }}{{nickname}}.', sampleFields(trigger))).toBe('Priya Sharma joined Engineering.');
    expect(renderTemplate('{single} stays', sampleFields(trigger))).toBe('{single} stays');
    expect(sampleFields(undefined)).toEqual({});
  });

  it('inserts a placeholder at the caret, or at the end without one', () => {
    expect(insertPlaceholder('Hello !', 'employeeName', 6)).toEqual({ text: 'Hello {{employeeName}}!', caret: 22 });
    expect(insertPlaceholder('Hello', 'department', null)).toEqual({ text: 'Hello{{department}}', caret: 19 });
    expect(insertPlaceholder('Hi', 'department', 99).text).toBe('Hi{{department}}');
  });

  it('names placeholders the trigger does not offer', () => {
    expect(unknownPlaceholders('{{employeeName}} {{nickname}} {{ salary }}', trigger)).toEqual(['nickname', 'salary']);
    expect(unknownPlaceholders('none here', trigger)).toEqual([]);
  });

  it('counts recent deliveries that need attention', () => {
    const delivery = (status: AutomationDelivery['status'], createdDate: string): AutomationDelivery => ({ id: status + createdDate, automationId: 'a', automationName: 'A', triggerCode: 't', status, attempts: 1, createdDate, parameters: {} });
    const now = new Date('2026-11-10T00:00:00Z');
    const rows = [delivery('Failed', '2026-11-09T00:00:00Z'), delivery('Abandoned', '2026-11-04T00:00:00Z'), delivery('Succeeded', '2026-11-09T00:00:00Z'), delivery('Failed', '2026-10-01T00:00:00Z')];
    expect(failedRecently(rows, now).map((row) => row.status)).toEqual(['Failed', 'Abandoned']);
  });

  it('names categories as people expect, with a fallback', () => {
    expect(categoryOf('General').label).toBe('Developer');
    expect(categoryOf('Identity').icon).toBe('pi pi-id-card');
    expect(categoryOf('Storage')).toEqual({ label: 'Storage', icon: 'pi pi-box' });
  });

  it('says how a connection is doing in one word', () => {
    expect(healthOf({ isActive: false, lastTestSucceeded: true })).toBe('Off');
    expect(healthOf({ isActive: true })).toBe('Not tested');
    expect(healthOf({ isActive: true, lastTestSucceeded: false })).toBe('Failing');
  });

  it('groups the catalogue by category, counts connections and filters by search', () => {
    const adapters = [adapter('webhook', 'General', 'Webhook'), adapter('entra-id', 'Identity', 'Microsoft Entra ID'), adapter('bank', 'Payroll', 'Bank transfer file')];
    const connections = [{ adapterKey: 'webhook' }, { adapterKey: 'webhook' }] as IntegrationConnection[];

    const all = catalogue(adapters, connections, '');
    expect(all.map((group) => group.label)).toEqual(['Identity & accounts', 'Payroll & finance', 'Developer']);
    expect(all[2]?.adapters[0]?.connected).toBe(2);

    expect(catalogue(adapters, connections, 'entra').flatMap((group) => group.adapters.map((item) => item.key))).toEqual(['entra-id']);
    expect(catalogue(adapters, connections, 'finance').flatMap((group) => group.adapters.map((item) => item.key))).toEqual(['bank']);
  });

  it('lists active keys expiring within 30 days', () => {
    const key = (id: string, status: ApiKeyItem['status'], expiresOn?: string) => ({ id, status, expiresOn }) as ApiKeyItem;
    const keys = [key('soon', 'Active', '2026-11-20'), key('later', 'Active', '2027-03-01'), key('never', 'Active'), key('gone', 'Revoked', '2026-11-05')];
    expect(expiringSoon(keys, '2026-11-01').map((item) => item.id)).toEqual(['soon']);
  });
});
