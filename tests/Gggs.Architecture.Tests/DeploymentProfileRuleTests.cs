using System.Reflection;
using Gggs.BuildingBlocks.Caching;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Messaging.Transports;
using Gggs.BuildingBlocks.Secrets;
using Shouldly;

namespace Gggs.Architecture.Tests;

/// <summary>
/// Keeps the platform deployable to a customer's own servers as well as to Azure.
/// <para>
/// On-premise support does not survive on good intentions: one <c>BlobServiceClient</c> in a
/// handler, and the on-premise build breaks in a way nobody notices until an install fails. These
/// rules fail the pull request instead.
/// </para>
/// </summary>
public sealed class DeploymentProfileRuleTests
{
    /// <summary>
    /// The only assemblies allowed to touch a cloud vendor SDK. Each one exists to hide that SDK
    /// behind a port, and each has an on-premise sibling.
    /// </summary>
    private static readonly string[] AdapterAssemblies =
    [
        "Gggs.BuildingBlocks.Files",
        "Gggs.BuildingBlocks.Messaging",
        "Gggs.BuildingBlocks.Secrets",
    ];

    private static readonly string[] VendorPrefixes =
    [
        "Azure.",
        "Microsoft.Azure.",
        "Amazon.",
        "AWSSDK.",
    ];

    [Fact]
    public void OnlyAdapterAssemblies_ReferenceCloudVendorSdks()
    {
        List<string> offenders = [];

        foreach (Assembly assembly in LoadPlatformAssemblies())
        {
            string name = assembly.GetName().Name!;
            if (AdapterAssemblies.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            IEnumerable<string> vendorReferences = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(reference => VendorPrefixes.Any(prefix =>
                    reference.StartsWith(prefix, StringComparison.Ordinal)));

            offenders.AddRange(vendorReferences.Select(reference => $"{name} -> {reference}"));
        }

        offenders.ShouldBeEmpty(
            "cloud SDKs belong behind a port with an on-premise adapter. Offending references: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryInfrastructurePort_HasBothACloudAndAnOnPremiseImplementation()
    {
        AssertHasBothProfiles<IFileStorage>(typeof(AzureBlobFileStorage), typeof(FileSystemFileStorage));
        AssertHasBothProfiles<ISecretStore>(typeof(KeyVaultSecretStore), typeof(ProtectedFileSecretStore));
        AssertHasBothProfiles<IMessageTransport>(typeof(AzureServiceBusTransport), typeof(RabbitMqTransport));

        // Caching is intentionally single-implementation: HybridCache with Redis behind it in the
        // cloud and in-process on a single-node install. There is no vendor API in the code path.
        typeof(IGggsCache).IsAssignableFrom(typeof(GggsHybridCache)).ShouldBeTrue();
    }

    private static void AssertHasBothProfiles<TPort>(Type cloudAdapter, Type onPremiseAdapter)
    {
        typeof(TPort).IsAssignableFrom(cloudAdapter).ShouldBeTrue($"{cloudAdapter.Name} must implement {typeof(TPort).Name}");
        typeof(TPort).IsAssignableFrom(onPremiseAdapter).ShouldBeTrue($"{onPremiseAdapter.Name} must implement {typeof(TPort).Name}");
    }

    /// <summary>Every Gggs assembly next to the test binary, which is the whole platform under test.</summary>
    private static IEnumerable<Assembly> LoadPlatformAssemblies()
    {
        string directory = Path.GetDirectoryName(typeof(DeploymentProfileRuleTests).Assembly.Location)!;

        foreach (string path in Directory.EnumerateFiles(directory, "Gggs.*.dll"))
        {
            if (Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            Assembly? assembly = TryLoad(path);
            if (assembly is not null)
            {
                yield return assembly;
            }
        }
    }

    private static Assembly? TryLoad(string path)
    {
        try
        {
            return Assembly.LoadFrom(path);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
