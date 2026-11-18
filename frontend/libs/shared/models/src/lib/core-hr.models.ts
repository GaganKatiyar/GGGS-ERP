/**
 * Core HR contracts (M2): master data, organization structure and employees, as the Nuts and Bolts API
 * returns them.
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
  | 'companies'
  | 'business-units'
  | 'departments'
  | 'locations'
  | 'cost-centers'
  | 'levels'
  | 'designations'
  | 'roles';

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

/** A department in the tree; its `children` are its functions (two levels, nothing below a function). */
export interface DepartmentNode {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly businessUnitId?: string;
  readonly headEmployeeId?: string;
  readonly locationId?: string;
  readonly costCenterId?: string;
  readonly headcount: number;
  readonly isActive: boolean;
  readonly children: readonly DepartmentNode[];
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
  readonly company?: NamedRef;
  readonly businessUnit?: NamedRef;
  /** Where the employee is placed: a department, or a function inside one. */
  readonly department?: NamedRef;
  /** When `department` is a function, the department it belongs to. */
  readonly parentDepartment?: NamedRef;
  /** Their role: the work they do. */
  readonly role?: NamedRef;
  readonly level?: NamedRef;
  /** The designation picked from the list; `designation` is its name as printed. */
  readonly designationRef?: NamedRef;
  /** The company name they work under. */
  readonly costCenter?: NamedRef;
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
  readonly passportHint?: string;
  readonly passportExpiry?: string;
  /** Years of work before joining, as given at hire. */
  readonly previousExperienceYears?: number;
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
  readonly contractEndDate?: string;
  /** The current profile photo; a new photo gets a new id, so it doubles as the image's cache version. */
  readonly photoId?: string;
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
  readonly roleName?: string;
  readonly levelName?: string;
  readonly costCenterName?: string;
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
  readonly passportNumber?: string;
}

export interface BankDetails {
  readonly bankName?: string;
  readonly ifsc?: string;
  readonly accountHolder?: string;
  readonly accountNumber?: string;
}

/** A qualification on the employee's profile. Years are as the employee gives them; either may be missing. */
export interface EducationEntry {
  readonly id: string;
  readonly degree: string;
  readonly fieldOfStudy?: string;
  readonly institution: string;
  readonly startYear?: number;
  readonly endYear?: number;
  readonly grade?: string;
}

export type EducationInput = Omit<EducationEntry, 'id'>;

/** A job held before joining. No end date means it was still held when the employee joined. */
export interface WorkExperienceEntry {
  readonly id: string;
  readonly company: string;
  readonly title: string;
  readonly location?: string;
  readonly fromDate: string;
  readonly toDate?: string;
  readonly summary?: string;
}

export type WorkExperienceInput = Omit<WorkExperienceEntry, 'id'>;

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
  readonly roleId?: string | null;
  readonly levelId?: string | null;
  readonly designationId?: string | null;
  /** Empty means the location's cost center, or the company default. */
  readonly costCenterId?: string | null;
  readonly locationId?: string | null;
  readonly companyId?: string | null;
  readonly businessUnitId?: string | null;
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
  readonly details?: HireDetails | null;
}

export interface HireAddress {
  readonly line1?: string | null;
  readonly line2?: string | null;
  readonly city?: string | null;
  readonly stateCode?: string | null;
  readonly countryCode?: string | null;
  readonly postalCode?: string | null;
}

/** A Hire form saved part-way, as the Draft hires list shows it. `files` are the slots that hold a file. */
export interface HireDraftItem {
  readonly id: string;
  readonly displayName: string;
  readonly step: number;
  readonly offerId?: string;
  readonly files: readonly string[];
  readonly createdDate: string;
  readonly modifiedDate?: string;
}

/** A file kept with a draft: the photo (`photo`) or a document, by its type code (`AADHAAR`, `PAN`, `PASSPORT`, `OFFER_LETTER`). */
export interface HireDraftFile {
  readonly slot: string;
  readonly fileId: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
}

/** A draft opened to carry on. `payload` is whatever the Hire form saved; only the form reads it. */
export interface HireDraftDetail {
  readonly id: string;
  readonly displayName: string;
  readonly step: number;
  readonly offerId?: string;
  readonly payload: Readonly<Record<string, unknown>>;
  readonly files: readonly HireDraftFile[];
}

export interface HireDraftBody {
  readonly displayName: string | null;
  readonly step: number;
  readonly offerId: string | null;
  readonly payload: Readonly<Record<string, unknown>>;
}

/** The rest of a new hire's record, saved with the hire itself. Identifiers go in the clear and are encrypted on arrival. */
export interface HireDetails {
  readonly genderCode?: string | null;
  readonly dateOfBirth?: string | null;
  readonly preferredName?: string | null;
  readonly maritalStatusCode?: string | null;
  readonly nationalityCode?: string | null;
  readonly bloodGroupCode?: string | null;
  readonly officialEmail?: string | null;
  readonly presentAddress?: HireAddress | null;
  readonly permanentAddress?: HireAddress | null;
  readonly aadhaar?: string | null;
  readonly pan?: string | null;
  readonly passportNumber?: string | null;
  readonly passportExpiry?: string | null;
  readonly bankName?: string | null;
  readonly bankIfsc?: string | null;
  readonly bankAccountHolder?: string | null;
  readonly bankAccountNumber?: string | null;
  readonly previousExperienceYears?: number | null;
  readonly probationEndDate?: string | null;
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
  readonly roleCode?: string;
  readonly levelCode?: string;
  readonly designationCode?: string;
  readonly costCenterCode?: string;
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
