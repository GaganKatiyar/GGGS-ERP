using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Platform.Api.Contracts;
using Gggs.Platform.Domain.Tenants;
using Gggs.Platform.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Platform.Api.Endpoints;

/// <summary>
/// A tenant's own configuration, managed by their administrators.
/// <para>
/// The one tenant-scoped area of this service. Nothing here names a tenant: the tenant comes from
/// the token, the query filter applies it, and row-level security enforces it underneath. That is
/// why a tenant admin can edit their working week but cannot touch their own subscription, which
/// lives two tables away under platform permissions.
/// </para>
/// </summary>
public static class TenantSettingEndpoints
{
    public static IEndpointRouteBuilder MapTenantSettingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/settings").WithTags("Settings");

        group.MapGet("/", ListAsync)
            .WithName("ListTenantSettings")
            .RequirePermission(Permissions.Admin.SettingView);

        group.MapGet("/{module}", ListByModuleAsync)
            .WithName("ListTenantSettingsByModule")
            .RequirePermission(Permissions.Admin.SettingView);

        group.MapPut("/{module}/{key}", UpsertAsync)
            .WithName("UpsertTenantSetting")
            .RequirePermission(Permissions.Admin.SettingManage);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<TenantSettingItem>>> ListAsync(
        PlatformDbContext database,
        CancellationToken cancellationToken)
    {
        // No tenant predicate: the global query filter adds it, and the database enforces it.
        List<TenantSettingItem> settings = await database.TenantSettings
            .AsNoTracking()
            .OrderBy(setting => setting.Module)
            .ThenBy(setting => setting.Key)
            .Select(setting => new TenantSettingItem(setting.Module, setting.Key, setting.ValueJson))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<TenantSettingItem>>(settings);
    }

    private static async Task<Ok<IReadOnlyList<TenantSettingItem>>> ListByModuleAsync(
        string module,
        PlatformDbContext database,
        CancellationToken cancellationToken)
    {
        string normalized = module.Trim().ToLowerInvariant();

        List<TenantSettingItem> settings = await database.TenantSettings
            .AsNoTracking()
            .Where(setting => setting.Module == normalized)
            .OrderBy(setting => setting.Key)
            .Select(setting => new TenantSettingItem(setting.Module, setting.Key, setting.ValueJson))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<TenantSettingItem>>(settings);
    }

    private static async Task<Ok<TenantSettingItem>> UpsertAsync(
        string module,
        string key,
        UpsertTenantSettingBody body,
        PlatformDbContext database,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!IsJson(body.ValueJson))
        {
            // Settings are stored as JSON so modules can hold structures without a migration each
            // time. Validating on the way in keeps a malformed value from breaking the screen that
            // reads it long afterwards, when the cause is no longer obvious.
            throw new DomainRuleException("SETTING_VALUE_NOT_JSON", "The setting value must be valid JSON.");
        }

        string normalizedModule = module.Trim().ToLowerInvariant();
        string normalizedKey = key.Trim();

        TenantSetting? setting = await database.TenantSettings
            .FirstOrDefaultAsync(
                candidate => candidate.Module == normalizedModule && candidate.Key == normalizedKey,
                cancellationToken);

        if (setting is null)
        {
            setting = TenantSetting.Create(normalizedModule, normalizedKey, body.ValueJson);
            database.TenantSettings.Add(setting);
        }
        else
        {
            setting.SetValue(body.ValueJson);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new TenantSettingItem(setting.Module, setting.Key, setting.ValueJson));
    }

    private static bool IsJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
