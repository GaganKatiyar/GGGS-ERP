/**
 * Employees, mirroring the DTOs in `Gggs.CoreHr.Api.Endpoints.EmployeeEndpoints`.
 */

export type EmployeeStatus =
  | 'PreBoarding'
  | 'Probation'
  | 'Confirmed'
  | 'OnNotice'
  | 'Exited'
  | 'Absconded';

export type EmploymentType = 'Permanent' | 'Contract' | 'Intern' | 'Consultant';

export interface EmployeeListItem {
  readonly id: string;
  readonly employeeCode: string;
  readonly displayName: string;
  readonly status: EmployeeStatus;
  readonly dateOfJoining: string;
}

export interface Employee {
  readonly id: string;
  readonly employeeCode: string;
  readonly firstName: string;
  readonly middleName?: string;
  readonly lastName?: string;
  readonly displayName: string;
  readonly workEmail?: string;
  readonly status: EmployeeStatus;
  readonly dateOfJoining: string;
  readonly originalHireDate?: string;
  readonly confirmationDate?: string;
  readonly exitDate?: string;
  readonly userId?: string;
}

export interface EmploymentRecord {
  readonly id: string;
  readonly effectiveFrom: string;
  readonly effectiveTo?: string;
  readonly isCurrent: boolean;
  readonly legalEntityId: string;
  readonly branchId: string;
  readonly departmentId: string;
  readonly designationId: string;
  readonly gradeId?: string;
  readonly costCenterId?: string;
  readonly reportingManagerId?: string;
  readonly employmentType: EmploymentType;
  readonly probationEndDate?: string;
  readonly changeType: string;
  readonly changeReason?: string;
}

/** What every assignment - a hire, a transfer, a promotion - needs, matching `AssignmentBody`. */
export interface AssignmentInput {
  readonly legalEntityId: string;
  readonly branchId: string;
  readonly departmentId: string;
  readonly designationId: string;
  readonly gradeId?: string;
  readonly costCenterId?: string;
  readonly reportingManagerId?: string;
  readonly employmentType: EmploymentType;
  readonly probationEndDate?: string;
}

export interface HireEmployeeRequest {
  readonly employeeCode: string;
  readonly firstName: string;
  readonly middleName?: string;
  readonly lastName?: string;
  readonly workEmail?: string;
  readonly dateOfJoining: string;
  readonly assignment: AssignmentInput;
}

export interface ChangeAssignmentRequest {
  readonly effectiveFrom: string;
  readonly assignment: AssignmentInput;
  readonly reason?: string;
}

export interface ConfirmEmployeeRequest {
  readonly confirmationDate: string;
}

export interface ExitEmployeeRequest {
  readonly exitDate: string;
  readonly wasAbsconded: boolean;
}
