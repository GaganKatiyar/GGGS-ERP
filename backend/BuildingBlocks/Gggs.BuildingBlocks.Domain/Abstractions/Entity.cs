using Gggs.BuildingBlocks.Domain.Events;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.BuildingBlocks.Domain.Abstractions;

/// <summary>Base entity: identity, equality by key, and a domain-event buffer.</summary>
public abstract class Entity : IEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(Guid id) => Id = id;

    protected Entity() => Id = SequentialGuid.Create();

    public Guid Id { get; protected set; }

    /// <summary>Events raised since the entity was loaded; drained by the outbox interceptor on save.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>An aggregate root: the only entity type a repository may load or save directly.</summary>
public abstract class AggregateRoot : Entity
{
    protected AggregateRoot(Guid id) : base(id)
    {
    }

    protected AggregateRoot()
    {
    }
}

/// <summary>
/// Base for every tenant-owned aggregate. The tenant, audit and soft-delete columns are set by
/// persistence interceptors rather than by domain code, so no handler can forget them or forge them.
/// </summary>
public abstract class TenantAggregateRoot
    : AggregateRoot, ITenantOwned, ISoftDeletable, IAuditable, IConcurrencyAware, IActivatable
{
    protected TenantAggregateRoot(Guid id) : base(id)
    {
    }

    protected TenantAggregateRoot()
    {
    }

    public Guid TenantId { get; private set; }

    public bool IsActive { get; protected set; } = true;

    public bool IsDeleted { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public DateTime? DeletedDate { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime CreatedDate { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public DateTime? ModifiedDate { get; private set; }

    public byte[]? RowVersion { get; private set; }
}
