using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Application.Authentication;

/// <summary>How a tenant was identified at sign-in, recorded so support can reconstruct a login.</summary>
public enum TenantDiscoveryMethod
{
    /// <summary>Supplied explicitly, usually from the subdomain the user is on.</summary>
    TenantCode = 1,

    /// <summary>Inferred from the email domain, for customers who have verified one.</summary>
    EmailDomain = 2,
}

public sealed record TenantDiscoveryResult(TenantInfo Tenant, TenantDiscoveryMethod Method);

/// <summary>
/// Works out which customer a sign-in belongs to, before any user is looked up.
/// <para>
/// This exists because users are tenant-owned: the same email address can belong to two customers,
/// as two separate accounts with two separate passwords. A query for a user is meaningless until
/// the tenant is known, so discovery has to happen first.
/// </para>
/// <para>
/// Discovery decides <em>where to look</em>, never <em>whether to trust</em>. Whatever a caller
/// supplies here only narrows the search; the credential check that follows is what actually
/// authenticates, and the tenant in the issued token comes from the user record that was found -
/// not from what the caller asked for.
/// </para>
/// </summary>
public sealed class TenantDiscoveryService(
    ITenantRegistry registry,
    ITenantDomainResolver domainResolver,
    ILogger<TenantDiscoveryService> logger)
{
    public async Task<TenantDiscoveryResult?> DiscoverAsync(
        string? tenantCode,
        string email,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(tenantCode))
        {
            TenantInfo? byCode = await registry.GetByCodeAsync(tenantCode, cancellationToken);

            if (byCode is null)
            {
                // Logged, not returned: telling a caller "no such customer" lets anyone probe which
                // companies are on the platform.
                logger.LogInformation("Sign-in referenced unknown tenant code {TenantCode}.", tenantCode);
                return null;
            }

            return new TenantDiscoveryResult(byCode, TenantDiscoveryMethod.TenantCode);
        }

        // Falls back to the email domain, which only works for customers who have verified one.
        // A shared domain such as gmail.com is never mapped, so those users reach the platform
        // through their tenant's own address.
        string domain = ExtractDomain(email);
        if (domain.Length == 0)
        {
            return null;
        }

        Guid? tenantId = await domainResolver.ResolveTenantIdAsync(domain, cancellationToken);
        if (tenantId is null)
        {
            logger.LogInformation("Sign-in supplied no tenant code and domain {Domain} maps to no tenant.", domain);
            return null;
        }

        TenantInfo? byDomain = await registry.GetAsync(tenantId.Value, cancellationToken);
        return byDomain is null ? null : new TenantDiscoveryResult(byDomain, TenantDiscoveryMethod.EmailDomain);
    }

    private static string ExtractDomain(string email)
    {
        int at = email.LastIndexOf('@');
        return at < 0 || at == email.Length - 1 ? string.Empty : email[(at + 1)..].Trim().ToLowerInvariant();
    }
}

/// <summary>
/// Maps a verified email domain to the tenant that owns it.
/// <para>
/// Owned by the Platform service, which holds the domain verification. Kept as a port here so the
/// sign-in path does not depend on how that lookup is made - and so the same interface later serves
/// federation, where the domain also decides which identity provider to redirect to.
/// </para>
/// </summary>
public interface ITenantDomainResolver
{
    Task<Guid?> ResolveTenantIdAsync(string emailDomain, CancellationToken cancellationToken = default);
}
