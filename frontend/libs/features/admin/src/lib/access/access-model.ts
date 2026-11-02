import { navigationFor, unlockingPermission, type NavItem } from '@gggs/core/layout';
import type { DataScope, PermissionAssignment, PermissionCatalogItem } from '@gggs/shared/models';

/** Every reach, narrowest first, with the short label a matrix cell shows and the long one a select shows. */
export const REACHES: readonly { readonly scope: DataScope; readonly short: string; readonly long: string }[] = [
  { scope: 'Own', short: 'Own', long: 'Own records' },
  { scope: 'DirectReports', short: 'Reports', long: 'Direct reports' },
  { scope: 'AllReports', short: 'All reports', long: 'All reports (whole team below)' },
  { scope: 'Department', short: 'Dept', long: 'Their department' },
  { scope: 'Branch', short: 'Branch', long: 'Their branch' },
  { scope: 'LegalEntity', short: 'Entity', long: 'Their company' },
  { scope: 'BusinessUnit', short: 'BU', long: 'Their business unit' },
  { scope: 'Tenant', short: 'All', long: 'Everyone' },
];

export function reachLabel(scope: DataScope, form: 'short' | 'long' = 'long'): string {
  const reach = REACHES.find((candidate) => candidate.scope === scope);
  return reach ? reach[form] : scope;
}

/** "self-service" → "Self service", "master-data" → "Master data". */
export function humanize(word: string): string {
  const spaced = word.replace(/[-_]/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** A permission as people read it: its module, and "Resource · Action" ("Profile · View"). */
export function permissionLabel(code: string): { readonly module: string; readonly label: string } {
  const [module = code, resource = '', action = ''] = code.split('.');
  return { module: humanize(module), label: [resource, action].filter(Boolean).map(humanize).join(' · ') || code };
}

export interface ModuleGroup {
  readonly module: string;
  readonly label: string;
  readonly items: readonly PermissionCatalogItem[];
}

/** The catalog grouped by module, both sorted by what people read. */
export function groupByModule(catalog: readonly PermissionCatalogItem[]): readonly ModuleGroup[] {
  const groups = new Map<string, PermissionCatalogItem[]>();
  for (const item of catalog) {
    groups.set(item.module, [...(groups.get(item.module) ?? []), item]);
  }

  return [...groups.entries()]
    .map(([module, items]) => ({
      module,
      label: humanize(module),
      items: [...items].sort((a, b) => permissionLabel(a.code).label.localeCompare(permissionLabel(b.code).label)),
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

export interface PermissionDiff {
  readonly added: readonly string[];
  readonly removed: readonly string[];
  readonly changed: readonly string[];
  readonly count: number;
}

/** What the grid's save would change, for its save bar. */
export function diffPermissions(original: readonly PermissionAssignment[], edited: ReadonlyMap<string, DataScope>): PermissionDiff {
  const before = new Map(original.map((permission) => [permission.permissionCode.toLowerCase(), permission.scope]));
  const after = new Map([...edited].map(([code, scope]) => [code.toLowerCase(), scope]));

  const added = [...after.keys()].filter((code) => !before.has(code)).sort();
  const removed = [...before.keys()].filter((code) => !after.has(code)).sort();
  const changed = [...after.keys()].filter((code) => before.has(code) && before.get(code) !== after.get(code)).sort();
  return { added, removed, changed, count: added.length + removed.length + changed.length };
}

/** A page in someone's menu, with the module it sits in and the permission that puts it there. */
export interface MenuPage {
  readonly group: NavItem;
  readonly page: NavItem;
  readonly unlockedBy?: string;
}

/**
 * The menu pages a set of permissions opens, by the same rule as the shell (`navigationFor`). A
 * module that is a page itself (Dashboard, Analytics) is its own single page.
 */
export function menuPages(codes: ReadonlySet<string>, hasFeature: (feature: string) => boolean, hasEmployee: boolean): readonly MenuPage[] {
  const has = (code: string) => codes.has(code.toLowerCase());
  return navigationFor({ hasPermission: has, hasFeature, hasEmployee }).flatMap((group) =>
    (group.children?.length ? group.children : [group]).map((page) => ({ group, page, unlockedBy: unlockingPermission(page, has) })),
  );
}

/** The codes as a lower-cased set, as the menu rule compares them. */
export function codeSet(codes: Iterable<string>): ReadonlySet<string> {
  return new Set([...codes].map((code) => code.toLowerCase()));
}
