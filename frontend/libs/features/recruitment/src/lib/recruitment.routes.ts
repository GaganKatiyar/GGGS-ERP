import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ApplicationPage } from './application-page';
import {
  CandidatePage,
  CandidatesPage,
  InterviewsPage,
  OfferPage,
  OffersPage,
  PostingsPage,
  RecruitmentAnalyticsPage,
  RecruitmentSettingsPage,
  RequisitionPage,
  RequisitionsPage,
} from './recruitment-pages';
import { JobBoardPage, MyInterviewPage, MyInterviewsPage } from './self-pages';

/** Lazily loaded by the shell at `/recruitment/*`. An offer page is also where approvers land from their task. */
export const RECRUITMENT_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'requisitions' },
  { path: 'requisitions', component: RequisitionsPage, title: 'Requisitions', ...withPermissions(['recruitment.requisition.view']) },
  { path: 'requisitions/:id', component: RequisitionPage, title: 'Requisition', ...withPermissions(['recruitment.requisition.view']) },
  { path: 'postings', component: PostingsPage, title: 'Job postings', ...withPermissions(['recruitment.posting.view']) },
  { path: 'candidates', component: CandidatesPage, title: 'Candidates', ...withPermissions(['recruitment.application.view']) },
  { path: 'candidates/:id', component: CandidatePage, title: 'Candidate', ...withPermissions(['recruitment.application.view']) },
  { path: 'applications/:id', component: ApplicationPage, title: 'Application', ...withPermissions(['recruitment.application.view']) },
  { path: 'interviews', component: InterviewsPage, title: 'Interviews', ...withPermissions(['recruitment.application.view']) },
  { path: 'offers', component: OffersPage, title: 'Offers', ...withPermissions(['recruitment.offer.view']) },
  { path: 'offers/:id', component: OfferPage, title: 'Offer', ...withPermissions(['recruitment.offer.view', 'workflow.task.act']) },
  { path: 'settings', component: RecruitmentSettingsPage, title: 'Recruitment settings', ...withPermissions(['recruitment.settings.manage', 'recruitment.application.view']) },
  { path: 'analytics', component: RecruitmentAnalyticsPage, title: 'Recruitment analytics', ...withPermissions(['recruitment.analytics.view']) },
];

/** Lazily loaded by the shell at `/me/jobs`. */
export const JOB_BOARD_ROUTES: Routes = [{ path: '', component: JobBoardPage, title: 'Jobs & referrals' }];

/** Lazily loaded by the shell at `/me/interviews`. */
export const MY_INTERVIEWS_ROUTES: Routes = [
  { path: '', component: MyInterviewsPage, title: 'My interviews' },
  { path: ':id', component: MyInterviewPage, title: 'Interview feedback' },
];
