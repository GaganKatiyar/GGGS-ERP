import type { ApiKeyItem, AutomationConditionOperator, AutomationDelivery, AutomationTrigger, DirectoryAccount, IntegrationAdapter, IntegrationConnection } from '@gggs/shared/models';
import type { BadgeVariant } from '@gggs/shared/ui';

/** How a connector category is shown in the catalogue, in the order the catalogue lists them. */
const CATEGORIES: readonly { readonly key: string; readonly label: string; readonly icon: string }[] = [
  { key: 'Messaging', label: 'Messaging', icon: 'pi pi-comments' },
  { key: 'Identity', label: 'Identity & accounts', icon: 'pi pi-id-card' },
  { key: 'Email', label: 'Email', icon: 'pi pi-envelope' },
  { key: 'Payroll', label: 'Payroll & finance', icon: 'pi pi-wallet' },
  { key: 'Finance', label: 'Payroll & finance', icon: 'pi pi-wallet' },
  { key: 'Accounting', label: 'Payroll & finance', icon: 'pi pi-wallet' },
  { key: 'Banking', label: 'Payroll & finance', icon: 'pi pi-wallet' },
  { key: 'General', label: 'Developer', icon: 'pi pi-code' },
];

export function categoryOf(category: string): { readonly label: string; readonly icon: string } {
  return CATEGORIES.find((entry) => entry.key.toLowerCase() === category.toLowerCase()) ?? { label: category, icon: 'pi pi-box' };
}

/** A connection's state in one word, for its badge. */
export type ConnectionHealth = 'Off' | 'Not tested' | 'Healthy' | 'Failing';

export function healthOf(connection: Pick<IntegrationConnection, 'isActive' | 'lastTestSucceeded'>): ConnectionHealth {
  if (!connection.isActive) {
    return 'Off';
  }
  if (connection.lastTestSucceeded === undefined || connection.lastTestSucceeded === null) {
    return 'Not tested';
  }
  return connection.lastTestSucceeded ? 'Healthy' : 'Failing';
}

export interface CatalogueGroup {
  readonly label: string;
  readonly icon: string;
  readonly adapters: readonly (IntegrationAdapter & { readonly connected: number })[];
}

/** The catalogue, grouped by category and filtered by a search over name, category and description. */
export function catalogue(adapters: readonly IntegrationAdapter[], connections: readonly IntegrationConnection[], query: string): readonly CatalogueGroup[] {
  const term = query.trim().toLowerCase();
  const groups = new Map<string, CatalogueGroup & { adapters: (IntegrationAdapter & { connected: number })[] }>();
  for (const adapter of adapters) {
    const category = categoryOf(adapter.category);
    if (term && ![adapter.name, adapter.description, category.label].some((text) => text.toLowerCase().includes(term))) {
      continue;
    }
    const group = groups.get(category.label) ?? { label: category.label, icon: category.icon, adapters: [] };
    group.adapters.push({ ...adapter, connected: connections.filter((connection) => connection.adapterKey === adapter.key).length });
    groups.set(category.label, group);
  }
  const order = [...new Set(CATEGORIES.map((entry) => entry.label))];
  return [...groups.values()].sort((a, b) => (order.indexOf(a.label) + 1 || 99) - (order.indexOf(b.label) + 1 || 99));
}

/** Active keys that stop working within the next 30 days. */
export function expiringSoon(keys: readonly ApiKeyItem[], today: string): readonly ApiKeyItem[] {
  const limit = new Date(`${today}T00:00:00Z`);
  limit.setUTCDate(limit.getUTCDate() + 30);
  const cutoff = limit.toISOString().slice(0, 10);
  return keys.filter((key) => key.status === 'Active' && !!key.expiresOn && key.expiresOn <= cutoff);
}

export const EXPIRY_CHOICES: readonly { readonly days: number | null; readonly label: string }[] = [
  { days: 30, label: '30 days' },
  { days: 90, label: '90 days' },
  { days: 365, label: '1 year' },
  { days: null, label: 'Never' },
];

export const CONDITION_OPERATORS: readonly { readonly value: AutomationConditionOperator; readonly label: string }[] = [
  { value: 'Equals', label: 'is' },
  { value: 'NotEquals', label: 'is not' },
  { value: 'Contains', label: 'contains' },
];

/** The trigger's example values by field, for previews. */
export function sampleFields(trigger: Pick<AutomationTrigger, 'fields'> | undefined): Readonly<Record<string, string>> {
  return Object.fromEntries((trigger?.fields ?? []).map((field) => [field.key.toLowerCase(), field.sample]));
}

/** Fills "{{field}}" as the server does: without regard to case, and an unknown field becomes empty. */
export function renderTemplate(template: string, fields: Readonly<Record<string, string>>): string {
  return template.replace(/\{\{\s*([A-Za-z][A-Za-z0-9_.]*)\s*\}\}/g, (_, key: string) => fields[key.toLowerCase()] ?? '');
}

/** The text with "{{key}}" put in at the caret (or at the end), and where the caret goes after it. */
export function insertPlaceholder(text: string, key: string, caret: number | null): { readonly text: string; readonly caret: number } {
  const at = caret === null ? text.length : Math.min(caret, text.length);
  const placeholder = `{{${key}}}`;
  return { text: text.slice(0, at) + placeholder + text.slice(at), caret: at + placeholder.length };
}

/** Placeholders in the text that the trigger does not offer: they would come out empty, so the builder warns. */
export function unknownPlaceholders(text: string, trigger: Pick<AutomationTrigger, 'fields'> | undefined): readonly string[] {
  const known = new Set((trigger?.fields ?? []).map((field) => field.key.toLowerCase()));
  return [...text.matchAll(/\{\{\s*([A-Za-z][A-Za-z0-9_.]*)\s*\}\}/g)].map((match) => match[1]).filter((key) => !known.has(key.toLowerCase()));
}

/** Deliveries that need someone's eye within the last `days`: failing and still retrying, or given up on. */
export function failedRecently(deliveries: readonly AutomationDelivery[], now: Date, days = 7): readonly AutomationDelivery[] {
  const since = now.getTime() - days * 86_400_000;
  return deliveries.filter((delivery) => (delivery.status === 'Failed' || delivery.status === 'Abandoned') && new Date(delivery.createdDate).getTime() >= since);
}

/** How the Sending page words each channel. */
export interface ChannelCopy {
  readonly title: string;
  readonly description: string;
  readonly icon: string;
  readonly none: string;
  readonly fallback: string;
  readonly testLabel: string;
  readonly inputType: string;
  readonly placeholder: string;
}

export const CHANNELS: Readonly<Record<string, ChannelCopy>> = {
  email: {
    title: 'Email',
    description: 'Invitations, approvals and every other Nuts and Bolts email go out from this connection, under your own address.',
    icon: 'pi pi-envelope',
    none: 'Nuts and Bolts default',
    fallback: 'email is going out through the Nuts and Bolts default.',
    testLabel: 'Send a test to',
    inputType: 'email',
    placeholder: 'you@example.com',
  },
  sms: {
    title: 'Text messages',
    description: 'SMS or WhatsApp, for the notifications you choose.',
    icon: 'pi pi-mobile',
    none: 'Off',
    fallback: 'no text messages are being sent.',
    testLabel: 'Send a test to',
    inputType: 'tel',
    placeholder: '+91 98765 43210',
  },
};

export const ACCOUNT_STATUS_VARIANTS: Readonly<Record<string, BadgeVariant>> = {
  creating: 'primary',
  active: 'success',
  disabling: 'warning',
  disabled: 'neutral',
  needsyou: 'danger',
};

/** An account that has stopped trying says so, whatever it was trying to do. */
export function accountStatusLabel(account: Pick<DirectoryAccount, 'status' | 'needsAttention'>): string {
  return account.needsAttention ? 'Needs you' : account.status;
}

export function accountsNeedingAttention(accounts: readonly DirectoryAccount[]): readonly DirectoryAccount[] {
  return accounts.filter((account) => account.needsAttention);
}
