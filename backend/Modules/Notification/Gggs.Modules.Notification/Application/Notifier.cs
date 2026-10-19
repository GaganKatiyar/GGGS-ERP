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

/// <summary>The notifications PeopleOS sends, with their default wording.</summary>
internal static class NotificationTemplateCatalog
{
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
        new(NotificationTemplates.UserInvited, "Invitation to PeopleOS",
            "You're invited to PeopleOS",
            "Hi {{recipientName}}, you have been invited to PeopleOS. Set your password here before {{expires}}: {{invitationLink}}", SendInApp: false, SendEmail: true),
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
        new(NotificationTemplates.ResignationSubmitted, "Resignation",
            "{{employeeName}} has resigned",
            "Hi {{recipientName}}, {{employeeName}} has resigned. Requested last working day: {{lastDay}}.", SendInApp: true, SendEmail: true),
        new(NotificationTemplates.ClearanceAssigned, "Exit clearance",
            "Clearance for {{employeeName}}: {{item}}",
            "Hi {{recipientName}}, please clear \"{{item}}\" for {{employeeName}}, whose last day is {{lastDay}}.", SendInApp: true, SendEmail: true),
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

internal sealed class Notifier(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
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

        foreach (NotificationRecipient recipient in request.Recipients.Distinct())
        {
            EmployeeSummary? person = recipient.EmployeeId is { } id ? people.GetValueOrDefault(id) : null;
            var values = new Dictionary<string, string>(request.Values, StringComparer.Ordinal)
            {
                ["recipientName"] = person?.DisplayName ?? request.Values.GetValueOrDefault("recipientName") ?? "there",
            };

            string subject = TemplateRenderer.Render(template.Subject, values);
            string body = TemplateRenderer.Render(template.Body, values);

            if (template.SendInApp && (recipient.EmployeeId is not null || recipient.UserId is not null))
            {
                dbContext.InAppNotifications.Add(InAppNotification.Create(recipient.EmployeeId, recipient.UserId, code, subject, body, request.Link));
            }

            string? address = recipient.Email ?? person?.OfficialEmail;
            if (template.SendEmail && !string.IsNullOrWhiteSpace(address))
            {
                var email = EmailMessage.Queue(address, subject, body, code);
                dbContext.EmailMessages.Add(email);
                publisher.Publish(new EmailQueuedIntegrationEvent(email.Id));
            }
        }
    }
}
