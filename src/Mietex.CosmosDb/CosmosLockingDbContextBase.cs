using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb;

/// <summary>
/// Context variant that coordinates writes with ETag-fenced partition leases. A lease is released in
/// the same transactional batch as the protected writes.
/// </summary>
/// <typeparam name="TEntity">Host entity base type implementing <see cref="ICosmosEntity"/>.</typeparam>
/// <typeparam name="TUser">Host user identity type.</typeparam>
public abstract class CosmosLockingDbContextBase<TEntity, TUser> : CosmosDbContextBase<TEntity, TUser>
    where TEntity : class, ICosmosEntity
    where TUser : class
{
    private readonly Dictionary<string, CosmosPartitionLock> _locks =
        new(StringComparer.Ordinal);

    /// <summary>Initializes a locking context from validated options.</summary>
    /// <param name="options">Context options.</param>
    /// <param name="currentUser">Optional current user for audit stamping.</param>
    protected CosmosLockingDbContextBase(CosmosDbOptions options, TUser? currentUser = null)
        : base(options, currentUser)
    {
    }

    /// <summary>Acquires a lease and holds it until the next save, clear, or disposal.</summary>
    /// <param name="resourceKey">Logical resource key scoped to this partition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The held lock handle.</returns>
    public async Task<CosmosPartitionLock> PerformLockUntilSaveChangesAsync(
        string resourceKey,
        CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        if (_locks.ContainsKey(resourceKey))
        {
            throw new InvalidOperationException(
                $"The context already holds the partition lease for resource '{resourceKey}'.");
        }

        var lockOptions = new CosmosPartitionLockOptions
        {
            AcquireTimeout = Options.LockAcquireTimeout,
            LeaseDuration = Options.LockLeaseDuration,
            CleanupTimeout = Options.LockCleanupTimeout,
            TimeProvider = Options.TimeProvider,
        };

        var handle = await CosmosPartitionLock
            .AcquireAsync(
                Container,
                PartitionKey,
                resourceKey,
                lockOptions,
                Logger,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            EnsureUsable();
            if (_locks.ContainsKey(resourceKey))
            {
                throw new InvalidOperationException(
                    $"The context already holds the partition lease for resource '{resourceKey}'.");
            }
        }
        catch
        {
            await handle.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        handle.OnAbandoned = () =>
        {
            _locks.Remove(resourceKey);
            DiscardPendingAndCaches();
        };

        _locks[resourceKey] = handle;
        ClearCache();
        return handle;
    }

    /// <summary>Synchronous wrapper for <see cref="PerformLockUntilSaveChangesAsync"/>.</summary>
    /// <param name="resourceKey">Logical resource key scoped to this partition.</param>
    /// <returns>The held lock handle.</returns>
    public CosmosPartitionLock PerformLockUntilSaveChanges(string resourceKey) =>
        PerformLockUntilSaveChangesAsync(resourceKey).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override void Clear()
    {
        ReleaseLocksSync();
        base.Clear();
    }

    /// <inheritdoc />
    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ReleaseLocksAsync().ConfigureAwait(false);
        await base.ClearAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await ReleaseLocksAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override int AdditionalBatchOperationCount => _locks.Count;

    /// <inheritdoc />
    protected override void ValidateBatchOperationCount(int pendingOperationCount)
    {
        base.ValidateBatchOperationCount(pendingOperationCount);
    }

    /// <inheritdoc />
    protected override void PrepareBatch(TransactionalBatch batch)
    {
        base.PrepareBatch(batch);

        foreach (var handle in _locks.Values.ToList())
        {
            handle.AddReleaseToBatch(batch);
        }
    }

    /// <inheritdoc />
    protected override void OnBatchCommitted()
    {
        foreach (var handle in _locks.Values)
        {
            handle.MarkCommitted();
        }

        _locks.Clear();
        base.OnBatchCommitted();
    }

    /// <inheritdoc />
    protected override async Task OnSaveFinishedAsync(bool succeeded)
    {
        if (!succeeded)
        {
            DiscardPendingAndCaches();
        }

        await ReleaseLocksAsync().ConfigureAwait(false);
        await base.OnSaveFinishedAsync(succeeded).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        ReleaseLocksSync();
        base.Dispose(disposing);
    }

    private async Task ReleaseLocksAsync()
    {
        var handles = _locks.Values.ToList();
        _locks.Clear();

        foreach (var handle in handles)
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ReleaseLocksSync()
    {
        var handles = _locks.Values.ToList();
        _locks.Clear();

        foreach (var handle in handles)
        {
            handle.Dispose();
        }
    }
}
