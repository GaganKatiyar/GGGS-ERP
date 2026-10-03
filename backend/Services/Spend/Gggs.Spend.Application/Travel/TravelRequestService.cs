using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Spend.Application.Snapshots;
using Gggs.Spend.Domain.Snapshots;
using Gggs.Spend.Domain.Travel;
using Gggs.Workflow.Contracts;

namespace Gggs.Spend.Application.Travel;

/// <summary>Creating a travel request and submitting it for approval - see
/// <see cref="TravelRequest"/>'s own remarks. Submission starts a Workflow instance
/// (architecture.md §13.3); approval/rejection happen only in
/// <c>TravelRequestWorkflowOutcomeConsumer</c>.</summary>
public sealed class TravelRequestService(
    ITravelRequestRepository requests,
    IEmployeeSnapshotRepository snapshots,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const string TravelRequestWorkflowCode = "TRAVEL_REQUEST";
    private const string TravelRequestEntityType = "TRAVEL_REQUEST";

    public async Task<TravelRequest> CreateAsync(
        Guid employeeId, string requestNo, string purpose, string fromCity, string toCity,
        DateOnly fromDate, DateOnly toDate, decimal estimatedCost, string currency, CancellationToken cancellationToken = default)
    {
        EmployeeSnapshot? employee = await snapshots.GetAsync(employeeId, cancellationToken);
        Guard(employee is not null, "SPEND_EMPLOYEE_NOT_FOUND", "The employee could not be found.");
        Guard(employee!.Status != "Exited", "SPEND_EMPLOYEE_EXITED", "This employee has already exited.");

        Guard(
            !await requests.RequestNoExistsAsync(requestNo, cancellationToken),
            "TRAVEL_REQUEST_NO_ALREADY_EXISTS",
            $"A request with number '{requestNo}' already exists.");

        TravelRequest request = TravelRequest.Create(requestNo, employeeId, purpose, fromCity, toCity, fromDate, toDate, estimatedCost, currency);
        requests.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task<TravelRequest> SubmitAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        TravelRequest request = await GetOrThrowAsync(requestId, cancellationToken);
        request.Submit(clock.GetUtcNow().UtcDateTime);

        publisher.Publish(new StartWorkflowIntegrationEvent(
            TravelRequestWorkflowCode,
            TravelRequestEntityType,
            request.Id,
            SourceService: "spend",
            InitiatorUserId: currentUser.UserId,
            SubjectEmployeeId: request.EmployeeId,
            ContextJson: BuildWorkflowContextJson(request),
            CorrelationId: currentUser.CorrelationId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request;
    }

    private static string BuildWorkflowContextJson(TravelRequest request)
        => System.Text.Json.JsonSerializer.Serialize(new { estimatedCost = request.EstimatedCost, currency = request.Currency });

    private async Task<TravelRequest> GetOrThrowAsync(Guid requestId, CancellationToken cancellationToken)
        => await requests.GetAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(TravelRequest), requestId);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
