import type { CanDeactivateFn, Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { InviteUserPage } from './users/invite-user-page';
import { UserListPage } from './users/user-list-page';
import { UserRolesPage } from './users/user-roles-page';
import { RoleDetailPage } from './roles/role-detail-page';
import { RoleListPage } from './roles/role-list-page';
import { AccessOverviewPage } from './access/access-overview-page';
import { UserAccessPage } from './access/user-access-page';
import { TenantSettingsPage } from './settings/tenant-settings-page';
import { AuditLogPage } from './audit/audit-log-page';
import { MasterDataPage } from './master-data/master-data-page';
import { NotificationTemplatesPage } from './notifications/notification-templates-page';
import { WorkflowDefinitionsPage } from './workflows/workflow-definitions-page';
import { AboutPage } from './about/about-page';

/** Leaving the role grid with unsaved edits asks first: a save there changes what people may do. */
const confirmUnsavedRole: CanDeactivateFn<RoleDetailPage> = (page) =>
  !page.hasUnsavedChanges() || confirm('Leave without saving your permission changes?');

/** Lazily loaded by the shell at `/admin/*`; these pages sit in `NAVIGATION`'s Settings group. */
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
    path: 'access',
    component: AccessOverviewPage,
    title: 'Access overview',
    ...withPermissions(['admin.user.view']),
  },
  {
    path: 'users/:id/access',
    component: UserAccessPage,
    title: 'User access',
    ...withPermissions(['admin.user.view']),
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
    canDeactivate: [confirmUnsavedRole],
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
  {
    path: 'about',
    component: AboutPage,
    title: 'About',
    ...withPermissions(['admin.setting.view']),
  },
];
