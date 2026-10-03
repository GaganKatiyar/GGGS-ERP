namespace Gggs.Reporting.Api.Contracts;

public sealed record HeadcountDailySnapshotResponse(
    DateOnly SnapshotDate,
    Guid LegalEntityId,
    Guid BranchId,
    Guid DepartmentId,
    int Headcount,
    int Joiners,
    int Leavers);
