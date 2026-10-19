/** Hiring contracts (M8): workforce plans, manpower requests, recruitment and onboarding, as the API returns them. */

// ----- Workforce ------------------------------------------------------------------------------

export type WorkforcePlanStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Closed';
export type ManpowerReason = 'NewRole' | 'Replacement' | 'Expansion' | 'Project';
export type ManpowerPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type ManpowerRequestStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Cancelled' | 'Fulfilled';

export const MANPOWER_REASONS: readonly ManpowerReason[] = ['NewRole', 'Replacement', 'Expansion', 'Project'];
export const MANPOWER_PRIORITIES: readonly ManpowerPriority[] = ['Low', 'Medium', 'High', 'Critical'];

export interface WorkforcePlanItem {
  readonly id: string;
  readonly name: string;
  readonly year: number;
  readonly status: WorkforcePlanStatus;
  readonly effectiveDate: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly notes?: string;
  readonly currentHeadcount: number;
  readonly plannedHeadcount: number;
  readonly salaryBudget: number;
  readonly hiringBudget: number;
  readonly lines: number;
}

export interface WorkforcePlanLineItem {
  readonly id: string;
  readonly departmentId: string;
  readonly departmentName?: string;
  readonly locationId?: string;
  readonly locationName?: string;
  readonly jobId?: string;
  readonly jobName?: string;
  readonly currentHeadcount: number;
  readonly liveHeadcount: number;
  readonly plannedHeadcount: number;
  readonly headcountGap: number;
  readonly openPositions: number;
  readonly salaryBudget: number;
  readonly hiringBudget: number;
  readonly notes?: string;
}

export interface WorkforcePlanView {
  readonly plan: WorkforcePlanItem;
  readonly lines: readonly WorkforcePlanLineItem[];
  readonly canManage: boolean;
}

export interface WorkforcePlanInput {
  readonly name: string;
  readonly year: number;
  readonly effectiveDate: string;
  readonly ownerEmployeeId?: string | null;
  readonly notes?: string | null;
}

export interface WorkforcePlanLineInput {
  readonly departmentId: string;
  readonly locationId?: string | null;
  readonly jobId?: string | null;
  readonly plannedHeadcount: number;
  readonly salaryBudget: number;
  readonly hiringBudget: number;
  readonly notes?: string | null;
}

export interface PlanFit {
  readonly planName: string;
  readonly planned: number;
  readonly current: number;
  readonly committed: number;
  readonly remaining: number;
  readonly within: boolean;
}

export interface ManpowerRequestItem {
  readonly id: string;
  readonly number: string;
  readonly title: string;
  readonly departmentId: string;
  readonly departmentName?: string;
  readonly positionId?: string;
  readonly jobId?: string;
  readonly jobName?: string;
  readonly locationId?: string;
  readonly locationName?: string;
  readonly positions: number;
  readonly skills: readonly string[];
  readonly minExperienceYears?: number;
  readonly maxExperienceYears?: number;
  readonly employmentTypeCode: string;
  readonly workModeCode: string;
  readonly salaryMin?: number;
  readonly salaryMax?: number;
  readonly budget?: number;
  readonly priority: ManpowerPriority;
  readonly requiredBy?: string;
  readonly reason: ManpowerReason;
  readonly replacementForEmployeeId?: string;
  readonly replacementForName?: string;
  readonly justification?: string;
  readonly requesterEmployeeId: string;
  readonly requesterName?: string;
  readonly status: ManpowerRequestStatus;
  readonly workflowInstanceId?: string;
  readonly createdDate: string;
  readonly planFit?: PlanFit;
  readonly canEdit: boolean;
}

export interface ManpowerRequestInput {
  readonly title: string;
  readonly departmentId: string;
  readonly positionId?: string | null;
  readonly jobId?: string | null;
  readonly locationId?: string | null;
  readonly positions: number;
  readonly skills: readonly string[];
  readonly minExperienceYears?: number | null;
  readonly maxExperienceYears?: number | null;
  readonly employmentTypeCode: string;
  readonly workModeCode: string;
  readonly salaryMin?: number | null;
  readonly salaryMax?: number | null;
  readonly budget?: number | null;
  readonly priority: ManpowerPriority;
  readonly requiredBy?: string | null;
  readonly reason: ManpowerReason;
  readonly replacementForEmployeeId?: string | null;
  readonly justification?: string | null;
}

export interface DepartmentDemand {
  readonly departmentId: string;
  readonly departmentName?: string;
  readonly planned: number;
  readonly live: number;
  readonly gap: number;
  readonly openPositions: number;
  readonly pendingPositions: number;
}

export interface WorkforceDashboard {
  readonly plan?: WorkforcePlanItem;
  readonly planned: number;
  readonly live: number;
  readonly gap: number;
  readonly openPositions: number;
  readonly pendingRequests: number;
  readonly salaryBudget: number;
  readonly hiringBudget: number;
  readonly departments: readonly DepartmentDemand[];
  readonly requestsByStatus: Readonly<Record<string, number>>;
}

// ----- Recruitment ----------------------------------------------------------------------------

export type RequisitionStatus = 'Open' | 'OnHold' | 'Filled' | 'Closed' | 'Cancelled';
export type PostingStatus = 'Draft' | 'Published' | 'Closed';
export type CandidateSource = 'Direct' | 'Careers' | 'JobBoard' | 'Referral' | 'Agency' | 'Internal' | 'Campus' | 'Social';
export type ApplicationStage = 'Applied' | 'Screening' | 'Shortlisted' | 'Interview' | 'Offer' | 'Hired' | 'Rejected' | 'Withdrawn';
export type InterviewMode = 'InPerson' | 'Video' | 'Phone';
export type InterviewStatus = 'Scheduled' | 'Completed' | 'Cancelled' | 'NoShow';
export type Recommendation = 'StrongNo' | 'No' | 'Yes' | 'StrongYes';
export type OfferStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Sent' | 'Accepted' | 'Declined' | 'Withdrawn';
export type BackgroundCheckStatus = 'Initiated' | 'InProgress' | 'Clear' | 'Discrepancy' | 'Failed';
export type ReferralStatus = 'Submitted' | 'InProcess' | 'Hired' | 'NotSelected' | 'BonusAwarded';

export const CANDIDATE_SOURCES: readonly CandidateSource[] = ['Direct', 'Careers', 'JobBoard', 'Referral', 'Agency', 'Internal', 'Campus', 'Social'];
/** The open stages, in pipeline order - the columns of the board. */
export const PIPELINE_STAGES: readonly ApplicationStage[] = ['Applied', 'Screening', 'Shortlisted', 'Interview', 'Offer'];
export const INTERVIEW_MODES: readonly InterviewMode[] = ['Video', 'InPerson', 'Phone'];
export const RECOMMENDATIONS: readonly Recommendation[] = ['StrongNo', 'No', 'Yes', 'StrongYes'];
export const BACKGROUND_CHECKS: readonly string[] = ['Identity', 'Address', 'Education', 'Employment', 'Criminal', 'Reference'];

export interface RequisitionItem {
  readonly id: string;
  readonly number: string;
  readonly manpowerRequestId?: string;
  readonly title: string;
  readonly departmentId: string;
  readonly departmentName?: string;
  readonly positionId?: string;
  readonly jobId?: string;
  readonly jobName?: string;
  readonly locationId?: string;
  readonly locationName?: string;
  readonly openings: number;
  readonly hired: number;
  readonly remaining: number;
  readonly employmentTypeCode: string;
  readonly workModeCode: string;
  readonly minExperienceYears?: number;
  readonly maxExperienceYears?: number;
  readonly salaryMin?: number;
  readonly salaryMax?: number;
  readonly skills: readonly string[];
  readonly description?: string;
  readonly hiringManagerEmployeeId?: string;
  readonly hiringManagerName?: string;
  readonly recruiterEmployeeId?: string;
  readonly recruiterName?: string;
  readonly priority?: string;
  readonly targetDate?: string;
  readonly status: RequisitionStatus;
  readonly openedOn: string;
  readonly closedOn?: string;
  readonly applications: number;
  readonly pipeline: Readonly<Record<string, number>>;
}

export interface RequisitionInput {
  readonly title: string;
  readonly departmentId: string;
  readonly positionId?: string | null;
  readonly jobId?: string | null;
  readonly locationId?: string | null;
  readonly openings: number;
  readonly employmentTypeCode: string;
  readonly workModeCode: string;
  readonly minExperienceYears?: number | null;
  readonly maxExperienceYears?: number | null;
  readonly salaryMin?: number | null;
  readonly salaryMax?: number | null;
  readonly skills: readonly string[];
  readonly description?: string | null;
  readonly hiringManagerEmployeeId?: string | null;
  readonly recruiterEmployeeId?: string | null;
  readonly priority?: string | null;
  readonly targetDate?: string | null;
}

export interface PostingItem {
  readonly id: string;
  readonly requisitionId: string;
  readonly requisitionNumber: string;
  readonly title: string;
  readonly description: string;
  readonly internal: boolean;
  readonly external: boolean;
  readonly status: PostingStatus;
  readonly publishedOn?: string;
  readonly expiresOn?: string;
  readonly departmentName?: string;
  readonly locationName?: string;
  readonly employmentTypeCode: string;
  readonly workModeCode: string;
  readonly skills: readonly string[];
  readonly minExperienceYears?: number;
  readonly maxExperienceYears?: number;
}

export interface PostingInput {
  readonly requisitionId: string;
  readonly title: string;
  readonly description: string;
  readonly internal: boolean;
  readonly external: boolean;
  readonly expiresOn?: string | null;
}

export interface CandidateProfile {
  readonly id: string;
  readonly number: string;
  readonly fullName: string;
  readonly firstName: string;
  readonly lastName?: string;
  readonly email: string;
  readonly mobile?: string;
  readonly currentCompany?: string;
  readonly currentTitle?: string;
  readonly experienceYears?: number;
  readonly currentCtc?: number;
  readonly expectedCtc?: number;
  readonly noticePeriodDays?: number;
  readonly location?: string;
  readonly skills: readonly string[];
  readonly linkedInUrl?: string;
  readonly notes?: string;
  readonly source: CandidateSource;
  readonly agencyId?: string;
  readonly agencyName?: string;
  readonly referrerEmployeeId?: string;
  readonly referrerName?: string;
  readonly employeeId?: string;
  readonly createdDate: string;
  readonly applications: number;
}

export interface CandidateDetailsInput {
  readonly firstName: string;
  readonly lastName?: string | null;
  readonly email: string;
  readonly mobile?: string | null;
  readonly currentCompany?: string | null;
  readonly currentTitle?: string | null;
  readonly experienceYears?: number | null;
  readonly currentCtc?: number | null;
  readonly expectedCtc?: number | null;
  readonly noticePeriodDays?: number | null;
  readonly location?: string | null;
  readonly skills: readonly string[];
  readonly linkedInUrl?: string | null;
  readonly notes?: string | null;
}

export interface JobApplicationItem {
  readonly id: string;
  readonly candidateId: string;
  readonly candidateNumber: string;
  readonly candidateName: string;
  readonly candidateEmail: string;
  readonly currentTitle?: string;
  readonly experienceYears?: number;
  readonly source: CandidateSource;
  readonly requisitionId: string;
  readonly requisitionNumber: string;
  readonly requisitionTitle: string;
  readonly stage: ApplicationStage;
  readonly matchScore: number;
  readonly appliedDate: string;
  readonly stageChangedDate: string;
  readonly rejectionReason?: string;
  readonly averageRating?: number;
  readonly interviews: number;
  readonly offerStatus?: OfferStatus;
}

export interface InterviewItem {
  readonly id: string;
  readonly applicationId: string;
  readonly candidateId: string;
  readonly candidateName: string;
  readonly requisitionTitle: string;
  readonly round: string;
  readonly scheduledAt: string;
  readonly durationMinutes: number;
  readonly mode: InterviewMode;
  readonly location?: string;
  readonly panel: readonly string[];
  readonly panelNames: readonly string[];
  readonly status: InterviewStatus;
  readonly notes?: string;
  readonly scorecards: number;
  readonly myScorecardSubmitted: boolean;
}

export interface InterviewInput {
  readonly round: string;
  readonly scheduledAt: string;
  readonly durationMinutes: number;
  readonly mode: InterviewMode;
  readonly location?: string | null;
  readonly panel: readonly string[];
  readonly notes?: string | null;
}

export interface ScorecardItem {
  readonly id: string;
  readonly interviewId: string;
  readonly round: string;
  readonly interviewerEmployeeId: string;
  readonly interviewerName?: string;
  readonly ratings: Readonly<Record<string, number>>;
  readonly overallRating: number;
  readonly recommendation: Recommendation;
  readonly comments?: string;
  readonly submittedDate: string;
}

export interface BackgroundCheckItem {
  readonly id: string;
  readonly candidateId: string;
  readonly applicationId?: string;
  readonly vendor?: string;
  readonly checks: readonly string[];
  readonly status: BackgroundCheckStatus;
  readonly initiatedOn: string;
  readonly completedOn?: string;
  readonly remarks?: string;
}

export interface StageChange {
  readonly stage: ApplicationStage;
  readonly at: string;
  readonly byEmployeeId?: string;
  readonly note?: string;
}

export interface MatchView {
  readonly score: number;
  readonly skillsScore: number;
  readonly experienceScore: number;
  readonly salaryScore: number;
  readonly matchedSkills: readonly string[];
  readonly missingSkills: readonly string[];
}

export interface OfferItem {
  readonly id: string;
  readonly number: string;
  readonly applicationId: string;
  readonly candidateId: string;
  readonly candidateName: string;
  readonly requisitionId: string;
  readonly requisitionNumber: string;
  readonly requisitionTitle: string;
  readonly annualCtc: number;
  readonly currency: string;
  readonly joiningDate: string;
  readonly designation: string;
  readonly gradeId?: string;
  readonly expiresOn?: string;
  readonly notes?: string;
  readonly status: OfferStatus;
  readonly workflowInstanceId?: string;
  readonly sentDate?: string;
  readonly respondedDate?: string;
  readonly declineReason?: string;
  readonly createdDate: string;
  readonly withinBudget: boolean;
}

export interface OfferTermsInput {
  readonly annualCtc: number;
  readonly currency: string;
  readonly joiningDate: string;
  readonly designation: string;
  readonly gradeId?: string | null;
  readonly expiresOn?: string | null;
  readonly notes?: string | null;
}

export interface ApplicationView {
  readonly application: JobApplicationItem;
  readonly candidate: CandidateProfile;
  readonly match: MatchView;
  readonly history: readonly StageChange[];
  readonly interviews: readonly InterviewItem[];
  readonly scorecards: readonly ScorecardItem[];
  readonly offers: readonly OfferItem[];
  readonly backgroundChecks: readonly BackgroundCheckItem[];
  readonly canManage: boolean;
}

export interface CandidateView {
  readonly candidate: CandidateProfile;
  readonly applications: readonly JobApplicationItem[];
  readonly backgroundChecks: readonly BackgroundCheckItem[];
  readonly canManage: boolean;
}

export interface MyInterviewView {
  readonly interview: InterviewItem;
  readonly candidate: CandidateProfile;
  readonly criteria: readonly string[];
  readonly myScorecard?: ScorecardItem;
  readonly requisitionDescription?: string;
}

export interface HireResult {
  readonly employeeId: string;
  readonly employeeCode: string;
  readonly requisitionFilled: boolean;
}

export interface AgencyItem {
  readonly id: string;
  readonly name: string;
  readonly contactName?: string;
  readonly email?: string;
  readonly phone?: string;
  readonly feePercent: number;
  readonly isActive: boolean;
  readonly candidates: number;
  readonly hires: number;
}

export interface AgencyInput {
  readonly name: string;
  readonly contactName?: string | null;
  readonly email?: string | null;
  readonly phone?: string | null;
  readonly feePercent: number;
  readonly isActive: boolean;
}

export interface ReferralItem {
  readonly id: string;
  readonly referrerEmployeeId: string;
  readonly referrerName?: string;
  readonly candidateId: string;
  readonly candidateName: string;
  readonly requisitionId?: string;
  readonly requisitionTitle?: string;
  readonly status: ReferralStatus;
  readonly stage?: string;
  readonly bonusAmount?: number;
  readonly bonusDueOn?: string;
  readonly createdDate: string;
}

export interface RecruitmentRules {
  readonly referralBonus: number;
  readonly referralBonusAfterMonths: number;
  readonly offerValidDays: number;
  readonly currency: string;
  readonly scorecardCriteria: readonly string[];
}

export interface CountRow {
  readonly key: string;
  readonly label: string;
  readonly count: number;
}

export interface SourceRow {
  readonly source: string;
  readonly applications: number;
  readonly interviewed: number;
  readonly offered: number;
  readonly hired: number;
  readonly conversionPercent: number;
}

export interface RecruiterRow {
  readonly recruiterEmployeeId?: string;
  readonly recruiterName?: string;
  readonly requisitions: number;
  readonly applications: number;
  readonly hires: number;
  readonly averageDaysToHire?: number;
}

export interface RecruitmentAnalytics {
  readonly from: string;
  readonly to: string;
  readonly openRequisitions: number;
  readonly openPositions: number;
  readonly applications: number;
  readonly hires: number;
  readonly averageDaysToHire?: number;
  readonly costPerHire?: number;
  readonly totalHiringCost: number;
  readonly offerAcceptancePercent?: number;
  readonly offersAccepted: number;
  readonly offersDeclined: number;
  readonly pipeline: readonly CountRow[];
  readonly sources: readonly SourceRow[];
  readonly recruiters: readonly RecruiterRow[];
}

// ----- Onboarding -----------------------------------------------------------------------------

export type OnboardingTaskCategory =
  | 'Documents'
  | 'Verification'
  | 'EmployeeSetup'
  | 'Email'
  | 'Laptop'
  | 'Software'
  | 'Vpn'
  | 'Access'
  | 'IdCard'
  | 'Induction'
  | 'Training'
  | 'ManagerIntroduction'
  | 'Other';
export type OnboardingAssignee = 'Hr' | 'It' | 'Admin' | 'Manager' | 'Buddy' | 'Employee';
export type OnboardingCaseStatus = 'PreBoarding' | 'InProgress' | 'Completed' | 'Cancelled';
export type OnboardingTaskStatus = 'Pending' | 'Done' | 'Skipped';

export const ONBOARDING_CATEGORIES: readonly OnboardingTaskCategory[] = [
  'Documents',
  'Verification',
  'EmployeeSetup',
  'Email',
  'Laptop',
  'Software',
  'Vpn',
  'Access',
  'IdCard',
  'Induction',
  'Training',
  'ManagerIntroduction',
  'Other',
];
export const ONBOARDING_ASSIGNEES: readonly OnboardingAssignee[] = ['Hr', 'It', 'Admin', 'Manager', 'Buddy', 'Employee'];

export interface OnboardingTemplateTask {
  readonly title: string;
  readonly description?: string | null;
  readonly category: OnboardingTaskCategory;
  readonly assignee: OnboardingAssignee;
  readonly dueOffsetDays: number;
  readonly required: boolean;
}

export interface OnboardingTemplateItem {
  readonly id: string;
  readonly name: string;
  readonly description?: string;
  readonly departmentId?: string;
  readonly departmentName?: string;
  readonly locationId?: string;
  readonly locationName?: string;
  readonly employmentTypeCode?: string;
  readonly isDefault: boolean;
  readonly isActive: boolean;
  readonly tasks: readonly OnboardingTemplateTask[];
}

export interface OnboardingTemplateInput {
  readonly name: string;
  readonly description?: string | null;
  readonly departmentId?: string | null;
  readonly locationId?: string | null;
  readonly employmentTypeCode?: string | null;
  readonly isDefault: boolean;
  readonly tasks: readonly OnboardingTemplateTask[];
}

export interface OnboardingTaskItem {
  readonly id: string;
  readonly caseId: string;
  readonly title: string;
  readonly description?: string;
  readonly category: OnboardingTaskCategory;
  readonly assigneeRole: OnboardingAssignee;
  readonly assigneeEmployeeId?: string;
  readonly assigneeName?: string;
  readonly dueDate: string;
  readonly required: boolean;
  readonly status: OnboardingTaskStatus;
  readonly completedDate?: string;
  readonly completedByName?: string;
  readonly notes?: string;
  readonly overdue: boolean;
  readonly canAct: boolean;
}

export interface OnboardingCaseItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly designation?: string;
  readonly departmentName?: string;
  readonly templateName: string;
  readonly joiningDate: string;
  readonly managerEmployeeId?: string;
  readonly managerName?: string;
  readonly buddyEmployeeId?: string;
  readonly buddyName?: string;
  readonly status: OnboardingCaseStatus;
  readonly completedOn?: string;
  readonly tasks: number;
  readonly done: number;
  readonly overdue: number;
  readonly progressPercent: number;
}

export interface OnboardingCaseView {
  readonly case: OnboardingCaseItem;
  readonly tasks: readonly OnboardingTaskItem[];
  readonly canManage: boolean;
}

export interface OnboardingOwners {
  readonly hrOwnerEmployeeId?: string;
  readonly hrOwnerName?: string;
  readonly itOwnerEmployeeId?: string;
  readonly itOwnerName?: string;
  readonly adminOwnerEmployeeId?: string;
  readonly adminOwnerName?: string;
}

export interface OnboardingDashboard {
  readonly open: number;
  readonly preBoarding: number;
  readonly joiningNext30Days: number;
  readonly completedLast30Days: number;
  readonly overdueTasks: number;
  readonly upcoming: readonly OnboardingCaseItem[];
  readonly overdueByCategory: Readonly<Record<string, number>>;
}
