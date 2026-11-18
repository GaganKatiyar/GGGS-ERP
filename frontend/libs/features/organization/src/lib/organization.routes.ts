import type { Routes } from '@angular/router';
import { DepartmentTreePage } from './structure/department-tree-page';
import { ORG_KINDS } from './structure/org-kinds';
import { OrgUnitPage } from './structure/org-unit-page';
import { StructureImportPage } from './structure/structure-import-page';

/**
 * Lazily loaded by the shell at `/org/*`. Every kind of record is the same {@link OrgUnitPage},
 * told which kind by the route's data (bound to its `kindKey` input); the structure page is the one
 * view of its own.
 */
export const ORGANIZATION_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'structure' },
  { path: 'structure', component: DepartmentTreePage, title: 'Organization structure' },
  { path: 'import', component: StructureImportPage, title: 'Import structure' },
  // The old addresses of the Roles and Levels screens, kept so bookmarks still land.
  { path: 'jobs', pathMatch: 'full', redirectTo: 'roles' },
  { path: 'job-levels', pathMatch: 'full', redirectTo: 'levels' },
  ...Object.entries(ORG_KINDS).map(([kindKey, config]) => ({
    path: kindKey,
    component: OrgUnitPage,
    data: { kindKey },
    title: config.title,
  })),
];
