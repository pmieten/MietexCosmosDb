using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class CosmosClientProviderTests : IntegrationContextTestBase
{
    public CosmosClientProviderTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "client-provider";

    [Fact]
    public void SameClientKeyReturnsSameInstance()
    {
        using var provider = new CosmosClientProvider(CreateClient);

        var first = provider.GetClient(Descriptor("same-key"));
        var second = provider.GetClient(Descriptor("same-key"));

        Assert.Same(first, second);
    }

    [Fact]
    public void DifferentClientKeysReturnDifferentInstances()
    {
        using var provider = new CosmosClientProvider(CreateClient);

        var first = provider.GetClient(Descriptor("key-one"));
        var second = provider.GetClient(Descriptor("key-two"));

        Assert.NotSame(first, second);
    }

    [Fact]
    public void ResetCreatesANewClientInstance()
    {
        using var provider = new CosmosClientProvider(CreateClient);
        var descriptor = Descriptor("reset-key");

        var first = provider.GetClient(descriptor);
        provider.Reset();
        var second = provider.GetClient(descriptor);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void SharedProviderCanBeResetForTestIsolation()
    {
        CosmosClientProvider.ResetShared();
        var first = CosmosClientProvider.Shared;

        CosmosClientProvider.ResetShared();
        var second = CosmosClientProvider.Shared;

        Assert.NotSame(first, second);
        CosmosClientProvider.ResetShared();
    }

    [Fact]
    public async Task InjectedClientIsUsedAndNotDisposedByTheContext()
    {
        var partitionKey = NewPartition();
        using var client = CreateClient(Descriptor("injected"));
        var options = Options(
            partitionKey,
            current => current with
            {
                Client = client,
                ClientProvider = null,
                ConnectionString = null,
            });

        var entity = new TestEntity { Name = "injected-client" };

        await using (var context = new TestDbContext(options, TestUser()))
        {
            context.Add(entity);
            await context.SaveChangesAsync(Cancellation);
        }

        var databaseResponse = await client.GetDatabase(Emulator.DatabaseName)
            .ReadAsync(requestOptions: null, cancellationToken: Cancellation);
        Assert.NotNull(databaseResponse.Resource);

        var item = await client
            .GetDatabase(Emulator.DatabaseName)
            .GetContainer(ContainerName)
            .ReadItemAsync<TestEntity>(
                entity.Id,
                new PartitionKey(partitionKey),
                cancellationToken: Cancellation);

        Assert.Equal("injected-client", item.Resource.Name);
    }

    private CosmosClientDescriptor Descriptor(string clientKey) =>
        new(clientKey, Emulator.ConnectionString, ClientOptions());

    private static CosmosClientOptions ClientOptions() => new()
    {
        ApplicationName = "Mietex.CosmosDb.Tests.Provider",
        ConnectionMode = ConnectionMode.Gateway,
    };

    private static CosmosClient CreateClient(CosmosClientDescriptor descriptor)
    {
        var options = CosmosLocalEndpointSupport.ApplyIfLocalEndpoint(
            descriptor.ClientOptions,
            descriptor.ConnectionString);

        return new CosmosClient(descriptor.ConnectionString, options);
    }
}
