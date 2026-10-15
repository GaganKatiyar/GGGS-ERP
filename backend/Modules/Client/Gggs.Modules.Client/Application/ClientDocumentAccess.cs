using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Client.Infrastructure;
using Gggs.Modules.Documents.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Client.Application;

/// <summary>Client documents (signed MSAs, NDAs, purchase orders) follow client access.</summary>
internal sealed class ClientDocumentAccess(AppDbContext dbContext, IPermissionProvider permissions) : IDocumentOwnerAccess
{
    public string OwnerType => "Client";

    public async Task<bool> CanReadAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Client.View) && await ExistsAsync(ownerId, cancellationToken);

    public async Task<bool> CanWriteAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Client.Manage) && await ExistsAsync(ownerId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. ownerIds.Distinct()];
        return await dbContext.Clients.AsNoTracking().Where(client => ids.Contains(client.Id)).ToDictionaryAsync(client => client.Id, client => client.Name, cancellationToken);
    }

    private Task<bool> ExistsAsync(Guid clientId, CancellationToken cancellationToken)
        => dbContext.Clients.AnyAsync(client => client.Id == clientId, cancellationToken);
}
