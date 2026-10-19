using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Recruitment.Application;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Workflow.Contracts;
using Gggs.Modules.Workforce.Contracts;

namespace Gggs.Modules.Recruitment;

/// <summary>Recruitment (spec §12): requisition to offer and hire.</summary>
public sealed class RecruitmentModule : IModule
{
    public string Name => "recruitment";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<RecruitmentAccess>();
        services.AddScoped<RecruitmentService>();
        services.AddScoped<PipelineService>();
        services.AddScoped<OfferService>();
        services.AddScoped<IDocumentOwnerAccess, CandidateDocumentAccess>();
        services.AddWorkflowSubject(OfferService.Subject);
    }

    public void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
        services.AddGggsEventConsumer<ManpowerRequestApprovedIntegrationEvent, OpenRequisitionOnRequestApproved>();
        services.AddGggsEventConsumer<WorkflowApprovedIntegrationEvent, ApproveOfferOnApproval>();
        services.AddGggsEventConsumer<WorkflowRejectedIntegrationEvent, RejectOfferOnRejection>();
        services.AddGggsEventConsumer<WorkflowSentBackIntegrationEvent, ReturnOfferOnSendBack>();
        services.AddScoped<ReferralBonusSweep>();
        services.AddScoped<ReferralBonusJob>();
        services.AddHostedService<RecruitmentJobRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/recruitment").WithTags("Recruitment");

        // Requisitions and postings.
        group.MapGet("/requisitions", async ([AsParameters] RequisitionQuery query, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.RequisitionsAsync(query, ct)))
            .WithName("ListRequisitions").RequirePermission(Permissions.Recruitment.RequisitionView);
        group.MapGet("/requisitions/{id:guid}", async (Guid id, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.RequisitionAsync(id, ct)))
            .WithName("GetRequisition").RequirePermission(Permissions.Recruitment.RequisitionView);
        group.MapPost("/requisitions", async (RequisitionDetails body, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.CreateRequisitionAsync(body, ct)))
            .WithName("CreateRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);
        group.MapPut("/requisitions/{id:guid}", async (Guid id, RequisitionDetails body, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.UpdateRequisitionAsync(id, body, ct)))
            .WithName("UpdateRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);
        group.MapPost("/requisitions/{id:guid}/{action:regex(^(hold|resume|close|cancel)$)}", async (Guid id, string action, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.ChangeRequisitionAsync(id, action, ct)))
            .WithName("ChangeRequisition").RequirePermission(Permissions.Recruitment.RequisitionManage);

        group.MapGet("/postings", async (Guid? requisitionId, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.PostingsAsync(requisitionId, ct)))
            .WithName("ListPostings").RequirePermission(Permissions.Recruitment.PostingView);
        group.MapPost("/postings", async (PostingBody body, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.SavePostingAsync(null, body, ct)))
            .WithName("CreatePosting").RequirePermission(Permissions.Recruitment.PostingManage);
        group.MapPut("/postings/{id:guid}", async (Guid id, PostingBody body, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.SavePostingAsync(id, body, ct)))
            .WithName("UpdatePosting").RequirePermission(Permissions.Recruitment.PostingManage);
        group.MapPost("/postings/{id:guid}/publish", async (Guid id, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.ChangePostingAsync(id, publish: true, ct)))
            .WithName("PublishPosting").RequirePermission(Permissions.Recruitment.PostingManage);
        group.MapPost("/postings/{id:guid}/close", async (Guid id, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.ChangePostingAsync(id, publish: false, ct)))
            .WithName("ClosePosting").RequirePermission(Permissions.Recruitment.PostingManage);

        // Candidates and the pipeline.
        group.MapGet("/candidates", async ([AsParameters] CandidateQuery query, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.CandidatesAsync(query, ct)))
            .WithName("ListCandidates").RequirePermission(Permissions.Recruitment.ApplicationView);
        group.MapGet("/candidates/{id:guid}", async (Guid id, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.CandidateAsync(id, ct)))
            .WithName("GetCandidate").RequirePermission(Permissions.Recruitment.ApplicationView);
        group.MapPost("/candidates", async (CandidateBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.AddCandidateAsync(body, ct)))
            .WithName("AddCandidate").RequirePermission(Permissions.Recruitment.ApplicationManage);
        group.MapPut("/candidates/{id:guid}", async (Guid id, CandidateDetails body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.UpdateCandidateAsync(id, body, ct)))
            .WithName("UpdateCandidate").RequirePermission(Permissions.Recruitment.ApplicationManage);
        group.MapPost("/candidates/{id:guid}/apply/{requisitionId:guid}", async (Guid id, Guid requisitionId, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.ApplyAsync(id, requisitionId, ct)))
            .WithName("ApplyCandidate").RequirePermission(Permissions.Recruitment.ApplicationManage);

        group.MapGet("/applications", async (Guid? requisitionId, ApplicationStage? stage, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.ApplicationsAsync(requisitionId, stage, ct)))
            .WithName("ListApplications").RequirePermission(Permissions.Recruitment.ApplicationView);
        group.MapGet("/applications/{id:guid}", async (Guid id, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.ApplicationAsync(id, ct)))
            .WithName("GetApplication").RequirePermission(Permissions.Recruitment.ApplicationView);
        group.MapPost("/applications/{id:guid}/move", async (Guid id, MoveBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.MoveAsync(id, body, ct)))
            .WithName("MoveApplication").RequirePermission(Permissions.Recruitment.ApplicationManage);
        group.MapPost("/applications/{id:guid}/reject", async (Guid id, ReasonBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.RejectAsync(id, body.Reason, withdrawn: false, ct)))
            .WithName("RejectApplication").RequirePermission(Permissions.Recruitment.ApplicationManage);
        group.MapPost("/applications/{id:guid}/withdraw", async (Guid id, ReasonBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.RejectAsync(id, body.Reason, withdrawn: true, ct)))
            .WithName("WithdrawApplication").RequirePermission(Permissions.Recruitment.ApplicationManage);

        group.MapGet("/interviews", async (DateOnly? from, DateOnly? to, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.InterviewsAsync(from, to, ct)))
            .WithName("ListInterviews").RequirePermission(Permissions.Recruitment.ApplicationView);
        group.MapPost("/applications/{id:guid}/interviews", async (Guid id, InterviewBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.ScheduleAsync(id, body, ct)))
            .WithName("ScheduleInterview").RequirePermission(Permissions.Recruitment.ApplicationManage);
        group.MapPut("/interviews/{id:guid}", async (Guid id, InterviewBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.RescheduleAsync(id, body, ct)))
            .WithName("RescheduleInterview").RequirePermission(Permissions.Recruitment.ApplicationManage);
        group.MapPost("/interviews/{id:guid}/{action:regex(^(complete|cancel|no-show)$)}", async (Guid id, string action, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.CloseInterviewAsync(id, action, ct)))
            .WithName("CloseInterview").RequirePermission(Permissions.Recruitment.ApplicationManage);

        group.MapPost("/background-checks", async (BackgroundCheckBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.InitiateCheckAsync(body, ct)))
            .WithName("InitiateBackgroundCheck").RequirePermission(Permissions.Recruitment.ApplicationManage);
        group.MapPost("/background-checks/{id:guid}/result", async (Guid id, BackgroundResultBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.RecordCheckAsync(id, body, ct)))
            .WithName("RecordBackgroundCheck").RequirePermission(Permissions.Recruitment.ApplicationManage);

        group.MapGet("/referrals", async (PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.ReferralsAsync(ct)))
            .WithName("ListReferrals").RequirePermission(Permissions.Recruitment.ApplicationView);

        // Offers and hiring.
        group.MapGet("/offers", async (OfferStatus? status, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.OffersAsync(status, ct)))
            .WithName("ListOffers").RequirePermission(Permissions.Recruitment.OfferView);
        group.MapGet("/offers/{id:guid}", async (Guid id, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.OfferAsync(id, ct)))
            .WithName("GetOffer").RequirePermission(Permissions.Recruitment.OfferView, Permissions.Workflow.TaskAct);
        group.MapPost("/offers", async (OfferBody body, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.DraftAsync(body, ct)))
            .WithName("DraftOffer").RequirePermission(Permissions.Recruitment.OfferManage);
        group.MapPut("/offers/{id:guid}", async (Guid id, OfferTerms body, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.UpdateAsync(id, body, ct)))
            .WithName("UpdateOffer").RequirePermission(Permissions.Recruitment.OfferManage);
        group.MapPost("/offers/{id:guid}/submit", async (Guid id, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.SubmitAsync(id, ct)))
            .WithName("SubmitOffer").RequirePermission(Permissions.Recruitment.OfferManage);
        group.MapPost("/offers/{id:guid}/send", async (Guid id, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.ActAsync(id, "send", null, ct)))
            .WithName("SendOffer").RequirePermission(Permissions.Recruitment.OfferManage);
        group.MapPost("/offers/{id:guid}/respond", async (Guid id, RespondBody body, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.ActAsync(id, "respond", body, ct)))
            .WithName("RecordOfferResponse").RequirePermission(Permissions.Recruitment.OfferManage);
        group.MapPost("/offers/{id:guid}/withdraw", async (Guid id, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.ActAsync(id, "withdraw", null, ct)))
            .WithName("WithdrawOffer").RequirePermission(Permissions.Recruitment.OfferManage);
        group.MapPost("/offers/{id:guid}/hire", async (Guid id, HireBody body, OfferService offers, CancellationToken ct) => TypedResults.Ok(await offers.HireAsync(id, body, ct)))
            .WithName("HireFromOffer").RequirePermission(Permissions.Recruitment.OfferManage);

        // Agencies, rules and analytics.
        group.MapGet("/agencies", async (RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.AgenciesAsync(ct)))
            .WithName("ListAgencies").RequirePermission(Permissions.Recruitment.ApplicationView);
        group.MapPost("/agencies", async (AgencyBody body, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.SaveAgencyAsync(null, body, ct)))
            .WithName("CreateAgency").RequirePermission(Permissions.Recruitment.SettingsManage);
        group.MapPut("/agencies/{id:guid}", async (Guid id, AgencyBody body, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.SaveAgencyAsync(id, body, ct)))
            .WithName("UpdateAgency").RequirePermission(Permissions.Recruitment.SettingsManage);
        group.MapGet("/settings", async (RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.RulesAsync(ct)))
            .WithName("GetRecruitmentSettings").RequirePermission(Permissions.Recruitment.SettingsManage, Permissions.Recruitment.ApplicationView);
        group.MapPut("/settings", async (RecruitmentRules body, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.SaveRulesAsync(body, ct)))
            .WithName("SaveRecruitmentSettings").RequirePermission(Permissions.Recruitment.SettingsManage);
        group.MapGet("/analytics", async (DateOnly? from, DateOnly? to, RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.AnalyticsAsync(from, to, ct)))
            .WithName("RecruitmentAnalytics").RequirePermission(Permissions.Recruitment.AnalyticsView);

        // Self-service: the internal job board, referrals and interview feedback.
        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me").WithTags("Recruitment");
        me.MapGet("/jobs", async (RecruitmentService recruitment, CancellationToken ct) => TypedResults.Ok(await recruitment.JobBoardAsync(ct)))
            .WithName("MyJobBoard").RequirePermission(Permissions.Self.ReferralCreate);
        me.MapGet("/referrals", async (PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.MyReferralsAsync(ct)))
            .WithName("MyReferrals").RequirePermission(Permissions.Self.ReferralCreate);
        me.MapPost("/referrals", async (ReferralBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.ReferAsync(body, ct)))
            .WithName("SubmitReferral").RequirePermission(Permissions.Self.ReferralCreate);
        me.MapGet("/interviews", async (PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.MyInterviewsAsync(ct)))
            .WithName("MyInterviews").RequirePermission(Permissions.Self.InterviewFeedback);
        me.MapGet("/interviews/{id:guid}", async (Guid id, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.MyInterviewAsync(id, ct)))
            .WithName("MyInterview").RequirePermission(Permissions.Self.InterviewFeedback);
        me.MapPut("/interviews/{id:guid}/scorecard", async (Guid id, ScorecardBody body, PipelineService pipeline, CancellationToken ct) => TypedResults.Ok(await pipeline.SubmitScorecardAsync(id, body, ct)))
            .WithName("SubmitScorecard").RequirePermission(Permissions.Self.InterviewFeedback);
    }
}
