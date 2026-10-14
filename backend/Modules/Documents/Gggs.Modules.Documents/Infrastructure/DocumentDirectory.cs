using Gggs.Data;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Documents.Infrastructure;

internal sealed class DocumentDirectory(AppDbContext dbContext) : IDocumentDirectory
{
    public async Task<IReadOnlyDictionary<Guid, int>> CountForEmployeesAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        List<Guid?> ids = [.. employeeIds.Distinct().Select(id => (Guid?)id)];

        return await dbContext.Documents.AsNoTracking()
            .Where(document => document.OwnerType == DocumentOwnerType.Employee && ids.Contains(document.OwnerId) && document.IsActive)
            .GroupBy(document => document.OwnerId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);
    }
}
