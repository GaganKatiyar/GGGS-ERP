using Gggs.Modules.Identity.Application.Authentication;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// Maps a verified email domain to its tenant, via the Platform service.
/// <para>
/// Not yet implemented: domain verification is Platform work that has not landed. It returns no
/// match rather than guessing, so sign-in falls back to requiring a tenant code - which is what the
/// subdomain supplies anyway. Guessing here would mean sending someone's password to the wrong
/// customer's user store.
/// </para>
/// </summary>
public sealed class PlatformTenantDomainResolver(ILogger<PlatformTenantDomainResolver> logger) : ITenantDomainResolver
{
    private bool _warned;

    public Task<Guid?> ResolveTenantIdAsync(string emailDomain, CancellationToken cancellationToken = default)
    {
        if (!_warned)
        {
            logger.LogInformation(
                "Email-domain tenant discovery is not configured; sign-in requires a tenant code.");
            _warned = true;
        }

        return Task.FromResult<Guid?>(null);
    }
}
