import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { InviteUserPage } from './users/invite-user-page';
import { UserListPage } from './users/user-list-page';
import { UserRolesPage } from './users/user-roles-page';
import { RoleDetailPage } from './roles/role-detail-page';
import { RoleListPage } from './roles/role-list-page';

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
];
