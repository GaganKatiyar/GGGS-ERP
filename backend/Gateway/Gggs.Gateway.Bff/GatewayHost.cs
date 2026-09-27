namespace Gggs.Gateway.Bff;

/// <summary>
/// Entry-point marker for the test host.
/// <para>
/// Named rather than relying on the implicit <c>Program</c> type, because a test project that hosts
/// both the gateway and a service would otherwise have two types called <c>Program</c> in scope and
/// fail to compile.
/// </para>
/// </summary>
public sealed class GatewayHost;
