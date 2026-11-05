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
    public Task<CosmosPartitionLock> PerformLockUntilSaveChangesAsync(
        string resourceKey,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    /// <summary>Synchronous wrapper for <see cref="PerformLockUntilSaveChangesAsync"/>.</summary>
    /// <param name="resourceKey">Logical resource key scoped to this partition.</param>
    /// <returns>The held lock handle.</returns>
    public CosmosPartitionLock PerformLockUntilSaveChanges(string resourceKey) =>
        PerformLockUntilSaveChangesAsync(resourceKey).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override void Clear() => throw new NotImplementedException();

    /// <inheritdoc />
    public override Task ClearAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    public override ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return base.DisposeAsync();
    }
}
