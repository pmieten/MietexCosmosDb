using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb;

/// <summary>Non-generic view of a discovered set, used by the context for cache management.</summary>
public interface ICosmosDbSet
{
    /// <summary>Gets the entity type handled by the set.</summary>
    Type EntityType { get; }

    /// <summary>Clears the set's point-read cache.</summary>
    void ClearCache();

    /// <summary>Removes one id from the set's point-read cache.</summary>
    /// <param name="id">Document id.</param>
    void Evict(string id);
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
    private readonly bool _populateIndexMetrics;
    private readonly ConcurrentDictionary<string, T> _cache = new(StringComparer.Ordinal);

    internal CosmosDbSet(
        Container container,
        string partitionKey,
        string discriminator,
        bool cacheEnabled,
        bool populateIndexMetrics)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);

        _container = container;
        _partitionKey = partitionKey;
        _discriminator = discriminator;
        _cacheEnabled = cacheEnabled;
        _populateIndexMetrics = populateIndexMetrics;
    }

    /// <inheritdoc />
    public Type EntityType => typeof(T);

    /// <summary>Gets the partition-scoped LINQ query for this entity type.</summary>
    public IQueryable<T> Query =>
        _container
            .GetItemLinqQueryable<T>(
                allowSynchronousQueryExecution: true,
                continuationToken: null,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(_partitionKey),
                    PopulateIndexMetrics = _populateIndexMetrics,
                })
            .Where(entity => entity.Discriminator == _discriminator);

    /// <summary>Reads one document by id, using the cache when enabled.</summary>
    /// <param name="id">Document id, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document, or <see langword="null"/> when it does not exist.</returns>
    public Task<T?> FindAsync(string? id, CancellationToken cancellationToken = default)
    {
        if (id is null)
        {
            return Task.FromResult<T?>(null);
        }

        return FindCoreAsync(id, cancellationToken);
    }

    /// <summary>Reads one document by a numeric id formatted invariantly.</summary>
    /// <param name="id">Document id, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document, or <see langword="null"/> when it does not exist.</returns>
    public Task<T?> FindAsync(long? id, CancellationToken cancellationToken = default)
    {
        if (id is null)
        {
            return Task.FromResult<T?>(null);
        }

        return FindAsync(
            id.Value.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
    }

    /// <inheritdoc />
    public void ClearCache() => _cache.Clear();

    /// <inheritdoc />
    public void Evict(string id)
    {
        if (!string.IsNullOrEmpty(id))
        {
            _cache.TryRemove(id, out _);
        }
    }

    private async Task<T?> FindCoreAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_cacheEnabled && _cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        try
        {
            var response = await _container
                .ReadItemAsync<T>(
                    id,
                    new PartitionKey(_partitionKey),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var entity = response.Resource;
            entity.Etag = response.ETag;

            if (_cacheEnabled)
            {
                _cache[id] = entity;
            }

            return entity;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
