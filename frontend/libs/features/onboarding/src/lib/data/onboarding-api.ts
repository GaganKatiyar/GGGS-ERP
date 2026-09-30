import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AddOnboardingTemplateTaskRequest,
  CreateOnboardingTemplateRequest,
  OnboardingCase,
  OnboardingTask,
  OnboardingTemplate,
  OnboardingTemplateListItem,
  OnboardingTemplateTask,
  StartOnboardingCaseRequest,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Onboarding templates and cases, over the gateway's `/api/v1/onboarding/*` routes. */
@Injectable({ providedIn: 'root' })
export class OnboardingApi {
  private readonly http = inject(HttpClient);

  // ---- Templates ----

  listTemplates(): Promise<readonly OnboardingTemplateListItem[]> {
    return firstValueFrom(this.http.get<readonly OnboardingTemplateListItem[]>('/api/v1/onboarding/templates'));
  }

  createTemplate(request: CreateOnboardingTemplateRequest): Promise<OnboardingTemplate> {
    return firstValueFrom(this.http.post<OnboardingTemplate>('/api/v1/onboarding/templates', request));
  }

  listTemplateTasks(templateId: string): Promise<readonly OnboardingTemplateTask[]> {
    return firstValueFrom(
      this.http.get<readonly OnboardingTemplateTask[]>(`/api/v1/onboarding/templates/${templateId}/tasks`),
    );
  }

  addTemplateTask(templateId: string, request: AddOnboardingTemplateTaskRequest): Promise<OnboardingTemplateTask> {
    return firstValueFrom(
      this.http.post<OnboardingTemplateTask>(`/api/v1/onboarding/templates/${templateId}/tasks`, request),
    );
  }

  // ---- Cases ----

  getCase(id: string): Promise<OnboardingCase> {
    return firstValueFrom(this.http.get<OnboardingCase>(`/api/v1/onboarding/cases/${id}`));
  }

  startCase(request: StartOnboardingCaseRequest): Promise<OnboardingCase> {
    return firstValueFrom(this.http.post<OnboardingCase>('/api/v1/onboarding/cases', request));
  }

  cancelCase(id: string): Promise<OnboardingCase> {
    return firstValueFrom(this.http.post<OnboardingCase>(`/api/v1/onboarding/cases/${id}/cancel`, {}));
  }

  listCaseTasks(caseId: string): Promise<readonly OnboardingTask[]> {
    return firstValueFrom(this.http.get<readonly OnboardingTask[]>(`/api/v1/onboarding/cases/${caseId}/tasks`));
  }

  completeTask(taskId: string): Promise<OnboardingTask> {
    return firstValueFrom(this.http.post<OnboardingTask>(`/api/v1/onboarding/tasks/${taskId}/complete`, {}));
  }
}
