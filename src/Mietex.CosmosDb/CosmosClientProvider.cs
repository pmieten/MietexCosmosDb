using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb;

/// <summary>Describes the client a context needs.</summary>
/// <param name="ClientKey">Cache key.</param>
/// <param name="ConnectionString">Cosmos connection string.</param>
/// <param name="ClientOptions">Options used to create the client.</param>
public sealed record CosmosClientDescriptor(
    string ClientKey,
    string ConnectionString,
    CosmosClientOptions ClientOptions);

/// <summary>Creates or returns Cosmos clients.</summary>
public interface ICosmosClientProvider
{
    /// <summary>Returns a client for the descriptor.</summary>
    /// <param name="descriptor">Client descriptor.</param>
    /// <returns>A Cosmos client.</returns>
    CosmosClient GetClient(CosmosClientDescriptor descriptor);
}

/// <summary>Thread-safe client cache keyed by <see cref="CosmosClientDescriptor.ClientKey"/>.</summary>
public sealed class CosmosClientProvider : ICosmosClientProvider, IDisposable
{
    /// <summary>Initializes a provider with an optional client factory.</summary>
    /// <param name="factory">Factory used to create clients. Defaults to <see cref="CosmosClient"/>.</param>
    public CosmosClientProvider(Func<CosmosClientDescriptor, CosmosClient>? factory = null) =>
        throw new NotImplementedException();

    /// <summary>Gets the process-wide shared provider.</summary>
    public static CosmosClientProvider Shared => throw new NotImplementedException();

    /// <summary>Resets and disposes the shared provider. Intended for test isolation.</summary>
    public static void ResetShared() => throw new NotImplementedException();

    /// <inheritdoc />
    public CosmosClient GetClient(CosmosClientDescriptor descriptor) => throw new NotImplementedException();

    /// <summary>Disposes and forgets all cached clients.</summary>
    public void Reset() => throw new NotImplementedException();

    /// <inheritdoc />
    public void Dispose() => throw new NotImplementedException();
}
