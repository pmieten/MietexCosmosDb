using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Converters;
using Xunit;

namespace Mietex.CosmosDb.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class CosmosEmulatorCollectionDefinition : ICollectionFixture<CosmosEmulatorFixture>
{
    public const string Name = "Cosmos emulator";
}

public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    public const string ClientKey = "mietex-tests";

    private CosmosClient? _client;
    private Database? _database;

    public static bool IsAvailable { get; private set; }

    public static string? SkipReason { get; private set; }

    public string ConnectionString { get; } = ResolveConnectionString();

    public string DatabaseName { get; } = ResolveDatabaseName();

    public CosmosClientProvider Provider { get; } = new();

    public ValueTask InitializeAsync() => InitializeCoreAsync();

    private async ValueTask InitializeCoreAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(180);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var clientOptions = new CosmosClientOptions
                {
                    ApplicationName = "Mietex.CosmosDb.Tests",
                    ConnectionMode = ConnectionMode.Gateway,
                    Serializer = new CosmosJsonDotNetSerializer(
                        CosmosSerializerSettingsFactory.Create(converters: [new StringEnumConverter()])),
                };
                CosmosLocalEndpointSupport.ApplyIfLocalEndpoint(clientOptions, ConnectionString);

                _client = Provider.GetClient(
                    new CosmosClientDescriptor(ClientKey, ConnectionString, clientOptions));
                var response = await _client.CreateDatabaseIfNotExistsAsync(
                    DatabaseName,
                    cancellationToken: cancellationToken);
                _database = response.Database;
                IsAvailable = true;
                SkipReason = null;
                return;
            }
            catch (Exception ex) when (ex is CosmosException or HttpRequestException or TaskCanceledException or ArgumentException or FormatException or UriFormatException)
            {
                lastError = ex;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        IsAvailable = false;
        SkipReason =
            "Cosmos DB emulator is not reachable at the configured endpoint. " +
            "Start the vNext container or set MIETEX_COSMOSDB_EMULATOR_CONNECTION_STRING. " +
            $"Last error type: {lastError?.GetType().Name ?? "none"}.";
    }

    public async ValueTask<Container> CreateContainerAsync(string name)
    {
        if (!IsAvailable || _database is null)
        {
            throw new InvalidOperationException(
                "The Cosmos DB emulator is not available. Integration tests should be skipped by [RequireEmulator].");
        }

        var response = await _database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(name, "/PartitionKey"),
            cancellationToken: TestContext.Current.CancellationToken);
        return response.Container;
    }

    public CosmosDbOptions CreateOptions(
        string containerName,
        string partitionKey,
        Func<CosmosDbOptions, CosmosDbOptions>? configure = null)
    {
        var options = new CosmosDbOptions
        {
            ConnectionString = ConnectionString,
            DatabaseName = DatabaseName,
            ContainerName = containerName,
            PartitionKey = partitionKey,
            ClientKey = ClientKey,
            ClientProvider = Provider,
            SerializerSettings = CosmosSerializerSettingsFactory.Create(
                converters: [new StringEnumConverter()]),
            ConsistencyLevel = ConsistencyLevel.Session,
        };

        return configure?.Invoke(options) ?? options;
    }

    public async ValueTask DisposeAsync()
    {
        if (_database is not null && IsAvailable)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await _database.DeleteAsync(cancellationToken: timeout.Token);
            }
            catch (Exception ex)
            {
                try
                {
                    TestContext.Current.SendDiagnosticMessage(
                        "Could not delete test database '{0}'. Left for manual cleanup. {1}: {2}",
                        DatabaseName,
                        ex.GetType().Name,
                        ex.Message);
                }
                catch (InvalidOperationException)
                {
                    // No active test context; the database name is deterministic enough for cleanup.
                }
            }
        }

        Provider.Dispose();
        _client = null;
        _database = null;
    }

    private static string ResolveDatabaseName()
    {
        var runId = Environment.GetEnvironmentVariable("MIETEX_TEST_RUN_ID");
        if (!string.IsNullOrWhiteSpace(runId))
        {
            return $"mietex-tests-{runId}";
        }

        return $"mietex-tests-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static string ResolveConnectionString()
    {
        var explicitConnection =
            Environment.GetEnvironmentVariable("MIETEX_COSMOSDB_EMULATOR_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConnection))
        {
            return explicitConnection;
        }

        var portText = Environment.GetEnvironmentVariable("MIETEX_COSMOSDB_EMULATOR_PORT");
        var port = int.TryParse(portText, out var parsedPort) ? parsedPort : 8281;
        return CosmosLocalEndpointSupport.BuildEmulatorConnectionString(port);
    }
}

public abstract class IntegrationTestBase
{
    protected IntegrationTestBase(CosmosEmulatorFixture emulator)
    {
        Emulator = emulator;
    }

    protected CosmosEmulatorFixture Emulator { get; }

    protected static string NewPartition() => Guid.NewGuid().ToString("N");

    protected static TestUser TestUser(string name = "tester") => new(name);
}
