import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { EngagementPage, MySurveysPage, SurveyPage } from './engagement-pages';

/** Lazily loaded by the shell at `/engagement/*`. */
export const ENGAGEMENT_ROUTES: Routes = [
  { path: '', component: EngagementPage, title: 'Engagement', ...withPermissions(['engagement.survey.view']) },
  { path: 'surveys/:id', component: SurveyPage, title: 'Survey', ...withPermissions(['engagement.survey.view']) },
];

/** Lazily loaded by the shell at `/me/surveys`. */
export const MY_SURVEYS_ROUTES: Routes = [{ path: '', component: MySurveysPage, title: 'My surveys' }];
