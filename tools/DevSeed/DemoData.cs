using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Application;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.MasterData.Infrastructure;
using Gggs.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>A small IT services company: one entity, three locations, a department tree, jobs, positions and people.</summary>
    public static async Task SeedDemoCompanyAsync(IServiceProvider scope)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();

        if (await db.Set<LegalEntity>().AnyAsync())
        {
            Console.WriteLine("Demo company already present.");
            return;
        }

        DateOnly opened = new(2020, 4, 1);

        LegalEntity company = LegalEntity.Create("ACME-IN", "Acme Technologies India", "IN", "INR");
        company.UpdateDetails("Acme Technologies India", "Acme Technologies India Private Limited", "IN", "INR",
            new PostalAddress("Outer Ring Road", "Marathahalli", "Bengaluru", "IN-KA", "IN", "560037"));
        company.UpdateRegistration("29ABCDE1234F1Z5", "ABCDE1234F", "U72200KA2020PTC000001", "BLRA00001A");
        db.Add(company);

        BusinessUnit delivery = BusinessUnit.Create("DEL", "Delivery", company.Id, null);
        BusinessUnit corporate = BusinessUnit.Create("CORP", "Corporate functions", company.Id, null);
        db.AddRange(delivery, corporate);

        Location blr = Location.Create("BLR", "Bengaluru HQ", company.Id, new PostalAddress("Outer Ring Road", null, "Bengaluru", "IN-KA", "IN", "560037"), "Asia/Kolkata");
        Location pune = Location.Create("PNQ", "Pune", company.Id, new PostalAddress("Hinjewadi Phase 1", null, "Pune", "IN-MH", "IN", "411057"), "Asia/Kolkata");
        Location remote = Location.Create("REMOTE", "Remote (India)", company.Id, null, "Asia/Kolkata");
        db.AddRange(blr, pune, remote);

        CostCenter engCost = CostCenter.Create("CC-ENG", "Engineering", null);
        CostCenter hrCost = CostCenter.Create("CC-HR", "Human resources", null);
        db.AddRange(engCost, hrCost);

        string[] gradeNames = ["Associate", "Senior associate", "Lead", "Manager", "Senior manager", "Director"];
        Grade[] grades = [.. gradeNames.Select((name, index) => Grade.Create($"G{index + 1}", name, index + 1))];
        db.AddRange(grades);

        JobLevel[] levels = [.. Enumerable.Range(1, 6).Select(level => JobLevel.Create($"L{level}", $"Level {level}", level))];
        db.AddRange(levels);

        Department engineering = Department.Create("ENG", "Engineering", opened);
        Department platform = Department.Create("ENG-PLAT", "Platform engineering", opened);
        Department quality = Department.Create("ENG-QA", "Quality engineering", opened);
        Department people = Department.Create("HR", "People team", opened);
        Department finance = Department.Create("FIN", "Finance", opened);
        engineering.Place(null, null, null, engCost.Id, null, blr.Id);
        platform.Place(engineering.Id, null, null, engCost.Id, null, blr.Id);
        quality.Place(engineering.Id, null, null, engCost.Id, null, pune.Id);
        people.Place(null, null, null, hrCost.Id, null, blr.Id);
        finance.Place(null, null, null, null, null, blr.Id);
        db.AddRange(engineering, platform, quality, people, finance);

        Job CreateJob(string code, string title, string family, int level, Department department, params string[] skills)
        {
            Job job = Job.Create(code, title);
            job.Define(family, levels[level - 1].Id, department.Id, level - 1, level + 2, null, skills);
            db.Add(job);
            return job;
        }

        Job cto = CreateJob("CTO", "Chief technology officer", "LEADERSHIP", 6, engineering, "Architecture", "Leadership");
        Job engManager = CreateJob("ENG-MGR", "Engineering manager", "ENGINEERING", 4, platform, ".NET", "Azure", "People management");
        Job seniorDev = CreateJob("SR-DEV", "Senior software engineer", "ENGINEERING", 3, platform, ".NET", "Angular", "SQL Server");
        Job dev = CreateJob("DEV", "Software engineer", "ENGINEERING", 2, platform, ".NET", "Angular");
        Job qa = CreateJob("QA", "QA engineer", "QUALITY", 2, quality, "Test automation", "Playwright");
        Job hrbp = CreateJob("HRBP", "HR business partner", "HR", 4, people, "Employee relations");

        Position CreateSeat(string code, string title, Job job, Department department, Location location, Grade grade, Position? reportsTo)
        {
            Position position = Position.Create(code, title, job.Id, department.Id, opened);
            position.Configure(job.Id, department.Id, location.Id, grade.Id, reportsTo?.Id, isBudgeted: true, headcount: 1);
            db.Add(position);
            return position;
        }

        Position ctoSeat = CreateSeat("P-CTO", "CTO", cto, engineering, blr, grades[5], null);
        Position mgrSeat = CreateSeat("P-PLAT-MGR", "Platform engineering manager", engManager, platform, blr, grades[3], ctoSeat);
        Position srSeat = CreateSeat("P-PLAT-SR1", "Senior engineer, platform", seniorDev, platform, blr, grades[2], mgrSeat);
        Position devSeat1 = CreateSeat("P-PLAT-DEV1", "Engineer, platform", dev, platform, remote, grades[1], mgrSeat);
        Position devSeat2 = CreateSeat("P-PLAT-DEV2", "Engineer, platform", dev, platform, blr, grades[1], mgrSeat);
        CreateSeat("P-PLAT-DEV3", "Engineer, platform (open)", dev, platform, pune, grades[1], mgrSeat);
        Position qaSeat = CreateSeat("P-QA1", "QA engineer", qa, quality, pune, grades[1], mgrSeat);
        Position hrSeat = CreateSeat("P-HRBP", "HR business partner", hrbp, people, blr, grades[3], null);

        await db.SaveChangesAsync();

        EmployeeService employees = scope.GetRequiredService<EmployeeService>();

        async Task<Guid> Hire(string code, string first, string last, string email, DateOnly joined, Position seat, Guid? manager, string mode = "OFFICE")
        {
            var hired = await employees.HireAsync(new HireEmployeeRequest(
                code, first, null, last, null, null, null, null, email, null, joined, joined.AddMonths(6),
                new AssignmentInput(null, seat.Id, null, null, null, company.Id, delivery.Id, null, manager, null, "FULL_TIME", mode, seat.Name)));
            return hired.Id;
        }

        Guid anita = await Hire("EMP00001", "Anita", "Rao", "anita.rao@acme.example", new DateOnly(2020, 4, 1), ctoSeat, null);
        Guid vikram = await Hire("EMP00002", "Vikram", "Shah", "vikram.shah@acme.example", new DateOnly(2021, 1, 11), mgrSeat, anita);
        await Hire("EMP00003", "Meera", "Iyer", "meera.iyer@acme.example", new DateOnly(2021, 6, 7), srSeat, vikram);
        await Hire("EMP00004", "Arjun", "Nair", "arjun.nair@acme.example", new DateOnly(2023, 2, 1), devSeat1, vikram, "REMOTE");
        await Hire("EMP00005", "Sara", "Khan", "sara.khan@acme.example", new DateOnly(2024, 7, 15), devSeat2, vikram, "HYBRID");
        await Hire("EMP00006", "Rohan", "Das", "rohan.das@acme.example", new DateOnly(2022, 9, 5), qaSeat, vikram);
        await Hire("EMP00007", "Priya", "Menon", "priya.menon@acme.example", new DateOnly(2021, 3, 1), hrSeat, anita);

        Console.WriteLine("Demo company created: 1 company, 5 departments, 8 positions, 7 employees.");
    }
}
