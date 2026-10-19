import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { GoalPage, GoalsPage, MyGoalsPage } from './goals-pages';
import { CyclePage, CyclesPage, PipPage, PipsPage, ReviewPage } from './review-pages';
import { MyFeedbackPage, MyReviewsPage } from './self-pages';

/** Lazily loaded by the shell at `/performance/*`. Review and plan pages also serve employees and managers reaching their own. */
export const PERFORMANCE_ROUTES: Routes = [
  { path: '', component: CyclesPage, title: 'Performance reviews', ...withPermissions(['performance.cycle.view']) },
  { path: 'cycles/:id', component: CyclePage, title: 'Review cycle', ...withPermissions(['performance.cycle.view']) },
  { path: 'reviews/:id', component: ReviewPage, title: 'Review', ...withPermissions(['performance.review.manage-own', 'performance.review.view', 'performance.review.manage-as-manager']) },
  { path: 'pips', component: PipsPage, title: 'Improvement plans', ...withPermissions(['performance.pip.view', 'performance.pip.manage']) },
  { path: 'pips/:id', component: PipPage, title: 'Improvement plan', ...withPermissions(['performance.pip.view', 'performance.pip.manage', 'performance.review.manage-own']) },
];

/** Lazily loaded by the shell at `/goals/*`. */
export const GOALS_ROUTES: Routes = [
  { path: '', component: GoalsPage, title: 'Goals', ...withPermissions(['performance.goal.manage', 'performance.review.view']) },
  { path: ':id', component: GoalPage, title: 'Goal' },
];

/** Lazily loaded by the shell at `/me/goals`. */
export const MY_GOALS_ROUTES: Routes = [{ path: '', component: MyGoalsPage, title: 'My goals' }];

/** Lazily loaded by the shell at `/me/reviews`. */
export const MY_REVIEWS_ROUTES: Routes = [{ path: '', component: MyReviewsPage, title: 'My reviews' }];

/** Lazily loaded by the shell at `/me/feedback`. */
export const MY_FEEDBACK_ROUTES: Routes = [{ path: '', component: MyFeedbackPage, title: 'My feedback' }];
