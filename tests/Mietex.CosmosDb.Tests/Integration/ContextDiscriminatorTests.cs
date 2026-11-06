namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextDiscriminatorTests : IntegrationContextTestBase
{
    public ContextDiscriminatorTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-discriminator";

    [Fact]
    public async Task CustomResolverIsAuthoritativeForWritesAndQueries()
    {
        var partitionKey = NewPartition();
        var options = Options(
            partitionKey,
            current => current with { DiscriminatorResolver = _ => "custom-discriminator" });
        var entity = new TestEntity { Name = "resolved" };

        await using (var context = new TestDbContext(options, TestUser()))
        {
            context.Add(entity);
            await context.SaveChangesAsync(Cancellation);

            Assert.Equal("custom-discriminator", entity.Discriminator);

            var results = await context.Entities.Query.ToListWithFeedSupportAsync(Cancellation);
            var result = Assert.Single(results);
            Assert.Equal(entity.Id, result.Id);
        }

        var raw = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal("custom-discriminator", raw.Resource.Discriminator);
    }
}
