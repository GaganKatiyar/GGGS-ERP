/*
 * The platform console's shapes, as Gggs.PlatformApi sends them (backend: Platform/Console/ConsoleModels.cs
 * and Identity/Console/IdentityConsoleModule.cs). Dates arrive as ISO strings; DateOnly as yyyy-MM-dd.
 */

export type TenantStatus = 'Provisioning' | 'Active' | 'Suspended' | 'Offboarding' | 'Archived' | 'Purged';

export type IsolationTier = 'Pooled' | 'Dedicated';

export interface UsageView {
  readonly employees: number;
  readonly users: number;
  readonly signedInToday: number;
  readonly lastSignIn: string | null;
  readonly emailsSentToday: number;
  readonly emailsFailedToday: number;
  readonly emailsLoggedToday: number;
  readonly emailChannel: string | null;
  readonly signIn: string | null;
  readonly measuredDate: string;
}

export interface OrganisationRow {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly status: TenantStatus;
  readonly planCode: string | null;
  readonly planName: string | null;
  readonly subscriptionStatus: string | null;
  readonly seatLimit: number | null;
  readonly trialEndsOn: string | null;
  readonly renewsOn: string | null;
  readonly purgeEligibleDate: string | null;
  readonly suspensionReason: string | null;
  readonly dataRegion: string;
  readonly usage: UsageView | null;
  readonly createdDate: string;
}

export interface ModuleState {
  readonly code: string;
  readonly inPlan: boolean;
  readonly enabled: boolean;
  readonly overridden: boolean;
}

export interface SubscriptionView {
  readonly id: string;
  readonly planCode: string;
  readonly planName: string;
  readonly planMaxEmployees: number | null;
  readonly status: string;
  readonly startDate: string;
  readonly trialEndsOn: string | null;
  readonly renewsOn: string | null;
  readonly seatLimit: number | null;
  readonly billingReference: string | null;
}

export interface OrganisationDetail {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly legalName: string | null;
  readonly status: TenantStatus;
  readonly isolationTier: IsolationTier;
  readonly countryCode: string;
  readonly dataRegion: string;
  readonly defaultTimeZone: string;
  readonly defaultCurrency: string;
  readonly defaultLocale: string;
  readonly activatedDate: string | null;
  readonly suspendedDate: string | null;
  readonly suspensionReason: string | null;
  readonly purgeEligibleDate: string | null;
  readonly subscription: SubscriptionView | null;
  readonly modules: readonly ModuleState[];
  readonly usage: UsageView | null;
  readonly createdDate: string;
}

export interface CreateOrganisationBody {
  code: string;
  name: string;
  legalName?: string | null;
  countryCode: string;
  dataRegion: string;
  defaultTimeZone: string;
  defaultCurrency: string;
  defaultLocale: string;
  planCode: string;
  isolationTier: IsolationTier;
  trialDays: number | null;
  seatLimit: number | null;
  billingReference: string | null;
  administratorEmail: string;
  addOns: string[];
}

export interface UpdateSubscriptionBody {
  planCode: string;
  seatLimit: number | null;
  paid: boolean;
  trialEndsOn: string | null;
  renewsOn: string | null;
  billingReference: string | null;
}

export interface AuditItem {
  readonly id: string;
  readonly occurredDate: string;
  readonly staffName: string;
  readonly tenantId: string | null;
  readonly tenantCode: string | null;
  readonly action: string;
  readonly summary: string;
  readonly reason: string | null;
}

export interface PlanView {
  readonly code: string;
  readonly name: string;
  readonly edition: string;
  readonly maxEmployees: number | null;
  readonly isActive: boolean;
  readonly features: readonly string[];
  readonly organisations: number;
}

export interface ModuleUsage {
  readonly code: string;
  readonly organisations: number;
}

export interface PlansView {
  readonly plans: readonly PlanView[];
  readonly modules: readonly ModuleUsage[];
}

export interface CountByKey {
  readonly key: string;
  readonly count: number;
}

export interface MonthCount {
  readonly month: string;
  readonly count: number;
}

export interface AttentionItem {
  readonly kind: string;
  readonly tenantId: string | null;
  readonly text: string;
}

export interface UpcomingItem {
  readonly tenantId: string;
  readonly name: string;
  readonly what: string;
  readonly when: string;
  readonly planName: string | null;
}

export interface DashboardView {
  readonly byStatus: readonly CountByKey[];
  readonly byPlan: readonly CountByKey[];
  readonly employees: number;
  readonly signedInToday: number;
  readonly emailsSentToday: number;
  readonly emailsFailedToday: number;
  readonly newPerMonth: readonly MonthCount[];
  readonly upcoming: readonly UpcomingItem[];
  readonly attention: readonly AttentionItem[];
  readonly recent: readonly AuditItem[];
  readonly failedJobs: number;
}

export interface ServiceStatus {
  readonly name: string;
  readonly state: string;
  readonly healthy: boolean;
  readonly detail: string;
}

export interface RecurringJobView {
  readonly id: string;
  readonly cron: string;
  readonly lastExecution: string | null;
  readonly nextExecution: string | null;
  readonly lastState: string | null;
  readonly error: string | null;
}

export interface FailedJobView {
  readonly id: string;
  readonly job: string;
  readonly failedAt: string | null;
  readonly reason: string | null;
  readonly tenantId: string | null;
  readonly tenantName: string | null;
}

export interface EmailHealthRow {
  readonly tenantId: string;
  readonly name: string;
  readonly channel: string | null;
  readonly sent: number;
  readonly failed: number;
  readonly logged: number;
}

export interface HealthView {
  readonly services: readonly ServiceStatus[];
  readonly failed: readonly FailedJobView[];
  readonly jobs: readonly RecurringJobView[];
  readonly email: readonly EmailHealthRow[];
}

export interface StaffRoleRef {
  readonly id: string;
  readonly name: string;
}

export interface PlatformStaffRow {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly status: 'Invited' | 'Active' | 'Disabled' | string;
  readonly lastLoginDate: string | null;
  readonly invitationExpiresDate: string | null;
  readonly lockedOut: boolean;
  readonly roles: readonly StaffRoleRef[];
}

export interface PlatformRoleRow {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description: string | null;
  readonly permissions: readonly string[];
  readonly holders: number;
}

export interface InviteStaffBody {
  email: string;
  displayName: string;
  roleIds: string[];
}

export interface InviteStaffResult {
  readonly platformStaffId: string;
  readonly invitationToken: string;
  readonly expiresDate: string;
}

export interface OrganisationAdministrator {
  readonly userId: string;
  readonly email: string;
  readonly displayName: string;
  readonly status: string;
  readonly lastLoginDate: string | null;
  readonly invitationExpiresDate: string | null;
}

export interface AdministratorInvitationSent {
  readonly userId: string;
  readonly email: string;
  readonly expiresDate: string;
}

export type DatabaseAuthentication = 'SqlLogin' | 'Integrated' | 'ManagedIdentity';

export type DatabaseEncryption = 'Strict' | 'TrustCertificate' | 'None';

export interface SharedDatabaseView {
  readonly server: string;
  readonly databaseName: string;
  readonly otherOrganisations: number;
  readonly tenantId: string;
  readonly sizeMegabytes: number | null;
}

export interface DedicatedDatabaseView {
  readonly server: string;
  readonly databaseName: string;
  readonly authentication: DatabaseAuthentication;
  readonly userName: string | null;
  readonly encryption: DatabaseEncryption;
  readonly hasPassword: boolean;
  readonly passwordChangedDate: string | null;
  readonly passwordChangedBy: string | null;
  readonly updatedDate: string;
  readonly updatedBy: string;
  readonly lastTestedDate: string | null;
  readonly lastTestSucceeded: boolean | null;
  readonly lastTestResult: string | null;
  readonly connectionStringPreview: string;
}

/** `inUse` is where the HR app actually keeps the organisation: "Shared" until dedicated hosting is switched on. */
export interface OrganisationDatabaseView {
  readonly tenantId: string;
  readonly inUse: string;
  readonly shared: SharedDatabaseView;
  readonly dedicated: DedicatedDatabaseView | null;
}

/** A null password keeps the stored one. */
export interface SaveDatabaseBody {
  server: string;
  databaseName: string;
  authentication: DatabaseAuthentication;
  userName: string | null;
  password: string | null;
  encryption: DatabaseEncryption;
}

export interface DatabaseTestResult {
  readonly succeeded: boolean;
  readonly elapsedMilliseconds: number;
  readonly serverVersion: string | null;
  readonly schema: string | null;
  readonly message: string;
}

export interface RevealedDatabasePassword {
  readonly password: string;
  readonly hideAfter: string;
}

export interface SetupBody {
  setupKey: string;
  displayName: string;
  email: string;
  password: string;
}
