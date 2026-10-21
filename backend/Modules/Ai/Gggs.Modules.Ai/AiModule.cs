using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Ai.Application;
using Hangfire;

namespace Gggs.Modules.Ai;

/// <summary>The AI tool layer and assistants (spec §89).</summary>
public sealed class AiModule : IModule
{
    public string Name => "ai";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        services.Configure<AiOptions>(context.Configuration.GetSection(AiOptions.Section));
        services.AddScoped<AiService>();
        services.AddSingleton<ILlmClient, AnthropicLlmClient>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<AiRetentionSweep>();
        services.AddScoped<AiRetentionJob>();
        services.AddHostedService<AiJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/ai").WithTags("AI");

        group.MapGet("/status", async (AiService service, CancellationToken ct) => TypedResults.Ok(await service.StatusAsync(ct)))
            .WithName("AiStatus").RequirePermission(Permissions.Ai.AssistantUse);
        group.MapGet("/conversations", async (AiService service, CancellationToken ct) => TypedResults.Ok(await service.ConversationsAsync(ct)))
            .WithName("AiConversations").RequirePermission(Permissions.Ai.AssistantUse);
        group.MapPost("/conversations", async (SendBody body, AiService service, CancellationToken ct) => TypedResults.Ok(await service.SendAsync(null, body, ct)))
            .WithName("StartAiConversation").RequirePermission(Permissions.Ai.AssistantUse).RequireRateLimiting(RateLimitPolicies.Assistant);
        group.MapGet("/conversations/{id:guid}", async (Guid id, AiService service, CancellationToken ct) => TypedResults.Ok(await service.ConversationAsync(id, ct)))
            .WithName("GetAiConversation").RequirePermission(Permissions.Ai.AssistantUse);
        group.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendBody body, AiService service, CancellationToken ct) => TypedResults.Ok(await service.SendAsync(id, body, ct)))
            .WithName("SendAiMessage").RequirePermission(Permissions.Ai.AssistantUse).RequireRateLimiting(RateLimitPolicies.Assistant);
        group.MapDelete("/conversations/{id:guid}", async (Guid id, AiService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteAiConversation").RequirePermission(Permissions.Ai.AssistantUse);
        group.MapPost("/actions/{id:guid}/approve", async (Guid id, AiService service, CancellationToken ct) => TypedResults.Ok(await service.ApproveAsync(id, ct)))
            .WithName("ApproveAiAction").RequirePermission(Permissions.Ai.AssistantUse);
        group.MapPost("/actions/{id:guid}/reject", async (Guid id, AiService service, CancellationToken ct) => TypedResults.Ok(await service.RejectAsync(id, ct)))
            .WithName("RejectAiAction").RequirePermission(Permissions.Ai.AssistantUse);
        group.MapGet("/usage", async (int? days, AiService service, CancellationToken ct) => TypedResults.Ok(await service.UsageAsync(days ?? 30, ct)))
            .WithName("AiUsage").RequirePermission(Permissions.Ai.UsageView);
    }
}

/// <summary>Fans the daily conversation purge out to every tenant.</summary>
[PlatformJob("AI retention sweep: fans out one retention clean-up per active tenant.")]
internal sealed class AiRetentionSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "ai-conversation-retention";

    public Task RunAsync() => fanOut.FanOutAsync<AiRetentionJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class AiRetentionJob(AiService ai, ILogger<AiRetentionJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        int purged = await ai.PurgeAsync(CancellationToken.None);
        if (purged > 0)
        {
            logger.LogInformation("AI retention for tenant {TenantId}: {Purged} conversation(s) deleted.", tenantId, purged);
        }
    }
}

internal sealed class AiJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<AiRetentionSweep>(AiRetentionSweep.RecurringJobName, sweep => sweep.RunAsync(), "40 2 * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
