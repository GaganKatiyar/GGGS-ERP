namespace Gggs.BuildingBlocks.Application;

/// <summary>
/// Placeholder secrets used only when no real one is configured and the host is in Development.
/// <para>
/// Here rather than duplicated in each host so the issuer and the client cannot drift apart: two
/// different development defaults means a token endpoint that rejects its own gateway, and the error
/// it produces looks like a credential problem rather than a configuration one.
/// </para>
/// <para>
/// Every host that falls back to one of these refuses to start outside Development, so none of these
/// values can reach a real deployment.
/// </para>
/// </summary>
public static class DevelopmentSecrets
{
    /// <summary>The gateway's OAuth client secret in development.</summary>
    public const string GatewayClientSecret = "development-only-gateway-secret";
}
