/** The platform permission codes (backend Permissions.Platform). */
export const PLATFORM = {
  tenantView: 'platform.tenant.view',
  tenantManage: 'platform.tenant.manage',
  tenantSuspend: 'platform.tenant.suspend',
  subscriptionManage: 'platform.subscription.manage',
  featureManage: 'platform.feature.manage',
  healthView: 'platform.health.view',
  staffView: 'platform.staff.view',
  staffInvite: 'platform.staff.invite',
  staffManage: 'platform.staff.manage',
  databaseManage: 'platform.database.manage',
} as const;

/** "Something went wrong" text from whatever a call threw. */
export function messageOf(error: unknown, fallback: string): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string' && error.message) {
    return error.message;
  }
  return fallback;
}
