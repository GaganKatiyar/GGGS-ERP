using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Documents.Application;
using Gggs.Modules.Employee.Application;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Leave.Application;
using Gggs.Modules.MasterData.Infrastructure;
using Gggs.Modules.Notification.Application;
using Gggs.Modules.Organization.Application.StructureImport;
using Gggs.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrgRole = Gggs.Modules.Organization.Domain.Role;
using Role = Gggs.Modules.Identity.Domain.Roles.Role;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only data: lookup lists, the administrator's role topped up to the current template,
/// and a small demo company so the Core HR screens have something to show. Every step is idempotent.
/// </summary>
internal static class DemoData
{
    public static async Task SeedMasterDataAsync(IServiceProvider scope)
    {
        int added = await scope.GetRequiredService<MasterDataSeeder>().SeedCurrentTenantAsync();
        Console.WriteLine($"Master data: {added} value(s) added.");
    }

    /// <summary>Notification templates and the document catalog, as provisioning gives a new tenant.</summary>
    public static async Task SeedPlatformCatalogsAsync(IServiceProvider scope)
    {
        int templates = await scope.GetRequiredService<NotificationTemplateSeeder>().SeedCurrentTenantAsync();
        int documents = await scope.GetRequiredService<DocumentCatalogSeeder>().SeedCurrentTenantAsync();
        Console.WriteLine($"Notification templates: {templates} added. Document types and templates: {documents} added.");
    }

    /// <summary>
    /// Leave types, policies and a default holiday calendar, then this month's accrual - so the
    /// demo employees have balances to apply against. Both steps add nothing when run again.
    /// </summary>
    public static async Task SeedLeaveAsync(IServiceProvider scope)
    {
        int catalog = await scope.GetRequiredService<LeaveCatalogSeeder>().SeedCurrentTenantAsync();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        AccrualResult accrual = await scope.GetRequiredService<LeaveAccrualService>().RunAsync(today.Year, today.Month, CancellationToken.None);
        Console.WriteLine($"Leave catalog: {catalog} added. Accrual {accrual.Period}: {accrual.Accrued} entries.");
    }

    /// <summary>
    /// Development convenience: the administrator signs in as an employee (the HR business partner),
    /// so self-service, My team and the approvals inbox have someone to show. Left alone when the
    /// account is already linked.
    /// </summary>
    public static async Task LinkAdministratorToEmployeeAsync(IServiceProvider scope, string adminEmail)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        User? admin = await scope.GetRequiredService<IUserRepository>().FindByEmailAsync(User.NormalizeEmailForLookup(adminEmail));
        if (admin is null || admin.EmployeeId is not null)
        {
            return;
        }

        Guid? hrbp = await db.Set<Gggs.Modules.Employee.Domain.Employee>()
            .Where(employee => employee.EmployeeCode == "EMP00007")
            .Select(employee => (Guid?)employee.Id)
            .FirstOrDefaultAsync();

        if (hrbp is null)
        {
            return;
        }

        admin.LinkToEmployee(hrbp.Value);
        await scope.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        Console.WriteLine("Administrator linked to employee EMP00007 (Priya Menon, HR).");
    }

    /// <summary>
    /// Tenant roles are seeded as editable copies, so a template that gains permissions in a later
    /// release does not reach an existing tenant. In development that just means "my admin cannot
    /// see the new screens", so the administrator role is topped up here. Never done in production.
    /// </summary>
    public static async Task TopUpAdministratorRoleAsync(IServiceProvider scope)
    {
        IReadOnlyDictionary<string, Role> templates = await scope.GetRequiredService<IRoleRepository>().GetTemplateRolesAsync();

        if (!templates.TryGetValue(RoleTemplates.TenantAdministratorTemplateCode, out Role? role))
        {
            return;
        }

        int before = role.Permissions.Count;
        foreach (PermissionAssignment permission in RoleTemplates.For(RoleTemplates.TenantAdministratorTemplateCode))
        {
            role.Grant(permission.PermissionCode, permission.Scope);
        }

        await scope.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        Console.WriteLine($"Administrator role: {role.Permissions.Count - before} permission(s) added.");
    }

    /// <summary>A small IT services company: one company, three offices, departments and functions, roles, designations, levels and people.</summary>
    public static async Task SeedDemoCompanyAsync(IServiceProvider scope)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();

        if (await db.Set<Company>().AnyAsync())
        {
            Console.WriteLine("Demo company already present.");
            return;
        }

        DateOnly opened = new(2020, 4, 1);

        Company company = Company.Create("ACME-IN", "Acme Technologies India", "IN", "INR");
        company.UpdateDetails("Acme Technologies India", "Acme Technologies India Private Limited", "IN", "INR",
            new PostalAddress("Outer Ring Road", "Marathahalli", "Bengaluru", "IN-KA", "IN", "560037"));
        company.UpdateRegistration("29ABCDE1234F1Z5", "ABCDE1234F", "U72200KA2020PTC000001", "BLRA00001A");
        db.Add(company);

        BusinessUnit delivery = BusinessUnit.Create("DEL", "Delivery", company.Id, null);
        BusinessUnit corporate = BusinessUnit.Create("CORP", "Corporate functions", company.Id, null);
        db.AddRange(delivery, corporate);

        // Cost centers are the company names offices work under: the company's own by default, and
        // the Pune office's sister company there.
        CostCenter acmeCost = CostCenter.DefaultFor(company);
        CostCenter puneCost = CostCenter.Create("ACME-PNQ", "Acme Infotech", company.Id, null);
        puneCost.UpdateRegistration("27ABCDE1234F1Z9", null, new PostalAddress("Hinjewadi Phase 1", null, "Pune", "IN-MH", "IN", "411057"));
        db.AddRange(acmeCost, puneCost);

        Location blr = Location.Create("BLR", "Bengaluru HQ", company.Id, new PostalAddress("Outer Ring Road", null, "Bengaluru", "IN-KA", "IN", "560037"), "Asia/Kolkata");
        Location pune = Location.Create("PNQ", "Pune", company.Id, new PostalAddress("Hinjewadi Phase 1", null, "Pune", "IN-MH", "IN", "411057"), "Asia/Kolkata", puneCost.Id);
        Location remote = Location.Create("REMOTE", "Remote (India)", company.Id, null, "Asia/Kolkata");
        db.AddRange(blr, pune, remote);

        // Three levels; every role is held at L1, L2 or L3.
        db.AddRange(Level.Create("L1", "Associate", 1), Level.Create("L2", "Senior associate", 2), Level.Create("L3", "Lead", 3));
        await db.SaveChangesAsync();

        // The company's departments, functions and roles come from the same file the import screen
        // takes (tools/DevSeed/org-structure.csv); each role gets a designation per level.
        string file = Path.Combine(AppContext.BaseDirectory, "org-structure.csv");
        StructureParseResult parsed = StructureImportParser.Parse(await File.ReadAllTextAsync(file));
        StructurePlan plan = StructureImportPlanner.Plan(parsed.Rows, await StructureImporter.SnapshotAsync(db, CancellationToken.None));
        if (parsed.Errors.Count > 0 || !plan.CanApply)
        {
            throw new InvalidOperationException($"org-structure.csv has problems: {string.Join("; ", parsed.Errors.Concat(plan.Errors).Select(issue => $"line {issue.Line}: {issue.Message}"))}");
        }

        await StructureImporter.ApplyAsync(db, plan, opened, CancellationToken.None);
        await db.SaveChangesAsync();

        // Delivery departments sit in the Delivery business unit, the rest in Corporate; functions follow.
        string[] deliveryCodes = ["D-TS", "D-ITES", "D-QUAL", "D-DOP", "D-MS", "D-WFS"];
        List<Department> departments = await db.Set<Department>().ToListAsync();
        foreach (Department department in departments.Where(row => row.ParentDepartmentId is null))
        {
            Guid unit = deliveryCodes.Contains(department.Code) ? delivery.Id : corporate.Id;
            department.Place(null, unit, null, null, blr.Id);
            foreach (Department function in departments.Where(row => row.ParentDepartmentId == department.Id))
            {
                function.Place(department.Id, unit, null, null, blr.Id);
            }
        }

        await db.SaveChangesAsync();

        Dictionary<string, Department> byCode = departments.ToDictionary(row => row.Code);
        Dictionary<string, OrgRole> roles = await db.Set<OrgRole>().ToDictionaryAsync(row => row.Code);
        Dictionary<string, Designation> titles = await db.Set<Designation>().ToDictionaryAsync(row => row.Code);

        EmployeeService employees = scope.GetRequiredService<EmployeeService>();

        // Each person: the function they sit in, their role and level - the designation is the pair.
        // The cost center follows the office: Pune people work under Acme Infotech.
        async Task<Guid> Hire(string code, string first, string last, string email, DateOnly joined, string function, string role, string level, Location office, Guid? manager, string mode = "OFFICE")
        {
            Department department = byCode[function];
            Designation title = titles[$"{role}-{level}"];
            var hired = await employees.HireAsync(new HireEmployeeRequest(
                code, first, null, last, null, null, null, null, email, null, joined, joined.AddMonths(6),
                new AssignmentInput(department.Id, roles[role].Id, title.LevelId, title.Id, null, office.Id, company.Id, department.BusinessUnitId, manager, null, "FULL_TIME", mode, null)));
            return hired.Id;
        }

        Guid anita = await Hire("EMP00001", "Anita", "Rao", "anita.rao@acme.example", new DateOnly(2020, 4, 1), "MGMT", "MGMT-EVP", "L3", blr, null);
        Guid vikram = await Hire("EMP00002", "Vikram", "Shah", "vikram.shah@acme.example", new DateOnly(2021, 1, 11), "CNI", "TS-CIH", "L3", blr, anita);
        await Hire("EMP00003", "Meera", "Iyer", "meera.iyer@acme.example", new DateOnly(2021, 6, 7), "ENG", "TS-AIE", "L2", blr, vikram);
        await Hire("EMP00004", "Arjun", "Nair", "arjun.nair@acme.example", new DateOnly(2023, 2, 1), "ENG", "TS-AIE", "L1", remote, vikram, "REMOTE");
        await Hire("EMP00005", "Sara", "Khan", "sara.khan@acme.example", new DateOnly(2024, 7, 15), "ENG", "TS-AIE", "L1", blr, vikram, "HYBRID");
        await Hire("EMP00006", "Rohan", "Das", "rohan.das@acme.example", new DateOnly(2022, 9, 5), "QA", "TS-QAE", "L2", pune, vikram);
        await Hire("EMP00007", "Priya", "Menon", "priya.menon@acme.example", new DateOnly(2021, 3, 1), "GH", "HR-PP", "L3", blr, anita);

        Console.WriteLine($"Demo company created: 1 company, {plan.Departments.Count} departments, {plan.Functions.Count} functions, {plan.Roles.Count} roles, 7 employees.");
    }
}
