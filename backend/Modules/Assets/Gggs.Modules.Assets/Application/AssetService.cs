using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Assets.Contracts;
using Gggs.Modules.Assets.Domain;
using Gggs.Modules.Assets.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Assets.Application;

public sealed record CategoryItem(Guid Id, string Code, string Name, int DepreciationYears, bool RequiresSerial, bool IsActive, int Assets, int InStock);

public sealed record CategoryBody(string Code, string Name, int DepreciationYears, bool RequiresSerial, bool IsActive = true);

public sealed record AssetItem(
    Guid Id,
    string Tag,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string? Make,
    string? Model,
    string? SerialNumber,
    DateOnly? PurchaseDate,
    decimal? PurchaseCost,
    decimal BookValue,
    string? Vendor,
    DateOnly? WarrantyUntil,
    bool WarrantyExpiringSoon,
    Guid? LocationId,
    string? LocationName,
    string? Notes,
    AssetStatus Status,
    AssetCondition Condition,
    Guid? AssignedEmployeeId,
    string? AssignedName,
    DateOnly? AssignedOn);

public sealed record AssetEventItem(Guid Id, AssetEventKind Kind, DateOnly On, string? EmployeeName, string? ByName, decimal? Cost, string? Note);

public sealed record AssetView(AssetItem Asset, IReadOnlyList<AssetEventItem> History);

public sealed record AssetQuery
{
    public AssetStatus? Status { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? EmployeeId { get; init; }

    public string? Search { get; init; }
}

public sealed record AssetBody(AssetDetails Details, AssetCondition Condition = AssetCondition.New);

public sealed record AssignBody(Guid EmployeeId, DateOnly? On, Guid? RequestId);

public sealed record ReturnBody(AssetCondition Condition, string? Note);

public sealed record EventBody(string? Note, decimal? Cost, AssetCondition? Condition);

public sealed record RequestItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    Guid CategoryId,
    string CategoryName,
    string Reason,
    AssetRequestStatus Status,
    Guid? AssetId,
    string? AssetTag,
    string? Response,
    DateTime CreatedDate,
    DateOnly? ClosedOn);

public sealed record RaiseBody(Guid CategoryId, string Reason);

public sealed record RejectBody(string Response);

public sealed record AssetDashboard(int Total, int InStock, int Assigned, int InRepair, decimal BookValue, int WarrantyExpiring, int OpenRequests, IReadOnlyDictionary<string, int> ByCategory);

/// <summary>
/// The asset register (spec: assets, full lifecycle): categories, assets bought into stock, assigned
/// and returned, repaired, retired or lost, every step in the asset's history, employees' requests,
/// and straight-line book value for exit clearance.
/// </summary>
internal sealed class AssetService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    INotifier notifier,
    ICurrentUser currentUser,
    ITenantClock clock)
{
    // ----- Categories ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<CategoryItem>> CategoriesAsync(CancellationToken cancellationToken)
    {
        List<AssetCategory> categories = await dbContext.AssetCategories.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken);
        var counts = await dbContext.Assets.AsNoTracking().Where(row => row.Status != AssetStatus.Retired && row.Status != AssetStatus.Lost)
            .GroupBy(row => row.CategoryId)
            .Select(group => new { group.Key, Total = group.Count(), InStock = group.Count(row => row.Status == AssetStatus.InStock) })
            .ToDictionaryAsync(row => row.Key, cancellationToken);
        return [.. categories.Select(row => new CategoryItem(row.Id, row.Code, row.Name, row.DepreciationYears, row.RequiresSerial, row.IsActive,
            counts.TryGetValue(row.Id, out var count) ? count.Total : 0, count?.InStock ?? 0))];
    }

    public async Task<IReadOnlyList<CategoryItem>> SaveCategoryAsync(Guid? id, CategoryBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await dbContext.AssetCategories.AnyAsync(row => row.Code == code && row.Id != id, cancellationToken))
        {
            throw new DomainRuleException("ASSET_CATEGORY_CODE", $"A category already uses the code {code}.");
        }

        if (id is { } existing)
        {
            AssetCategory category = await dbContext.AssetCategories.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(AssetCategory), existing);
            category.Update(code, body.Name, body.DepreciationYears, body.RequiresSerial, body.IsActive);
        }
        else
        {
            dbContext.AssetCategories.Add(AssetCategory.Create(code, body.Name, body.DepreciationYears, body.RequiresSerial));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await CategoriesAsync(cancellationToken);
    }

    // ----- Assets -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AssetItem>> AssetsAsync(AssetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Asset> rows = dbContext.Assets.AsNoTracking();
        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.CategoryId is { } category)
        {
            rows = rows.Where(row => row.CategoryId == category);
        }

        if (query.EmployeeId is { } employee)
        {
            rows = rows.Where(row => row.AssignedEmployeeId == employee);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            rows = rows.Where(row => row.Tag.Contains(term) || row.Name.Contains(term) || (row.SerialNumber != null && row.SerialNumber.Contains(term)));
        }

        return await ItemsAsync(await rows.OrderBy(row => row.Tag).Take(1000).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<AssetView> AssetAsync(Guid id, CancellationToken cancellationToken)
    {
        Asset asset = await dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Asset), id);
        List<AssetEvent> history = await dbContext.AssetEvents.AsNoTracking().Where(row => row.AssetId == id).OrderByDescending(row => row.On).ThenByDescending(row => row.CreatedDate).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(history.SelectMany(row => new[] { row.EmployeeId, row.ByEmployeeId }), cancellationToken);
        return new AssetView(
            (await ItemsAsync([asset], cancellationToken))[0],
            [.. history.Select(row => new AssetEventItem(row.Id, row.Kind, row.On, Name(names, row.EmployeeId), Name(names, row.ByEmployeeId), row.Cost, row.Note))]);
    }

    public async Task<AssetView> RegisterAsync(AssetBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        AssetCategory category = await ValidateAsync(body.Details, null, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        string tag = await NextTagAsync(category.Code, cancellationToken);
        var asset = Asset.Register(tag, body.Details, body.Condition);
        dbContext.Assets.Add(asset);
        dbContext.AssetEvents.Add(AssetEvent.Record(asset.Id, AssetEventKind.Registered, body.Details.PurchaseDate ?? today, null, currentUser.EmployeeId, body.Details.PurchaseCost, null));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await AssetAsync(asset.Id, cancellationToken);
    }

    public async Task<AssetView> UpdateAsync(Guid id, AssetDetails details, CancellationToken cancellationToken)
    {
        Asset asset = await TrackedAsync(id, cancellationToken);
        await ValidateAsync(details, id, cancellationToken);
        asset.Update(details);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await AssetAsync(id, cancellationToken);
    }

    public async Task<AssetView> AssignAsync(Guid id, AssignBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Asset asset = await TrackedAsync(id, cancellationToken);
        if (!await employees.IsActiveAsync(body.EmployeeId, cancellationToken))
        {
            throw new DomainRuleException("ASSET_EMPLOYEE", "Assets are assigned to active employees.");
        }

        DateOnly on = body.On ?? await clock.TodayAsync(cancellationToken);
        asset.Assign(body.EmployeeId, on);
        dbContext.AssetEvents.Add(AssetEvent.Record(id, AssetEventKind.Assigned, on, body.EmployeeId, currentUser.EmployeeId, null, null));
        if (body.RequestId is { } requestId)
        {
            AssetRequest request = await dbContext.AssetRequests.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken) ?? throw new NotFoundException(nameof(AssetRequest), requestId);
            if (request.EmployeeId != body.EmployeeId)
            {
                throw new DomainRuleException("ASSET_REQUEST", "The request was raised by someone else.");
            }

            request.Fulfil(id, on);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.AssetAssigned,
                [new NotificationRecipient(body.EmployeeId)],
                new Dictionary<string, string> { ["assetName"] = asset.Name, ["tag"] = asset.Tag },
                "/me/assets"),
            cancellationToken);
        return await AssetAsync(id, cancellationToken);
    }

    public async Task<AssetView> ReturnAsync(Guid id, ReturnBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Asset asset = await TrackedAsync(id, cancellationToken);
        Guid? holder = asset.AssignedEmployeeId;
        asset.Return(body.Condition);
        dbContext.AssetEvents.Add(AssetEvent.Record(id, AssetEventKind.Returned, await clock.TodayAsync(cancellationToken), holder, currentUser.EmployeeId, null,
            $"Returned in {body.Condition.ToString().ToLowerInvariant()} condition.{(string.IsNullOrWhiteSpace(body.Note) ? string.Empty : " " + body.Note.Trim())}"));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await AssetAsync(id, cancellationToken);
    }

    public async Task<AssetView> ActAsync(Guid id, string action, EventBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Asset asset = await TrackedAsync(id, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Guid? holder = asset.AssignedEmployeeId;
        AssetEventKind kind;
        switch (action)
        {
            case "repair":
                asset.SendForRepair();
                kind = AssetEventKind.Repair;
                break;
            case "repaired":
                asset.Repaired(body.Condition ?? AssetCondition.Good);
                kind = AssetEventKind.Repaired;
                break;
            case "retire":
                asset.Retire(lost: false);
                kind = AssetEventKind.Retired;
                break;
            case "lost":
                asset.Retire(lost: true);
                kind = AssetEventKind.Lost;
                break;
            case "note":
                if (string.IsNullOrWhiteSpace(body.Note))
                {
                    throw new DomainRuleException("ASSET_NOTE", "Write the note.");
                }

                kind = AssetEventKind.Note;
                break;
            default:
                throw new DomainRuleException("ASSET_ACTION", $"'{action}' is not an asset action.");
        }

        dbContext.AssetEvents.Add(AssetEvent.Record(id, kind, today, holder, currentUser.EmployeeId, body.Cost, body.Note));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await AssetAsync(id, cancellationToken);
    }

    // ----- Requests and self-service ------------------------------------------------------------

    public async Task<IReadOnlyList<RequestItem>> RequestsAsync(AssetRequestStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<AssetRequest> rows = dbContext.AssetRequests.AsNoTracking();
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return await RequestItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<RequestItem>> MyRequestsAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return await RequestItemsAsync(await dbContext.AssetRequests.AsNoTracking().Where(row => row.EmployeeId == me).OrderByDescending(row => row.CreatedDate).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<RequestItem>> RaiseAsync(RaiseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        if (!await dbContext.AssetCategories.AnyAsync(row => row.Id == body.CategoryId && row.IsActive, cancellationToken))
        {
            throw new NotFoundException(nameof(AssetCategory), body.CategoryId);
        }

        if (await dbContext.AssetRequests.AnyAsync(row => row.EmployeeId == me && row.CategoryId == body.CategoryId && row.Status == AssetRequestStatus.Requested, cancellationToken))
        {
            throw new DomainRuleException("ASSET_REQUEST_OPEN", "You already have an open request for this kind of asset.");
        }

        dbContext.AssetRequests.Add(AssetRequest.Raise(me, body.CategoryId, body.Reason));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MyRequestsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RequestItem>> CloseRequestAsync(Guid id, bool cancelled, string? response, CancellationToken cancellationToken)
    {
        AssetRequest request = await dbContext.AssetRequests.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(AssetRequest), id);
        if (cancelled && request.EmployeeId != currentUser.EmployeeId)
        {
            throw new NotFoundException(nameof(AssetRequest), id);
        }

        request.Close(cancelled, response, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return cancelled ? await MyRequestsAsync(cancellationToken) : await RequestsAsync(null, cancellationToken);
    }

    public async Task<IReadOnlyList<AssetItem>> MineAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return await ItemsAsync(await dbContext.Assets.AsNoTracking().Where(row => row.AssignedEmployeeId == me).OrderBy(row => row.Tag).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<AssetDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Asset> assets = await dbContext.Assets.AsNoTracking().Where(row => row.Status != AssetStatus.Retired && row.Status != AssetStatus.Lost).ToListAsync(cancellationToken);
        Dictionary<Guid, AssetCategory> categories = await dbContext.AssetCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, cancellationToken);
        return new AssetDashboard(
            assets.Count,
            assets.Count(row => row.Status == AssetStatus.InStock),
            assets.Count(row => row.Status == AssetStatus.Assigned),
            assets.Count(row => row.Status == AssetStatus.InRepair),
            assets.Sum(row => row.BookValue(today, categories[row.CategoryId].DepreciationYears)),
            assets.Count(row => row.WarrantyUntil is { } until && until >= today && until <= today.AddDays(60)),
            await dbContext.AssetRequests.CountAsync(row => row.Status == AssetRequestStatus.Requested, cancellationToken),
            assets.GroupBy(row => categories[row.CategoryId].Name).ToDictionary(group => group.Key, group => group.Count()));
    }

    // ----- Internals ----------------------------------------------------------------------------

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("ASSET_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<Asset> TrackedAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.Assets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Asset), id);

    private async Task<AssetCategory> ValidateAsync(AssetDetails details, Guid? assetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        AssetCategory category = await dbContext.AssetCategories.AsNoTracking().FirstOrDefaultAsync(row => row.Id == details.CategoryId, cancellationToken)
            ?? throw new NotFoundException(nameof(AssetCategory), details.CategoryId);
        if (category.RequiresSerial && string.IsNullOrWhiteSpace(details.SerialNumber))
        {
            throw new DomainRuleException("ASSET_SERIAL", $"A {category.Name.ToLowerInvariant()} needs its serial number.");
        }

        if (!string.IsNullOrWhiteSpace(details.SerialNumber))
        {
            string serial = details.SerialNumber.Trim();
            if (await dbContext.Assets.AnyAsync(row => row.SerialNumber == serial && row.Id != assetId, cancellationToken))
            {
                throw new DomainRuleException("ASSET_SERIAL", $"Another asset has the serial number {serial}.");
            }
        }

        if (details.LocationId is { } location && (await organization.FindInvalidAsync([new OrgReference(OrgUnitKind.Location, location)], cancellationToken)).Count > 0)
        {
            throw new DomainRuleException("ASSET_LOCATION", "The location is not active.");
        }

        return category;
    }

    private async Task<string> NextTagAsync(string categoryCode, CancellationToken cancellationToken)
    {
        string prefix = $"AST-{categoryCode[..Math.Min(4, categoryCode.Length)]}-";
        List<string> taken = await dbContext.Assets.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Where(row => row.Tag.StartsWith(prefix)).Select(row => row.Tag).ToListAsync(cancellationToken);
        int next = taken.Select(tag => int.TryParse(tag[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:0000}";
    }

    private async Task<List<AssetItem>> ItemsAsync(IReadOnlyList<Asset> assets, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        Dictionary<Guid, AssetCategory> categories = await dbContext.AssetCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, cancellationToken);
        Dictionary<Guid, string> holders = await NamesAsync(assets.Select(row => row.AssignedEmployeeId), cancellationToken);
        IReadOnlyDictionary<Guid, string> locations = await organization.GetNamesAsync(OrgUnitKind.Location, assets.Where(row => row.LocationId != null).Select(row => row.LocationId!.Value).Distinct(), cancellationToken);
        return [.. assets.Select(row =>
        {
            AssetCategory category = categories[row.CategoryId];
            return new AssetItem(
                row.Id, row.Tag, row.CategoryId, category.Name, row.Name, row.Make, row.Model, row.SerialNumber, row.PurchaseDate, row.PurchaseCost,
                row.BookValue(today, category.DepreciationYears), row.Vendor, row.WarrantyUntil,
                row.WarrantyUntil is { } until && until >= today && until <= today.AddDays(60), row.LocationId,
                row.LocationId is { } location ? locations.GetValueOrDefault(location) : null, row.Notes, row.Status, row.Condition, row.AssignedEmployeeId,
                Name(holders, row.AssignedEmployeeId), row.AssignedOn);
        })];
    }

    private async Task<List<RequestItem>> RequestItemsAsync(IReadOnlyList<AssetRequest> requests, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> categories = await dbContext.AssetCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        List<Guid> assetIds = [.. requests.Where(row => row.AssetId != null).Select(row => row.AssetId!.Value)];
        Dictionary<Guid, string> tags = await dbContext.Assets.AsNoTracking().Where(row => assetIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Tag, cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(requests.Select(row => (Guid?)row.EmployeeId), cancellationToken);
        return [.. requests.Select(row => new RequestItem(
            row.Id, row.EmployeeId, names.GetValueOrDefault(row.EmployeeId), row.CategoryId, categories.GetValueOrDefault(row.CategoryId) ?? string.Empty, row.Reason, row.Status,
            row.AssetId, row.AssetId is { } asset ? tags.GetValueOrDefault(asset) : null, row.Response, row.CreatedDate, row.ClosedOn))];
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.OfType<Guid>().Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } value ? names.GetValueOrDefault(value) : null;
}

/// <summary>Assets people hold, for other modules.</summary>
internal sealed class AssetLedger(AppDbContext dbContext, ITenantClock clock) : IAssetLedger
{
    public async Task<IReadOnlyList<HeldAsset>> HeldByAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Asset> assets = await dbContext.Assets.AsNoTracking().Where(row => row.AssignedEmployeeId == employeeId).ToListAsync(cancellationToken);
        Dictionary<Guid, AssetCategory> categories = await dbContext.AssetCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, cancellationToken);
        return [.. assets.Select(row => new HeldAsset(row.Id, row.Tag, row.Name, categories[row.CategoryId].Name, row.AssignedOn ?? today, row.BookValue(today, categories[row.CategoryId].DepreciationYears)))];
    }
}

/// <summary>Seeds a tenant's default asset categories.</summary>
public sealed class AssetSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> codes = [.. await dbContext.AssetCategories.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Code).ToListAsync(cancellationToken)];
        int added = 0;
        foreach ((string code, string name, int years, bool serial) in AssetDefaults.Categories.Where(row => !codes.Contains(row.Code)))
        {
            dbContext.AssetCategories.Add(AssetCategory.Create(code, name, years, serial));
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}
