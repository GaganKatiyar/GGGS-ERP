using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Notification.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gggs.Modules.Notification.Application;

/// <summary>A group of notifications that can have its own sender, found by the start of the template code.</summary>
public sealed record EmailModule(string Key, string Name, string Description, IReadOnlyList<string> Prefixes, string Color);

/// <summary>
/// The modules the Email senders page lists. A template belongs to the module whose prefix starts its code
/// (<c>reward.recognized</c> is Star Reward); a code no module claims is "Everything else".
/// </summary>
public static class EmailModules
{
    public const string DefaultKey = "default";
    public const string OtherKey = "other";
    public const string DefaultColor = "#b0102a";

    public static IReadOnlyList<EmailModule> All { get; } =
    [
        new("identity", "Login & account", "Invitations to set a password, new accounts and password resets.", ["identity"], "#334155"),
        new("onboarding", "Onboarding", "The welcome email to a joiner, and onboarding tasks for buddies, managers and IT.", ["onboarding"], "#0f766e"),
        new("recruitment", "Recruitment", "Interview schedules and referral updates.", ["recruitment"], "#6d28d9"),
        new("appraisal", "Appraisal", "Review cycles, feedback requests, released reviews, improvement plans and promotions.", ["performance", "career"], "#1d4ed8"),
        new("reward", "Star Reward", "Recognitions, redemption updates and points about to expire.", ["reward"], "#b45309"),
        new("engagement", "Celebrations & engagement", "Birthday and work-anniversary wishes, manager notices and surveys.", ["engagement"], "#be185d"),
        new("payroll", "Payroll & finance", "Payslips, salary revisions, loans, expenses and benefits.", ["payroll", "compensation", "loan", "expense", "benefit"], "#15803d"),
        new("time", "Leave & timesheets", "Timesheet reminders. Leave approvals go through Approvals.", ["timesheet", "leave", "attendance"], "#0369a1"),
        new("approvals", "Approvals", "Approval tasks, escalations and decisions for every workflow.", ["workflow"], "#4338ca"),
        new("exits", "Exits", "Resignations, clearances, offboarding tasks, notices and settlements.", ["separation"], "#9f1239"),
        new(OtherKey, "Everything else", "Projects, helpdesk, learning, documents, assets, compliance and reports.", [], DefaultColor),
    ];

    public static string KeyFor(string eventCode)
    {
        ArgumentNullException.ThrowIfNull(eventCode);
        int dot = eventCode.IndexOf('.', StringComparison.Ordinal);
        string prefix = dot < 0 ? eventCode : eventCode[..dot];
        return All.FirstOrDefault(module => module.Prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))?.Key ?? OtherKey;
    }

    public static bool IsKnown(string key) => key == DefaultKey || All.Any(module => module.Key == key);

    public static EmailModule? Find(string key) => All.FirstOrDefault(module => module.Key == key);

    public static string NameOf(string? key) => key == DefaultKey ? "Default sender" : (key is null ? null : Find(key))?.Name ?? "Everything else";
}

/// <summary>Everything one email needs from its sender, after a module's blanks are filled from the default sender.</summary>
public sealed record ResolvedSender(
    string Key,
    string ModuleName,
    Guid? ConnectionId,
    string Title,
    string? FromName,
    string? FromAddress,
    string? ReplyTo,
    string? Bcc,
    string? Signature,
    string Color,
    bool IsPaused)
{
    public string TextBody(string body) => Signature is null ? body : $"{body}\n\n{Signature}";

    public OutgoingEmail Compose(string to, string subject, string body)
        => new(to, subject, TextBody(body), EmailLayout.Html(Title, Color, body, Signature), FromName, FromAddress, ReplyTo, Bcc);

    /// <summary>"GGGS Star Rewards &lt;rewards@acme.example&gt; via Company Gmail", for the Email log.</summary>
    public string Describe(string via)
    {
        string? who = (FromName, FromAddress) switch
        {
            ({ } name, { } address) => $"{name} <{address}>",
            ({ } name, null) => name,
            (null, { } address) => address,
            _ => null,
        };
        return who is null ? $"via {via}" : $"{who} via {via}";
    }
}

/// <summary>Works out who an email comes from: the module's own sender, filled in from the default sender.</summary>
public sealed class EmailSenderResolver(AppDbContext dbContext, IOptions<NotificationOptions> options)
{
    public async Task<ResolvedSender> ResolveAsync(string key, CancellationToken cancellationToken = default)
    {
        string wanted = EmailModules.IsKnown(key) ? key : EmailModules.OtherKey;
        List<EmailSender> rows = await dbContext.EmailSenders.AsNoTracking()
            .Where(row => row.ModuleKey == wanted || row.ModuleKey == EmailModules.DefaultKey)
            .ToListAsync(cancellationToken);
        EmailSender? fallback = rows.FirstOrDefault(row => row.ModuleKey == EmailModules.DefaultKey);
        EmailSender? own = wanted == EmailModules.DefaultKey ? null : rows.FirstOrDefault(row => row.ModuleKey == wanted);
        return Merge(wanted, own, fallback, options.Value.FromName);
    }

    /// <summary>
    /// A blank takes the default sender's value - except the address when the module has its own connection:
    /// the default's address is one the default connection may send as, not necessarily this one.
    /// </summary>
    public static ResolvedSender Merge(string key, EmailSender? own, EmailSender? fallback, string systemName)
    {
        EmailModule? module = EmailModules.Find(key);
        bool ownConnection = own?.ConnectionId is not null;
        string? fromName = own?.FromName ?? fallback?.FromName;
        return new ResolvedSender(
            key,
            EmailModules.NameOf(key),
            own?.ConnectionId ?? fallback?.ConnectionId,
            fromName ?? systemName,
            fromName,
            ownConnection ? own!.FromAddress : own?.FromAddress ?? fallback?.FromAddress,
            own?.ReplyTo ?? fallback?.ReplyTo,
            own?.Bcc ?? fallback?.Bcc,
            own?.Signature ?? fallback?.Signature,
            own?.BrandColor ?? fallback?.BrandColor ?? module?.Color ?? EmailModules.DefaultColor,
            own?.IsPaused == true);
    }
}

/// <summary>
/// The HTML version of an email: the sender's name on a band of its colour, the message, and the signature.
/// Everything the template or a person wrote is encoded, so a placeholder's value can never add markup.
/// </summary>
public static partial class EmailLayout
{
    public static string Html(string title, string color, string body, string? signature)
    {
        string band = HexColor().IsMatch(color) ? color : EmailModules.DefaultColor;
        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f1f5f9;font-family:'Segoe UI',Arial,sans-serif;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\">");
        html.Append("<table role=\"presentation\" width=\"560\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;width:100%;background:#ffffff;border-radius:8px;\">");
        html.Append("<tr><td style=\"background:").Append(band).Append(";color:#ffffff;padding:18px 24px;font-size:18px;font-weight:bold;border-radius:8px 8px 0 0;\">")
            .Append(Encode(title)).Append("</td></tr>");
        html.Append("<tr><td style=\"padding:24px;font-size:14px;line-height:1.6;color:#1e293b;\">").Append(Paragraphs(body)).Append("</td></tr>");
        if (!string.IsNullOrWhiteSpace(signature))
        {
            html.Append("<tr><td style=\"padding:16px 24px;border-top:1px solid #e2e8f0;font-size:13px;line-height:1.5;color:#475569;\">").Append(Paragraphs(signature)).Append("</td></tr>");
        }

        html.Append("</table></td></tr></table></body></html>");
        return html.ToString();
    }

    /// <summary>Encoded text with its line breaks kept and its web addresses made into links.</summary>
    private static string Paragraphs(string text)
        => Link().Replace(Encode(text).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal), match => $"<a href=\"{match.Value}\" style=\"color:#1d4ed8;\">{match.Value}</a>");

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    [GeneratedRegex(@"https?://[^\s<""]+", RegexOptions.CultureInvariant)]
    private static partial Regex Link();
}

public sealed record EmailSenderItem(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<string> Covers,
    int Templates,
    bool HasOwn,
    Guid? ConnectionId,
    string? FromName,
    string? FromAddress,
    string? ReplyTo,
    string? Bcc,
    string? Signature,
    string? BrandColor,
    bool IsPaused,
    string ModuleColor,
    string? Problem);

/// <summary>The Email senders page: every sender, the connections they can use, and whether the system's own sender delivers.</summary>
public sealed record EmailSendersView(IReadOnlyList<EmailSenderItem> Senders, IReadOnlyList<EmailConnectionOption> Connections, Guid? RouteConnectionId, bool SystemSenderDelivers);

public sealed record EmailSenderBody(Guid? ConnectionId, string? FromName, string? FromAddress, string? ReplyTo, string? Bcc, string? Signature, string? BrandColor, bool IsPaused = false);

public sealed record EmailSenderTestBody(string To);

public sealed record EmailLogQuery
{
    public string? Status { get; init; }

    public string? Module { get; init; }

    public string? Search { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

public sealed record EmailLogItem(
    Guid Id,
    DateTime CreatedDate,
    string To,
    string Subject,
    string EventCode,
    string ModuleKey,
    string ModuleName,
    EmailStatus Status,
    string? SentAs,
    string? Reason,
    int Attempts,
    DateTime? SentDate,
    bool CanResend,
    EmailFailureKind? FailureKind = null,
    string? Problem = null,
    string? Fix = null,
    DateTime? NextAttemptAt = null);

/// <summary>How many failed emails went back in the queue, and how many could not (a masked one-time notice).</summary>
public sealed record ResendFailedResult(int Requeued, int Skipped);

/// <summary>A page of the Email log, with how many emails each status has under the same module and search.</summary>
public sealed record EmailLogPage(PagedResult<EmailLogItem> Page, IReadOnlyDictionary<string, int> Counts);

/// <summary>
/// The Email senders page and the Email log: who each module's emails come from, a test through that sender,
/// and every email with what happened to it. Bodies are never listed: an invitation's holds a sign-in link.
/// </summary>
internal sealed partial class EmailSenderService(
    AppDbContext dbContext,
    ITenantMessageSender messageSender,
    EmailSenderResolver resolver,
    IIntegrationEventPublisher publisher,
    IOptions<NotificationOptions> options)
{
    public async Task<EmailSendersView> ListAsync(CancellationToken cancellationToken)
    {
        EmailConnections connections = await messageSender.EmailConnectionsAsync(cancellationToken);
        HashSet<Guid> usable = [.. connections.Options.Where(option => option.IsActive).Select(option => option.Id)];
        bool systemDelivers = string.Equals(options.Value.EmailProvider, "Smtp", StringComparison.OrdinalIgnoreCase);

        Dictionary<string, EmailSender> rows = await dbContext.EmailSenders.AsNoTracking().ToDictionaryAsync(row => row.ModuleKey, cancellationToken);
        Dictionary<string, int> templates = (await dbContext.NotificationTemplates.AsNoTracking().Select(template => template.Code).ToListAsync(cancellationToken))
            .GroupBy(EmailModules.KeyFor)
            .ToDictionary(group => group.Key, group => group.Count());

        EmailSender? fallback = rows.GetValueOrDefault(EmailModules.DefaultKey);
        Guid? chosen = fallback?.ConnectionId ?? connections.RouteConnectionId;
        string? defaultProblem = chosen is { } id && usable.Contains(id)
            ? null
            : chosen is not null
                ? systemDelivers ? "Its connection is switched off or was deleted, so the system's sender is used." : "Its connection is switched off or was deleted, so email is only written to the server log."
                : systemDelivers ? null : "No mail connection is chosen, so email is only written to the server log.";

        List<EmailSenderItem> items =
        [
            Item(EmailModules.DefaultKey, "Default sender", "Used by every module without a sender of its own.", [], templates.Values.Sum(), fallback, EmailModules.DefaultColor, defaultProblem),
        ];
        foreach (EmailModule module in EmailModules.All)
        {
            EmailSender? own = rows.GetValueOrDefault(module.Key);
            string? problem = own?.ConnectionId is { } ownConnection && !usable.Contains(ownConnection) ? "Its connection is switched off or was deleted, so the default sender's is used." : null;
            items.Add(Item(module.Key, module.Name, module.Description, module.Prefixes, templates.GetValueOrDefault(module.Key), own, module.Color, problem));
        }

        return new EmailSendersView(items, connections.Options, connections.RouteConnectionId, systemDelivers);
    }

    public async Task<EmailSendersView> SaveAsync(string key, EmailSenderBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string module = Known(key);
        if (body.ConnectionId is { } connectionId && !(await messageSender.EmailConnectionsAsync(cancellationToken)).Options.Any(option => option.Id == connectionId))
        {
            throw new DomainRuleException("EMAIL_SENDER_CONNECTION", "That connection cannot send email.");
        }

        foreach ((string? address, string label) in new[] { (body.FromAddress, "Send from"), (body.ReplyTo, "Replies go to"), (body.Bcc, "Also copy") })
        {
            if (!string.IsNullOrWhiteSpace(address) && !MailAddress.TryCreate(address.Trim(), out _))
            {
                throw new DomainRuleException("EMAIL_SENDER_ADDRESS", $"{label}: \"{address.Trim()}\" is not an email address.");
            }
        }

        if (!string.IsNullOrWhiteSpace(body.BrandColor) && !HexColor().IsMatch(body.BrandColor.Trim()))
        {
            throw new DomainRuleException("EMAIL_SENDER_COLOR", "The header colour must look like #b0102a.");
        }

        if (body.IsPaused && module == EmailModules.DefaultKey)
        {
            throw new DomainRuleException("EMAIL_SENDER_PAUSE", "The default sender cannot be paused. Pause the modules one by one.");
        }

        EmailSender? sender = await dbContext.EmailSenders.FirstOrDefaultAsync(row => row.ModuleKey == module, cancellationToken);
        if (sender is null)
        {
            sender = EmailSender.Create(module);
            dbContext.EmailSenders.Add(sender);
        }

        sender.Set(body.ConnectionId, body.FromName, body.FromAddress, body.ReplyTo, body.Bcc, body.Signature, body.BrandColor, body.IsPaused);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ListAsync(cancellationToken);
    }

    /// <summary>The module goes back to the default sender; for the default sender itself, back to Studio's route.</summary>
    public async Task<EmailSendersView> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        string module = Known(key);
        if (await dbContext.EmailSenders.FirstOrDefaultAsync(row => row.ModuleKey == module, cancellationToken) is { } sender)
        {
            dbContext.EmailSenders.Remove(sender);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await ListAsync(cancellationToken);
    }

    /// <summary>Sends one real email through the sender as it is saved, so the person sees what its emails look like.</summary>
    public async Task<ChannelSendResult> TestAsync(string key, EmailSenderTestBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string module = Known(key);
        if (string.IsNullOrWhiteSpace(body.To) || !MailAddress.TryCreate(body.To.Trim(), out _))
        {
            throw new DomainRuleException("EMAIL_SENDER_TEST", "Say which address the test should go to.");
        }

        ResolvedSender sender = await resolver.ResolveAsync(module, cancellationToken);
        OutgoingEmail email = sender.Compose(
            body.To.Trim(),
            $"Test email: {sender.ModuleName}",
            $"This test came from Nuts and Bolts. Emails for {sender.ModuleName} look like this one.");
        EmailSendResult result = await messageSender.SendEmailAsync(email, sender.ConnectionId, cancellationToken)
            ?? throw new DomainRuleException("EMAIL_SENDER_NONE", "Choose a mail connection that is switched on first.");
        return new ChannelSendResult(result.Succeeded, result.Succeeded ? $"Sent through {result.ConnectionName}." : result.Message);
    }

    public async Task<EmailLogPage> LogAsync(EmailLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<EmailMessage> rows = dbContext.EmailMessages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            string module = query.Module.Trim().ToLowerInvariant();
            rows = rows.Where(row => row.ModuleKey == module);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            rows = rows.Where(row => row.ToAddress.Contains(term) || row.Subject.Contains(term));
        }

        Dictionary<string, int> counts = (await rows.GroupBy(row => row.Status).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(row => row.Key.ToString(), row => row.Count);

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse(query.Status, ignoreCase: true, out EmailStatus status))
        {
            rows = rows.Where(row => row.Status == status);
        }

        PagedResult<EmailMessage> page = await rows
            .OrderByDescending(row => row.CreatedDate)
            .ToPagedResultAsync(new PageRequest { Page = query.Page ?? 1, PageSize = query.PageSize ?? PageRequest.DefaultPageSize }, row => row, cancellationToken);

        return new EmailLogPage(
            new PagedResult<EmailLogItem>
            {
                Items = [.. page.Items.Select(Log)],
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
            },
            counts);
    }

    /// <summary>Puts a failed, logged or paused email back in the queue; it goes with today's sender settings.</summary>
    public async Task<EmailLogItem> ResendAsync(Guid id, CancellationToken cancellationToken)
    {
        EmailMessage email = await dbContext.EmailMessages.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(EmailMessage), id);
        if (!email.CanResend)
        {
            throw new DomainRuleException(
                "EMAIL_RESEND",
                email.IsMasked ? "This email held a one-time secret, so it cannot be sent again. Ask for a new one." : "Only a failed, logged or paused email can be sent again.");
        }

        email.Requeue();
        publisher.Publish(new EmailQueuedIntegrationEvent(email.Id));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Log(email);
    }

    /// <summary>
    /// After the cause is fixed (a reconnected account, say): every failed email that can go again, in the
    /// module filter if one is chosen, back in the queue. Those still waiting for an automatic retry go now.
    /// </summary>
    public async Task<ResendFailedResult> ResendFailedAsync(string? moduleKey, CancellationToken cancellationToken)
    {
        IQueryable<EmailMessage> rows = dbContext.EmailMessages.Where(row => row.Status == EmailStatus.Failed);
        if (!string.IsNullOrWhiteSpace(moduleKey))
        {
            string module = moduleKey.Trim().ToLowerInvariant();
            rows = rows.Where(row => row.ModuleKey == module);
        }

        List<EmailMessage> failed = await rows.OrderBy(row => row.CreatedDate).Take(ResendAllLimit).ToListAsync(cancellationToken);
        int requeued = 0;
        foreach (EmailMessage email in failed.Where(email => email.CanResend))
        {
            email.Requeue();
            publisher.Publish(new EmailQueuedIntegrationEvent(email.Id));
            requeued++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ResendFailedResult(requeued, failed.Count - requeued);
    }

    /// <summary>Emails one Resend all puts back at most; press it again for the rest.</summary>
    private const int ResendAllLimit = 500;

    private static EmailLogItem Log(EmailMessage row)
    {
        string module = row.ModuleKey ?? EmailModules.KeyFor(row.EventCode);
        EmailFailure? failure = row.Status == EmailStatus.Failed ? EmailFailures.Classify(row.LastError) : null;
        return new EmailLogItem(
            row.Id, row.CreatedDate, row.ToAddress, row.Subject, row.EventCode, module, EmailModules.NameOf(module), row.Status, row.SentAs, row.LastError, row.Attempts, row.SentDate, row.CanResend,
            failure?.Kind, failure?.Reason, failure?.Fix, row.NextAttemptAt);
    }

    private static EmailSenderItem Item(string key, string name, string description, IReadOnlyList<string> covers, int templates, EmailSender? row, string color, string? problem)
        => new(key, name, description, covers, templates, row is not null, row?.ConnectionId, row?.FromName, row?.FromAddress, row?.ReplyTo, row?.Bcc, row?.Signature, row?.BrandColor, row?.IsPaused == true, color, problem);

    private static string Known(string key)
    {
        string module = (key ?? string.Empty).Trim().ToLowerInvariant();
        return EmailModules.IsKnown(module) ? module : throw new NotFoundException("Email sender", key ?? string.Empty);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
