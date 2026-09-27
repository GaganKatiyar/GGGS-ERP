using Gggs.BuildingBlocks.Domain.Events;

namespace Gggs.CoreHr.Domain.Employees.Events;

/*
 * Domain events from the Employee aggregate.
 *
 * EmployeeHiredDomainEvent and EmployeeExitedDomainEvent matter far beyond this service: every
 * other module that keeps a ref.EmployeeSnapshot (Time, Payroll, Talent, Spend, Workflow) exists
 * because of events shaped like these. They stay internal domain events for now - nothing consumes
 * them yet - and become the first Gggs.CoreHr.Contracts integration events the day something does.
 */

public sealed record EmployeeHiredDomainEvent(Guid EmployeeId, string EmployeeCode, string DisplayName, DateOnly DateOfJoining) : DomainEvent;

public sealed record EmployeeTransferredDomainEvent(Guid EmployeeId, Guid EmploymentRecordId, DateOnly EffectiveFrom) : DomainEvent;

public sealed record EmployeeConfirmedDomainEvent(Guid EmployeeId, DateOnly ConfirmationDate) : DomainEvent;

public sealed record EmployeeExitedDomainEvent(Guid EmployeeId, DateOnly ExitDate, string ExitTypeCode) : DomainEvent;
