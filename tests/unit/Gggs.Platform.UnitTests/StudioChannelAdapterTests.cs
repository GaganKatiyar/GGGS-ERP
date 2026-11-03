using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Studio.Application;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>What the email, SMS and WhatsApp connectors put on the wire, against a handler standing in for each service.</summary>
public sealed class StudioChannelAdapterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Service(HttpStatusCode status = HttpStatusCode.OK, string answer = "{}") : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpMethod Method, Uri Url, string? Auth, string? AuthKey, string Body)> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), request.Headers.TryGetValues("authkey", out IEnumerable<string>? keys) ? keys.Single() : null, body));
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private static Dictionary<string, string> Sms(string to = "+91 98765 43210", string text = "Hello") => new() { ["to"] = to, ["text"] = text };

    [Theory]
    [InlineData("+91 98765-43210", null, "919876543210")]
    [InlineData("9876543210", "91", "919876543210")]
    [InlineData("09876543210", "91", "919876543210")]
    [InlineData("00919876543210", null, "919876543210")]
    [InlineData("919876543210", null, "919876543210")]
    [InlineData("4155550100", "1", "14155550100")]
    [InlineData("+14155550100", "91", "14155550100")]
    public void PhoneNumbers_BecomeInternational(string raw, string? country, string expected)
        => PhoneNumbers.Normalize(raw, country).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("12345")]
    [InlineData("+1234567890123456")]
    public void WhatCannotBeANumber_IsRefused(string? raw) => PhoneNumbers.Normalize(raw, "91").ShouldBeNull();

    [Fact]
    public void TheTriggersSampleMobile_CanNeverReachAnyone()
        => PhoneNumbers.Normalize(StudioTriggers.Find("employee.hired")!.SampleFields["employeeMobile"], "91").ShouldBeNull("a test send fills in the sample, and must not text a stranger");

    [Fact]
    public async Task TwilioSms_PostsTheForm_WithBasicAuth()
    {
        var service = new Service(HttpStatusCode.Created);
        IntegrationResult result = await new TwilioSmsAdapter(service).RunAsync("send", new Dictionary<string, string> { ["accountSid"] = "AC1", ["authToken"] = "secret", ["from"] = "+14155550100" }, Sms(), Ct);

        result.Succeeded.ShouldBeTrue();
        var sent = service.Requests.ShouldHaveSingleItem();
        sent.Url.ToString().ShouldBe("https://api.twilio.com/2010-04-01/Accounts/AC1/Messages.json");
        sent.Auth.ShouldBe("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("AC1:secret")));
        sent.Body.ShouldBe("To=%2B919876543210&From=%2B14155550100&Body=Hello");
    }

    [Fact]
    public async Task TwilioWhatsApp_PrefixesTheNumbers_AndUsesTheTemplateWhenGiven()
    {
        var service = new Service(HttpStatusCode.Created);
        var settings = new Dictionary<string, string> { ["accountSid"] = "AC1", ["authToken"] = "secret", ["from"] = "+14155550100" };
        await new TwilioWhatsAppAdapter(service).RunAsync("send", settings, Sms(), Ct);
        service.Requests[0].Body.ShouldBe("To=whatsapp%3A%2B919876543210&From=whatsapp%3A%2B14155550100&Body=Hello");

        settings["contentSid"] = "HX1";
        await new TwilioWhatsAppAdapter(service).RunAsync("send", settings, Sms(), Ct);
        service.Requests[1].Body.ShouldContain("ContentSid=HX1");
        service.Requests[1].Body.ShouldNotContain("Body=");
        WebUtility.UrlDecode(service.Requests[1].Body).ShouldContain("ContentVariables={\"1\":\"Hello\"}");
    }

    [Fact]
    public async Task ABadNumber_IsNeverSent()
    {
        var service = new Service();
        (await new TwilioSmsAdapter(service).RunAsync("send", new Dictionary<string, string>(), Sms("+0000000000"), Ct)).Succeeded.ShouldBeFalse();
        (await new Msg91Adapter(service).RunAsync("send", new Dictionary<string, string>(), Sms("nobody"), Ct)).Succeeded.ShouldBeFalse();
        IntegrationResult whatsApp = await new WhatsAppCloudAdapter(service).RunAsync("send", new Dictionary<string, string>(), Sms(string.Empty), Ct);

        whatsApp.Succeeded.ShouldBeFalse();
        whatsApp.Message.ShouldContain("not a phone number");
        service.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Msg91_SendsThroughItsFlow_WithTheKeyInAHeader()
    {
        var service = new Service();
        (await new Msg91Adapter(service).RunAsync("send", new Dictionary<string, string> { ["authKey"] = "key", ["flowId"] = "flow1" }, Sms(), Ct)).Succeeded.ShouldBeTrue();

        var sent = service.Requests.ShouldHaveSingleItem();
        sent.Url.ToString().ShouldBe("https://control.msg91.com/api/v5/flow/");
        sent.AuthKey.ShouldBe("key");
        JsonNode body = JsonNode.Parse(sent.Body)!;
        body["flow_id"]!.GetValue<string>().ShouldBe("flow1");
        body["recipients"]![0]!["mobiles"]!.GetValue<string>().ShouldBe("919876543210");
        body["recipients"]![0]!["VAR1"]!.GetValue<string>().ShouldBe("Hello");
    }

    [Fact]
    public async Task WhatsAppCloud_SendsTheTemplate_WithTheTextFlattened()
    {
        var service = new Service();
        var settings = new Dictionary<string, string> { ["accessToken"] = "token", ["phoneNumberId"] = "123", ["templateName"] = "people_update" };
        await new WhatsAppCloudAdapter(service).RunAsync("send", settings, Sms(text: "Line one\n\n  Line\ttwo"), Ct);

        var sent = service.Requests.ShouldHaveSingleItem();
        sent.Url.ToString().ShouldBe("https://graph.facebook.com/v22.0/123/messages");
        sent.Auth.ShouldBe("Bearer token");
        JsonNode body = JsonNode.Parse(sent.Body)!;
        body["messaging_product"]!.GetValue<string>().ShouldBe("whatsapp");
        body["to"]!.GetValue<string>().ShouldBe("919876543210");
        body["template"]!["name"]!.GetValue<string>().ShouldBe("people_update");
        body["template"]!["language"]!["code"]!.GetValue<string>().ShouldBe("en");
        body["template"]!["components"]![0]!["parameters"]![0]!["text"]!.GetValue<string>().ShouldBe("Line one Line two");

        settings["apiVersion"] = "v30.0";
        await new WhatsAppCloudAdapter(service).RunAsync("send", settings, Sms(), Ct);
        service.Requests[1].Url.ToString().ShouldStartWith("https://graph.facebook.com/v30.0/");

        settings["apiVersion"] = "../../evil";
        await new WhatsAppCloudAdapter(service).RunAsync("send", settings, Sms(), Ct);
        service.Requests[2].Url.ToString().ShouldStartWith("https://graph.facebook.com/v22.0/", Case.Sensitive, "a version that is not one falls back to the default");
    }

    [Fact]
    public async Task SendGrid_PostsTheMessage_AndSaysWhyItWasRefused()
    {
        var service = new Service(HttpStatusCode.Accepted);
        var settings = new Dictionary<string, string> { ["apiKey"] = "SG.key", ["fromAddress"] = "people@acme.example", ["fromName"] = "Acme People" };
        var message = new Dictionary<string, string> { ["to"] = "priya@acme.example", ["subject"] = "Hello", ["body"] = "Welcome" };
        (await new SendGridAdapter(service).RunAsync("send", settings, message, Ct)).Succeeded.ShouldBeTrue();

        var sent = service.Requests.ShouldHaveSingleItem();
        sent.Url.ToString().ShouldBe("https://api.sendgrid.com/v3/mail/send");
        sent.Auth.ShouldBe("Bearer SG.key");
        JsonNode body = JsonNode.Parse(sent.Body)!;
        body["personalizations"]![0]!["to"]![0]!["email"]!.GetValue<string>().ShouldBe("priya@acme.example");
        body["from"]!["name"]!.GetValue<string>().ShouldBe("Acme People");
        body["content"]![0]!["type"]!.GetValue<string>().ShouldBe("text/plain");
        body["content"]![0]!["value"]!.GetValue<string>().ShouldBe("Welcome");

        IntegrationResult refused = await new SendGridAdapter(new Service(HttpStatusCode.Forbidden, "{\"errors\":[{\"message\":\"sender not verified\"}]}")).RunAsync("send", settings, message, Ct);
        refused.Succeeded.ShouldBeFalse();
        refused.Message.ShouldContain("403");
        refused.Message.ShouldContain("sender not verified");
    }

    [Fact]
    public void Microsoft365_SendsPlainText_WithoutFillingSentItems()
    {
        JsonObject message = Microsoft365MailAdapter.Message("priya@acme.example", "Hello", "Welcome");

        message["message"]!["toRecipients"]![0]!["emailAddress"]!["address"]!.GetValue<string>().ShouldBe("priya@acme.example");
        message["message"]!["body"]!["contentType"]!.GetValue<string>().ShouldBe("Text");
        message["saveToSentItems"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Theory]
    [InlineData("127.0.0.1", "inside the hosting network")]
    [InlineData("10.0.0.5", "inside the hosting network")]
    [InlineData("", "missing")]
    public async Task AnSmtpServerInside_IsRefused(string host, string reason)
        => (await OutboundUrlGuard.CheckHostAsync(host, Ct)).ShouldNotBeNull().ShouldContain(reason);

    [Fact]
    public async Task Smtp_RefusesWhatItCannotDoSafely_BeforeConnecting()
    {
        var message = new Dictionary<string, string> { ["to"] = "priya@acme.example", ["subject"] = "Hello", ["body"] = "Welcome" };
        var adapter = new SmtpAdapter();

        (await adapter.RunAsync("send", new Dictionary<string, string> { ["host"] = "mail.example.com", ["port"] = "465" }, message, Ct)).Message.ShouldContain("587");
        (await adapter.RunAsync("send", new Dictionary<string, string> { ["host"] = "mail.example.com", ["port"] = "abc" }, message, Ct)).Message.ShouldContain("must be a number");
        (await adapter.RunAsync("send", new Dictionary<string, string> { ["host"] = "127.0.0.1", ["fromAddress"] = "a@b.example" }, message, Ct)).Message.ShouldContain("hosting network");
    }

    [Fact]
    public void EveryChannelConnector_CanCarryItsChannel()
    {
        var service = new Service();
        IIntegrationAdapter[] email = [new SmtpAdapter(), new SendGridAdapter(service), new Microsoft365MailAdapter(service)];
        IIntegrationAdapter[] sms = [new TwilioSmsAdapter(service), new TwilioWhatsAppAdapter(service), new Msg91Adapter(service), new WhatsAppCloudAdapter(service)];

        email.ShouldAllBe(adapter => adapter.Channel == IntegrationChannels.Email && adapter.Automatable);
        email.ShouldAllBe(adapter => adapter.Operations.Single().Key == "send" && adapter.Operations.Single().Parameters.Select(parameter => parameter.Key).SequenceEqual(new[] { "to", "subject", "body" }));
        sms.ShouldAllBe(adapter => adapter.Channel == IntegrationChannels.Sms && adapter.Automatable);
        sms.ShouldAllBe(adapter => adapter.Operations.Single().Key == "send" && adapter.Operations.Single().Parameters.Select(parameter => parameter.Key).SequenceEqual(new[] { "to", "text" }));
    }
}
