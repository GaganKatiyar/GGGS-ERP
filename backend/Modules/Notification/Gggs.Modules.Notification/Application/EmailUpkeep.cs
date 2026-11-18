using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Identity.Contracts;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Notification.Infrastructure;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Gggs.Modules.Notification.Application;

/// <summary>How long the Email log keeps emails and whether admins hear about failures, with the choices the page offers.</summary>
public sealed record EmailLogSettingsView(int KeepSentDays, int KeepProblemDays, bool AlertOnFailure, IReadOnlyList<int> SentChoices, IReadOnlyList<int> ProblemChoices);

public sealed record EmailLogSettingsBody(int KeepSentDays, int KeepProblemDays, bool AlertOnFailure);

/// <summary>What one clean-up removed.</summary>
public sealed record EmailCleanup(int SentEmails, int ProblemEmails, int TextMessages);

/// <summary>The Email log's keep-period and failure-notice rules, apart from the database so they are tested on their own.</summary>
public static class EmailLogRules
{
    /// <summary>The dates before which sent emails, and failed or unsent ones, are deleted. Keeping no sent email still keeps today's.</summary>
    public static (DateTime SentBefore, DateTime ProblemsBefore) Cutoffs(EmailLogSetting setting, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(setting);
        DateTime sentBefore = setting.KeepSentDays == 0 ? now.Date : now.AddDays(-setting.KeepSentDays);
        return (sentBefore, now.AddDays(-setting.KeepProblemDays));
    }

    /// <summary>The notice: how many failed, and the most common reason in plain words.</summary>
    public static (string Title, string Body) AlertText(IReadOnlyCollection<string?> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Count == 0)
        {
            throw new ArgumentException("There is nothing to tell about.", nameof(reasons));
        }

        EmailFailure common = reasons
            .Select(EmailFailures.Classify)
            .GroupBy(failure => failure.Kind)
            .OrderByDescending(group => group.Count())
            .First()
            .First();
        string title = reasons.Count == 1 ? "An email could not be sent" : $"{reasons.Count} emails could not be sent";
        string body = common.Fix is { } fix ? $"Most common reason: {common.Reason} {fix}" : $"Most common reason: {common.Reason}";
        return (title, body.Length > 4000 ? body[..4000] : body);
    }
}

/// <summary>
/// Keeps the Email log useful and small: deletes emails past the organisation's keep period, tries
/// temporary failures again, and tells the people who manage mail when emails fail for good.
/// </summary>
internal sealed class EmailUpkeep(
    AppDbContext dbContext,
    IIntegrationEventPublisher publisher,
    IUserAccounts accounts,
    TimeProvider timeProvider,
    ILogger<EmailUpkeep> logger)
{
    /// <summary>Rows per delete statement, so a first clean-up of a large log never holds a long lock.</summary>
    private const int DeleteBatch = 5000;

    /// <summary>Emails tried again per pass; more wait for the next one.</summary>
    private const int RetryBatch = 200;

    public const string AlertEventCode = "email.delivery-failed";

    private static readonly TimeSpan AlertEvery = TimeSpan.FromHours(1);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<EmailLogSettingsView> SettingsAsync(CancellationToken cancellationToken)
        => View(await dbContext.EmailLogSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? EmailLogSetting.CreateDefault());

    public async Task<EmailLogSettingsView> SaveSettingsAsync(EmailLogSettingsBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!EmailLogSetting.SentChoices.Contains(body.KeepSentDays) || !EmailLogSetting.ProblemChoices.Contains(body.KeepProblemDays))
        {
            throw new DomainRuleException("EMAIL_LOG_KEEP", "Choose one of the keep periods the page offers.");
        }

        EmailLogSetting setting = await TrackedSettingAsync(cancellationToken);
        setting.Set(body.KeepSentDays, body.KeepProblemDays, body.AlertOnFailure);
        await dbContext.SaveChangesAsync(cancellationToken);
        return View(setting);
    }

    /// <summary>
    /// Deletes for real, not softly: a soft delete frees no space, and the point is the space. Only the
    /// soft-delete filter is lifted; the tenant filter and row-level security still hold.
    /// </summary>
    public async Task<EmailCleanup> CleanUpAsync(CancellationToken cancellationToken)
    {
        EmailLogSetting setting = await dbContext.EmailLogSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? EmailLogSetting.CreateDefault();
        (DateTime sentBefore, DateTime problemsBefore) = EmailLogRules.Cutoffs(setting, Now);

        IQueryable<EmailMessage> emails = dbContext.EmailMessages.IgnoreQueryFilters([QueryFilters.SoftDelete]);
        int sent = await DeleteInBatchesAsync(emails.Where(email => email.Status == EmailStatus.Sent && email.SentDate < sentBefore), cancellationToken);
        int problems = await DeleteInBatchesAsync(
            emails.Where(email => (email.Status == EmailStatus.Failed || email.Status == EmailStatus.Logged || email.Status == EmailStatus.Paused)
                && email.NextAttemptAt == null
                && email.CreatedDate < problemsBefore),
            cancellationToken);

        // Text messages follow the same rules; they have no retry of their own.
        IQueryable<SmsMessage> texts = dbContext.SmsMessages.IgnoreQueryFilters([QueryFilters.SoftDelete]);
        int sms = 0;
        while (true)
        {
            List<Guid> batch = await texts
                .Where(text => (text.Status == SmsStatus.Sent && text.SentDate < sentBefore) || (text.Status == SmsStatus.Failed && text.CreatedDate < problemsBefore))
                .Select(text => text.Id)
                .Take(DeleteBatch)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            sms += await texts.Where(text => batch.Contains(text.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        if (sent + problems + sms > 0)
        {
            logger.LogInformation("Email log clean-up removed {Sent} sent emails, {Problems} failed or unsent ones and {Sms} text messages.", sent, problems, sms);
        }

        return new EmailCleanup(sent, problems, sms);
    }

    /// <summary>Puts temporary failures whose wait is over back in the queue, through the same path as Resend.</summary>
    public async Task<int> RetryDueAsync(CancellationToken cancellationToken)
    {
        DateTime now = Now;
        List<EmailMessage> due = await dbContext.EmailMessages
            .Where(email => email.Status == EmailStatus.Failed && email.NextAttemptAt != null && email.NextAttemptAt <= now)
            .OrderBy(email => email.NextAttemptAt)
            .Take(RetryBatch)
            .ToListAsync(cancellationToken);

        foreach (EmailMessage email in due.Where(email => email.CanResend))
        {
            email.Requeue();
            publisher.Publish(new EmailQueuedIntegrationEvent(email.Id));
        }

        if (due.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Tried {Count} emails again after a temporary failure.", due.Count);
        }

        return due.Count;
    }

    /// <summary>
    /// One in-app notice an hour at most, to everyone who can fix mail, when emails have failed for good
    /// since the last one. In-app only: if mail is what is broken, an email about it would not arrive.
    /// </summary>
    public async Task<int> AlertAsync(CancellationToken cancellationToken)
    {
        DateTime now = Now;
        EmailLogSetting? stored = await dbContext.EmailLogSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (stored is { AlertOnFailure: false } || (stored?.AlertedUpTo is { } last && now - last < AlertEvery))
        {
            return 0;
        }

        // The first time, only the last hour: older failures were there before anyone asked to be told.
        DateTime since = stored?.AlertedUpTo ?? now - AlertEvery;
        List<string?> reasons = await dbContext.EmailMessages.AsNoTracking()
            .Where(email => email.Status == EmailStatus.Failed && email.NextAttemptAt == null && (email.ModifiedDate ?? email.CreatedDate) > since && (email.ModifiedDate ?? email.CreatedDate) <= now)
            .Select(email => email.LastError)
            .ToListAsync(cancellationToken);

        int notified = 0;
        if (reasons.Count > 0)
        {
            IReadOnlyList<Guid> admins = await accounts.UserIdsWithPermissionAsync(Permissions.DataHub.IntegrationManage, cancellationToken);
            (string title, string body) = EmailLogRules.AlertText(reasons);
            foreach (Guid admin in admins)
            {
                dbContext.InAppNotifications.Add(InAppNotification.Create(null, admin, AlertEventCode, title, body, "/studio/email-log?status=Failed"));
            }

            notified = admins.Count;
            logger.LogWarning("{Count} emails failed for good; {Admins} people told.", reasons.Count, admins.Count);
        }

        EmailLogSetting setting = await TrackedSettingAsync(cancellationToken);
        setting.MarkAlerted(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return notified;
    }

    private static EmailLogSettingsView View(EmailLogSetting setting)
        => new(setting.KeepSentDays, setting.KeepProblemDays, setting.AlertOnFailure, EmailLogSetting.SentChoices, EmailLogSetting.ProblemChoices);

    private async Task<EmailLogSetting> TrackedSettingAsync(CancellationToken cancellationToken)
    {
        EmailLogSetting? setting = await dbContext.EmailLogSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting is null)
        {
            setting = EmailLogSetting.CreateDefault();
            dbContext.EmailLogSettings.Add(setting);
        }

        return setting;
    }

    private static async Task<int> DeleteInBatchesAsync(IQueryable<EmailMessage> rows, CancellationToken cancellationToken)
    {
        int total = 0;
        while (true)
        {
            List<Guid> batch = await rows.Select(email => email.Id).Take(DeleteBatch).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return total;
            }

            total += await rows.Where(email => batch.Contains(email.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }
}

/// <summary>Fans the nightly Email log clean-up out to every tenant.</summary>
[PlatformJob("Email log clean-up: fans out one keep-period clean-up per active tenant.")]
internal sealed class EmailLogCleanupSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "email-log-cleanup";

    public Task RunAsync() => fanOut.FanOutAsync<EmailLogCleanupJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class EmailLogCleanupJob(EmailUpkeep upkeep)
{
    public async Task RunAsync(Guid tenantId)
    {
        _ = tenantId;
        await upkeep.CleanUpAsync(CancellationToken.None);
    }
}

/// <summary>Fans the five-minute pass out to every tenant: temporary failures tried again, and the hourly failure notice.</summary>
[PlatformJob("Email retries: fans out one retry and failure-notice pass per active tenant.")]
internal sealed class EmailRetrySweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "email-retries";

    public Task RunAsync() => fanOut.FanOutAsync<EmailRetryJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class EmailRetryJob(EmailUpkeep upkeep)
{
    public async Task RunAsync(Guid tenantId)
    {
        _ = tenantId;
        await upkeep.RetryDueAsync(CancellationToken.None);
        await upkeep.AlertAsync(CancellationToken.None);
    }
}

internal sealed class EmailUpkeepJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<EmailLogCleanupSweep>(EmailLogCleanupSweep.RecurringJobName, sweep => sweep.RunAsync(), "50 2 * * *");
        recurringJobs.AddOrUpdate<EmailRetrySweep>(EmailRetrySweep.RecurringJobName, sweep => sweep.RunAsync(), "*/5 * * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
