/**
 * Users, roles and permissions, mirroring the DTOs in
 * `Gggs.Identity.Api.Endpoints.{UserEndpoints,RoleEndpoints}`.
 */
import type { DataScope } from './session.models';

export type UserStatus = 'Invited' | 'Active' | 'Locked' | 'Disabled';

export type UserType = 'TenantUser' | 'PlatformUser' | 'ApiClient';

export interface UserListItem {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly status: UserStatus;
  readonly userType: UserType;
  readonly employeeId?: string;
  readonly lastLoginDate?: string;
}

export interface InviteUserRequest {
  readonly email: string;
  readonly displayName: string;
  readonly employeeId?: string;
  readonly roleIds: readonly string[];
}

/**
 * The invitation link's own ingredients, handed back to the inviter rather than emailed - there
 * is no Notification service yet (docs/runbooks/local-dev-login.md and the architecture roadmap
 * both track this), so passing the link on is the administrator's own job for now.
 */
export interface InviteUserResult {
  readonly userId: string;
  readonly invitationToken: string;
  readonly expiresDate: string;
}

export interface CompleteInvitationRequest {
  readonly tenantCode: string;
  readonly userId: string;
  readonly token: string;
  readonly newPassword: string;
}

export interface PermissionAssignment {
  readonly permissionCode: string;
  readonly scope: DataScope;
}

/** Who the org chart gives a role to without anyone granting it. */
export type RoleAutoAssign = 'None' | 'PeopleManagers' | 'DepartmentHeads';

export interface Role {
  readonly id: string;
  readonly name: string;
  readonly description?: string;
  readonly isSystem: boolean;
  readonly permissions: readonly PermissionAssignment[];
  readonly autoAssign: RoleAutoAssign;
}

/** A role grant as the access screens show it: which role, given how, anchored where, from when. */
export interface AccessGrant {
  readonly userId: string;
  readonly roleId: string;
  readonly roleName: string;
  readonly isAutomatic: boolean;
  readonly scopeType?: string;
  readonly scopeId?: string;
  readonly scopeName?: string;
  readonly validFrom: string;
  readonly validTo?: string;
}

/** One account on the access overview, with every permission its roles add up to. */
export interface UserAccessSummary {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly status: string;
  readonly employeeId?: string;
  readonly employeeName?: string;
  readonly lastLoginDate?: string;
  readonly roles: readonly AccessGrant[];
  readonly permissionCodes: readonly string[];
}

/** A permission someone holds, how far it reaches, and the roles it comes through. */
export interface EffectivePermission {
  readonly code: string;
  readonly scope: DataScope;
  readonly scopeId?: string;
  readonly via: readonly string[];
  readonly isSensitive: boolean;
}

export interface UserAccessDetail {
  readonly user: UserAccessSummary;
  readonly permissions: readonly EffectivePermission[];
}

/** Someone holding a role, for the role's Members tab. */
export interface RoleMember {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string;
  readonly isAutomatic: boolean;
  readonly scopeName?: string;
  readonly validFrom: string;
  readonly validTo?: string;
}

/** A role a user holds because of the org chart; a department head's is anchored to the department. */
export interface AutomaticRoleGrant {
  readonly roleId: string;
  readonly roleName: string;
  readonly departmentId?: string;
  readonly departmentName?: string;
}

export interface CreateRoleRequest {
  readonly name: string;
  readonly description?: string;
}

export interface GrantPermissionRequest {
  readonly permissionCode: string;
  readonly scope: DataScope;
}

/** One entry in the permission catalog - the role editor's own source of truth for what exists. */
export interface PermissionCatalogItem {
  readonly code: string;
  readonly module: string;
  readonly resource: string;
  readonly action: string;
  readonly isSensitive: boolean;
}

/**
 * One tenant setting: a module-scoped key holding a JSON value, mirroring
 * `Gggs.Platform.Api.Endpoints.TenantSettingEndpoints`.
 *
 * No module has a defined settings schema yet, so the value is opaque JSON rather than typed
 * fields - the server validates only that it parses, not what shape it takes.
 */
export interface TenantSettingItem {
  readonly module: string;
  readonly key: string;
  readonly valueJson: string;
}

/** The running deployment, mirroring `Gggs.Modules.Platform.Endpoints.AboutResponse` (`/api/v1/about`). */
export interface AboutInfo {
  readonly product: string;
  readonly version: string;
  /** The source revision the API was built from, when the build recorded one. */
  readonly commit: string | null;
  readonly environment: string;
  readonly runtime: string;
  readonly operatingSystem: string;
  readonly startedAt: string;
  readonly serverTime: string;
  readonly databaseReachable: boolean;
  /** Module names in composition order, e.g. `platform`, `identity`, `employee`. */
  readonly modules: readonly string[];
}

export interface UpsertTenantSettingRequest {
  readonly valueJson: string;
}

/** A key an outside system calls the API with; it acts as a machine account holding these roles. */
export interface ApiKeyItem {
  readonly id: string;
  readonly name: string;
  /** The key's public part, shown to tell keys apart; never the secret. */
  readonly prefix: string;
  readonly roles: readonly { readonly id: string; readonly name: string }[];
  readonly createdBy?: string;
  readonly createdDate: string;
  readonly lastUsedAt?: string;
  readonly expiresOn?: string;
  readonly revokedAt?: string;
  readonly status: 'Active' | 'Expired' | 'Revoked';
}

export interface CreateApiKeyRequest {
  readonly name: string;
  readonly roleIds: readonly string[];
  /** Empty for a key that never expires. */
  readonly expiresInDays?: number | null;
}

/** The new key with its full value: shown once, never again. */
export interface CreatedApiKey {
  readonly key: ApiKeyItem;
  readonly value: string;
}
