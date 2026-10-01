using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Spend.Application.Snapshots;
using Gggs.Spend.Domain.Snapshots;
using Gggs.Spend.Domain.Travel;

namespace Gggs.Spend.Application.Travel;

/// <summary>Creating a travel request and moving it through Submitted -&gt; Approved/Rejected -
/// see <see cref="TravelRequest"/>'s own remarks.</summary>
public sealed class TravelRequestService(
    ITravelRequestRepository requests, IEmployeeSnapshotRepository snapshots, IUnitOfWork unitOfWork, TimeProvider clock)
{
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task<TravelRequest> ApproveAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        TravelRequest request = await GetOrThrowAsync(requestId, cancellationToken);
        request.Approve(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task<TravelRequest> RejectAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        TravelRequest request = await GetOrThrowAsync(requestId, cancellationToken);
        request.Reject(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request;
    }

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
