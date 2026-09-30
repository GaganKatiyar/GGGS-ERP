using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Recruitment;

namespace Gggs.Talent.Application.Recruitment;

/// <summary>
/// Offers against an application, once it has reached <see cref="ApplicationStage.Offer"/>. See
/// <see cref="Offer"/>'s own remarks on why <see cref="AcceptAsync"/> also advances the
/// application's stage in the same transaction.
/// </summary>
public sealed class OfferService(
    IOfferRepository offers, IJobApplicationRepository applications, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<Offer> CreateAsync(
        Guid applicationId, Guid designationId, Guid? gradeId, decimal annualCtc, string currency,
        DateOnly joiningDate, DateOnly expiryDate, CancellationToken cancellationToken = default)
    {
        JobApplication application = await applications.GetAsync(applicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(JobApplication), applicationId);

        Guard(
            application.Stage == ApplicationStage.Offer,
            "APPLICATION_NOT_AT_OFFER_STAGE",
            $"The application must be at the Offer stage before an offer can be created; it is {application.Stage}.");

        Guard(
            !await offers.ExistsForApplicationAsync(applicationId, cancellationToken),
            "OFFER_ALREADY_EXISTS",
            "This application already has an offer.");

        Offer offer = Offer.Create(
            GenerateOfferNo(clock), applicationId, designationId, gradeId, annualCtc, currency, joiningDate, expiryDate);

        offers.Add(offer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return offer;
    }

    public async Task<Offer> SendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Offer offer = await GetOrThrowAsync(id, cancellationToken);
        offer.Send();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return offer;
    }

    /// <summary>Accepts the offer and marks its application Hired in the same save - see
    /// <see cref="Offer"/>'s own remarks on why these two facts never go out of sync.</summary>
    public async Task<Offer> AcceptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Offer offer = await GetOrThrowAsync(id, cancellationToken);
        offer.Accept(clock.GetUtcNow().UtcDateTime);

        JobApplication application = await applications.GetAsync(offer.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(JobApplication), offer.ApplicationId);
        application.MarkHired();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return offer;
    }

    public async Task<Offer> DeclineAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Offer offer = await GetOrThrowAsync(id, cancellationToken);
        offer.Decline();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return offer;
    }

    public async Task<Offer> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Offer offer = await GetOrThrowAsync(id, cancellationToken);
        offer.Revoke();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return offer;
    }

    private async Task<Offer> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await offers.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Offer), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }

    // Same trailing-random-hex convention as JobRequisitionService.GenerateRequisitionNo - see its
    // own remarks on why the trailing digits, not the leading ones, of a version-7 GUID.
    private static string GenerateOfferNo(TimeProvider clock)
        => $"OFR-{clock.GetUtcNow():yyyyMMdd}-{Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant()}";
}
