using System.Reflection;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Documents;
using Gggs.Modules.Employee;
using Gggs.Modules.Identity;
using Gggs.Modules.MasterData;
using Gggs.Modules.Notification;
using Gggs.Modules.Organization;
using Gggs.Modules.Platform;
using Gggs.Modules.Policies;
using Gggs.Modules.Workflow;

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
    ];

    /// <summary>The assemblies the shared <c>AppDbContext</c> builds its model from.</summary>
    public static IReadOnlyList<Assembly> ModelAssemblies { get; } = [.. All.Select(module => module.ModelAssembly).Distinct()];
}
