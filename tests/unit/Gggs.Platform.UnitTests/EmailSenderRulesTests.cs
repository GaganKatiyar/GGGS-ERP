using System.Net.Mail;
using System.Text.Json.Nodes;
using Gggs.Modules.Notification.Application;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Studio.Application;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>
/// Module-wise email: which module a template belongs to, how a module's sender is filled in from the default
/// sender, what the HTML version looks like, and how each email connector carries the sender's details.
/// </summary>
public sealed class EmailSenderRulesTests
{
    [Theory]
    [InlineData("reward.recognized", "reward")]
    [InlineData("performance.review-released", "appraisal")]
    [InlineData("career.promotion-decided", "appraisal")]
    [InlineData("onboarding.welcome", "onboarding")]
    [InlineData("expense.decided", "payroll")]
    [InlineData("workflow.task-assigned", "approvals")]
    [InlineData("separation.absence-notice", "exits")]
    [InlineData("project.task-assigned", "other")]
    [InlineData("nothing", "other")]
    public void A_template_belongs_to_the_module_its_code_starts_with(string code, string module)
        => EmailModules.KeyFor(code).ShouldBe(module);

    [Fact]
    public void A_module_without_its_own_values_takes_the_default_senders()
    {
        EmailSender fallback = Sender("default", connection: Guid.NewGuid(), name: "GGGS People", address: "people@acme.example", replyTo: "hr@acme.example", signature: "GGGS");

        ResolvedSender reward = EmailSenderResolver.Merge("reward", own: null, fallback, "Nuts and Bolts");

        reward.ConnectionId.ShouldBe(fallback.ConnectionId);
        reward.FromName.ShouldBe("GGGS People");
        reward.FromAddress.ShouldBe("people@acme.example");
        reward.ReplyTo.ShouldBe("hr@acme.example");
        reward.Signature.ShouldBe("GGGS");
        reward.Color.ShouldBe("#b45309", "Star Reward's own colour when nobody chose one");
        reward.IsPaused.ShouldBeFalse();
    }

    [Fact]
    public void A_module_with_its_own_connection_does_not_borrow_the_default_address()
    {
        EmailSender fallback = Sender("default", connection: Guid.NewGuid(), name: "GGGS People", address: "people@acme.example");
        EmailSender own = Sender("payroll", connection: Guid.NewGuid(), name: "GGGS Payroll", paused: true);

        ResolvedSender payroll = EmailSenderResolver.Merge("payroll", own, fallback, "Nuts and Bolts");

        payroll.ConnectionId.ShouldBe(own.ConnectionId);
        payroll.FromAddress.ShouldBeNull("the default's address may not be one this connection can send as");
        payroll.FromName.ShouldBe("GGGS Payroll");
        payroll.IsPaused.ShouldBeTrue();
    }

    [Fact]
    public void With_nothing_set_the_email_goes_under_the_systems_name()
    {
        ResolvedSender sender = EmailSenderResolver.Merge("identity", null, null, "Nuts and Bolts");

        sender.ConnectionId.ShouldBeNull();
        sender.Title.ShouldBe("Nuts and Bolts");
        sender.FromName.ShouldBeNull();
        sender.Describe("the server log").ShouldBe("via the server log");
    }

    [Fact]
    public void The_text_carries_the_signature_and_the_html_encodes_whatever_it_is_given()
    {
        ResolvedSender sender = EmailSenderResolver.Merge("reward", Sender("reward", name: "Stars <team>", signature: "Keep shining!"), null, "Nuts and Bolts");

        var email = sender.Compose("asha@acme.example", "Hi", "<script>alert(1)</script>\nSee https://app.acme.example/rewards?a=1&b=2");

        email.Body.ShouldEndWith("\n\nKeep shining!");
        email.HtmlBody!.ShouldNotContain("<script>");
        email.HtmlBody.ShouldContain("&lt;script&gt;");
        email.HtmlBody.ShouldContain("Stars &lt;team&gt;");
        email.HtmlBody.ShouldContain("<br>");
        email.HtmlBody.ShouldContain("<a href=\"https://app.acme.example/rewards?a=1&amp;b=2\"");
        email.HtmlBody.ShouldContain("Keep shining!");
    }

    [Fact]
    public void A_colour_that_is_not_a_colour_falls_back_to_the_brand()
        => EmailLayout.Html("T", "red;background:url(x)", "Body", null).ShouldNotContain("url(x)");

    [Fact]
    public void Only_a_failed_logged_or_paused_email_that_holds_no_secret_can_be_sent_again()
    {
        EmailMessage email = EmailMessage.Queue("asha@acme.example", "Hi", "Body", "reward.recognized", "reward");
        email.CanResend.ShouldBeFalse("still queued");

        email.MarkLogged("No connection.");
        email.CanResend.ShouldBeTrue();
        email.Requeue();
        email.Status.ShouldBe(EmailStatus.Queued);
        email.LastError.ShouldBeNull();

        email.MarkSent(DateTime.UtcNow, "GGGS Star Rewards via Gmail");
        email.CanResend.ShouldBeFalse();
        email.SentAs.ShouldBe("GGGS Star Rewards via Gmail");

        EmailMessage reset = EmailMessage.Queue("asha@acme.example", "Reset", "••••", "identity.password-reset-requested");
        reset.MarkMasked();
        reset.MarkFailed("Refused.");
        reset.CanResend.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(reset.Requeue);
    }

    [Fact]
    public void Smtp_carries_the_senders_address_reply_to_copy_and_html()
    {
        var settings = new Dictionary<string, string> { ["fromAddress"] = "people@acme.example", ["fromName"] = "People" };
        using MailMessage mail = SmtpAdapter.Message(settings, new MailParts("asha@acme.example", "Hi", "Text", "<p>Html</p>", "Stars", "rewards@acme.example", "culture@acme.example", "archive@acme.example"));

        mail.From!.Address.ShouldBe("rewards@acme.example");
        mail.From.DisplayName.ShouldBe("Stars");
        mail.ReplyToList.ShouldHaveSingleItem().Address.ShouldBe("culture@acme.example");
        mail.Bcc.ShouldHaveSingleItem().Address.ShouldBe("archive@acme.example");
        mail.Body.ShouldBe("Text");
        mail.AlternateViews.ShouldHaveSingleItem().ContentType.MediaType.ShouldBe("text/html");

        using MailMessage plain = SmtpAdapter.Message(settings, new MailParts("asha@acme.example", "Hi", "Text"));
        plain.From!.Address.ShouldBe("people@acme.example");
        plain.AlternateViews.ShouldBeEmpty();
    }

    [Fact]
    public void Microsoft_365_sends_the_html_with_reply_to_and_copy()
    {
        JsonObject message = Microsoft365MailAdapter.Message("asha@acme.example", "Hi", "Text", "<p>Html</p>", "culture@acme.example", "archive@acme.example");
        JsonNode body = message["message"]!;
        body["body"]!["contentType"]!.GetValue<string>().ShouldBe("HTML");
        body["replyTo"]![0]!["emailAddress"]!["address"]!.GetValue<string>().ShouldBe("culture@acme.example");
        body["bccRecipients"]![0]!["emailAddress"]!["address"]!.GetValue<string>().ShouldBe("archive@acme.example");
    }

    [Fact]
    public void SendGrid_sends_both_versions_with_reply_to_and_copy()
    {
        JsonObject message = SendGridAdapter.Message("rewards@acme.example", "Stars", "asha@acme.example", "Hi", "Text", "<p>Html</p>", "culture@acme.example", "archive@acme.example");
        message["content"]!.AsArray().Select(part => part!["type"]!.GetValue<string>()).ShouldBe(["text/plain", "text/html"]);
        message["reply_to"]!["email"]!.GetValue<string>().ShouldBe("culture@acme.example");
        message["personalizations"]![0]!["bcc"]![0]!["email"]!.GetValue<string>().ShouldBe("archive@acme.example");
    }

    private static EmailSender Sender(string key, Guid? connection = null, string? name = null, string? address = null, string? replyTo = null, string? signature = null, bool paused = false)
    {
        EmailSender sender = EmailSender.Create(key);
        sender.Set(connection, name, address, replyTo, null, signature, null, paused);
        return sender;
    }
}
