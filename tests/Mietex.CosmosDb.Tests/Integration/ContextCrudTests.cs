namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextCrudTests : IntegrationContextTestBase
{
    public ContextCrudTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-crud";

    [Fact]
    public async Task AddSaveAndPointReadPersistsDocumentInTheContextPartition()
    {
        var partitionKey = NewPartition();
        var entity = new TestEntity { Name = "created", Value = 11 };

        await using (var context = new TestDbContext(Options(partitionKey), TestUser()))
        {
            context.Add(entity);
            await context.SaveChangesAsync(Cancellation);

            Assert.Equal(partitionKey, entity.PartitionKey);
            Assert.NotNull(entity.Etag);
            Assert.False(entity.IsNew());

            var found = await context.Entities.FindAsync(entity.Id, Cancellation);
            Assert.NotNull(found);
            Assert.Equal("created", found.Name);
        }

        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal(partitionKey, response.Resource.PartitionKey);
        Assert.Equal("created", response.Resource.Name);
        Assert.Equal(11, response.Resource.Value);
        Assert.NotNull(response.ETag);
    }
}
