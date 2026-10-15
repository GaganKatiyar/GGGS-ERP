/** Delivery contracts (M5): clients, contracts and projects, as the API returns them. */

// ----- Clients -------------------------------------------------------------------------------

export type ClientStatus = 'Prospect' | 'Active' | 'Inactive';

export interface ClientItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly legalName?: string;
  readonly industryCode?: string;
  readonly industryName?: string;
  readonly countryCode?: string;
  readonly stateCode?: string;
  readonly city?: string;
  readonly address?: string;
  readonly postalCode?: string;
  readonly website?: string;
  readonly email?: string;
  readonly phone?: string;
  readonly taxId?: string;
  readonly accountManagerEmployeeId?: string;
  readonly accountManagerName?: string;
  readonly status: ClientStatus;
  readonly notes?: string;
  readonly primaryContactName?: string;
  readonly primaryContactEmail?: string;
  readonly contactCount: number;
  readonly createdDate: string;
}

export interface ContactItem {
  readonly id: string;
  readonly clientId: string;
  readonly clientName?: string;
  readonly name: string;
  readonly designation?: string;
  readonly email?: string;
  readonly phone?: string;
  readonly department?: string;
  readonly isPrimary: boolean;
  readonly notes?: string;
}

export interface ClientDetail {
  readonly client: ClientItem;
  readonly contacts: readonly ContactItem[];
}

export interface ClientInput {
  readonly code: string;
  readonly name: string;
  readonly legalName?: string | null;
  readonly industryCode?: string | null;
  readonly countryCode?: string | null;
  readonly stateCode?: string | null;
  readonly city?: string | null;
  readonly address?: string | null;
  readonly postalCode?: string | null;
  readonly website?: string | null;
  readonly email?: string | null;
  readonly phone?: string | null;
  readonly taxId?: string | null;
  readonly accountManagerEmployeeId?: string | null;
  readonly notes?: string | null;
  readonly status?: ClientStatus | null;
  readonly primaryContact?: ContactInput | null;
}

export interface ContactInput {
  readonly name: string;
  readonly designation?: string | null;
  readonly email?: string | null;
  readonly phone?: string | null;
  readonly department?: string | null;
  readonly notes?: string | null;
  readonly isPrimary?: boolean | null;
}

// ----- Contracts -----------------------------------------------------------------------------

export type ContractType = 'Msa' | 'Sow' | 'PurchaseOrder' | 'Sla' | 'Retainer' | 'TimeAndMaterial';
export type BillingModel = 'FixedPrice' | 'TimeAndMaterial' | 'Milestone' | 'Retainer' | 'NonBillable';
export type ContractStatus = 'Draft' | 'PendingApproval' | 'Active' | 'Expired' | 'Terminated';

export const CONTRACT_TYPE_LABELS: Readonly<Record<ContractType, string>> = {
  Msa: 'MSA',
  Sow: 'SOW',
  PurchaseOrder: 'Purchase order',
  Sla: 'SLA',
  Retainer: 'Retainer',
  TimeAndMaterial: 'Time & material',
};

export const BILLING_MODEL_LABELS: Readonly<Record<BillingModel, string>> = {
  FixedPrice: 'Fixed price',
  TimeAndMaterial: 'Time & material',
  Milestone: 'Milestone',
  Retainer: 'Retainer',
  NonBillable: 'Non-billable',
};

export interface ContractItem {
  readonly id: string;
  readonly number: string;
  readonly clientId: string;
  readonly clientName?: string;
  readonly title: string;
  readonly type: ContractType;
  readonly parentContractId?: string;
  readonly parentNumber?: string;
  readonly startDate: string;
  readonly endDate?: string;
  readonly currency: string;
  readonly value?: number;
  readonly billingModel: BillingModel;
  readonly paymentTermsDays?: number;
  readonly paymentTerms?: string;
  readonly taxTerms?: string;
  readonly renewalDate?: string;
  readonly autoRenew: boolean;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly clientContactId?: string;
  readonly notes?: string;
  readonly status: ContractStatus;
  readonly workflowInstanceId?: string;
  readonly terminatedOn?: string;
  readonly terminationReason?: string;
  readonly daysToRenewal?: number;
}

export interface ContractDetail {
  readonly contract: ContractItem;
  readonly children: readonly ContractItem[];
}

export interface ContractInput {
  readonly number?: string | null;
  readonly clientId: string;
  readonly title: string;
  readonly type: ContractType;
  readonly parentContractId?: string | null;
  readonly startDate: string;
  readonly endDate?: string | null;
  readonly currency: string;
  readonly value?: number | null;
  readonly billingModel: BillingModel;
  readonly paymentTermsDays?: number | null;
  readonly paymentTerms?: string | null;
  readonly taxTerms?: string | null;
  readonly renewalDate?: string | null;
  readonly autoRenew: boolean;
  readonly ownerEmployeeId?: string | null;
  readonly clientContactId?: string | null;
  readonly notes?: string | null;
}

// ----- Projects ------------------------------------------------------------------------------

export type ProjectType = 'FixedPrice' | 'TimeAndMaterial' | 'Retainer' | 'Internal' | 'RnD' | 'Support' | 'ManagedService' | 'Implementation' | 'Consulting';
export type ProjectStage = 'Opportunity' | 'Proposal' | 'Won' | 'Planning' | 'Kickoff' | 'Execution' | 'Testing' | 'Uat' | 'Deployment' | 'Support' | 'Closure';
export type ProjectStatus = 'Active' | 'OnHold' | 'Completed' | 'Cancelled';
export type WorkPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type ProjectBillingType = 'FixedPrice' | 'TimeAndMaterial' | 'Milestone' | 'Retainer' | 'NonBillable';
export type Confidentiality = 'Public' | 'Internal' | 'Confidential' | 'Restricted';
export type DeliveryMethodology = 'Waterfall' | 'Agile' | 'Hybrid';
export type HealthRating = 'NotAssessed' | 'Green' | 'Amber' | 'Red';

export const PROJECT_STAGES: readonly ProjectStage[] = ['Opportunity', 'Proposal', 'Won', 'Planning', 'Kickoff', 'Execution', 'Testing', 'Uat', 'Deployment', 'Support', 'Closure'];

export const PROJECT_TYPE_LABELS: Readonly<Record<ProjectType, string>> = {
  FixedPrice: 'Fixed price',
  TimeAndMaterial: 'Time & material',
  Retainer: 'Retainer',
  Internal: 'Internal',
  RnD: 'R&D',
  Support: 'Support',
  ManagedService: 'Managed service',
  Implementation: 'Implementation',
  Consulting: 'Consulting',
};

export const STAGE_LABELS: Readonly<Record<ProjectStage, string>> = {
  Opportunity: 'Opportunity',
  Proposal: 'Proposal',
  Won: 'Won',
  Planning: 'Planning',
  Kickoff: 'Kick-off',
  Execution: 'Execution',
  Testing: 'Testing',
  Uat: 'UAT',
  Deployment: 'Deployment',
  Support: 'Support',
  Closure: 'Closure',
};

export interface ProjectItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly type: ProjectType;
  readonly category?: string;
  readonly clientId?: string;
  readonly clientName?: string;
  readonly contractId?: string;
  readonly contractNumber?: string;
  readonly businessUnitId?: string;
  readonly businessUnitName?: string;
  readonly departmentId?: string;
  readonly departmentName?: string;
  readonly projectManagerEmployeeId?: string;
  readonly projectManagerName?: string;
  readonly deliveryManagerEmployeeId?: string;
  readonly deliveryManagerName?: string;
  readonly accountManagerEmployeeId?: string;
  readonly accountManagerName?: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly plannedEndDate?: string;
  readonly actualEndDate?: string;
  readonly priority: WorkPriority;
  readonly status: ProjectStatus;
  readonly stage: ProjectStage;
  readonly healthStatus: HealthRating;
  readonly healthAssessedOn?: string;
  readonly billingType: ProjectBillingType;
  readonly currency: string;
  readonly budget?: number;
  readonly revenueTarget?: number;
  readonly costBudget?: number;
  readonly description?: string;
  readonly locationId?: string;
  readonly locationName?: string;
  readonly workModeCode?: string;
  readonly confidentiality: Confidentiality;
  readonly methodology: DeliveryMethodology;
  readonly clientSatisfaction?: number;
  readonly progressPercent: number;
  readonly teamSize: number;
  readonly openTasks: number;
}

export interface ProjectAbilities {
  readonly manage: boolean;
  readonly manageTasks: boolean;
  readonly manageMilestones: boolean;
  readonly manageSprints: boolean;
  readonly manageChangeRequests: boolean;
  readonly manageRaid: boolean;
  readonly seeRates: boolean;
}

export interface ProjectDetail {
  readonly project: ProjectItem;
  readonly can: ProjectAbilities;
}

export interface ProjectInput {
  readonly code: string;
  readonly name: string;
  readonly type: ProjectType;
  readonly category?: string | null;
  readonly clientId?: string | null;
  readonly contractId?: string | null;
  readonly businessUnitId?: string | null;
  readonly departmentId?: string | null;
  readonly projectManagerEmployeeId?: string | null;
  readonly deliveryManagerEmployeeId?: string | null;
  readonly accountManagerEmployeeId?: string | null;
  readonly startDate: string;
  readonly endDate: string;
  readonly plannedEndDate?: string | null;
  readonly priority: WorkPriority;
  readonly billingType: ProjectBillingType;
  readonly currency: string;
  readonly budget?: number | null;
  readonly revenueTarget?: number | null;
  readonly costBudget?: number | null;
  readonly description?: string | null;
  readonly locationId?: string | null;
  readonly workModeCode?: string | null;
  readonly confidentiality: Confidentiality;
  readonly methodology: DeliveryMethodology;
  readonly stage?: ProjectStage | null;
}

export interface PortfolioSummary {
  readonly total: number;
  readonly byStatus: Readonly<Record<string, number>>;
  readonly byStage: Readonly<Record<string, number>>;
  readonly byHealth: Readonly<Record<string, number>>;
  readonly overdueMilestones: number;
  readonly openHighRisks: number;
  readonly pendingChangeRequests: number;
}

export interface HistoryItem {
  readonly id: string;
  readonly kind: string;
  readonly fromValue: string;
  readonly toValue: string;
  readonly comment?: string;
  readonly changedByEmployeeId?: string;
  readonly changedByName?: string;
  readonly changedDate: string;
}

export type TeamMemberStatus = 'Active' | 'Released';

export interface TeamMember {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly designation?: string;
  readonly projectRoleId: string;
  readonly roleName?: string;
  readonly startDate: string;
  readonly endDate?: string;
  readonly allocationPercent: number;
  readonly billable: boolean;
  readonly billingRate?: number;
  readonly costRate?: number;
  readonly status: TeamMemberStatus;
  readonly notes?: string;
  readonly totalAllocationPercent: number;
  readonly overAllocated: boolean;
}

export interface TeamMemberInput {
  readonly employeeId: string;
  readonly projectRoleId: string;
  readonly startDate: string;
  readonly endDate?: string | null;
  readonly allocationPercent: number;
  readonly billable: boolean;
  readonly billingRate?: number | null;
  readonly costRate?: number | null;
  readonly notes?: string | null;
}

export interface ProjectMembership {
  readonly memberId: string;
  readonly projectId: string;
  readonly projectCode: string;
  readonly projectName: string;
  readonly projectStatus: ProjectStatus;
  readonly employeeId: string;
  readonly roleName: string;
  readonly allocationPercent: number;
  readonly startDate: string;
  readonly endDate?: string;
  readonly billable: boolean;
  readonly isActive: boolean;
}

export type PhaseStatus = 'NotStarted' | 'InProgress' | 'Completed' | 'OnHold';

export interface Phase {
  readonly id: string;
  readonly name: string;
  readonly sortOrder: number;
  readonly startDate?: string;
  readonly endDate?: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly budget?: number;
  readonly status: PhaseStatus;
  readonly progress: number;
  readonly measuredProgress: number;
  readonly taskCount: number;
}

export interface PhaseInput {
  readonly name: string;
  readonly sortOrder?: number | null;
  readonly startDate?: string | null;
  readonly endDate?: string | null;
  readonly ownerEmployeeId?: string | null;
  readonly budget?: number | null;
  readonly status: PhaseStatus;
  readonly progress: number;
}

export type ColumnCategory = 'Backlog' | 'Todo' | 'InProgress' | 'Done';

export interface BoardColumn {
  readonly id: string;
  readonly name: string;
  readonly sortOrder: number;
  readonly category: ColumnCategory;
  readonly wipLimit?: number;
  readonly taskCount: number;
}

export interface ColumnInput {
  readonly id?: string | null;
  readonly name: string;
  readonly category: ColumnCategory;
  readonly wipLimit?: number | null;
}

export type TaskType = 'Epic' | 'Story' | 'Task' | 'Subtask' | 'Bug' | 'ChangeRequest';

export const TASK_TYPES: readonly TaskType[] = ['Epic', 'Story', 'Task', 'Subtask', 'Bug', 'ChangeRequest'];

export const TASK_TYPE_ICONS: Readonly<Record<TaskType, string>> = {
  Epic: 'pi pi-bolt',
  Story: 'pi pi-bookmark',
  Task: 'pi pi-check-square',
  Subtask: 'pi pi-minus',
  Bug: 'pi pi-exclamation-circle',
  ChangeRequest: 'pi pi-arrow-right-arrow-left',
};

export interface ProjectTask {
  readonly id: string;
  readonly projectId: string;
  readonly projectCode?: string;
  readonly projectName?: string;
  readonly number: number;
  readonly code: string;
  readonly type: TaskType;
  readonly parentTaskId?: string;
  readonly parentCode?: string;
  readonly phaseId?: string;
  readonly milestoneId?: string;
  readonly sprintId?: string;
  readonly requirementId?: string;
  readonly title: string;
  readonly description?: string;
  readonly assigneeEmployeeId?: string;
  readonly assigneeName?: string;
  readonly reporterEmployeeId?: string;
  readonly reporterName?: string;
  readonly priority: WorkPriority;
  readonly columnId: string;
  readonly columnName?: string;
  readonly category: ColumnCategory;
  readonly startDate?: string;
  readonly dueDate?: string;
  readonly estimatedHours?: number;
  readonly actualHours?: number;
  readonly storyPoints?: number;
  readonly progress: number;
  readonly billable: boolean;
  readonly rank: number;
  readonly completedDate?: string;
  readonly isOverdue: boolean;
  readonly childCount: number;
  readonly openPredecessors: number;
}

export interface TaskInput {
  readonly type: TaskType;
  readonly parentTaskId?: string | null;
  readonly phaseId?: string | null;
  readonly milestoneId?: string | null;
  readonly sprintId?: string | null;
  readonly requirementId?: string | null;
  readonly columnId?: string | null;
  readonly title: string;
  readonly description?: string | null;
  readonly assigneeEmployeeId?: string | null;
  readonly priority: WorkPriority;
  readonly startDate?: string | null;
  readonly dueDate?: string | null;
  readonly estimatedHours?: number | null;
  readonly actualHours?: number | null;
  readonly storyPoints?: number | null;
  readonly progress?: number | null;
  readonly billable?: boolean | null;
}

export interface BoardView {
  readonly columns: readonly BoardColumn[];
  readonly tasks: readonly ProjectTask[];
}

export type DependencyType = 'FinishToStart' | 'StartToStart' | 'FinishToFinish' | 'StartToFinish';

export const DEPENDENCY_LABELS: Readonly<Record<DependencyType, string>> = {
  FinishToStart: 'Finish to start',
  StartToStart: 'Start to start',
  FinishToFinish: 'Finish to finish',
  StartToFinish: 'Start to finish',
};

export interface Dependency {
  readonly id: string;
  readonly predecessorTaskId: string;
  readonly predecessorCode?: string;
  readonly predecessorTitle?: string;
  readonly predecessorDone: boolean;
  readonly successorTaskId: string;
  readonly successorCode?: string;
  readonly successorTitle?: string;
  readonly type: DependencyType;
  readonly lagDays: number;
}

export type RequirementType = 'Business' | 'Functional' | 'NonFunctional' | 'Technical' | 'Compliance';
export type RequirementPriority = 'Must' | 'Should' | 'Could' | 'Wont';
export type RequirementStatus = 'Draft' | 'Approved' | 'InProgress' | 'Implemented' | 'Verified' | 'Rejected' | 'Deferred';

export interface Requirement {
  readonly id: string;
  readonly code: string;
  readonly title: string;
  readonly description?: string;
  readonly type: RequirementType;
  readonly priority: RequirementPriority;
  readonly source?: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly status: RequirementStatus;
  readonly acceptanceCriteria?: string;
  readonly taskCount: number;
  readonly doneTaskCount: number;
}

export interface RequirementInput {
  readonly title: string;
  readonly description?: string | null;
  readonly type: RequirementType;
  readonly priority: RequirementPriority;
  readonly source?: string | null;
  readonly ownerEmployeeId?: string | null;
  readonly status?: RequirementStatus | null;
  readonly acceptanceCriteria?: string | null;
}

export type MilestoneStatus = 'Planned' | 'InProgress' | 'Achieved' | 'Cancelled';

export interface Milestone {
  readonly id: string;
  readonly projectId: string;
  readonly projectCode?: string;
  readonly projectName?: string;
  readonly name: string;
  readonly description?: string;
  readonly plannedDate: string;
  readonly actualDate?: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly phaseId?: string;
  readonly status: MilestoneStatus;
  readonly percentComplete: number;
  readonly measuredPercent: number;
  readonly taskCount: number;
  readonly isBillable: boolean;
  readonly daysOverdue: number;
}

export interface MilestoneInput {
  readonly name: string;
  readonly description?: string | null;
  readonly plannedDate: string;
  readonly ownerEmployeeId?: string | null;
  readonly phaseId?: string | null;
  readonly status?: MilestoneStatus | null;
  readonly percentComplete?: number | null;
  readonly isBillable?: boolean | null;
}

export type SprintStatus = 'Planned' | 'Active' | 'Completed' | 'Cancelled';

export interface Sprint {
  readonly id: string;
  readonly projectId: string;
  readonly projectCode?: string;
  readonly projectName?: string;
  readonly name: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly goal?: string;
  readonly capacityPoints?: number;
  readonly status: SprintStatus;
  readonly committedPoints?: number;
  readonly completedPoints?: number;
  readonly plannedPoints: number;
  readonly donePoints: number;
  readonly taskCount: number;
  readonly doneTaskCount: number;
  readonly reviewNotes?: string;
  readonly retrospectiveNotes?: string;
}

export interface SprintInput {
  readonly name: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly goal?: string | null;
  readonly capacityPoints?: number | null;
  readonly reviewNotes?: string | null;
  readonly retrospectiveNotes?: string | null;
}

export interface BurndownPoint {
  readonly date: string;
  readonly ideal: number;
  readonly remaining?: number;
}

export interface VelocityPoint {
  readonly sprintId: string;
  readonly name: string;
  readonly endDate: string;
  readonly committed: number;
  readonly completed: number;
}

export interface CalendarEvent {
  readonly kind: 'Phase' | 'Milestone' | 'Sprint' | 'Meeting' | 'Task';
  readonly id: string;
  readonly title: string;
  readonly start: string;
  readonly end: string;
  readonly status?: string;
  readonly detail?: string;
}

export type ChangeRequestStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Implementing' | 'Testing' | 'Closed' | 'Withdrawn';

export interface ChangeRequest {
  readonly id: string;
  readonly projectId: string;
  readonly projectCode?: string;
  readonly projectName?: string;
  readonly code: string;
  readonly title: string;
  readonly description: string;
  readonly reason?: string;
  readonly impact?: string;
  readonly estimatedHours?: number;
  readonly estimatedCost?: number;
  readonly scheduleImpactDays?: number;
  readonly priority: WorkPriority;
  readonly requestedByEmployeeId?: string;
  readonly requestedByName?: string;
  readonly status: ChangeRequestStatus;
  readonly workflowInstanceId?: string;
  readonly decidedDate?: string;
  readonly clientApprovedOn?: string;
  readonly clientApprovalReference?: string;
  readonly implementationDate?: string;
  readonly closedOn?: string;
  readonly createdDate: string;
}

export interface ChangeRequestInput {
  readonly title: string;
  readonly description: string;
  readonly reason?: string | null;
  readonly impact?: string | null;
  readonly estimatedHours?: number | null;
  readonly estimatedCost?: number | null;
  readonly scheduleImpactDays?: number | null;
  readonly priority: WorkPriority;
  readonly requestedByEmployeeId?: string | null;
  readonly requestedByName?: string | null;
}

export type RaidKind = 'Risk' | 'Assumption' | 'Issue' | 'Dependency';
export type RaidStatus = 'Open' | 'InProgress' | 'Resolved' | 'Closed' | 'Accepted';
export type Severity = 'Low' | 'Medium' | 'High' | 'Critical';

export interface RaidItem {
  readonly id: string;
  readonly projectId: string;
  readonly projectCode?: string;
  readonly projectName?: string;
  readonly code: string;
  readonly kind: RaidKind;
  readonly title: string;
  readonly description?: string;
  readonly category?: string;
  readonly probability?: number;
  readonly impact?: number;
  readonly score?: number;
  readonly severity?: Severity;
  readonly priority: WorkPriority;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly assigneeEmployeeId?: string;
  readonly assigneeName?: string;
  readonly dueDate?: string;
  readonly mitigation?: string;
  readonly contingencyPlan?: string;
  readonly rootCause?: string;
  readonly resolution?: string;
  readonly status: RaidStatus;
  readonly resolvedOn?: string;
  readonly isOverdue: boolean;
}

export interface RaidInput {
  readonly kind: RaidKind;
  readonly title: string;
  readonly description?: string | null;
  readonly category?: string | null;
  readonly probability?: number | null;
  readonly impact?: number | null;
  readonly severity?: Severity | null;
  readonly priority: WorkPriority;
  readonly ownerEmployeeId?: string | null;
  readonly assigneeEmployeeId?: string | null;
  readonly dueDate?: string | null;
  readonly mitigation?: string | null;
  readonly contingencyPlan?: string | null;
  readonly rootCause?: string | null;
  readonly resolution?: string | null;
  readonly status?: RaidStatus | null;
}

export type MeetingType = 'Kickoff' | 'StatusReview' | 'SprintPlanning' | 'SprintReview' | 'Retrospective' | 'Steering' | 'Client' | 'Other';
export type MeetingStatus = 'Scheduled' | 'Held' | 'Cancelled';
export type ActionStatus = 'Open' | 'Done' | 'Cancelled';

export interface MeetingAction {
  readonly id: string;
  readonly meetingId: string;
  readonly description: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly dueDate?: string;
  readonly status: ActionStatus;
  readonly completedOn?: string;
  readonly isOverdue: boolean;
}

export interface Meeting {
  readonly id: string;
  readonly title: string;
  readonly type: MeetingType;
  readonly date: string;
  readonly startTime?: string;
  readonly durationMinutes?: number;
  readonly location?: string;
  readonly participantEmployeeIds: readonly string[];
  readonly participantNames: readonly string[];
  readonly externalParticipants?: string;
  readonly agenda?: string;
  readonly minutes?: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly status: MeetingStatus;
  readonly actions: readonly MeetingAction[];
}

export interface MeetingInput {
  readonly title: string;
  readonly type: MeetingType;
  readonly date: string;
  readonly startTime?: string | null;
  readonly durationMinutes?: number | null;
  readonly location?: string | null;
  readonly participantEmployeeIds?: readonly string[] | null;
  readonly externalParticipants?: string | null;
  readonly agenda?: string | null;
  readonly minutes?: string | null;
  readonly ownerEmployeeId?: string | null;
  readonly status?: MeetingStatus | null;
}

export interface HealthResult {
  readonly schedule: HealthRating;
  readonly budget: HealthRating;
  readonly resources: HealthRating;
  readonly scope: HealthRating;
  readonly quality: HealthRating;
  readonly risk: HealthRating;
  readonly issues: HealthRating;
  readonly clientSatisfaction: HealthRating;
  readonly overall: HealthRating;
  readonly score: number;
  readonly progressPercent: number;
  readonly reasons: readonly string[];
}

export interface HealthTrendPoint {
  readonly assessedOn: string;
  readonly overall: HealthRating;
  readonly score: number;
  readonly progressPercent: number;
}

export interface HealthView {
  readonly current: HealthResult;
  readonly assessedOn: string;
  readonly trend: readonly HealthTrendPoint[];
}

export interface ProjectDashboard {
  readonly project: ProjectItem;
  readonly health: HealthView;
  readonly elapsedPercent: number;
  readonly tasks: {
    readonly total: number;
    readonly open: number;
    readonly done: number;
    readonly overdue: number;
    readonly byCategory: Readonly<Record<string, number>>;
    readonly byType: Readonly<Record<string, number>>;
  };
  readonly milestones: readonly Milestone[];
  readonly milestonesAchieved: number;
  readonly milestonesTotal: number;
  readonly raid: {
    readonly openRisks: number;
    readonly highRisks: number;
    readonly openIssues: number;
    readonly criticalIssues: number;
    readonly openAssumptions: number;
    readonly openDependencies: number;
  };
  readonly pendingChangeRequests: number;
  readonly openActions: number;
  readonly activeSprint?: Sprint;
  readonly burndown: readonly BurndownPoint[];
  readonly velocity: readonly VelocityPoint[];
  readonly phases: readonly Phase[];
}

export interface ProjectRole {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description?: string;
  readonly skills: readonly string[];
  readonly isSystem: boolean;
  readonly isActive: boolean;
  readonly sortOrder: number;
}

export interface ProjectHealthThresholds {
  readonly scheduleAmberGap: number;
  readonly scheduleRedGap: number;
  readonly milestoneRedOverdueDays: number;
  readonly resourceEndingSoonDays: number;
  readonly scopeAmberPendingChanges: number;
  readonly scopeRedPendingChanges: number;
  readonly scopeRedScheduleImpactDays: number;
  readonly qualityAmberOpenBugs: number;
  readonly qualityRedOpenBugs: number;
  readonly riskAmberScore: number;
  readonly riskRedScore: number;
  readonly issueRedOverdueCount: number;
  readonly overallAmberScore: number;
  readonly overallRedScore: number;
}

/** "TimeAndMaterial" -> "Time and material": enum values as words, for places without a label map. */
export function humanize(value: string | undefined | null): string {
  if (!value) {
    return '';
  }

  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
