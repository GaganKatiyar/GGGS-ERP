using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Workforce.Contracts;

/// <summary>
/// A manpower request was approved (spec §11: manager → department head → HR → finance → recruitment).
/// Recruitment opens a requisition from it.
/// </summary>
[MessageType("gggs.workforce.manpower-request-approved")]
public sealed record ManpowerRequestApprovedIntegrationEvent(
    Guid RequestId,
    string Number,
    string Title,
    Guid DepartmentId,
    Guid? PositionId,
    Guid? JobId,
    Guid? LocationId,
    int Positions,
    string EmploymentTypeCode,
    string WorkModeCode,
    decimal? MinExperienceYears,
    decimal? MaxExperienceYears,
    decimal? SalaryMin,
    decimal? SalaryMax,
    IReadOnlyList<string> Skills,
    DateOnly? RequiredBy,
    Guid RequesterEmployeeId,
    string Priority,
    string? Justification) : IntegrationEvent;
