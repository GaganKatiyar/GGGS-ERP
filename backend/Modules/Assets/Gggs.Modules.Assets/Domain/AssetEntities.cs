using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Assets.Domain;

/// <summary>A kind of asset (laptop, monitor, phone, access card) and how long it depreciates over.</summary>
public sealed class AssetCategory : TenantAggregateRoot
{
    private AssetCategory()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Straight-line depreciation period; zero for items that do not depreciate.</summary>
    public int DepreciationYears { get; private set; }

    public bool RequiresSerial { get; private set; }

    public static AssetCategory Create(string code, string name, int depreciationYears, bool requiresSerial)
    {
        var category = new AssetCategory();
        category.Update(code, name, depreciationYears, requiresSerial, active: true);
        return category;
    }

    public void Update(string code, string name, int depreciationYears, bool requiresSerial, bool active)
    {
        if (depreciationYears is < 0 or > 20)
        {
            throw new DomainRuleException("ASSET_CATEGORY_DEPRECIATION", "Depreciation runs over 0 to 20 years.");
        }

        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        DepreciationYears = depreciationYears;
        RequiresSerial = requiresSerial;
        IsActive = active;
    }
}

public enum AssetStatus
{
    InStock = 1,
    Assigned = 2,
    InRepair = 3,
    Retired = 4,
    Lost = 5,
}

public enum AssetCondition
{
    New = 1,
    Good = 2,
    Fair = 3,
    Poor = 4,
    Damaged = 5,
}

public sealed record AssetDetails(
    Guid CategoryId,
    string Name,
    string? Make,
    string? Model,
    string? SerialNumber,
    DateOnly? PurchaseDate,
    decimal? PurchaseCost,
    string? Vendor,
    DateOnly? WarrantyUntil,
    Guid? LocationId,
    string? Notes);

/// <summary>
/// A company asset through its whole life (spec: assets): bought into stock, assigned and returned,
/// sent for repair, and retired or written off - with straight-line book value along the way.
/// </summary>
public sealed class Asset : TenantAggregateRoot
{
    private Asset()
    {
        Tag = null!;
        Name = null!;
    }

    public string Tag { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; }

    public string? Make { get; private set; }

    public string? Model { get; private set; }

    public string? SerialNumber { get; private set; }

    public DateOnly? PurchaseDate { get; private set; }

    public decimal? PurchaseCost { get; private set; }

    public string? Vendor { get; private set; }

    public DateOnly? WarrantyUntil { get; private set; }

    public Guid? LocationId { get; private set; }

    public string? Notes { get; private set; }

    public AssetStatus Status { get; private set; }

    public AssetCondition Condition { get; private set; }

    public Guid? AssignedEmployeeId { get; private set; }

    public DateOnly? AssignedOn { get; private set; }

    public static Asset Register(string tag, AssetDetails details, AssetCondition condition)
    {
        var asset = new Asset { Tag = Guard.MaxLength(Guard.NotNullOrWhiteSpace(tag), 30), Status = AssetStatus.InStock, Condition = condition };
        asset.Update(details);
        return asset;
    }

    public void Update(AssetDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.PurchaseCost < 0)
        {
            throw new DomainRuleException("ASSET_COST", "The purchase cost cannot be negative.");
        }

        CategoryId = Guard.NotEmpty(details.CategoryId);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(details.Name).Trim(), 200);
        Make = Optional(details.Make, 100);
        Model = Optional(details.Model, 100);
        SerialNumber = Optional(details.SerialNumber, 100);
        PurchaseDate = details.PurchaseDate;
        PurchaseCost = details.PurchaseCost;
        Vendor = Optional(details.Vendor, 200);
        WarrantyUntil = details.WarrantyUntil;
        LocationId = details.LocationId;
        Notes = Optional(details.Notes, 2000);
    }

    public void Assign(Guid employeeId, DateOnly on)
    {
        if (Status != AssetStatus.InStock)
        {
            throw new DomainRuleException("ASSET_STATE", $"Only an asset in stock can be assigned; this one is {Describe(Status)}.");
        }

        Status = AssetStatus.Assigned;
        AssignedEmployeeId = Guard.NotEmpty(employeeId);
        AssignedOn = on;
    }

    public void Return(AssetCondition condition)
    {
        if (Status != AssetStatus.Assigned)
        {
            throw new DomainRuleException("ASSET_STATE", "The asset is not assigned to anyone.");
        }

        Status = condition == AssetCondition.Damaged ? AssetStatus.InRepair : AssetStatus.InStock;
        Condition = condition;
        AssignedEmployeeId = null;
        AssignedOn = null;
    }

    public void SendForRepair()
    {
        if (Status is not (AssetStatus.InStock or AssetStatus.Assigned))
        {
            throw new DomainRuleException("ASSET_STATE", $"The asset is {Describe(Status)}.");
        }

        Status = AssetStatus.InRepair;
        AssignedEmployeeId = null;
        AssignedOn = null;
    }

    public void Repaired(AssetCondition condition)
    {
        if (Status != AssetStatus.InRepair)
        {
            throw new DomainRuleException("ASSET_STATE", "The asset is not in repair.");
        }

        Status = AssetStatus.InStock;
        Condition = condition;
    }

    public void Retire(bool lost)
    {
        if (Status is AssetStatus.Retired or AssetStatus.Lost)
        {
            throw new DomainRuleException("ASSET_STATE", "The asset is already off the register.");
        }

        Status = lost ? AssetStatus.Lost : AssetStatus.Retired;
        AssignedEmployeeId = null;
        AssignedOn = null;
    }

    /// <summary>Straight-line book value on a day: the cost spread evenly over the depreciation years, never below zero.</summary>
    public decimal BookValue(DateOnly on, int depreciationYears)
    {
        if (PurchaseCost is not { } cost || Status is AssetStatus.Retired or AssetStatus.Lost)
        {
            return 0;
        }

        if (depreciationYears == 0 || PurchaseDate is not { } bought)
        {
            return cost;
        }

        int months = Math.Max(0, ((on.Year - bought.Year) * 12) + on.Month - bought.Month);
        decimal remaining = cost - (cost * months / (depreciationYears * 12m));
        return Math.Round(Math.Max(0, remaining), 2);
    }

    private static string Describe(AssetStatus status) => status switch
    {
        AssetStatus.InStock => "in stock",
        AssetStatus.InRepair => "in repair",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), max);
}

public enum AssetEventKind
{
    Registered = 1,
    Assigned = 2,
    Returned = 3,
    Repair = 4,
    Repaired = 5,
    Retired = 6,
    Lost = 7,
    Note = 8,
}

/// <summary>One thing that happened to an asset - its history.</summary>
public sealed class AssetEvent : TenantAggregateRoot
{
    private AssetEvent()
    {
    }

    public Guid AssetId { get; private set; }

    public AssetEventKind Kind { get; private set; }

    public DateOnly On { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public Guid? ByEmployeeId { get; private set; }

    public decimal? Cost { get; private set; }

    public string? Note { get; private set; }

    public static AssetEvent Record(Guid assetId, AssetEventKind kind, DateOnly on, Guid? employeeId, Guid? byEmployeeId, decimal? cost, string? note) => new()
    {
        AssetId = Guard.NotEmpty(assetId),
        Kind = kind,
        On = on,
        EmployeeId = employeeId,
        ByEmployeeId = byEmployeeId,
        Cost = cost is < 0 ? throw new DomainRuleException("ASSET_COST", "A cost cannot be negative.") : cost,
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 1000),
    };
}

public enum AssetRequestStatus
{
    Requested = 1,
    Fulfilled = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>An employee asking for equipment: fulfilled by assigning an asset from stock.</summary>
public sealed class AssetRequest : TenantAggregateRoot
{
    private AssetRequest()
    {
        Reason = null!;
    }

    public Guid EmployeeId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Reason { get; private set; }

    public AssetRequestStatus Status { get; private set; }

    public Guid? AssetId { get; private set; }

    public string? Response { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    public static AssetRequest Raise(Guid employeeId, Guid categoryId, string reason) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        CategoryId = Guard.NotEmpty(categoryId),
        Reason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 1000),
        Status = AssetRequestStatus.Requested,
    };

    public void Fulfil(Guid assetId, DateOnly today)
    {
        EnsureOpen();
        Status = AssetRequestStatus.Fulfilled;
        AssetId = assetId;
        ClosedOn = today;
    }

    public void Close(bool cancelled, string? response, DateOnly today)
    {
        EnsureOpen();
        if (!cancelled && string.IsNullOrWhiteSpace(response))
        {
            throw new DomainRuleException("ASSET_REQUEST_REASON", "Say why the request is turned down.");
        }

        Status = cancelled ? AssetRequestStatus.Cancelled : AssetRequestStatus.Rejected;
        Response = string.IsNullOrWhiteSpace(response) ? null : Guard.MaxLength(response.Trim(), 1000);
        ClosedOn = today;
    }

    private void EnsureOpen()
    {
        if (Status != AssetRequestStatus.Requested)
        {
            throw new DomainRuleException("ASSET_REQUEST_STATE", $"The request is {Status.ToString().ToLowerInvariant()}.");
        }
    }
}

/// <summary>The categories every tenant starts with.</summary>
public static class AssetDefaults
{
    public static IReadOnlyList<(string Code, string Name, int Years, bool Serial)> Categories { get; } =
    [
        ("LAPTOP", "Laptop", 3, true),
        ("MONITOR", "Monitor", 5, true),
        ("PHONE", "Mobile phone", 2, true),
        ("HEADSET", "Headset", 2, false),
        ("ACCESS_CARD", "Access card", 0, false),
        ("FURNITURE", "Furniture", 10, false),
    ];
}
