using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Time.Testing;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class LockingContextLockTests : LockingIntegrationTestBase
{
    public LockingContextLockTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "lock-context";

    [Fact]
    public async Task SuccessfulSaveReleasesLeaseInTransactionWithoutSecondDelete()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var entity = new TestEntity { Name = "locked-write" };

        await using var context = new LockingTestDbContext(options, TestUser());
        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        var leaseBefore = await ReadLeaseAsync(handle.LockId, partitionKey);
        Assert.Equal("DistributedLock", leaseBefore.Discriminator);

        context.Add(entity);
        await context.SaveChangesAsync(Cancellation);

        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal("locked-write", response.Resource.Name);

        // A new owner must survive disposal of the old context: proof that the successful save
        // marked the lock committed and disposal did not issue a second cleanup delete.
        await using var second = new LockingTestDbContext(options, TestUser());
        var secondHandle = await second.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        await context.DisposeAsync();
        var stillThere = await ReadLeaseAsync(secondHandle.LockId, partitionKey);
        Assert.Equal(secondHandle.LockId, stillThere.Id);
        await second.ClearAsync(Cancellation);
    }

    [Fact]
    public async Task FailedSaveReleasesLeaseAndDiscardsPendingChanges()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var duplicateId = Guid.NewGuid().ToString("N");

        await using (var context = new LockingTestDbContext(options, TestUser()))
        {
            var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
            context.Add(new TestEntity { Id = duplicateId, Name = "first" });
            context.Add(new TestEntity { Id = duplicateId, Name = "second" });

            await Assert.ThrowsAsync<CosmosBatchException>(
                () => context.SaveChangesAsync(Cancellation));

            await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(0, await CountPartitionAsync(partitionKey));

        await using (var next = new LockingTestDbContext(options, TestUser()))
        {
            var nextHandle = await next.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
            Assert.NotNull(nextHandle);
            await next.ClearAsync(Cancellation);
            await AssertLeaseAbsentAsync(nextHandle.LockId, partitionKey);
        }
    }

    [Fact]
    public async Task AbandonedHandleDiscardsChangesAndCachesAndAllowsReacquisition()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var seed = await CreateSeedAsync(options, new TestEntity { Name = "original" });

        await using var context = new LockingTestDbContext(options, TestUser());
        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        var cached = await context.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(cached);
        Assert.Equal("original", cached.Name);

        context.Add(new TestEntity { Name = "discarded" });

        var raw = await ReadRawAsync(seed.Id, partitionKey);
        raw.Resource.Name = "changed";
        await Container.ReplaceItemAsync(
            raw.Resource,
            seed.Id,
            new PartitionKey(partitionKey),
            cancellationToken: Cancellation);

        await handle.DisposeAsync();
        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);

        await context.SaveChangesAsync(Cancellation);
        Assert.Equal(1, await CountPartitionAsync(partitionKey));

        var refreshed = await context.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(refreshed);
        Assert.Equal("changed", refreshed.Name);

        var reacquired = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        Assert.NotNull(reacquired);
        await context.ClearAsync(Cancellation);
        await AssertLeaseAbsentAsync(reacquired.LockId, partitionKey);
    }

    [Fact]
    public async Task StaleOwnerSaveFailsFencedByLeaseTakeover()
    {
        var partitionKey = NewPartition();
        var firstClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var firstOptions = ContextOptions(
            partitionKey,
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(1),
            timeProvider: firstClock);
        var secondOptions = ContextOptions(
            partitionKey,
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(2));
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var staleEntity = new TestEntity { Name = "stale" };

        await using var first = new LockingTestDbContext(firstOptions, TestUser());
        var firstHandle = await first.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        first.Add(staleEntity);

        await WaitUntilExpiredAsync(firstHandle, Cancellation);

        await using var second = new LockingTestDbContext(secondOptions, TestUser());
        var secondHandle = await second.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);

        // The first context's frozen clock still considers its lease valid, so it reaches the batch
        // and the takeover's ETag change fences it with 412 instead of the local expiry guard.
        var exception = await Assert.ThrowsAsync<CosmosBatchException>(
            () => first.SaveChangesAsync(Cancellation));
        Assert.Equal(2, exception.OperationCount);
        Assert.Contains(
            exception.Failures,
            failure => failure.StatusCode == HttpStatusCode.PreconditionFailed);

        await second.ClearAsync(Cancellation);
        await AssertLeaseAbsentAsync(secondHandle.LockId, partitionKey);

        await using var third = new LockingTestDbContext(secondOptions, TestUser());
        Assert.Null(await third.Entities.FindAsync(staleEntity.Id, Cancellation));
        Assert.Equal(0, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task ExpiredLocalHandleFailsFastBeforeAnyWrite()
    {
        var partitionKey = NewPartition();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = ContextOptions(
            partitionKey,
            acquireTimeout: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(1),
            timeProvider: clock);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        await using var context = new LockingTestDbContext(options, TestUser());
        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        context.Add(new TestEntity { Name = "must-not-write" });

        clock.Advance(TimeSpan.FromSeconds(2));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync(Cancellation));

        Assert.Contains("expired", exception.Message);
        Assert.Equal(0, await CountPartitionAsync(partitionKey));
        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
    }

    [Fact]
    public async Task DuplicateResourceKeyInOneContextIsRejected()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        await using var context = new LockingTestDbContext(options, TestUser());
        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation));

        await AssertLeaseExistsAsync(handle.LockId, partitionKey);
        await context.ClearAsync(Cancellation);
        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
    }

    [Fact]
    public async Task AcquiringLockClearsSetCaches()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");
        var seed = await CreateSeedAsync(options, new TestEntity { Name = "v1" });

        await using var context = new LockingTestDbContext(options, TestUser());
        var cached = await context.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(cached);
        Assert.Equal("v1", cached.Name);

        var raw = await ReadRawAsync(seed.Id, partitionKey);
        raw.Resource.Name = "external";
        await Container.ReplaceItemAsync(
            raw.Resource,
            seed.Id,
            new PartitionKey(partitionKey),
            cancellationToken: Cancellation);

        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        var refreshed = await context.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(refreshed);
        Assert.Equal("external", refreshed.Name);

        await context.ClearAsync(Cancellation);
        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
    }

    [Fact]
    public async Task ExpiredHandleIsRejectedAtBatchPreparation()
    {
        var partitionKey = NewPartition();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = ContextOptions(
            partitionKey,
            leaseDuration: TimeSpan.FromSeconds(1),
            timeProvider: clock);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        await using var context = new LockingTestDbContext(options, TestUser());
        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        clock.Advance(TimeSpan.FromSeconds(2));

        var batch = context.Container.CreateTransactionalBatch(new PartitionKey(partitionKey));
        Assert.Throws<InvalidOperationException>(() => handle.AddReleaseToBatch(batch));

        await context.ClearAsync(Cancellation);
    }

    [Fact]
    public async Task EmptySaveReleasesLeaseWithoutBusinessWrite()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        var context = new LockingTestDbContext(options, TestUser());
        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);

        await context.SaveChangesAsync(Cancellation);

        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
        Assert.Equal(0, await CountPartitionAsync(partitionKey));

        await context.DisposeAsync();
        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
    }

    [Fact]
    public async Task BatchLimitCountsLeaseReleaseWithSmallLimit()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey, batchOperationLimit: 3);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        await using (var context = new LockingTestDbContext(options, TestUser()))
        {
            var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
            for (var index = 0; index < 3; index++)
            {
                context.Add(new TestEntity { Name = $"entity-{index}" });
            }

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.SaveChangesAsync(Cancellation));
            await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
        }

        Assert.Equal(0, await CountPartitionAsync(partitionKey));

        await using (var context = new LockingTestDbContext(options, TestUser()))
        {
            var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
            context.Add(new TestEntity { Name = "first" });
            context.Add(new TestEntity { Name = "second" });

            await context.SaveChangesAsync(Cancellation);
            await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
        }

        Assert.Equal(2, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task BatchLimitCountsLeaseReleaseWithDefaultLimit()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        await using (var context = new LockingTestDbContext(options, TestUser()))
        {
            var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
            for (var index = 0; index < 100; index++)
            {
                context.Add(new TestEntity { Name = $"entity-{index}" });
            }

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.SaveChangesAsync(Cancellation));
            await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
        }

        Assert.Equal(0, await CountPartitionAsync(partitionKey));

        await using (var context = new LockingTestDbContext(options, TestUser()))
        {
            var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
            for (var index = 0; index < 99; index++)
            {
                context.Add(new TestEntity { Name = $"entity-{index}" });
            }

            await context.SaveChangesAsync(Cancellation);
            await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
        }

        Assert.Equal(99, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task CanceledClearAsyncDoesNotLeaveHalfClearedState()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        await using var context = new LockingTestDbContext(options, TestUser());
        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        context.Add(new TestEntity { Name = "pending" });

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.ClearAsync(cancellation.Token));

        await AssertLeaseExistsAsync(handle.LockId, partitionKey);

        await context.ClearAsync(Cancellation);
        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);
        await context.SaveChangesAsync(Cancellation);
        Assert.Equal(0, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task ClearAndClearAsyncReleaseLeases()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        await using var context = new LockingTestDbContext(options, TestUser());
        var firstHandle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        await AssertLeaseExistsAsync(firstHandle.LockId, partitionKey);

        await context.ClearAsync(Cancellation);
        await AssertLeaseAbsentAsync(firstHandle.LockId, partitionKey);

        var secondHandle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);
        await AssertLeaseExistsAsync(secondHandle.LockId, partitionKey);

        context.Clear();
        await AssertLeaseAbsentAsync(secondHandle.LockId, partitionKey);
    }

    [Fact]
    public async Task DisposalIsIdempotentAndRejectsWrites()
    {
        var partitionKey = NewPartition();
        var options = ContextOptions(partitionKey);
        var resourceKey = "resource-" + Guid.NewGuid().ToString("N");

        var context = new LockingTestDbContext(options, TestUser());
        var handle = await context.PerformLockUntilSaveChangesAsync(resourceKey, Cancellation);

        await context.DisposeAsync();
        await AssertLeaseAbsentAsync(handle.LockId, partitionKey);

        await context.DisposeAsync();
        var exception = Record.Exception(context.Dispose);
        Assert.Null(exception);

        Assert.Throws<ObjectDisposedException>(() => context.Add(new TestEntity()));
        Assert.Throws<ObjectDisposedException>(() => context.Delete(new TestEntity()));
        Assert.Throws<ObjectDisposedException>(
            () => context.Patch("id", [PatchOperation.Set("/Name", "patched")]));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => context.SaveChangesAsync(Cancellation));
    }
}
