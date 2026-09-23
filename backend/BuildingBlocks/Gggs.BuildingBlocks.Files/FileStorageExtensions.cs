using Azure.Identity;
using Azure.Storage.Blobs;
using Gggs.BuildingBlocks.Application.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.BuildingBlocks.Files;

/// <summary>Picks the storage adapter that matches the deployment profile.</summary>
public static class FileStorageExtensions
{
    public static IServiceCollection AddGggsFileStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        DeploymentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (profile == DeploymentProfile.OnPremise)
        {
            services.AddOptions<FileSystemStorageOptions>()
                .Bind(configuration.GetSection(FileSystemStorageOptions.SectionName))
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.RootPath),
                    "On-premise storage needs Gggs:Storage:FileSystem:RootPath.")
                .ValidateOnStart();

            services.AddScoped<IFileStorage, FileSystemFileStorage>();
            return services;
        }

        services.AddSingleton(_ =>
        {
            string? connectionString = configuration.GetConnectionString("BlobStorage");

            // Prefer a managed identity; a connection string is for local development and for the
            // storage emulator, and means a secret exists that could leak.
            return string.IsNullOrWhiteSpace(connectionString)
                ? new BlobServiceClient(
                    new Uri(configuration["Gggs:Storage:Blob:ServiceUri"]
                        ?? throw new InvalidOperationException("Gggs:Storage:Blob:ServiceUri must be set.")),
                    new DefaultAzureCredential())
                : new BlobServiceClient(connectionString);
        });

        services.AddScoped<IFileStorage, AzureBlobFileStorage>();
        return services;
    }
}
