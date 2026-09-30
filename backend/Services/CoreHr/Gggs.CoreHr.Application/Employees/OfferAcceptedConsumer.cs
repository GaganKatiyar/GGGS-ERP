using Gggs.BuildingBlocks.Messaging;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Domain.Organization;
using Gggs.Talent.Contracts;
using Microsoft.Extensions.Logging;

namespace Gggs.CoreHr.Application.Employees;

/// <summary>
/// Auto-hires the Core HR employee once a candidate accepts their offer - the recruitment
/// pipeline's handoff into Core HR. Resolves the legal entity from the event's branch id (a branch
/// belongs to exactly one legal entity; see <see cref="OfferAcceptedIntegrationEvent"/>'s own
/// remarks on why the event itself does not carry one) and generates an employee code, the same
/// short random-suffix convention <c>JobRequisitionService.GenerateRequisitionNo</c> and its
/// siblings already use - no tenant-configurable <c>cfg.NumberSequence</c> exists yet to generate a
/// "real" one from.
/// <para>
/// Redelivery of the same event is handled by the framework's inbox (<c>messaging.InboxState</c>),
/// not by this handler - see architecture.md §5.4. This handler does not additionally guard against
/// "was this offer already hired," matching how every other idempotent consumer in this codebase
/// leans on the inbox rather than re-implementing its own duplicate check.
/// </para>
/// </summary>
public sealed class OfferAcceptedConsumer(
    EmployeeService employeeService, IOrganizationRepository organization, ILogger<OfferAcceptedConsumer> logger)
    : IIntegrationEventConsumer<OfferAcceptedIntegrationEvent>
{
    public async Task HandleAsync(OfferAcceptedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Branch branch = await organization.GetBranchAsync(integrationEvent.BranchId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Offer {integrationEvent.OfferId} accepted for a branch ({integrationEvent.BranchId}) that no longer exists.");

        var assignment = new AssignmentDetails(
            branch.LegalEntityId,
            integrationEvent.BranchId,
            integrationEvent.DepartmentId,
            integrationEvent.DesignationId,
            integrationEvent.GradeId,
            CostCenterId: null,
            ReportingManagerId: null,
            EmploymentType.Permanent);

        Employee employee = await employeeService.HireAsync(
            GenerateEmployeeCode(),
            integrationEvent.CandidateFirstName,
            middleName: null,
            integrationEvent.CandidateLastName,
            integrationEvent.CandidateEmail,
            integrationEvent.JoiningDate,
            assignment,
            cancellationToken);

        logger.LogInformation(
            "Employee {EmployeeId} ({Code}) auto-hired from accepted offer {OfferId}.",
            employee.Id, employee.EmployeeCode, integrationEvent.OfferId);
    }

    // Same trailing-random-hex convention as JobRequisitionService.GenerateRequisitionNo and its
    // siblings - see that method's own remarks on why the trailing digits of a version-7 GUID, not
    // the leading ones.
    private static string GenerateEmployeeCode()
        => $"EMP-{Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant()}";
}
