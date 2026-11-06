namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextUpsertTests : IntegrationContextTestBase
{
    public ContextUpsertTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-upsert";

    [Fact]
    public async Task UpsertCreatesMissingDocumentWhenEntityIsNotNew()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var entity = new TestEntity { Name = "upserted-create", Etag = "missing-etag" };

        await using (var context = new TestDbContext(options, TestUser()))
        {
            context.Add(entity);
            await context.SaveChangesAsync(Cancellation);
        }

        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal("upserted-create", response.Resource.Name);
        Assert.NotNull(entity.Etag);
        Assert.NotEqual("missing-etag", entity.Etag);
    }

    [Fact]
    public async Task UpsertUpdatesExistingDocument()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "before" });

        entity.Name = "after";

        await using (var context = new TestDbContext(options, TestUser()))
        {
            context.Add(entity);
            await context.SaveChangesAsync(Cancellation);
        }

        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal("after", response.Resource.Name);
    }
}
