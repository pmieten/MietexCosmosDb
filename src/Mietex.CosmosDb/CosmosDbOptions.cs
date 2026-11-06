using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Mietex.CosmosDb;

/// <summary>Controls how an existing document is written.</summary>
public enum CosmosDbUpdateMode
{
    /// <summary>Create the document when it is missing and overwrite it when it exists.</summary>
    Upsert,

    /// <summary>Replace the document and fail when it does not exist.</summary>
    Replace,
}

/// <summary>Controls whether writes are conditional on the in-memory ETag.</summary>
public enum CosmosDbConcurrencyMode
{
    /// <summary>Do not send an ETag precondition. The last writer wins.</summary>
    LastWriteWins,

    /// <summary>Send <c>If-Match</c> for replace, delete, and patch operations that have an ETag.</summary>
    MatchEtag,
}

/// <summary>Validated configuration for a <see cref="CosmosDbContextBase{TEntity,TUser}"/>.</summary>
public sealed record CosmosDbOptions
{
    /// <summary>Gets the Cosmos DB database name.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>Gets the Cosmos DB container name.</summary>
    public required string ContainerName { get; init; }

    /// <summary>Gets the partition key value this context operates on.</summary>
    public required string PartitionKey { get; init; }

    /// <summary>Gets the connection string. Required unless <see cref="Client"/> is supplied.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Gets an optional pre-built client. The library never disposes it.</summary>
    public CosmosClient? Client { get; init; }

    /// <summary>Gets an optional client provider. Mutually exclusive with <see cref="Client"/>.</summary>
    public ICosmosClientProvider? ClientProvider { get; init; }

    /// <summary>Gets the client cache key. Defaults to a value derived from the connection settings.</summary>
    public string? ClientKey { get; init; }

    /// <summary>Gets the Cosmos DB consistency level used when the library creates a client.</summary>
    public ConsistencyLevel ConsistencyLevel { get; init; } = ConsistencyLevel.Session;

    /// <summary>Gets a fully custom Cosmos serializer. Mutually exclusive with <see cref="SerializerSettings"/>.</summary>
    public CosmosSerializer? Serializer { get; init; }

    /// <summary>Gets Newtonsoft.Json settings used to create a serializer. Mutually exclusive with <see cref="Serializer"/>.</summary>
    public JsonSerializerSettings? SerializerSettings { get; init; }

    /// <summary>Gets a callback applied to the serializer settings after defaults are created.</summary>
    public Action<JsonSerializerSettings>? ConfigureSerializerSettings { get; init; }

    /// <summary>Gets a callback applied to the client options before the client is created.</summary>
    public Action<CosmosClientOptions>? ConfigureClientOptions { get; init; }

    /// <summary>Gets the clock used for audit timestamps and lock expiry.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Gets an optional logger factory used to create the context logger.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>Gets an optional context logger. Takes precedence over <see cref="LoggerFactory"/>.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>Gets how existing documents are written.</summary>
    public CosmosDbUpdateMode UpdateMode { get; init; } = CosmosDbUpdateMode.Upsert;

    /// <summary>Gets whether writes carry an ETag precondition.</summary>
    public CosmosDbConcurrencyMode ConcurrencyMode { get; init; } = CosmosDbConcurrencyMode.LastWriteWins;

    /// <summary>Gets the maximum number of operations in one transactional batch.</summary>
    public int BatchOperationLimit { get; init; } = 100;

    /// <summary>Gets whether index metrics are requested for queries.</summary>
    public bool PopulateIndexMetrics { get; init; }

    /// <summary>Gets whether point reads use the per-set cache.</summary>
    public bool EnableCache { get; init; } = true;

    /// <summary>Gets whether deleted-audit entities are soft-deleted by default.</summary>
    public bool EnableSoftDelete { get; init; } = true;

    /// <summary>Gets whether audited writes fail when the current user is null.</summary>
    public bool RequireUserForAuditedWrites { get; init; }

    /// <summary>Gets an optional discriminator resolver. Defaults to the runtime type name.</summary>
    public Func<Type, string>? DiscriminatorResolver { get; init; }

    /// <summary>Gets the application name reported to Cosmos DB.</summary>
    public string ApplicationName { get; init; } = "Mietex.CosmosDb";

    /// <summary>Gets how long a lock acquisition waits before timing out.</summary>
    public TimeSpan LockAcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets how long a partition lease is valid.</summary>
    public TimeSpan LockLeaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets how long lock cleanup may take.</summary>
    public TimeSpan LockCleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Validates the option combination.</summary>
    /// <exception cref="ArgumentException">A required value is empty or a value is out of range.</exception>
    /// <exception cref="InvalidOperationException">Two mutually exclusive options are set.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ContainerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(PartitionKey);

        if (Client is not null && ClientProvider is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(Client)} and {nameof(ClientProvider)} cannot be set together.");
        }

        if (Client is null && string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException(
                $"{nameof(ConnectionString)} is required unless {nameof(Client)} is supplied.",
                nameof(ConnectionString));
        }

        if (Serializer is not null && SerializerSettings is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(Serializer)} and {nameof(SerializerSettings)} cannot be set together.");
        }

        if (UpdateMode == CosmosDbUpdateMode.Upsert &&
            ConcurrencyMode == CosmosDbConcurrencyMode.MatchEtag)
        {
            throw new InvalidOperationException(
                "Upsert cannot be combined with MatchEtag. Use Replace when an ETag precondition is required.");
        }

        if (BatchOperationLimit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchOperationLimit),
                BatchOperationLimit,
                "BatchOperationLimit must be between 1 and 100.");
        }

        if (TimeProvider is null)
        {
            throw new ArgumentNullException(nameof(TimeProvider));
        }

        if (!Enum.IsDefined(ConsistencyLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConsistencyLevel),
                ConsistencyLevel,
                "The consistency level is not defined.");
        }

        ValidatePositive(LockAcquireTimeout, nameof(LockAcquireTimeout));
        ValidatePositive(LockLeaseDuration, nameof(LockLeaseDuration));
        ValidatePositive(LockCleanupTimeout, nameof(LockCleanupTimeout));
    }

    private static void ValidatePositive(TimeSpan value, string propertyName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                propertyName,
                value,
                $"{propertyName} must be greater than zero.");
        }
    }
}
