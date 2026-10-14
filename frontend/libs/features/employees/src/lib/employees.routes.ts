import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { Employee360Page } from './people/employee-360-page';
import { EmployeeDirectoryPage } from './people/employee-directory-page';
import { EmployeeImportPage } from './people/employee-import-page';
import { HireEmployeePage } from './people/hire-employee-page';
import { MyProfilePage } from './people/my-profile-page';
import { OrgChartPage } from './people/org-chart-page';
import { TeamPage } from './people/team-page';

/**
 * Lazily loaded by the shell at `/employees/*`. Lifecycle actions (transfer, promotion, confirm,
 * notice, exit) are dialogs on Employee 360, not routes of their own.
 *
 * Fixed paths (`new`, `import`, `org-chart`) come before `:id`, so the router never reads `new` as
 * an id.
 */
export const EMPLOYEES_ROUTES: Routes = [
  { path: '', component: EmployeeDirectoryPage, title: 'Employees' },
  { path: 'new', component: HireEmployeePage, title: 'Hire employee', ...withPermissions(['employee.profile.create']) },
  { path: 'import', component: EmployeeImportPage, title: 'Import employees', ...withPermissions(['employee.profile.create']) },
  { path: 'org-chart', component: OrgChartPage, title: 'Org chart' },
  { path: ':id', component: Employee360Page, title: 'Employee 360' },
];

/** Mounted directly at `/team` - see `NAVIGATION`'s "My team" entry. */
export const TEAM_ROUTES: Routes = [{ path: '', component: TeamPage, title: 'My team' }];

/** Mounted at `/me/profile`: self-service, needs only an account linked to an employee. */
export const MY_PROFILE_ROUTES: Routes = [{ path: '', component: MyProfilePage, title: 'My profile' }];
