/**
 * Core HR contracts (M2): master data, organization structure and employees, as the PeopleOS API
 * returns them. Names are distinct from the legacy GGGS models still compiled by unrouted feature
 * libraries; those files go when the libraries are deleted.
 */

// ----- Master data ---------------------------------------------------------------------------

export interface LookupList {
  readonly code: string;
  readonly name: string;
  readonly parentList?: string;
  readonly tenantExtensible: boolean;
}

export interface LookupValue {
  readonly id: string;
  readonly code: string;
  readonly label: string;
  readonly parentCode?: string;
  readonly sortOrder: number;
  readonly isActive: boolean;
  readonly isSystem: boolean;
}

/** Lists every form can rely on. */
export const LOOKUP_LISTS = {
  country: 'country',
  state: 'state',
  currency: 'currency',
  employmentType: 'employment-type',
  workMode: 'work-mode',
  gender: 'gender',
  maritalStatus: 'marital-status',
  bloodGroup: 'blood-group',
  relationship: 'relationship',
  exitReason: 'exit-reason',
  jobFamily: 'job-family',
  industry: 'industry',
} as const;

// ----- Organization --------------------------------------------------------------------------

/** The kinds of organization record, as the API names them in its routes. */
export type OrgKind =
  | 'legal-entities'
  | 'business-units'
  | 'divisions'
  | 'departments'
  | 'locations'
  | 'cost-centers'
  | 'profit-centers'
  | 'grades'
  | 'job-levels'
  | 'jobs'
  | 'positions';

export interface Address {
  readonly line1?: string;
  readonly line2?: string;
  readonly city?: string;
  readonly stateCode?: string;
  readonly countryCode?: string;
  readonly postalCode?: string;
}

/** What every organization record has; each kind adds its own fields. */
export interface OrgUnit {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly [field: string]: unknown;
}

export interface DepartmentNode {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly headEmployeeId?: string;
  readonly headcount: number;
  readonly isActive: boolean;
  readonly children: readonly DepartmentNode[];
}

export interface PositionOccupancy {
  readonly positionId: string;
  readonly headcount: number;
  readonly occupied: number;
  readonly vacant: number;
}

// ----- Employees -----------------------------------------------------------------------------

export type EmployeeStatusCode = 'Active' | 'OnNotice' | 'Exited';

export type AssignmentChangeType = 'Transfer' | 'Promotion' | 'ManagerChange' | 'Redesignation' | 'EmploymentTermsChange';

export interface NamedRef {
  readonly id: string;
  readonly name: string;
}

export interface CodedValue {
  readonly code: string;
  readonly label: string;
}

export interface EmployeeRow {
  readonly id: string;
  readonly employeeCode: string;
  readonly displayName: string;
  readonly officialEmail?: string;
  readonly designation?: string;
  readonly status: EmployeeStatusCode;
  readonly joiningDate: string;
  readonly departmentId?: string;
  readonly departmentName?: string;
  readonly managerEmployeeId?: string;
  readonly managerName?: string;
  readonly locationId?: string;
  readonly locationName?: string;
}

export interface EmployeeAssignment {
  readonly effectiveFrom: string;
  readonly legalEntity?: NamedRef;
  readonly businessUnit?: NamedRef;
  readonly division?: NamedRef;
  readonly department?: NamedRef;
  readonly job?: NamedRef;
  readonly position?: NamedRef;
  readonly grade?: NamedRef;
  readonly location?: NamedRef;
  readonly manager?: NamedRef;
  readonly hrbp?: NamedRef;
  readonly employmentType: CodedValue;
  readonly workMode: CodedValue;
  readonly designation?: string;
}

/** Masked hints only; the values themselves need their own permission and route. */
export interface SensitiveSummary {
  readonly panHint?: string;
  readonly uanHint?: string;
  readonly esicHint?: string;
  readonly nationalIdHint?: string;
  readonly professionalTaxStateCode?: string;
  readonly bankName?: string;
  readonly bankIfsc?: string;
  readonly bankAccountHint?: string;
}

export interface EmployeeProfile {
  readonly id: string;
  readonly employeeCode: string;
  readonly firstName: string;
  readonly middleName?: string;
  readonly lastName?: string;
  readonly preferredName?: string;
  readonly displayName: string;
  readonly gender?: CodedValue;
  readonly dateOfBirth?: string;
  readonly maritalStatus?: CodedValue;
  readonly nationality?: CodedValue;
  readonly bloodGroup?: CodedValue;
  readonly personalEmail?: string;
  readonly officialEmail?: string;
  readonly mobilePhone?: string;
  readonly alternatePhone?: string;
  readonly currentAddress?: Address;
  readonly permanentAddress?: Address;
  readonly status: EmployeeStatusCode;
  readonly joiningDate: string;
  readonly probationEndDate?: string;
  readonly confirmationDate?: string;
  readonly noticeStartDate?: string;
  readonly exitDate?: string;
  readonly exitReason?: CodedValue;
  readonly assignment: EmployeeAssignment;
  readonly sensitive: SensitiveSummary;
}

export interface Employee360Section {
  readonly key: string;
  readonly title: string;
  readonly available: boolean;
  readonly feature?: string;
  readonly milestone?: string;
}

export interface Employee360 {
  readonly profile: EmployeeProfile;
  readonly directReportCount: number;
  readonly tenureMonths: number;
  readonly sections: readonly Employee360Section[];
}

export interface EmploymentHistoryEntry {
  readonly id: string;
  readonly changeType: string;
  readonly effectiveFrom: string;
  readonly effectiveTo?: string;
  readonly departmentName?: string;
  readonly jobName?: string;
  readonly positionName?: string;
  readonly gradeName?: string;
  readonly locationName?: string;
  readonly managerName?: string;
  readonly designation?: string;
  readonly employmentTypeCode: string;
  readonly workModeCode: string;
  readonly reason?: string;
}

export interface StatutoryDetails {
  readonly pan?: string;
  readonly uan?: string;
  readonly esicNumber?: string;
  readonly nationalId?: string;
  readonly professionalTaxStateCode?: string;
}

export interface BankDetails {
  readonly bankName?: string;
  readonly ifsc?: string;
  readonly accountHolder?: string;
  readonly accountNumber?: string;
}

export interface Dependent {
  readonly id: string;
  readonly fullName: string;
  readonly relationshipCode: string;
  readonly dateOfBirth?: string;
  readonly genderCode?: string;
}

export interface EmergencyContactEntry {
  readonly id: string;
  readonly fullName: string;
  readonly relationshipCode: string;
  readonly phone: string;
  readonly email?: string;
  readonly isPrimary: boolean;
}

export interface OrgChartNode {
  readonly id: string;
  readonly displayName: string;
  readonly designation?: string;
  readonly departmentName?: string;
  readonly directReportCount: number;
  readonly reports: readonly OrgChartNode[];
}

export interface EmployeeAssignmentInput {
  readonly departmentId?: string | null;
  readonly positionId?: string | null;
  readonly jobId?: string | null;
  readonly gradeId?: string | null;
  readonly locationId?: string | null;
  readonly legalEntityId?: string | null;
  readonly businessUnitId?: string | null;
  readonly divisionId?: string | null;
  readonly managerEmployeeId?: string | null;
  readonly hrbpEmployeeId?: string | null;
  readonly employmentTypeCode: string;
  readonly workModeCode: string;
  readonly designation?: string | null;
}

export interface HireEmployee {
  readonly employeeCode?: string | null;
  readonly firstName: string;
  readonly middleName?: string | null;
  readonly lastName?: string | null;
  readonly preferredName?: string | null;
  readonly genderCode?: string | null;
  readonly dateOfBirth?: string | null;
  readonly personalEmail?: string | null;
  readonly officialEmail?: string | null;
  readonly mobilePhone?: string | null;
  readonly joiningDate: string;
  readonly probationEndDate?: string | null;
  readonly assignment: EmployeeAssignmentInput;
}

export interface ImportRow {
  readonly employeeCode?: string;
  readonly firstName?: string;
  readonly middleName?: string;
  readonly lastName?: string;
  readonly officialEmail?: string;
  readonly mobilePhone?: string;
  readonly joiningDate?: string;
  readonly departmentCode?: string;
  readonly positionCode?: string;
  readonly jobCode?: string;
  readonly gradeCode?: string;
  readonly locationCode?: string;
  readonly managerEmployeeCode?: string;
  readonly employmentTypeCode?: string;
  readonly workModeCode?: string;
  readonly designation?: string;
}

export interface ImportRowResult {
  readonly rowNumber: number;
  readonly employeeCode?: string;
  readonly succeeded: boolean;
  readonly errors: readonly string[];
}

export interface ImportResult {
  readonly committed: boolean;
  readonly total: number;
  readonly succeeded: number;
  readonly failed: number;
  readonly rows: readonly ImportRowResult[];
}
