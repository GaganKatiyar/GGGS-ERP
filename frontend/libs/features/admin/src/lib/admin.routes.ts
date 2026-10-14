import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { InviteUserPage } from './users/invite-user-page';
import { UserListPage } from './users/user-list-page';
import { UserRolesPage } from './users/user-roles-page';
import { RoleDetailPage } from './roles/role-detail-page';
import { RoleListPage } from './roles/role-list-page';
import { TenantSettingsPage } from './settings/tenant-settings-page';
import { AuditLogPage } from './audit/audit-log-page';
import { MasterDataPage } from './master-data/master-data-page';
import { NotificationTemplatesPage } from './notifications/notification-templates-page';
import { WorkflowDefinitionsPage } from './workflows/workflow-definitions-page';

/** Lazily loaded by the shell at `/admin/*`; see `NAVIGATION`'s "Administration" entry. */
export const ADMIN_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'users' },
  {
    path: 'users',
    component: UserListPage,
    title: 'Users',
    ...withPermissions(['admin.user.view']),
  },
  {
    path: 'users/invite',
    component: InviteUserPage,
    title: 'Invite user',
    ...withPermissions(['admin.user.invite']),
  },
  {
    path: 'users/:id/roles',
    component: UserRolesPage,
    title: 'Manage roles',
    ...withPermissions(['admin.user.manage']),
  },
  {
    path: 'roles',
    component: RoleListPage,
    title: 'Roles',
    ...withPermissions(['admin.role.view']),
  },
  {
    path: 'roles/:id',
    component: RoleDetailPage,
    title: 'Role',
    ...withPermissions(['admin.role.view']),
  },
  {
    path: 'settings',
    component: TenantSettingsPage,
    title: 'Settings',
    ...withPermissions(['admin.setting.view']),
  },
  {
    path: 'audit',
    component: AuditLogPage,
    title: 'Audit log',
    ...withPermissions(['audit.log.view']),
  },
  {
    path: 'workflows',
    component: WorkflowDefinitionsPage,
    title: 'Workflows',
    ...withPermissions(['workflow.definition.view']),
  },
  {
    path: 'notifications',
    component: NotificationTemplatesPage,
    title: 'Notifications',
    ...withPermissions(['notification.template.view']),
  },
  {
    path: 'master-data',
    component: MasterDataPage,
    title: 'Master data',
    ...withPermissions(['master-data.lookup.manage']),
  },
];
