import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { BenefitEnrollmentsPage, BenefitPlansPage, MyBenefitsPage } from './benefits-pages';

/** Lazily loaded by the shell at `/benefits/*`. */
export const BENEFITS_ROUTES: Routes = [
  { path: '', component: BenefitPlansPage, title: 'Benefit plans', ...withPermissions(['benefit.plan.view']) },
  { path: 'enrollments', component: BenefitEnrollmentsPage, title: 'Enrollments', ...withPermissions(['benefit.enrollment.view']) },
];

/** Lazily loaded by the shell at `/me/benefits`. */
export const MY_BENEFITS_ROUTES: Routes = [{ path: '', component: MyBenefitsPage, title: 'My benefits' }];
