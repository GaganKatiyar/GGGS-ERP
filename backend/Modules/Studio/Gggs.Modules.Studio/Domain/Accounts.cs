using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Studio.Domain;

/// <summary>What a directory connection does by itself. Both off until someone chooses: this creates and disables real accounts.</summary>
public sealed class ProvisioningSetting : TenantAggregateRoot
{
    private ProvisioningSetting()
    {
    }

    public Guid ConnectionId { get; private set; }

    public bool ProvisionOnHire { get; private set; }

    public bool DisableOnExit { get; private set; }

    public static ProvisioningSetting Create(Guid connectionId, bool provisionOnHire, bool disableOnExit) => new()
    {
        ConnectionId = Guard.NotEmpty(connectionId),
        ProvisionOnHire = provisionOnHire,
        DisableOnExit = disableOnExit,
    };

    public void Set(bool provisionOnHire, bool disableOnExit)
    {
        ProvisionOnHire = provisionOnHire;
        DisableOnExit = disableOnExit;
    }
}

public enum AccountStatus
{
    /// <summary>Wanted, not yet made.</summary>
    Creating = 1,
    Active = 2,

    /// <summary>Should be shut off, not yet done.</summary>
    Disabling = 3,
    Disabled = 4,
}

/// <summary>
/// One person's account in one outside directory, and where getting it made or shut off has reached.
/// Only ever disabled, never deleted: mail and files in the account stay for the organisation to deal with.
/// </summary>
public sealed class ProvisionedAccount : TenantAggregateRoot
{
    private ProvisionedAccount()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid ConnectionId { get; private set; }

    /// <summary>The directory's own id for the account, which disabling it goes by.</summary>
    public string? ExternalId { get; private set; }

    public string? Login { get; private set; }

    public AccountStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastMessage { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    public DateTime? DisabledAt { get; private set; }

    /// <summary>Tries have run out, and it waits for someone. On a leaver this means an account still open.</summary>
    public bool NeedsAttention => Status is AccountStatus.Creating or AccountStatus.Disabling && NextAttemptAt is null;

    public static ProvisionedAccount Request(Guid employeeId, Guid connectionId, DateTime firstAttemptAt) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        ConnectionId = Guard.NotEmpty(connectionId),
        Status = AccountStatus.Creating,
        NextAttemptAt = firstAttemptAt,
    };

    public void MarkCreated(string externalId, string login, string? message)
    {
        ExternalId = Guard.MaxLength(Guard.NotNullOrWhiteSpace(externalId), 200);
        Login = Guard.MaxLength(Guard.NotNullOrWhiteSpace(login), 256);
        Status = AccountStatus.Active;
        Attempts = 0;
        NextAttemptAt = null;
        Note(message);
    }

    /// <summary>Asks for it to be shut off. With no account made there is nothing to shut off, so it simply stops trying.</summary>
    public void RequestDisable(DateTime now, DateTime firstAttemptAt)
    {
        switch (Status)
        {
            case AccountStatus.Active:
            case AccountStatus.Disabling when NeedsAttention:
                Status = AccountStatus.Disabling;
                Attempts = 0;
                NextAttemptAt = firstAttemptAt;
                break;
            case AccountStatus.Creating:
                Status = AccountStatus.Disabled;
                NextAttemptAt = null;
                DisabledAt = now;
                Note("No account had been created, so there was nothing to disable.");
                break;
        }
    }

    public void MarkDisabled(string? message, DateTime now)
    {
        Status = AccountStatus.Disabled;
        NextAttemptAt = null;
        DisabledAt = now;
        Note(message);
    }

    /// <summary>A failed try: the next is scheduled until the tries run out.</summary>
    public void RecordFailure(string message, DateTime now)
    {
        Attempts++;
        Note(message);
        NextAttemptAt = AutomationRules.RetryDelay(Attempts) is { } wait ? now + wait : null;
    }

    public void Note(string? message)
        => LastMessage = string.IsNullOrWhiteSpace(message) ? null : Guard.MaxLength(message.Trim(), 500);

    public void RetryNow(DateTime now)
    {
        if (Status is AccountStatus.Active or AccountStatus.Disabled)
        {
            throw new DomainRuleException("ACCOUNT_SETTLED", "There is nothing left to try on this account.");
        }

        NextAttemptAt = now;
    }
}
