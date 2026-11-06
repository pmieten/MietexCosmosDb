using System.Globalization;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CosmosLocalEndpointSupportTests
{
    private const string LocalConnectionString =
        "AccountEndpoint=https://127.0.0.1:8281/;AccountKey=key;";

    private const string RemoteConnectionString =
        "AccountEndpoint=https://example.documents.azure.com:443/;AccountKey=key;";

    [Theory]
    [InlineData(8281)]
    [InlineData(9090)]
    public void BuildEmulatorConnectionStringContainsEndpointAndKeyForPort(int port)
    {
        var connectionString = CosmosLocalEndpointSupport.BuildEmulatorConnectionString(port);

        Assert.Contains("AccountEndpoint=", connectionString);
        Assert.Contains("https://localhost:", connectionString);
        Assert.Contains("localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/", connectionString);
        Assert.Contains("AccountKey=", connectionString);
        Assert.Contains(CosmosLocalEndpointSupport.EmulatorAccountKey, connectionString);
    }

    [Theory]
    [InlineData("AccountEndpoint=https://localhost:8281/;AccountKey=key;", true)]
    [InlineData("AccountEndpoint=https://127.0.0.1:8281/;AccountKey=key;", true)]
    [InlineData("AccountEndpoint=https://[::1]:8281/;AccountKey=key;", true)]
    [InlineData("accountendpoint=https://LOCALHOST:8281/;accountkey=key;", true)]
    [InlineData(" AccountEndpoint = https://localhost:8281/ ; AccountKey = key ; ", true)]
    [InlineData("AccountEndpoint=https://localhost:8281/", true)]
    [InlineData("AccountEndpoint=https://example.documents.azure.com:443/;AccountKey=key;", false)]
    [InlineData("AccountEndpoint=/relative/path;AccountKey=key;", false)]
    [InlineData("AccountEndpoint=https://[not-an-address];AccountKey=key;", false)]
    [InlineData("AccountKey=key;", false)]
    [InlineData("this is not a connection string", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsLocalEndpointDetectsLoopbackEndpoints(string? connectionString, bool expected)
    {
        Assert.Equal(expected, CosmosLocalEndpointSupport.IsLocalEndpoint(connectionString));
    }

    [Fact]
    public void ApplyIfLocalEndpointConfiguresGatewayAndHttpClientFactory()
    {
        var options = new CosmosClientOptions();

        var result = CosmosLocalEndpointSupport.ApplyIfLocalEndpoint(options, LocalConnectionString);

        Assert.Same(options, result);
        Assert.Equal(ConnectionMode.Gateway, options.ConnectionMode);
        Assert.NotNull(options.HttpClientFactory);
    }

    [Fact]
    public void ApplyIfLocalEndpointPreservesExistingHttpClientFactory()
    {
        static HttpClient Factory() => new();
        Func<HttpClient> factory = Factory;
        var options = new CosmosClientOptions { HttpClientFactory = factory };

        var result = CosmosLocalEndpointSupport.ApplyIfLocalEndpoint(options, LocalConnectionString);

        Assert.Same(options, result);
        Assert.Same(factory, options.HttpClientFactory);
    }

    [Fact]
    public void ApplyIfLocalEndpointLeavesRemoteEndpointOptionsUntouched()
    {
        var options = new CosmosClientOptions();
        var originalMode = options.ConnectionMode;
        var originalFactory = options.HttpClientFactory;

        var result = CosmosLocalEndpointSupport.ApplyIfLocalEndpoint(options, RemoteConnectionString);

        Assert.Same(options, result);
        Assert.Equal(originalMode, options.ConnectionMode);
        Assert.Same(originalFactory, options.HttpClientFactory);
    }

    [Fact]
    public async Task EnsureLocalDatabaseExistsIsANoOpForRemoteEndpoint()
    {
        await CosmosLocalEndpointSupport.EnsureLocalDatabaseExistsAsync(
            RemoteConnectionString,
            "should-not-be-created",
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnsureLocalDatabaseExistsIsANoOpForMissingDatabaseName(string? databaseName)
    {
        await CosmosLocalEndpointSupport.EnsureLocalDatabaseExistsAsync(
            LocalConnectionString,
            databaseName,
            TestContext.Current.CancellationToken);
    }
}
