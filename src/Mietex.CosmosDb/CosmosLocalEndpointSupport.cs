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
        throw new NotImplementedException();

    /// <summary>Returns whether the connection string points at a loopback endpoint.</summary>
    /// <param name="connectionString">Connection string to inspect.</param>
    /// <returns><see langword="true"/> for loopback endpoints.</returns>
    public static bool IsLocalEndpoint(string? connectionString) => throw new NotImplementedException();

    /// <summary>Applies gateway mode and TLS relaxation when the endpoint is loopback.</summary>
    /// <param name="options">Client options to mutate.</param>
    /// <param name="connectionString">Connection string to inspect.</param>
    /// <returns>The same options instance.</returns>
    public static CosmosClientOptions ApplyIfLocalEndpoint(
        CosmosClientOptions options,
        string? connectionString) => throw new NotImplementedException();

    /// <summary>Creates the database when it is missing on a local emulator.</summary>
    /// <param name="connectionString">Connection string to inspect.</param>
    /// <param name="databaseName">Database to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the database exists or the endpoint is not local.</returns>
    public static Task EnsureLocalDatabaseExistsAsync(
        string? connectionString,
        string? databaseName,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
