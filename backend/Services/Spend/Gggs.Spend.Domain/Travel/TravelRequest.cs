using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Spend.Domain.Travel;

public enum TravelRequestStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
}

/// <summary>
/// One employee's request to travel for work - Draft -&gt; Submitted -&gt; Approved/Rejected (both
/// terminal), the exact same lifecycle shape as <c>Expenses.ExpenseClaim</c>: no Workflow
/// integration yet, an admin with the right permission decides directly. No itinerary/segment
/// detail in this v1 slice (architecture.md's <c>trv.TravelItinerary</c>) - just the trip's own
/// purpose, cities and dates, enough for an advance to be requested against once this is Approved.
/// </summary>
public sealed class TravelRequest : TenantAggregateRoot
{
    private TravelRequest()
    {
        RequestNo = null!;
        Purpose = null!;
        FromCity = null!;
        ToCity = null!;
        Currency = null!;
    }

    public string RequestNo { get; private set; }

    public Guid EmployeeId { get; private set; }

    public string Purpose { get; private set; }

    public string FromCity { get; private set; }

    public string ToCity { get; private set; }

    public DateOnly FromDate { get; private set; }

    public DateOnly ToDate { get; private set; }

    public decimal EstimatedCost { get; private set; }

    public string Currency { get; private set; }

    public TravelRequestStatus Status { get; private set; }

    public DateTime? SubmittedDate { get; private set; }

    public DateTime? DecidedDate { get; private set; }

    public static TravelRequest Create(
        string requestNo, Guid employeeId, string purpose, string fromCity, string toCity,
        DateOnly fromDate, DateOnly toDate, decimal estimatedCost, string currency)
    {
        Guard.Against(toDate < fromDate, "TRAVEL_REQUEST_DATES_INVALID", "The return date cannot be before the departure date.");
        Guard.Against(estimatedCost <= 0, "TRAVEL_REQUEST_ESTIMATE_NOT_POSITIVE", "The estimated cost must be positive.");

        return new TravelRequest
        {
            RequestNo = Guard.MaxLength(Guard.NotNullOrWhiteSpace(requestNo).Trim(), 30),
            EmployeeId = Guard.NotEmpty(employeeId),
            Purpose = Guard.MaxLength(Guard.NotNullOrWhiteSpace(purpose).Trim(), 200),
            FromCity = Guard.MaxLength(Guard.NotNullOrWhiteSpace(fromCity).Trim(), 100),
            ToCity = Guard.MaxLength(Guard.NotNullOrWhiteSpace(toCity).Trim(), 100),
            FromDate = fromDate,
            ToDate = toDate,
            EstimatedCost = estimatedCost,
            Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(currency).Trim().ToUpperInvariant(), 3),
            Status = TravelRequestStatus.Draft,
            IsActive = true,
        };
    }

    public void Submit(DateTime submittedAtUtc)
    {
        Guard.Against(Status != TravelRequestStatus.Draft, "TRAVEL_REQUEST_NOT_DRAFT", $"Request '{RequestNo}' must be Draft to submit; it is {Status}.");
        Status = TravelRequestStatus.Submitted;
        SubmittedDate = submittedAtUtc;
    }

    public void Approve(DateTime decidedAtUtc)
    {
        Guard.Against(Status != TravelRequestStatus.Submitted, "TRAVEL_REQUEST_NOT_SUBMITTED", $"Request '{RequestNo}' must be Submitted to approve; it is {Status}.");
        Status = TravelRequestStatus.Approved;
        DecidedDate = decidedAtUtc;
    }

    public void Reject(DateTime decidedAtUtc)
    {
        Guard.Against(Status != TravelRequestStatus.Submitted, "TRAVEL_REQUEST_NOT_SUBMITTED", $"Request '{RequestNo}' must be Submitted to reject; it is {Status}.");
        Status = TravelRequestStatus.Rejected;
        DecidedDate = decidedAtUtc;
    }
}
