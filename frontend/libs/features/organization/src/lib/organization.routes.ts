import type { Routes } from '@angular/router';
import { BranchesPage } from './branches/branches-page';
import { CostCentersPage } from './cost-centers/cost-centers-page';
import { DepartmentsPage } from './departments/departments-page';
import { DesignationsPage } from './designations/designations-page';
import { GradesPage } from './grades/grades-page';
import { LegalEntitiesPage } from './legal-entities/legal-entities-page';

/**
 * Lazily loaded by the shell at `/org/*`; see `app.routes.ts` and `NAVIGATION`'s "Organization"
 * entry, which is where these paths come from.
 */
export const ORGANIZATION_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'legal-entities' },
  { path: 'legal-entities', component: LegalEntitiesPage, title: 'Legal entities' },
  { path: 'branches', component: BranchesPage, title: 'Branches' },
  { path: 'departments', component: DepartmentsPage, title: 'Departments' },
  { path: 'designations', component: DesignationsPage, title: 'Designations' },
  { path: 'grades', component: GradesPage, title: 'Grades' },
  { path: 'cost-centers', component: CostCentersPage, title: 'Cost centers' },
];
