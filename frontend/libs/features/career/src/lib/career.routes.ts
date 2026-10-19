import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { CareerPathsPage, MyCareerPage, PromotionPage, PromotionsPage, SuccessionPage, TalentReviewPage } from './career-pages';

/** Lazily loaded by the shell at `/career/*`. A promotion page is also where approvers land from their task. */
export const CAREER_ROUTES: Routes = [
  { path: '', component: CareerPathsPage, title: 'Career paths', ...withPermissions(['career.path.view']) },
  { path: 'promotions', component: PromotionsPage, title: 'Promotions', ...withPermissions(['career.promotion.view', 'career.promotion.manage']) },
  { path: 'promotions/:id', component: PromotionPage, title: 'Promotion', ...withPermissions(['career.promotion.view', 'career.promotion.manage', 'workflow.task.act']) },
  { path: 'succession', component: SuccessionPage, title: 'Succession', ...withPermissions(['career.succession.view', 'career.succession.manage']) },
  { path: 'talent-review', component: TalentReviewPage, title: 'Talent review', ...withPermissions(['career.talent-review.manage']) },
];

/** Lazily loaded by the shell at `/me/career`. */
export const MY_CAREER_ROUTES: Routes = [{ path: '', component: MyCareerPage, title: 'My career' }];
