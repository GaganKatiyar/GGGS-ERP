using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence.Interceptors;
using Gggs.BuildingBlocks.Security;
using Gggs.Identity.Domain.Roles;
using Gggs.Identity.Domain.Users;
using Gggs.Identity.Infrastructure;
using Gggs.Platform.Domain.Tenants;
using Gggs.Platform.Infrastructure;
using Gggs.Workflow.Domain.Workflows;
using Gggs.Workflow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

// Provisions one demo tenant and one working tenant-admin account, so a developer can sign in
// through the Angular app without standing up RabbitMQ or Identity.Worker. See the csproj summary
// for why: the async provisioning saga is not needed to hand someone a working login.
//
//   dotnet run --project tools/DevSeed -- [tenantCode] [adminEmail] [adminPassword]

string tenantCode = args.Length > 0 ? args[0] : "acme";
string adminEmail = args.Length > 1 ? args[1] : $"admin@{tenantCode}.example";
string adminPassword = args.Length > 2 ? args[2] : "Passw0rd!2026";

string platformConnectionString = Environment.GetEnvironmentVariable("GGGS_PLATFORM_DB")
    ?? "Server=localhost;Database=gggs_platform;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";
string identityConnectionString = Environment.GetEnvironmentVariable("GGGS_IDENTITY_DB")
    ?? "Server=localhost;Database=gggs_identity;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";
string workflowConnectionString = Environment.GetEnvironmentVariable("GGGS_WORKFLOW_DB")
    ?? "Server=localhost;Database=gggs_workflow;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

Console.WriteLine($"Platform DB: {platformConnectionString}");
Console.WriteLine($"Identity DB: {identityConnectionString}");
Console.WriteLine($"Workflow DB: {workflowConnectionString}");

Guid tenantId = await SeedTenantAsync(platformConnectionString, tenantCode);
await SeedTenantAdminAsync(identityConnectionString, tenantId, adminEmail, adminPassword);
await SeedSingleStepWorkflowAsync(workflowConnectionString, tenantId, "LEAVE_REQUEST", "LEAVE_REQUEST", "Leave request approval");
await SeedSingleStepWorkflowAsync(
    workflowConnectionString, tenantId, "ATTENDANCE_REGULARIZATION", "ATTENDANCE_REGULARIZATION", "Attendance regularization approval");

Console.WriteLine();
Console.WriteLine("Done. Sign in with:");
Console.WriteLine($"  Organisation code: {tenantCode}");
Console.WriteLine($"  Work email:        {adminEmail}");
Console.WriteLine($"  Password:          {adminPassword}");

static async Task<Guid> SeedTenantAsync(string connectionString, string tenantCode)
{
    var tenantContext = new TenantContext();
    tenantContext.BeginPlatformScope("dev-seed");

    DbContextOptions<PlatformDbContext> options = new DbContextOptionsBuilder<PlatformDbContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using var db = new PlatformDbContext(options, tenantContext);
    await db.Database.MigrateAsync();

    Tenant? existing = await db.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode);
    if (existing is not null)
    {
        Console.WriteLine($"Tenant '{tenantCode}' already exists ({existing.Id}), status {existing.Status}.");
        return existing.Id;
    }

    Tenant tenant = Tenant.Register(
        tenantCode,
        "Acme Manufacturing (dev seed)",
        countryCode: "IN",
        dataRegion: "centralindia",
        defaultTimeZone: "Asia/Kolkata",
        defaultCurrency: "INR",
        defaultLocale: "en-IN",
        TenantIsolationTier.Pooled,
        DateTime.UtcNow);

    // Never born Active (see Tenant.Register's own comment) - this is the seed tool standing in
    // for every provisioning step the real saga would otherwise run.
    tenant.CompleteProvisioning(DateTime.UtcNow);

    db.Tenants.Add(tenant);
    await db.SaveChangesAsync();

    Console.WriteLine($"Tenant '{tenantCode}' created and activated ({tenant.Id}).");
    return tenant.Id;
}

static async Task SeedTenantAdminAsync(string connectionString, Guid tenantId, string adminEmail, string adminPassword)
{
    // Migrations run under platform scope, matching IdentityDbContextFactory's design-time
    // behaviour: they must see and alter every tenant's tables. The actual seed rows are
    // tenant-owned, so they are written through a second context scoped to this one tenant -
    // exactly the distinction TenantAuditSaveChangesInterceptor enforces at runtime.
    var migrationTenantContext = new TenantContext();
    migrationTenantContext.BeginPlatformScope("dev-seed");

    DbContextOptions<IdentityDbContext> migrationOptions = new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using (var migrationDb = new IdentityDbContext(migrationOptions, migrationTenantContext))
    {
        await migrationDb.Database.MigrateAsync();
    }

    var tenantScopedContext = new TenantContext();
    tenantScopedContext.SetTenant(tenantId);

    // Two interceptors, matching what every running service registers on its DbContext, for the
    // two halves of tenant isolation this write has to satisfy:
    //  - TenantSessionContextInterceptor sets SESSION_CONTEXT so SQL Server's RLS predicate
    //    admits the write at all;
    //  - TenantAuditSaveChangesInterceptor stamps TenantId on the new rows themselves. Without it
    //    they would carry Guid.Empty (nothing else sets it - that is infrastructure's job, not the
    //    domain's), and the RLS predicate would then reject them anyway even with a correct
    //    session context, since the row's own TenantId would never match it.
    DbContextOptions<IdentityDbContext> writeOptions = new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(connectionString)
        .AddInterceptors(
            new TenantSessionContextInterceptor(tenantScopedContext),
            new TenantAuditSaveChangesInterceptor(
                tenantScopedContext,
                new SeedActor(),
                new GggsPlatformOptions { ServiceName = "identity" },
                TimeProvider.System))
        .Options;

    await using var db = new IdentityDbContext(writeOptions, tenantScopedContext);

    // A generous grant, not a minimal one: the point of this account is to see the whole app
    // shell working, not to exercise least-privilege scenarios. Re-applied below even for an
    // account that already exists - Role.Grant replaces rather than duplicates a code it already
    // holds, so this list is the account's actual permission set after every run, not just its
    // set at first creation. Without that, a permission added here after someone's first seed
    // would silently never reach their already-seeded account.
    string[] grantedPermissions =
    [
        Permissions.Admin.UserView,
        Permissions.Admin.UserManage,
        Permissions.Admin.UserInvite,
        Permissions.Admin.UserLock,
        Permissions.Admin.RoleView,
        Permissions.Admin.RoleManage,
        Permissions.Admin.SettingView,
        Permissions.Admin.SettingManage,
        Permissions.Organization.View,
        Permissions.Organization.Manage,
        Permissions.Organization.LegalEntityManage,
        Permissions.Organization.DepartmentManage,
        Permissions.Employee.View,
        Permissions.Employee.Create,
        Permissions.Employee.Edit,
        Permissions.Employee.EmploymentManage,
        Permissions.Employee.HistoryView,
        Permissions.Employee.ExitManage,
        Permissions.Document.View,
        Permissions.Document.Upload,
        Permissions.Document.Delete,
        Permissions.Document.TypeManage,
        Permissions.Audit.LogView,
        Permissions.Notification.InboxView,
        Permissions.Notification.InboxManage,
        Permissions.Notification.TemplateView,
        Permissions.Notification.TemplateManage,
        Permissions.Workflow.TaskView,
        Permissions.Workflow.TaskAct,
        Permissions.Workflow.TaskReassign,
        Permissions.Workflow.DefinitionView,
        Permissions.Workflow.DefinitionManage,
        Permissions.Workflow.DelegationManage,
        Permissions.Workflow.InstanceViewAll,
        Permissions.Leave.TypeManage,
        Permissions.Leave.PolicyManage,
        Permissions.Leave.BalanceView,
        Permissions.Leave.BalanceAdjust,
        Permissions.Leave.RequestView,
        Permissions.Leave.RequestCreate,
        Permissions.Leave.RequestCreateOnBehalf,
        Permissions.Leave.RequestCancel,
        Permissions.Leave.HolidayManage,
        Permissions.Self.LeaveView,
        Permissions.Self.LeaveApply,
        Permissions.Attendance.View,
        Permissions.Attendance.Manage,
        Permissions.Attendance.RegularizationView,
        Permissions.Attendance.RegularizationCreate,
        Permissions.Attendance.ShiftManage,
        Permissions.Attendance.RosterManage,
        Permissions.Attendance.PolicyManage,
        Permissions.Attendance.DeviceManage,
        Permissions.Self.AttendanceView,
        Permissions.Self.AttendancePunch,
        Permissions.Report.View,
        Permissions.Report.Export,
        Permissions.Report.ScheduleManage,
        Permissions.Recruitment.RequisitionView,
        Permissions.Recruitment.RequisitionManage,
        Permissions.Recruitment.PostingView,
        Permissions.Recruitment.PostingManage,
        Permissions.Recruitment.ApplicationView,
        Permissions.Recruitment.ApplicationManage,
        Permissions.Recruitment.OfferView,
        Permissions.Recruitment.OfferManage,
        Permissions.Onboarding.TemplateView,
        Permissions.Onboarding.TemplateManage,
        Permissions.Onboarding.CaseView,
        Permissions.Onboarding.CaseManage,
        Permissions.Onboarding.TaskComplete,
    ];

    string normalizedEmail = adminEmail.Trim().ToUpperInvariant();
    User? existingUser = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
    if (existingUser is not null)
    {
        Guid? existingRoleId = await db.UserRoles
            .Where(userRole => userRole.UserId == existingUser.Id)
            .Select(userRole => (Guid?)userRole.RoleId)
            .FirstOrDefaultAsync();

        Role? existingRole = existingRoleId is { } roleId
            ? await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId)
            : null;

        if (existingRole is null)
        {
            Console.WriteLine($"User '{adminEmail}' already exists ({existingUser.Id}) with no role to reconcile - leaving it as is.");
            return;
        }

        foreach (string permission in grantedPermissions)
        {
            existingRole.Grant(permission, DataScope.Tenant);
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"User '{adminEmail}' already exists ({existingUser.Id}) - its role's permissions were reconciled to the current list.");
        return;
    }

    Role role = Role.Create(
        "Tenant Administrator",
        "Full administrative control, seeded for local development.",
        isSystem: true,
        templateCode: "TENANT_ADMIN");

    foreach (string permission in grantedPermissions)
    {
        role.Grant(permission, DataScope.Tenant);
    }

    var hasher = new Pbkdf2PasswordHasher(NullLogger<Pbkdf2PasswordHasher>.Instance);

    User admin = User.Invite(adminEmail, "Dev Administrator", UserType.TenantUser, employeeId: null, DateTime.UtcNow);
    admin.CompleteInvitation(hasher.Hash(adminPassword), DateTime.UtcNow);

    db.Roles.Add(role);
    db.Users.Add(admin);
    db.UserRoles.Add(UserRole.Grant(admin.Id, role.Id, DateOnly.FromDateTime(DateTime.UtcNow)));

    await db.SaveChangesAsync();

    Console.WriteLine($"Tenant admin '{adminEmail}' created ({admin.Id}) with role '{role.Name}'.");
}

/// <summary>
/// Creates and publishes the simplest possible approval chain - the reporting manager at level 1,
/// matching the architecture doc's own simplest example (§13.2) - for a workflow definition code
/// this service's request-and-approve modules assume already exists (Leave, and now Attendance's
/// regularization requests). Richer step configuration (department head, HR review, conditions) is
/// deferred until a tenant actually needs it. Idempotent: skips if the code is already seeded.
/// </summary>
static async Task SeedSingleStepWorkflowAsync(string connectionString, Guid tenantId, string definitionCode, string entityType, string name)
{
    var migrationTenantContext = new TenantContext();
    migrationTenantContext.BeginPlatformScope("dev-seed");

    DbContextOptions<WorkflowDbContext> migrationOptions = new DbContextOptionsBuilder<WorkflowDbContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using (var migrationDb = new WorkflowDbContext(migrationOptions, migrationTenantContext))
    {
        await migrationDb.Database.MigrateAsync();
    }

    var tenantScopedContext = new TenantContext();
    tenantScopedContext.SetTenant(tenantId);

    DbContextOptions<WorkflowDbContext> writeOptions = new DbContextOptionsBuilder<WorkflowDbContext>()
        .UseSqlServer(connectionString)
        .AddInterceptors(
            new TenantSessionContextInterceptor(tenantScopedContext),
            new TenantAuditSaveChangesInterceptor(
                tenantScopedContext,
                new SeedActor(),
                new GggsPlatformOptions { ServiceName = "workflow" },
                TimeProvider.System))
        .Options;

    await using var db = new WorkflowDbContext(writeOptions, tenantScopedContext);

    bool alreadyExists = await db.Definitions.AnyAsync(d => d.Code == definitionCode);
    if (alreadyExists)
    {
        Console.WriteLine($"Workflow definition '{definitionCode}' already exists - leaving it as is.");
        return;
    }

    const string DefinitionJson = """
        {
          "steps": [
            {
              "key": "manager",
              "name": "Reporting manager",
              "approvers": { "type": "ReportingManager", "level": 1 },
              "mode": "Any"
            }
          ]
        }
        """;

    WorkflowDefinition definition = WorkflowDefinition.Create(definitionCode, name, entityType);
    WorkflowDefinitionVersion version = WorkflowDefinitionVersion.CreateDraft(definition.Id, version: 1, DefinitionJson);
    version.Publish(DateTime.UtcNow);

    db.Definitions.Add(definition);
    db.DefinitionVersions.Add(version);

    await db.SaveChangesAsync();

    Console.WriteLine($"Workflow definition '{definitionCode}' created and published ({definition.Id}).");
}

/// <summary>
/// No human is behind this write. IsAuthenticated is true here specifically so
/// <see cref="TenantAuditSaveChangesInterceptor"/> stamps <see cref="SystemUsers.Migration"/> as
/// CreatedBy rather than the generic <see cref="SystemUsers.System"/> it falls back to when
/// IsAuthenticated is false - this really is seed data, not a background job.
/// </summary>
file sealed class SeedActor : ICurrentUser
{
    public Guid UserId => SystemUsers.Migration;

    public Guid? EmployeeId => null;

    public Guid? SessionId => null;

    public bool IsAuthenticated => true;

    public Guid? ImpersonatorUserId => null;

    public bool IsPlatformStaff => false;

    public string? IpAddress => null;

    public string? UserAgent => null;

    public string? CorrelationId => null;
}
