using System.Text.Json;
using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Studio.Domain;

public enum ConditionOperator
{
    Equals = 1,
    NotEquals = 2,
    Contains = 3,
}

/// <summary>"Only if" one of the trigger's fields compares to a value; text compared without regard to case.</summary>
public sealed record AutomationCondition(string Field, ConditionOperator Operator, string Value);

/// <summary>
/// "When this happens, do that": a business event (the trigger), optional conditions on its
/// fields, and an operation run on one of the tenant's connections with parameters written as
/// templates over the trigger's fields - "{{employeeName}} joined {{department}}".
/// </summary>
public sealed class Automation : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Automation()
    {
        Name = null!;
        TriggerCode = null!;
        Operation = null!;
        ConditionsJson = "[]";
        ParametersJson = "{}";
    }

    public string Name { get; private set; }

    public string TriggerCode { get; private set; }

    public string ConditionsJson { get; private set; }

    public Guid ConnectionId { get; private set; }

    public string Operation { get; private set; }

    /// <summary>The operation's parameters by key, each a template over the trigger's fields.</summary>
    public string ParametersJson { get; private set; }

    public IReadOnlyList<AutomationCondition> Conditions => JsonSerializer.Deserialize<List<AutomationCondition>>(ConditionsJson, Json) ?? [];

    public IReadOnlyDictionary<string, string> Parameters => JsonSerializer.Deserialize<Dictionary<string, string>>(ParametersJson, Json) ?? [];

    public static Automation Create(string name, string triggerCode, IReadOnlyList<AutomationCondition> conditions, Guid connectionId, string operation, IReadOnlyDictionary<string, string> parameters)
    {
        var automation = new Automation();
        automation.Update(name, triggerCode, conditions, connectionId, operation, parameters);
        return automation;
    }

    public void Update(string name, string triggerCode, IReadOnlyList<AutomationCondition> conditions, Guid connectionId, string operation, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(parameters);
        Guard.Against(conditions.Count > 10, "AUTOMATION_CONDITIONS", "An automation has at most 10 conditions.");
        Guard.Against(conditions.Any(condition => string.IsNullOrWhiteSpace(condition.Field) || !Enum.IsDefined(condition.Operator)), "AUTOMATION_CONDITIONS", "Each condition needs a field and a comparison.");

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 120);
        TriggerCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(triggerCode).Trim(), 80);
        ConnectionId = Guard.NotEmpty(connectionId);
        Operation = Guard.MaxLength(Guard.NotNullOrWhiteSpace(operation).Trim(), 60);
        ConditionsJson = JsonSerializer.Serialize(conditions.Select(condition => condition with { Field = condition.Field.Trim(), Value = condition.Value?.Trim() ?? string.Empty }), Json);
        ParametersJson = JsonSerializer.Serialize(parameters.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)).ToDictionary(pair => pair.Key.Trim(), pair => pair.Value ?? string.Empty), Json);
        Guard.Against(ParametersJson.Length > 8000, "AUTOMATION_PARAMETERS", "The action's text is too long.");
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public enum DeliveryStatus
{
    Pending = 1,
    Succeeded = 2,

    /// <summary>Failed and will be tried again at <see cref="AutomationDelivery.NextAttemptAt"/>.</summary>
    Failed = 3,

    /// <summary>Failed every attempt; left for someone to look at and retry by hand.</summary>
    Abandoned = 4,
}

/// <summary>One automation acting on one event: the parameters it sent, and how each attempt went.</summary>
public sealed class AutomationDelivery : TenantAggregateRoot
{
    private AutomationDelivery()
    {
        TriggerCode = null!;
        PayloadJson = "{}";
    }

    public Guid AutomationId { get; private set; }

    /// <summary>The event's message id: an automation acts once on each event.</summary>
    public Guid EventId { get; private set; }

    public string TriggerCode { get; private set; }

    /// <summary>The rendered parameters, as sent.</summary>
    public string PayloadJson { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastMessage { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public static AutomationDelivery Queue(Guid automationId, Guid eventId, string triggerCode, IReadOnlyDictionary<string, string> payload, DateTime now) => new()
    {
        AutomationId = Guard.NotEmpty(automationId),
        EventId = eventId,
        TriggerCode = Guard.MaxLength(triggerCode, 80),
        PayloadJson = JsonSerializer.Serialize(payload),
        Status = DeliveryStatus.Pending,
        NextAttemptAt = now,
    };

    public IReadOnlyDictionary<string, string> Payload => JsonSerializer.Deserialize<Dictionary<string, string>>(PayloadJson) ?? [];

    public bool IsDue(DateTime now) => Status is DeliveryStatus.Pending or DeliveryStatus.Failed && NextAttemptAt is { } next && next <= now;

    /// <summary>Records an attempt: done, or failed with the next try scheduled until the attempts run out.</summary>
    public void Record(bool succeeded, string message, DateTime now)
    {
        Attempts++;
        LastMessage = string.IsNullOrWhiteSpace(message) ? null : Guard.MaxLength(message.Trim(), 500);
        if (succeeded)
        {
            Status = DeliveryStatus.Succeeded;
            NextAttemptAt = null;
            CompletedAt = now;
            return;
        }

        TimeSpan? wait = AutomationRules.RetryDelay(Attempts);
        Status = wait is null ? DeliveryStatus.Abandoned : DeliveryStatus.Failed;
        NextAttemptAt = wait is { } delay ? now + delay : null;
        CompletedAt = wait is null ? now : null;
    }

    /// <summary>Gives up without another attempt, when there is nothing left to send it with.</summary>
    public void Abandon(string message, DateTime now)
    {
        Status = DeliveryStatus.Abandoned;
        LastMessage = Guard.MaxLength(message, 500);
        NextAttemptAt = null;
        CompletedAt = now;
    }

    /// <summary>Tries again now, whatever happened before; the attempt count carries on.</summary>
    public void RetryNow(DateTime now)
    {
        if (Status == DeliveryStatus.Succeeded)
        {
            throw new DomainRuleException("DELIVERY_DONE", "This delivery already succeeded.");
        }

        Status = DeliveryStatus.Pending;
        NextAttemptAt = now;
        CompletedAt = null;
    }
}

/// <summary>The rules automations follow, kept free of storage so they can be tested on their own.</summary>
public static partial class AutomationRules
{
    /// <summary>After the first attempt fails: 1, 5, 15 and 60 minutes, then give up. Five attempts in all.</summary>
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    public const int MaxAttempts = 5;

    public static TimeSpan? RetryDelay(int attemptsSoFar) => attemptsSoFar >= 1 && attemptsSoFar <= Backoff.Length ? Backoff[attemptsSoFar - 1] : null;

    /// <summary>Every condition holds; a field the event does not have counts as empty.</summary>
    public static bool Matches(IEnumerable<AutomationCondition> conditions, IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(fields);
        return conditions.All(condition =>
        {
            string actual = fields.TryGetValue(condition.Field, out string? value) ? value : string.Empty;
            return condition.Operator switch
            {
                ConditionOperator.Equals => string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
                ConditionOperator.NotEquals => !string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
                ConditionOperator.Contains => actual.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        });
    }

    /// <summary>Fills "{{field}}" placeholders; an unknown field becomes empty rather than showing its braces.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return string.IsNullOrEmpty(template)
            ? string.Empty
            : Placeholder().Replace(template, match => fields.TryGetValue(match.Groups[1].Value, out string? value) ? value : string.Empty);
    }

    public static IReadOnlyDictionary<string, string> RenderAll(IReadOnlyDictionary<string, string> templates, IReadOnlyDictionary<string, string> fields)
        => templates.ToDictionary(pair => pair.Key, pair => Render(pair.Value, fields), StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_.]*)\s*\}\}")]
    private static partial Regex Placeholder();
}
