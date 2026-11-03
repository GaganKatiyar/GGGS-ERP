import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AboutInfo,
  ApiKeyItem,
  CreateApiKeyRequest,
  CreatedApiKey,
  PermissionAssignment,
  RoleMember,
  UserAccessDetail,
  UserAccessSummary,
  RoleAutoAssign,
  AutomaticRoleGrant,
  CompleteInvitationRequest,
  CreateRoleRequest,
  GrantPermissionRequest,
  InviteUserRequest,
  InviteUserResult,
  PagedResult,
  PermissionCatalogItem,
  Role,
  TenantSettingItem,
  UpsertTenantSettingRequest,
  UserListItem,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * Users, roles, the permission catalog and tenant settings, over the API's
 * `/api/v1/{users,roles,permissions,settings}` routes - the first three from the Identity module,
 * the last from Platform. One client for everything "administration" touches.
 *
 * Lives in `shared`, not inside the admin feature, for two independent reasons:
 *  - `completeInvitation` is called by `apps/hrms-web`'s own complete-invitation page, which sits
 *    outside every feature (the invitee has no session, and the page is not "admin" - the person
 *    using it may never see an admin screen in their life).
 *  - Nx's own lint rule refuses a static import of a lazily-loaded library, and `feature-admin` is
 *    lazy-loaded from `app.routes.ts`. A page outside that feature importing straight from it
 *    would defeat the lazy loading, exactly as `@gggs/shared/org-reference` exists so employees
 *    does not have to statically import the organization feature for the same lookups.
 */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  // ----- API keys (Studio): machine accounts holding chosen roles -----

  apiKeys(): Promise<readonly ApiKeyItem[]> {
    return firstValueFrom(this.http.get<readonly ApiKeyItem[]>('/api/v1/api-keys'));
  }

  createApiKey(body: CreateApiKeyRequest): Promise<CreatedApiKey> {
    return firstValueFrom(this.http.post<CreatedApiKey>('/api/v1/api-keys', body));
  }

  async revokeApiKey(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/api-keys/${id}/revoke`, {}));
  }

  listUsers(query: {
    page: number;
    pageSize: number;
    search?: string;
    status?: string;
  }): Promise<PagedResult<UserListItem>> {
    const params: Record<string, string> = {
      page: String(query.page),
      pageSize: String(query.pageSize),
    };

    if (query.search) {
      params['search'] = query.search;
    }

    if (query.status) {
      params['status'] = query.status;
    }

    return firstValueFrom(
      this.http.get<PagedResult<UserListItem>>('/api/v1/users', { params }),
    );
  }

  inviteUser(request: InviteUserRequest): Promise<InviteUserResult> {
    return firstValueFrom(
      this.http.post<InviteUserResult>('/api/v1/users/invite', request),
    );
  }

  disableUser(userId: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/users/${userId}/disable`, {}),
    );
  }

  enableUser(userId: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/users/${userId}/enable`, {}),
    );
  }

  unlockUser(userId: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/users/${userId}/unlock`, {}),
    );
  }

  getUserRoles(userId: string): Promise<readonly string[]> {
    return firstValueFrom(
      this.http.get<readonly string[]>(`/api/v1/users/${userId}/roles`),
    );
  }

  /** Roles the org chart gives the user - shown beside the editor, never changed by it. */
  getAutomaticRoles(userId: string): Promise<readonly AutomaticRoleGrant[]> {
    return firstValueFrom(
      this.http.get<readonly AutomaticRoleGrant[]>(`/api/v1/users/${userId}/automatic-roles`),
    );
  }

  setUserRoles(userId: string, roleIds: readonly string[]): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`/api/v1/users/${userId}/roles`, { roleIds }),
    );
  }

  /** Answers the same whether or not the account exists. */
  forgotPassword(request: { readonly tenantCode?: string; readonly email: string }): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/auth/forgot-password', request));
  }

  /** This browser stays signed in; every other sign-in on the account ends. */
  changeOwnPassword(request: { readonly currentPassword: string; readonly newPassword: string }): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/users/me/password', request));
  }

  resetPassword(request: CompleteInvitationRequest): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/auth/reset-password', request));
  }

  completeInvitation(request: CompleteInvitationRequest): Promise<void> {
    return firstValueFrom(
      this.http.post<void>('/api/v1/auth/complete-invitation', request),
    );
  }

  listRoles(): Promise<readonly Role[]> {
    return firstValueFrom(this.http.get<readonly Role[]>('/api/v1/roles'));
  }

  createRole(request: CreateRoleRequest): Promise<Role> {
    return firstValueFrom(this.http.post<Role>('/api/v1/roles', request));
  }

  grantPermission(roleId: string, request: GrantPermissionRequest): Promise<Role> {
    return firstValueFrom(
      this.http.post<Role>(`/api/v1/roles/${roleId}/permissions`, request),
    );
  }

  /** Who the org chart gives this role to; the grants follow at once. */
  setRoleAutoAssign(roleId: string, autoAssign: RoleAutoAssign): Promise<Role> {
    return firstValueFrom(
      this.http.put<Role>(`/api/v1/roles/${roleId}/auto-assign`, { autoAssign }),
    );
  }

  /** Saves a role's whole permission set from the grid; its holders have the change at once. */
  replaceRolePermissions(roleId: string, permissions: readonly PermissionAssignment[]): Promise<Role> {
    return firstValueFrom(
      this.http.put<Role>(`/api/v1/roles/${roleId}/permissions`, { permissions }),
    );
  }

  roleMembers(roleId: string): Promise<readonly RoleMember[]> {
    return firstValueFrom(this.http.get<readonly RoleMember[]>(`/api/v1/roles/${roleId}/members`));
  }

  /** Every account, its roles and the permissions they add up to. */
  accessUsers(): Promise<readonly UserAccessSummary[]> {
    return firstValueFrom(this.http.get<readonly UserAccessSummary[]>('/api/v1/access/users'));
  }

  userAccess(userId: string): Promise<UserAccessDetail> {
    return firstValueFrom(this.http.get<UserAccessDetail>(`/api/v1/users/${userId}/access`));
  }

  revokePermission(roleId: string, permissionCode: string): Promise<Role> {
    return firstValueFrom(
      this.http.delete<Role>(
        `/api/v1/roles/${roleId}/permissions/${encodeURIComponent(permissionCode)}`,
      ),
    );
  }

  listPermissionCatalog(): Promise<readonly PermissionCatalogItem[]> {
    return firstValueFrom(
      this.http.get<readonly PermissionCatalogItem[]>('/api/v1/permissions'),
    );
  }

  getAbout(): Promise<AboutInfo> {
    return firstValueFrom(this.http.get<AboutInfo>('/api/v1/about'));
  }

  listSettings(): Promise<readonly TenantSettingItem[]> {
    return firstValueFrom(
      this.http.get<readonly TenantSettingItem[]>('/api/v1/settings'),
    );
  }

  upsertSetting(
    module: string,
    key: string,
    request: UpsertTenantSettingRequest,
  ): Promise<TenantSettingItem> {
    return firstValueFrom(
      this.http.put<TenantSettingItem>(
        `/api/v1/settings/${encodeURIComponent(module)}/${encodeURIComponent(key)}`,
        request,
      ),
    );
  }
}
