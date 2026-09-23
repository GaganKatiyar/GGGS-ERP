/** A permission as granted to the signed-in user, mirroring `PermissionGrant` on the server. */
export interface PermissionGrant {
  readonly code: string;
  readonly scope: DataScope;
  /** Org unit the grant was anchored to, when it was anchored at all. */
  readonly scopeId?: string;
}

/** How far a permission reaches. Kept in step with `DataScope` in the backend. */
export type DataScope =
  | 'Own'
  | 'DirectReports'
  | 'AllReports'
  | 'Department'
  | 'Branch'
  | 'LegalEntity'
  | 'BusinessUnit'
  | 'Tenant';

/**
 * Everything the shell needs about the signed-in user, served by the BFF.
 *
 * There is no token here on purpose: the BFF keeps tokens server-side and the browser holds only
 * an httpOnly cookie, so a script injected into the page has nothing to steal.
 */
export interface UserSession {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string;
  /** Present when the user is an employee; platform staff and API clients are not. */
  readonly employeeId?: string;
  readonly tenant: TenantContext;
  readonly permissions: readonly PermissionGrant[];
  /** Feature codes the tenant's plan enables; drives navigation and route guards. */
  readonly features: readonly string[];
  readonly locale: string;
  readonly timeZone: string;
  /** When the current session expires, so the UI can warn before work is lost. */
  readonly expiresAt: string;
  /** True when a platform support user is acting inside the tenant under a grant. */
  readonly isImpersonating: boolean;
  /** Tenants this user can switch between; one entry means the switcher stays hidden. */
  readonly availableTenants: readonly TenantSummary[];
}

export interface TenantContext {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly countryCode: string;
  readonly defaultCurrency: string;
  readonly defaultTimeZone: string;
  readonly branding?: TenantBranding;
}

export interface TenantSummary {
  readonly id: string;
  readonly code: string;
  readonly name: string;
}

/** Tenant branding, applied as CSS custom properties. */
export interface TenantBranding {
  readonly logoUrl?: string;
  /** Primary brand colour; contrast-checked before it is applied. */
  readonly primaryColor?: string;
}
