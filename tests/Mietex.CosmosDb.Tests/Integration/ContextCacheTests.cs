namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextCacheTests : IntegrationContextTestBase
{
    public ContextCacheTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-cache";

    [Fact]
    public async Task FindAsyncReturnsNullForNullMissingAndNumericMiss()
    {
        await using var context = new TestDbContext(Options(NewPartition()), TestUser());

        Assert.Null(await context.Entities.FindAsync((string?)null, Cancellation));
        Assert.Null(await context.Entities.FindAsync((long?)null, Cancellation));
        Assert.Null(await context.Entities.FindAsync("does-not-exist", Cancellation));
        Assert.Null(await context.Entities.FindAsync(123456789L, Cancellation));
    }

    [Fact]
    public async Task NumericFindAsyncHitsAndSharesTheStringCacheKey()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        await CreateSeedAsync(options, new TestEntity { Id = "123", Name = "numeric" });

        await using var context = new TestDbContext(options, TestUser());
        var byString = await context.Entities.FindAsync("123", Cancellation);
        var byNumber = await context.Entities.FindAsync(123L, Cancellation);

        Assert.NotNull(byString);
        Assert.NotNull(byNumber);
        Assert.Same(byString, byNumber);
        Assert.Equal("numeric", byNumber.Name);
    }

    [Fact]
    public async Task CachedReadReturnsStaleValueUntilClearCache()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "v1" });

        await using var reader = new TestDbContext(options, TestUser());
        var first = await reader.Entities.FindAsync(entity.Id, Cancellation);
        Assert.NotNull(first);
        Assert.Equal("v1", first.Name);

        await using (var writer = new TestDbContext(options, TestUser()))
        {
            var document = await writer.Entities.FindAsync(entity.Id, Cancellation);
            Assert.NotNull(document);
            document.Name = "v2";
            writer.Add(document);
            await writer.SaveChangesAsync(Cancellation);
        }

        var stale = await reader.Entities.FindAsync(entity.Id, Cancellation);
        Assert.NotNull(stale);
        Assert.Equal("v1", stale.Name);

        reader.ClearCache();

        var fresh = await reader.Entities.FindAsync(entity.Id, Cancellation);
        Assert.NotNull(fresh);
        Assert.Equal("v2", fresh.Name);
    }

    [Fact]
    public async Task EnableCacheFalseReadsThrough()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey, o => o with { EnableCache = false });
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "v1" });

        await using var reader = new TestDbContext(options, TestUser());
        var first = await reader.Entities.FindAsync(entity.Id, Cancellation);
        Assert.NotNull(first);
        Assert.Equal("v1", first.Name);

        await using (var writer = new TestDbContext(options, TestUser()))
        {
            var document = await writer.Entities.FindAsync(entity.Id, Cancellation);
            Assert.NotNull(document);
            document.Name = "v2";
            writer.Add(document);
            await writer.SaveChangesAsync(Cancellation);
        }

        var second = await reader.Entities.FindAsync(entity.Id, Cancellation);
        Assert.NotNull(second);
        Assert.Equal("v2", second.Name);
    }

    [Fact]
    public async Task HardDeleteDoesNotResurrectFromCache()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "cached" });

        await using var context = new TestDbContext(options, TestUser());
        var cached = await context.Entities.FindAsync(entity.Id, Cancellation);
        Assert.NotNull(cached);

        context.Delete(cached, useSoftDeleteIfPossible: false);
        await context.SaveChangesAsync(Cancellation);

        Assert.Null(await context.Entities.FindAsync(entity.Id, Cancellation));
    }
}
