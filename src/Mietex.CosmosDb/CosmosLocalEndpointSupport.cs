using System.Globalization;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb;

/// <summary>
/// Support for a local Cosmos DB emulator. TLS relaxation is applied only when the connection string
/// points at a loopback address, so a real Azure endpoint is never affected.
/// </summary>
public static class CosmosLocalEndpointSupport
{
    /// <summary>Well-known account key required by SDKs when talking to the vNext emulator.</summary>
    public const string EmulatorAccountKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    /// <summary>Builds a connection string for the local vNext emulator.</summary>
    /// <param name="port">Gateway port.</param>
    /// <returns>Emulator connection string.</returns>
    public static string BuildEmulatorConnectionString(int port = 8281) =>
        $"AccountEndpoint=https://localhost:{port.ToString(CultureInfo.InvariantCulture)}/;AccountKey={EmulatorAccountKey};";

    /// <summary>Returns whether the connection string points at a loopback endpoint.</summary>
    /// <param name="connectionString">Connection string to inspect.</param>
    /// <returns><see langword="true"/> for loopback endpoints.</returns>
    public static bool IsLocalEndpoint(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var endpoints = FindAccountEndpoints(connectionString);
        return endpoints.Count > 0 && endpoints.All(IsLoopbackEndpoint);
    }

    /// <summary>Applies gateway mode and TLS relaxation when the endpoint is loopback.</summary>
    /// <param name="options">Client options to mutate.</param>
    /// <param name="connectionString">Connection string to inspect.</param>
    /// <returns>The same options instance.</returns>
    public static CosmosClientOptions ApplyIfLocalEndpoint(
        CosmosClientOptions options,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsLocalEndpoint(connectionString))
        {
            return options;
        }

        options.ConnectionMode = ConnectionMode.Gateway;
        options.HttpClientFactory ??= () => new HttpClient(
            new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });

        return options;
    }

    /// <summary>Creates the database when it is missing on a local emulator.</summary>
    /// <param name="connectionString">Connection string to inspect.</param>
    /// <param name="databaseName">Database to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the database exists or the endpoint is not local.</returns>
    public static async Task EnsureLocalDatabaseExistsAsync(
        string? connectionString,
        string? databaseName,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalEndpoint(connectionString) || string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "Mietex.CosmosDb",
        };
        ApplyIfLocalEndpoint(clientOptions, connectionString);

        using var client = new CosmosClient(connectionString, clientOptions);
        await client
            .CreateDatabaseIfNotExistsAsync(databaseName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<string> FindAccountEndpoints(string connectionString)
    {
        var endpoints = new List<string>();
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = part[..separator].Trim();
            if (name.Equals("AccountEndpoint", StringComparison.OrdinalIgnoreCase))
            {
                endpoints.Add(part[(separator + 1)..].Trim());
            }
        }

        return endpoints;
    }

    private static bool IsLoopbackEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        // Uri.IsLoopback is case-sensitive for "LOCALHOST" on some runtime versions.
        return uri.IsLoopback ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("127.0.0.1", StringComparison.Ordinal) ||
            uri.Host.Equals("::1", StringComparison.Ordinal) ||
            uri.Host.Equals("[::1]", StringComparison.Ordinal);
    }
}
