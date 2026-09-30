using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Talent.Domain.Recruitment;

public enum OfferStatus
{
    Draft = 1,
    Sent = 2,
    Accepted = 3,
    Declined = 4,
    Revoked = 5,
}

/// <summary>
/// An offer against one application, once it has reached <see cref="ApplicationStage.Offer"/>. No
/// offer-letter document generation or approval workflow in this v1 slice (architecture.md's
/// <c>rec.Offer.DocumentId</c>/<c>WorkflowInstanceId</c>) - those need the Document service's
/// template rendering and Workflow integration, both real scope but more than a first pass needs to
/// prove the offer record and its accept/decline outcome.
/// <para>
/// Accepting an offer is the one place this service reaches back into the application's stage
/// (<see cref="JobApplication.MarkHired"/>, via <c>OfferService.AcceptAsync</c>) rather than
/// leaving it to a second call: an accepted offer and a Hired application are the same fact
/// observed from two aggregates, and letting them go out of sync even briefly is worse than the
/// two-aggregate write.
/// </para>
/// </summary>
public sealed class Offer : TenantAggregateRoot
{
    private Offer()
    {
        OfferNo = null!;
        Currency = null!;
    }

    public Guid ApplicationId { get; private set; }

    public string OfferNo { get; private set; }

    public Guid DesignationId { get; private set; }

    public Guid? GradeId { get; private set; }

    public decimal AnnualCtc { get; private set; }

    public string Currency { get; private set; }

    public DateOnly JoiningDate { get; private set; }

    public DateOnly ExpiryDate { get; private set; }

    public DateTime? AcceptedDate { get; private set; }

    public OfferStatus Status { get; private set; }

    public static Offer Create(
        string offerNo, Guid applicationId, Guid designationId, Guid? gradeId,
        decimal annualCtc, string currency, DateOnly joiningDate, DateOnly expiryDate)
    {
        Guard.Against(annualCtc <= 0, "OFFER_CTC_MUST_BE_POSITIVE", "Annual CTC must be greater than zero.");
        Guard.Against(expiryDate < joiningDate, "OFFER_EXPIRY_BEFORE_JOINING", "The offer cannot expire before the joining date.");

        return new Offer
        {
            OfferNo = Guard.MaxLength(Guard.NotNullOrWhiteSpace(offerNo).Trim().ToUpperInvariant(), 30),
            ApplicationId = Guard.NotEmpty(applicationId),
            DesignationId = Guard.NotEmpty(designationId),
            GradeId = gradeId,
            AnnualCtc = annualCtc,
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            JoiningDate = joiningDate,
            ExpiryDate = expiryDate,
            Status = OfferStatus.Draft,
            IsActive = true,
        };
    }

    /// <summary>Draft -> Sent: the offer is now the candidate's to accept or decline.</summary>
    public void Send()
    {
        Guard.Against(
            Status != OfferStatus.Draft, "OFFER_NOT_DRAFT", $"Offer '{OfferNo}' must be Draft to send; it is {Status}.");

        Status = OfferStatus.Sent;
    }

    public void Accept(DateTime acceptedDate)
    {
        Guard.Against(
            Status != OfferStatus.Sent, "OFFER_NOT_SENT", $"Offer '{OfferNo}' must be Sent to accept; it is {Status}.");

        Status = OfferStatus.Accepted;
        AcceptedDate = acceptedDate;
    }

    public void Decline()
    {
        Guard.Against(
            Status != OfferStatus.Sent, "OFFER_NOT_SENT", $"Offer '{OfferNo}' must be Sent to decline; it is {Status}.");

        Status = OfferStatus.Declined;
    }

    public void Revoke()
    {
        Guard.Against(
            Status is OfferStatus.Accepted or OfferStatus.Declined or OfferStatus.Revoked,
            "OFFER_ALREADY_TERMINAL",
            $"Offer '{OfferNo}' is already {Status} and cannot be revoked.");

        Status = OfferStatus.Revoked;
    }
}
