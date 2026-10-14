/**
 * Platform services contracts (M3): workflow, notification templates, documents and policies, as
 * the PeopleOS API returns them. Enums travel as their names.
 */

// ----- Workflow ------------------------------------------------------------------------------

export type ApproverKind = 'ReportingManager' | 'SkipLevelManager' | 'DepartmentHead' | 'HrBusinessPartner' | 'Employee';

export type StageMode = 'All' | 'Any';

export type ConditionOperator = 'Equals' | 'NotEquals' | 'GreaterThan' | 'GreaterThanOrEqual' | 'LessThan' | 'LessThanOrEqual' | 'In';

export interface ApproverRule {
  readonly kind: ApproverKind;
  readonly employeeId?: string | null;
}

export interface StageCondition {
  readonly field: string;
  readonly operator: ConditionOperator;
  readonly value: string;
}

export interface WorkflowStage {
  readonly name: string;
  readonly mode: StageMode;
  readonly approvers: readonly ApproverRule[];
  readonly condition?: StageCondition | null;
  readonly slaHours?: number | null;
  readonly escalateTo?: ApproverRule | null;
}

export interface WorkflowSubjectField {
  readonly key: string;
  readonly label: string;
  readonly type: 'Text' | 'Number';
}

export interface WorkflowSubject {
  readonly code: string;
  readonly name: string;
  readonly module: string;
  readonly fields: readonly WorkflowSubjectField[];
}

export interface WorkflowDefinition {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description?: string;
  readonly subjectType: string;
  readonly subjectName: string;
  readonly version: number;
  readonly isActive: boolean;
  readonly fallbackApproverEmployeeId?: string;
  readonly fallbackApproverName?: string;
  readonly stages: readonly WorkflowStage[];
}

export interface WorkflowDefinitionInput {
  readonly code: string;
  readonly name: string;
  readonly description?: string | null;
  readonly subjectType: string;
  readonly stages: readonly WorkflowStage[];
  readonly fallbackApproverEmployeeId?: string | null;
}

export type WorkflowTaskStatus = 'Pending' | 'Approved' | 'Rejected' | 'SentBack' | 'Closed' | 'Reassigned';

export type WorkflowRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'SentBack' | 'Withdrawn';

export interface WorkflowTask {
  readonly id: string;
  readonly instanceId: string;
  readonly subjectType: string;
  readonly subjectName: string;
  readonly title: string;
  readonly link?: string;
  readonly stageIndex: number;
  readonly round: number;
  readonly stageName: string;
  readonly status: WorkflowTaskStatus;
  readonly assigneeEmployeeId: string;
  readonly assigneeName?: string;
  readonly delegatedFromName?: string;
  readonly requesterName?: string;
  readonly submittedDate: string;
  readonly dueDate?: string;
  readonly isOverdue: boolean;
  readonly escalatedDate?: string;
  readonly actedDate?: string;
  readonly comment?: string;
}

export interface WorkflowRequest {
  readonly id: string;
  readonly subjectType: string;
  readonly subjectName: string;
  readonly subjectId: string;
  readonly title: string;
  readonly link?: string;
  readonly status: WorkflowRequestStatus;
  readonly round: number;
  readonly requesterName?: string;
  readonly currentStageName?: string;
  readonly submittedDate: string;
  readonly completedDate?: string;
}

export type WorkflowStageState = 'InProgress' | 'Approved' | 'Rejected' | 'SentBack' | 'Skipped' | 'Waiting' | 'Closed';

export interface WorkflowStageView {
  readonly index: number;
  readonly name: string;
  readonly mode: StageMode;
  readonly condition?: string;
  readonly slaHours?: number;
  readonly state: WorkflowStageState;
}

export type WorkflowActionKind =
  | 'Submitted'
  | 'Approved'
  | 'Rejected'
  | 'SentBack'
  | 'Resubmitted'
  | 'Withdrawn'
  | 'Escalated'
  | 'Reassigned'
  | 'StageSkipped'
  | 'Completed'
  | 'AutoApproved';

export interface WorkflowHistoryEntry {
  readonly kind: WorkflowActionKind;
  readonly actorName?: string;
  readonly comment?: string;
  readonly occurredDate: string;
}

export interface WorkflowRequestDetail {
  readonly request: WorkflowRequest;
  readonly fields: Readonly<Record<string, string>>;
  readonly stages: readonly WorkflowStageView[];
  readonly tasks: readonly WorkflowTask[];
  readonly history: readonly WorkflowHistoryEntry[];
  readonly canWithdraw: boolean;
  readonly canResubmit: boolean;
}

export interface Delegation {
  readonly id: string;
  readonly delegatorEmployeeId: string;
  readonly delegatorName?: string;
  readonly delegateEmployeeId: string;
  readonly delegateName?: string;
  readonly startsOn: string;
  readonly endsOn: string;
  readonly subjectType?: string;
  readonly reason?: string;
  readonly isActive: boolean;
  readonly isCurrent: boolean;
}

export interface DelegationInput {
  readonly delegatorEmployeeId?: string | null;
  readonly delegateEmployeeId: string;
  readonly startsOn: string;
  readonly endsOn: string;
  readonly subjectType?: string | null;
  readonly reason?: string | null;
}

// ----- Notifications -------------------------------------------------------------------------

export interface NotificationTemplate {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly subject: string;
  readonly body: string;
  readonly sendInApp: boolean;
  readonly sendEmail: boolean;
  readonly isActive: boolean;
}

// ----- Documents -----------------------------------------------------------------------------

export type DocumentCategory = 'Identity' | 'Education' | 'Employment' | 'Letter' | 'Policy' | 'Contract' | 'Other';

export type DocumentOwnerType = 'Employee' | 'Organization';

export interface DocumentTypeItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly category: DocumentCategory;
  readonly employeeVisible: boolean;
  readonly requiresExpiry: boolean;
  readonly isSystem: boolean;
  readonly isActive: boolean;
}

export interface DocumentItem {
  readonly id: string;
  readonly ownerType: DocumentOwnerType;
  readonly ownerId?: string;
  readonly ownerName?: string;
  readonly documentTypeId: string;
  readonly typeName: string;
  readonly category: DocumentCategory;
  readonly title: string;
  readonly currentVersion: number;
  readonly expiresOn?: string;
  readonly isExpired: boolean;
  readonly employeeVisible: boolean;
  readonly source: 'Uploaded' | 'Generated';
  readonly isActive: boolean;
  readonly createdDate: string;
  readonly modifiedDate?: string;
}

export interface DocumentVersionItem {
  readonly number: number;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly note?: string;
  readonly createdDate: string;
}

export type SignatureStatus = 'Requested' | 'Signed' | 'Declined' | 'Cancelled';

export interface DocumentSignature {
  readonly id: string;
  readonly documentId: string;
  readonly documentTitle: string;
  readonly versionNumber: number;
  readonly signerEmployeeId: string;
  readonly signerName?: string;
  readonly status: SignatureStatus;
  readonly provider: string;
  readonly requestedDate: string;
  readonly completedDate?: string;
  readonly comment?: string;
}

export interface DocumentDetail {
  readonly document: DocumentItem;
  readonly versions: readonly DocumentVersionItem[];
  readonly signatures: readonly DocumentSignature[];
}

export interface DocumentTemplateItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description?: string;
  readonly documentTypeId: string;
  readonly typeName: string;
  readonly body: string;
  readonly isActive: boolean;
}

export interface DocumentUpload {
  readonly ownerType: DocumentOwnerType;
  readonly ownerId?: string | null;
  readonly documentTypeId: string;
  readonly title: string;
  readonly expiresOn?: string | null;
  readonly employeeVisible?: boolean | null;
  readonly note?: string | null;
  readonly file: File;
}

// ----- Policies ------------------------------------------------------------------------------

export type PolicyCategory =
  | 'General'
  | 'Conduct'
  | 'Leave'
  | 'Attendance'
  | 'Compensation'
  | 'Travel'
  | 'InformationSecurity'
  | 'HealthAndSafety'
  | 'Other';

export type PolicyVersionStatus = 'Draft' | 'PendingApproval' | 'Published' | 'Superseded';

export interface PolicyItem {
  readonly id: string;
  readonly code: string;
  readonly title: string;
  readonly summary?: string;
  readonly category: PolicyCategory;
  readonly requiresAcknowledgement: boolean;
  readonly audienceDepartmentIds: readonly string[];
  readonly audienceLocationIds: readonly string[];
  readonly audience: string;
  readonly isActive: boolean;
  readonly publishedVersion?: number;
  readonly effectiveFrom?: string;
  readonly openVersionStatus?: PolicyVersionStatus;
}

export interface PolicyVersion {
  readonly id: string;
  readonly number: number;
  readonly content: string;
  readonly changeSummary?: string;
  readonly effectiveFrom: string;
  readonly status: PolicyVersionStatus;
  readonly publishedDate?: string;
  readonly workflowInstanceId?: string;
}

export interface PolicyDetail {
  readonly policy: PolicyItem;
  readonly versions: readonly PolicyVersion[];
}

export interface PolicyInput {
  readonly code: string;
  readonly title: string;
  readonly summary?: string | null;
  readonly category: PolicyCategory;
  readonly requiresAcknowledgement: boolean;
  readonly audienceDepartmentIds: readonly string[];
  readonly audienceLocationIds: readonly string[];
  readonly content?: string | null;
  readonly effectiveFrom?: string | null;
}

export interface PolicyVersionInput {
  readonly content: string;
  readonly changeSummary?: string | null;
  readonly effectiveFrom: string;
}

export interface PublishResult {
  readonly status: PolicyVersionStatus;
  readonly workflowInstanceId?: string;
}

export interface AcknowledgementRow {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly acknowledgedDate?: string;
}

export interface AcknowledgementReport {
  readonly policyVersionId: string;
  readonly versionNumber: number;
  readonly audienceCount: number;
  readonly acknowledgedCount: number;
  readonly rows: readonly AcknowledgementRow[];
}

export interface MyPolicy {
  readonly policyId: string;
  readonly title: string;
  readonly summary?: string;
  readonly category: PolicyCategory;
  readonly versionId: string;
  readonly versionNumber: number;
  readonly effectiveFrom: string;
  readonly requiresAcknowledgement: boolean;
  readonly acknowledgedDate?: string;
}

export interface MyPolicyDetail {
  readonly policy: MyPolicy;
  readonly content: string;
  readonly changeSummary?: string;
}

/** Display names for the enums the API sends as identifiers. */
export const POLICY_CATEGORY_LABELS: Readonly<Record<PolicyCategory, string>> = {
  General: 'General',
  Conduct: 'Code of conduct',
  Leave: 'Leave',
  Attendance: 'Attendance',
  Compensation: 'Compensation',
  Travel: 'Travel',
  InformationSecurity: 'Information security',
  HealthAndSafety: 'Health and safety',
  Other: 'Other',
};

export const DOCUMENT_CATEGORY_LABELS: Readonly<Record<DocumentCategory, string>> = {
  Identity: 'Identity',
  Education: 'Education',
  Employment: 'Employment',
  Letter: 'Letters',
  Policy: 'Policies',
  Contract: 'Contracts',
  Other: 'Other',
};

export const APPROVER_KIND_LABELS: Readonly<Record<ApproverKind, string>> = {
  ReportingManager: "Requester's manager",
  SkipLevelManager: "Manager's manager",
  DepartmentHead: 'Department head',
  HrBusinessPartner: 'HR business partner',
  Employee: 'A named person',
};
