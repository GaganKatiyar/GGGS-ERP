using Gggs.Modules.Notification.Application;
using Gggs.Modules.Notification.Domain;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>
/// Keeping the Email log small and acting on failures: which failures are tried again, what a failure is
/// called in plain words, how long emails are kept, and what the failure notice says.
/// </summary>
public sealed class EmailLogRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("The operation has timed out.", EmailFailureKind.Temporary)]
    [InlineData("The mail server did not answer in time.", EmailFailureKind.Temporary)]
    [InlineData("Failure sending mail.", EmailFailureKind.Temporary)]
    [InlineData("SendGrid answered 429 Too Many Requests", EmailFailureKind.Temporary)]
    [InlineData("Service not available, closing transmission channel. The server response was: 421 4.7.0 Try again later", EmailFailureKind.Temporary)]
    [InlineData("The SMTP server requires a secure connection or the client was not authenticated. The server response was: 5.7.0 Authentication Required", EmailFailureKind.Account)]
    [InlineData("SendGrid answered 401 Unauthorized: bad key", EmailFailureKind.Account)]
    [InlineData("Microsoft refused the client credentials.", EmailFailureKind.Account)]
    [InlineData("535 5.7.8 Username and Password not accepted", EmailFailureKind.Account)]
    [InlineData("Mailbox unavailable. The server response was: 5.1.1 User unknown", EmailFailureKind.Address)]
    [InlineData("The specified string is not in the form required for an e-mail address.", EmailFailureKind.Address)]
    [InlineData("Port 465 is not supported. Use your server's STARTTLS port, usually 587.", EmailFailureKind.Other)]
    [InlineData(null, EmailFailureKind.Other)]
    public void A_failure_is_read_into_a_kind(string? message, EmailFailureKind kind)
        => EmailFailures.Classify(message).Kind.ShouldBe(kind);

    [Fact]
    public void An_unknown_failure_keeps_the_mail_systems_own_words()
    {
        EmailFailure failure = EmailFailures.Classify("Port 465 is not supported.");

        failure.Reason.ShouldBe("Port 465 is not supported.");
        failure.Fix.ShouldBeNull();
    }

    [Fact]
    public void An_account_or_address_failure_says_what_to_do()
    {
        EmailFailures.Classify("535 authentication failed").Fix.ShouldNotBeNull();
        EmailFailures.Classify("550 5.1.1 user unknown").Fix.ShouldNotBeNull();
    }

    [Fact]
    public void A_temporary_failure_is_tried_again_after_5_minutes_then_30_then_2_hours_then_never()
    {
        EmailMessage email = Email();

        email.MarkFailed("The operation has timed out.", now: Now);
        email.NextAttemptAt.ShouldBe(Now.AddMinutes(5));
        email.FailedForGood.ShouldBeFalse();

        email.Requeue();
        email.MarkFailed("The operation has timed out.", now: Now);
        email.NextAttemptAt.ShouldBe(Now.AddMinutes(30));

        email.Requeue();
        email.MarkFailed("The operation has timed out.", now: Now);
        email.NextAttemptAt.ShouldBe(Now.AddHours(2));

        email.Requeue();
        email.MarkFailed("The operation has timed out.", now: Now);
        email.NextAttemptAt.ShouldBeNull();
        email.FailedForGood.ShouldBeTrue();
    }

    [Fact]
    public void An_account_failure_is_final_at_once()
    {
        EmailMessage email = Email();

        email.MarkFailed("535 5.7.8 Username and Password not accepted", now: Now);

        email.NextAttemptAt.ShouldBeNull();
        email.FailedForGood.ShouldBeTrue();
    }

    [Fact]
    public void A_failure_recorded_without_the_time_is_never_retried()
    {
        EmailMessage email = Email();

        email.MarkFailed("The operation has timed out.");

        email.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void A_masked_one_time_notice_is_never_retried()
    {
        EmailMessage email = Email();
        email.MarkMasked();

        email.MarkFailed("The operation has timed out.", now: Now);

        email.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void A_sent_email_keeps_who_and_what_but_not_the_body()
    {
        EmailMessage email = Email();
        email.MarkFailed("The operation has timed out.", now: Now);
        email.Requeue();

        email.MarkSent(Now, "GGGS People via Company Gmail");

        email.Body.ShouldBeEmpty();
        email.Subject.ShouldBe("Welcome");
        email.ToAddress.ShouldBe("asha@acme.example");
        email.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void Resend_clears_a_pending_retry()
    {
        EmailMessage email = Email();
        email.MarkFailed("The operation has timed out.", now: Now);

        email.Requeue();

        email.Status.ShouldBe(EmailStatus.Queued);
        email.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void By_default_sent_and_failed_emails_are_kept_30_days()
    {
        (DateTime sentBefore, DateTime problemsBefore) = EmailLogRules.Cutoffs(EmailLogSetting.CreateDefault(), Now);

        sentBefore.ShouldBe(Now.AddDays(-30));
        problemsBefore.ShouldBe(Now.AddDays(-30));
    }

    [Fact]
    public void Sixty_days_is_offered_for_both()
    {
        EmailLogSetting setting = EmailLogSetting.CreateDefault();
        setting.Set(60, 60, alertOnFailure: true);

        (DateTime sentBefore, DateTime problemsBefore) = EmailLogRules.Cutoffs(setting, Now);

        sentBefore.ShouldBe(Now.AddDays(-60));
        problemsBefore.ShouldBe(Now.AddDays(-60));
    }

    [Fact]
    public void Failed_only_removes_every_sent_email_from_before_today()
    {
        EmailLogSetting setting = EmailLogSetting.CreateDefault();
        setting.Set(0, 90, alertOnFailure: true);

        (DateTime sentBefore, DateTime problemsBefore) = EmailLogRules.Cutoffs(setting, Now);

        sentBefore.ShouldBe(Now.Date);
        problemsBefore.ShouldBe(Now.AddDays(-90));
    }

    [Theory]
    [InlineData(7, 90)]
    [InlineData(365, 90)]
    [InlineData(90, 365)]
    [InlineData(90, 0)]
    public void Only_the_offered_keep_periods_are_accepted(int sentDays, int problemDays)
        => Should.Throw<ArgumentOutOfRangeException>(() => EmailLogSetting.CreateDefault().Set(sentDays, problemDays, alertOnFailure: true));

    [Fact]
    public void The_failure_notice_counts_the_emails_and_gives_the_most_common_reason()
    {
        (string title, string body) = EmailLogRules.AlertText(["535 authentication failed", "535 authentication failed", "550 5.1.1 user unknown"]);

        title.ShouldBe("3 emails could not be sent");
        body.ShouldStartWith("Most common reason: The mail account refused the sign-in");
        body.ShouldContain("Connectors");
    }

    [Fact]
    public void A_single_failure_is_told_in_the_singular()
        => EmailLogRules.AlertText(["Failure sending mail."]).Title.ShouldBe("An email could not be sent");

    private static EmailMessage Email() => EmailMessage.Queue("asha@acme.example", "Welcome", "Hi Asha, welcome.", "onboarding.welcome", "onboarding");
}
