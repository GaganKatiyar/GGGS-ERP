import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AcknowledgementReport,
  MyPolicy,
  MyPolicyDetail,
  PolicyDetail,
  PolicyInput,
  PolicyItem,
  PolicyVersion,
  PolicyVersionInput,
  PublishResult,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** The Policies API (`/api/v1/policies`, `/api/v1/me/policies`). Only this feature uses it. */
@Injectable({ providedIn: 'root' })
export class PoliciesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/policies';

  list(includeRetired = false): Promise<readonly PolicyItem[]> {
    return firstValueFrom(this.http.get<readonly PolicyItem[]>(this.base, { params: { includeRetired } }));
  }

  get(id: string): Promise<PolicyDetail> {
    return firstValueFrom(this.http.get<PolicyDetail>(`${this.base}/${id}`));
  }

  create(body: PolicyInput): Promise<PolicyDetail> {
    return firstValueFrom(this.http.post<PolicyDetail>(this.base, body));
  }

  update(id: string, body: PolicyInput): Promise<PolicyDetail> {
    return firstValueFrom(this.http.put<PolicyDetail>(`${this.base}/${id}`, body));
  }

  async setRetired(id: string, retired: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/${retired ? 'retire' : 'reinstate'}`, {}));
  }

  addDraft(id: string, body: PolicyVersionInput): Promise<PolicyVersion> {
    return firstValueFrom(this.http.post<PolicyVersion>(`${this.base}/${id}/versions`, body));
  }

  editDraft(id: string, versionId: string, body: PolicyVersionInput): Promise<PolicyVersion> {
    return firstValueFrom(this.http.put<PolicyVersion>(`${this.base}/${id}/versions/${versionId}`, body));
  }

  publish(id: string, versionId: string): Promise<PublishResult> {
    return firstValueFrom(this.http.post<PublishResult>(`${this.base}/${id}/versions/${versionId}/publish`, {}));
  }

  acknowledgements(id: string): Promise<AcknowledgementReport> {
    return firstValueFrom(this.http.get<AcknowledgementReport>(`${this.base}/${id}/acknowledgements`));
  }

  mine(): Promise<readonly MyPolicy[]> {
    return firstValueFrom(this.http.get<readonly MyPolicy[]>('/api/v1/me/policies'));
  }

  myPolicy(id: string): Promise<MyPolicyDetail> {
    return firstValueFrom(this.http.get<MyPolicyDetail>(`/api/v1/me/policies/${id}`));
  }

  async acknowledge(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/me/policies/${id}/acknowledge`, {}));
  }
}
