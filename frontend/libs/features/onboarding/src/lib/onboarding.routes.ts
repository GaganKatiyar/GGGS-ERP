import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { MyOnboardingPage, MyOnboardingTasksPage, OnboardingCasePage, OnboardingDashboardPage, OnboardingTemplatesPage } from './onboarding-pages';

/** Lazily loaded by the shell at `/onboarding/*`. */
export const ONBOARDING_ROUTES: Routes = [
  { path: '', component: OnboardingDashboardPage, title: 'Onboarding', ...withPermissions(['onboarding.case.view']) },
  { path: 'cases/:id', component: OnboardingCasePage, title: 'Onboarding', ...withPermissions(['onboarding.case.view']) },
  { path: 'templates', component: OnboardingTemplatesPage, title: 'Onboarding checklists', ...withPermissions(['onboarding.template.view', 'onboarding.case.manage']) },
];

/** Lazily loaded by the shell at `/me/onboarding`. */
export const MY_ONBOARDING_ROUTES: Routes = [
  { path: '', component: MyOnboardingPage, title: 'My onboarding' },
  { path: 'tasks', component: MyOnboardingTasksPage, title: 'Onboarding tasks for me' },
];
