/**
 * Organization structure, mirroring the DTOs in `Gggs.CoreHr.Api.Endpoints.OrganizationEndpoints`.
 *
 * Only the fields the v1 screens use are modelled - address and geo-fencing exist on the server
 * but have no screen yet, so their request/response shapes are not reproduced here. Add them when
 * the screen that needs them lands, rather than ahead of it.
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

export interface BusinessUnitListItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly headEmployeeId?: string;
}

export interface CreateBusinessUnitRequest {
  readonly code: string;
  readonly name: string;
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

/**
 * Mirrors `Gggs.CoreHr.Domain.Organization.LocationType`. The API serializes every enum as its
 * member name (`JsonConfiguration.Apply` adds a `JsonStringEnumConverter` globally), not its
 * numeric value, so this is a string union, not the C# enum's underlying `int`.
 */
export type LocationType = 'Office' | 'Site' | 'Remote';

export const LOCATION_TYPE_LABELS: Readonly<Record<LocationType, string>> = {
  Office: 'Office',
  Site: 'Site',
  Remote: 'Remote',
};

export interface LocationListItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly branchId: string;
  readonly locationType: LocationType;
}

export interface CreateLocationRequest {
  readonly code: string;
  readonly name: string;
  readonly branchId: string;
  readonly locationType: LocationType;
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
