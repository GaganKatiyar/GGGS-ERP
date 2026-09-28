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
  readonly mfaEnabled: boolean;
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

export interface Role {
  readonly id: string;
  readonly name: string;
  readonly description?: string;
  readonly isSystem: boolean;
  readonly permissions: readonly PermissionAssignment[];
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
