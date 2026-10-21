/** Employee experience contracts (M10): assets, expenses and travel, helpdesk, engagement, rewards and separation, as the API returns them. */

// ----- Assets ---------------------------------------------------------------------------------

export type AssetStatus = 'InStock' | 'Assigned' | 'InRepair' | 'Retired' | 'Lost';
export type AssetCondition = 'New' | 'Good' | 'Fair' | 'Poor' | 'Damaged';
export type AssetEventKind = 'Registered' | 'Assigned' | 'Returned' | 'Repair' | 'Repaired' | 'Retired' | 'Lost' | 'Note';
export type AssetRequestStatus = 'Requested' | 'Fulfilled' | 'Rejected' | 'Cancelled';
export type AssetAction = 'repair' | 'repaired' | 'retire' | 'lost' | 'note';

export const ASSET_STATUSES: readonly AssetStatus[] = ['InStock', 'Assigned', 'InRepair', 'Retired', 'Lost'];
export const ASSET_CONDITIONS: readonly AssetCondition[] = ['New', 'Good', 'Fair', 'Poor', 'Damaged'];

export interface AssetCategoryItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly depreciationYears: number;
  readonly requiresSerial: boolean;
  readonly isActive: boolean;
  readonly assets: number;
  readonly inStock: number;
}

export interface AssetCategoryInput {
  readonly code: string;
  readonly name: string;
  readonly depreciationYears: number;
  readonly requiresSerial: boolean;
  readonly isActive: boolean;
}

export interface AssetItem {
  readonly id: string;
  readonly tag: string;
  readonly categoryId: string;
  readonly categoryName: string;
  readonly name: string;
  readonly make?: string;
  readonly model?: string;
  readonly serialNumber?: string;
  readonly purchaseDate?: string;
  readonly purchaseCost?: number;
  readonly bookValue: number;
  readonly vendor?: string;
  readonly warrantyUntil?: string;
  readonly warrantyExpiringSoon: boolean;
  readonly locationId?: string;
  readonly locationName?: string;
  readonly notes?: string;
  readonly status: AssetStatus;
  readonly condition: AssetCondition;
  readonly assignedEmployeeId?: string;
  readonly assignedName?: string;
  readonly assignedOn?: string;
}

export interface AssetEventItem {
  readonly id: string;
  readonly kind: AssetEventKind;
  readonly on: string;
  readonly employeeName?: string;
  readonly byName?: string;
  readonly cost?: number;
  readonly note?: string;
}

export interface AssetView {
  readonly asset: AssetItem;
  readonly history: readonly AssetEventItem[];
}

export interface AssetDetailsInput {
  readonly categoryId: string;
  readonly name: string;
  readonly make: string | null;
  readonly model: string | null;
  readonly serialNumber: string | null;
  readonly purchaseDate: string | null;
  readonly purchaseCost: number | null;
  readonly vendor: string | null;
  readonly warrantyUntil: string | null;
  readonly locationId: string | null;
  readonly notes: string | null;
}

export interface AssetRequestItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly categoryId: string;
  readonly categoryName: string;
  readonly reason: string;
  readonly status: AssetRequestStatus;
  readonly assetId?: string;
  readonly assetTag?: string;
  readonly response?: string;
  readonly createdDate: string;
  readonly closedOn?: string;
}

export interface AssetDashboard {
  readonly total: number;
  readonly inStock: number;
  readonly assigned: number;
  readonly inRepair: number;
  readonly bookValue: number;
  readonly warrantyExpiring: number;
  readonly openRequests: number;
  readonly byCategory: Readonly<Record<string, number>>;
}

// ----- Expenses and travel --------------------------------------------------------------------

export type ClaimStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Paid' | 'Cancelled';
export type PaymentMode = 'Payroll' | 'Direct';
export type TravelMode = 'Air' | 'Rail' | 'Road' | 'Other';
export type TravelStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Settled' | 'Cancelled';

export const CLAIM_STATUSES: readonly ClaimStatus[] = ['Draft', 'PendingApproval', 'Approved', 'Rejected', 'Paid', 'Cancelled'];
export const TRAVEL_MODES: readonly TravelMode[] = ['Air', 'Rail', 'Road', 'Other'];
export const TRAVEL_STATUSES: readonly TravelStatus[] = ['Draft', 'PendingApproval', 'Approved', 'Rejected', 'Settled', 'Cancelled'];

export interface ExpenseCategoryItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly limitPerLine?: number;
  readonly receiptRequiredAbove: number;
  readonly isActive: boolean;
}

export interface ExpenseCategoryInput {
  readonly code: string;
  readonly name: string;
  readonly limitPerLine: number | null;
  readonly receiptRequiredAbove: number;
  readonly isActive: boolean;
}

export interface ClaimLineItem {
  readonly id: string;
  readonly categoryId: string;
  readonly categoryName: string;
  readonly date: string;
  readonly description: string;
  readonly amount: number;
  readonly merchant?: string;
  readonly hasReceipt: boolean;
  readonly billable: boolean;
  readonly policyFlag?: string;
  readonly justification?: string;
}

export interface ClaimLineInput {
  readonly categoryId: string;
  readonly date: string;
  readonly description: string;
  readonly amount: number;
  readonly merchant: string | null;
  readonly hasReceipt: boolean;
  readonly billable: boolean;
  readonly justification: string | null;
}

export interface ClaimItem {
  readonly id: string;
  readonly number: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly title: string;
  readonly travelRequestId?: string;
  readonly travelNumber?: string;
  readonly projectId?: string;
  readonly projectName?: string;
  readonly currency: string;
  readonly total: number;
  readonly lines: number;
  readonly policyFlags: number;
  readonly status: ClaimStatus;
  readonly workflowInstanceId?: string;
  readonly submittedDate?: string;
  readonly paymentMode?: PaymentMode;
  readonly paymentReference?: string;
  readonly paidOn?: string;
  readonly createdDate: string;
  readonly canEdit: boolean;
}

export interface ClaimView {
  readonly claim: ClaimItem;
  readonly lines: readonly ClaimLineItem[];
}

export interface ClaimInput {
  readonly title: string;
  readonly travelRequestId: string | null;
  readonly projectId: string | null;
  readonly currency: string | null;
}

export interface TravelDetailsInput {
  readonly purpose: string;
  readonly fromCity: string;
  readonly toCity: string;
  readonly departOn: string;
  readonly returnOn: string;
  readonly mode: TravelMode;
  readonly estimatedCost: number;
  readonly advanceRequested: number;
  readonly projectId: string | null;
}

export interface TravelItem {
  readonly id: string;
  readonly number: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly purpose: string;
  readonly fromCity: string;
  readonly toCity: string;
  readonly departOn: string;
  readonly returnOn: string;
  readonly mode: TravelMode;
  readonly estimatedCost: number;
  readonly advanceRequested: number;
  readonly projectId?: string;
  readonly projectName?: string;
  readonly status: TravelStatus;
  readonly workflowInstanceId?: string;
  readonly advancePaid?: number;
  readonly advancePaidOn?: string;
  readonly claimed: number;
  readonly settlementAmount?: number;
  readonly settledOn?: string;
  readonly canEdit: boolean;
}

export interface ExpenseDashboard {
  readonly pendingApproval: number;
  readonly pendingAmount: number;
  readonly approvedUnpaid: number;
  readonly approvedUnpaidAmount: number;
  readonly paidThisMonth: number;
  readonly openAdvances: number;
  readonly byCategoryThisMonth: Readonly<Record<string, number>>;
}

// ----- Helpdesk -------------------------------------------------------------------------------

export type TicketPriority = 'Low' | 'Medium' | 'High' | 'Urgent';
export type TicketStatus = 'Open' | 'InProgress' | 'WaitingOnRequester' | 'Resolved' | 'Closed' | 'Cancelled';

export const TICKET_PRIORITIES: readonly TicketPriority[] = ['Low', 'Medium', 'High', 'Urgent'];
export const TICKET_STATUSES: readonly TicketStatus[] = ['Open', 'InProgress', 'WaitingOnRequester', 'Resolved', 'Closed', 'Cancelled'];

export interface HelpdeskCategoryItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description?: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly responseHours: number;
  readonly resolutionHours: number;
  readonly isConfidential: boolean;
  readonly isActive: boolean;
}

export interface HelpdeskCategoryInput {
  readonly code: string;
  readonly name: string;
  readonly description: string | null;
  readonly ownerEmployeeId: string | null;
  readonly responseHours: number;
  readonly resolutionHours: number;
  readonly isConfidential: boolean;
  readonly isActive: boolean;
}

export interface TicketItem {
  readonly id: string;
  readonly number: string;
  readonly requesterEmployeeId: string;
  readonly requesterName?: string;
  readonly categoryId: string;
  readonly categoryName: string;
  readonly subject: string;
  readonly priority: TicketPriority;
  readonly status: TicketStatus;
  readonly assigneeEmployeeId?: string;
  readonly assigneeName?: string;
  readonly isConfidential: boolean;
  readonly createdDate: string;
  readonly responseDueAt: string;
  readonly resolutionDueAt: string;
  readonly firstRespondedAt?: string;
  readonly resolvedAt?: string;
  readonly responseBreached: boolean;
  readonly resolutionBreached: boolean;
  readonly satisfactionRating?: number;
}

export interface TicketComment {
  readonly id: string;
  readonly authorEmployeeId?: string;
  readonly authorName?: string;
  readonly body: string;
  readonly isInternal: boolean;
  readonly createdDate: string;
}

export interface TicketView {
  readonly ticket: TicketItem;
  readonly description: string;
  readonly resolution?: string;
  readonly satisfactionComment?: string;
  readonly reopenCount: number;
  readonly comments: readonly TicketComment[];
  readonly isAgent: boolean;
  readonly isRequester: boolean;
}

export interface KnowledgeArticleItem {
  readonly id: string;
  readonly categoryId?: string;
  readonly categoryName?: string;
  readonly title: string;
  readonly tags?: string;
  readonly isPublished: boolean;
  readonly views: number;
  readonly helpfulVotes: number;
  readonly unhelpfulVotes: number;
  readonly modifiedDate?: string;
}

export interface KnowledgeArticleView {
  readonly article: KnowledgeArticleItem;
  readonly body: string;
}

export interface KnowledgeArticleInput {
  readonly categoryId: string | null;
  readonly title: string;
  readonly body: string;
  readonly tags: string | null;
  readonly publish: boolean;
}

export interface HelpdeskDashboard {
  readonly open: number;
  readonly unassigned: number;
  readonly responseBreached: number;
  readonly resolutionBreached: number;
  readonly averageResolutionHours?: number;
  readonly averageSatisfaction?: number;
  readonly responseSlaMetPercent: number;
  readonly openByCategory: Readonly<Record<string, number>>;
  readonly openByPriority: Readonly<Record<string, number>>;
}

// ----- Engagement -----------------------------------------------------------------------------

export type QuestionType = 'Rating' | 'Enps' | 'Choice' | 'Text';
export type SurveyStatus = 'Draft' | 'Scheduled' | 'Open' | 'Closed';

export const QUESTION_TYPES: readonly QuestionType[] = ['Rating', 'Enps', 'Choice', 'Text'];

export interface SurveyQuestion {
  readonly key: string;
  readonly text: string;
  readonly type: QuestionType;
  readonly options?: readonly string[] | null;
  readonly required: boolean;
}

export interface SurveyItem {
  readonly id: string;
  readonly title: string;
  readonly description?: string;
  readonly isAnonymous: boolean;
  readonly opensOn: string;
  readonly closesOn: string;
  readonly status: SurveyStatus;
  readonly questions: number;
  readonly audienceDepartmentIds: readonly string[];
  readonly invitedCount: number;
  readonly responseCount: number;
  readonly responseRate: number;
}

export interface SurveyView {
  readonly survey: SurveyItem;
  readonly questions: readonly SurveyQuestion[];
}

export interface SurveyInput {
  readonly title: string;
  readonly description: string | null;
  readonly isAnonymous: boolean;
  readonly questions: readonly SurveyQuestion[];
  readonly audienceDepartmentIds: readonly string[];
  readonly opensOn: string;
  readonly closesOn: string;
}

export interface MySurveyItem {
  readonly id: string;
  readonly title: string;
  readonly description?: string;
  readonly isAnonymous: boolean;
  readonly closesOn: string;
  readonly responded: boolean;
  readonly questions: readonly SurveyQuestion[];
}

export interface QuestionResult {
  readonly key: string;
  readonly text: string;
  readonly type: QuestionType;
  readonly answers: number;
  readonly average?: number;
  readonly distribution: Readonly<Record<string, number>>;
  readonly enpsScore?: number;
  readonly comments: readonly string[];
}

export interface GroupResult {
  readonly departmentId?: string;
  readonly department: string;
  readonly responses: number;
  readonly enps?: number;
  readonly averageRating?: number;
}

export interface SurveyResults {
  readonly survey: SurveyItem;
  readonly suppressed: boolean;
  readonly minimumGroupSize: number;
  readonly enps?: number;
  readonly promoters: number;
  readonly passives: number;
  readonly detractors: number;
  readonly questions: readonly QuestionResult[];
  readonly byDepartment: readonly GroupResult[];
  readonly suppressedGroups: number;
}

export interface EnpsPoint {
  readonly surveyId: string;
  readonly title: string;
  readonly closesOn: string;
  readonly responses: number;
  readonly enps?: number;
}

export interface EngagementDashboard {
  readonly openSurveys: number;
  readonly latestEnps?: number;
  readonly averageResponseRate: number;
  readonly enpsTrend: readonly EnpsPoint[];
}

// ----- Rewards --------------------------------------------------------------------------------

export type RecognitionKind = 'Peer' | 'Manager' | 'Award';
export type RecognitionStatus = 'Pending' | 'Approved' | 'Rejected';
export type RuleTrigger = 'WorkAnniversary' | 'ReviewRating' | 'CourseCompleted';
export type PointEntryKind = 'Earned' | 'Redeemed' | 'Expired' | 'Refunded' | 'Adjusted';
export type RedemptionStatus = 'Requested' | 'Fulfilled' | 'Cancelled';

export const RECOGNITION_KINDS: readonly RecognitionKind[] = ['Peer', 'Manager', 'Award'];
export const RULE_TRIGGERS: readonly RuleTrigger[] = ['WorkAnniversary', 'ReviewRating', 'CourseCompleted'];

export interface RewardSettings {
  readonly pointValue: number;
  readonly currency: string;
  readonly expiryMonths: number;
  readonly monthlyPeerAllowance: number;
}

export interface RecognitionTypeItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description?: string;
  readonly kind: RecognitionKind;
  readonly points: number;
  readonly requiresApproval: boolean;
  readonly cashAmount?: number;
  readonly isActive: boolean;
}

export interface RecognitionTypeInput {
  readonly code: string;
  readonly name: string;
  readonly description: string | null;
  readonly kind: RecognitionKind;
  readonly points: number;
  readonly requiresApproval: boolean;
  readonly cashAmount: number | null;
  readonly isActive: boolean;
}

export interface RewardRuleItem {
  readonly id: string;
  readonly name: string;
  readonly trigger: RuleTrigger;
  readonly points: number;
  readonly threshold: number;
  readonly perYear: boolean;
  readonly message: string;
  readonly isActive: boolean;
}

export type RewardRuleInput = Omit<RewardRuleItem, 'id'>;

export interface RecognitionItem {
  readonly id: string;
  readonly giverEmployeeId?: string;
  readonly giverName?: string;
  readonly recipientEmployeeId: string;
  readonly recipientName?: string;
  readonly typeName?: string;
  readonly ruleName?: string;
  readonly kind?: RecognitionKind;
  readonly message: string;
  readonly points: number;
  readonly cashAmount?: number;
  readonly isPublic: boolean;
  readonly status: RecognitionStatus;
  readonly givenOn: string;
  readonly cheers: number;
  readonly cheeredByMe: boolean;
}

export interface PointLedgerItem {
  readonly id: string;
  readonly kind: PointEntryKind;
  readonly points: number;
  readonly remainingPoints: number;
  readonly occurredOn: string;
  readonly expiresOn?: string;
  readonly note: string;
}

export interface RewardWallet {
  readonly balance: number;
  readonly expiringSoon: number;
  readonly nextExpiry?: string;
  readonly earnedThisYear: number;
  readonly redeemedThisYear: number;
  readonly peerAllowance: number;
  readonly peerAllowanceLeft: number;
  readonly pointValue: number;
  readonly currency: string;
}

export interface CatalogItem {
  readonly id: string;
  readonly name: string;
  readonly description?: string;
  readonly category: string;
  readonly pointsCost: number;
  readonly stock?: number;
  readonly isActive: boolean;
}

export interface CatalogItemInput {
  readonly name: string;
  readonly description: string | null;
  readonly category: string;
  readonly pointsCost: number;
  readonly stock: number | null;
  readonly isActive: boolean;
}

export interface RedemptionItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly itemId: string;
  readonly itemName: string;
  readonly points: number;
  readonly status: RedemptionStatus;
  readonly note?: string;
  readonly requestedOn: string;
  readonly fulfilledOn?: string;
}

export interface RewardBudgetItem {
  readonly id: string;
  readonly year: number;
  readonly departmentId?: string;
  readonly department: string;
  readonly points: number;
  readonly used: number;
  readonly usedPercent: number;
}

export interface LeaderEntry {
  readonly employeeId: string;
  readonly name?: string;
  readonly count: number;
  readonly points: number;
}

export interface RewardDashboard {
  readonly recognitionsThisMonth: number;
  readonly pointsAwardedThisYear: number;
  readonly valueAwardedThisYear: number;
  readonly pendingApprovals: number;
  readonly pendingRedemptions: number;
  readonly pointsOutstanding: number;
  readonly topRecipients: readonly LeaderEntry[];
  readonly topGivers: readonly LeaderEntry[];
  readonly budgets: readonly RewardBudgetItem[];
}

// ----- Separation -----------------------------------------------------------------------------

export type SeparationType = 'Resignation' | 'Termination' | 'Retirement' | 'ContractEnd' | 'Mutual' | 'Absconding' | 'Death' | 'Retrenchment';
export type SeparationStatus = 'PendingApproval' | 'Approved' | 'Rejected' | 'Withdrawn' | 'Completed';
export type ClearanceArea = 'Manager' | 'It' | 'Admin' | 'Finance' | 'Assets' | 'Hr';
export type ClearanceStatus = 'Pending' | 'Cleared' | 'Waived';
export type SettlementStatus = 'Draft' | 'Approved' | 'Paid';

export const SEPARATION_TYPES: readonly SeparationType[] = ['Resignation', 'Termination', 'Retirement', 'ContractEnd', 'Mutual', 'Absconding', 'Death', 'Retrenchment'];
export const CLEARANCE_AREAS: readonly ClearanceArea[] = ['Manager', 'It', 'Admin', 'Finance', 'Assets', 'Hr'];
export const EXIT_REASONS: readonly string[] = ['Better opportunity', 'Compensation', 'Career growth', 'Manager', 'Work-life balance', 'Relocation', 'Higher studies', 'Health', 'Personal', 'Other'];

export interface SeparationCaseItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly designation?: string;
  readonly type: SeparationType;
  readonly reason: string;
  readonly noticeDate: string;
  readonly noticePeriodDays: number;
  readonly requestedLastDay: string;
  readonly lastWorkingDay: string;
  readonly shortfallDays: number;
  readonly shortfallWaived: boolean;
  readonly eligibleForRehire: boolean;
  readonly status: SeparationStatus;
  readonly workflowInstanceId?: string;
  readonly clearancePending: number;
  readonly clearanceTotal: number;
  readonly interviewDone: boolean;
  readonly settlementStatus?: SettlementStatus;
  readonly settlementNet?: number;
  readonly relievingLetterId?: string;
  readonly experienceLetterId?: string;
  readonly reasonCode?: string;
  readonly reasonLabel?: string;
  readonly secondaryReasonCode?: string;
  readonly regretted?: boolean;
  readonly retentionNote?: string;
  readonly requestedReleaseDay?: string;
  readonly releaseWithBuyout: boolean;
  readonly gardenLeave: boolean;
  readonly payInLieuDays: number;
  readonly nomineeName?: string;
  readonly nomineeRelation?: string;
  readonly nomineeContact?: string;
  readonly awaitingConfirmation: boolean;
}

export interface SeparationEvent {
  readonly kind: string;
  readonly title: string;
  readonly note?: string;
  readonly actorName?: string;
  readonly occurredAt: string;
}

export interface SeparationReason {
  readonly code: string;
  readonly label: string;
  readonly voluntary: boolean;
}

export interface SeparationPolicy {
  readonly defaultNoticeDays: number;
  readonly probationNoticeDays: number;
  readonly buyoutAllowed: boolean;
  readonly gardenLeaveAllowed: boolean;
  readonly blockedLeaveTypeCodes: readonly string[];
  readonly encashableLeaveTypeCodes: readonly string[];
  readonly encashmentCapDays: number | null;
  readonly retirementAge: number;
  readonly abscondingDays: number;
  readonly settlementDueWorkingDays: number;
  readonly gratuityDueDays: number;
  readonly showCauseReplyDays: number;
  readonly exitProposalLeadDays: number;
}

export type ShowCauseStatus = 'Issued' | 'Replied' | 'UnderEnquiry' | 'Closed';
export type ShowCauseOutcome = 'None' | 'Warning' | 'Termination' | 'Dropped';

export interface ShowCause {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly allegation: string;
  readonly issuedOn: string;
  readonly replyDueOn: string;
  readonly noticeLetterId?: string;
  readonly reply?: string;
  readonly repliedOn?: string;
  readonly enquiryOfficerEmployeeId?: string;
  readonly enquiryOfficerName?: string;
  readonly enquiryFindings?: string;
  readonly enquiryCompletedOn?: string;
  readonly status: ShowCauseStatus;
  readonly outcome: ShowCauseOutcome;
  readonly decisionNote?: string;
  readonly decidedOn?: string;
  readonly separationCaseId?: string;
  readonly heard: boolean;
}

export type AbsenceAlertStatus = 'Open' | 'Returned' | 'Separated' | 'Dismissed';

export interface AbsenceAlert {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly firstAbsentDay: string;
  readonly lastWorkedDay: string;
  readonly workingDaysAbsent: number;
  readonly detectedOn: string;
  readonly status: AbsenceAlertStatus;
  readonly noticesSent: number;
  readonly lastNoticeOn?: string;
  readonly respondBy?: string;
  readonly lastLetterId?: string;
  readonly note?: string;
  readonly separationCaseId?: string;
  readonly canSendNotice: boolean;
  readonly canSeparate: boolean;
}

export interface RetrenchmentResult {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly caseId?: string;
  readonly payInLieuDays: number;
  readonly error?: string;
}

export interface NoticeRule {
  readonly id: string;
  readonly gradeId?: string;
  readonly employmentTypeCode?: string;
  readonly onProbation?: boolean;
  readonly noticeDays: number;
}

export type OffboardingOwner = 'Manager' | 'It' | 'Admin' | 'Finance' | 'Hr' | 'Employee' | 'Successor';
export const OFFBOARDING_OWNERS: readonly OffboardingOwner[] = ['Manager', 'Employee', 'Successor', 'Hr', 'It', 'Admin', 'Finance'];
export type OffboardingTaskStatus = 'Pending' | 'Done' | 'Skipped';
export type HandoverKind = 'DirectReport' | 'ProjectAllocation' | 'ProjectTasks' | 'Approvals';
export type HandoverStatus = 'Open' | 'Done' | 'Skipped';

export interface OffboardingTask {
  readonly id: string;
  readonly caseId: string;
  readonly title: string;
  readonly description?: string;
  readonly owner: OffboardingOwner;
  readonly assigneeEmployeeId?: string;
  readonly assigneeName?: string;
  readonly dueDate: string;
  readonly required: boolean;
  readonly status: OffboardingTaskStatus;
  readonly completedAt?: string;
  readonly completedByName?: string;
  readonly notes?: string;
  readonly overdue: boolean;
  readonly canAct: boolean;
  readonly employeeName?: string;
}

export interface HandoverItem {
  readonly id: string;
  readonly kind: HandoverKind;
  readonly title: string;
  readonly detail?: string;
  readonly toEmployeeId?: string;
  readonly toName?: string;
  readonly status: HandoverStatus;
  readonly doneAt?: string;
  readonly outcome?: string;
  readonly needsRecipient: boolean;
}

export interface OffboardingView {
  readonly successorEmployeeId?: string;
  readonly successorName?: string;
  readonly handoverNote?: string;
  readonly accessDisabledAt?: string;
  readonly accountStatus?: string;
  readonly tasks: readonly OffboardingTask[];
  readonly handover: readonly HandoverItem[];
}

export interface OffboardingStep {
  readonly title: string;
  readonly description?: string | null;
  readonly owner: OffboardingOwner;
  readonly dueOffsetDays: number;
  readonly required: boolean;
}

export interface OffboardingTemplate {
  readonly id: string;
  readonly name: string;
  readonly description?: string;
  readonly departmentId?: string;
  readonly locationId?: string;
  readonly employmentTypeCode?: string;
  readonly isDefault: boolean;
  readonly isActive: boolean;
  readonly steps: readonly OffboardingStep[];
}

export interface OffboardingTemplateInput {
  readonly name: string;
  readonly description: string | null;
  readonly departmentId: string | null;
  readonly locationId: string | null;
  readonly employmentTypeCode: string | null;
  readonly isDefault: boolean;
  readonly isActive: boolean;
  readonly steps: readonly OffboardingStep[];
}

export interface ClearanceItem {
  readonly id: string;
  readonly area: ClearanceArea;
  readonly title: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly status: ClearanceStatus;
  readonly recoveryAmount: number;
  readonly note?: string;
  readonly clearedOn?: string;
  readonly canResolve: boolean;
}

export interface ExitInterviewView {
  readonly primaryReason: string;
  readonly overallRating: number;
  readonly wouldRecommend: boolean;
  readonly wouldReturn: boolean;
  readonly whatWentWell?: string;
  readonly whatToImprove?: string;
  readonly conductedOn: string;
}

export interface ExitInterviewInput {
  readonly primaryReason: string;
  readonly overallRating: number;
  readonly wouldRecommend: boolean;
  readonly wouldReturn: boolean;
  readonly whatWentWell: string | null;
  readonly whatToImprove: string | null;
}

export interface SettlementLine {
  readonly id: string;
  readonly code: string;
  readonly label: string;
  readonly isDeduction: boolean;
  readonly amount: number;
  readonly isManual: boolean;
  readonly basis?: string;
}

export interface SettlementView {
  readonly id: string;
  readonly status: SettlementStatus;
  readonly earnings: number;
  readonly deductions: number;
  readonly net: number;
  readonly paidOn?: string;
  readonly paymentReference?: string;
  readonly lines: readonly SettlementLine[];
}

export interface SeparationCaseView {
  readonly case: SeparationCaseItem;
  readonly clearance: readonly ClearanceItem[];
  readonly interview?: ExitInterviewView;
  readonly settlement?: SettlementView;
  readonly events?: readonly SeparationEvent[];
  readonly offboarding?: OffboardingView;
}

export interface AlumniItem {
  readonly id: string;
  readonly employeeId: string;
  readonly name: string;
  readonly email?: string;
  readonly lastDesignation?: string;
  readonly exitDate: string;
  readonly separationType: SeparationType;
  readonly eligibleForRehire: boolean;
  readonly optedIn: boolean;
  readonly notes?: string;
}

export interface SeparationDashboard {
  readonly inProgress: number;
  readonly pendingApproval: number;
  readonly leavingThisMonth: number;
  readonly clearancePending: number;
  readonly settlementsDue: number;
  readonly attritionLast12Months: number;
  readonly byType: Readonly<Record<string, number>>;
  readonly topExitReasons: Readonly<Record<string, number>>;
}
