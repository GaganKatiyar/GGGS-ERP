namespace Gggs.BuildingBlocks.Domain.Abstractions;

/// <summary>Any persisted entity with a surrogate key.</summary>
public interface IEntity
{
    Guid Id { get; }
}

/// <summary>
/// Marks a row as belonging to exactly one tenant. Every such entity is automatically filtered
/// (EF global query filter), stamped on insert, protected against tenant changes, and covered by
/// the SQL Server row-level security policy.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>
/// Marks a table that deliberately belongs to no tenant: the tenant registry itself, subscription
/// plans, the permission catalog, published compliance packs.
/// <para>
/// Declared explicitly rather than inferred from the absence of <see cref="ITenantOwned"/>, because
/// "this table has no TenantId" is either a considered decision or the most serious bug in a
/// multi-tenant system, and the two must not look alike in a pull request.
/// </para>
/// </summary>
public interface IPlatformOwned
{
}

/// <summary>Rows are never physically deleted by application code; retention jobs do that.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    Guid? DeletedBy { get; }

    DateTime? DeletedDate { get; }
}

/// <summary>Standard audit columns required on every business table.</summary>
public interface IAuditable
{
    Guid CreatedBy { get; }

    DateTime CreatedDate { get; }

    Guid? ModifiedBy { get; }

    DateTime? ModifiedDate { get; }
}

/// <summary>Optimistic concurrency via SQL Server ROWVERSION, surfaced to clients as an ETag.</summary>
public interface IConcurrencyAware
{
    byte[]? RowVersion { get; }
}

/// <summary>Business enable/disable, which is not the same as deletion.</summary>
public interface IActivatable
{
    bool IsActive { get; }
}
