using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Insight.Domain;

public enum DigestFrequency
{
    Weekly = 1,
    Monthly = 2,
}

/// <summary>A person's request to receive a dashboard's headline figures by notification on a schedule.</summary>
public sealed class ReportSubscription : TenantAggregateRoot
{
    private ReportSubscription()
    {
        Area = null!;
    }

    public Guid UserId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    /// <summary>A metric area, or "executive".</summary>
    public string Area { get; private set; }

    public DigestFrequency Frequency { get; private set; }

    public DateOnly? LastSentOn { get; private set; }

    public static ReportSubscription Create(Guid userId, Guid? employeeId, string area, DigestFrequency frequency)
    {
        if (!Enum.IsDefined(frequency))
        {
            throw new DomainRuleException("DIGEST_FREQUENCY", "Choose weekly or monthly.");
        }

        return new ReportSubscription { UserId = Guard.NotEmpty(userId), EmployeeId = employeeId, Area = Guard.MaxLength(Guard.NotNullOrWhiteSpace(area), 30), Frequency = frequency };
    }

    public void Change(DigestFrequency frequency) => Frequency = frequency;

    /// <summary>Weekly digests go on Mondays, monthly ones on the first; never twice in one day.</summary>
    public bool IsDue(DateOnly today) => LastSentOn != today && (Frequency == DigestFrequency.Weekly ? today.DayOfWeek == DayOfWeek.Monday : today.Day == 1);

    public void Sent(DateOnly today) => LastSentOn = today;
}

/// <summary>A record of one bulk import, for the history and the audit.</summary>
public sealed class ImportRun : TenantAggregateRoot
{
    private ImportRun()
    {
        HandlerKey = null!;
        FileName = null!;
    }

    public string HandlerKey { get; private set; }

    public string FileName { get; private set; }

    public int Rows { get; private set; }

    public int Valid { get; private set; }

    public int Imported { get; private set; }

    public bool Committed { get; private set; }

    public Guid? ByUserId { get; private set; }

    public static ImportRun Record(string handlerKey, string? fileName, int rows, int valid, int imported, bool committed, Guid? byUserId) => new()
    {
        HandlerKey = Guard.MaxLength(handlerKey, 60),
        FileName = Guard.MaxLength(string.IsNullOrWhiteSpace(fileName) ? "upload.csv" : fileName.Trim(), 200),
        Rows = rows,
        Valid = valid,
        Imported = imported,
        Committed = committed,
        ByUserId = byUserId,
    };
}

/// <summary>
/// A tenant's configured connection to an outside system through one adapter. Plain settings are
/// stored as they are; secret settings only encrypted, and never returned.
/// </summary>
public sealed class IntegrationConnection : TenantAggregateRoot
{
    private IntegrationConnection()
    {
        AdapterKey = null!;
        Name = null!;
        SettingsJson = "{}";
    }

    public string AdapterKey { get; private set; }

    public string Name { get; private set; }

    /// <summary>Settings by key; secret values hold ciphertext.</summary>
    public string SettingsJson { get; private set; }

    public DateTime? LastTestedAt { get; private set; }

    public bool? LastTestSucceeded { get; private set; }

    public string? LastMessage { get; private set; }

    public static IntegrationConnection Create(string adapterKey, string name, string settingsJson)
    {
        var connection = new IntegrationConnection { AdapterKey = Guard.MaxLength(Guard.NotNullOrWhiteSpace(adapterKey), 60) };
        connection.Update(name, settingsJson, active: true);
        return connection;
    }

    public void Update(string name, string settingsJson, bool active)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        SettingsJson = Guard.NotNullOrWhiteSpace(settingsJson);
        IsActive = active;
    }

    public void Tested(bool succeeded, string message, DateTime now)
    {
        LastTestedAt = now;
        LastTestSucceeded = succeeded;
        LastMessage = Guard.MaxLength(message, 500);
    }
}

/// <summary>One run of an integration operation: what was asked, how it went, how many records.</summary>
public sealed class IntegrationRun : TenantAggregateRoot
{
    private IntegrationRun()
    {
        Operation = null!;
        Message = null!;
    }

    public Guid ConnectionId { get; private set; }

    public string Operation { get; private set; }

    public bool Succeeded { get; private set; }

    public string Message { get; private set; }

    public int Records { get; private set; }

    public string? FileName { get; private set; }

    public Guid? ByUserId { get; private set; }

    public static IntegrationRun Record(Guid connectionId, string operation, bool succeeded, string message, int records, string? fileName, Guid? byUserId) => new()
    {
        ConnectionId = connectionId,
        Operation = Guard.MaxLength(operation, 60),
        Succeeded = succeeded,
        Message = Guard.MaxLength(message, 500),
        Records = records,
        FileName = fileName is null ? null : Guard.MaxLength(fileName, 200),
        ByUserId = byUserId,
    };
}
