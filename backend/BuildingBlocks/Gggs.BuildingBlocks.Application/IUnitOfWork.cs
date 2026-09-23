namespace Gggs.BuildingBlocks.Application;

/// <summary>
/// Commits one business operation. The same transaction carries the domain change, its audit
/// records and its outbox messages, which is what makes "saved implies eventually published" true
/// without a distributed transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// For the rare operation that spans several aggregates and needs an explicit transaction.
    /// Prefer a single SaveChanges: if two aggregates must always commit together, they probably
    /// belong to the same aggregate.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
