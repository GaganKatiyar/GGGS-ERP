using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Benefits.Application;
using Gggs.Modules.Benefits.Domain;
using Gggs.Modules.Compensation.Domain;
using Gggs.Modules.Compliance.Application;
using Gggs.Modules.Compliance.Domain;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Organization.Domain;
using Gggs.Modules.Payroll.Application;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Payroll.Domain;
using Gggs.Modules.ProjectFinancials.Application;
using Gggs.Modules.ProjectFinancials.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only money data (M7): India's statutory rules and filing calendar, payroll set-up,
/// salaries, statutory and bank details for the demo people, benefit enrollments, salary bands, last
/// month's payroll finalized with payslips, and NWCOM's budget, a vendor cost and an invoice.
/// </summary>
internal static class MoneyDemo
{
    private static readonly Dictionary<string, decimal> Ctcs = new()
    {
        ["EMP00001"] = 4_800_000m,
        ["EMP00002"] = 3_000_000m,
        ["EMP00003"] = 2_400_000m,
        ["EMP00004"] = 1_100_000m,
        ["EMP00005"] = 1_500_000m,
        ["EMP00006"] = 540_000m,
        ["EMP00007"] = 1_800_000m,
    };

    public static async Task SeedAsync(IServiceProvider scope, string adminEmail)
    {
        int rules = await scope.GetRequiredService<ComplianceSeeder>().SeedCurrentTenantAsync();
        int payroll = await scope.GetRequiredService<PayrollSeeder>().SeedCurrentTenantAsync();
        int plans = await scope.GetRequiredService<BenefitSeeder>().SeedCurrentTenantAsync();
        Console.WriteLine($"Money catalogs: {rules} compliance, {payroll} payroll and {plans} benefit item(s) added.");

        await GrantPayrollRoleAsync(scope, adminEmail);

        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        List<EmployeeEntity> people = await db.Set<EmployeeEntity>().Where(employee => employee.ExitDate == null).OrderBy(employee => employee.EmployeeCode).ToListAsync();
        if (people.Count == 0)
        {
            Console.WriteLine("No demo people; skipping demo payroll.");
            return;
        }

        // Statutory and bank details, as HR would have recorded them on joining.
        ISensitiveDataProtector protector = scope.GetRequiredService<ISensitiveDataProtector>();
        int index = 0;
        foreach (EmployeeEntity person in people)
        {
            index++;
            if (person.Pan is not null)
            {
                continue;
            }

            person.SetStatutory(
                protector.Protect(SensitiveDataPurposes.Pan, $"ABCPE{index:0000}K"),
                protector.Protect(SensitiveDataPurposes.Uan, $"1001{index:00000000}"),
                null,
                null,
                "IN-KA");
            person.SetBank("HDFC Bank", "HDFC0001234", person.DisplayName, protector.Protect(SensitiveDataPurposes.BankAccount, $"5010012{index:0000000}"));
        }

        // Group cover for everyone, from the day they joined.
        List<BenefitPlan> automatic = await db.Set<BenefitPlan>().Where(plan => plan.AutoEnroll && plan.IsActive).ToListAsync();
        HashSet<Guid> enrolled = [.. await db.Set<BenefitEnrollment>().Select(row => row.EmployeeId).Distinct().ToListAsync()];
        foreach (EmployeeEntity person in people.Where(person => !enrolled.Contains(person.Id)))
        {
            foreach (BenefitPlan plan in automatic)
            {
                db.Add(BenefitEnrollment.Join(person.Id, plan, [], person.JoiningDate, pending: false, "Enrolled automatically on joining."));
            }
        }

        await db.SaveChangesAsync();

        // Salaries from joining, on the standard structure.
        IPayrollSalaries salaries = scope.GetRequiredService<IPayrollSalaries>();
        HashSet<Guid> paid = [.. await db.Set<EmployeeSalary>().Select(row => row.EmployeeId).Distinct().ToListAsync()];
        foreach (EmployeeEntity person in people.Where(person => !paid.Contains(person.Id)))
        {
            decimal ctc = Ctcs.GetValueOrDefault(person.EmployeeCode, 800_000m);
            await salaries.ReviseAsync(new SalaryRevisionRequest(person.Id, ctc, person.JoiningDate, null, "Hire", "DevSeed", null));
        }

        await SeedBandsAsync(db);

        await SeedRunsAsync(scope, db, people.Count);
        await SeedComplianceAsync(scope, db);
        await SeedProjectFinanceAsync(scope, db, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    /// <summary>Last month's payroll, run end to end; this month's opened and calculated.</summary>
    private static async Task SeedRunsAsync(IServiceProvider scope, AppDbContext db, int people)
    {
        if (await db.Set<PayrollRun>().AnyAsync())
        {
            return;
        }

        RunService runs = scope.GetRequiredService<RunService>();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        RunItem last = await runs.CreateAsync(new CreateRunBody(thisMonth.AddMonths(-1), null, null, "Demo payroll"), CancellationToken.None);
        await runs.CalculateAsync(last.Id, CancellationToken.None);
        await runs.SubmitAsync(last.Id, CancellationToken.None);
        RunDetail finalized = await runs.FinalizeAsync(last.Id, CancellationToken.None);
        await runs.MarkPaidAsync(last.Id, new PaidBody(finalized.Run.PayDate, "NEFT-DEMO-001"), CancellationToken.None);
        RunItem current = await runs.CreateAsync(new CreateRunBody(thisMonth, null, null, null), CancellationToken.None);
        await runs.CalculateAsync(current.Id, CancellationToken.None);
        Console.WriteLine($"Payroll: {people} salaries; {finalized.Run.Label} finalized for {finalized.Run.EmployeeCount} people (net {finalized.Run.Net:N0}); {thisMonth:MMMM} calculated.");
    }

    private static async Task SeedComplianceAsync(IServiceProvider scope, AppDbContext db)
    {
        await scope.GetRequiredService<ComplianceCalendar>().GenerateAsync(CancellationToken.None);
        if (await db.Set<ComplianceRegistration>().AnyAsync())
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Add(ComplianceRegistration.Create(new RegistrationDetails(
            RegistrationKind.Registration, "Shops and establishments - Bengaluru", "KA-SE-2020-0412", "Department of Labour, Karnataka", "IN-KA", null,
            new DateOnly(2020, 5, 1), today.AddDays(20), 30, null, "Renew online on the Karnataka labour portal.", null)));
        db.Add(ComplianceRegistration.Create(new RegistrationDetails(
            RegistrationKind.Certificate, "ISO 27001", "IS 765432", "BSI", null, null, new DateOnly(2024, 6, 1), new DateOnly(2027, 5, 31), 60, null, null, null)));
        await db.SaveChangesAsync();
    }

    /// <summary>The administrator also runs payroll in the demo: the sensitive grants sit with the Payroll Administrator role.</summary>
    private static async Task GrantPayrollRoleAsync(IServiceProvider scope, string adminEmail)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        User? admin = await scope.GetRequiredService<IUserRepository>().FindByEmailAsync(User.NormalizeEmailForLookup(adminEmail));
        IReadOnlyDictionary<string, Role> templates = await scope.GetRequiredService<IRoleRepository>().GetTemplateRolesAsync();
        if (admin is null || !templates.TryGetValue("PAYROLL_ADMIN", out Role? role))
        {
            return;
        }

        // Tenant roles are copies made at provisioning, so a tenant created before M7 has a Payroll
        // Administrator role without the payroll grants. Topped up from the template, as the
        // administrator role is - development only.
        int before = role.Permissions.Count;
        foreach (PermissionAssignment permission in RoleTemplates.For("PAYROLL_ADMIN"))
        {
            role.Grant(permission.PermissionCode, permission.Scope);
        }

        bool granted = false;
        if (!await db.Set<UserRole>().AnyAsync(grant => grant.UserId == admin.Id && grant.RoleId == role.Id))
        {
            db.Add(UserRole.Grant(admin.Id, role.Id, DateOnly.FromDateTime(DateTime.UtcNow)));
            granted = true;
        }

        if (granted || role.Permissions.Count != before)
        {
            // A new permission version makes the cached permission set stale at once.
            admin.BumpPermissionVersion();
            await db.SaveChangesAsync();
            Console.WriteLine($"Payroll Administrator role: {role.Permissions.Count - before} permission(s) added{(granted ? "; given to the administrator" : string.Empty)}.");
        }
    }

    private static async Task SeedBandsAsync(AppDbContext db)
    {
        if (await db.Set<SalaryBand>().AnyAsync())
        {
            return;
        }

        foreach (Grade grade in await db.Set<Grade>().OrderBy(grade => grade.Rank).ToListAsync())
        {
            decimal midpoint = 400_000m * grade.Rank + 200_000m;
            db.Add(SalaryBand.Create(grade.Id, "INR", Math.Round(midpoint * 0.75m / 10000) * 10000, midpoint, Math.Round(midpoint * 1.25m / 10000) * 10000, new DateOnly(2025, 4, 1), null));
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedProjectFinanceAsync(IServiceProvider scope, AppDbContext db, DateOnly today)
    {
        ProjectEntity? project = await db.Set<ProjectEntity>().FirstOrDefaultAsync(row => row.Code == "NWCOM");
        if (project is null || await db.Set<ProjectBudgetLine>().AnyAsync(row => row.ProjectId == project.Id))
        {
            return;
        }

        db.Add(ProjectBudgetLine.For(project.Id, CostCategory.Labour, 2_400_000m, "Team of five for six months"));
        db.Add(ProjectBudgetLine.For(project.Id, CostCategory.Infrastructure, 180_000m, "Cloud environments"));
        db.Add(ProjectBudgetLine.For(project.Id, CostCategory.Vendor, 250_000m, "Design agency"));
        db.Add(ProjectCost.Record(project.Id, new CostDetails(CostCategory.Infrastructure, today.AddDays(-20), 32_000m, "Microsoft Azure", "Test and staging environments", "AZ-INV-8812", false)));
        db.Add(ProjectCost.Record(project.Id, new CostDetails(CostCategory.Vendor, today.AddDays(30), 120_000m, "Pixel Studio", "Design system, second phase", null, true)));
        var invoice = ProjectInvoice.Raise(project.Id, $"INV-{today.Year}-0001", new InvoiceDetails(today.AddDays(-10), today.AddDays(20), today.AddDays(-40), today.AddDays(-11), 150_000m, 27_000m, null, "Time and material, first month"));
        invoice.Send();
        db.Add(invoice);
        await db.SaveChangesAsync();

        await scope.GetRequiredService<FinanceService>().SnapshotAsync(CancellationToken.None);
        Console.WriteLine("Project financials: NWCOM budget, costs and an invoice added.");
    }
}
