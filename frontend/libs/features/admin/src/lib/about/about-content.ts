/**
 * The fixed text of the About Nuts and Bolts page: what the platform is built with and why.
 *
 * Versions are major.minor from frontend/package.json and Directory.Packages.props - update them
 * here when a framework moves a minor version. Everything that can change at run time (build,
 * environment, modules, database) comes from `/api/v1/about` instead.
 */

export interface StackItem {
  /** Two letters for the mark beside the name. */
  readonly mark: string;
  readonly name: string;
  readonly version?: string;
  readonly note: string;
}

export interface StackGroup {
  readonly title: string;
  readonly subtitle: string;
  readonly items: readonly StackItem[];
}

export interface Row {
  readonly cells: readonly string[];
}

export const MODULE_RULES: readonly string[] = [
  'A module references the building blocks and other modules’ .Contracts projects, nothing else.',
  'Reads across modules go through a contract interface such as IEmployeeDirectory. No module copies another module’s data.',
  'Side effects across modules are integration events. They are written to the outbox in the same transaction and the Worker delivers them.',
  'Every module implements IModule (AddServices, MapEndpoints) and both hosts discover it automatically.',
  'Every tenant-owned table is covered by the SQL row-level security policy, which is generated from the EF model.',
];

export const MODULE_LAYOUT: readonly Row[] = [
  { cells: ['Domain/', 'Entities, value objects, business rules'] },
  { cells: ['Application/', 'Services, queries, validators (FluentValidation)'] },
  { cells: ['Infrastructure/', 'EF configurations, contract implementations'] },
  { cells: ['Endpoints/', 'Minimal API routes under /api/v1'] },
];

export const FRONTEND: readonly StackGroup[] = [
  {
    title: 'Framework and UI',
    subtitle: 'What the screens are built with.',
    items: [
      { mark: 'NG', name: 'Angular', note: 'Standalone components, signals and the new control flow. No NgModules. Every feature is lazy-loaded.' },
      { mark: 'TS', name: 'TypeScript', version: '5.9', note: 'Strict typing across the whole workspace.' },
      { mark: 'P', name: 'PrimeNG + PrimeIcons', version: '21.1', note: 'Tables, dialogs, calendars and trees. Features use it only through the shared/ui wrappers.' },
      { mark: 'C', name: 'Chart.js', version: '4.5', note: 'Charts on dashboards and analytics, themed with the design tokens.' },
      { mark: 'Rx', name: 'RxJS', version: '7.8', note: 'HTTP calls and event streams. Screen state is held in signals.' },
    ],
  },
  {
    title: 'Workspace and design system',
    subtitle: 'How the front-end code is organised.',
    items: [
      { mark: 'Nx', name: 'Nx monorepo', version: '23.2', note: 'One app (hrms-web) and libraries split into core / shared / features. Lint rules enforce the boundaries, and builds run only for what changed.' },
      { mark: 'V', name: 'Angular build (esbuild) + Vite', version: '8', note: 'Fast dev server and production bundles, with a budget on the initial bundle.' },
      { mark: 'S', name: 'SCSS design tokens', note: '--gggs-* tokens for colour, elevation, glass and motion. Light and dark mode, and switchable brand palettes.' },
      { mark: 'Aa', name: 'Plus Jakarta Sans + Inter', note: 'Self-hosted fonts: Plus Jakarta Sans for headings, Inter for body text.' },
    ],
  },
];

export const FRONTEND_LIBRARIES: readonly Row[] = [
  { cells: ['core/*', 'auth, http, layout (the shell), permissions, tenant'] },
  { cells: ['shared/*', 'ui, data-table, models, money, workflow and other shared pieces'] },
  { cells: ['features/*', 'One lazy-loaded library per area: employees, payroll, time, projects…'] },
];

export const BACKEND: StackGroup = {
  title: 'Runtime and web',
  subtitle: 'The server side.',
  items: [
    { mark: '.N', name: '.NET 10 (LTS) / C#', note: 'ASP.NET Core minimal APIs, versioned with Asp.Versioning, with OpenAPI documents.' },
    { mark: 'EF', name: 'Entity Framework Core 10 + Dapper', note: 'EF for writes and most reads, Dapper for heavy report queries. One migration history.' },
    { mark: 'FV', name: 'FluentValidation', version: '12.1', note: 'Request validation rules next to each use case.' },
    { mark: 'HF', name: 'Hangfire', version: '1.8', note: 'Scheduled and background jobs in the Worker, stored in SQL Server.' },
    { mark: 'AI', name: 'Anthropic Claude API', note: 'The AI assistants of the Ai module.' },
  ],
};

export const BUILDING_BLOCKS: readonly Row[] = [
  { cells: ['MultiTenancy', 'Tenant context from the validated token only'] },
  { cells: ['Security', 'Permission catalogue, policy handlers, step-up'] },
  { cells: ['Persistence', 'Base DbContext, tenant stamping, audit'] },
  { cells: ['Messaging', 'Outbox and inbox; in-process, RabbitMQ or Service Bus transports'] },
  { cells: ['Caching', 'HybridCache over Redis'] },
  { cells: ['Files', 'File system or Azure Blob storage, keyed by tenant'] },
  { cells: ['Jobs', 'Hangfire wiring'] },
  { cells: ['Secrets', 'Azure Key Vault or local configuration'] },
  { cells: ['Observability', 'Serilog logs, OpenTelemetry traces and metrics'] },
  { cells: ['Web', 'Problem details, rate limiting, versioning, modules'] },
  { cells: ['Domain / Application', 'Base types, results, handler interfaces'] },
];

export const ISOLATION_LAYERS: readonly { readonly title: string; readonly note: string }[] = [
  { title: 'Token', note: 'The tenant comes only from the validated token. Any tenant ID in the URL or body is ignored.' },
  { title: 'EF Core global filter', note: 'Every query on a tenant-owned table is filtered automatically.' },
  { title: 'SQL Server row-level security', note: 'The database itself refuses other tenants’ rows.' },
  { title: 'Composite same-tenant foreign keys', note: 'A row can only point at rows of the same tenant.' },
];

export const ACCESS: readonly Row[] = [
  { cells: ['Identity server', 'OpenIddict (OpenID Connect) inside the Identity module'] },
  { cells: ['Browser session', 'BFF pattern: an httpOnly cookie, tokens kept on the server'] },
  { cells: ['Single sign-on', 'Federation to Entra ID and other OIDC providers'] },
  { cells: ['Permissions', 'Fine-grained permission catalogue, roles, automatic Manager and Department Head roles'] },
  { cells: ['Step-up', 'Re-enter the password before sensitive actions'] },
  { cells: ['Rate limiting', 'Limits per user and per address on sensitive endpoints'] },
  { cells: ['Keys', 'Sequential GUIDs, clustered on (TenantId, Id)'] },
  { cells: ['Compliance packs', 'Versioned, effective-dated country rules; India first (PF, ESI, PT, TDS)'] },
];

export const OPERATIONS: readonly StackGroup[] = [
  {
    title: 'Deployment',
    subtitle: 'Two supported profiles, the same code.',
    items: [
      { mark: 'Az', name: 'Azure (SaaS)', note: 'Container Apps, Azure SQL, Service Bus, Blob storage and Key Vault, provisioned with Bicep.' },
      { mark: 'On', name: 'On-premise', note: 'Docker Compose or IIS, SQL Server, RabbitMQ and local file storage, installed with install.ps1.' },
    ],
  },
  {
    title: 'Observability',
    subtitle: 'What happens at run time.',
    items: [
      { mark: 'Se', name: 'Serilog', version: '10.0', note: 'Structured logs enriched with tenant and request.' },
      { mark: 'OT', name: 'OpenTelemetry', version: '1.19', note: 'Traces and metrics for HTTP, SQL and the runtime, exported over OTLP.' },
      { mark: 'HC', name: 'Health checks', note: 'Liveness and readiness probes for load balancers.' },
    ],
  },
];

export const TESTING: readonly Row[] = [
  { cells: ['Unit', 'xUnit v3, Shouldly, NSubstitute'] },
  { cells: ['Integration', 'Testcontainers (SQL Server), WebApplicationFactory'] },
  { cells: ['Architecture', 'NetArchTest: the module rules'] },
  { cells: ['Tenant isolation', 'Cross-tenant leak tests'] },
  { cells: ['Front-end unit', 'Vitest'] },
  { cells: ['End to end', 'Playwright with axe-core accessibility checks'] },
  { cells: ['Lint', 'ESLint, Stylelint, Prettier'] },
];

export const DECISIONS: readonly Row[] = [
  { cells: ['01', 'Pooled shared database with TenantId and row-level security by default; a dedicated database for enterprise tenants', 'Schema per tenant'] },
  { cells: ['06', 'Transactional outbox and inbox for events', 'Kafka'] },
  { cells: ['08', 'Sequential GUID keys, clustered on (TenantId, Id)', 'bigint IDENTITY'] },
  { cells: ['10', 'Nx, standalone Angular and PrimeNG behind wrappers', 'Angular Material + AG Grid'] },
  { cells: ['11', 'Dual deployment: Azure and on-premise', 'Azure only'] },
  { cells: ['12', 'One modular monolith, one database', '11 microservices, 11 databases'] },
  { cells: ['13', 'No two-step sign-in; password step-up before sensitive actions', 'Authenticator app (TOTP)'] },
];
