using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Notification.Domain;

/// <summary>
/// The wording of one kind of notification, per tenant. Seeded from the catalog; the tenant may
/// reword it and choose its channels, but not invent codes - a template only matters if some
/// module sends it.
/// </summary>
public sealed class NotificationTemplate : TenantAggregateRoot
{
    private NotificationTemplate()
    {
        Code = null!;
        Name = null!;
        Subject = null!;
        Body = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    public bool SendInApp { get; private set; }

    public bool SendEmail { get; private set; }

    /// <summary>Off until someone chooses it: a text message costs money and reaches a personal phone.</summary>
    public bool SendSms { get; private set; }

    public static NotificationTemplate Create(string code, string name, string subject, string body, bool sendInApp, bool sendEmail) => new()
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToLowerInvariant(), 64),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 200),
        Subject = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subject).Trim(), 300),
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), 4000),
        SendInApp = sendInApp,
        SendEmail = sendEmail,
    };

    /// <summary>
    /// Another module's sender for this one email, chosen on the Email templates page. Null - nearly
    /// always - means the sender of the module the template belongs to.
    /// </summary>
    public string? SenderKey { get; private set; }

    public void Update(string subject, string body, bool sendInApp, bool sendEmail, bool sendSms, bool isActive)
    {
        Subject = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subject).Trim(), 300);
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), 4000);
        SendInApp = sendInApp;
        SendEmail = sendEmail;
        SendSms = sendSms;
        IsActive = isActive;
    }

    public void UseSender(string? senderKey)
        => SenderKey = string.IsNullOrWhiteSpace(senderKey) ? null : Guard.MaxLength(senderKey.Trim().ToLowerInvariant(), 32);
}

/// <summary>One notification in someone's in-app inbox.</summary>
public sealed class InAppNotification : TenantAggregateRoot
{
    private InAppNotification()
    {
        EventCode = null!;
        Title = null!;
        Body = null!;
    }

    public Guid? RecipientEmployeeId { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public string EventCode { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public string? Link { get; private set; }

    public DateTime? ReadDate { get; private set; }

    public static InAppNotification Create(Guid? employeeId, Guid? userId, string eventCode, string title, string body, string? link)
    {
        if (employeeId is null && userId is null)
        {
            throw new ArgumentException("An in-app notification needs an employee or a user to belong to.", nameof(employeeId));
        }

        return new InAppNotification
        {
            RecipientEmployeeId = employeeId,
            RecipientUserId = userId,
            EventCode = Guard.MaxLength(eventCode, 64),
            Title = Guard.MaxLength(title, 300),
            Body = Guard.MaxLength(body, 4000),
            Link = string.IsNullOrWhiteSpace(link) ? null : Guard.MaxLength(link, 300),
        };
    }

    public void MarkRead(DateTime now) => ReadDate ??= now;
}

public enum EmailStatus
{
    Queued = 1,
    Sent = 2,
    Failed = 3,

    /// <summary>Written to the server log and not sent: no mail connection could carry it (development's sender).</summary>
    Logged = 4,

    /// <summary>Not sent because email is paused for its module; the in-app notice still went.</summary>
    Paused = 5,
}

/// <summary>
/// An email on its way out, kept as a record of what was sent to whom. Queued in the same
/// transaction as the change that caused it; delivered by the worker.
/// </summary>
public sealed class EmailMessage : TenantAggregateRoot
{
    private EmailMessage()
    {
        ToAddress = null!;
        Subject = null!;
        Body = null!;
        EventCode = null!;
    }

    public string ToAddress { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    public string EventCode { get; private set; }

    public EmailStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTime? SentDate { get; private set; }

    /// <summary>The module the email belongs to (see <c>EmailModules</c>), for the Email log's filter.</summary>
    public string? ModuleKey { get; private set; }

    /// <summary>Who it went out as, e.g. "GGGS Star Rewards &lt;rewards@acme.example&gt; via Company Gmail".</summary>
    public string? SentAs { get; private set; }

    /// <summary>A one-time notice whose stored copy has its secret masked: it can never be sent again from here.</summary>
    public bool IsMasked { get; private set; }

    /// <summary>
    /// When a temporary failure is tried again by itself (the retry sweep). Null for a sent email and for a
    /// failure that is final: an account or address problem, or a temporary one whose retries are used up.
    /// </summary>
    public DateTime? NextAttemptAt { get; private set; }

    public bool CanResend => !IsMasked && Status is EmailStatus.Failed or EmailStatus.Logged or EmailStatus.Paused;

    /// <summary>Failed, and no retry is coming: someone has to act.</summary>
    public bool FailedForGood => Status == EmailStatus.Failed && NextAttemptAt is null;

    public static EmailMessage Queue(string toAddress, string subject, string body, string eventCode, string? moduleKey = null) => new()
    {
        ToAddress = Guard.MaxLength(Guard.NotNullOrWhiteSpace(toAddress).Trim(), 256),
        Subject = Guard.MaxLength(subject, 300),
        Body = Guard.MaxLength(body, 8000),
        EventCode = Guard.MaxLength(eventCode, 64),
        ModuleKey = moduleKey is null ? null : Guard.MaxLength(moduleKey, 32),
        Status = EmailStatus.Queued,
    };

    public void MarkMasked() => IsMasked = true;

    /// <summary>
    /// The body goes once the email is out: the log never shows it (an invitation's holds a sign-in link) and
    /// a sent email cannot be resent, so keeping it would only fill the database.
    /// </summary>
    public void MarkSent(DateTime now, string? sentAs = null)
    {
        Attempts++;
        Status = EmailStatus.Sent;
        SentDate = now;
        LastError = null;
        NextAttemptAt = null;
        Body = string.Empty;
        SentAs = Cut(sentAs, 300);
    }

    /// <summary>
    /// With <paramref name="now"/>, a temporary failure is scheduled to be tried again (see
    /// <see cref="EmailFailures.RetryWaits"/>); without it, as for a one-time notice whose secret is masked,
    /// the failure is final at once.
    /// </summary>
    public void MarkFailed(string error, string? sentAs = null, DateTime? now = null)
    {
        Attempts++;
        Status = EmailStatus.Failed;
        LastError = Cut(error, 1000);
        SentAs = Cut(sentAs, 300) ?? SentAs;
        NextAttemptAt = now is { } at && !IsMasked && EmailFailures.Classify(error).Kind == EmailFailureKind.Temporary && EmailFailures.RetryWait(Attempts) is { } wait
            ? at + wait
            : null;
    }

    public void MarkLogged(string reason, string? sentAs = null)
    {
        Attempts++;
        Status = EmailStatus.Logged;
        LastError = Cut(reason, 1000);
        SentAs = Cut(sentAs, 300);
    }

    public void MarkPaused(string reason)
    {
        Status = EmailStatus.Paused;
        LastError = Cut(reason, 1000);
    }

    /// <summary>Back in the queue, to be delivered with today's sender settings.</summary>
    public void Requeue()
    {
        if (!CanResend)
        {
            throw new InvalidOperationException("Only a failed, logged or paused email can be sent again.");
        }

        Status = EmailStatus.Queued;
        LastError = null;
        NextAttemptAt = null;
    }

    private static string? Cut(string? text, int length) => text is null ? null : text.Length > length ? text[..length] : text;
}

/// <summary>
/// How long the Email log keeps emails, and whether admins hear about failures; one row per organisation,
/// none meaning the defaults. Sent emails are the bulk of the log and can go as soon as the night after;
/// failed, logged-only and paused ones are what someone needs to see, so they are always kept a while.
/// </summary>
public sealed class EmailLogSetting : TenantAggregateRoot
{
    public const int DefaultKeepSentDays = 30;
    public const int DefaultKeepProblemDays = 30;

    /// <summary>0 keeps no sent email past the night it went: only problems stay in the log.</summary>
    public static IReadOnlyList<int> SentChoices { get; } = [0, 30, 60, 90];

    public static IReadOnlyList<int> ProblemChoices { get; } = [30, 60, 90];

    private EmailLogSetting()
    {
    }

    public int KeepSentDays { get; private set; } = DefaultKeepSentDays;

    public int KeepProblemDays { get; private set; } = DefaultKeepProblemDays;

    public bool AlertOnFailure { get; private set; } = true;

    /// <summary>Up to when failures have been told to admins, so each one is told once.</summary>
    public DateTime? AlertedUpTo { get; private set; }

    public static EmailLogSetting CreateDefault() => new();

    public void Set(int keepSentDays, int keepProblemDays, bool alertOnFailure)
    {
        if (!SentChoices.Contains(keepSentDays))
        {
            throw new ArgumentOutOfRangeException(nameof(keepSentDays), keepSentDays, "Keep sent emails for 0, 30, 60 or 90 days.");
        }

        if (!ProblemChoices.Contains(keepProblemDays))
        {
            throw new ArgumentOutOfRangeException(nameof(keepProblemDays), keepProblemDays, "Keep failed emails for 30, 60 or 90 days.");
        }

        KeepSentDays = keepSentDays;
        KeepProblemDays = keepProblemDays;
        AlertOnFailure = alertOnFailure;
    }

    public void MarkAlerted(DateTime upTo) => AlertedUpTo = upTo;
}

/// <summary>
/// Who one module's emails come from (the Email senders page). The row with key <c>default</c> is the
/// organisation's default sender; a module without a row uses it. A blank field takes the default
/// sender's value, and in the end the connection's own.
/// </summary>
public sealed class EmailSender : TenantAggregateRoot
{
    private EmailSender()
    {
        ModuleKey = null!;
    }

    public string ModuleKey { get; private set; }

    /// <summary>The Studio connection that carries the mail; null takes the default sender's.</summary>
    public Guid? ConnectionId { get; private set; }

    public string? FromName { get; private set; }

    public string? FromAddress { get; private set; }

    public string? ReplyTo { get; private set; }

    public string? Bcc { get; private set; }

    /// <summary>Added under every email this sender sends.</summary>
    public string? Signature { get; private set; }

    /// <summary>The header colour of the HTML email, as #rrggbb.</summary>
    public string? BrandColor { get; private set; }

    /// <summary>No email goes for the module; in-app notifications still do. Never on the default sender.</summary>
    public bool IsPaused { get; private set; }

    public static EmailSender Create(string moduleKey) => new() { ModuleKey = Guard.MaxLength(Guard.NotNullOrWhiteSpace(moduleKey).Trim().ToLowerInvariant(), 32) };

    public void Set(Guid? connectionId, string? fromName, string? fromAddress, string? replyTo, string? bcc, string? signature, string? brandColor, bool isPaused)
    {
        ConnectionId = connectionId;
        FromName = Clean(fromName, 200);
        FromAddress = Clean(fromAddress, 256);
        ReplyTo = Clean(replyTo, 256);
        Bcc = Clean(bcc, 256);
        Signature = Clean(signature, 1000);
        BrandColor = Clean(brandColor, 7);
        IsPaused = isPaused;
    }

    private static string? Clean(string? text, int length) => string.IsNullOrWhiteSpace(text) ? null : Guard.MaxLength(text.Trim(), length);
}

public enum SmsStatus
{
    Queued = 1,
    Sent = 2,
    Failed = 3,
}

/// <summary>A text message on its way out, kept as a record like <see cref="EmailMessage"/> and delivered by the worker.</summary>
public sealed class SmsMessage : TenantAggregateRoot
{
    private SmsMessage()
    {
        ToNumber = null!;
        Body = null!;
        EventCode = null!;
    }

    public string ToNumber { get; private set; }

    public string Body { get; private set; }

    public string EventCode { get; private set; }

    public SmsStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTime? SentDate { get; private set; }

    /// <summary>Long text is cut rather than refused: a template's wording should not stop the notification going.</summary>
    public static SmsMessage Queue(string toNumber, string body, string eventCode) => new()
    {
        ToNumber = Guard.MaxLength(Guard.NotNullOrWhiteSpace(toNumber).Trim(), 32),
        Body = body.Length > 1000 ? body[..1000] : body,
        EventCode = Guard.MaxLength(eventCode, 64),
        Status = SmsStatus.Queued,
    };

    public void MarkSent(DateTime now)
    {
        Attempts++;
        Status = SmsStatus.Sent;
        SentDate = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        Status = SmsStatus.Failed;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}

/// <summary>
/// One person's choice for one kind of notification: which channels it reaches them on. No row means
/// all, as the template sends. Mandatory notifications ignore it.
/// </summary>
public sealed class NotificationPreference : TenantAggregateRoot
{
    private NotificationPreference()
    {
        EventCode = null!;
    }

    public Guid UserId { get; private set; }

    /// <summary>The employee the account signs in as, so notifications addressed to the employee follow it too.</summary>
    public Guid? EmployeeId { get; private set; }

    public string EventCode { get; private set; }

    public bool EmailEnabled { get; private set; }

    public bool InAppEnabled { get; private set; }

    public bool SmsEnabled { get; private set; }

    public static NotificationPreference Create(Guid userId, Guid? employeeId, string eventCode, bool emailEnabled, bool inAppEnabled, bool smsEnabled = true) => new()
    {
        UserId = Guard.NotEmpty(userId),
        EmployeeId = employeeId,
        EventCode = Guard.MaxLength(Guard.NotNullOrWhiteSpace(eventCode).Trim().ToLowerInvariant(), 64),
        EmailEnabled = emailEnabled,
        InAppEnabled = inAppEnabled,
        SmsEnabled = smsEnabled,
    };

    public void Set(Guid? employeeId, bool emailEnabled, bool inAppEnabled, bool smsEnabled)
    {
        EmployeeId = employeeId;
        EmailEnabled = emailEnabled;
        InAppEnabled = inAppEnabled;
        SmsEnabled = smsEnabled;
    }
}
