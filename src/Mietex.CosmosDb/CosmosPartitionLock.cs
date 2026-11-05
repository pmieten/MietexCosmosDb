using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Mietex.CosmosDb;

/// <summary>Timing and clock configuration for a partition lease.</summary>
public sealed record CosmosPartitionLockOptions
{
    /// <summary>Gets how long acquisition waits before timing out.</summary>
    public TimeSpan AcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets how long a lease remains valid.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets how long best-effort cleanup may take.</summary>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the minimum retry delay.</summary>
    public TimeSpan RetryDelayMin { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Gets the maximum retry delay.</summary>
    public TimeSpan RetryDelayMax { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Gets the clock used for lease expiry.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>
/// A tenant-partition lease. Requires a single-write-region Cosmos account with at least Session
/// consistency. Expiry makes the lease eligible for takeover; the ETag-conditional delete in the
/// business transaction fences writes after a takeover.
/// </summary>
public sealed class CosmosPartitionLock : IDisposable, IAsyncDisposable
{
    /// <summary>Acquires a lease for a logical resource key.</summary>
    /// <param name="container">Container that stores the lease.</param>
    /// <param name="partitionKey">Partition key value.</param>
    /// <param name="resourceKey">Logical resource key.</param>
    /// <param name="options">Optional lock options.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The acquired lease handle.</returns>
    public static Task<CosmosPartitionLock> AcquireAsync(
        Container container,
        string partitionKey,
        string resourceKey,
        CosmosPartitionLockOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    /// <summary>Gets the deterministic Cosmos document id of the lease.</summary>
    public string LockId => throw new NotImplementedException();

    /// <summary>Gets the logical resource key.</summary>
    public string ResourceKey => throw new NotImplementedException();

    /// <summary>Gets when the lease becomes eligible for takeover.</summary>
    public DateTimeOffset ExpiresAt => throw new NotImplementedException();

    /// <summary>Appends an ETag-conditional lease delete to a business batch.</summary>
    /// <param name="batch">Batch that commits the protected writes.</param>
    public void AddReleaseToBatch(TransactionalBatch batch) => throw new NotImplementedException();

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => throw new NotImplementedException();

    internal Action? OnAbandoned { get; set; }

    internal void MarkCommitted() => throw new NotImplementedException();
}

/// <summary>Document stored in Cosmos DB for a partition lease.</summary>
public sealed class CosmosLockLease
{
    /// <summary>Gets or sets the document id.</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the partition key value.</summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the discriminator.</summary>
    public string Discriminator { get; set; } = "DistributedLock";

    /// <summary>Gets or sets the owner token.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Gets or sets when the lease becomes eligible for takeover.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
