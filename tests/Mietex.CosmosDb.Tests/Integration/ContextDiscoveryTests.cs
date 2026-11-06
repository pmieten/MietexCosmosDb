namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextDiscoveryTests : IntegrationContextTestBase
{
    public ContextDiscoveryTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-discovery";

    [Fact]
    public void AutoPropertySetsAreDiscoveredDuringConstruction()
    {
        using var context = new AutoSetDbContext(Options(NewPartition()), TestUser());

        Assert.NotNull(context.Entities);
        var set = Assert.Single(context.DbSets);
        Assert.Equal(typeof(TestEntity), set.EntityType);
    }

    [Fact]
    public void ExpressionBodiedSetsAreRegisteredOnFirstAccess()
    {
        using var context = new TestDbContext(Options(NewPartition()), TestUser());

        Assert.Empty(context.DbSets);
        _ = context.Entities;

        var set = Assert.Single(context.DbSets);
        Assert.Equal(typeof(TestEntity), set.EntityType);
    }
}
