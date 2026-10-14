using System.Reflection;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Gggs.Data;

/// <summary>
/// A module's contribution to the shared model: its tables, keys, indexes and relationships.
/// <para>
/// Every type implementing this in a module's <see cref="ModelAssemblies">model assembly</see> is
/// instantiated and applied by <see cref="AppDbContext"/>. Implementations name their schema on
/// every table explicitly; nothing lands in <c>dbo</c> by accident (an architecture test checks).
/// </para>
/// </summary>
public interface IModuleModel
{
    void Configure(ModelBuilder modelBuilder);
}

/// <summary>The assemblies whose <see cref="IModuleModel"/>s make up the model, in a stable order.</summary>
public sealed class ModelAssemblies
{
    public ModelAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        All = [.. assemblies.Distinct().OrderBy(assembly => assembly.FullName, StringComparer.Ordinal)];
        CacheKey = string.Join('|', All.Select(assembly => assembly.GetName().Name));
    }

    public IReadOnlyList<Assembly> All { get; }

    /// <summary>Distinguishes models built from different module sets, e.g. in tests.</summary>
    public string CacheKey { get; }
}

/// <summary>
/// The one database context of the monolith (ADR-12).
/// <para>
/// One context, one connection and one transaction per request, whatever modules the request
/// touches - which is what lets a module call another module's contract and have both changes
/// commit or roll back together. Each module still owns its own schema(s).
/// </para>
/// <para>
/// Conventions from <see cref="GggsDbContext"/> apply to every entity from every module: tenant and
/// soft-delete filters, the clustered (TenantId, Id) layout, rowversion and UTC columns.
/// </para>
/// </summary>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext,
    ModelAssemblies modelAssemblies) : GggsDbContext(options, tenantContext)
{
    /// <summary>Where anything not given a schema would go. Nothing should; see <see cref="IModuleModel"/>.</summary>
    public const string FallbackSchema = "dbo";

    /// <summary>Per-module migration history would split one database into several; there is one.</summary>
    public const string MigrationsHistorySchema = "platform";

    public ModelAssemblies ModelAssemblies { get; } = modelAssemblies;

    protected override string DefaultSchema => FallbackSchema;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // The model depends on which modules are loaded, not just on the context type.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ModuleSetModelCacheKeyFactory>();
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (IModuleModel moduleModel in ModuleModelsIn(ModelAssemblies.All))
        {
            moduleModel.Configure(modelBuilder);
        }

        // One outbox and one inbox for the whole application.
        modelBuilder.AddGggsMessagingModel();

        // Last, so the conventions cover every module's entities.
        base.OnModelCreating(modelBuilder);
    }

    private static IEnumerable<IModuleModel> ModuleModelsIn(IEnumerable<Assembly> assemblies)
        => assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModuleModel).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IModuleModel)Activator.CreateInstance(type)!);

    private sealed class ModuleSetModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is AppDbContext app
                ? (context.GetType(), app.ModelAssemblies.CacheKey, designTime)
                : (object)(context.GetType(), designTime);
    }
}
