/** Insight contracts (M11): analytics dashboards, global search, the HR calendar, imports and integrations, as the API returns them. */

export type MetricUnit = 'Count' | 'Percent' | 'Currency' | 'Hours' | 'Days' | 'Score';

export interface MetricPoint {
  readonly label: string;
  readonly value: number;
}

export interface Metric {
  readonly key: string;
  readonly area: string;
  readonly label: string;
  readonly value: number;
  readonly unit: MetricUnit;
  readonly hint?: string;
  readonly breakdown?: readonly MetricPoint[];
  readonly trend?: readonly MetricPoint[];
  readonly executive: boolean;
  readonly higherIsBetter: boolean;
}

export interface AnalyticsArea {
  readonly key: string;
  readonly title: string;
  readonly description: string;
}

export interface DashboardView {
  readonly area: string;
  readonly title: string;
  readonly generatedAt: string;
  readonly tenantWide: boolean;
  readonly metrics: readonly Metric[];
}

export type DigestFrequency = 'Weekly' | 'Monthly';

export interface ReportSubscription {
  readonly id: string;
  readonly area: string;
  readonly title: string;
  readonly frequency: DigestFrequency;
  readonly lastSentOn?: string;
}

export interface SearchHit {
  readonly kind: string;
  readonly id: string;
  readonly title: string;
  readonly subtitle?: string;
  readonly link: string;
  readonly score: number;
}

export interface SearchResults {
  readonly term: string;
  readonly hits: readonly SearchHit[];
}

export type CalendarScope = 'mine' | 'team' | 'all';

export interface CalendarEntry {
  readonly kind: string;
  readonly title: string;
  readonly start: string;
  readonly end: string;
  readonly employeeId?: string;
  readonly link?: string;
  readonly detail?: string;
}

export interface CalendarView {
  readonly firstDay: string;
  readonly lastDay: string;
  readonly scope: CalendarScope;
  readonly entries: readonly CalendarEntry[];
}

export interface ImportColumn {
  readonly name: string;
  readonly description: string;
  readonly required: boolean;
  readonly example?: string;
}

export interface ImportHandlerInfo {
  readonly key: string;
  readonly title: string;
  readonly description: string;
  readonly columns: readonly ImportColumn[];
}

export interface ImportRowOutcome {
  readonly row: number;
  readonly errors: readonly string[];
  readonly summary?: string;
}

export interface ImportOutcome {
  readonly rows: number;
  readonly valid: number;
  readonly imported: number;
  readonly results: readonly ImportRowOutcome[];
}

export interface ImportRunItem {
  readonly id: string;
  readonly handlerKey: string;
  readonly fileName: string;
  readonly rows: number;
  readonly valid: number;
  readonly imported: number;
  readonly committed: boolean;
  readonly createdDate: string;
}

export interface IntegrationField {
  readonly key: string;
  readonly label: string;
  readonly secret: boolean;
  readonly required: boolean;
  readonly hint?: string;
}

export interface IntegrationOperation {
  readonly key: string;
  readonly label: string;
  readonly description: string;
  readonly parameters: readonly IntegrationField[];
}

export interface IntegrationAdapter {
  readonly key: string;
  readonly name: string;
  readonly category: string;
  readonly description: string;
  readonly fields: readonly IntegrationField[];
  readonly operations: readonly IntegrationOperation[];
}

export interface IntegrationConnection {
  readonly id: string;
  readonly adapterKey: string;
  readonly adapterName: string;
  readonly name: string;
  readonly settings: Readonly<Record<string, string>>;
  readonly isActive: boolean;
  readonly lastTestedAt?: string;
  readonly lastTestSucceeded?: boolean;
  readonly lastMessage?: string;
}

export interface IntegrationConnectionInput {
  readonly adapterKey: string;
  readonly name: string;
  readonly settings: Readonly<Record<string, string>>;
  readonly isActive: boolean;
}

export interface IntegrationRunOutcome {
  readonly succeeded: boolean;
  readonly message: string;
  readonly records: number;
  readonly fileName?: string;
  readonly contentType?: string;
  readonly contentBase64?: string;
}

export interface IntegrationRunItem {
  readonly id: string;
  readonly connectionId: string;
  readonly operation: string;
  readonly succeeded: boolean;
  readonly message: string;
  readonly records: number;
  readonly fileName?: string;
  readonly createdDate: string;
}

/** A value a trigger offers to conditions and to "{{placeholders}}", with an example for previews. */
export interface AutomationTriggerField {
  readonly key: string;
  readonly label: string;
  readonly sample: string;
}

export interface AutomationTrigger {
  readonly code: string;
  readonly label: string;
  readonly category: string;
  readonly description: string;
  readonly fields: readonly AutomationTriggerField[];
}

export type AutomationConditionOperator = 'Equals' | 'NotEquals' | 'Contains';

export interface AutomationCondition {
  readonly field: string;
  readonly operator: AutomationConditionOperator;
  readonly value: string;
}

/** A connection an automation can act through. */
export interface AutomationConnection {
  readonly id: string;
  readonly name: string;
  readonly adapterKey: string;
  readonly adapterName: string;
  readonly isActive: boolean;
  readonly operations: readonly IntegrationOperation[];
  readonly channel?: string;
}

export type AutomationDeliveryStatus = 'Pending' | 'Succeeded' | 'Failed' | 'Abandoned';

export interface AutomationInput {
  readonly name: string;
  readonly triggerCode: string;
  readonly conditions: readonly AutomationCondition[];
  readonly connectionId: string;
  readonly operation: string;
  readonly parameters: Readonly<Record<string, string>>;
  readonly isActive: boolean;
}

export interface Automation extends AutomationInput {
  readonly id: string;
  readonly triggerLabel: string;
  readonly connectionName: string;
  readonly adapterKey: string;
  readonly operationLabel: string;
  readonly lastStatus?: AutomationDeliveryStatus;
  readonly lastDeliveredAt?: string;
}

export interface AutomationTestOutcome {
  readonly succeeded: boolean;
  readonly message: string;
  readonly parameters: Readonly<Record<string, string>>;
}

export interface AutomationDelivery {
  readonly id: string;
  readonly automationId: string;
  readonly automationName: string;
  readonly triggerCode: string;
  readonly status: AutomationDeliveryStatus;
  readonly attempts: number;
  readonly lastMessage?: string;
  readonly nextAttemptAt?: string;
  readonly completedAt?: string;
  readonly createdDate: string;
  readonly parameters: Readonly<Record<string, string>>;
}

/** A channel on the Sending page: the connection set for it, and those that could carry it. */
export interface MessageChannel {
  readonly channel: string;
  readonly connectionId?: string;
  readonly connections: readonly AutomationConnection[];
}

export interface ChannelSendOutcome {
  readonly succeeded: boolean;
  readonly message: string;
}

/** A directory connection, and what it does by itself. */
export interface AccountConnection {
  readonly connectionId: string;
  readonly name: string;
  readonly adapterName: string;
  readonly isActive: boolean;
  readonly provisionOnHire: boolean;
  readonly disableOnExit: boolean;
}

export type DirectoryAccountStatus = 'Creating' | 'Active' | 'Disabling' | 'Disabled';

/** One person's account in an outside directory. */
export interface DirectoryAccount {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName: string;
  readonly employeeCode: string;
  readonly connectionId: string;
  readonly connectionName: string;
  readonly login?: string;
  readonly status: DirectoryAccountStatus;
  /** Tries have run out: on a leaver, an account still open. */
  readonly needsAttention: boolean;
  readonly attempts: number;
  readonly lastMessage?: string;
  readonly nextAttemptAt?: string;
  readonly disabledAt?: string;
  readonly createdDate: string;
}
