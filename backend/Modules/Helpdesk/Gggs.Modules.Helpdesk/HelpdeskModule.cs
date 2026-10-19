using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Helpdesk.Application;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Platform.Contracts;
using Hangfire;

namespace Gggs.Modules.Helpdesk;

/// <summary>The employee helpdesk and knowledge base (spec: helpdesk).</summary>
public sealed class HelpdeskModule : IModule
{
    public string Name => "helpdesk";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IMetricSource, HelpdeskMetrics>();
        services.AddScoped<ISearchSource, KnowledgeSearch>();
        services.AddScoped<HelpdeskService>();
        services.AddScoped<HelpdeskSeeder>();
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<TenantProvisionedIntegrationEvent, SeedHelpdeskOnTenantProvisioned>();
        services.AddScoped<HelpdeskSweep>();
        services.AddScoped<HelpdeskSweepJob>();
        services.AddHostedService<HelpdeskJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/helpdesk").WithTags("Helpdesk");

        group.MapGet("/dashboard", async (HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.DashboardAsync(ct)))
            .WithName("HelpdeskDashboard").RequirePermission(Permissions.Helpdesk.TicketView);
        group.MapGet("/categories", async (HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.CategoriesAsync(ct)))
            .WithName("ListHelpdeskCategories").RequirePermission(Permissions.Helpdesk.TicketView, Permissions.Self.TicketCreate);
        group.MapPost("/categories", async (CategoryBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCategoryAsync(null, body, ct)))
            .WithName("CreateHelpdeskCategory").RequirePermission(Permissions.Helpdesk.CategoryManage);
        group.MapPut("/categories/{id:guid}", async (Guid id, CategoryBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.SaveCategoryAsync(id, body, ct)))
            .WithName("UpdateHelpdeskCategory").RequirePermission(Permissions.Helpdesk.CategoryManage);

        string[] ticketAccess = [Permissions.Helpdesk.TicketView, Permissions.Self.TicketCreate];
        group.MapGet("/tickets", async ([AsParameters] TicketQuery query, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.TicketsAsync(query, ct)))
            .WithName("ListTickets").RequirePermission(Permissions.Helpdesk.TicketView);
        group.MapGet("/tickets/{id:guid}", async (Guid id, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.TicketAsync(id, ct)))
            .WithName("GetTicket").RequirePermission(ticketAccess);
        group.MapPost("/tickets/{id:guid}/comments", async (Guid id, CommentBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.CommentAsync(id, body, ct)))
            .WithName("CommentOnTicket").RequirePermission(ticketAccess);
        group.MapPost("/tickets/{id:guid}/assign", async (Guid id, AssignBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.AssignAsync(id, body.AssigneeEmployeeId, ct)))
            .WithName("AssignTicket").RequirePermission(Permissions.Helpdesk.TicketManage);
        group.MapPost("/tickets/{id:guid}/status", async (Guid id, StatusBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.ChangeStatusAsync(id, body, ct)))
            .WithName("ChangeTicketStatus").RequirePermission(Permissions.Helpdesk.TicketView);
        group.MapPost("/tickets/{id:guid}/recategorise", async (Guid id, RecategoriseBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.RecategoriseAsync(id, body, ct)))
            .WithName("RecategoriseTicket").RequirePermission(Permissions.Helpdesk.TicketManage);

        string[] knowledgeAccess = [Permissions.Self.TicketCreate, Permissions.Helpdesk.TicketView, Permissions.Helpdesk.KnowledgeManage];
        group.MapGet("/knowledge", async (string? search, Guid? categoryId, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.ArticlesAsync(search, categoryId, ct)))
            .WithName("SearchKnowledge").RequirePermission(knowledgeAccess);
        group.MapGet("/knowledge/{id:guid}", async (Guid id, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.ArticleAsync(id, ct)))
            .WithName("GetKnowledgeArticle").RequirePermission(knowledgeAccess);
        group.MapPost("/knowledge/{id:guid}/vote", async (Guid id, VoteBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.VoteAsync(id, body.Helpful, ct)))
            .WithName("VoteKnowledgeArticle").RequirePermission(knowledgeAccess);
        group.MapPost("/knowledge", async (ArticleBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.SaveArticleAsync(null, body, ct)))
            .WithName("CreateKnowledgeArticle").RequirePermission(Permissions.Helpdesk.KnowledgeManage);
        group.MapPut("/knowledge/{id:guid}", async (Guid id, ArticleBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.SaveArticleAsync(id, body, ct)))
            .WithName("UpdateKnowledgeArticle").RequirePermission(Permissions.Helpdesk.KnowledgeManage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/tickets").WithTags("Helpdesk");
        me.MapGet("/", async (HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.MyTicketsAsync(ct)))
            .WithName("MyTickets").RequirePermission(Permissions.Self.TicketCreate);
        me.MapPost("/", async (RaiseBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.RaiseAsync(body, ct)))
            .WithName("RaiseTicket").RequirePermission(Permissions.Self.TicketCreate);
        me.MapPost("/{id:guid}/reopen", async (Guid id, ReopenBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.ReopenAsync(id, body.Reason, ct)))
            .WithName("ReopenTicket").RequirePermission(Permissions.Self.TicketCreate);
        me.MapPost("/{id:guid}/close", async (Guid id, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.CloseAsync(id, ct)))
            .WithName("CloseTicket").RequirePermission(Permissions.Self.TicketCreate);
        me.MapPost("/{id:guid}/cancel", async (Guid id, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.CancelAsync(id, ct)))
            .WithName("CancelTicket").RequirePermission(Permissions.Self.TicketCreate);
        me.MapPost("/{id:guid}/rate", async (Guid id, RateBody body, HelpdeskService service, CancellationToken ct) => TypedResults.Ok(await service.RateAsync(id, body, ct)))
            .WithName("RateTicket").RequirePermission(Permissions.Self.TicketCreate);
    }
}

internal sealed class SeedHelpdeskOnTenantProvisioned(HelpdeskSeeder seeder) : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

/// <summary>Fans the half-hourly SLA sweep out to every tenant.</summary>
internal sealed class HelpdeskSweep(ITenantFanOutScheduler fanOut)
{
    public const string RecurringJobName = "helpdesk-sla-sweep";

    public Task RunAsync() => fanOut.FanOutAsync<HelpdeskSweepJob>((job, tenantId) => job.RunAsync(tenantId));
}

internal sealed class HelpdeskSweepJob(HelpdeskService helpdesk, ILogger<HelpdeskSweepJob> logger)
{
    public async Task RunAsync(Guid tenantId)
    {
        (int breached, int closed) = await helpdesk.SweepAsync(CancellationToken.None);
        if (breached + closed > 0)
        {
            logger.LogInformation("Helpdesk sweep for tenant {TenantId}: {Breached} SLA breach(es), {Closed} ticket(s) auto-closed.", tenantId, breached, closed);
        }
    }
}

internal sealed class HelpdeskJobRegistration(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<HelpdeskSweep>(HelpdeskSweep.RecurringJobName, sweep => sweep.RunAsync(), "*/30 * * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
