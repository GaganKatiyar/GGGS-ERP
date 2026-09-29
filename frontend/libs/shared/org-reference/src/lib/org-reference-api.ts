import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  BranchListItem,
  BusinessUnitListItem,
  CostCenterListItem,
  CreateBranchRequest,
  CreateBusinessUnitRequest,
  CreateCostCenterRequest,
  CreateDepartmentRequest,
  CreateDesignationRequest,
  CreateGradeRequest,
  CreateLegalEntityRequest,
  CreateLocationRequest,
  DepartmentListItem,
  DesignationListItem,
  GradeListItem,
  LegalEntityListItem,
  LocationListItem,
  PagedResult,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * Organization structure, over the gateway's `/api/v1/*` routes.
 *
 * One service for all six resources, mirroring `OrganizationEndpoints.cs` on the server: each is a
 * handful of fields and "does this code exist, does the parent exist", not enough behaviour to
 * earn a service of its own.
 *
 * Lives in `shared`, not inside the organization feature, because the employees feature needs the
 * same lookups (legal entity, branch, department, designation) for its hire and transfer forms.
 * The module-boundary rule is deliberately one-way - a feature never imports another feature - so
 * this had to move up rather than have employees reach across.
 */
@Injectable({ providedIn: 'root' })
export class OrgReferenceApi {
  private readonly http = inject(HttpClient);

  // ---- Legal entities (the one paged list here - see LegalEntityListItem's own note) ----

  listLegalEntities(query: {
    page: number;
    pageSize: number;
    search?: string;
  }): Promise<PagedResult<LegalEntityListItem>> {
    return firstValueFrom(
      this.http.get<PagedResult<LegalEntityListItem>>('/api/v1/legal-entities', {
        params: toHttpParams(query),
      }),
    );
  }

  createLegalEntity(
    request: CreateLegalEntityRequest,
  ): Promise<LegalEntityListItem> {
    return firstValueFrom(
      this.http.post<LegalEntityListItem>('/api/v1/legal-entities', request),
    );
  }

  // ---- Business units ----

  listBusinessUnits(): Promise<readonly BusinessUnitListItem[]> {
    return firstValueFrom(
      this.http.get<readonly BusinessUnitListItem[]>('/api/v1/business-units'),
    );
  }

  createBusinessUnit(
    request: CreateBusinessUnitRequest,
  ): Promise<BusinessUnitListItem> {
    return firstValueFrom(
      this.http.post<BusinessUnitListItem>('/api/v1/business-units', request),
    );
  }

  // ---- Branches ----

  listBranches(legalEntityId?: string): Promise<readonly BranchListItem[]> {
    return firstValueFrom(
      this.http.get<readonly BranchListItem[]>('/api/v1/branches', {
        params: legalEntityId ? { legalEntityId } : {},
      }),
    );
  }

  createBranch(request: CreateBranchRequest): Promise<BranchListItem> {
    return firstValueFrom(
      this.http.post<BranchListItem>('/api/v1/branches', request),
    );
  }

  // ---- Locations ----

  listLocations(branchId?: string): Promise<readonly LocationListItem[]> {
    return firstValueFrom(
      this.http.get<readonly LocationListItem[]>('/api/v1/locations', {
        params: branchId ? { branchId } : {},
      }),
    );
  }

  createLocation(request: CreateLocationRequest): Promise<LocationListItem> {
    return firstValueFrom(
      this.http.post<LocationListItem>('/api/v1/locations', request),
    );
  }

  // ---- Departments ----

  listDepartments(): Promise<readonly DepartmentListItem[]> {
    return firstValueFrom(
      this.http.get<readonly DepartmentListItem[]>('/api/v1/departments'),
    );
  }

  createDepartment(
    request: CreateDepartmentRequest,
  ): Promise<DepartmentListItem> {
    return firstValueFrom(
      this.http.post<DepartmentListItem>('/api/v1/departments', request),
    );
  }

  moveDepartment(
    id: string,
    newParentId: string | undefined,
  ): Promise<DepartmentListItem> {
    return firstValueFrom(
      this.http.post<DepartmentListItem>(`/api/v1/departments/${id}/move`, {
        newParentId: newParentId ?? null,
      }),
    );
  }

  // ---- Designations ----

  listDesignations(): Promise<readonly DesignationListItem[]> {
    return firstValueFrom(
      this.http.get<readonly DesignationListItem[]>('/api/v1/designations'),
    );
  }

  createDesignation(
    request: CreateDesignationRequest,
  ): Promise<DesignationListItem> {
    return firstValueFrom(
      this.http.post<DesignationListItem>('/api/v1/designations', request),
    );
  }

  // ---- Grades ----

  listGrades(): Promise<readonly GradeListItem[]> {
    return firstValueFrom(this.http.get<readonly GradeListItem[]>('/api/v1/grades'));
  }

  createGrade(request: CreateGradeRequest): Promise<GradeListItem> {
    return firstValueFrom(this.http.post<GradeListItem>('/api/v1/grades', request));
  }

  // ---- Cost centers ----

  listCostCenters(): Promise<readonly CostCenterListItem[]> {
    return firstValueFrom(
      this.http.get<readonly CostCenterListItem[]>('/api/v1/cost-centers'),
    );
  }

  createCostCenter(
    request: CreateCostCenterRequest,
  ): Promise<CostCenterListItem> {
    return firstValueFrom(
      this.http.post<CostCenterListItem>('/api/v1/cost-centers', request),
    );
  }
}

/** `HttpParams` refuses `undefined`, so this drops anything the caller left unset. */
function toHttpParams(
  query: Record<string, string | number | boolean | undefined>,
): Record<string, string> {
  const params: Record<string, string> = {};

  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params[key] = String(value);
    }
  }

  return params;
}
