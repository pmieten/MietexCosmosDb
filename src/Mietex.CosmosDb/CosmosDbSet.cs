using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb;

/// <summary>Non-generic view of a discovered set, used by the context for cache management.</summary>
public interface ICosmosDbSet
{
    /// <summary>Gets the entity type handled by the set.</summary>
    Type EntityType { get; }

    /// <summary>Clears the set's point-read cache.</summary>
    void ClearCache();
}

/// <summary>Partition-scoped, discriminator-filtered access to one entity type.</summary>
/// <typeparam name="T">Entity type.</typeparam>
public sealed class CosmosDbSet<T> : ICosmosDbSet
    where T : class, ICosmosEntity
{
    private readonly Container _container;
    private readonly string _partitionKey;
    private readonly string _discriminator;
    private readonly bool _cacheEnabled;

    internal CosmosDbSet(Container container, string partitionKey, string discriminator, bool cacheEnabled)
    {
        _container = container;
        _partitionKey = partitionKey;
        _discriminator = discriminator;
        _cacheEnabled = cacheEnabled;
    }

    /// <inheritdoc />
    public Type EntityType => typeof(T);

    /// <summary>Gets the partition-scoped LINQ query for this entity type.</summary>
    public IQueryable<T> Query => throw new NotImplementedException(State);

    /// <summary>Reads one document by id, using the cache when enabled.</summary>
    /// <param name="id">Document id, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document, or <see langword="null"/> when it does not exist.</returns>
    public Task<T?> FindAsync(string? id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(State);

    /// <summary>Reads one document by a numeric id formatted invariantly.</summary>
    /// <param name="id">Document id, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document, or <see langword="null"/> when it does not exist.</returns>
    public Task<T?> FindAsync(long? id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(State);

    /// <inheritdoc />
    public void ClearCache() => throw new NotImplementedException(State);

    private string State =>
        $"{_container.Id}/{_partitionKey}/{_discriminator}/{_cacheEnabled}";
}
