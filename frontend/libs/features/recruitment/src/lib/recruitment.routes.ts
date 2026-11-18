import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ApplicationPage } from './application-page';
import { CandidatePipelinePage, RequirementsPage } from './pipeline-pages';
import {
  CandidatePage,
  InterviewsPage,
  OfferPage,
  OffersPage,
  RecruitmentAnalyticsPage,
  RecruitmentSettingsPage,
  ReferralsPage,
  RequisitionPage,
} from './recruitment-pages';
import { JobBoardPage, MyInterviewPage, MyInterviewsPage } from './self-pages';

/** Lazily loaded by the shell at `/recruitment/*`. An offer page is also where approvers land from their task. */
export const RECRUITMENT_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'requirements' },
  { path: 'requirements', component: RequirementsPage, title: 'Requirements', ...withPermissions(['recruitment.requisition.view']) },
  { path: 'requirements/:id', component: RequisitionPage, title: 'Requirement', ...withPermissions(['recruitment.requisition.view']) },
  // Requirements were called requisitions; old links and bookmarks still land.
  { path: 'requisitions', pathMatch: 'full', redirectTo: 'requirements' },
  { path: 'requisitions/:id', redirectTo: 'requirements/:id' },
  // Postings are managed on each requirement; the old list address lands on Requirements.
  { path: 'postings', pathMatch: 'full', redirectTo: 'requirements' },
  { path: 'candidates', component: CandidatePipelinePage, title: 'Candidates', ...withPermissions(['recruitment.application.view']) },
  { path: 'candidates/:id', component: CandidatePage, title: 'Candidate', ...withPermissions(['recruitment.application.view']) },
  { path: 'applications/:id', component: ApplicationPage, title: 'Application', ...withPermissions(['recruitment.application.view']) },
  { path: 'interviews', component: InterviewsPage, title: 'Interviews', ...withPermissions(['recruitment.application.view']) },
  { path: 'offers', component: OffersPage, title: 'Offers', ...withPermissions(['recruitment.offer.view']) },
  { path: 'offers/:id', component: OfferPage, title: 'Offer', ...withPermissions(['recruitment.offer.view', 'workflow.task.act']) },
  { path: 'referrals', component: ReferralsPage, title: 'Referrals & incentives', ...withPermissions(['recruitment.application.view']) },
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
