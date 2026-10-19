using System.Globalization;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Assets.Domain;
using Gggs.Modules.Assets.Infrastructure;
using Gggs.Modules.Insight.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Assets.Application;

/// <summary>The asset register for company-wide dashboards: stock, assignment, value and warranties.</summary>
internal sealed class AssetMetrics(AssetService assets) : IMetricSource
{
    public IReadOnlyCollection<string> Areas { get; } = [MetricAreas.Experience];

    public async Task<IReadOnlyList<Metric>> CollectAsync(MetricContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TenantWide)
        {
            return [];
        }

        AssetDashboard dashboard = await assets.DashboardAsync(cancellationToken);
        return
        [
            new Metric("experience.assets", MetricAreas.Experience, "Assets assigned", dashboard.Assigned, MetricUnit.Count, $"{dashboard.InStock} in stock, {dashboard.InRepair} in repair",
                [.. dashboard.ByCategory.Select(pair => new MetricPoint(pair.Key, pair.Value))]),
            new Metric("experience.asset-value", MetricAreas.Experience, "Asset book value", dashboard.BookValue, MetricUnit.Currency, $"{dashboard.WarrantyExpiring} warranty(ies) ending soon; {dashboard.OpenRequests} request(s) open"),
        ];
    }
}

/// <summary>Assets in global search by tag, serial number or name, for those who see the register.</summary>
internal sealed class AssetSearch(AppDbContext dbContext, IPermissionProvider permissions) : ISearchSource
{
    public string Kind => "asset";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int take, CancellationToken cancellationToken = default)
    {
        if (!(await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Asset.View))
        {
            return [];
        }

        var rows = await dbContext.Assets.AsNoTracking()
            .Where(row => row.Tag.Contains(term) || row.Name.Contains(term) || (row.SerialNumber != null && row.SerialNumber.Contains(term)))
            .OrderBy(row => row.Tag).Take(take)
            .Select(row => new { row.Id, row.Tag, row.Name, row.SerialNumber, row.Status })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SearchHit(Kind, row.Id, row.Name, $"{row.Tag}{(row.SerialNumber is null ? string.Empty : " · " + row.SerialNumber)} · {row.Status}", $"/assets/{row.Id}",
            row.Tag.Equals(term, StringComparison.OrdinalIgnoreCase) || string.Equals(row.SerialNumber, term, StringComparison.OrdinalIgnoreCase) ? 1 : 0.45))];
    }
}

/// <summary>Bulk registration of assets into stock.</summary>
internal sealed class AssetImport(AppDbContext dbContext, AssetService assets) : IImportHandler
{
    public string Key => "assets";

    public string Title => "Assets";

    public string Description => "Register assets into stock; each gets a tag. Assign them afterwards from the register.";

    public string Permission => Permissions.Asset.Manage;

    public IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new("category", "Asset category code", true, "LAPTOP"),
        new("name", "Name", true, "Dell Latitude 7440"),
        new("make", "Make", false, "Dell"),
        new("model", "Model", false, "Latitude 7440"),
        new("serialNumber", "Serial number", false, "DL7440-2001"),
        new("purchaseDate", "Purchase date (yyyy-mm-dd)", false, "2026-04-01"),
        new("purchaseCost", "Purchase cost", false, "98000"),
        new("vendor", "Vendor", false, "Croma Business"),
        new("warrantyUntil", "Warranty until (yyyy-mm-dd)", false, "2029-03-31"),
    ];

    public async Task<ImportOutcome> RunAsync(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, bool commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Dictionary<string, AssetCategory> categories = await dbContext.AssetCategories.AsNoTracking().Where(row => row.IsActive).ToDictionaryAsync(row => row.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        HashSet<string> serials = new((await dbContext.Assets.AsNoTracking().Where(row => row.SerialNumber != null).Select(row => row.SerialNumber!).ToListAsync(cancellationToken)), StringComparer.OrdinalIgnoreCase);
        List<ImportRowResult> results = [];
        List<AssetDetails> valid = [];
        for (int index = 0; index < rows.Count; index++)
        {
            IReadOnlyDictionary<string, string> row = rows[index];
            List<string> errors = [];
            string? Text(string key) => string.IsNullOrWhiteSpace(row.GetValueOrDefault(key)) ? null : row[key].Trim();
            DateOnly? Date(string key)
            {
                if (Text(key) is not { } value)
                {
                    return null;
                }

                if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                {
                    return date;
                }

                errors.Add($"{key} must be yyyy-mm-dd.");
                return null;
            }

            categories.TryGetValue(Text("category") ?? string.Empty, out AssetCategory? category);
            if (category is null)
            {
                errors.Add($"No active category has the code '{Text("category")}'.");
            }

            string? name = Text("name");
            if (name is null || name.Length > 150)
            {
                errors.Add("Give a name of up to 150 characters.");
            }

            string? serial = Text("serialNumber");
            if (category is { RequiresSerial: true } && serial is null)
            {
                errors.Add($"{category.Name} needs a serial number.");
            }

            if (serial is not null && !serials.Add(serial))
            {
                errors.Add($"The serial number {serial} is already registered.");
            }

            decimal? cost = null;
            if (Text("purchaseCost") is { } costText)
            {
                if (decimal.TryParse(costText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) && parsed >= 0)
                {
                    cost = parsed;
                }
                else
                {
                    errors.Add("The purchase cost must be a number.");
                }
            }

            DateOnly? purchased = Date("purchaseDate");
            DateOnly? warranty = Date("warrantyUntil");
            if (errors.Count == 0)
            {
                valid.Add(new AssetDetails(category!.Id, name!, Text("make"), Text("model"), serial, purchased, cost, Text("vendor"), warranty, null, null));
            }

            results.Add(new ImportRowResult(index + 2, errors, errors.Count == 0 ? $"{name}{(serial is null ? string.Empty : " (" + serial + ")")}" : null));
        }

        int imported = 0;
        if (commit)
        {
            foreach (AssetDetails details in valid)
            {
                await assets.RegisterAsync(new AssetBody(details), cancellationToken);
                imported++;
            }
        }

        return new ImportOutcome(rows.Count, valid.Count, imported, results);
    }
}
