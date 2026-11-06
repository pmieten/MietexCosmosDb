using System.Net;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextReplaceTests : IntegrationContextTestBase
{
    public ContextReplaceTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-replace";

    [Fact]
    public async Task ReplaceUpdatesExistingDocument()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey, o => o with { UpdateMode = CosmosDbUpdateMode.Replace });
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

    [Fact]
    public async Task ReplaceMissingDocumentFailsBatchWithNotFoundAndNoPartialWrite()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey, o => o with { UpdateMode = CosmosDbUpdateMode.Replace });
        var created = new TestEntity { Name = "must-roll-back" };
        var missing = new TestEntity { Name = "missing", Etag = "etag" };

        await using (var context = new TestDbContext(options, TestUser()))
        {
            context.Add(created);
            context.Add(missing);

            var exception = await Assert.ThrowsAsync<CosmosBatchException>(
                () => context.SaveChangesAsync(Cancellation));

            Assert.Equal(2, exception.OperationCount);
            Assert.Contains(
                exception.Failures,
                failure => failure.Index == 1 && failure.StatusCode == HttpStatusCode.NotFound);
        }

        Assert.Equal(0, await CountPartitionAsync(partitionKey));
    }
}
