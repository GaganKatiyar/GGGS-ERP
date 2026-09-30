using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Talent.Application.Recruitment;
using Gggs.Talent.Domain.Recruitment;
using Gggs.Talent.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Talent.Api.Endpoints;

public sealed record CreateOfferBody(
    Guid DesignationId, Guid? GradeId, decimal AnnualCtc, string Currency, DateOnly JoiningDate, DateOnly ExpiryDate);

public sealed record OfferResponse(
    Guid Id,
    Guid ApplicationId,
    string OfferNo,
    Guid DesignationId,
    Guid? GradeId,
    decimal AnnualCtc,
    string Currency,
    DateOnly JoiningDate,
    DateOnly ExpiryDate,
    DateTime? AcceptedDate,
    string Status);

/// <summary>
/// Offers - created against an application at the Offer stage (<c>/applications/{applicationId}/offer</c>,
/// singular: one application has at most one offer), then acted on by their own id. No offer-letter
/// document or approval workflow yet - see <see cref="Offer"/>'s own remarks.
/// </summary>
public static class OfferEndpoints
{
    public static IEndpointRouteBuilder MapOfferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder applicationGroup = endpoints
            .MapGroup("/api/v1/recruitment/applications/{applicationId:guid}/offer")
            .WithTags("Recruitment");

        applicationGroup.MapGet("/", GetForApplicationAsync).WithName("GetOfferForApplication").RequirePermission(Permissions.Recruitment.OfferView);
        applicationGroup.MapPost("/", CreateAsync).WithName("CreateOffer").RequirePermission(Permissions.Recruitment.OfferManage);

        RouteGroupBuilder offerGroup = endpoints
            .MapGroup("/api/v1/recruitment/offers")
            .WithTags("Recruitment");

        offerGroup.MapPost("/{id:guid}/send", SendAsync).WithName("SendOffer").RequirePermission(Permissions.Recruitment.OfferManage);
        offerGroup.MapPost("/{id:guid}/accept", AcceptAsync).WithName("AcceptOffer").RequirePermission(Permissions.Recruitment.OfferManage);
        offerGroup.MapPost("/{id:guid}/decline", DeclineAsync).WithName("DeclineOffer").RequirePermission(Permissions.Recruitment.OfferManage);
        offerGroup.MapPost("/{id:guid}/revoke", RevokeAsync).WithName("RevokeOffer").RequirePermission(Permissions.Recruitment.OfferManage);

        return endpoints;
    }

    private static async Task<Results<Ok<OfferResponse>, NotFound>> GetForApplicationAsync(
        Guid applicationId, TalentDbContext db, CancellationToken ct)
    {
        Offer? entity = await db.Offers.AsNoTracking().FirstOrDefaultAsync(x => x.ApplicationId == applicationId, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<OfferResponse>> CreateAsync(
        Guid applicationId, CreateOfferBody body, OfferService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Offer entity = await service.CreateAsync(
            applicationId, body.DesignationId, body.GradeId, body.AnnualCtc, body.Currency,
            body.JoiningDate, body.ExpiryDate, ct);

        return TypedResults.Created($"/api/v1/recruitment/applications/{applicationId}/offer", ToResponse(entity));
    }

    private static async Task<Ok<OfferResponse>> SendAsync(Guid id, OfferService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.SendAsync(id, ct)));

    private static async Task<Ok<OfferResponse>> AcceptAsync(Guid id, OfferService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.AcceptAsync(id, ct)));

    private static async Task<Ok<OfferResponse>> DeclineAsync(Guid id, OfferService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.DeclineAsync(id, ct)));

    private static async Task<Ok<OfferResponse>> RevokeAsync(Guid id, OfferService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.RevokeAsync(id, ct)));

    private static OfferResponse ToResponse(Offer entity) => new(
        entity.Id, entity.ApplicationId, entity.OfferNo, entity.DesignationId, entity.GradeId,
        entity.AnnualCtc, entity.Currency, entity.JoiningDate, entity.ExpiryDate, entity.AcceptedDate, entity.Status.ToString());
}
