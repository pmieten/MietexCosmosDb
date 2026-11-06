using System.Net;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextConcurrencyTests : IntegrationContextTestBase
{
    public ContextConcurrencyTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-concurrency";

    private CosmosDbOptions ConcurrencyOptions(string partitionKey) =>
        Options(partitionKey, options => options with
        {
            UpdateMode = CosmosDbUpdateMode.Replace,
            ConcurrencyMode = CosmosDbConcurrencyMode.MatchEtag,
        });

    [Fact]
    public async Task StaleReplaceFailsWithPreconditionAndFreshEtagSucceeds()
    {
        var partitionKey = NewPartition();
        var options = ConcurrencyOptions(partitionKey);
        var seed = await CreateSeedAsync(options, new TestEntity { Name = "v1" });

        await using var winner = new TestDbContext(options, TestUser());
        await using var loser = new TestDbContext(options, TestUser());
        var winnerDocument = await winner.Entities.FindAsync(seed.Id, Cancellation);
        var loserDocument = await loser.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(winnerDocument);
        Assert.NotNull(loserDocument);

        winnerDocument.Name = "v2";
        winner.Add(winnerDocument);
        await winner.SaveChangesAsync(Cancellation);

        loserDocument.Name = "stale";
        loser.Add(loserDocument);
        var exception = await Assert.ThrowsAsync<CosmosBatchException>(
            () => loser.SaveChangesAsync(Cancellation));
        Assert.Contains(
            exception.Failures,
            failure => failure.StatusCode == HttpStatusCode.PreconditionFailed);

        var current = await ReadRawAsync(seed.Id, partitionKey);
        Assert.Equal("v2", current.Resource.Name);

        loser.ClearCache();
        var fresh = await loser.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(fresh);
        fresh.Name = "v3";
        loser.Add(fresh);
        await loser.SaveChangesAsync(Cancellation);

        current = await ReadRawAsync(seed.Id, partitionKey);
        Assert.Equal("v3", current.Resource.Name);
    }

    [Fact]
    public async Task StaleDeleteFailsWithPreconditionAndFreshDeleteSucceeds()
    {
        var partitionKey = NewPartition();
        var options = ConcurrencyOptions(partitionKey);
        var seed = await CreateSeedAsync(options, new TestEntity { Name = "v1" });

        await using var winner = new TestDbContext(options, TestUser());
        await using var loser = new TestDbContext(options, TestUser());
        var winnerDocument = await winner.Entities.FindAsync(seed.Id, Cancellation);
        var loserDocument = await loser.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(winnerDocument);
        Assert.NotNull(loserDocument);

        winnerDocument.Name = "v2";
        winner.Add(winnerDocument);
        await winner.SaveChangesAsync(Cancellation);

        loser.Delete(loserDocument, useSoftDeleteIfPossible: false);
        var exception = await Assert.ThrowsAsync<CosmosBatchException>(
            () => loser.SaveChangesAsync(Cancellation));
        Assert.Contains(
            exception.Failures,
            failure => failure.StatusCode == HttpStatusCode.PreconditionFailed);

        var current = await ReadRawAsync(seed.Id, partitionKey);
        Assert.Equal("v2", current.Resource.Name);

        loser.ClearCache();
        var fresh = await loser.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(fresh);
        loser.Delete(fresh, useSoftDeleteIfPossible: false);
        await loser.SaveChangesAsync(Cancellation);

        var notFound = await Assert.ThrowsAsync<CosmosException>(
            () => ReadRawAsync(seed.Id, partitionKey));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task StalePatchFailsWithPreconditionAndFreshPatchSucceeds()
    {
        var partitionKey = NewPartition();
        var options = ConcurrencyOptions(partitionKey);
        var seed = await CreateSeedAsync(options, new TestEntity { Name = "v1" });

        await using var winner = new TestDbContext(options, TestUser());
        await using var loser = new TestDbContext(options, TestUser());
        var winnerDocument = await winner.Entities.FindAsync(seed.Id, Cancellation);
        var loserDocument = await loser.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(winnerDocument);
        Assert.NotNull(loserDocument);

        winnerDocument.Name = "v2";
        winner.Add(winnerDocument);
        await winner.SaveChangesAsync(Cancellation);

        loser.Patch(loserDocument, [PatchOperation.Set("/Name", "stale-patch")]);
        var exception = await Assert.ThrowsAsync<CosmosBatchException>(
            () => loser.SaveChangesAsync(Cancellation));
        Assert.Contains(
            exception.Failures,
            failure => failure.StatusCode == HttpStatusCode.PreconditionFailed);

        var current = await ReadRawAsync(seed.Id, partitionKey);
        Assert.Equal("v2", current.Resource.Name);

        loser.ClearCache();
        var fresh = await loser.Entities.FindAsync(seed.Id, Cancellation);
        Assert.NotNull(fresh);
        loser.Patch(fresh, [PatchOperation.Set("/Name", "fresh-patch")]);
        await loser.SaveChangesAsync(Cancellation);

        current = await ReadRawAsync(seed.Id, partitionKey);
        Assert.Equal("fresh-patch", current.Resource.Name);
    }
}
