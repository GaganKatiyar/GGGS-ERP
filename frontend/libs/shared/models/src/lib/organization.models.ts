/**
 * Organization structure, mirroring the DTOs in `Gggs.CoreHr.Api.Endpoints.OrganizationEndpoints`.
 *
 * Only the fields the v1 screens use are modelled - address, geo-fencing, business units and
 * locations exist on the server but have no screen yet, so their request/response shapes are not
 * reproduced here. Add them when the screen that needs them lands, rather than ahead of it.
 */

export interface LegalEntityListItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly countryCode: string;
  readonly currency: string;
}

export interface CreateLegalEntityRequest {
  readonly code: string;
  readonly name: string;
  readonly countryCode: string;
  readonly currency: string;
  readonly registrationNumber?: string;
}

export interface BranchListItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly legalEntityId: string;
  readonly timeZone: string;
}

export interface CreateBranchRequest {
  readonly code: string;
  readonly name: string;
  readonly legalEntityId: string;
  readonly countryCode: string;
  readonly timeZone: string;
}

export interface DepartmentListItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly parentDepartmentId?: string;
}

export interface CreateDepartmentRequest {
  readonly code: string;
  readonly name: string;
  readonly parentDepartmentId?: string;
}

export interface DesignationListItem {
  readonly id: string;
  readonly code: string;
  readonly title: string;
  readonly jobFamily?: string;
  readonly jobLevel?: number;
}

export interface CreateDesignationRequest {
  readonly code: string;
  readonly title: string;
  readonly jobFamily?: string;
  readonly jobLevel?: number;
}

export interface GradeListItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly rank: number;
  readonly salaryBandMinimum?: number;
  readonly salaryBandMaximum?: number;
  readonly currency?: string;
}

export interface CreateGradeRequest {
  readonly code: string;
  readonly name: string;
  readonly rank: number;
}

export interface CostCenterListItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly glAccountCode?: string;
  readonly ownerEmployeeId?: string;
  readonly parentCostCenterId?: string;
}

export interface CreateCostCenterRequest {
  readonly code: string;
  readonly name: string;
  readonly glAccountCode?: string;
}
