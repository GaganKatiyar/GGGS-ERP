using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Ai.Application;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// M12 assistants end to end, against a scripted model: tools run as the user and are offered only
/// when the user may use them, action tools only propose, and a proposal runs only on approval.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class AiTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, DataScope)[] Employee =
    [
        (Permissions.Ai.AssistantUse, DataScope.Own),
        (Permissions.Self.LeaveView, DataScope.Own),
        (Permissions.Self.TicketCreate, DataScope.Own),
        (Permissions.Self.ProfileView, DataScope.Own),
    ];

    private async Task<HttpClient> EmployeeAsync()
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        return await factory.CreateScopedUserClientAsync(Email("ai"), person, Employee);
    }

    [Fact]
    public async Task A_read_tool_runs_as_the_user_and_its_result_reaches_the_model()
    {
        using HttpClient self = await EmployeeAsync();
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("my_leave_balances", new { }, "Let me check your balances.");
        factory.Llm.ReplyText("You have leave available.");

        JsonElement status = await OkAsync(await self.GetAsync("/api/v1/ai/status", Ct));
        status.GetProperty("configured").GetBoolean().ShouldBeTrue();
        JsonElement[] assistants = [.. status.GetProperty("assistants").EnumerateArray()];
        assistants.ShouldContain(assistant => assistant.GetProperty("key").GetString() == "me");
        assistants.ShouldNotContain(assistant => assistant.GetProperty("key").GetString() == "hr");

        JsonElement view = await OkAsync(await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "How much leave do I have?" }, Ct));
        JsonElement[] messages = [.. view.GetProperty("messages").EnumerateArray()];
        messages.Length.ShouldBe(2);
        messages[0].GetProperty("role").GetString().ShouldBe("user");
        messages[1].GetProperty("text").GetString()!.ShouldContain("You have leave available.");
        messages[1].GetProperty("toolsUsed").EnumerateArray().Select(tool => tool.GetString()).ShouldContain("my_leave_balances");

        LlmRequest[] requests = [.. factory.Llm.Requests];
        requests.Length.ShouldBe(2);
        requests[0].Tools.Select(tool => tool.Name).ShouldContain("my_leave_balances");
        requests[0].Tools.Select(tool => tool.Name).ShouldNotContain("get_metrics");
        requests[0].System.ShouldContain("My assistant");
        LlmToolResult result = requests[1].Messages[^1].Blocks.OfType<LlmToolResult>().Single();
        result.IsError.ShouldBeFalse();
        result.Content.ShouldStartWith("[");

        Guid id = view.GetProperty("conversation").GetProperty("id").GetGuid();
        (await OkAsync(await self.GetAsync("/api/v1/ai/conversations", Ct))).EnumerateArray().ShouldContain(row => row.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task A_tool_the_user_may_not_use_is_refused_even_when_the_model_asks()
    {
        using HttpClient self = await EmployeeAsync();
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("get_metrics", new { area = "payroll" });
        factory.Llm.ReplyText("I can't see that.");

        await OkAsync(await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "What is the payroll cost?" }, Ct));
        LlmToolResult result = factory.Llm.Requests.Last().Messages[^1].Blocks.OfType<LlmToolResult>().Single();
        result.IsError.ShouldBeTrue();
        result.Content.ShouldContain("not available");
        (await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "hr", Message = "Headcount?" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_action_is_only_proposed_and_runs_when_the_user_approves_it()
    {
        using HttpClient self = await EmployeeAsync();
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("raise_ticket", new { categoryCode = "IT", subject = "Laptop will not boot", description = "Black screen since this morning.", priority = "High" });
        factory.Llm.ReplyText("I've drafted a ticket for you to approve.");

        JsonElement view = await OkAsync(await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "My laptop won't boot, raise a ticket." }, Ct));
        JsonElement action = view.GetProperty("actions").EnumerateArray().Single();
        action.GetProperty("status").GetString().ShouldBe("Proposed");
        action.GetProperty("summary").GetString()!.ShouldContain("Laptop will not boot");
        view.GetProperty("messages").EnumerateArray().Last().GetProperty("actionIds").GetArrayLength().ShouldBe(1);
        factory.Llm.Requests.Last().Messages[^1].Blocks.OfType<LlmToolResult>().Single().Content.ShouldContain("has NOT been done");
        (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/tickets", Ct))!.ShouldNotContain(ticket => ticket.GetProperty("subject").GetString() == "Laptop will not boot");

        Guid actionId = action.GetProperty("id").GetGuid();
        JsonElement approved = await OkAsync(await self.PostAsync($"/api/v1/ai/actions/{actionId}/approve", null, Ct));
        approved.GetProperty("status").GetString().ShouldBe("Executed");
        approved.GetProperty("result").GetString()!.ShouldStartWith("Ticket TKT-");
        (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/tickets", Ct))!.ShouldContain(ticket => ticket.GetProperty("subject").GetString() == "Laptop will not boot");
        (await CoreHrApi.ErrorCodeAsync(await self.PostAsync($"/api/v1/ai/actions/{actionId}/approve", null, Ct))).ShouldBe("AI_ACTION_DECIDED");

        // The next turn tells the model what became of its proposal.
        factory.Llm.ReplyText("Glad it's sorted.");
        Guid conversation = view.GetProperty("conversation").GetProperty("id").GetGuid();
        await OkAsync(await self.PostAsJsonAsync($"/api/v1/ai/conversations/{conversation}/messages", new { Message = "Thanks!" }, Ct));
        factory.Llm.Requests.Last().Messages[^1].Blocks.OfType<LlmText>().First().Text.ShouldContain("was executed");
    }

    [Fact]
    public async Task A_rejected_proposal_never_runs_and_another_user_cannot_touch_it()
    {
        using HttpClient self = await EmployeeAsync();
        factory.Llm.Reset();
        factory.Llm.ReplyToolUse("raise_ticket", new { categoryCode = "ADMIN", subject = "Desk lamp broken", description = "Needs replacing." });
        factory.Llm.ReplyText("Drafted.");
        JsonElement view = await OkAsync(await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "My lamp is broken" }, Ct));
        Guid actionId = view.GetProperty("actions").EnumerateArray().Single().GetProperty("id").GetGuid();

        using HttpClient other = await EmployeeAsync();
        (await other.PostAsync($"/api/v1/ai/actions/{actionId}/approve", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonElement rejected = await OkAsync(await self.PostAsync($"/api/v1/ai/actions/{actionId}/reject", null, Ct));
        rejected.GetProperty("status").GetString().ShouldBe("Rejected");
        (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/tickets", Ct))!.ShouldNotContain(ticket => ticket.GetProperty("subject").GetString() == "Desk lamp broken");
    }

    [Fact]
    public async Task Assistant_turns_are_rate_limited_per_user()
    {
        using HttpClient self = await EmployeeAsync();
        factory.Llm.Reset();
        for (int turn = 0; turn < 6; turn++)
        {
            factory.Llm.ReplyText("Hello!");
            (await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "Hi" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        HttpResponseMessage limited = await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "Hi" }, Ct);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        (await CoreHrApi.ErrorCodeAsync(limited)).ShouldBe("RATE_LIMITED");

        // Someone else is not held up by it.
        using HttpClient other = await EmployeeAsync();
        factory.Llm.ReplyText("Hello!");
        (await other.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "Hi" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Usage_is_reported_by_assistant_for_administrators_only()
    {
        using HttpClient self = await EmployeeAsync();
        factory.Llm.Reset();
        factory.Llm.ReplyText("Hello!");
        await OkAsync(await self.PostAsJsonAsync("/api/v1/ai/conversations", new { Assistant = "me", Message = "Hi" }, Ct));
        (await self.GetAsync("/api/v1/ai/usage", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using HttpClient admin = await factory.CreateAdminClientAsync();
        JsonElement usage = await OkAsync(await admin.GetAsync("/api/v1/ai/usage", Ct));
        JsonElement me = usage.EnumerateArray().Single(row => row.GetProperty("assistantKey").GetString() == "me");
        me.GetProperty("turns").GetInt32().ShouldBeGreaterThan(0);
        me.GetProperty("inputTokens").GetInt64().ShouldBeGreaterThan(0);
    }
}
