import type { Routes } from '@angular/router';
import { BranchesPage } from './branches/branches-page';
import { BusinessUnitsPage } from './business-units/business-units-page';
import { CostCentersPage } from './cost-centers/cost-centers-page';
import { DepartmentsPage } from './departments/departments-page';
import { DesignationsPage } from './designations/designations-page';
import { GradesPage } from './grades/grades-page';
import { LegalEntitiesPage } from './legal-entities/legal-entities-page';
import { LocationsPage } from './locations/locations-page';

/**
 * Lazily loaded by the shell at `/org/*`; see `app.routes.ts` and `NAVIGATION`'s "Organization"
 * entry, which is where these paths come from.
 */
export const ORGANIZATION_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'legal-entities' },
  { path: 'legal-entities', component: LegalEntitiesPage, title: 'Legal entities' },
  { path: 'business-units', component: BusinessUnitsPage, title: 'Business units' },
  { path: 'branches', component: BranchesPage, title: 'Branches' },
  { path: 'locations', component: LocationsPage, title: 'Locations' },
  { path: 'departments', component: DepartmentsPage, title: 'Departments' },
  { path: 'designations', component: DesignationsPage, title: 'Designations' },
  { path: 'grades', component: GradesPage, title: 'Grades' },
  { path: 'cost-centers', component: CostCentersPage, title: 'Cost centers' },
];
