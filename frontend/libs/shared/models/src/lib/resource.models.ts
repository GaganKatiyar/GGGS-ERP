/** Resourcing contracts (M6): skills, timesheets and resource management, as the API returns them. */

// ----- Skills --------------------------------------------------------------------------------

export interface SkillItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly category: string;
  readonly description?: string;
  readonly isActive: boolean;
  readonly peopleCount: number;
}

export type AssessmentSource = 'Self' | 'Manager' | 'Assessment' | 'Certification' | 'Project' | 'Imported';

export interface EmployeeSkillItem {
  readonly id: string;
  readonly skillId: string;
  readonly skillName: string;
  readonly category: string;
  readonly currentLevel: number;
  readonly targetLevel?: number;
  readonly gap: number;
  readonly yearsExperience?: number;
  readonly isPrimary: boolean;
  readonly lastAssessedOn: string;
  readonly source: AssessmentSource;
  readonly assessedByName?: string;
}

export interface CertificationItem {
  readonly id: string;
  readonly name: string;
  readonly issuer?: string;
  readonly credentialId?: string;
  readonly issuedOn?: string;
  readonly expiresOn?: string;
  readonly skillId?: string;
  readonly isCurrent: boolean;
}

export interface SkillProfile {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly skills: readonly EmployeeSkillItem[];
  readonly certifications: readonly CertificationItem[];
}

export interface EmployeeSkillInput {
  readonly skillId: string;
  readonly currentLevel: number;
  readonly targetLevel?: number | null;
  readonly yearsExperience?: number | null;
  readonly isPrimary: boolean;
  readonly source?: AssessmentSource | null;
}

export interface CertificationInput {
  readonly name: string;
  readonly issuer?: string | null;
  readonly credentialId?: string | null;
  readonly issuedOn?: string | null;
  readonly expiresOn?: string | null;
  readonly skillId?: string | null;
}

export interface MatrixRow {
  readonly skillId: string;
  readonly skillName: string;
  readonly category: string;
  readonly countsByLevel: readonly number[];
  readonly people: number;
  readonly averageLevel: number;
  readonly withGap: number;
}

export interface SkilledPerson {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly designation?: string;
  readonly level: number;
  readonly yearsExperience?: number;
  readonly isPrimary: boolean;
}

export const SKILL_LEVEL_LABELS: readonly string[] = ['', 'Aware', 'Working', 'Practitioner', 'Advanced', 'Expert'];

// ----- Timesheets ----------------------------------------------------------------------------

export type HourCategory = 'Billable' | 'NonBillable' | 'Internal' | 'Training' | 'Bench' | 'Leave';
export type TimesheetStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected';
export type IssueLevel = 'Info' | 'Warning' | 'Error';

export const HOUR_CATEGORIES: readonly HourCategory[] = ['Billable', 'NonBillable', 'Internal', 'Training', 'Bench', 'Leave'];

export interface TimesheetEntryItem {
  readonly id: string;
  readonly date: string;
  readonly projectId?: string;
  readonly projectCode?: string;
  readonly taskId?: string;
  readonly taskCode?: string;
  readonly category: HourCategory;
  readonly hours: number;
  readonly activity?: string;
  readonly description?: string;
}

export interface TimesheetDay {
  readonly date: string;
  readonly isWorkingDay: boolean;
  readonly holiday?: string;
  readonly leave?: string;
  readonly hours: number;
}

export interface TimesheetTask {
  readonly taskId: string;
  readonly code: string;
  readonly title: string;
}

export interface TimesheetTarget {
  readonly projectId: string;
  readonly projectCode: string;
  readonly projectName: string;
  readonly billable: boolean;
  readonly tasks: readonly TimesheetTask[];
}

export interface TimesheetIssue {
  readonly level: IssueLevel;
  readonly code: string;
  readonly message: string;
  readonly date?: string;
}

export interface TimesheetRules {
  readonly standardHoursPerDay: number;
  readonly minHoursPerDay: number;
  readonly maxHoursPerDay: number;
  readonly minWeeklyHours: number;
  readonly enforceWeeklyMinimum: boolean;
  readonly allowWeekendEntries: boolean;
  readonly allowHolidayEntries: boolean;
  readonly submissionDeadlineDays: number;
  readonly lockedThrough?: string;
}

export interface TimesheetHeader {
  readonly id?: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly weekStart: string;
  readonly weekEnd: string;
  readonly status: TimesheetStatus;
  readonly totalHours: number;
  readonly billableHours: number;
  readonly submittedDate?: string;
  readonly decidedDate?: string;
  readonly isLate: boolean;
  readonly workflowInstanceId?: string;
  readonly comment?: string;
  readonly correctionCount: number;
  readonly lastCorrectionReason?: string;
  readonly isLocked: boolean;
}

export interface TimesheetView {
  readonly timesheet: TimesheetHeader;
  readonly entries: readonly TimesheetEntryItem[];
  readonly days: readonly TimesheetDay[];
  readonly targets: readonly TimesheetTarget[];
  readonly issues: readonly TimesheetIssue[];
  readonly byCategory: Readonly<Record<string, number>>;
  readonly overtimeHours: number;
  readonly rules: TimesheetRules;
}

export interface TimesheetEntryInput {
  readonly date: string;
  readonly projectId?: string | null;
  readonly taskId?: string | null;
  readonly category: HourCategory;
  readonly hours: number;
  readonly activity?: string | null;
  readonly description?: string | null;
}

export interface TimesheetRow {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly weekStart: string;
  readonly status: TimesheetStatus;
  readonly totalHours: number;
  readonly billableHours: number;
  readonly submittedDate?: string;
  readonly isLate: boolean;
  readonly correctionCount: number;
}

export interface MissingTimesheet {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly designation?: string;
  readonly status?: TimesheetStatus;
}

// ----- Resources -----------------------------------------------------------------------------

export type CapacityState = 'Bench' | 'PartiallyAvailable' | 'FullyAllocated' | 'Overallocated' | 'Leave';

export interface SkillTag {
  readonly skillId: string;
  readonly name: string;
  readonly level: number;
  readonly isPrimary: boolean;
}

export interface ProjectShare {
  readonly projectId: string;
  readonly projectCode: string;
  readonly percent: number;
  readonly billable: boolean;
}

export interface CapacityCell {
  readonly from: string;
  readonly to: string;
  readonly allocatedPercent: number;
  readonly billablePercent: number;
  readonly leavePercent: number;
  readonly availablePercent: number;
  readonly state: CapacityState;
  readonly projects: readonly ProjectShare[];
}

export interface ResourceRow {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly employeeCode: string;
  readonly designation?: string;
  readonly departmentName?: string;
  readonly locationName?: string;
  readonly skills: readonly SkillTag[];
  readonly allocatedPercent: number;
  readonly availablePercent: number;
  readonly state: CapacityState;
  readonly currentProjects: readonly string[];
  readonly isBillable: boolean;
  readonly costRate?: number;
  readonly billingRate?: number;
  readonly exitDate?: string;
}

export interface AllocationItem {
  readonly memberId: string;
  readonly projectId: string;
  readonly projectCode: string;
  readonly projectName: string;
  readonly roleName: string;
  readonly allocationPercent: number;
  readonly startDate: string;
  readonly endDate?: string;
  readonly billable: boolean;
  readonly billingRate?: number;
  readonly costRate?: number;
  readonly isActive: boolean;
}

export interface ProfileRates {
  readonly costRate?: number;
  readonly billingRate?: number;
  readonly currency: string;
  readonly capacityHoursPerWeek: number;
  readonly noticePeriodDays?: number;
  readonly isBillable: boolean;
  readonly notes?: string;
}

export interface ResourceProfileView {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly employeeCode: string;
  readonly designation?: string;
  readonly departmentName?: string;
  readonly locationName?: string;
  readonly workModeCode?: string;
  readonly joiningDate?: string;
  readonly yearsInCompany: number;
  readonly exitDate?: string;
  readonly skills: readonly SkillTag[];
  readonly certifications: readonly string[];
  readonly current: readonly AllocationItem[];
  readonly upcoming: readonly AllocationItem[];
  readonly history: readonly AllocationItem[];
  readonly capacity: readonly CapacityCell[];
  readonly rates?: ProfileRates;
  readonly canEditRates: boolean;
}

export interface ReleaseItem {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly projectCode: string;
  readonly endDate: string;
  readonly allocationPercent: number;
  readonly nextAssignment?: string;
}

export interface ResourceDashboard {
  readonly totalPeople: number;
  readonly billable: number;
  readonly nonBillable: number;
  readonly onBench: number;
  readonly overallocated: number;
  readonly partiallyAvailable: number;
  readonly fullyAllocated: number;
  readonly upcomingReleases: number;
  readonly openRequests: number;
  readonly openDemand: number;
  readonly benchCostPerDay: number;
  readonly releases: readonly ReleaseItem[];
}

export interface CalendarRow {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly designation?: string;
  readonly topSkills: readonly string[];
  readonly cells: readonly CapacityCell[];
}

export interface BenchItem {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly designation?: string;
  readonly locationName?: string;
  readonly skills: readonly SkillTag[];
  readonly previousProject?: string;
  readonly benchSince?: string;
  readonly benchDays: number;
  readonly availablePercent: number;
  readonly costRate?: number;
  readonly nextProject?: string;
  readonly nextAssignment?: string;
}

export interface AllocationOutcome {
  readonly memberId: string;
  readonly peakAllocationPercent: number;
  readonly overallocated: boolean;
}

export type TransferStatus = 'Pending' | 'Completed' | 'Rejected' | 'Cancelled';

export interface TransferItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly fromProject?: string;
  readonly toProject?: string;
  readonly allocationPercent: number;
  readonly effectiveDate: string;
  readonly status: TransferStatus;
  readonly reason: string;
  readonly workflowInstanceId?: string;
  readonly createdDate: string;
}

export type RequestPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type RequestStatus = 'Open' | 'PartiallyFulfilled' | 'Fulfilled' | 'Cancelled';

export interface RequirementItem {
  readonly skillId: string;
  readonly skillName?: string;
  readonly minLevel: number;
}

export interface ResourceRequestItem {
  readonly id: string;
  readonly projectId: string;
  readonly projectCode?: string;
  readonly projectName?: string;
  readonly projectRoleId?: string;
  readonly title: string;
  readonly skills: readonly RequirementItem[];
  readonly minYearsExperience?: number;
  readonly quantity: number;
  readonly fulfilledCount: number;
  readonly allocationPercent: number;
  readonly startDate: string;
  readonly endDate: string;
  readonly billable: boolean;
  readonly locationId?: string;
  readonly workModeCode?: string;
  readonly maxCostRate?: number;
  readonly priority: RequestPriority;
  readonly requestedByName?: string;
  readonly status: RequestStatus;
  readonly notes?: string;
  readonly createdDate: string;
  readonly canManage: boolean;
}

export interface ResourceRequestInput {
  readonly projectId: string;
  readonly projectRoleId?: string | null;
  readonly title: string;
  readonly skills: readonly { readonly skillId: string; readonly minLevel: number }[];
  readonly minYearsExperience?: number | null;
  readonly quantity: number;
  readonly allocationPercent: number;
  readonly startDate: string;
  readonly endDate: string;
  readonly billable: boolean;
  readonly locationId?: string | null;
  readonly workModeCode?: string | null;
  readonly maxCostRate?: number | null;
  readonly priority: RequestPriority;
  readonly notes?: string | null;
}

export interface MatchScore {
  readonly skills: number;
  readonly availability: number;
  readonly experience: number;
  readonly location: number;
  readonly cost: number;
  readonly overall: number;
  readonly missingSkills: readonly string[];
}

export type CandidateStatus = 'Proposed' | 'Allocated' | 'Rejected';

export interface MatchItem {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly designation?: string;
  readonly skills: readonly SkillTag[];
  readonly peakAllocation: number;
  readonly costRate?: number;
  readonly exitDate?: string;
  readonly certifications: readonly string[];
  readonly score: MatchScore;
  readonly candidateStatus?: CandidateStatus;
}

export interface CandidateItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly score: number;
  readonly breakdown?: MatchScore;
  readonly status: CandidateStatus;
  readonly allocationMemberId?: string;
  readonly note?: string;
}

export interface ResourceRequestDetail {
  readonly request: ResourceRequestItem;
  readonly candidates: readonly CandidateItem[];
}

export type UtilizationGrouping = 'Employee' | 'Project' | 'Department' | 'Manager' | 'Location';

export interface UtilizationRow {
  readonly key?: string;
  readonly name: string;
  readonly availableHours: number;
  readonly billableHours: number;
  readonly loggedHours: number;
  readonly utilizationPercent: number;
  readonly hoursByCategory: Readonly<Record<string, number>>;
}

export interface UtilizationReport {
  readonly from: string;
  readonly to: string;
  readonly groupBy: UtilizationGrouping;
  readonly availableHours: number;
  readonly billableHours: number;
  readonly utilizationPercent: number;
  readonly hoursByCategory: Readonly<Record<string, number>>;
  readonly rows: readonly UtilizationRow[];
  readonly trend: readonly { readonly month: string; readonly availableHours: number; readonly billableHours: number; readonly utilizationPercent: number }[];
}

export interface ForecastReport {
  readonly skills: readonly { readonly skillId: string; readonly skillName: string; readonly months: readonly { readonly month: string; readonly demand: number; readonly supply: number; readonly gap: number }[] }[];
  readonly capacity: readonly {
    readonly month: string;
    readonly people: number;
    readonly allocatedFte: number;
    readonly availableFte: number;
    readonly leaveFte: number;
    readonly onBench: number;
    readonly overallocated: number;
  }[];
  readonly peopleLeaving: number;
}
