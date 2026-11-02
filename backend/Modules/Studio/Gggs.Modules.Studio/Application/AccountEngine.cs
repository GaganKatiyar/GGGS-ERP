using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Studio.Domain;
using Gggs.Modules.Studio.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Studio.Application;

public sealed record AccountConnectionItem(Guid ConnectionId, string Name, string AdapterName, bool IsActive, bool ProvisionOnHire, bool DisableOnExit);

public sealed record AccountConnectionBody(bool ProvisionOnHire, bool DisableOnExit);

public sealed record RequestAccountBody(Guid EmployeeId, Guid ConnectionId);

public sealed record AccountItem(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeCode,
    Guid ConnectionId,
    string ConnectionName,
    string? Login,
    AccountStatus Status,
    bool NeedsAttention,
    int Attempts,
    string? LastMessage,
    DateTime? NextAttemptAt,
    DateTime? DisabledAt,
    DateTime CreatedDate);

/// <summary>
/// Directory accounts as people manage them: what each connection does by itself, the list, and asking
/// for an account to be made, shut off or tried again. The worker does the work within a minute, because
/// sending the first-sign-in email belongs to it.
/// </summary>
internal sealed class AccountService(AppDbContext dbContext, IIntegrationConnections connections, IEmployeeDirectory employees, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<AccountConnectionItem>> ConnectionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ConnectionOption> options = await connections.AccountConnectionsAsync(cancellationToken);
        Dictionary<Guid, ProvisioningSetting> settings = await dbContext.ProvisioningSettings.AsNoTracking().ToDictionaryAsync(row => row.ConnectionId, cancellationToken);
        return [.. options.Select(option =>
        {
            ProvisioningSetting? setting = settings.GetValueOrDefault(option.Id);
            return new AccountConnectionItem(option.Id, option.Name, option.AdapterName, option.IsActive, setting?.ProvisionOnHire ?? false, setting?.DisableOnExit ?? false);
        })];
    }

    public async Task<IReadOnlyList<AccountConnectionItem>> SetConnectionAsync(Guid connectionId, AccountConnectionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!(await connections.AccountConnectionsAsync(cancellationToken)).Any(option => option.Id == connectionId))
        {
            throw new NotFoundException("Connection", connectionId);
        }

        ProvisioningSetting? setting = await dbContext.ProvisioningSettings.FirstOrDefaultAsync(row => row.ConnectionId == connectionId, cancellationToken);
        if (setting is null)
        {
            dbContext.ProvisioningSettings.Add(ProvisioningSetting.Create(connectionId, body.ProvisionOnHire, body.DisableOnExit));
        }
        else
        {
            setting.Set(body.ProvisionOnHire, body.DisableOnExit);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ConnectionsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountItem>> ListAsync(Guid? employeeId, CancellationToken cancellationToken)
    {
        IQueryable<ProvisionedAccount> rows = dbContext.ProvisionedAccounts.AsNoTracking();
        if (employeeId is { } person)
        {
            rows = rows.Where(row => row.EmployeeId == person);
        }

        List<ProvisionedAccount> page = await rows.OrderByDescending(row => row.CreatedDate).Take(200).ToListAsync(cancellationToken);
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(page.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(row => row.Id);
        Dictionary<Guid, string> names = (await connections.AccountConnectionsAsync(cancellationToken)).ToDictionary(option => option.Id, option => option.Name);
        return [.. page.Select(row => Item(row, people.GetValueOrDefault(row.EmployeeId), names.GetValueOrDefault(row.ConnectionId)))];
    }

    /// <summary>For someone who joined before the connection was set up, or while it was switched off.</summary>
    public async Task<AccountItem> RequestAsync(RequestAccountBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ConnectionOption connection = (await connections.AccountConnectionsAsync(cancellationToken)).FirstOrDefault(option => option.Id == body.ConnectionId)
            ?? throw new DomainRuleException("ACCOUNT_CONNECTION", "That connection cannot create accounts.");
        if (!connection.IsActive)
        {
            throw new DomainRuleException("ACCOUNT_CONNECTION", "That connection is switched off.");
        }

        EmployeeSummary person = await employees.GetAsync(body.EmployeeId, cancellationToken) ?? throw new NotFoundException("Employee", body.EmployeeId);
        if (await dbContext.ProvisionedAccounts.AnyAsync(row => row.EmployeeId == body.EmployeeId && row.ConnectionId == body.ConnectionId, cancellationToken))
        {
            throw new DomainRuleException("ACCOUNT_EXISTS", $"{person.DisplayName} already has an account through {connection.Name}.");
        }

        ProvisionedAccount account = ProvisionedAccount.Request(body.EmployeeId, body.ConnectionId, timeProvider.GetUtcNow().UtcDateTime);
        dbContext.ProvisionedAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(account, person, connection.Name);
    }

    public Task<AccountItem> DisableAsync(Guid id, CancellationToken cancellationToken)
        => ChangeAsync(id, account =>
        {
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            account.RequestDisable(now, now);
        }, cancellationToken);

    public Task<AccountItem> RetryAsync(Guid id, CancellationToken cancellationToken)
        => ChangeAsync(id, account => account.RetryNow(timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

    private static AccountItem Item(ProvisionedAccount row, EmployeeSummary? person, string? connectionName) => new(
        row.Id, row.EmployeeId, person?.DisplayName ?? "Unknown employee", person?.EmployeeCode ?? string.Empty, row.ConnectionId, connectionName ?? "Removed connection",
        row.Login, row.Status, row.NeedsAttention, row.Attempts, row.LastMessage, row.NextAttemptAt, row.DisabledAt, row.CreatedDate);

    private async Task<AccountItem> ChangeAsync(Guid id, Action<ProvisionedAccount> change, CancellationToken cancellationToken)
    {
        ProvisionedAccount account = await dbContext.ProvisionedAccounts.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ProvisionedAccount), id);
        change(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(account, await employees.GetAsync(account.EmployeeId, cancellationToken), (await connections.AccountConnectionsAsync(cancellationToken)).FirstOrDefault(option => option.Id == account.ConnectionId)?.Name);
    }
}

/// <summary>
/// Makes and shuts off accounts in the worker. A temporary password exists only inside one attempt: it
/// goes to the directory and to the person's personal email, and is never stored, queued or logged.
/// </summary>
internal sealed class AccountEngine(
    AppDbContext dbContext,
    IEmployeeAccounts holders,
    IIntegrationConnections connections,
    IOneTimeNotices notices,
    TimeProvider timeProvider,
    ILogger<AccountEngine> logger)
{
    public async Task OnHiredAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        List<Guid> wanted = await dbContext.ProvisioningSettings.AsNoTracking().Where(row => row.ProvisionOnHire).Select(row => row.ConnectionId).ToListAsync(cancellationToken);
        if (wanted.Count == 0)
        {
            return;
        }

        HashSet<Guid> active = [.. (await connections.AccountConnectionsAsync(cancellationToken)).Where(option => option.IsActive).Select(option => option.Id)];
        HashSet<Guid> existing = [.. await dbContext.ProvisionedAccounts.AsNoTracking().Where(row => row.EmployeeId == employeeId).Select(row => row.ConnectionId).ToListAsync(cancellationToken)];

        // Tried right here; the sweep only steps in if this attempt never finishes.
        DateTime fallback = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2);
        List<ProvisionedAccount> accounts = [.. wanted.Where(id => active.Contains(id) && !existing.Contains(id)).Select(id => ProvisionedAccount.Request(employeeId, id, fallback))];
        if (accounts.Count == 0)
        {
            return;
        }

        dbContext.ProvisionedAccounts.AddRange(accounts);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (ProvisionedAccount account in accounts)
        {
            await AttemptAsync(account, cancellationToken);
        }
    }

    public async Task OnExitedAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        HashSet<Guid> shutting = [.. await dbContext.ProvisioningSettings.AsNoTracking().Where(row => row.DisableOnExit).Select(row => row.ConnectionId).ToListAsync(cancellationToken)];
        if (shutting.Count == 0)
        {
            return;
        }

        List<ProvisionedAccount> accounts = await dbContext.ProvisionedAccounts.Where(row => row.EmployeeId == employeeId && shutting.Contains(row.ConnectionId)).ToListAsync(cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (ProvisionedAccount account in accounts)
        {
            account.RequestDisable(now, now.AddMinutes(2));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (ProvisionedAccount account in accounts.Where(row => row.Status == AccountStatus.Disabling))
        {
            await AttemptAsync(account, cancellationToken);
        }
    }

    public async Task<int> RetryDueAsync(CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        List<ProvisionedAccount> due = await dbContext.ProvisionedAccounts
            .Where(row => (row.Status == AccountStatus.Creating || row.Status == AccountStatus.Disabling) && row.NextAttemptAt <= now)
            .OrderBy(row => row.NextAttemptAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        foreach (ProvisionedAccount account in due)
        {
            await AttemptAsync(account, cancellationToken);
        }

        return due.Count;
    }

    private async Task AttemptAsync(ProvisionedAccount account, CancellationToken cancellationToken)
    {
        if (account.Status == AccountStatus.Disabling)
        {
            ConnectionRunResult result = await connections.DisableAccountAsync(account.ConnectionId, account.ExternalId ?? string.Empty, cancellationToken);
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            if (result.Succeeded)
            {
                account.MarkDisabled(result.Message, now);
            }
            else
            {
                logger.LogWarning("Account {AccountId} could not be disabled: {Message}", account.Id, result.Message);
                account.RecordFailure(result.Message, now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (await holders.GetAsync(account.EmployeeId, cancellationToken) is not { } holder)
        {
            account.RecordFailure("The employee record could not be found.", timeProvider.GetUtcNow().UtcDateTime);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        string password = TemporaryPasswords.Generate();
        AccountOutcome outcome = await connections.CreateAccountAsync(
            account.ConnectionId,
            new AccountRequest(holder.EmployeeCode, holder.FirstName, holder.LastName, holder.DisplayName, holder.OfficialEmail, password),
            cancellationToken);
        if (!outcome.Succeeded || outcome.ExternalId is null || outcome.Login is null)
        {
            logger.LogWarning("Account {AccountId} could not be created: {Message}", account.Id, outcome.Message);
            account.RecordFailure(outcome.Message, timeProvider.GetUtcNow().UtcDateTime);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Saved before anything else can go wrong: from here on the account exists, and the record must say so.
        account.MarkCreated(outcome.ExternalId, outcome.Login, outcome.Message);
        await dbContext.SaveChangesAsync(cancellationToken);

        await holders.SetOfficialEmailIfEmptyAsync(account.EmployeeId, outcome.Login, cancellationToken);
        if (outcome.Created)
        {
            account.Note($"{outcome.Message} {await SendPasswordAsync(holder, outcome.Login, password, cancellationToken)}");
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Says how handing over the password went; someone has to reset it in the directory if it did not.</summary>
    private async Task<string> SendPasswordAsync(AccountHolder holder, string login, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(holder.PersonalEmail))
        {
            return "No personal email is on record, so reset the password in the directory to give it to them.";
        }

        ChannelSendResult sent = await notices.SendAsync(
            NotificationTemplates.AccountCreated,
            holder.PersonalEmail,
            new Dictionary<string, string> { ["recipientName"] = holder.DisplayName, ["login"] = login, ["temporaryPassword"] = password },
            new HashSet<string>(StringComparer.Ordinal) { "temporaryPassword" },
            cancellationToken);
        return sent.Succeeded
            ? "The first sign-in details went to their personal email."
            : "The email with the first sign-in details could not be sent, so reset the password in the directory to give it to them.";
    }
}

/// <summary>Accounts follow people in and out, for the connections switched on to do so.</summary>
internal sealed class ProvisionAccounts(AccountEngine engine) :
    IIntegrationEventConsumer<EmployeeHiredIntegrationEvent>,
    IIntegrationEventConsumer<EmployeeExitedIntegrationEvent>
{
    public Task HandleAsync(EmployeeHiredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return engine.OnHiredAsync(integrationEvent.EmployeeId, cancellationToken);
    }

    public Task HandleAsync(EmployeeExitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return engine.OnExitedAsync(integrationEvent.EmployeeId, cancellationToken);
    }
}
