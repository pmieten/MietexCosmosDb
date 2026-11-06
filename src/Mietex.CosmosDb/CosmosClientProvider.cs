using System.Collections.Concurrent;
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
    private static readonly object SharedGate = new();
    private static CosmosClientProvider _shared = new();

    private readonly ConcurrentDictionary<string, CosmosClient> _clients =
        new(StringComparer.Ordinal);
    private readonly Func<CosmosClientDescriptor, CosmosClient> _factory;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Initializes a provider with an optional client factory.</summary>
    /// <param name="factory">Factory used to create clients. Defaults to <see cref="CosmosClient"/>.</param>
    public CosmosClientProvider(Func<CosmosClientDescriptor, CosmosClient>? factory = null)
    {
        _factory = factory ?? CreateDefaultClient;
    }

    /// <summary>Gets the process-wide shared provider.</summary>
    public static CosmosClientProvider Shared
    {
        get
        {
            lock (SharedGate)
            {
                return _shared;
            }
        }
    }

    /// <summary>Resets and disposes the shared provider. Intended for test isolation.</summary>
    public static void ResetShared()
    {
        CosmosClientProvider previous;
        lock (SharedGate)
        {
            previous = _shared;
            _shared = new CosmosClientProvider();
        }

        previous.Dispose();
    }

    /// <inheritdoc />
    public CosmosClient GetClient(CosmosClientDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _clients.GetOrAdd(
                descriptor.ClientKey,
                _ => _factory(descriptor));
        }
    }

    /// <summary>Disposes and forgets all cached clients.</summary>
    public void Reset()
    {
        List<CosmosClient> clients;
        lock (_gate)
        {
            clients = _clients.Values.ToList();
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<CosmosClient> clients;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            clients = _clients.Values.ToList();
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    private static CosmosClient CreateDefaultClient(CosmosClientDescriptor descriptor) =>
        new(descriptor.ConnectionString, descriptor.ClientOptions);
}
