using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Studio.Application;
using Gggs.Modules.Studio.Domain;

namespace Gggs.Modules.Studio.Endpoints;

internal static class StudioEndpoints
{
    public static void MapStudioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder studio = endpoints.MapGroup("/api/v1/studio").WithTags("Studio");

        studio.MapGet("/triggers", () => TypedResults.Ok(AutomationService.Triggers()))
            .WithName("StudioTriggers").RequirePermission(Permissions.DataHub.IntegrationView);
        studio.MapGet("/connections", async (AutomationService service, CancellationToken ct) => TypedResults.Ok(await service.ConnectionsAsync(ct)))
            .WithName("StudioConnections").RequirePermission(Permissions.DataHub.IntegrationView);

        studio.MapGet("/channels", async (ChannelService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)))
            .WithName("StudioChannels").RequirePermission(Permissions.DataHub.IntegrationView);
        studio.MapPut("/channels/{channel}", async (string channel, ChannelBody body, ChannelService service, CancellationToken ct) => TypedResults.Ok(await service.SetAsync(channel, body, ct)))
            .WithName("SetStudioChannel").RequirePermission(Permissions.DataHub.IntegrationManage);
        studio.MapPost("/channels/{channel}/test", async (string channel, ChannelTestBody body, ChannelService service, CancellationToken ct) => TypedResults.Ok(await service.TestAsync(channel, body, ct)))
            .WithName("TestStudioChannel").RequirePermission(Permissions.DataHub.IntegrationManage);

        studio.MapGet("/account-connections", async (AccountService service, CancellationToken ct) => TypedResults.Ok(await service.ConnectionsAsync(ct)))
            .WithName("AccountConnections").RequirePermission(Permissions.DataHub.IntegrationView);
        studio.MapPut("/account-connections/{id:guid}", async (Guid id, AccountConnectionBody body, AccountService service, CancellationToken ct) => TypedResults.Ok(await service.SetConnectionAsync(id, body, ct)))
            .WithName("SetAccountConnection").RequirePermission(Permissions.DataHub.IntegrationManage);
        studio.MapGet("/accounts", async (Guid? employeeId, AccountService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(employeeId, ct)))
            .WithName("ProvisionedAccounts").RequirePermission(Permissions.DataHub.IntegrationView);
        studio.MapPost("/accounts", async (RequestAccountBody body, AccountService service, CancellationToken ct) => TypedResults.Ok(await service.RequestAsync(body, ct)))
            .WithName("RequestAccount").RequirePermission(Permissions.DataHub.IntegrationManage);
        studio.MapPost("/accounts/{id:guid}/disable", async (Guid id, AccountService service, CancellationToken ct) => TypedResults.Ok(await service.DisableAsync(id, ct)))
            .WithName("DisableAccount").RequirePermission(Permissions.DataHub.IntegrationManage);
        studio.MapPost("/accounts/{id:guid}/retry", async (Guid id, AccountService service, CancellationToken ct) => TypedResults.Ok(await service.RetryAsync(id, ct)))
            .WithName("RetryAccount").RequirePermission(Permissions.DataHub.IntegrationManage);

        studio.MapGet("/automations", async (AutomationService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)))
            .WithName("Automations").RequirePermission(Permissions.DataHub.IntegrationView);
        studio.MapPost("/automations", async (AutomationBody body, AutomationService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(null, body, ct)))
            .WithName("CreateAutomation").RequirePermission(Permissions.DataHub.IntegrationManage);
        studio.MapPut("/automations/{id:guid}", async (Guid id, AutomationBody body, AutomationService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(id, body, ct)))
            .WithName("UpdateAutomation").RequirePermission(Permissions.DataHub.IntegrationManage);
        studio.MapDelete("/automations/{id:guid}", async (Guid id, AutomationService service, CancellationToken ct) => TypedResults.Ok(await service.DeleteAsync(id, ct)))
            .WithName("DeleteAutomation").RequirePermission(Permissions.DataHub.IntegrationManage);
        studio.MapPost("/automations/test", async (AutomationBody body, AutomationService service, CancellationToken ct) => TypedResults.Ok(await service.TestAsync(body, ct)))
            .WithName("TestAutomation").RequirePermission(Permissions.DataHub.IntegrationManage);

        studio.MapGet("/deliveries", async (Guid? automationId, DeliveryStatus? status, AutomationService service, CancellationToken ct) => TypedResults.Ok(await service.DeliveriesAsync(automationId, status, ct)))
            .WithName("AutomationDeliveries").RequirePermission(Permissions.DataHub.IntegrationView);
        studio.MapPost("/deliveries/{id:guid}/retry", async (Guid id, AutomationService service, CancellationToken ct) => TypedResults.Ok(await service.RetryAsync(id, ct)))
            .WithName("RetryAutomationDelivery").RequirePermission(Permissions.DataHub.IntegrationManage);
    }
}
