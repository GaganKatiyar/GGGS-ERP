import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  Delegation,
  DelegationInput,
  PagedResult,
  WorkflowDefinition,
  WorkflowDefinitionInput,
  WorkflowRequest,
  WorkflowRequestDetail,
  WorkflowRequestStatus,
  WorkflowSubject,
  WorkflowTask,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

export type TaskDecision = 'approve' | 'reject' | 'send-back';

/**
 * The Workflow API (`/api/v1/workflows`). Shared because the approvals inbox and the admin
 * definition editor both use it, and features never import each other.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/workflows';

  tasks(status: 'pending' | 'done', page = 1, pageSize = 50): Promise<PagedResult<WorkflowTask>> {
    return firstValueFrom(this.http.get<PagedResult<WorkflowTask>>(`${this.base}/tasks`, { params: { status, page, pageSize } }));
  }

  async pendingCount(): Promise<number> {
    return (await firstValueFrom(this.http.get<{ pending: number }>(`${this.base}/tasks/count`))).pending;
  }

  async decide(taskId: string, decision: TaskDecision, comment: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/tasks/${taskId}/${decision}`, { comment }));
  }

  async reassign(taskId: string, employeeId: string, comment: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/tasks/${taskId}/reassign`, { employeeId, comment }));
  }

  requests(query: { scope?: 'mine' | 'all'; status?: WorkflowRequestStatus | ''; page?: number; pageSize?: number }): Promise<PagedResult<WorkflowRequest>> {
    const params: Record<string, string | number> = {};
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== '') {
        params[key] = value as string | number;
      }
    }
    return firstValueFrom(this.http.get<PagedResult<WorkflowRequest>>(`${this.base}/instances`, { params }));
  }

  request(id: string): Promise<WorkflowRequestDetail> {
    return firstValueFrom(this.http.get<WorkflowRequestDetail>(`${this.base}/instances/${id}`));
  }

  async withdraw(id: string, comment: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/instances/${id}/withdraw`, { comment }));
  }

  async resubmit(id: string, comment: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/instances/${id}/resubmit`, { comment }));
  }

  delegations(scope: 'mine' | 'all' = 'mine'): Promise<readonly Delegation[]> {
    return firstValueFrom(this.http.get<readonly Delegation[]>(`${this.base}/delegations`, { params: { scope } }));
  }

  createDelegation(body: DelegationInput): Promise<Delegation> {
    return firstValueFrom(this.http.post<Delegation>(`${this.base}/delegations`, body));
  }

  async revokeDelegation(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/delegations/${id}/revoke`, {}));
  }

  subjects(): Promise<readonly WorkflowSubject[]> {
    return firstValueFrom(this.http.get<readonly WorkflowSubject[]>(`${this.base}/subjects`));
  }

  definitions(): Promise<readonly WorkflowDefinition[]> {
    return firstValueFrom(this.http.get<readonly WorkflowDefinition[]>(`${this.base}/definitions`));
  }

  createDefinition(body: WorkflowDefinitionInput): Promise<WorkflowDefinition> {
    return firstValueFrom(this.http.post<WorkflowDefinition>(`${this.base}/definitions`, body));
  }

  updateDefinition(id: string, body: WorkflowDefinitionInput): Promise<WorkflowDefinition> {
    return firstValueFrom(this.http.put<WorkflowDefinition>(`${this.base}/definitions/${id}`, body));
  }

  setDefinitionActive(id: string, active: boolean): Promise<WorkflowDefinition> {
    return firstValueFrom(this.http.post<WorkflowDefinition>(`${this.base}/definitions/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }
}
