using Gggs.BuildingBlocks.Messaging;
using Gggs.Platform.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.Identity.Application.Provisioning;

/// <summary>
/// Seeds a tenant's roles and first administrator when Platform announces it.
/// <para>
/// The tenant context is already set from the message envelope before this runs, so everything the
/// seeding service writes lands under the right tenant without it having to be told which one - and
/// a message with no tenant is rejected rather than silently writing somewhere.
/// </para>
/// </summary>
public sealed class TenantProvisionedConsumer(
    TenantSeedingService seeding,
    ILogger<TenantProvisionedConsumer> logger) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public async Task HandleAsync(
        TenantProvisionedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        TenantSeedingResult result = await seeding.SeedAsync(
            integrationEvent.AdministratorEmail,
            cancellationToken);

        logger.LogInformation(
            "Tenant {TenantCode} seeded: {RolesCreated} role(s) created, {RolesAlreadyPresent} already present.",
            integrationEvent.Code,
            result.RolesCreated,
            result.RolesAlreadyPresent);
    }
}
