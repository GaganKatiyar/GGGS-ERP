using System.Net;
using System.Text.Json.Nodes;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Studio.Application;
using Gggs.Modules.Studio.Domain;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>Automation rules: conditions, placeholders, the retry schedule and a delivery's life.</summary>
public sealed class StudioAutomationTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["employeeName"] = "Priya Sharma",
        ["department"] = "Engineering",
    };

    [Fact]
    public void Conditions_CompareWithoutCase_AndAMissingFieldIsEmpty()
    {
        AutomationRules.Matches([new("department", ConditionOperator.Equals, "engineering")], Fields).ShouldBeTrue();
        AutomationRules.Matches([new("department", ConditionOperator.NotEquals, "Sales")], Fields).ShouldBeTrue();
        AutomationRules.Matches([new("employeeName", ConditionOperator.Contains, "SHARMA")], Fields).ShouldBeTrue();
        AutomationRules.Matches([new("location", ConditionOperator.Equals, string.Empty)], Fields).ShouldBeTrue();
        AutomationRules.Matches([new("department", ConditionOperator.Equals, "Engineering"), new("employeeName", ConditionOperator.Contains, "Rao")], Fields).ShouldBeFalse();
        AutomationRules.Matches([], Fields).ShouldBeTrue();
    }

    [Fact]
    public void Placeholders_AreFilled_AndUnknownOnesVanish()
    {
        AutomationRules.Render("{{employeeName}} joined {{ department }}{{nickname}}.", Fields).ShouldBe("Priya Sharma joined Engineering.");
        AutomationRules.Render("No placeholders {single}", Fields).ShouldBe("No placeholders {single}");
        AutomationRules.RenderAll(new Dictionary<string, string> { ["text"] = "Hi {{EMPLOYEENAME}}" }, Fields)["text"].ShouldBe("Hi Priya Sharma");
    }

    [Fact]
    public void Retries_BackOff_ThenStop()
    {
        AutomationRules.RetryDelay(1).ShouldBe(TimeSpan.FromMinutes(1));
        AutomationRules.RetryDelay(2).ShouldBe(TimeSpan.FromMinutes(5));
        AutomationRules.RetryDelay(3).ShouldBe(TimeSpan.FromMinutes(15));
        AutomationRules.RetryDelay(4).ShouldBe(TimeSpan.FromMinutes(60));
        AutomationRules.RetryDelay(AutomationRules.MaxAttempts).ShouldBeNull();
    }

    [Fact]
    public void ADelivery_FailsFiveTimes_ThenIsAbandoned_AndCanBeRetriedByHand()
    {
        AutomationDelivery delivery = AutomationDelivery.Queue(Guid.NewGuid(), Guid.NewGuid(), "employee.hired", new Dictionary<string, string> { ["text"] = "Hi" }, Now);
        delivery.IsDue(Now).ShouldBeTrue();

        delivery.Record(false, "Slack answered 500.", Now);
        delivery.Status.ShouldBe(DeliveryStatus.Failed);
        delivery.NextAttemptAt.ShouldBe(Now.AddMinutes(1));
        delivery.IsDue(Now).ShouldBeFalse();

        for (int attempt = 2; attempt <= AutomationRules.MaxAttempts; attempt++)
        {
            delivery.Record(false, "Slack answered 500.", Now);
        }

        delivery.Status.ShouldBe(DeliveryStatus.Abandoned);
        delivery.Attempts.ShouldBe(5);
        delivery.NextAttemptAt.ShouldBeNull();

        delivery.RetryNow(Now);
        delivery.IsDue(Now).ShouldBeTrue();
        delivery.Record(true, "Posted to Slack.", Now);
        delivery.Status.ShouldBe(DeliveryStatus.Succeeded);
        delivery.CompletedAt.ShouldBe(Now);
        delivery.Payload["text"].ShouldBe("Hi");
        Should.Throw<Gggs.BuildingBlocks.Domain.Exceptions.DomainRuleException>(() => delivery.RetryNow(Now));
    }

    [Fact]
    public void AnAutomation_KeepsItsConditionsAndParameters_Trimmed()
    {
        var automation = Automation.Create(" Welcome ", "employee.hired", [new(" department ", ConditionOperator.Equals, " Engineering ")], Guid.NewGuid(), "post", new Dictionary<string, string> { ["text"] = "{{employeeName}}", [" "] = "dropped" });

        automation.Name.ShouldBe("Welcome");
        automation.Conditions.ShouldHaveSingleItem().ShouldBe(new AutomationCondition("department", ConditionOperator.Equals, "Engineering"));
        automation.Parameters.Keys.ShouldBe(["text"]);
        Should.Throw<Gggs.BuildingBlocks.Domain.Exceptions.DomainRuleException>(() => automation.Update("Too many", "employee.hired", [.. Enumerable.Repeat(new AutomationCondition("department", ConditionOperator.Equals, "x"), 11)], Guid.NewGuid(), "post", new Dictionary<string, string>()));
    }

    [Fact]
    public void EveryTrigger_HasAUniqueCode_AndSampleForEachField()
    {
        StudioTriggers.All.Select(trigger => trigger.Code).ShouldBeUnique();
        StudioTriggers.All.Select(trigger => trigger.EventType).ShouldBeUnique();
        StudioTriggers.All.ShouldAllBe(trigger => trigger.Fields.Count > 0 && trigger.Fields.All(field => field.Sample.Length > 0));
        StudioTriggers.Find("EMPLOYEE.HIRED")!.SampleFields["employeeName"].ShouldBe("Priya Sharma");
        StudioTriggers.Find("nothing").ShouldBeNull();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void InternalAddresses_AreRefused(string address) => OutboundUrlGuard.IsInternal(IPAddress.Parse(address)).ShouldBeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("2001:4860:4860::8888")]
    public void PublicAddresses_AreAllowed(string address) => OutboundUrlGuard.IsInternal(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    [InlineData("http://hooks.example.com/x")]
    [InlineData("https://user:secret@hooks.example.com/x")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://[::1]/hook")]
    [InlineData("not a url")]
    public async Task UnsafeUrls_AreRefused(string url) => (await OutboundUrlGuard.CheckAsync(url, TestContext.Current.CancellationToken)).ShouldNotBeNull();

    [Fact]
    public async Task TheHandler_FollowsNoRedirects_AndRefusesToConnectInside()
    {
        using SocketsHttpHandler handler = OutboundUrlGuard.CreateHandler();
        handler.AllowAutoRedirect.ShouldBeFalse();

        using var client = new HttpClient(handler);
        HttpRequestException refused = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://localhost:5299/"), TestContext.Current.CancellationToken));
        refused.ToString().ShouldContain("inside the hosting network");
    }

    [Fact]
    public void SlackMessage_HasABoldTitle_AndAButtonOnlyForAnAbsoluteLink()
    {
        JsonObject message = SlackAdapter.Message("New joiner", "Priya joined Engineering", "https://people.example.com/employees/1");

        message["text"]!.GetValue<string>().ShouldBe("*New joiner*\nPriya joined Engineering");
        message["blocks"]!.AsArray().Count.ShouldBe(2);
        message["blocks"]![1]!["elements"]![0]!["url"]!.GetValue<string>().ShouldBe("https://people.example.com/employees/1");
        SlackAdapter.Message(null, "Plain", "/approvals")["blocks"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void TeamsMessage_IsOneAdaptiveCard()
    {
        JsonObject message = TeamsAdapter.Message("New joiner", "Priya joined Engineering", "https://people.example.com/employees/1");

        message["type"]!.GetValue<string>().ShouldBe("message");
        JsonNode attachment = message["attachments"]!.AsArray().ShouldHaveSingleItem()!;
        attachment["contentType"]!.GetValue<string>().ShouldBe("application/vnd.microsoft.card.adaptive");
        attachment["content"]!["body"]!.AsArray().Count.ShouldBe(2);
        attachment["content"]!["actions"]![0]!["url"]!.GetValue<string>().ShouldBe("https://people.example.com/employees/1");
        TeamsAdapter.Message(null, "Plain", null)["attachments"]![0]!["content"]!["actions"].ShouldBeNull();
    }
}
