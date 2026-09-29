/**
 * Workflow, mirroring the DTOs in `Gggs.Workflow.Api.Contracts.WorkflowContracts`.
 */

export type WorkflowVersionStatus = 'Draft' | 'Published' | 'Retired';

export interface WorkflowDefinition {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly entityType: string;
  readonly isSystem: boolean;
}

export interface CreateWorkflowDefinitionRequest {
  readonly code: string;
  readonly name: string;
  readonly entityType: string;
}

export interface WorkflowDefinitionVersion {
  readonly id: string;
  readonly definitionId: string;
  readonly version: number;
  readonly status: WorkflowVersionStatus;
  readonly definitionJson: string;
  readonly publishedDate?: string;
}

export type WorkflowInstanceStatus = 'Running' | 'Approved' | 'Rejected' | 'Cancelled' | 'Error';

export interface WorkflowInstance {
  readonly id: string;
  readonly entityType: string;
  readonly entityId: string;
  readonly sourceService: string;
  readonly initiatorUserId: string;
  readonly subjectEmployeeId: string;
  readonly status: WorkflowInstanceStatus;
  readonly currentStepKey?: string;
  readonly startedDate: string;
  readonly completedDate?: string;
}

export interface WorkflowActionLogItem {
  readonly id: string;
  readonly action: string;
  readonly actorUserId: string;
  readonly onBehalfOfUserId?: string;
  readonly comment?: string;
  readonly occurredDate: string;
}

export type WorkflowTaskStatus =
  | 'Pending'
  | 'Approved'
  | 'Rejected'
  | 'SentBack'
  | 'Delegated'
  | 'Escalated'
  | 'Skipped'
  | 'Cancelled';

export const WORKFLOW_TASK_STATUS_LABELS: Readonly<Record<WorkflowTaskStatus, string>> = {
  Pending: 'Pending',
  Approved: 'Approved',
  Rejected: 'Rejected',
  SentBack: 'Sent back',
  Delegated: 'Delegated',
  Escalated: 'Escalated',
  Skipped: 'Skipped',
  Cancelled: 'Cancelled',
};

/**
 * One row in the unified approvals inbox - architecture.md §12.1. `entityType` is the source
 * module's own record kind (`LEAVE_REQUEST`, `ATTENDANCE_REGULARIZATION`, …), and `entityId` is
 * that record's id, so a click-through can route to the right feature's detail view.
 */
export interface WorkflowTaskItem {
  readonly id: string;
  readonly instanceId: string;
  readonly entityType: string;
  readonly entityId: string;
  readonly sourceService: string;
  readonly stepKey: string;
  readonly originalAssigneeUserId?: string;
  readonly status: WorkflowTaskStatus;
  readonly dueDate?: string;
}

export interface ActOnWorkflowTaskRequest {
  readonly comment?: string;
}

export interface Delegation {
  readonly id: string;
  readonly fromUserId: string;
  readonly toUserId: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly reason?: string;
}

export interface CreateDelegationRequest {
  readonly toUserId: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly reason?: string;
}
