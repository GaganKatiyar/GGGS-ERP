using System.Security.Claims;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Domain.ApiKeys;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Application.ApiKeys;

public sealed record ApiKeyRole(Guid Id, string Name);

public sealed record ApiKeyItem(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<ApiKeyRole> Roles,
    string? CreatedBy,
    DateTime CreatedDate,
    DateTime? LastUsedAt,
    DateOnly? ExpiresOn,
    DateTime? RevokedAt,
    string Status);

public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<Guid> RoleIds, int? ExpiresInDays);

/// <summary>The new key, with its full value - shown this once and never again.</summary>
public sealed record CreatedApiKey(ApiKeyItem Key, string Value);

/// <summary>
/// API keys (Studio): each is a machine account holding chosen roles, so outside systems get
/// exactly the permissions those roles carry. A key cannot be given more than its creator holds.
/// </summary>
public sealed class ApiKeyService(
    AppDbContext db,
    ITenantContext tenantContext,
    ITenantContextSetter tenantSetter,
    ITenantRegistry tenants,
    ICurrentUser currentUser,
    IPermissionProvider permissions,
    TimeProvider timeProvider)
{
    public const string SchemeName = "ApiKey";

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<ApiKeyItem>> ListAsync(CancellationToken cancellationToken)
    {
        List<ApiKey> keys = await db.ApiKeys.AsNoTracking().OrderByDescending(key => key.CreatedDate).ToListAsync(cancellationToken);
        return await ItemsAsync(keys, cancellationToken);
    }

    public async Task<CreatedApiKey> CreateAsync(CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RoleIds is not { Count: > 0 })
        {
            throw new DomainRuleException("API_KEY_ROLES", "Give the key at least one role: it can do what its roles allow, and nothing else.");
        }

        if (request.ExpiresInDays is < 1 or > 3650)
        {
            throw new DomainRuleException("API_KEY_EXPIRY", "A key expires after 1 to 3650 days, or never.");
        }

        List<Guid> roleIds = [.. request.RoleIds.Distinct()];
        List<Role> roles = await db.Roles.AsNoTracking().Where(role => roleIds.Contains(role.Id)).ToListAsync(cancellationToken);
        if (roles.Count != roleIds.Count)
        {
            throw new DomainRuleException("API_KEY_ROLES", "One of those roles does not exist.");
        }

        // No escalation: a key holds nothing its creator does not.
        PermissionSet mine = await permissions.GetCurrentAsync(cancellationToken);
        string? beyond = roles.SelectMany(role => role.Permissions).Select(permission => permission.PermissionCode).FirstOrDefault(code => !mine.Has(code));
        if (beyond is not null)
        {
            throw new DomainRuleException("API_KEY_ESCALATION", $"A key cannot be given permissions you do not hold yourself ({beyond}).");
        }

        string tenantCode = tenantContext.Tenant?.Code ?? (await tenants.GetAsync(tenantContext.TenantId, cancellationToken))?.Code
            ?? throw new InvalidOperationException("The organisation's code is unknown.");
        (string value, string prefix, string secret) = ApiKeyFormat.Generate(tenantCode);
        string name = request.Name?.Trim() ?? string.Empty;

        User machine = User.CreateApiClient($"API key: {name}", $"api-{prefix}@keys.peopleos.invalid");
        db.Users.Add(machine);
        DateOnly today = DateOnly.FromDateTime(Now);
        foreach (Role role in roles)
        {
            db.UserRoles.Add(UserRole.Grant(machine.Id, role.Id, today, reason: "API key"));
        }

        DateOnly? expires = request.ExpiresInDays is { } days ? today.AddDays(days) : null;
        var key = ApiKey.Create(name, prefix, ApiKeyFormat.Hash(secret), machine.Id, expires, currentUser.UserId);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatedApiKey((await ItemsAsync([key], cancellationToken))[0], value);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        ApiKey key = await db.ApiKeys.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ApiKey), id);
        key.Revoke(Now);

        // The machine account goes with it, so nothing signed in as it keeps working.
        if (await db.Users.FirstOrDefaultAsync(user => user.Id == key.ServiceUserId, cancellationToken) is { Status: not UserStatus.Disabled } machine)
        {
            machine.Disable();
            machine.BumpPermissionVersion();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The principal for a request carrying this key, or null when the key is malformed, unknown,
    /// revoked, expired, or its organisation or machine account cannot be used.
    /// </summary>
    public async Task<ClaimsPrincipal?> AuthenticateAsync(string? value, CancellationToken cancellationToken)
    {
        if (ApiKeyFormat.Parse(value) is not { } parts)
        {
            return null;
        }

        TenantInfo? tenant = await tenants.GetByCodeAsync(parts.TenantCode, cancellationToken);
        if (tenant is null || !tenant.IsOperational)
        {
            return null;
        }

        tenantSetter.SetTenant(tenant);
        ApiKey? key = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(row => row.Prefix == parts.Prefix, cancellationToken);
        if (key is null || !key.Matches(parts.Secret) || !key.IsUsableOn(Now))
        {
            return null;
        }

        User? machine = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == key.ServiceUserId, cancellationToken);
        if (machine is null || machine.UserType != UserType.ApiClient || machine.Status != UserStatus.Active)
        {
            return null;
        }

        // Recorded straight to the row: nobody is signed in yet for change tracking or auditing to name.
        DateTime now = Now;
        if (key.Touch(now))
        {
            await db.ApiKeys.Where(row => row.Id == key.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastUsedAt, now), cancellationToken);
        }

        return UserPrincipalFactory.Create(machine, tenant.Id, key.Id, timeProvider.GetUtcNow(), SchemeName, UserPrincipalFactory.AuthenticationMethods.ApiKey);
    }

    private async Task<List<ApiKeyItem>> ItemsAsync(List<ApiKey> keys, CancellationToken cancellationToken)
    {
        List<Guid> machineIds = [.. keys.Select(key => key.ServiceUserId)];
        List<Guid> creatorIds = [.. keys.Select(key => key.CreatedByUserId).OfType<Guid>().Distinct()];
        Dictionary<Guid, string> creators = await db.Users.AsNoTracking().Where(user => creatorIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        var grants = await db.UserRoles.AsNoTracking()
            .Where(grant => machineIds.Contains(grant.UserId))
            .Join(db.Roles.AsNoTracking(), grant => grant.RoleId, role => role.Id, (grant, role) => new { grant.UserId, role.Id, role.Name })
            .ToListAsync(cancellationToken);
        DateTime now = Now;

        return [.. keys.Select(key => new ApiKeyItem(
            key.Id,
            key.Name,
            key.Prefix,
            [.. grants.Where(grant => grant.UserId == key.ServiceUserId).Select(grant => new ApiKeyRole(grant.Id, grant.Name)).OrderBy(role => role.Name)],
            key.CreatedByUserId is { } by ? creators.GetValueOrDefault(by) : null,
            key.CreatedDate == default ? now : key.CreatedDate,
            key.LastUsedAt,
            key.ExpiresOn,
            key.RevokedAt,
            key.RevokedAt is not null ? "Revoked" : key.IsUsableOn(now) ? "Active" : "Expired"))];
    }
}
