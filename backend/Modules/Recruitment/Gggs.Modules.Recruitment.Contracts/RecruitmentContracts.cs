using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Recruitment.Contracts;

/// <summary>Every opening of a requisition has been filled. Workforce closes the manpower request behind it.</summary>
[MessageType("gggs.recruitment.requisition-filled")]
public sealed record RequisitionFilledIntegrationEvent(Guid RequisitionId, Guid? ManpowerRequestId, int Hired) : IntegrationEvent;

/// <summary>A candidate was hired as an employee.</summary>
[MessageType("gggs.recruitment.candidate-hired")]
public sealed record CandidateHiredIntegrationEvent(Guid CandidateId, Guid ApplicationId, Guid RequisitionId, Guid EmployeeId, DateOnly JoiningDate, Guid? ReferrerEmployeeId) : IntegrationEvent;
