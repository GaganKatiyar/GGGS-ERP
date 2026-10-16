using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Compliance.Application;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Compliance.Domain;

namespace Gggs.Modules.Compliance.Endpoints;

/// <summary>The compliance centre under <c>/api/v1/compliance</c> (spec §22, §107).</summary>
internal static class ComplianceEndpoints
{
    public static IEndpointRouteBuilder MapComplianceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/compliance").WithTags("Compliance");

        group.MapGet("/dashboard", async (ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.DashboardAsync(ct)))
            .WithName("ComplianceDashboard").RequirePermission(Permissions.Compliance.RuleView, Permissions.Compliance.FilingView);

        // Statutory rules
        group.MapGet("/rules", async (StatutoryRuleType? type, string? state, RuleService rules, CancellationToken ct) => TypedResults.Ok(await rules.ListAsync(type, state, ct)))
            .WithName("ListStatutoryRules").RequirePermission(Permissions.Compliance.RuleView);
        group.MapGet("/rules/resolve", async (StatutoryRuleType type, string? state, DateOnly on, IStatutoryRuleBook book, CancellationToken ct) =>
                await book.ResolveAsync(type, state, on, ct) is { } version ? Results.Ok(version) : Results.NotFound())
            .WithName("ResolveStatutoryRule").RequirePermission(Permissions.Compliance.RuleView);
        group.MapGet("/rules/codes/{code}", async (string code, RuleService rules, CancellationToken ct) => TypedResults.Ok(await rules.VersionsAsync(code, ct)))
            .WithName("StatutoryRuleVersions").RequirePermission(Permissions.Compliance.RuleView);
        group.MapGet("/rules/{id:guid}", async (Guid id, RuleService rules, CancellationToken ct) => TypedResults.Ok(await rules.GetAsync(id, ct)))
            .WithName("GetStatutoryRule").RequirePermission(Permissions.Compliance.RuleView);
        group.MapPost("/rules", async (RuleBody body, RuleService rules, CancellationToken ct) => TypedResults.Ok(await rules.CreateAsync(body, ct)))
            .WithName("CreateStatutoryRule").RequirePermission(Permissions.Compliance.RuleManage);
        group.MapPost("/rules/{id:guid}/versions", async (Guid id, NewVersionBody body, RuleService rules, CancellationToken ct) => TypedResults.Ok(await rules.DraftNextVersionAsync(id, body, ct)))
            .WithName("DraftStatutoryRuleVersion").RequirePermission(Permissions.Compliance.RuleManage);
        group.MapPut("/rules/{id:guid}", async (Guid id, DraftBody body, RuleService rules, CancellationToken ct) => TypedResults.Ok(await rules.UpdateDraftAsync(id, body, ct)))
            .WithName("UpdateStatutoryRuleDraft").RequirePermission(Permissions.Compliance.RuleManage);
        group.MapDelete("/rules/{id:guid}", async (Guid id, RuleService rules, CancellationToken ct) =>
            {
                await rules.DiscardDraftAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DiscardStatutoryRuleDraft").RequirePermission(Permissions.Compliance.RuleManage);
        group.MapPost("/rules/{id:guid}/publish", async (Guid id, RuleService rules, CancellationToken ct) => TypedResults.Ok(await rules.PublishAsync(id, ct)))
            .WithName("PublishStatutoryRule").RequirePermission(Permissions.Compliance.RuleManage);
        group.MapPost("/rules/{id:guid}/retire", async (Guid id, RetireBody body, RuleService rules, CancellationToken ct) => TypedResults.Ok(await rules.RetireAsync(id, body.LastDay, ct)))
            .WithName("RetireStatutoryRule").RequirePermission(Permissions.Compliance.RuleManage);

        // Obligations and filings
        group.MapGet("/obligations", async (ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.ObligationsAsync(ct)))
            .WithName("ListComplianceObligations").RequirePermission(Permissions.Compliance.FilingView);
        group.MapPost("/obligations", async (ObligationBody body, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.SaveObligationAsync(null, body, ct)))
            .WithName("CreateComplianceObligation").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapPut("/obligations/{id:guid}", async (Guid id, ObligationBody body, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.SaveObligationAsync(id, body, ct)))
            .WithName("UpdateComplianceObligation").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapPost("/obligations/{id:guid}/activate", async (Guid id, ComplianceCalendar calendar, CancellationToken ct) =>
            {
                await calendar.SetObligationActiveAsync(id, true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ActivateComplianceObligation").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapPost("/obligations/{id:guid}/deactivate", async (Guid id, ComplianceCalendar calendar, CancellationToken ct) =>
            {
                await calendar.SetObligationActiveAsync(id, false, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeactivateComplianceObligation").RequirePermission(Permissions.Compliance.FilingManage);

        group.MapGet("/filings", async ([AsParameters] FilingQuery query, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.FilingsAsync(query, ct)))
            .WithName("ListComplianceFilings").RequirePermission(Permissions.Compliance.FilingView);
        group.MapPost("/filings/generate", async (ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(new { generated = await calendar.GenerateAsync(ct) }))
            .WithName("GenerateComplianceFilings").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapPost("/filings/{id:guid}/file", async (Guid id, FileBody body, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.FileAsync(id, body, ct)))
            .WithName("RecordComplianceFiling").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapPost("/filings/{id:guid}/waive", async (Guid id, WaiveBody body, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.WaiveAsync(id, body.Reason, ct)))
            .WithName("WaiveComplianceFiling").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapPost("/filings/{id:guid}/reopen", async (Guid id, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.ReopenAsync(id, ct)))
            .WithName("ReopenComplianceFiling").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapPost("/filings/{id:guid}/reschedule", async (Guid id, RescheduleBody body, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.RescheduleAsync(id, body.DueDate, ct)))
            .WithName("RescheduleComplianceFiling").RequirePermission(Permissions.Compliance.FilingManage);

        // Licences, registrations, certificates and audits
        group.MapGet("/registrations", async (ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.RegistrationsAsync(ct)))
            .WithName("ListComplianceRegistrations").RequirePermission(Permissions.Compliance.FilingView);
        group.MapPost("/registrations", async (RegistrationDetails body, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.SaveRegistrationAsync(null, body, ct)))
            .WithName("CreateComplianceRegistration").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapPut("/registrations/{id:guid}", async (Guid id, RegistrationDetails body, ComplianceCalendar calendar, CancellationToken ct) => TypedResults.Ok(await calendar.SaveRegistrationAsync(id, body, ct)))
            .WithName("UpdateComplianceRegistration").RequirePermission(Permissions.Compliance.FilingManage);
        group.MapDelete("/registrations/{id:guid}", async (Guid id, ComplianceCalendar calendar, CancellationToken ct) =>
            {
                await calendar.DeleteRegistrationAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteComplianceRegistration").RequirePermission(Permissions.Compliance.FilingManage);

        return endpoints;
    }
}
