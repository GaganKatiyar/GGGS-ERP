using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Studio.Domain;
using Gggs.Modules.Studio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Studio.Application;

public sealed record TriggerItem(string Code, string Label, string Category, string Description, IReadOnlyList<TriggerField> Fields);

public sealed record AutomationBody(
    string Name,
    string TriggerCode,
    IReadOnlyList<AutomationCondition>? Conditions,
    Guid ConnectionId,
    string Operation,
    IReadOnlyDictionary<string, string>? Parameters,
    bool IsActive = true);

public sealed record AutomationItem(
    Guid Id,
    string Name,
    string TriggerCode,
    string TriggerLabel,
    IReadOnlyList<AutomationCondition> Conditions,
    Guid ConnectionId,
    string ConnectionName,
    string AdapterKey,
    string Operation,
    string OperationLabel,
    IReadOnlyDictionary<string, string> Parameters,
    bool IsActive,
    DeliveryStatus? LastStatus,
    DateTime? LastDeliveredAt);

/// <summary>A test send: the parameters as they were filled from the trigger's sample, and how it went.</summary>
public sealed record AutomationTestOutcome(bool Succeeded, string Message, IReadOnlyDictionary<string, string> Parameters);

public sealed record DeliveryItem(
    Guid Id,
    Guid AutomationId,
    string AutomationName,
    string TriggerCode,
    DeliveryStatus Status,
    int Attempts,
    string? LastMessage,
    DateTime? NextAttemptAt,
    DateTime? CompletedAt,
    DateTime CreatedDate,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>Automations as people set them up: the trigger catalogue, the automations themselves, test sends and the delivery log.</summary>
internal sealed class AutomationService(AppDbContext dbContext, IIntegrationConnections connections, AutomationEngine engine, TimeProvider timeProvider)
{
    public static IReadOnlyList<TriggerItem> Triggers()
        => [.. StudioTriggers.All.Select(trigger => new TriggerItem(trigger.Code, trigger.Label, trigger.Category, trigger.Description, trigger.Fields))];

    public Task<IReadOnlyList<ConnectionOption>> ConnectionsAsync(CancellationToken cancellationToken) => connections.ListAsync(cancellationToken);

    public async Task<IReadOnlyList<AutomationItem>> ListAsync(CancellationToken cancellationToken)
    {
        List<Automation> rows = await dbContext.Automations.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken);
        List<Guid> ids = [.. rows.Select(row => row.Id)];
        var last = (await dbContext.AutomationDeliveries.AsNoTracking()
                .Where(row => ids.Contains(row.AutomationId))
                .GroupBy(row => row.AutomationId)
                .Select(group => group.OrderByDescending(row => row.CreatedDate).Select(row => new { row.AutomationId, row.Status, row.CreatedDate }).First())
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.AutomationId);
        IReadOnlyList<ConnectionOption> options = await connections.ListAsync(cancellationToken);
        return [.. rows.Select(row => Item(row, options, last.TryGetValue(row.Id, out var latest) ? (latest.Status, latest.CreatedDate) : null))];
    }

    public async Task<AutomationItem> SaveAsync(Guid? id, AutomationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        IReadOnlyList<ConnectionOption> options = await connections.ListAsync(cancellationToken);
        Validate(body, options);

        IReadOnlyList<AutomationCondition> conditions = body.Conditions ?? [];
        IReadOnlyDictionary<string, string> parameters = body.Parameters ?? new Dictionary<string, string>();
        Automation automation;
        if (id is { } existing)
        {
            automation = await dbContext.Automations.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(Automation), existing);
            automation.Update(body.Name, body.TriggerCode, conditions, body.ConnectionId, body.Operation, parameters);
        }
        else
        {
            automation = Automation.Create(body.Name, body.TriggerCode, conditions, body.ConnectionId, body.Operation, parameters);
            dbContext.Automations.Add(automation);
        }

        if (body.IsActive)
        {
            automation.Activate();
        }
        else
        {
            automation.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(automation, options, null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        Automation automation = await dbContext.Automations.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Automation), id);
        dbContext.Automations.Remove(automation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Sends once with the trigger's sample fields, without saving anything but the connection's run log.</summary>
    public async Task<AutomationTestOutcome> TestAsync(AutomationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Validate(body, await connections.ListAsync(cancellationToken));
        TriggerDefinition trigger = StudioTriggers.Find(body.TriggerCode)!;
        IReadOnlyDictionary<string, string> parameters = AutomationRules.RenderAll(body.Parameters ?? new Dictionary<string, string>(), trigger.SampleFields);
        ConnectionRunResult result = await connections.RunAsync(body.ConnectionId, body.Operation, parameters, cancellationToken);
        return new AutomationTestOutcome(result.Succeeded, result.Message, parameters);
    }

    public async Task<IReadOnlyList<DeliveryItem>> DeliveriesAsync(Guid? automationId, DeliveryStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<AutomationDelivery> rows = dbContext.AutomationDeliveries.AsNoTracking();
        if (automationId is { } automation)
        {
            rows = rows.Where(row => row.AutomationId == automation);
        }

        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        List<AutomationDelivery> page = await rows.OrderByDescending(row => row.CreatedDate).Take(200).ToListAsync(cancellationToken);
        List<Guid> ids = [.. page.Select(row => row.AutomationId).Distinct()];
        Dictionary<Guid, string> names = await dbContext.Automations.AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        return [.. page.Select(row => Item(row, names.GetValueOrDefault(row.AutomationId) ?? "Deleted automation"))];
    }

    /// <summary>Tries a failed or abandoned delivery again now, and says how it went.</summary>
    public async Task<DeliveryItem> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        AutomationDelivery delivery = await dbContext.AutomationDeliveries.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(AutomationDelivery), id);
        Automation? automation = await dbContext.Automations.AsNoTracking().FirstOrDefaultAsync(row => row.Id == delivery.AutomationId, cancellationToken);
        delivery.RetryNow(timeProvider.GetUtcNow().UtcDateTime);
        await engine.AttemptAsync(delivery, automation, cancellationToken);
        return Item(delivery, automation?.Name ?? "Deleted automation");
    }

    private static void Validate(AutomationBody body, IReadOnlyList<ConnectionOption> options)
    {
        TriggerDefinition trigger = StudioTriggers.Find(body.TriggerCode ?? string.Empty)
            ?? throw new DomainRuleException("AUTOMATION_TRIGGER", "Choose what the automation starts from.");
        ConnectionOption connection = options.FirstOrDefault(option => option.Id == body.ConnectionId)
            ?? throw new DomainRuleException("AUTOMATION_CONNECTION", "Choose a connection for the automation to act through.");
        IntegrationOperation operation = connection.Operations.FirstOrDefault(candidate => candidate.Key == body.Operation)
            ?? throw new DomainRuleException("AUTOMATION_OPERATION", $"{connection.AdapterName} cannot do that; choose one of its actions.");

        HashSet<string> known = [.. trigger.Fields.Select(field => field.Key)];
        if ((body.Conditions ?? []).FirstOrDefault(condition => !known.Contains(condition.Field)) is { } unknown)
        {
            throw new DomainRuleException("AUTOMATION_CONDITIONS", $"\"{unknown.Field}\" is not one of {trigger.Label}'s fields.");
        }

        Dictionary<string, string> parameters = new(body.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        if (operation.Parameters.FirstOrDefault(parameter => parameter.Required && string.IsNullOrWhiteSpace(parameters.GetValueOrDefault(parameter.Key))) is { } missing)
        {
            throw new DomainRuleException("AUTOMATION_PARAMETERS", $"{missing.Label} is required.");
        }
    }

    private static AutomationItem Item(Automation row, IReadOnlyList<ConnectionOption> options, (DeliveryStatus Status, DateTime At)? last)
    {
        ConnectionOption? connection = options.FirstOrDefault(option => option.Id == row.ConnectionId);
        return new AutomationItem(
            row.Id,
            row.Name,
            row.TriggerCode,
            StudioTriggers.Find(row.TriggerCode)?.Label ?? row.TriggerCode,
            row.Conditions,
            row.ConnectionId,
            connection?.Name ?? "Removed connection",
            connection?.AdapterKey ?? string.Empty,
            row.Operation,
            connection?.Operations.FirstOrDefault(operation => operation.Key == row.Operation)?.Label ?? row.Operation,
            row.Parameters,
            row.IsActive,
            last?.Status,
            last?.At);
    }

    private static DeliveryItem Item(AutomationDelivery row, string automationName) => new(
        row.Id, row.AutomationId, automationName, row.TriggerCode, row.Status, row.Attempts, row.LastMessage, row.NextAttemptAt, row.CompletedAt, row.CreatedDate, row.Payload);
}
