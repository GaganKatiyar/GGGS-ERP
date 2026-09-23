using Gggs.BuildingBlocks.Domain.Events;

namespace Gggs.Identity.Domain.Users.Events;

/*
 * Domain events from the User aggregate.
 *
 * Several of these matter to the security audit trail rather than to another service: a lockout
 * and a password change are things an auditor will ask about long after the fact, and recording
 * them as events means the audit entry is written by the same transaction that made the change.
 */

public sealed record UserInvitedDomainEvent(Guid UserId, string Email, string DisplayName) : DomainEvent;

public sealed record UserSignedInDomainEvent(Guid UserId, string Email) : DomainEvent;

public sealed record UserLockedOutDomainEvent(Guid UserId, string Email, DateTime LockoutEndDate) : DomainEvent;

public sealed record UserPasswordChangedDomainEvent(Guid UserId, string Email) : DomainEvent;

public sealed record UserDisabledDomainEvent(Guid UserId, string Email) : DomainEvent;

public sealed record UserRolesChangedDomainEvent(Guid UserId, IReadOnlyList<Guid> RoleIds) : DomainEvent;
