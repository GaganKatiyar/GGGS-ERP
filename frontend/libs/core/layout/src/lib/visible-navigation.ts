import { Injectable, computed, inject } from '@angular/core';
import { SessionStore } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { NAVIGATION, filterNavigation, isBuilt, type NavItem } from './navigation';

/** What decides someone's menu: the permissions they hold, the tenant's plan, and whether they are an employee. */
export interface NavigationAccess {
  readonly hasPermission: (code: string) => boolean;
  readonly hasFeature: (feature: string) => boolean;
  readonly hasEmployee: boolean;
}

/** Whether one entry is shown to someone with this access - the rule the rail, the launcher and the access screens share. */
export function canSee(item: NavItem, access: NavigationAccess): boolean {
  if (!isBuilt(item)) {
    return false;
  }

  if (item.feature !== undefined && !access.hasFeature(item.feature)) {
    return false;
  }

  if (item.needsEmployee === true && !access.hasEmployee) {
    return false;
  }

  return item.permissions === undefined || item.permissions.some((code) => access.hasPermission(code));
}

/** The menu someone with this access sees. */
export function navigationFor(access: NavigationAccess, items: readonly NavItem[] = NAVIGATION): NavItem[] {
  return filterNavigation(items, (item) => canSee(item, access));
}

/** The first of a page's permissions this access holds - the one that puts the page in the menu. */
export function unlockingPermission(item: NavItem, hasPermission: (code: string) => boolean): string | undefined {
  return item.permissions?.find((code) => hasPermission(code));
}

/**
 * The navigation the signed-in user can actually reach, worked out once for the rail, the launcher
 * and the Settings home, so they can never disagree about what someone may open.
 */
@Injectable({ providedIn: 'root' })
export class VisibleNavigation {
  private readonly permissions = inject(PermissionStore);
  private readonly session = inject(SessionStore);

  readonly items = computed(() =>
    navigationFor({
      hasPermission: (code) => this.permissions.has(code),
      hasFeature: (feature) => this.permissions.hasFeature(feature),
      hasEmployee: !!this.session.user()?.employeeId,
    }),
  );
}
