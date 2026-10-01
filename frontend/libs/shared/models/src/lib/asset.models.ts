/**
 * Assets, mirroring the DTOs in `Gggs.CoreHr.Api.Endpoints.AssetEndpoints`.
 */

export interface AssetCategory {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
}

export interface CreateAssetCategoryRequest {
  readonly code: string;
  readonly name: string;
}

export type AssetStatus = 'InStock' | 'Assigned' | 'InRepair' | 'Retired';

export interface Asset {
  readonly id: string;
  readonly assetTag: string;
  readonly categoryId: string;
  readonly make: string;
  readonly model: string;
  readonly serialNumber: string | null;
  readonly status: AssetStatus;
}

export interface CreateAssetRequest {
  readonly assetTag: string;
  readonly categoryId: string;
  readonly make: string;
  readonly model: string;
  readonly serialNumber?: string;
}

export interface AssetAssignment {
  readonly id: string;
  readonly assetId: string;
  readonly employeeId: string;
  readonly assignedDate: string;
  readonly returnedDate: string | null;
}

export interface AssignAssetRequest {
  readonly employeeId: string;
}
