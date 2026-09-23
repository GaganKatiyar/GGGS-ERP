import { inject } from '@angular/core';
import { Router, type CanActivateFn, type Route } from '@angular/router';
import { PermissionStore } from './permission.store';

/** Route metadata read by {@link permissionGuard}. */
export interface PermissionRouteData {
  /** Permission codes required to enter the route. */
  permissions?: readonly string[];
  /** Whether any one code suffices, or all are required. Defaults to `any`. */
  permissionMode?: 'any' | 'all';
  /** Feature the tenant's plan must include. */
  feature?: string;
}

/**
 * Blocks navigation to a route the user cannot use.
 *
 * Two different refusals, deliberately routed to different places: a missing permission is a
 * "not allowed" page, while a missing feature is a plan limitation and goes to an upgrade page.
 * Telling an admin to contact support when the real answer is "this module is not in your plan"
 * wastes everyone's time.
 */
export const permissionGuard: CanActivateFn = (route) => {
  const permissions = inject(PermissionStore);
  const router = inject(Router);

  const data = (route.data ?? {}) as PermissionRouteData;
  const required = data.permissions ?? [];

  if (data.feature !== undefined && !permissions.hasFeature(data.feature)) {
    return router.createUrlTree(['/feature-unavailable'], {
      queryParams: { feature: data.feature },
    });
  }

  if (required.length === 0) {
    return true;
  }

  const permitted =
    data.permissionMode === 'all'
      ? permissions.hasAll(required)
      : permissions.hasAny(required);

  return permitted ? true : router.createUrlTree(['/forbidden']);
};

/** Helper so feature routes declare their requirements in one readable place. */
export function withPermissions(
  permissions: readonly string[],
  options: { mode?: 'any' | 'all'; feature?: string } = {},
): Pick<Route, 'canActivate' | 'data'> {
  return {
    canActivate: [permissionGuard],
    data: {
      permissions,
      permissionMode: options.mode ?? 'any',
      feature: options.feature,
    } satisfies PermissionRouteData,
  };
}
