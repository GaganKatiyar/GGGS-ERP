using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Gggs.BuildingBlocks.Application.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.Secrets;

/// <summary>Picks the secret store that matches the deployment profile.</summary>
public static class SecretStoreExtensions
{
    public static IServiceCollection AddGggsSecretStore(
        this IServiceCollection services,
        IConfiguration configuration,
        DeploymentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMemoryCache();

        if (profile == DeploymentProfile.OnPremise)
        {
            services.AddDataProtection();
            services.AddOptions<FileSecretStoreOptions>()
                .Bind(configuration.GetSection(FileSecretStoreOptions.SectionName))
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.FilePath),
                    "On-premise deployments need Gggs:Secrets:File:FilePath.")
                .ValidateOnStart();

            services.AddSingleton<ISecretStore, ProtectedFileSecretStore>();
            return services;
        }

        services.AddSingleton(_ => new SecretClient(
            new Uri(configuration["Gggs:Secrets:KeyVault:Uri"]
                ?? throw new InvalidOperationException("Gggs:Secrets:KeyVault:Uri must be set for the cloud profile.")),
            new DefaultAzureCredential()));

        services.AddSingleton<ISecretStore, KeyVaultSecretStore>();
        return services;
    }
}
