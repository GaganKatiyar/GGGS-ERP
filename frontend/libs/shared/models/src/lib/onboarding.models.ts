/**
 * Onboarding, mirroring the DTOs in `Gggs.Talent.Api.Endpoints.OnboardingTemplateEndpoints` and
 * `OnboardingCaseEndpoints`.
 */

export type OnboardingAssigneeRole = 'NewHire' | 'Manager' | 'Hr' | 'It' | 'Admin';

export interface OnboardingTemplateListItem {
  readonly id: string;
  readonly name: string;
}

export interface OnboardingTemplate {
  readonly id: string;
  readonly name: string;
}

export interface CreateOnboardingTemplateRequest {
  readonly name: string;
}

export interface OnboardingTemplateTask {
  readonly id: string;
  readonly title: string;
  readonly assigneeRole: OnboardingAssigneeRole;
  readonly dueOffsetDays: number;
  readonly isMandatory: boolean;
}

export interface AddOnboardingTemplateTaskRequest {
  readonly title: string;
  readonly assigneeRole: OnboardingAssigneeRole;
  readonly dueOffsetDays: number;
  readonly isMandatory: boolean;
}

export type OnboardingCaseStatus = 'InProgress' | 'Completed' | 'Cancelled';

export interface OnboardingCase {
  readonly id: string;
  readonly employeeId: string;
  readonly templateId: string;
  readonly startDate: string;
  readonly status: OnboardingCaseStatus;
  readonly completionPercent: number;
}

export interface StartOnboardingCaseRequest {
  readonly employeeId: string;
  readonly templateId: string;
  readonly startDate: string;
}

export type OnboardingTaskStatus = 'NotStarted' | 'Completed';

export interface OnboardingTask {
  readonly id: string;
  readonly caseId: string;
  readonly title: string;
  readonly assigneeRole: OnboardingAssigneeRole;
  readonly dueDate: string;
  readonly isMandatory: boolean;
  readonly status: OnboardingTaskStatus;
  readonly completedDate?: string;
}
