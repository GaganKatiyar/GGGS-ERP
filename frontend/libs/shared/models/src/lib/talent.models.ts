/** Talent contracts (M9): goals, performance reviews, improvement plans, learning and career, as the API returns them. */

// ----- Goals ----------------------------------------------------------------------------------

export type GoalLevel = 'Company' | 'Department' | 'Team' | 'Individual';
export type GoalType = 'Okr' | 'Kpi';
export type GoalStatus = 'Draft' | 'Active' | 'Completed' | 'Cancelled';
export type Confidence = 'OnTrack' | 'AtRisk' | 'OffTrack';
export type MetricType = 'Number' | 'Percent' | 'Currency' | 'Milestone';

export const GOAL_LEVELS: readonly GoalLevel[] = ['Company', 'Department', 'Team', 'Individual'];
export const CONFIDENCES: readonly Confidence[] = ['OnTrack', 'AtRisk', 'OffTrack'];
export const METRIC_TYPES: readonly MetricType[] = ['Number', 'Percent', 'Currency', 'Milestone'];

export interface GoalItem {
  readonly id: string;
  readonly title: string;
  readonly description?: string;
  readonly type: GoalType;
  readonly level: GoalLevel;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly departmentId?: string;
  readonly departmentName?: string;
  readonly parentGoalId?: string;
  readonly parentTitle?: string;
  readonly cycleId?: string;
  readonly startDate: string;
  readonly dueDate: string;
  readonly weight: number;
  readonly status: GoalStatus;
  readonly progress: number;
  readonly confidence?: Confidence;
  readonly lastCheckInOn?: string;
  readonly keyResults: number;
  readonly children: number;
  readonly canEdit: boolean;
}

export interface KeyResultItem {
  readonly id: string;
  readonly title: string;
  readonly metricType: MetricType;
  readonly startValue: number;
  readonly targetValue: number;
  readonly currentValue: number;
  readonly unit?: string;
  readonly weight: number;
  readonly progress: number;
}

export interface CheckInItem {
  readonly id: string;
  readonly checkInOn: string;
  readonly progress: number;
  readonly confidence: Confidence;
  readonly note?: string;
  readonly byName?: string;
}

export interface GoalView {
  readonly goal: GoalItem;
  readonly keyResults: readonly KeyResultItem[];
  readonly checkIns: readonly CheckInItem[];
  readonly children: readonly GoalItem[];
}

export interface GoalDetailsInput {
  readonly title: string;
  readonly description?: string | null;
  readonly type: GoalType;
  readonly level: GoalLevel;
  readonly ownerEmployeeId?: string | null;
  readonly departmentId?: string | null;
  readonly parentGoalId?: string | null;
  readonly cycleId?: string | null;
  readonly startDate: string;
  readonly dueDate: string;
  readonly weight: number;
}

export interface KeyResultInput {
  readonly id?: string | null;
  readonly title: string;
  readonly metricType: MetricType;
  readonly startValue: number;
  readonly targetValue: number;
  readonly unit?: string | null;
  readonly weight: number;
}

// ----- Reviews --------------------------------------------------------------------------------

export type CycleType = 'Annual' | 'HalfYearly' | 'Quarterly' | 'Probation';
export type ReviewCycleStatus = 'Draft' | 'Active' | 'Calibration' | 'Released' | 'Closed';
export type ReviewStatus = 'Pending' | 'SelfSubmitted' | 'ManagerSubmitted' | 'Released' | 'Acknowledged';
export type FeedbackRelationship = 'Peer' | 'DirectReport' | 'Manager' | 'Other';
export type FeedbackStatus = 'Requested' | 'Submitted' | 'Declined';
export type PipStatus = 'Draft' | 'Active' | 'Succeeded' | 'Failed' | 'Cancelled';

export const CYCLE_TYPES: readonly CycleType[] = ['Annual', 'HalfYearly', 'Quarterly', 'Probation'];
export const FEEDBACK_RELATIONSHIPS: readonly FeedbackRelationship[] = ['Peer', 'DirectReport', 'Manager', 'Other'];

/** The five-point scale, as the API labels it. */
export const RATING_LABELS: Readonly<Record<number, string>> = {
  1: 'Unsatisfactory',
  2: 'Needs improvement',
  3: 'Meets expectations',
  4: 'Exceeds expectations',
  5: 'Outstanding',
};

export interface ReviewCycleItem {
  readonly id: string;
  readonly name: string;
  readonly type: CycleType;
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly selfReviewDue: string;
  readonly managerReviewDue: string;
  readonly competencies: readonly string[];
  readonly goalWeight: number;
  readonly peerFeedback: boolean;
  readonly departmentIds: readonly string[];
  readonly status: ReviewCycleStatus;
  readonly launchedOn?: string;
  readonly releasedOn?: string;
  readonly reviews: number;
  readonly byStatus: Readonly<Record<string, number>>;
}

export interface ReviewCycleInput {
  readonly name: string;
  readonly type: CycleType;
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly selfReviewDue: string;
  readonly managerReviewDue: string;
  readonly competencies: readonly string[];
  readonly goalWeight: number;
  readonly peerFeedback: boolean;
  readonly departmentIds: readonly string[];
}

export interface ReviewItem {
  readonly id: string;
  readonly cycleId: string;
  readonly cycleName: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly designation?: string;
  readonly departmentName?: string;
  readonly managerEmployeeId?: string;
  readonly managerName?: string;
  readonly status: ReviewStatus;
  readonly selfRating?: number;
  readonly managerRating?: number;
  readonly score?: number;
  readonly calibratedRating?: number;
  readonly finalRating?: number;
  readonly finalLabel?: string;
  readonly selfSubmittedDate?: string;
  readonly managerSubmittedDate?: string;
  readonly releasedDate?: string;
}

export interface ReviewInput {
  readonly ratings?: Readonly<Record<string, number>> | null;
  readonly strengths?: string | null;
  readonly improvements?: string | null;
  readonly comments?: string | null;
  readonly overall?: number | null;
}

export interface ReviewGoal {
  readonly id: string;
  readonly title: string;
  readonly status: GoalStatus;
  readonly weight: number;
  readonly progress: number;
}

export interface ProjectEvidence {
  readonly projectId?: string;
  readonly project: string;
  readonly hours: number;
  readonly role?: string;
}

export interface LearningCompletion {
  readonly employeeId: string;
  readonly courseId: string;
  readonly courseCode: string;
  readonly courseTitle: string;
  readonly completedOn: string;
  readonly certificateExpiresOn?: string;
}

export interface ReviewEvidence {
  readonly goals: readonly ReviewGoal[];
  readonly goalAchievement?: number;
  readonly goalRating?: number;
  readonly projects: readonly ProjectEvidence[];
  readonly learning: readonly LearningCompletion[];
  readonly skills: readonly string[];
}

export interface FeedbackItem {
  readonly id: string;
  readonly reviewId: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly reviewerEmployeeId: string;
  readonly reviewerName?: string;
  readonly relationship: FeedbackRelationship;
  readonly status: FeedbackStatus;
  readonly ratings: Readonly<Record<string, number>>;
  readonly strengths?: string;
  readonly improvements?: string;
  readonly respondedDate?: string;
  readonly cycleName: string;
  readonly competencies: readonly string[];
}

export interface ReviewView {
  readonly review: ReviewItem;
  readonly cycle: ReviewCycleItem;
  readonly self: ReviewInput;
  readonly manager?: ReviewInput;
  readonly calibrationNote?: string;
  readonly employeeComment?: string;
  readonly evidence: ReviewEvidence;
  readonly feedback: readonly FeedbackItem[];
  readonly suggestedScore?: number;
  readonly isEmployee: boolean;
  readonly canEditSelf: boolean;
  readonly canEditManager: boolean;
  readonly canCalibrate: boolean;
  readonly canRequestFeedback: boolean;
}

export interface DistributionRow {
  readonly rating: number;
  readonly label: string;
  readonly count: number;
  readonly percent: number;
  readonly targetPercent: number;
}

export interface CalibrationView {
  readonly cycle: ReviewCycleItem;
  readonly reviews: readonly ReviewItem[];
  readonly distribution: readonly DistributionRow[];
  readonly awaitingManager: number;
}

export interface PipObjective {
  readonly title: string;
  readonly measure?: string | null;
  readonly met?: boolean | null;
}

export interface PipItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly managerEmployeeId?: string;
  readonly managerName?: string;
  readonly reviewId?: string;
  readonly reason: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly objectives: readonly PipObjective[];
  readonly notes: readonly { readonly on: string; readonly byName?: string; readonly text: string }[];
  readonly status: PipStatus;
  readonly extended: boolean;
  readonly outcome?: string;
  readonly closedOn?: string;
  readonly daysLeft: number;
  readonly canManage: boolean;
}

export interface PipDetailsInput {
  readonly reason: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly objectives: readonly PipObjective[];
  readonly reviewId?: string | null;
}

// ----- Learning -------------------------------------------------------------------------------

export type CourseFormat = 'Online' | 'Classroom' | 'Virtual' | 'Blended';
export type LearningSource = 'Self' | 'Assigned' | 'Joiner' | 'Renewal' | 'Path';
export type LearningEnrollmentStatus = 'Assigned' | 'InProgress' | 'Completed' | 'Cancelled' | 'Expired';

export const COURSE_FORMATS: readonly CourseFormat[] = ['Online', 'Classroom', 'Virtual', 'Blended'];

export interface CourseItem {
  readonly id: string;
  readonly code: string;
  readonly title: string;
  readonly description?: string;
  readonly category?: string;
  readonly format: CourseFormat;
  readonly provider?: string;
  readonly durationHours: number;
  readonly contentUrl?: string;
  readonly mandatory: boolean;
  readonly assignToNewJoiners: boolean;
  readonly completionDays?: number;
  readonly certificateValidityMonths?: number;
  readonly skills: readonly string[];
  readonly isActive: boolean;
  readonly enrolled: number;
  readonly completed: number;
  readonly averageRating?: number;
}

export interface CourseInput {
  readonly code: string;
  readonly title: string;
  readonly description?: string | null;
  readonly category?: string | null;
  readonly format: CourseFormat;
  readonly provider?: string | null;
  readonly durationHours: number;
  readonly contentUrl?: string | null;
  readonly mandatory: boolean;
  readonly assignToNewJoiners: boolean;
  readonly completionDays?: number | null;
  readonly certificateValidityMonths?: number | null;
  readonly skills: readonly string[];
}

export interface LearningPathItem {
  readonly id: string;
  readonly code: string;
  readonly title: string;
  readonly description?: string;
  readonly courseIds: readonly string[];
  readonly courseTitles: readonly string[];
  readonly durationHours: number;
  readonly isActive: boolean;
}

export interface LearningEnrollment {
  readonly id: string;
  readonly courseId: string;
  readonly courseCode: string;
  readonly courseTitle: string;
  readonly format: CourseFormat;
  readonly durationHours: number;
  readonly contentUrl?: string;
  readonly mandatory: boolean;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly pathId?: string;
  readonly source: LearningSource;
  readonly assignedByName?: string;
  readonly dueDate?: string;
  readonly status: LearningEnrollmentStatus;
  readonly progress: number;
  readonly startedOn?: string;
  readonly completedOn?: string;
  readonly score?: number;
  readonly certificateNumber?: string;
  readonly certificateExpiresOn?: string;
  readonly rating?: number;
  readonly overdue: boolean;
  readonly canUpdate: boolean;
}

export interface CatalogEntry {
  readonly course: CourseItem;
  readonly mine?: LearningEnrollment;
}

export interface MyLearning {
  readonly active: readonly LearningEnrollment[];
  readonly completed: readonly LearningEnrollment[];
  readonly certificates: readonly LearningEnrollment[];
  readonly hoursThisYear: number;
}

export interface LearningDashboard {
  readonly assigned: number;
  readonly inProgress: number;
  readonly completedLast90Days: number;
  readonly overdue: number;
  readonly mandatoryCompliancePercent?: number;
  readonly certificatesExpiringIn30Days: number;
  readonly certificatesExpired: number;
  readonly topCourses: readonly CourseItem[];
}

// ----- Career ---------------------------------------------------------------------------------

export type CareerTrack = 'Individual' | 'Management';
export type PromotionStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Applied' | 'Withdrawn';
export type Criticality = 'Low' | 'Medium' | 'High' | 'Critical';
export type Readiness = 'ReadyNow' | 'OneToTwoYears' | 'ThreeToFiveYears';
export type TalentLevel = 'Low' | 'Medium' | 'High';

export const CRITICALITIES: readonly Criticality[] = ['Low', 'Medium', 'High', 'Critical'];
export const READINESS: readonly Readiness[] = ['ReadyNow', 'OneToTwoYears', 'ThreeToFiveYears'];
export const TALENT_LEVELS: readonly TalentLevel[] = ['Low', 'Medium', 'High'];

export interface CareerStepItem {
  readonly index: number;
  readonly title: string;
  readonly jobId?: string;
  readonly jobName?: string;
  readonly gradeId?: string;
  readonly gradeName?: string;
  readonly minMonthsInRole: number;
  readonly skills: readonly string[];
  readonly description?: string;
}

export interface CareerPathItem {
  readonly id: string;
  readonly name: string;
  readonly description?: string;
  readonly track: CareerTrack;
  readonly departmentId?: string;
  readonly departmentName?: string;
  readonly steps: readonly CareerStepItem[];
  readonly isActive: boolean;
}

export interface CareerStepInput {
  readonly title: string;
  readonly jobId?: string | null;
  readonly gradeId?: string | null;
  readonly minMonthsInRole: number;
  readonly skills: readonly string[];
  readonly description?: string | null;
}

export interface CareerPosition {
  readonly path: CareerPathItem;
  readonly currentStep: number;
  readonly next?: CareerStepItem;
  readonly skillsToBuild: readonly string[];
  readonly monthsInRole: number;
  readonly eligibleByTime: boolean;
}

export interface RatingInfo {
  readonly employeeId: string;
  readonly cycleId: string;
  readonly cycleName: string;
  readonly rating: number;
  readonly label: string;
  readonly releasedOn: string;
}

export interface PromotionItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly departmentName?: string;
  readonly nominatedByEmployeeId?: string;
  readonly nominatedByName?: string;
  readonly currentDesignation?: string;
  readonly currentAnnualCtc?: number;
  readonly proposedDesignation: string;
  readonly proposedJobId?: string;
  readonly proposedJobName?: string;
  readonly proposedGradeId?: string;
  readonly proposedGradeName?: string;
  readonly proposedPositionId?: string;
  readonly proposedAnnualCtc?: number;
  readonly increasePercent?: number;
  readonly effectiveDate: string;
  readonly justification: string;
  readonly rating?: number;
  readonly status: PromotionStatus;
  readonly workflowInstanceId?: string;
  readonly decidedDate?: string;
  readonly appliedOn?: string;
  readonly createdDate: string;
  readonly canEdit: boolean;
}

export interface PromotionDetailsInput {
  readonly proposedDesignation: string;
  readonly proposedJobId?: string | null;
  readonly proposedGradeId?: string | null;
  readonly proposedPositionId?: string | null;
  readonly proposedAnnualCtc?: number | null;
  readonly effectiveDate: string;
  readonly justification: string;
}

export interface MyCareer {
  readonly designation?: string;
  readonly jobName?: string;
  readonly gradeName?: string;
  readonly joiningDate?: string;
  readonly latestRating?: RatingInfo;
  readonly paths: readonly CareerPosition[];
  readonly promotions: readonly PromotionItem[];
}

export interface SuccessorItem {
  readonly employeeId: string;
  readonly name?: string;
  readonly designation?: string;
  readonly readiness: Readiness;
  readonly notes?: string;
  readonly rating?: number;
  readonly potential?: TalentLevel;
}

export interface SuccessionItem {
  readonly id: string;
  readonly roleTitle: string;
  readonly positionId?: string;
  readonly departmentId?: string;
  readonly departmentName?: string;
  readonly incumbentEmployeeId?: string;
  readonly incumbentName?: string;
  readonly criticality: Criticality;
  readonly vacancyRisk: Criticality;
  readonly notes?: string;
  readonly successors: readonly SuccessorItem[];
  readonly covered: boolean;
  readonly canManage: boolean;
}

export interface SuccessionInput {
  readonly roleTitle: string;
  readonly positionId?: string | null;
  readonly departmentId?: string | null;
  readonly incumbentEmployeeId?: string | null;
  readonly criticality: Criticality;
  readonly vacancyRisk: Criticality;
  readonly notes?: string | null;
  readonly successors: readonly { readonly employeeId: string; readonly readiness: Readiness; readonly notes?: string | null }[];
}

export interface TalentRow {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly designation?: string;
  readonly departmentName?: string;
  readonly rating?: number;
  readonly performance?: TalentLevel;
  readonly potential?: TalentLevel;
  readonly flightRisk?: TalentLevel;
  readonly box?: number;
  readonly boxLabel: string;
  readonly assessedOn?: string;
}

export interface NineBoxView {
  readonly people: readonly TalentRow[];
  readonly boxes: Readonly<Record<string, number>>;
  readonly unassessed: number;
  readonly unrated: number;
}
