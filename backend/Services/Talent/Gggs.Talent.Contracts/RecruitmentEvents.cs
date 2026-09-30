using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Talent.Contracts;

/*
 * Talent's published contract.
 *
 * Other services reference this package and nothing else of ours - same convention as
 * Gggs.CoreHr.Contracts and Gggs.Workflow.Contracts.
 */

/// <summary>
/// A candidate accepted their offer - the recruitment pipeline's terminal happy-path event. Carries
/// everything <c>EmployeeService.HireAsync</c> needs to auto-create the Core HR employee record,
/// except a legal entity id: a <c>JobRequisition</c> only carries a branch, and a branch belongs to
/// exactly one legal entity, so Core HR's own consumer derives it from <see cref="BranchId"/>
/// rather than this event guessing at a field the requisition never had. Everything else travels
/// here already resolved (from the <c>JobRequisition</c> the offer's application traces back to,
/// through its posting) rather than leaving Core HR to walk that chain itself, the same "payload
/// carries what a consumer needs to act, fetched IDs not re-derived" convention
/// <c>EmployeeAssignmentChangedIntegrationEvent</c> already uses.
/// <para>
/// No employee code here - Core HR generates its own on hire, the same short random code every
/// other auto-numbered entity in this codebase uses (no tenant-configurable
/// <c>cfg.NumberSequence</c> exists yet to generate a "real" one from).
/// </para>
/// </summary>
[MessageType("gggs.talent.offer.accepted")]
public sealed record OfferAcceptedIntegrationEvent(
    Guid OfferId,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateFirstName,
    string? CandidateLastName,
    string CandidateEmail,
    Guid BranchId,
    Guid DepartmentId,
    Guid DesignationId,
    Guid? GradeId,
    DateOnly JoiningDate) : IntegrationEvent;
