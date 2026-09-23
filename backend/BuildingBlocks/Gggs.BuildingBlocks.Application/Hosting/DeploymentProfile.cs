namespace Gggs.BuildingBlocks.Application.Hosting;

/// <summary>
/// Where this instance is running. The same container images serve both, and every infrastructure
/// dependency has an adapter per profile (architecture §20.1).
/// </summary>
public enum DeploymentProfile
{
    /// <summary>Our Azure SaaS: Container Apps, Azure SQL, Service Bus, Blob Storage, Key Vault.</summary>
    Cloud = 1,

    /// <summary>
    /// A customer's own servers: IIS or Docker, their SQL Server, RabbitMQ, a file share and an
    /// encrypted local secret store. Multi-region residency, elastic pools and auto-failover are
    /// cloud-only and are documented as unavailable here.
    /// </summary>
    OnPremise = 2,
}

/// <summary>Host-level settings every service reads.</summary>
public sealed class GggsPlatformOptions
{
    public const string SectionName = "Gggs:Platform";

    /// <summary>
    /// Cloud unless a deployment says otherwise, so a misconfigured on-prem install fails loudly on
    /// its first Key Vault call rather than quietly writing customer data somewhere unexpected.
    /// </summary>
    public DeploymentProfile DeploymentProfile { get; set; } = DeploymentProfile.Cloud;

    /// <summary>Service name used in telemetry, log enrichment and health reporting.</summary>
    public string ServiceName { get; set; } = "unknown";

    public bool IsOnPremise => DeploymentProfile == DeploymentProfile.OnPremise;

    public bool IsCloud => DeploymentProfile == DeploymentProfile.Cloud;
}
