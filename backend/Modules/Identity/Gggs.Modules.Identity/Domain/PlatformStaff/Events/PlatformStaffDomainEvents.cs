using Gggs.BuildingBlocks.Domain.Events;

namespace Gggs.Modules.Identity.Domain.PlatformStaff.Events;

/*
 * Domain events from the PlatformStaffMember aggregate.
 *
 * Mirrors the equivalent User events. These are audited the same way and for the same reason: a
 * lockout or a password change on an account this powerful is exactly what an auditor asks about
 * long after the fact.
 */

public sealed record PlatformStaffInvitedDomainEvent(Guid PlatformStaffId, string Email, string DisplayName) : DomainEvent;

public sealed record PlatformStaffSignedInDomainEvent(Guid PlatformStaffId, string Email) : DomainEvent;

public sealed record PlatformStaffLockedOutDomainEvent(Guid PlatformStaffId, string Email, DateTime LockoutEndDate) : DomainEvent;

public sealed record PlatformStaffPasswordChangedDomainEvent(Guid PlatformStaffId, string Email) : DomainEvent;

public sealed record PlatformStaffDisabledDomainEvent(Guid PlatformStaffId, string Email) : DomainEvent;
