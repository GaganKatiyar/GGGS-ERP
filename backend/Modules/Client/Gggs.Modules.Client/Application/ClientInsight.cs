using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Client.Infrastructure;
using Gggs.Modules.Insight.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Client.Application;

/// <summary>Clients in global search, for those who may see clients.</summary>
internal sealed class ClientSearch(AppDbContext dbContext, IPermissionProvider permissions) : ISearchSource
{
    public string Kind => "client";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int take, CancellationToken cancellationToken = default)
    {
        if (!(await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Client.View))
        {
            return [];
        }

        var rows = await dbContext.Clients.AsNoTracking()
            .Where(row => row.Name.Contains(term) || row.Code.Contains(term) || (row.LegalName != null && row.LegalName.Contains(term)))
            .OrderBy(row => row.Name).Take(take)
            .Select(row => new { row.Id, row.Name, row.Code, row.City })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SearchHit(Kind, row.Id, row.Name, row.City is null ? row.Code : $"{row.Code} · {row.City}", $"/clients/{row.Id}",
            row.Name.Equals(term, StringComparison.OrdinalIgnoreCase) || row.Code.Equals(term, StringComparison.OrdinalIgnoreCase) ? 1 : row.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0.8 : 0.5))];
    }
}
