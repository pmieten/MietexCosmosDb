using System.Net;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Integration;

/// <summary>
/// Shared helpers for distributed-lock integration tests. Each concrete test class owns one real
/// emulator container and each test uses a fresh partition key.
/// </summary>
public abstract class LockingIntegrationTestBase : IntegrationContextTestBase
{
    protected static readonly TimeSpan ShortAcquireTimeout = TimeSpan.FromSeconds(1);
    protected static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(30);
    protected static readonly TimeSpan ExpiryLeaseDuration = TimeSpan.FromSeconds(1);
    protected static readonly TimeSpan ShortCleanupTimeout = TimeSpan.FromSeconds(2);

    protected LockingIntegrationTestBase(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected CosmosDbOptions ContextOptions(
        string partitionKey,
        TimeSpan? acquireTimeout = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? cleanupTimeout = null,
        int batchOperationLimit = 100,
        TimeProvider? timeProvider = null,
        Func<CosmosDbOptions, CosmosDbOptions>? configure = null) =>
        Options(partitionKey, options =>
        {
            var configured = configure?.Invoke(options) ?? options;
            return configured with
            {
                LockAcquireTimeout = acquireTimeout ?? ShortAcquireTimeout,
                LockLeaseDuration = leaseDuration ?? DefaultLeaseDuration,
                LockCleanupTimeout = cleanupTimeout ?? ShortCleanupTimeout,
                BatchOperationLimit = batchOperationLimit,
                TimeProvider = timeProvider ?? configured.TimeProvider,
            };
        });

    protected static CosmosPartitionLockOptions LockOptions(
        TimeSpan? acquireTimeout = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? cleanupTimeout = null,
        TimeProvider? timeProvider = null) => new()
        {
            AcquireTimeout = acquireTimeout ?? ShortAcquireTimeout,
            LeaseDuration = leaseDuration ?? DefaultLeaseDuration,
            CleanupTimeout = cleanupTimeout ?? ShortCleanupTimeout,
            RetryDelayMin = TimeSpan.FromMilliseconds(20),
            RetryDelayMax = TimeSpan.FromMilliseconds(60),
            TimeProvider = timeProvider ?? TimeProvider.System,
        };

    protected async Task<CosmosLockLease> ReadLeaseAsync(string lockId, string partitionKey)
    {
        var response = await Container.ReadItemAsync<CosmosLockLease>(
            lockId,
            new PartitionKey(partitionKey),
            cancellationToken: Cancellation);
        return response.Resource;
    }

    protected async Task<CosmosLockLease> AssertLeaseExistsAsync(string lockId, string partitionKey)
    {
        var lease = await ReadLeaseAsync(lockId, partitionKey);
        Assert.Equal(lockId, lease.Id);
        return lease;
    }

    protected async Task AssertLeaseAbsentAsync(string lockId, string partitionKey)
    {
        var exception = await Assert.ThrowsAsync<CosmosException>(
            () => Container.ReadItemAsync<CosmosLockLease>(
                lockId,
                new PartitionKey(partitionKey),
                cancellationToken: Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    protected static async Task WaitUntilExpiredAsync(
        CosmosPartitionLock handle,
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var deadline = handle.ExpiresAt + TimeSpan.FromMilliseconds(200);
        var remaining = deadline - clock.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }
}
