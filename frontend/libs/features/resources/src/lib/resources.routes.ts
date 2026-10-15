import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import {
  ResourceBenchPage,
  ResourceCalendarPage,
  ResourceCapacityPage,
  ResourceTransfersPage,
  ResourceUtilizationPage,
} from './planning-pages';
import { ResourceRequestPage, ResourceRequestsPage } from './requests-pages';
import { ResourceProfilePage } from './resource-profile-page';
import { ResourcesPage } from './resources-page';

/** Lazily loaded by the shell at `/resources/*`. The named pages come before `:employeeId`. */
export const RESOURCES_ROUTES: Routes = [
  { path: '', component: ResourcesPage, title: 'Resources', ...withPermissions(['resource.profile.view']) },
  { path: 'requests', component: ResourceRequestsPage, title: 'Resource requests', ...withPermissions(['resource.request.view']) },
  { path: 'requests/:id', component: ResourceRequestPage, title: 'Resource request', ...withPermissions(['resource.request.view']) },
  { path: 'calendar', component: ResourceCalendarPage, title: 'Allocation calendar', ...withPermissions(['resource.allocation.view']) },
  { path: 'capacity', component: ResourceCapacityPage, title: 'Capacity & demand', ...withPermissions(['resource.allocation.view']) },
  { path: 'bench', component: ResourceBenchPage, title: 'Bench', ...withPermissions(['resource.profile.view']) },
  { path: 'utilization', component: ResourceUtilizationPage, title: 'Utilization', ...withPermissions(['resource.utilization.view']) },
  { path: 'transfers', component: ResourceTransfersPage, title: 'Transfers', ...withPermissions(['resource.allocation.view']) },
  { path: ':employeeId', component: ResourceProfilePage, title: 'Resource profile', ...withPermissions(['resource.profile.view']) },
];
