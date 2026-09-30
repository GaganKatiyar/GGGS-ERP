import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { OnboardingCaseDetailPage } from './cases/onboarding-case-detail-page';
import { StartOnboardingCasePage } from './cases/start-onboarding-case-page';
import { OnboardingTemplateDetailPage } from './templates/onboarding-template-detail-page';
import { OnboardingTemplateListPage } from './templates/onboarding-template-list-page';

/** Lazily loaded by the shell at `/onboarding/*`. */
export const ONBOARDING_ROUTES: Routes = [
  { path: '', component: OnboardingTemplateListPage, title: 'Onboarding templates' },
  {
    path: 'templates/:id',
    component: OnboardingTemplateDetailPage,
    title: 'Template tasks',
  },
  {
    path: 'cases/new',
    component: StartOnboardingCasePage,
    title: 'Start onboarding',
    ...withPermissions(['onboarding.case.manage']),
  },
  { path: 'cases/:id', component: OnboardingCaseDetailPage, title: 'Onboarding case' },
];
