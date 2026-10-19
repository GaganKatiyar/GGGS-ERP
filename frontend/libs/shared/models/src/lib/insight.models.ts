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
