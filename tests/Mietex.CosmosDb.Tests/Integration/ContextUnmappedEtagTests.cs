namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextUnmappedEtagTests : IntegrationContextTestBase
{
    public ContextUnmappedEtagTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-unmapped-etag";

    [Fact]
    public async Task PointReadCompensatesForMissingEtagJsonPropertyMapping()
    {
        var partitionKey = NewPartition();
        var seedOptions = Options(partitionKey);
        var readOptions = Options(partitionKey, options => options with
        {
            UpdateMode = CosmosDbUpdateMode.Replace,
            ConcurrencyMode = CosmosDbConcurrencyMode.MatchEtag,
        });
        var seed = new UnmappedEtagEntity { Name = "v1" };

        await using (var writer = new UnmappedEtagDbContext(seedOptions, TestUser()))
        {
            writer.Add(seed);
            await writer.SaveChangesAsync(Cancellation);
        }

        await using (var reader = new UnmappedEtagDbContext(readOptions, TestUser()))
        {
            var found = await reader.Entities.FindAsync(seed.Id, Cancellation);
            Assert.NotNull(found);
            Assert.False(string.IsNullOrWhiteSpace(found.Etag));

            found.Name = "v2";
            reader.Add(found);
            await reader.SaveChangesAsync(Cancellation);
        }

        var current = await Container.ReadItemAsync<UnmappedEtagEntity>(
            seed.Id,
            new Microsoft.Azure.Cosmos.PartitionKey(partitionKey),
            cancellationToken: Cancellation);
        Assert.Equal("v2", current.Resource.Name);
    }
}
