import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AgencyInput,
  AgencyItem,
  ApplicationStage,
  ApplicationView,
  BackgroundCheckStatus,
  CandidateDetailsInput,
  CandidateProfile,
  CandidateSource,
  CandidateView,
  HireResult,
  InterviewInput,
  InterviewItem,
  JobApplicationItem,
  ManpowerRequestInput,
  ManpowerRequestItem,
  ManpowerRequestStatus,
  MyInterviewView,
  OfferItem,
  OfferStatus,
  OfferTermsInput,
  OnboardingCaseItem,
  OnboardingCaseStatus,
  OnboardingCaseView,
  OnboardingDashboard,
  OnboardingOwners,
  OnboardingTaskItem,
  OnboardingTemplateInput,
  OnboardingTemplateItem,
  PostingInput,
  PostingItem,
  Recommendation,
  RecruitmentAnalytics,
  RecruitmentRules,
  ReferralItem,
  RequisitionInput,
  RequisitionItem,
  RequisitionStatus,
  WorkforceDashboard,
  WorkforcePlanInput,
  WorkforcePlanItem,
  WorkforcePlanLineInput,
  WorkforcePlanView,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

type Query = Record<string, string | number | boolean | null | undefined>;

function params(query: Query): Record<string, string | number | boolean> {
  const result: Record<string, string | number | boolean> = {};
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      result[key] = value;
    }
  }
  return result;
}

/** Workforce planning (`/api/v1/workforce`): headcount plans and manpower requests. */
@Injectable({ providedIn: 'root' })
export class WorkforceApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/workforce';

  dashboard(): Promise<WorkforceDashboard> {
    return firstValueFrom(this.http.get<WorkforceDashboard>(`${this.base}/dashboard`));
  }

  plans(year?: number): Promise<readonly WorkforcePlanItem[]> {
    return firstValueFrom(this.http.get<readonly WorkforcePlanItem[]>(`${this.base}/plans`, { params: params({ year }) }));
  }

  plan(id: string): Promise<WorkforcePlanView> {
    return firstValueFrom(this.http.get<WorkforcePlanView>(`${this.base}/plans/${id}`));
  }

  savePlan(id: string | null, body: WorkforcePlanInput): Promise<WorkforcePlanView> {
    return firstValueFrom(id ? this.http.put<WorkforcePlanView>(`${this.base}/plans/${id}`, body) : this.http.post<WorkforcePlanView>(`${this.base}/plans`, body));
  }

  saveLines(id: string, lines: readonly WorkforcePlanLineInput[]): Promise<WorkforcePlanView> {
    return firstValueFrom(this.http.put<WorkforcePlanView>(`${this.base}/plans/${id}/lines`, { lines }));
  }

  planAction(id: string, action: 'submit' | 'close'): Promise<WorkforcePlanView> {
    return firstValueFrom(this.http.post<WorkforcePlanView>(`${this.base}/plans/${id}/${action}`, {}));
  }

  requests(query: { status?: ManpowerRequestStatus | null; departmentId?: string | null; mine?: boolean | null } = {}): Promise<readonly ManpowerRequestItem[]> {
    return firstValueFrom(this.http.get<readonly ManpowerRequestItem[]>(`${this.base}/requests`, { params: params(query) }));
  }

  request(id: string): Promise<ManpowerRequestItem> {
    return firstValueFrom(this.http.get<ManpowerRequestItem>(`${this.base}/requests/${id}`));
  }

  raise(details: ManpowerRequestInput, submit: boolean): Promise<ManpowerRequestItem> {
    return firstValueFrom(this.http.post<ManpowerRequestItem>(`${this.base}/requests`, { details, submit }));
  }

  updateRequest(id: string, details: ManpowerRequestInput): Promise<ManpowerRequestItem> {
    return firstValueFrom(this.http.put<ManpowerRequestItem>(`${this.base}/requests/${id}`, details));
  }

  requestAction(id: string, action: 'submit' | 'cancel'): Promise<ManpowerRequestItem> {
    return firstValueFrom(this.http.post<ManpowerRequestItem>(`${this.base}/requests/${id}/${action}`, {}));
  }
}

/** Recruitment (`/api/v1/recruitment`) and the self-service job board, referrals and interviews (`/api/v1/me`). */
@Injectable({ providedIn: 'root' })
export class RecruitmentApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/recruitment';

  // Requisitions and postings
  requisitions(query: { status?: RequisitionStatus | null; departmentId?: string | null; search?: string | null } = {}): Promise<readonly RequisitionItem[]> {
    return firstValueFrom(this.http.get<readonly RequisitionItem[]>(`${this.base}/requisitions`, { params: params(query) }));
  }

  requisition(id: string): Promise<RequisitionItem> {
    return firstValueFrom(this.http.get<RequisitionItem>(`${this.base}/requisitions/${id}`));
  }

  saveRequisition(id: string | null, body: RequisitionInput): Promise<RequisitionItem> {
    return firstValueFrom(id ? this.http.put<RequisitionItem>(`${this.base}/requisitions/${id}`, body) : this.http.post<RequisitionItem>(`${this.base}/requisitions`, body));
  }

  requisitionAction(id: string, action: 'hold' | 'resume' | 'close' | 'cancel'): Promise<RequisitionItem> {
    return firstValueFrom(this.http.post<RequisitionItem>(`${this.base}/requisitions/${id}/${action}`, {}));
  }

  postings(requisitionId?: string): Promise<readonly PostingItem[]> {
    return firstValueFrom(this.http.get<readonly PostingItem[]>(`${this.base}/postings`, { params: params({ requisitionId }) }));
  }

  savePosting(id: string | null, body: PostingInput): Promise<PostingItem> {
    return firstValueFrom(id ? this.http.put<PostingItem>(`${this.base}/postings/${id}`, body) : this.http.post<PostingItem>(`${this.base}/postings`, body));
  }

  postingAction(id: string, action: 'publish' | 'close'): Promise<PostingItem> {
    return firstValueFrom(this.http.post<PostingItem>(`${this.base}/postings/${id}/${action}`, {}));
  }

  // Candidates and applications
  candidates(query: { search?: string | null; source?: CandidateSource | null; skill?: string | null } = {}): Promise<readonly CandidateProfile[]> {
    return firstValueFrom(this.http.get<readonly CandidateProfile[]>(`${this.base}/candidates`, { params: params(query) }));
  }

  candidate(id: string): Promise<CandidateView> {
    return firstValueFrom(this.http.get<CandidateView>(`${this.base}/candidates/${id}`));
  }

  addCandidate(body: { details: CandidateDetailsInput; source: CandidateSource; agencyId?: string | null; referrerEmployeeId?: string | null; requisitionId?: string | null }): Promise<CandidateView> {
    return firstValueFrom(this.http.post<CandidateView>(`${this.base}/candidates`, body));
  }

  updateCandidate(id: string, details: CandidateDetailsInput): Promise<CandidateView> {
    return firstValueFrom(this.http.put<CandidateView>(`${this.base}/candidates/${id}`, details));
  }

  apply(candidateId: string, requisitionId: string): Promise<ApplicationView> {
    return firstValueFrom(this.http.post<ApplicationView>(`${this.base}/candidates/${candidateId}/apply/${requisitionId}`, {}));
  }

  applications(query: { requisitionId?: string | null; stage?: ApplicationStage | null } = {}): Promise<readonly JobApplicationItem[]> {
    return firstValueFrom(this.http.get<readonly JobApplicationItem[]>(`${this.base}/applications`, { params: params(query) }));
  }

  application(id: string): Promise<ApplicationView> {
    return firstValueFrom(this.http.get<ApplicationView>(`${this.base}/applications/${id}`));
  }

  move(id: string, stage: ApplicationStage, note?: string | null): Promise<ApplicationView> {
    return firstValueFrom(this.http.post<ApplicationView>(`${this.base}/applications/${id}/move`, { stage, note }));
  }

  close(id: string, action: 'reject' | 'withdraw', reason: string | null): Promise<ApplicationView> {
    return firstValueFrom(this.http.post<ApplicationView>(`${this.base}/applications/${id}/${action}`, { reason }));
  }

  // Interviews and background checks
  interviews(from?: string, to?: string): Promise<readonly InterviewItem[]> {
    return firstValueFrom(this.http.get<readonly InterviewItem[]>(`${this.base}/interviews`, { params: params({ from, to }) }));
  }

  schedule(applicationId: string, body: InterviewInput): Promise<ApplicationView> {
    return firstValueFrom(this.http.post<ApplicationView>(`${this.base}/applications/${applicationId}/interviews`, body));
  }

  reschedule(interviewId: string, body: InterviewInput): Promise<ApplicationView> {
    return firstValueFrom(this.http.put<ApplicationView>(`${this.base}/interviews/${interviewId}`, body));
  }

  interviewAction(interviewId: string, action: 'complete' | 'cancel' | 'no-show'): Promise<ApplicationView> {
    return firstValueFrom(this.http.post<ApplicationView>(`${this.base}/interviews/${interviewId}/${action}`, {}));
  }

  initiateCheck(body: { candidateId: string; applicationId?: string | null; vendor?: string | null; checks: readonly string[] }): Promise<CandidateView> {
    return firstValueFrom(this.http.post<CandidateView>(`${this.base}/background-checks`, body));
  }

  recordCheck(id: string, status: BackgroundCheckStatus, remarks: string | null): Promise<CandidateView> {
    return firstValueFrom(this.http.post<CandidateView>(`${this.base}/background-checks/${id}/result`, { status, remarks }));
  }

  referrals(): Promise<readonly ReferralItem[]> {
    return firstValueFrom(this.http.get<readonly ReferralItem[]>(`${this.base}/referrals`));
  }

  // Offers
  offers(status?: OfferStatus | null): Promise<readonly OfferItem[]> {
    return firstValueFrom(this.http.get<readonly OfferItem[]>(`${this.base}/offers`, { params: params({ status }) }));
  }

  offer(id: string): Promise<OfferItem> {
    return firstValueFrom(this.http.get<OfferItem>(`${this.base}/offers/${id}`));
  }

  draftOffer(applicationId: string, terms: OfferTermsInput, submit: boolean): Promise<OfferItem> {
    return firstValueFrom(this.http.post<OfferItem>(`${this.base}/offers`, { applicationId, terms, submit }));
  }

  updateOffer(id: string, terms: OfferTermsInput): Promise<OfferItem> {
    return firstValueFrom(this.http.put<OfferItem>(`${this.base}/offers/${id}`, terms));
  }

  offerAction(id: string, action: 'submit' | 'send' | 'withdraw'): Promise<OfferItem> {
    return firstValueFrom(this.http.post<OfferItem>(`${this.base}/offers/${id}/${action}`, {}));
  }

  respond(id: string, accepted: boolean, reason: string | null): Promise<OfferItem> {
    return firstValueFrom(this.http.post<OfferItem>(`${this.base}/offers/${id}/respond`, { accepted, reason }));
  }

  hire(id: string, body: { managerEmployeeId?: string | null; positionId?: string | null; locationId?: string | null } = {}): Promise<HireResult> {
    return firstValueFrom(this.http.post<HireResult>(`${this.base}/offers/${id}/hire`, body));
  }

  // Agencies, rules and analytics
  agencies(): Promise<readonly AgencyItem[]> {
    return firstValueFrom(this.http.get<readonly AgencyItem[]>(`${this.base}/agencies`));
  }

  saveAgency(id: string | null, body: AgencyInput): Promise<readonly AgencyItem[]> {
    return firstValueFrom(id ? this.http.put<readonly AgencyItem[]>(`${this.base}/agencies/${id}`, body) : this.http.post<readonly AgencyItem[]>(`${this.base}/agencies`, body));
  }

  rules(): Promise<RecruitmentRules> {
    return firstValueFrom(this.http.get<RecruitmentRules>(`${this.base}/settings`));
  }

  saveRules(body: RecruitmentRules): Promise<RecruitmentRules> {
    return firstValueFrom(this.http.put<RecruitmentRules>(`${this.base}/settings`, body));
  }

  analytics(from?: string, to?: string): Promise<RecruitmentAnalytics> {
    return firstValueFrom(this.http.get<RecruitmentAnalytics>(`${this.base}/analytics`, { params: params({ from, to }) }));
  }

  // Self-service
  jobBoard(): Promise<readonly PostingItem[]> {
    return firstValueFrom(this.http.get<readonly PostingItem[]>('/api/v1/me/jobs'));
  }

  myReferrals(): Promise<readonly ReferralItem[]> {
    return firstValueFrom(this.http.get<readonly ReferralItem[]>('/api/v1/me/referrals'));
  }

  refer(body: { candidate: CandidateDetailsInput; requisitionId?: string | null; note?: string | null }): Promise<readonly ReferralItem[]> {
    return firstValueFrom(this.http.post<readonly ReferralItem[]>('/api/v1/me/referrals', body));
  }

  myInterviews(): Promise<readonly InterviewItem[]> {
    return firstValueFrom(this.http.get<readonly InterviewItem[]>('/api/v1/me/interviews'));
  }

  myInterview(id: string): Promise<MyInterviewView> {
    return firstValueFrom(this.http.get<MyInterviewView>(`/api/v1/me/interviews/${id}`));
  }

  submitScorecard(id: string, body: { ratings: Readonly<Record<string, number>>; recommendation: Recommendation; comments?: string | null }): Promise<MyInterviewView> {
    return firstValueFrom(this.http.put<MyInterviewView>(`/api/v1/me/interviews/${id}/scorecard`, body));
  }
}

/** Onboarding (`/api/v1/onboarding`) and one's own onboarding and tasks (`/api/v1/me/onboarding`). */
@Injectable({ providedIn: 'root' })
export class OnboardingApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/onboarding';

  dashboard(): Promise<OnboardingDashboard> {
    return firstValueFrom(this.http.get<OnboardingDashboard>(`${this.base}/dashboard`));
  }

  templates(): Promise<readonly OnboardingTemplateItem[]> {
    return firstValueFrom(this.http.get<readonly OnboardingTemplateItem[]>(`${this.base}/templates`));
  }

  saveTemplate(id: string | null, details: OnboardingTemplateInput, isActive: boolean): Promise<readonly OnboardingTemplateItem[]> {
    const body = { details, isActive };
    return firstValueFrom(
      id ? this.http.put<readonly OnboardingTemplateItem[]>(`${this.base}/templates/${id}`, body) : this.http.post<readonly OnboardingTemplateItem[]>(`${this.base}/templates`, body),
    );
  }

  owners(): Promise<OnboardingOwners> {
    return firstValueFrom(this.http.get<OnboardingOwners>(`${this.base}/owners`));
  }

  saveOwners(body: { hrOwnerEmployeeId?: string | null; itOwnerEmployeeId?: string | null; adminOwnerEmployeeId?: string | null }): Promise<OnboardingOwners> {
    return firstValueFrom(this.http.put<OnboardingOwners>(`${this.base}/owners`, body));
  }

  cases(query: { status?: OnboardingCaseStatus | null; open?: boolean | null; employeeId?: string | null } = {}): Promise<readonly OnboardingCaseItem[]> {
    return firstValueFrom(this.http.get<readonly OnboardingCaseItem[]>(`${this.base}/cases`, { params: params(query) }));
  }

  case(id: string): Promise<OnboardingCaseView> {
    return firstValueFrom(this.http.get<OnboardingCaseView>(`${this.base}/cases/${id}`));
  }

  start(body: { employeeId: string; templateId?: string | null; buddyEmployeeId?: string | null }): Promise<OnboardingCaseView> {
    return firstValueFrom(this.http.post<OnboardingCaseView>(`${this.base}/cases`, body));
  }

  assignBuddy(id: string, buddyEmployeeId: string | null): Promise<OnboardingCaseView> {
    return firstValueFrom(this.http.put<OnboardingCaseView>(`${this.base}/cases/${id}/buddy`, { buddyEmployeeId }));
  }

  cancel(id: string): Promise<OnboardingCaseView> {
    return firstValueFrom(this.http.post<OnboardingCaseView>(`${this.base}/cases/${id}/cancel`, {}));
  }

  changeTask(id: string, body: { assigneeEmployeeId?: string | null; dueDate?: string | null }): Promise<OnboardingCaseView> {
    return firstValueFrom(this.http.put<OnboardingCaseView>(`${this.base}/tasks/${id}`, body));
  }

  finishTask(id: string, skipped: boolean, notes: string | null): Promise<OnboardingTaskItem> {
    return firstValueFrom(this.http.post<OnboardingTaskItem>(`${this.base}/tasks/${id}/finish`, { skipped, notes }));
  }

  reopenTask(id: string): Promise<OnboardingTaskItem> {
    return firstValueFrom(this.http.post<OnboardingTaskItem>(`${this.base}/tasks/${id}/reopen`, {}));
  }

  /** The caller's own onboarding, or null when they have none (the API answers 204). */
  async mine(): Promise<OnboardingCaseView | null> {
    return (await firstValueFrom(this.http.get<OnboardingCaseView | null>('/api/v1/me/onboarding'))) ?? null;
  }

  myTasks(includeDone = false): Promise<readonly OnboardingTaskItem[]> {
    return firstValueFrom(this.http.get<readonly OnboardingTaskItem[]>('/api/v1/me/onboarding/tasks', { params: { includeDone } }));
  }
}
