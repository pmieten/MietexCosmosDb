using System.Runtime.CompilerServices;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextQueryTests : IntegrationContextTestBase
{
    public ContextQueryTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-query";

    [Fact]
    public async Task SetsFilterByDiscriminatorInsideSharedPartition()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var plain = new TestEntity { Name = "plain" };
        var other = new OtherTestEntity { Name = "other", Extra = "extra" };

        await using (var context = new TestDbContext(options, TestUser()))
        {
            context.Add(plain);
            context.Add(other);
            await context.SaveChangesAsync(Cancellation);

            var plainResults = await context.Entities.Query.ToListWithFeedSupportAsync(Cancellation);
            var plainResult = Assert.Single(plainResults);
            Assert.Equal(plain.Id, plainResult.Id);
            Assert.Equal(nameof(TestEntity), plainResult.Discriminator);

            var otherResults = await context.Others.Query.ToListWithFeedSupportAsync(Cancellation);
            var otherResult = Assert.Single(otherResults);
            Assert.Equal(other.Id, otherResult.Id);
            Assert.Equal(nameof(OtherTestEntity), otherResult.Discriminator);
            Assert.Equal("extra", otherResult.Extra);
        }
    }

    [Fact]
    public async Task SameIdInAnotherPartitionIsIsolated()
    {
        var firstPartition = NewPartition();
        var secondPartition = NewPartition();
        var id = Guid.NewGuid().ToString("N");
        var first = new TestEntity { Id = id, Name = "first-partition" };
        var second = new TestEntity { Id = id, Name = "second-partition" };

        await CreateSeedAsync(Options(firstPartition), first);
        await CreateSeedAsync(Options(secondPartition), second);

        await using var context = new TestDbContext(Options(firstPartition), TestUser());
        var results = await context.Entities.Query.ToListWithFeedSupportAsync(Cancellation);
        var result = Assert.Single(results);
        Assert.Equal("first-partition", result.Name);

        var found = await context.Entities.FindAsync(id, Cancellation);
        Assert.NotNull(found);
        Assert.Equal("first-partition", found.Name);
    }

    [Fact]
    public async Task GetItemQueryIteratorAndFeedExtensionEnumerateAllResults()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);

        await using (var seed = new TestDbContext(options, TestUser()))
        {
            for (var index = 0; index < 3; index++)
            {
                seed.Add(new TestEntity { Name = $"feed-{index}" });
            }

            await seed.SaveChangesAsync(Cancellation);
        }

        await using var context = new TestDbContext(options, TestUser());
        using var iterator = context.GetItemQueryIterator<TestEntity>(
            new QueryDefinition("SELECT * FROM c"));

        var results = new List<TestEntity>();
        await foreach (var item in iterator.ToAsyncEnumerableFromFeedIterator(Cancellation))
        {
            results.Add(item);
        }

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task FeedExtensionEnumeratesMultiplePages()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);

        await using (var seed = new TestDbContext(options, TestUser()))
        {
            for (var index = 0; index < 3; index++)
            {
                seed.Add(new TestEntity { Name = $"page-{index}" });
            }

            await seed.SaveChangesAsync(Cancellation);
        }

        await using var context = new TestDbContext(options, TestUser());
        using var iterator = context.Container.GetItemQueryIterator<TestEntity>(
            new QueryDefinition("SELECT * FROM c"),
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
                MaxItemCount = 1,
            });

        var results = new List<TestEntity>();
        await foreach (var item in iterator.ToAsyncEnumerableFromFeedIterator(Cancellation))
        {
            results.Add(item);
        }

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task ToListWithFeedSupportAppliesTheAsyncTransform()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);

        await using (var seed = new TestDbContext(options, TestUser()))
        {
            for (var index = 0; index < 5; index++)
            {
                seed.Add(new TestEntity { Name = $"transform-{index}", Value = index });
            }

            await seed.SaveChangesAsync(Cancellation);
        }

        await using var context = new TestDbContext(options, TestUser());
        var results = await context.Entities.Query.ToListWithFeedSupportAsync(
            source => OnlyEven(source, Cancellation),
            Cancellation);

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.Equal(0, result.Value % 2));
    }

    [Fact]
    public async Task CancellationStopsFeedEnumeration()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);

        await using (var seed = new TestDbContext(options, TestUser()))
        {
            seed.Add(new TestEntity { Name = "cancelled" });
            await seed.SaveChangesAsync(Cancellation);
        }

        await using var context = new TestDbContext(options, TestUser());
        using var iterator = context.GetItemQueryIterator<TestEntity>(
            new QueryDefinition("SELECT * FROM c"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in iterator.ToAsyncEnumerableFromFeedIterator(cancellation.Token))
            {
            }
        });
    }

    [Fact]
    public async Task EnumerableQueryFallsBackToInMemoryExecution()
    {
        IQueryable<TestEntity> query = new[]
        {
            new TestEntity { Name = "a", Value = 1 },
            new TestEntity { Name = "b", Value = 2 },
            new TestEntity { Name = "c", Value = 3 },
        }.AsQueryable().Where(entity => entity.Value > 1);

        var results = await query.ToListWithFeedSupportAsync(Cancellation);

        Assert.Equal(2, results.Count);
        Assert.All(results, entity => Assert.True(entity.Value > 1));
    }

    private static async IAsyncEnumerable<TestEntity> OnlyEven(
        IAsyncEnumerable<TestEntity> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entity in source.WithCancellation(cancellationToken))
        {
            if (entity.Value % 2 == 0)
            {
                yield return entity;
            }
        }
    }
}
