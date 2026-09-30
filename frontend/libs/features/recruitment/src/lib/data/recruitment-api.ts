import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  CreateJobPostingRequest,
  CreateJobRequisitionRequest,
  JobPosting,
  JobPostingListItem,
  JobRequisition,
  JobRequisitionListItem,
  PagedResult,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Job requisitions, over the gateway's `/api/v1/recruitment/requisitions` routes. */
@Injectable({ providedIn: 'root' })
export class RecruitmentApi {
  private readonly http = inject(HttpClient);

  list(query: { page: number; pageSize: number; search?: string }): Promise<PagedResult<JobRequisitionListItem>> {
    const params: Record<string, string> = {
      page: String(query.page),
      pageSize: String(query.pageSize),
    };

    if (query.search) {
      params['search'] = query.search;
    }

    return firstValueFrom(
      this.http.get<PagedResult<JobRequisitionListItem>>('/api/v1/recruitment/requisitions', { params }),
    );
  }

  get(id: string): Promise<JobRequisition> {
    return firstValueFrom(this.http.get<JobRequisition>(`/api/v1/recruitment/requisitions/${id}`));
  }

  create(request: CreateJobRequisitionRequest): Promise<JobRequisition> {
    return firstValueFrom(this.http.post<JobRequisition>('/api/v1/recruitment/requisitions', request));
  }

  open(id: string): Promise<JobRequisition> {
    return firstValueFrom(this.http.post<JobRequisition>(`/api/v1/recruitment/requisitions/${id}/open`, {}));
  }

  putOnHold(id: string): Promise<JobRequisition> {
    return firstValueFrom(this.http.post<JobRequisition>(`/api/v1/recruitment/requisitions/${id}/hold`, {}));
  }

  resume(id: string): Promise<JobRequisition> {
    return firstValueFrom(this.http.post<JobRequisition>(`/api/v1/recruitment/requisitions/${id}/resume`, {}));
  }

  close(id: string): Promise<JobRequisition> {
    return firstValueFrom(this.http.post<JobRequisition>(`/api/v1/recruitment/requisitions/${id}/close`, {}));
  }

  cancel(id: string): Promise<JobRequisition> {
    return firstValueFrom(this.http.post<JobRequisition>(`/api/v1/recruitment/requisitions/${id}/cancel`, {}));
  }

  // ---- Postings, nested under their requisition ----

  listPostings(requisitionId: string): Promise<readonly JobPostingListItem[]> {
    return firstValueFrom(
      this.http.get<readonly JobPostingListItem[]>(`/api/v1/recruitment/requisitions/${requisitionId}/postings`),
    );
  }

  createPosting(requisitionId: string, request: CreateJobPostingRequest): Promise<JobPosting> {
    return firstValueFrom(
      this.http.post<JobPosting>(`/api/v1/recruitment/requisitions/${requisitionId}/postings`, request),
    );
  }

  publishPosting(requisitionId: string, postingId: string): Promise<JobPosting> {
    return firstValueFrom(
      this.http.post<JobPosting>(`/api/v1/recruitment/requisitions/${requisitionId}/postings/${postingId}/publish`, {}),
    );
  }

  closePosting(requisitionId: string, postingId: string): Promise<JobPosting> {
    return firstValueFrom(
      this.http.post<JobPosting>(`/api/v1/recruitment/requisitions/${requisitionId}/postings/${postingId}/close`, {}),
    );
  }
}
