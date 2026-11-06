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

    [Fact]
    public void ToStringRedactsTheConnectionString()
    {
        var options = ValidOptions();

        var text = options.ToString();

        Assert.DoesNotContain("AccountKey", text);
        Assert.DoesNotContain(CosmosLocalEndpointSupport.EmulatorAccountKey, text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void DescriptorToStringRedactsTheConnectionString()
    {
        var descriptor = new CosmosClientDescriptor(
            "key",
            ConnectionString,
            new CosmosClientOptions());

        var text = descriptor.ToString();

        Assert.DoesNotContain("AccountKey", text);
        Assert.DoesNotContain(CosmosLocalEndpointSupport.EmulatorAccountKey, text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void ContextBuildsClientOptionsFromTheEffectiveOptions()
    {
        using var provider = new CapturingProvider();
        static HttpClient Sentinel() => new();
        Func<HttpClient> factory = Sentinel;
        var options = ValidOptions() with
        {
            ClientProvider = provider,
            ClientKey = "unique-key",
            ApplicationName = "test-app",
            ConsistencyLevel = ConsistencyLevel.Eventual,
            ConfigureClientOptions = client => client.HttpClientFactory = factory,
        };

        using var context = new TestDbContext(options);

        Assert.NotNull(provider.Descriptor);
        Assert.Equal("unique-key", provider.Descriptor!.ClientKey);
        Assert.Equal("test-app", provider.Descriptor.ClientOptions.ApplicationName);
        Assert.Equal(ConsistencyLevel.Eventual, provider.Descriptor.ClientOptions.ConsistencyLevel);
        Assert.IsType<CosmosJsonDotNetSerializer>(provider.Descriptor.ClientOptions.Serializer);
        Assert.Same(factory, provider.Descriptor.ClientOptions.HttpClientFactory);
        Assert.Equal(ConnectionMode.Gateway, provider.Descriptor.ClientOptions.ConnectionMode);
    }

    [Fact]
    public void ConfigureSerializerSettingsDoesNotMutateCallerSettings()
    {
        using var provider = new CapturingProvider();
        var callerSettings = new JsonSerializerSettings();
        var options = ValidOptions() with
        {
            ClientProvider = provider,
            SerializerSettings = callerSettings,
            ConfigureSerializerSettings = settings =>
                settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter()),
        };

        using var context = new TestDbContext(options);

        Assert.Empty(callerSettings.Converters);
        var serializer = Assert.IsType<CosmosJsonDotNetSerializer>(
            provider.Descriptor!.ClientOptions.Serializer);
        Assert.NotEmpty(serializer.Settings.Converters);
    }

    [Fact]
    public void DefaultClientKeyIsAHashAndDoesNotContainTheSecret()
    {
        using var provider = new CapturingProvider();
        var options = ValidOptions() with { ClientProvider = provider };
        using var context = new TestDbContext(options);

        Assert.NotNull(provider.Descriptor);
        Assert.StartsWith("client-", provider.Descriptor!.ClientKey);
        Assert.DoesNotContain("AccountKey", provider.Descriptor.ClientKey);
        Assert.DoesNotContain(CosmosLocalEndpointSupport.EmulatorAccountKey, provider.Descriptor.ClientKey);
    }

    private static CosmosDbOptions ValidOptions() => new()
    {
        DatabaseName = "database",
        ContainerName = "container",
        PartitionKey = "partition",
        ConnectionString = ConnectionString,
    };

    private sealed class CapturingProvider : ICosmosClientProvider, IDisposable
    {
        private CosmosClient? _client;

        public CosmosClientDescriptor? Descriptor { get; private set; }

        public CosmosClient GetClient(CosmosClientDescriptor descriptor)
        {
            Descriptor = descriptor;
            _client = new CosmosClient(descriptor.ConnectionString, descriptor.ClientOptions);
            return _client;
        }

        public void Dispose() => _client?.Dispose();
    }

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
