import type { NotificationPreference, NotificationPreferenceChoice } from '@gggs/shared/notifications';

/** An event code's area, as people call it. */
const MODULE_NAMES: Readonly<Record<string, string>> = {
  workflow: 'Approvals',
  identity: 'Account',
  insight: 'Reports',
  policy: 'Policies',
  document: 'Documents',
  contract: 'Contracts',
  project: 'Projects',
  resource: 'Resourcing',
  timesheet: 'Timesheets',
  separation: 'Exits',
  reward: 'Rewards',
  benefit: 'Benefits',
  asset: 'Assets',
  expense: 'Expenses',
  loan: 'Loans',
};

export function moduleName(module: string): string {
  return MODULE_NAMES[module] ?? module.charAt(0).toUpperCase() + module.slice(1);
}

/** The choice as edited on the page, before it is saved. */
export interface PreferenceRow extends NotificationPreference {
  readonly email: boolean;
  readonly inApp: boolean;
  readonly sms: boolean;
}

export function toRows(preferences: readonly NotificationPreference[]): readonly PreferenceRow[] {
  return preferences.map((preference) => ({ ...preference, email: preference.emailEnabled, inApp: preference.inAppEnabled, sms: preference.smsEnabled }));
}

/** The rows whose switches moved: what a save sends. Mandatory rows never change. */
export function changedChoices(rows: readonly PreferenceRow[]): readonly NotificationPreferenceChoice[] {
  return rows
    .filter((row) => !row.isMandatory && (row.email !== row.emailEnabled || row.inApp !== row.inAppEnabled || row.sms !== row.smsEnabled))
    .map((row) => ({ code: row.code, emailEnabled: row.email, inAppEnabled: row.inApp, smsEnabled: row.sms }));
}

/** A row is "switched off" when a channel its template sends has been turned off. */
export function isSwitchedOff(row: PreferenceRow): boolean {
  return (row.templateSendsEmail && !row.email) || (row.templateSendsInApp && !row.inApp) || (row.templateSendsSms && !row.sms);
}

/** Modules in the order the left-hand list shows them, each with how many of its events are switched off. */
export function moduleList(rows: readonly PreferenceRow[]): readonly { module: string; label: string; off: number }[] {
  const modules = new Map<string, number>();
  for (const row of rows) {
    modules.set(row.module, (modules.get(row.module) ?? 0) + (isSwitchedOff(row) ? 1 : 0));
  }
  return [...modules]
    .map(([module, off]) => ({ module, label: moduleName(module), off }))
    .sort((a, b) => a.label.localeCompare(b.label));
}
