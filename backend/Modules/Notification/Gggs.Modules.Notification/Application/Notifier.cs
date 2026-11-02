using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Notification.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Notification.Application;

/// <summary>A default template: what a new tenant starts with.</summary>
internal sealed record TemplateSeed(string Code, string Name, string Subject, string Body, bool SendInApp, bool SendEmail);

/// <summary>The notifications Nuts and Bolts sends, with their default wording.</summary>
internal static class NotificationTemplateCatalog
{
    /// <summary>
    /// Sent whatever someone's preferences say: an account invitation, and notices with legal weight
    /// that the employee must be shown to have received.
    /// </summary>
    public static IReadOnlySet<string> Mandatory { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        NotificationTemplates.UserInvited,
        NotificationTemplates.ShowCauseIssued,
        NotificationTemplates.AbsenceNotice,
        NotificationTemplates.AccountCreated,
    };

    public static IReadOnlyList<TemplateSeed> Defaults { get; } =
    [
        new(NotificationTemplates.WorkflowTaskAssigned, "Approval needed",
            "Approval needed: {{title}}",
            "Hi {{recipientName}}, {{title}} is waiting for your approval{{delegatedNote}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.WorkflowTaskEscalated, "Approval escalated to you",
            "Escalated: {{title}}",
            "Hi {{recipientName}}, {{title}} passed its deadline with {{fromName}} and now needs your decision.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.WorkflowApproved, "Request approved",
            "Approved: {{title}}",
            "Hi {{recipientName}}, {{title}} has been approved.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.WorkflowRejected, "Request rejected",
            "Rejected: {{title}}",
            "Hi {{recipientName}}, {{title}} was rejected. Comment: {{comment}}", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.WorkflowSentBack, "Request sent back",
            "Changes requested: {{title}}",
            "Hi {{recipientName}}, {{title}} was sent back for changes. Comment: {{comment}}", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.UserInvited, "Invitation to Nuts and Bolts",
            "You're invited to Nuts and Bolts",
            "Hi {{recipientName}}, you have been invited to Nuts and Bolts. Set your password here before {{expires}}: {{invitationLink}}", SendInApp: false, SendEmail: true),
        new(NotificationTemplates.PolicyPublished, "Policy to acknowledge",
            "Please read: {{policyTitle}}",
            "Hi {{recipientName}}, version {{version}} of {{policyTitle}} is now in effect. Please read and acknowledge it.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.DocumentSignatureRequested, "Signature requested",
            "Please sign: {{documentTitle}}",
            "Hi {{recipientName}}, {{documentTitle}} is waiting for your signature.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ContractRenewalDue, "Contract renewal due",
            "Renewal due: {{contractNumber}} with {{clientName}}",
            "Hi {{recipientName}}, contract {{contractNumber}} ({{contractTitle}}) with {{clientName}} is due for renewal on {{dueDate}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ProjectTeamAdded, "Added to a project",
            "You're on {{projectName}}",
            "Hi {{recipientName}}, you have been added to {{projectName}} as {{roleName}} from {{startDate}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ProjectTaskAssigned, "Task assigned",
            "{{taskCode}}: {{taskTitle}}",
            "Hi {{recipientName}}, {{taskCode}} {{taskTitle}} in {{projectName}} has been assigned to you.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.TimesheetMissing, "Timesheet missing",
            "Timesheet due: week of {{weekStart}}",
            "Hi {{recipientName}}, your timesheet for the week of {{weekStart}} has not been submitted yet.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ResourceTransferDecided, "Project transfer",
            "Moving to {{projectName}}",
            "Hi {{recipientName}}, you move to {{projectName}} from {{effectiveDate}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.PayslipPublished, "Payslip published",
            "Your payslip for {{period}} is ready",
            "Hi {{recipientName}}, your payslip for {{period}} has been published. Net pay: {{netPay}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ComplianceFilingDue, "Statutory filing due",
            "{{filingName}} for {{period}} is due {{dueDate}}",
            "Hi {{recipientName}}, {{filingName}} for {{period}} is {{state}} (due {{dueDate}}).", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ComplianceRegistrationExpiring, "Registration expiring",
            "{{registrationName}} expires {{expiresOn}}",
            "Hi {{recipientName}}, {{registrationName}} ({{number}}) expires on {{expiresOn}}. Start the renewal.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.CompensationRevisionDecided, "Salary revision",
            "Your salary revision was {{decision}}",
            "Hi {{recipientName}}, the {{revisionType}} revision effective {{effectiveDate}} was {{decision}}.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.BenefitEnrollmentDecided, "Benefit enrollment",
            "{{planName}}: enrollment {{decision}}",
            "Hi {{recipientName}}, your enrollment in {{planName}} was {{decision}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ProjectBudgetAlert, "Project budget alert",
            "{{projectCode}} has used {{percent}}% of its cost budget",
            "Hi {{recipientName}}, {{projectCode}} {{projectName}} has spent {{actualCost}} of a {{costBudget}} cost budget ({{percent}}%).", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.InterviewScheduled, "Interview scheduled",
            "Interview: {{candidateName}} for {{jobTitle}}",
            "Hi {{recipientName}}, you are on the panel for {{candidateName}} ({{jobTitle}}), {{round}}, on {{when}}. Submit your scorecard afterwards.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ReferralUpdate, "Referral update",
            "Your referral {{candidateName}}: {{status}}",
            "Hi {{recipientName}}, your referral {{candidateName}} for {{jobTitle}} is now: {{status}}.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.OnboardingTaskAssigned, "Onboarding task",
            "Onboarding {{employeeName}}: {{taskTitle}}",
            "Hi {{recipientName}}, please complete \"{{taskTitle}}\" for {{employeeName}}, who joins on {{joiningDate}}. Due {{dueDate}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.OnboardingTaskOverdue, "Onboarding task overdue",
            "Overdue: {{taskTitle}} for {{employeeName}}",
            "Hi {{recipientName}}, \"{{taskTitle}}\" for {{employeeName}} was due on {{dueDate}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.OnboardingWelcome, "Welcome aboard",
            "Welcome to the team, {{recipientName}}",
            "Hi {{recipientName}}, welcome! You join on {{joiningDate}}. Your onboarding checklist is ready under My onboarding.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ReviewCycleLaunched, "Performance review open",
            "{{cycleName}}: your review is open",
            "Hi {{recipientName}}, {{cycleName}} has started. Please complete your self-review by {{dueDate}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.FeedbackRequested, "Feedback requested",
            "{{requesterName}} asked for your feedback",
            "Hi {{recipientName}}, {{requesterName}} would value your feedback on {{employeeName}} for {{cycleName}}. It is under My feedback.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ReviewSubmitted, "Self-review submitted",
            "{{employeeName}} submitted their self-review",
            "Hi {{recipientName}}, {{employeeName}} has submitted their self-review for {{cycleName}}. Your manager review is next.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.ReviewDue, "Review due",
            "Review due {{dueDate}}: {{employeeName}}",
            "Hi {{recipientName}}, the {{cycleName}} review of {{employeeName}} is waiting on you and is due on {{dueDate}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ReviewReleased, "Review released",
            "Your {{cycleName}} review is ready",
            "Hi {{recipientName}}, your review for {{cycleName}} has been released. Please read it and acknowledge it.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.PipStarted, "Improvement plan started",
            "A performance improvement plan has started",
            "Hi {{recipientName}}, a performance improvement plan runs from {{startDate}} to {{endDate}}. Your manager will go through it with you.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.LearningAssigned, "Learning assigned",
            "New learning: {{courseTitle}}",
            "Hi {{recipientName}}, \"{{courseTitle}}\" has been assigned to you{{dueText}}. It is under My learning.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.LearningOverdue, "Learning overdue",
            "Overdue: {{courseTitle}}",
            "Hi {{recipientName}}, \"{{courseTitle}}\" was due on {{dueDate}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.CertificationExpiring, "Certification expiring",
            "{{courseTitle}} certificate expires {{expiresOn}}",
            "Hi {{recipientName}}, your \"{{courseTitle}}\" certificate expires on {{expiresOn}}. Renew it from My learning.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.PromotionDecided, "Promotion decision",
            "Promotion {{status}}: {{employeeName}}",
            "Hi {{recipientName}}, the promotion of {{employeeName}} to {{designation}} has been {{status}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.Recognized, "You were recognised",
            "{{giverName}} recognised you: {{typeName}}",
            "Hi {{recipientName}}, {{giverName}} recognised you with {{typeName}} (+{{points}} points): \"{{message}}\"", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.RedemptionUpdate, "Redemption update",
            "Your {{itemName}} redemption is {{status}}",
            "Hi {{recipientName}}, your redemption of {{itemName}} is {{status}}. {{note}}", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.PointsExpiring, "Points expiring",
            "{{points}} points expire on {{expiresOn}}",
            "Hi {{recipientName}}, {{points}} of your reward points expire on {{expiresOn}}. Redeem them under My rewards.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.SurveyOpened, "Survey open",
            "{{surveyTitle}} - your view counts",
            "Hi {{recipientName}}, please take {{surveyTitle}} by {{closesOn}}. {{anonymity}}", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.TicketUpdate, "Ticket update",
            "{{number}}: {{status}}",
            "Hi {{recipientName}}, your ticket {{number}} \"{{subject}}\" is {{status}}. {{note}}", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.TicketAssigned, "Ticket assigned",
            "{{number}} assigned to you",
            "Hi {{recipientName}}, ticket {{number}} \"{{subject}}\" ({{priority}}) is assigned to you. Respond by {{dueAt}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.TicketSlaBreached, "SLA breached",
            "{{number}} missed its {{kind}} target",
            "Hi {{recipientName}}, ticket {{number}} \"{{subject}}\" has missed its {{kind}} target of {{dueAt}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.AssetAssigned, "Asset assigned",
            "{{assetName}} is assigned to you",
            "Hi {{recipientName}}, {{assetName}} (tag {{tag}}) has been assigned to you. It is listed under My assets.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.ExpenseDecided, "Expense update",
            "{{number}} {{status}}",
            "Hi {{recipientName}}, your {{kind}} {{number}} ({{amount}}) has been {{status}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.LoanUpdate, "Loan update",
            "Loan {{number}} {{status}}",
            "Hi {{recipientName}}, your loan {{number}} ({{amount}}) has been {{status}}. See My loans for the schedule.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ResignationSubmitted, "Resignation",
            "{{employeeName}} has resigned",
            "Hi {{recipientName}}, {{employeeName}} has resigned. Requested last working day: {{lastDay}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ClearanceAssigned, "Exit clearance",
            "Clearance for {{employeeName}}: {{item}}",
            "Hi {{recipientName}}, please clear \"{{item}}\" for {{employeeName}}, whose last day is {{lastDay}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.OffboardingTaskAssigned, "Offboarding task",
            "Offboarding for {{employeeName}}: {{tasks}}",
            "Hi {{recipientName}}, {{employeeName}} is leaving on {{lastDay}}. Your offboarding tasks: {{tasks}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.OffboardingTaskOverdue, "Offboarding task overdue",
            "Overdue: {{taskTitle}}",
            "Hi {{recipientName}}, \"{{taskTitle}}\" for {{employeeName}} was due on {{dueDate}}.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.ShowCauseIssued, "Show-cause notice",
            "A show-cause notice needs your reply by {{replyBy}}",
            "Hi {{recipientName}}, you have been issued a show-cause notice. Please read it and reply by {{replyBy}} under My resignation.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ShowCauseReplied, "Show-cause reply",
            "{{employeeName}} replied to the show-cause notice",
            "Hi {{recipientName}}, {{employeeName}} has replied to the show-cause notice issued on {{issuedOn}}.", SendInApp: true, SendEmail: false),
        new(NotificationTemplates.AbsenceAlert, "Absence alert",
            "{{employeeName}} absent {{days}} working days",
            "Hi {{recipientName}}, {{employeeName}} has been absent without leave for {{days}} working days since {{absentSince}}. Review it under Separation > Absconding.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.AbsenceNotice, "Absence notice",
            "{{noticeTitle}}: please report back by {{respondBy}}",
            "Hi {{recipientName}}, you have been absent without leave since {{absentSince}}. Please report to work or explain your absence by {{respondBy}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.SettlementOverdue, "Settlement overdue",
            "Full and final for {{employeeName}} is overdue",
            "Hi {{recipientName}}, the full and final settlement for {{employeeName}} was due by {{dueOn}} (two working days after the last day, Code on Wages s.17(2)) and is not yet paid.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ExitProposed, "Exit due",
            "{{employeeName}}: {{kind}} on {{lastDay}}",
            "Hi {{recipientName}}, {{employeeName}} reaches {{kind}} on {{lastDay}}. A separation has been proposed for you to confirm or decline.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.AccountCreated, "Work account created",
            "Your work account is ready",
            "Hi {{recipientName}}, your work account is ready. Sign in as {{login}} with the temporary password {{temporaryPassword}}. You will be asked to choose your own the first time.", SendInApp: false, SendEmail: true),
        new(NotificationTemplates.ReportDigest, "Report digest",
            "Your {{period}} {{dashboard}} digest",
            "Hi {{recipientName}}, here are this {{period}}'s {{dashboard}} figures: {{summary}}", SendInApp: true, SendEmail: true),
    ];
}

/// <summary>Fills <c>{{placeholders}}</c>. Unknown placeholders render empty rather than leaking braces to a reader.</summary>
internal static partial class TemplateRenderer
{
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
        => Placeholder().Replace(template, match => values.TryGetValue(match.Groups[1].Value, out string? value) ? value : string.Empty);

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

/// <summary>Raised when an email is queued; the worker's consumer delivers it.</summary>
[MessageType("gggs.notification.email-queued")]
public sealed record EmailQueuedIntegrationEvent(Guid EmailMessageId) : IntegrationEvent;

/// <summary>Raised when a text message is queued; the worker's consumer delivers it.</summary>
[MessageType("gggs.notification.sms-queued")]
public sealed record SmsQueuedIntegrationEvent(Guid SmsMessageId) : IntegrationEvent;

/// <summary>
/// Sends straight away rather than queuing, so the secret is never written down: the record kept is
/// the same message with the secret masked. A tenant that predates a template gets the catalog's wording.
/// </summary>
internal sealed class OneTimeNotices(
    AppDbContext dbContext,
    INotificationChannel channel,
    ITenantMessageSender sender,
    TimeProvider timeProvider,
    ILogger<OneTimeNotices> logger) : IOneTimeNotices
{
    private const string Mask = "••••••••";

    public async Task<ChannelSendResult> SendAsync(string templateCode, string address, IReadOnlyDictionary<string, string> values, IReadOnlySet<string> secrets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(secrets);

        string code = templateCode.Trim().ToLowerInvariant();
        NotificationTemplate? template = await dbContext.NotificationTemplates.AsNoTracking().FirstOrDefaultAsync(row => row.Code == code, cancellationToken);
        TemplateSeed? seed = NotificationTemplateCatalog.Defaults.FirstOrDefault(row => row.Code == code);
        string subjectTemplate = template?.Subject ?? seed?.Subject ?? throw new InvalidOperationException($"There is no {code} template.");
        string bodyTemplate = template?.Body ?? seed!.Body;

        Dictionary<string, string> masked = values.ToDictionary(pair => pair.Key, pair => secrets.Contains(pair.Key) ? Mask : pair.Value, StringComparer.Ordinal);
        var record = EmailMessage.Queue(address, TemplateRenderer.Render(subjectTemplate, masked), TemplateRenderer.Render(bodyTemplate, masked), code);
        dbContext.EmailMessages.Add(record);

        string subject = TemplateRenderer.Render(subjectTemplate, values);
        string body = TemplateRenderer.Render(bodyTemplate, values);
        ChannelSendResult result;
        try
        {
            result = await sender.SendEmailAsync(address, subject, body, cancellationToken) ?? await SendThroughSystemAsync(address, subject, body, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // By type only: an exception's message could quote what it was given.
            logger.LogWarning("A one-time email ({Code}) failed: {Type}", code, exception.GetType().Name);
            result = new ChannelSendResult(false, "The email could not be sent.");
        }

        if (result.Succeeded)
        {
            record.MarkSent(timeProvider.GetUtcNow().UtcDateTime);
        }
        else
        {
            record.MarkFailed(result.Message);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<ChannelSendResult> SendThroughSystemAsync(string address, string subject, string body, CancellationToken cancellationToken)
    {
        await channel.SendAsync(new OutboundMessage(address, subject, body, Sensitive: true), cancellationToken);
        return new ChannelSendResult(true, "Sent.");
    }
}

internal sealed class Notifier(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IEmployeeSeparations contacts,
    ITenantMessageSender sender,
    IIntegrationEventPublisher publisher,
    ILogger<Notifier> logger) : INotifier
{
    public async Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = request.TemplateCode.Trim().ToLowerInvariant();
        NotificationTemplate? template = await dbContext.NotificationTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);

        if (template is null || !template.IsActive)
        {
            // A switched-off template is the tenant's choice; a missing one means seeding has not run.
            logger.LogInformation("Notification {Template} not sent: the template is {State}.", code, template is null ? "missing" : "inactive");
            return;
        }

        List<Guid> employeeIds = [.. request.Recipients.Where(recipient => recipient.EmployeeId is not null).Select(recipient => recipient.EmployeeId!.Value).Distinct()];
        Dictionary<Guid, EmployeeSummary> people = (await employees.GetManyAsync(employeeIds, cancellationToken))
            .ToDictionary(person => person.Id);

        // What each recipient chose for this kind of notification; nobody's choice silences a mandatory one.
        List<Guid> userIds = [.. request.Recipients.Where(recipient => recipient.UserId is not null).Select(recipient => recipient.UserId!.Value).Distinct()];
        List<NotificationPreference> preferences = NotificationTemplateCatalog.Mandatory.Contains(code) || (userIds.Count == 0 && employeeIds.Count == 0)
            ? []
            : await dbContext.NotificationPreferences.AsNoTracking()
                .Where(preference => preference.EventCode == code
                    && (userIds.Contains(preference.UserId) || (preference.EmployeeId != null && employeeIds.Contains(preference.EmployeeId.Value))))
                .ToListAsync(cancellationToken);

        // Mobile numbers are looked up only when a text message can actually go.
        IReadOnlyDictionary<Guid, PersonalContact> phones = template.SendSms && employeeIds.Count > 0 && await sender.SendsSmsAsync(cancellationToken)
            ? await contacts.PersonalContactsAsync(employeeIds, cancellationToken)
            : new Dictionary<Guid, PersonalContact>();

        foreach (NotificationRecipient recipient in request.Recipients.Distinct())
        {
            EmployeeSummary? person = recipient.EmployeeId is { } id ? people.GetValueOrDefault(id) : null;
            var values = new Dictionary<string, string>(request.Values, StringComparer.Ordinal)
            {
                ["recipientName"] = person?.DisplayName ?? request.Values.GetValueOrDefault("recipientName") ?? "there",
            };

            string subject = TemplateRenderer.Render(template.Subject, values);
            string body = TemplateRenderer.Render(template.Body, values);
            NotificationPreference? choice =
                preferences.FirstOrDefault(preference => recipient.UserId is not null && preference.UserId == recipient.UserId)
                ?? preferences.FirstOrDefault(preference => recipient.EmployeeId is not null && preference.EmployeeId == recipient.EmployeeId);

            if (template.SendInApp && choice?.InAppEnabled != false && (recipient.EmployeeId is not null || recipient.UserId is not null))
            {
                dbContext.InAppNotifications.Add(InAppNotification.Create(recipient.EmployeeId, recipient.UserId, code, subject, body, request.Link));
            }

            string? address = recipient.Email ?? person?.OfficialEmail;
            if (template.SendEmail && choice?.EmailEnabled != false && !string.IsNullOrWhiteSpace(address))
            {
                var email = EmailMessage.Queue(address, subject, body, code);
                dbContext.EmailMessages.Add(email);
                publisher.Publish(new EmailQueuedIntegrationEvent(email.Id));
            }

            string? mobile = recipient.EmployeeId is { } phoneOwner ? phones.GetValueOrDefault(phoneOwner)?.MobilePhone : null;
            if (choice?.SmsEnabled != false && !string.IsNullOrWhiteSpace(mobile))
            {
                var sms = SmsMessage.Queue(mobile, body, code);
                dbContext.SmsMessages.Add(sms);
                publisher.Publish(new SmsQueuedIntegrationEvent(sms.Id));
            }
        }
    }
}
