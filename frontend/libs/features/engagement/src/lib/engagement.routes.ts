import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { BannerSettingsPage } from './banner-settings-page';
import { CelebrationsPage } from './celebrations-page';
import { EngagementPage, MySurveysPage, SurveyPage } from './engagement-pages';

const greetings = withPermissions(['engagement.survey.manage']);

/** Lazily loaded by the shell at `/engagement/*`. */
export const ENGAGEMENT_ROUTES: Routes = [
  { path: '', component: EngagementPage, title: 'Engagement', ...withPermissions(['engagement.survey.view']) },
  { path: 'surveys/:id', component: SurveyPage, title: 'Survey', ...withPermissions(['engagement.survey.view']) },
  { path: 'banner', component: BannerSettingsPage, title: 'Dashboard banner', ...withPermissions(['engagement.survey.manage']) },
  // Greetings sent by hand from HR Portal; `kind` is bound to the page's input.
  { path: 'birthdays', component: CelebrationsPage, title: 'Birthday wishes', canActivate: greetings.canActivate, data: { ...greetings.data, kind: 'Birthday' } },
  { path: 'anniversaries', component: CelebrationsPage, title: 'Work anniversaries', canActivate: greetings.canActivate, data: { ...greetings.data, kind: 'WorkAnniversary' } },
];

/** Lazily loaded by the shell at `/me/surveys`. */
export const MY_SURVEYS_ROUTES: Routes = [{ path: '', component: MySurveysPage, title: 'My surveys' }];
