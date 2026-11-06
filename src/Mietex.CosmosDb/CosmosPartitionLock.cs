using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly Action<ILogger, Exception?> AbandonedCallbackFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, "AbandonedCallbackFailed"),
            "The abandoned-lease callback threw.");

    private static readonly Action<ILogger, TimeSpan, string, Exception?> CleanupTimedOut =
        LoggerMessage.Define<TimeSpan, string>(
            LogLevel.Warning,
            new EventId(2, "CleanupTimedOut"),
            "Timed out after {CleanupTimeout} while releasing partition lease {LockId}.");

    private static readonly Action<ILogger, string, Exception?> ReleaseFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3, "ReleaseFailed"),
            "Could not release partition lease {LockId}. Expiry remains the recovery path.");

    private readonly Container _container;
    private readonly string _partitionKey;
    private readonly string _lockId;
    private readonly string _resourceKey;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _cleanupTimeout;
    private readonly ILogger? _logger;
    private string? _etag;
    private bool _releaseAdded;
    private bool _committed;
    private bool _disposed;

    private CosmosPartitionLock(
        Container container,
        string partitionKey,
        string lockId,
        string resourceKey,
        DateTimeOffset expiresAt,
        string etag,
        TimeProvider timeProvider,
        TimeSpan cleanupTimeout,
        ILogger? logger)
    {
        _container = container;
        _partitionKey = partitionKey;
        _lockId = lockId;
        _resourceKey = resourceKey;
        ExpiresAt = expiresAt;
        _etag = etag;
        _timeProvider = timeProvider;
        _cleanupTimeout = cleanupTimeout;
        _logger = logger;
    }

    /// <summary>Gets when the lease becomes eligible for takeover.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Gets the deterministic Cosmos document id of the lease.</summary>
    public string LockId => _lockId;

    /// <summary>Gets the logical resource key.</summary>
    public string ResourceKey => _resourceKey;

    internal Action? OnAbandoned { get; set; }

    /// <summary>Acquires a lease for a logical resource key.</summary>
    /// <param name="container">Container that stores the lease.</param>
    /// <param name="partitionKey">Partition key value.</param>
    /// <param name="resourceKey">Logical resource key.</param>
    /// <param name="options">Optional lock options.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The acquired lease handle.</returns>
    public static async Task<CosmosPartitionLock> AcquireAsync(
        Container container,
        string partitionKey,
        string resourceKey,
        CosmosPartitionLockOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        options ??= new CosmosPartitionLockOptions();
        ValidateOptions(options);

        var timeProvider = options.TimeProvider;
        var lockId = CreateLockId(resourceKey);
        var owner = Guid.NewGuid().ToString("N");
        var partitionKeyValue = new PartitionKey(partitionKey);

        using var timeoutSource = new CancellationTokenSource(options.AcquireTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            while (true)
            {
                linkedSource.Token.ThrowIfCancellationRequested();

                var now = timeProvider.GetUtcNow();
                var lease = new CosmosLockLease
                {
                    Id = lockId,
                    PartitionKey = partitionKey,
                    Discriminator = "DistributedLock",
                    Owner = owner,
                    ExpiresAt = now + options.LeaseDuration,
                };

                try
                {
                    var created = await container
                        .CreateItemAsync(
                            lease,
                            partitionKeyValue,
                            new ItemRequestOptions { EnableContentResponseOnWrite = true },
                            linkedSource.Token)
                        .ConfigureAwait(false);

                    return CreateHandle(
                        container,
                        partitionKey,
                        lockId,
                        resourceKey,
                        lease.ExpiresAt,
                        created.ETag,
                        options,
                        logger);
                }
                catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
                {
                    if (await TryTakeOverAsync(
                        container,
                        partitionKeyValue,
                        lockId,
                        lease,
                        options,
                        linkedSource.Token).ConfigureAwait(false) is { } replacement)
                    {
                        return CreateHandle(
                            container,
                            partitionKey,
                            lockId,
                            resourceKey,
                            lease.ExpiresAt,
                            replacement.ETag,
                            options,
                            logger);
                    }
                }
                catch (CosmosException exception) when (IsRetryable(exception.StatusCode))
                {
                    // Another writer won the race or the service asked us to retry.
                }

                await DelayBeforeRetryAsync(options, linkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out after {options.AcquireTimeout} while acquiring partition lease '{resourceKey}'.",
                exception);
        }
    }

    /// <summary>Appends an ETag-conditional lease delete to a business batch.</summary>
    /// <param name="batch">Batch that commits the protected writes.</param>
    public void AddReleaseToBatch(TransactionalBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (_disposed || _committed || _releaseAdded)
        {
            throw new InvalidOperationException("The partition lease has already been released or committed.");
        }

        if (_timeProvider.GetUtcNow() >= ExpiresAt)
        {
            throw new InvalidOperationException(
                "The partition lease has expired. Discard the unit of work and retry from a fresh read.");
        }

        if (string.IsNullOrEmpty(_etag))
        {
            throw new InvalidOperationException("The partition lease has no ETag and cannot be released.");
        }

        _releaseAdded = true;
        batch.DeleteItem(
            _lockId,
            new TransactionalBatchItemRequestOptions { IfMatchEtag = _etag });
    }

    internal void MarkCommitted()
    {
        _committed = true;
        _etag = null;
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_committed)
        {
            return;
        }

        try
        {
            OnAbandoned?.Invoke();
        }
        catch (Exception exception)
        {
            if (_logger is not null)
            {
                AbandonedCallbackFailed(_logger, exception);
            }
        }

        if (string.IsNullOrEmpty(_etag))
        {
            return;
        }

        using var cleanupSource = new CancellationTokenSource(_cleanupTimeout);
        try
        {
            await _container
                .DeleteItemAsync<CosmosLockLease>(
                    _lockId,
                    new PartitionKey(_partitionKey),
                    new ItemRequestOptions { IfMatchEtag = _etag },
                    cleanupSource.Token)
                .ConfigureAwait(false);
        }
        catch (CosmosException exception) when (
            exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            // The lease was already released or taken over. Expiry is the recovery path.
        }
        catch (OperationCanceledException)
        {
            if (_logger is not null)
            {
                CleanupTimedOut(_logger, _cleanupTimeout, _lockId, null);
            }
        }
        catch (Exception exception)
        {
            if (_logger is not null)
            {
                ReleaseFailed(_logger, _lockId, exception);
            }
        }
        finally
        {
            _etag = null;
        }
    }

    private static CosmosPartitionLock CreateHandle(
        Container container,
        string partitionKey,
        string lockId,
        string resourceKey,
        DateTimeOffset expiresAt,
        string etag,
        CosmosPartitionLockOptions options,
        ILogger? logger)
    {
        if (string.IsNullOrEmpty(etag))
        {
            throw new InvalidOperationException(
                $"The partition lease '{lockId}' was acquired without an ETag. " +
                "The lease cannot be fenced safely.");
        }

        return new CosmosPartitionLock(
            container,
            partitionKey,
            lockId,
            resourceKey,
            expiresAt,
            etag,
            options.TimeProvider,
            options.CleanupTimeout,
            logger);
    }

    private static async Task<ItemResponse<CosmosLockLease>?> TryTakeOverAsync(
        Container container,
        PartitionKey partitionKey,
        string lockId,
        CosmosLockLease replacementLease,
        CosmosPartitionLockOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await container
                .ReadItemAsync<CosmosLockLease>(
                    lockId,
                    partitionKey,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var lease = existing.Resource;
            if (lease is null ||
                !string.Equals(lease.Id, lockId, StringComparison.Ordinal) ||
                !string.Equals(lease.Discriminator, "DistributedLock", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(lease.Owner) ||
                lease.ExpiresAt == default)
            {
                throw new InvalidOperationException(
                    $"The document with id '{lockId}' is not a valid distributed-lock lease. " +
                    "Refusing to overwrite it; choose a different resource key or clean up the conflicting document.");
            }

            var now = options.TimeProvider.GetUtcNow();
            if (lease.ExpiresAt > now)
            {
                return null;
            }

            replacementLease.ExpiresAt = now + options.LeaseDuration;

            try
            {
                return await container
                    .ReplaceItemAsync(
                        replacementLease,
                        lockId,
                        partitionKey,
                        new ItemRequestOptions
                        {
                            IfMatchEtag = existing.ETag,
                            EnableContentResponseOnWrite = true,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CosmosException exception) when (IsRetryable(exception.StatusCode))
            {
                return null;
            }
        }
        catch (CosmosException exception) when (IsRetryable(exception.StatusCode))
        {
            return null;
        }
    }

    private static async Task DelayBeforeRetryAsync(
        CosmosPartitionLockOptions options,
        CancellationToken cancellationToken)
    {
        var minimum = (int)Math.Max(0, options.RetryDelayMin.TotalMilliseconds);
        var maximum = (int)Math.Max(minimum, options.RetryDelayMax.TotalMilliseconds);
        var delay = minimum == maximum
            ? minimum
            : Random.Shared.Next(minimum, maximum + 1);

        if (delay > 0)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.NotFound
            or HttpStatusCode.PreconditionFailed
            or HttpStatusCode.Conflict
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;

    private static string CreateLockId(string resourceKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey));
        return "distributed-lock-" + Convert.ToHexString(hash);
    }

    private static void ValidateOptions(CosmosPartitionLockOptions options)
    {
        if (options.AcquireTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.AcquireTimeout,
                "AcquireTimeout must be greater than zero.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.LeaseDuration,
                "LeaseDuration must be greater than zero.");
        }

        if (options.CleanupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.CleanupTimeout,
                "CleanupTimeout must be greater than zero.");
        }

        if (options.RetryDelayMin < TimeSpan.Zero ||
            options.RetryDelayMax < options.RetryDelayMin)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Retry delays must be non-negative and RetryDelayMax must be at least RetryDelayMin.");
        }

        if (options.TimeProvider is null)
        {
            throw new ArgumentNullException(nameof(options), "TimeProvider is required.");
        }
    }
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
