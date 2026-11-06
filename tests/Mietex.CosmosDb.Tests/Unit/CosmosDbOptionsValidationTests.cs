using Microsoft.Azure.Cosmos;
using Mietex.CosmosDb.Tests.Infrastructure;
using Newtonsoft.Json;

namespace Mietex.CosmosDb.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CosmosDbOptionsValidationTests
{
    private const string ConnectionString =
        "AccountEndpoint=https://localhost:8281/;AccountKey=" + CosmosLocalEndpointSupport.EmulatorAccountKey;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyDatabaseNameIsRejected(string databaseName)
    {
        var options = ValidOptions() with { DatabaseName = databaseName };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyContainerNameIsRejected(string containerName)
    {
        var options = ValidOptions() with { ContainerName = containerName };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPartitionKeyIsRejected(string partitionKey)
    {
        var options = ValidOptions() with { PartitionKey = partitionKey };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void MissingConnectionStringWithoutClientIsRejected()
    {
        var options = ValidOptions() with { ConnectionString = null };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void ClientAndClientProviderTogetherAreRejected()
    {
        using var client = new CosmosClient(ConnectionString);
        var provider = new StubClientProvider();
        var options = ValidOptions() with
        {
            ConnectionString = null,
            Client = client,
            ClientProvider = provider,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void SerializerAndSerializerSettingsTogetherAreRejected()
    {
        var options = ValidOptions() with
        {
            Serializer = new StubCosmosSerializer(),
            SerializerSettings = new JsonSerializerSettings(),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void BatchOperationLimitOutsideOneToHundredIsRejected(int batchOperationLimit)
    {
        var options = ValidOptions() with { BatchOperationLimit = batchOperationLimit };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void UpsertWithMatchEtagIsRejected()
    {
        var options = ValidOptions() with
        {
            UpdateMode = CosmosDbUpdateMode.Upsert,
            ConcurrencyMode = CosmosDbConcurrencyMode.MatchEtag,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(nameof(CosmosDbOptions.LockAcquireTimeout), 0)]
    [InlineData(nameof(CosmosDbOptions.LockAcquireTimeout), -1)]
    [InlineData(nameof(CosmosDbOptions.LockLeaseDuration), 0)]
    [InlineData(nameof(CosmosDbOptions.LockLeaseDuration), -1)]
    [InlineData(nameof(CosmosDbOptions.LockCleanupTimeout), 0)]
    [InlineData(nameof(CosmosDbOptions.LockCleanupTimeout), -1)]
    public void NonPositiveLockDurationsAreRejected(string propertyName, int seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        var options = propertyName switch
        {
            nameof(CosmosDbOptions.LockAcquireTimeout) => ValidOptions() with { LockAcquireTimeout = duration },
            nameof(CosmosDbOptions.LockLeaseDuration) => ValidOptions() with { LockLeaseDuration = duration },
            nameof(CosmosDbOptions.LockCleanupTimeout) => ValidOptions() with { LockCleanupTimeout = duration },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "Unknown lock duration."),
        };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void MinimalValidOptionsPassValidation()
    {
        var options = ValidOptions();

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void ContextConstructorCallsOptionsValidation()
    {
        var options = ValidOptions() with { DatabaseName = "   " };

        Assert.Throws<ArgumentException>(() => new TestDbContext(options));
    }

    [Fact]
    public void ReplaceWithMatchEtagIsValid()
    {
        var options = ValidOptions() with
        {
            UpdateMode = CosmosDbUpdateMode.Replace,
            ConcurrencyMode = CosmosDbConcurrencyMode.MatchEtag,
        };

        options.Validate();
    }

    [Fact]
    public void ClientWithoutConnectionStringIsValid()
    {
        using var client = new CosmosClient(ConnectionString);
        var options = ValidOptions() with { ConnectionString = null, Client = client };

        options.Validate();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void BatchOperationLimitBoundariesAreValid(int batchOperationLimit)
    {
        var options = ValidOptions() with { BatchOperationLimit = batchOperationLimit };

        options.Validate();
    }

    private static CosmosDbOptions ValidOptions() => new()
    {
        DatabaseName = "database",
        ContainerName = "container",
        PartitionKey = "partition",
        ConnectionString = ConnectionString,
    };

    private sealed class StubCosmosSerializer : CosmosSerializer
    {
        public override T FromStream<T>(Stream stream) => throw new NotSupportedException();

        public override Stream ToStream<T>(T input) => throw new NotSupportedException();
    }

    private sealed class StubClientProvider : ICosmosClientProvider
    {
        public CosmosClient GetClient(CosmosClientDescriptor descriptor) => throw new NotSupportedException();
    }
}
