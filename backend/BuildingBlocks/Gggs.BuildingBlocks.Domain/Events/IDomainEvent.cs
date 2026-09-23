namespace Gggs.BuildingBlocks.Domain.Events;

/// <summary>
/// Something that happened inside an aggregate. Dispatched after the transaction commits; handlers
/// that must reach another service write to the outbox in the same transaction.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTime OccurredDate { get; }
}

/// <summary>
/// Convenience base, used as <c>public sealed record EmployeeHired(Guid EmployeeId) : DomainEvent;</c>
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredDate { get; init; } = DateTime.UtcNow;
}
