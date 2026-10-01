import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  Asset,
  AssetAssignment,
  AssetCategory,
  AssignAssetRequest,
  CreateAssetCategoryRequest,
  CreateAssetRequest,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Asset categories, the register, and assigning/returning assets, over the gateway's
 * `/api/v1/assets/*` routes. No maintenance history or software licenses in this v1 slice. */
@Injectable({ providedIn: 'root' })
export class AssetsApi {
  private readonly http = inject(HttpClient);

  // ---- Categories ----

  listCategories(): Promise<readonly AssetCategory[]> {
    return firstValueFrom(this.http.get<readonly AssetCategory[]>('/api/v1/assets/categories'));
  }

  createCategory(request: CreateAssetCategoryRequest): Promise<AssetCategory> {
    return firstValueFrom(this.http.post<AssetCategory>('/api/v1/assets/categories', request));
  }

  deactivateCategory(id: string): Promise<AssetCategory> {
    return firstValueFrom(this.http.post<AssetCategory>(`/api/v1/assets/categories/${id}/deactivate`, {}));
  }

  reactivateCategory(id: string): Promise<AssetCategory> {
    return firstValueFrom(this.http.post<AssetCategory>(`/api/v1/assets/categories/${id}/reactivate`, {}));
  }

  // ---- Register ----

  listAssets(): Promise<readonly Asset[]> {
    return firstValueFrom(this.http.get<readonly Asset[]>('/api/v1/assets'));
  }

  getAsset(id: string): Promise<Asset> {
    return firstValueFrom(this.http.get<Asset>(`/api/v1/assets/${id}`));
  }

  createAsset(request: CreateAssetRequest): Promise<Asset> {
    return firstValueFrom(this.http.post<Asset>('/api/v1/assets', request));
  }

  sendForRepair(id: string): Promise<Asset> {
    return firstValueFrom(this.http.post<Asset>(`/api/v1/assets/${id}/send-for-repair`, {}));
  }

  returnFromRepair(id: string): Promise<Asset> {
    return firstValueFrom(this.http.post<Asset>(`/api/v1/assets/${id}/return-from-repair`, {}));
  }

  retire(id: string): Promise<Asset> {
    return firstValueFrom(this.http.post<Asset>(`/api/v1/assets/${id}/retire`, {}));
  }

  // ---- Assignment ----

  listMyAssignments(): Promise<readonly AssetAssignment[]> {
    return firstValueFrom(this.http.get<readonly AssetAssignment[]>('/api/v1/assets/assignments/me'));
  }

  listEmployeeAssignments(employeeId: string): Promise<readonly AssetAssignment[]> {
    return firstValueFrom(this.http.get<readonly AssetAssignment[]>(`/api/v1/assets/employees/${employeeId}/assignments`));
  }

  assign(assetId: string, request: AssignAssetRequest): Promise<AssetAssignment> {
    return firstValueFrom(this.http.post<AssetAssignment>(`/api/v1/assets/${assetId}/assign`, request));
  }

  returnAsset(assignmentId: string): Promise<AssetAssignment> {
    return firstValueFrom(this.http.post<AssetAssignment>(`/api/v1/assets/assignments/${assignmentId}/return`, {}));
  }
}
