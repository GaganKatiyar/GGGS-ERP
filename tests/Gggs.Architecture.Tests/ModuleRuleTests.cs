using System.Reflection;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Composition;
using Shouldly;

namespace Gggs.Architecture.Tests;

/// <summary>
/// Keeps the monolith modular (ADR-12).
/// <para>
/// One process makes it easy to reach into another module's internals - a repository, an entity,
/// a table - and every such shortcut is what would later make a module impossible to extract or
/// reason about alone. These rules fail the build instead.
/// </para>
/// </summary>
public sealed class ModuleRuleTests
{
    private const string ModulePrefix = "Gggs.Modules.";
    private const string ContractsSuffix = ".Contracts";

    public static TheoryData<string> Modules()
    {
        var data = new TheoryData<string>();
        foreach (IModule module in ModuleCatalog.All)
        {
            data.Add(module.GetType().Assembly.GetName().Name!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void AModule_ReferencesOtherModulesOnlyThroughTheirContracts(string moduleAssemblyName)
    {
        Assembly module = Assembly.Load(moduleAssemblyName);
        string ownContracts = moduleAssemblyName + ContractsSuffix;

        List<string> offenders =
        [
            .. module.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => name.StartsWith(ModulePrefix, StringComparison.Ordinal))
                .Where(name => !name.EndsWith(ContractsSuffix, StringComparison.Ordinal))
                .Where(name => name != moduleAssemblyName && name != ownContracts),
        ];

        offenders.ShouldBeEmpty(
            $"{moduleAssemblyName} references another module's internals: {string.Join(", ", offenders)}. " +
            "Depend on that module's .Contracts assembly instead.");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void AModulesContracts_DependOnNothingButBuildingBlocks(string moduleAssemblyName)
    {
        Assembly? contracts = TryLoad(moduleAssemblyName + ContractsSuffix);

        if (contracts is null)
        {
            // A module with nothing to share needs no contracts assembly.
            return;
        }

        List<string> offenders =
        [
            .. contracts.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => name.StartsWith("Gggs.", StringComparison.Ordinal))
                .Where(name => !name.StartsWith("Gggs.BuildingBlocks.", StringComparison.Ordinal)),
        ];

        offenders.ShouldBeEmpty(
            $"{contracts.GetName().Name} must stay a thin public surface, but references: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryModule_HasAUniqueName()
    {
        List<string> duplicates =
        [
            .. ModuleCatalog.All
                .GroupBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];

        duplicates.ShouldBeEmpty();
    }

    [Fact]
    public void BuildingBlocks_NeverReferenceAModule()
    {
        // Dependencies point inwards: modules use building blocks, never the reverse.
        List<string> offenders =
        [
            .. AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name!.StartsWith("Gggs.BuildingBlocks.", StringComparison.Ordinal))
                .SelectMany(assembly => assembly.GetReferencedAssemblies()
                    .Where(reference => reference.Name!.StartsWith(ModulePrefix, StringComparison.Ordinal))
                    .Select(reference => $"{assembly.GetName().Name} -> {reference.Name}")),
        ];

        offenders.ShouldBeEmpty();
    }

    private static Assembly? TryLoad(string name)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
