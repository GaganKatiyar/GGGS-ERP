namespace Gggs.Modules.Notification.Contracts;

/// <summary>
/// Someone to notify. An employee is reached in-app and at their official email; a user without an
/// employee record in-app only; a bare address (an invitee) by email only.
/// </summary>
public sealed record NotificationRecipient(Guid? EmployeeId = null, Guid? UserId = null, string? Email = null);

/// <summary>
/// A notification to send: which template, to whom, and the values its placeholders take.
/// <c>{{recipientName}}</c> is filled in per recipient.
/// </summary>
public sealed record NotificationRequest(
    string TemplateCode,
    IReadOnlyList<NotificationRecipient> Recipients,
    IReadOnlyDictionary<string, string> Values,
    string? Link = null);

/// <summary>
/// Sends notifications through the tenant's templates (spec §108). Writes into the caller's unit of
/// work: the in-app rows and queued emails commit with whatever the caller saves next, so a
/// notification never goes out for a change that was rolled back.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What happened when a message went out through the organisation's own connection.</summary>
public sealed record ChannelSendResult(bool Succeeded, string Message);

/// <summary>
/// The organisation's own way of sending, if it has set one up in Studio. A null result means none
/// is set: email then goes through the system's sender, and text messages do not go at all.
/// </summary>
public interface ITenantMessageSender
{
    Task<bool> SendsSmsAsync(CancellationToken cancellationToken = default);

    Task<ChannelSendResult?> SendEmailAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);

    Task<ChannelSendResult?> SendSmsAsync(string recipient, string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// An email sent once and straight away, for what must not wait in a queue or stay in a record: the
/// stored copy has the secret values masked, and nothing logs them. Worker host only.
/// </summary>
public interface IOneTimeNotices
{
    Task<ChannelSendResult> SendAsync(string templateCode, string address, IReadOnlyDictionary<string, string> values, IReadOnlySet<string> secrets, CancellationToken cancellationToken = default);
}

/// <summary>Template codes other modules send. Released contract: add freely, never rename.</summary>
public static class NotificationTemplates
{
    public const string WorkflowTaskAssigned = "workflow.task-assigned";
    public const string WorkflowTaskEscalated = "workflow.task-escalated";
    public const string WorkflowApproved = "workflow.approved";
    public const string WorkflowRejected = "workflow.rejected";
    public const string WorkflowSentBack = "workflow.sent-back";
    public const string UserInvited = "identity.user-invited";
    public const string PolicyPublished = "policy.published";
    public const string DocumentSignatureRequested = "document.signature-requested";
    public const string ContractRenewalDue = "contract.renewal-due";
    public const string ProjectTeamAdded = "project.team-added";
    public const string ProjectTaskAssigned = "project.task-assigned";
    public const string TimesheetMissing = "timesheet.missing";
    public const string ResourceTransferDecided = "resource.transfer-decided";
    public const string PayslipPublished = "payroll.payslip-published";
    public const string ComplianceFilingDue = "compliance.filing-due";
    public const string ComplianceRegistrationExpiring = "compliance.registration-expiring";
    public const string CompensationRevisionDecided = "compensation.revision-decided";
    public const string BenefitEnrollmentDecided = "benefit.enrollment-decided";
    public const string ProjectBudgetAlert = "project.budget-alert";
    public const string InterviewScheduled = "recruitment.interview-scheduled";
    public const string ReferralUpdate = "recruitment.referral-update";
    public const string OnboardingTaskAssigned = "onboarding.task-assigned";
    public const string OnboardingTaskOverdue = "onboarding.task-overdue";
    public const string OnboardingWelcome = "onboarding.welcome";
    public const string ReviewCycleLaunched = "performance.cycle-launched";
    public const string FeedbackRequested = "performance.feedback-requested";
    public const string ReviewSubmitted = "performance.review-submitted";
    public const string ReviewDue = "performance.review-due";
    public const string ReviewReleased = "performance.review-released";
    public const string PipStarted = "performance.pip-started";
    public const string LearningAssigned = "learning.assigned";
    public const string LearningOverdue = "learning.overdue";
    public const string CertificationExpiring = "learning.certification-expiring";
    public const string PromotionDecided = "career.promotion-decided";
    public const string Recognized = "reward.recognized";
    public const string RedemptionUpdate = "reward.redemption-update";
    public const string PointsExpiring = "reward.points-expiring";
    public const string SurveyOpened = "engagement.survey-opened";
    public const string TicketUpdate = "helpdesk.ticket-update";
    public const string TicketAssigned = "helpdesk.ticket-assigned";
    public const string TicketSlaBreached = "helpdesk.sla-breached";
    public const string AssetAssigned = "asset.assigned";
    public const string ExpenseDecided = "expense.decided";
    public const string LoanUpdate = "loan.update";
    public const string ResignationSubmitted = "separation.resignation-submitted";
    public const string ClearanceAssigned = "separation.clearance-assigned";
    public const string OffboardingTaskAssigned = "separation.offboarding-task-assigned";
    public const string OffboardingTaskOverdue = "separation.offboarding-task-overdue";
    public const string ShowCauseIssued = "separation.show-cause-issued";
    public const string ShowCauseReplied = "separation.show-cause-replied";
    public const string AbsenceAlert = "separation.absence-alert";
    public const string AbsenceNotice = "separation.absence-notice";
    public const string ExitProposed = "separation.exit-proposed";
    public const string SettlementOverdue = "separation.settlement-overdue";
    public const string ReportDigest = "insight.report-digest";
    public const string AccountCreated = "identity.account-created";
    public const string PasswordResetRequested = "identity.password-reset-requested";
    public const string PasswordResetCompleted = "identity.password-reset-completed";
}
