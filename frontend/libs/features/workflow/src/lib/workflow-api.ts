import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  ActOnWorkflowTaskRequest,
  CreateWorkflowDefinitionRequest,
  PagedResult,
  WorkflowActionLogItem,
  WorkflowDefinition,
  WorkflowDefinitionVersion,
  WorkflowInstance,
  WorkflowTaskItem,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * Workflow, over the gateway's `/api/v1/workflows/*` routes - mirrors `WorkflowEndpoints.cs` on
 * the server.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowApi {
  private readonly http = inject(HttpClient);

  // ---- Definitions ----

  listDefinitions(): Promise<readonly WorkflowDefinition[]> {
    return firstValueFrom(
      this.http.get<readonly WorkflowDefinition[]>('/api/v1/workflows/definitions'),
    );
  }

  createDefinition(request: CreateWorkflowDefinitionRequest): Promise<WorkflowDefinition> {
    return firstValueFrom(
      this.http.post<WorkflowDefinition>('/api/v1/workflows/definitions', request),
    );
  }

  listVersions(definitionId: string): Promise<readonly WorkflowDefinitionVersion[]> {
    return firstValueFrom(
      this.http.get<readonly WorkflowDefinitionVersion[]>(
        `/api/v1/workflows/definitions/${definitionId}/versions`,
      ),
    );
  }

  createVersion(definitionId: string, definitionJson: string): Promise<WorkflowDefinitionVersion> {
    return firstValueFrom(
      this.http.post<WorkflowDefinitionVersion>(
        `/api/v1/workflows/definitions/${definitionId}/versions`,
        { definitionJson },
      ),
    );
  }

  publishVersion(versionId: string): Promise<WorkflowDefinitionVersion> {
    return firstValueFrom(
      this.http.post<WorkflowDefinitionVersion>(
        `/api/v1/workflows/definitions/versions/${versionId}/publish`,
        {},
      ),
    );
  }

  // ---- Instances ----

  getInstance(id: string): Promise<WorkflowInstance> {
    return firstValueFrom(
      this.http.get<WorkflowInstance>(`/api/v1/workflows/instances/${id}`),
    );
  }

  getInstanceTimeline(id: string): Promise<readonly WorkflowActionLogItem[]> {
    return firstValueFrom(
      this.http.get<readonly WorkflowActionLogItem[]>(
        `/api/v1/workflows/instances/${id}/timeline`,
      ),
    );
  }

  cancelInstance(id: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/workflows/instances/${id}/cancel`, {}),
    );
  }

  // ---- Tasks: the unified approvals inbox ----

  listMyTasks(query: {
    page: number;
    pageSize: number;
    status?: string;
  }): Promise<PagedResult<WorkflowTaskItem>> {
    return firstValueFrom(
      this.http.get<PagedResult<WorkflowTaskItem>>('/api/v1/workflows/tasks', {
        params: toHttpParams(query),
      }),
    );
  }

  approveTask(id: string, request: ActOnWorkflowTaskRequest): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/workflows/tasks/${id}/approve`, request),
    );
  }

  rejectTask(id: string, request: ActOnWorkflowTaskRequest): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/workflows/tasks/${id}/reject`, request),
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
