using System.Reflection;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Ai;
using Gggs.Modules.Assets;
using Gggs.Modules.Attendance;
using Gggs.Modules.Benefits;
using Gggs.Modules.Career;
using Gggs.Modules.Client;
using Gggs.Modules.Compensation;
using Gggs.Modules.Compliance;
using Gggs.Modules.Contract;
using Gggs.Modules.Documents;
using Gggs.Modules.Employee;
using Gggs.Modules.Engagement;
using Gggs.Modules.Expense;
using Gggs.Modules.Helpdesk;
using Gggs.Modules.Identity;
using Gggs.Modules.Insight;
using Gggs.Modules.Learning;
using Gggs.Modules.Leave;
using Gggs.Modules.MasterData;
using Gggs.Modules.Notification;
using Gggs.Modules.Onboarding;
using Gggs.Modules.Organization;
using Gggs.Modules.Payroll;
using Gggs.Modules.Performance;
using Gggs.Modules.Platform;
using Gggs.Modules.Policies;
using Gggs.Modules.Project;
using Gggs.Modules.ProjectFinancials;
using Gggs.Modules.Recruitment;
using Gggs.Modules.Resources;
using Gggs.Modules.Rewards;
using Gggs.Modules.Separation;
using Gggs.Modules.Shift;
using Gggs.Modules.Skills;
using Gggs.Modules.Timesheets;
using Gggs.Modules.Workflow;
using Gggs.Modules.Workforce;

namespace Gggs.Composition;

/// <summary>
/// The modules PeopleOS is made of (ADR-12).
/// <para>
/// Adding a module is one line here. Both hosts, the migrations project, DevSeed and the
/// integration tests all compose from this list, so they cannot disagree about what the application
/// contains - or about the shape of the database.
/// </para>
/// </summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new PlatformModule(),
        new IdentityModule(),
        new MasterDataModule(),
        new OrganizationModule(),
        new EmployeeModule(),
        new WorkflowModule(),
        new NotificationModule(),
        new DocumentsModule(),
        new PoliciesModule(),
        new ShiftModule(),
        new LeaveModule(),
        new AttendanceModule(),
        new ClientModule(),
        new ContractModule(),
        new ProjectModule(),
        new SkillsModule(),
        new TimesheetsModule(),
        new OnboardingModule(),
        new RecruitmentModule(),
        new WorkforceModule(),
        new ProjectFinancialsModule(),
        new CompensationModule(),
        new PayrollModule(),
        new BenefitsModule(),
        new ComplianceModule(),
        new ResourcesModule(),
        new AiModule(),
        new InsightModule(),
        new SeparationModule(),
        new RewardsModule(),
        new EngagementModule(),
        new HelpdeskModule(),
        new ExpenseModule(),
        new AssetsModule(),
        new CareerModule(),
        new LearningModule(),
        new PerformanceModule(),
    ];

    /// <summary>The assemblies the shared <c>AppDbContext</c> builds its model from.</summary>
    public static IReadOnlyList<Assembly> ModelAssemblies { get; } = [.. All.Select(module => module.ModelAssembly).Distinct()];
}
