using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class PartitionLockAcquireTests : LockingIntegrationTestBase
{
    public PartitionLockAcquireTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "lock-acquire";

    [Fact]
    public async Task AcquireCreatesDeterministicLeaseWithOwnerAndExpiry()
    {
        var partitionKey = NewPartition();
        var resourceKey1 = "resource-" + Guid.NewGuid().ToString("N");
        var resourceKey2 = "resource-" + Guid.NewGuid().ToString("N");
        var leaseDuration = TimeSpan.FromSeconds(2);
        var options = LockOptions(leaseDuration: leaseDuration);

        var before = DateTimeOffset.UtcNow;
        var first = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey1,
            options,
            cancellationToken: Cancellation);
        var after = DateTimeOffset.UtcNow;
        var second = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey2,
            options,
            cancellationToken: Cancellation);

        var firstLease = await ReadLeaseAsync(first.LockId, partitionKey);
        var secondLease = await ReadLeaseAsync(second.LockId, partitionKey);
        var expectedId = "distributed-lock-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey1)));

        Assert.Matches("^distributed-lock-[0-9A-F]{64}$", first.LockId);
        Assert.Equal(expectedId, first.LockId);
        Assert.Equal(resourceKey1, first.ResourceKey);
        Assert.Equal(first.LockId, firstLease.Id);
        Assert.Equal(partitionKey, firstLease.PartitionKey);
        Assert.Equal("DistributedLock", firstLease.Discriminator);
        Assert.NotEmpty(firstLease.Owner);
        Assert.NotEqual(firstLease.Owner, secondLease.Owner);
        Assert.Equal(firstLease.ExpiresAt, first.ExpiresAt);
        Assert.InRange(firstLease.ExpiresAt, before + leaseDuration, after + leaseDuration);

        await first.DisposeAsync();
        await second.DisposeAsync();
        await AssertLeaseAbsentAsync(first.LockId, partitionKey);
        await AssertLeaseAbsentAsync(second.LockId, partitionKey);
    }

    [Fact]
    public async Task SecondAcquireTimesOutWhileLeaseIsActive()
    {
        var partitionKey = NewPartition();
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var holderOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(1),
            leaseDuration: DefaultLeaseDuration);
        var holder = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey,
            holderOptions,
            cancellationToken: Cancellation);
        var beforeLease = await ReadLeaseAsync(holder.LockId, partitionKey);

        var contenderOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(1),
            leaseDuration: DefaultLeaseDuration);
        await Assert.ThrowsAsync<TimeoutException>(() =>
            CosmosPartitionLock.AcquireAsync(
                Container,
                partitionKey,
                resourceKey,
                contenderOptions,
                cancellationToken: Cancellation));

        var afterLease = await ReadLeaseAsync(holder.LockId, partitionKey);
        Assert.Equal(beforeLease.Owner, afterLease.Owner);

        await holder.DisposeAsync();
        await AssertLeaseAbsentAsync(holder.LockId, partitionKey);
    }

    [Fact]
    public async Task CallerCancellationThrowsOperationCanceled()
    {
        var partitionKey = NewPartition();
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var holderOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(1),
            leaseDuration: DefaultLeaseDuration);
        var holder = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey,
            holderOptions,
            cancellationToken: Cancellation);
        var beforeLease = await ReadLeaseAsync(holder.LockId, partitionKey);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var contenderOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: DefaultLeaseDuration);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CosmosPartitionLock.AcquireAsync(
                Container,
                partitionKey,
                resourceKey,
                contenderOptions,
                cancellationToken: cancellation.Token));

        var afterLease = await ReadLeaseAsync(holder.LockId, partitionKey);
        Assert.Equal(beforeLease.Owner, afterLease.Owner);

        await holder.DisposeAsync();
        await AssertLeaseAbsentAsync(holder.LockId, partitionKey);
    }

    [Fact]
    public async Task TakeoverSucceedsAfterLeaseExpires()
    {
        var partitionKey = NewPartition();
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var firstOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(1));
        var first = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey,
            firstOptions,
            cancellationToken: Cancellation);
        var firstLease = await ReadLeaseAsync(first.LockId, partitionKey);

        await WaitUntilExpiredAsync(first, Cancellation);

        var secondOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(2));
        var second = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey,
            secondOptions,
            cancellationToken: Cancellation);

        Assert.Equal(first.LockId, second.LockId);
        Assert.True(second.ExpiresAt > first.ExpiresAt);
        var secondLease = await ReadLeaseAsync(second.LockId, partitionKey);
        Assert.NotEqual(firstLease.Owner, secondLease.Owner);

        await first.DisposeAsync();
        await second.DisposeAsync();
        await AssertLeaseAbsentAsync(second.LockId, partitionKey);
    }

    [Fact]
    public async Task ConcurrentAcquirersAfterExpiryHaveExactlyOneWinner()
    {
        var partitionKey = NewPartition();
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var seedOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(1));
        var seed = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey,
            seedOptions,
            cancellationToken: Cancellation);

        await WaitUntilExpiredAsync(seed, Cancellation);

        var contenderOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(1),
            leaseDuration: TimeSpan.FromSeconds(2));
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            try
            {
                return (object)await CosmosPartitionLock.AcquireAsync(
                    Container,
                    partitionKey,
                    resourceKey,
                    contenderOptions,
                    cancellationToken: Cancellation);
            }
            catch (TimeoutException exception)
            {
                return exception;
            }
            catch (OperationCanceledException exception)
            {
                return exception;
            }
        }));

        var winners = results.OfType<CosmosPartitionLock>().ToList();
        var failures = results.OfType<Exception>().ToList();
        var winner = Assert.Single(winners);
        Assert.Equal(2, failures.Count);
        Assert.All(
            failures,
            failure => Assert.True(
                failure is TimeoutException or OperationCanceledException,
                $"Unexpected acquisition failure: {failure.GetType().Name}: {failure.Message}"));

        await seed.DisposeAsync();
        await winner.DisposeAsync();
        await AssertLeaseAbsentAsync(winner.LockId, partitionKey);
    }

    [Fact]
    public async Task CleanupIgnoresNotFoundWhenLeaseIsAlreadyGone()
    {
        var partitionKey = NewPartition();
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var options = LockOptions(leaseDuration: TimeSpan.FromSeconds(2));
        var handle = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey,
            options,
            cancellationToken: Cancellation);

        await Container.DeleteItemAsync<CosmosLockLease>(
            handle.LockId,
            new PartitionKey(partitionKey),
            cancellationToken: Cancellation);

        var exception = await Record.ExceptionAsync(() => handle.DisposeAsync().AsTask());
        Assert.Null(exception);
        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
    }

    [Fact]
    public async Task StaleHandleDisposalDoesNotDeleteNewOwnerLease()
    {
        var partitionKey = NewPartition();
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var firstOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(1));
        var first = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey,
            firstOptions,
            cancellationToken: Cancellation);

        await WaitUntilExpiredAsync(first, Cancellation);

        var secondOptions = LockOptions(
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(2));
        var second = await CosmosPartitionLock.AcquireAsync(
            Container,
            partitionKey,
            resourceKey,
            secondOptions,
            cancellationToken: Cancellation);
        var secondLease = await ReadLeaseAsync(second.LockId, partitionKey);

        var exception = await Record.ExceptionAsync(() => first.DisposeAsync().AsTask());
        Assert.Null(exception);

        var leaseAfter = await ReadLeaseAsync(second.LockId, partitionKey);
        Assert.Equal(secondLease.Owner, leaseAfter.Owner);

        await second.DisposeAsync();
        await AssertLeaseAbsentAsync(second.LockId, partitionKey);
    }

    [Fact]
    public async Task SameResourceKeyInDifferentPartitionsIsIndependent()
    {
        var resourceKey = "shared-resource-" + Guid.NewGuid().ToString("N");
        var firstPartition = NewPartition();
        var secondPartition = NewPartition();
        var options = LockOptions();

        var first = await CosmosPartitionLock.AcquireAsync(
            Container,
            firstPartition,
            resourceKey,
            options,
            cancellationToken: Cancellation);
        var second = await CosmosPartitionLock.AcquireAsync(
            Container,
            secondPartition,
            resourceKey,
            options,
            cancellationToken: Cancellation);

        Assert.Equal(first.LockId, second.LockId);
        var firstLease = await ReadLeaseAsync(first.LockId, firstPartition);
        var secondLease = await ReadLeaseAsync(second.LockId, secondPartition);
        Assert.Equal(firstPartition, firstLease.PartitionKey);
        Assert.Equal(secondPartition, secondLease.PartitionKey);
        Assert.NotEqual(firstLease.Owner, secondLease.Owner);

        await first.DisposeAsync();
        await second.DisposeAsync();
        await AssertLeaseAbsentAsync(first.LockId, firstPartition);
        await AssertLeaseAbsentAsync(second.LockId, secondPartition);
    }
}
