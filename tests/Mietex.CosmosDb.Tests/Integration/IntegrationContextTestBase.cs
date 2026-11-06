using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Integration;

/// <summary>
/// Shared setup for context/set integration tests. Each test class owns one real emulator container
/// and each test uses a fresh partition key.
/// </summary>
public abstract class IntegrationContextTestBase : IntegrationTestBase, IAsyncLifetime
{
    protected IntegrationContextTestBase(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected abstract string ContainerName { get; }

    protected Container Container { get; private set; } = null!;

    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // The collection fixture owns the skip decision. Returning early here keeps a missing
        // emulator from failing this class before [RequireEmulator] can skip the test.
        if (!CosmosEmulatorFixture.IsAvailable)
        {
            return;
        }

        Container = await Emulator.CreateContainerAsync(ContainerName);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected CosmosDbOptions Options(
        string partitionKey,
        Func<CosmosDbOptions, CosmosDbOptions>? configure = null) =>
        Emulator.CreateOptions(ContainerName, partitionKey, configure);

    protected static async Task<TestEntity> CreateSeedAsync(
        CosmosDbOptions options,
        TestEntity? entity = null,
        TestUser? user = null)
    {
        entity ??= new TestEntity();
        await using var context = new TestDbContext(options, user ?? TestUser());
        context.Add(entity);
        await context.SaveChangesAsync(Cancellation);
        return entity;
    }

    protected async Task<int> CountPartitionAsync(string partitionKey)
    {
        using var iterator = Container.GetItemQueryIterator<TestEntity>(
            new QueryDefinition("SELECT * FROM c"),
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
            });

        var count = 0;
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(Cancellation);
            count += page.Count;
        }

        return count;
    }

    protected Task<ItemResponse<TestEntity>> ReadRawAsync(string id, string partitionKey) =>
        Container.ReadItemAsync<TestEntity>(
            id,
            new PartitionKey(partitionKey),
            cancellationToken: Cancellation);
}
