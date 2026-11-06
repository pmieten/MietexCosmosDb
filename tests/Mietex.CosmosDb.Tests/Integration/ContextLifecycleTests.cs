namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextLifecycleTests : IntegrationContextTestBase
{
    public ContextLifecycleTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-lifecycle";

    [Fact]
    public async Task ClearDiscardsPendingChanges()
    {
        var partitionKey = NewPartition();
        await using var context = new HookTestDbContext(Options(partitionKey), TestUser());
        context.Add(new TestEntity { Name = "discarded" });

        context.Clear();
        await context.SaveChangesAsync(Cancellation);

        Assert.Empty(context.ValidatedCounts);
        Assert.Equal(0, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task ClearAsyncDiscardsPendingChanges()
    {
        var partitionKey = NewPartition();
        await using var context = new HookTestDbContext(Options(partitionKey), TestUser());
        context.Add(new TestEntity { Name = "discarded" });

        await context.ClearAsync(Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.Empty(context.ValidatedCounts);
        Assert.Equal(0, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task RepeatedDisposalIsSafe()
    {
        var context = new TestDbContext(Options(NewPartition()), TestUser());

        await context.DisposeAsync();
        await context.DisposeAsync();
        var exception = Record.Exception(context.Dispose);

        Assert.Null(exception);
    }
}
