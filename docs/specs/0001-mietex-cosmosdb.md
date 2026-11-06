# Mietex.CosmosDb — Shared Cosmos DB Library

## Problem Statement

Cosmos DB persistence code tends to grow organically: a context base class, set helpers, a JSON
serializer, query helpers, emulator workarounds, audit stamping, soft delete, and locking. When that
code is copied between services, the copies drift. The same operation can write with `UpsertItem` in
one service and `ReplaceItem` in another; one service may soft-delete, another may hard-delete; ETag
write-back, batch errors, cancellation, and emulator support may differ. The result is duplicate
maintenance, divergent semantics, and test suites that assert SDK interactions instead of behavior.

This spec defines a single MIT-licensed library, `Mietex.CosmosDb`, that provides the shared
behavior, makes every divergence an explicit option, and is validated against a real Cosmos DB
emulator rather than SDK mocks.

## Solution

Ship a small .NET library, package ID and root namespace `Mietex.CosmosDb`, targeting `net8.0` and
`net10.0`. It provides:

- A minimal `ICosmosEntity` contract so the library never owns host entity base classes.
- Generic audit contracts parameterized over the host's user-id type.
- A generic `CosmosDbContextBase<TEntity, TUser>` with an options object that captures connection,
  database, container, partition key, time, logging, client injection, consistency, serializer,
  update/concurrency mode, soft-delete mode, batch limit, index metrics, and cache behavior.
- `CosmosDbSet<T>` with partition-scoped LINQ, discriminator filtering, point reads, and an
  optional cache.
- A Newtonsoft.Json `CosmosSerializer` plus a default settings factory.
- Local-emulator support (connection-string builder, loopback detection, TLS relaxation, database
  bootstrap).
- Feed-iterator extensions with no external async-LINQ dependency.
- `CosmosPartitionLock` with configurable lease/acquisition/cleanup timings and `TimeProvider`, and
  `CosmosLockingDbContextBase<TEntity, TUser>` that wires locks into the batch lifecycle.
- A typed `CosmosBatchException` carrying per-operation failures, diagnostics, activity id, and
  request charge.
- A cached, injectable, resettable `CosmosClientProvider`.

## User Stories

1. As a host application developer, I want a single entity contract with `Id`, `PartitionKey`, `Discriminator`, `Etag`, and `IsNew`, so that the library can operate on my existing entity base classes without owning them.
2. As a host application developer, I want to declare that my existing `Entity` class implements the contract with no changes to its storage shape, so that migration does not rewrite documents.
3. As a host application developer, I want generic created-audit, updated-audit, and deleted-audit contracts, so that different host user types fit the same library.
4. As a host application developer, I want a combined audit contract, so that entities that are both created- and updated-audited need only one interface.
5. As a host application developer, I want audit stamping to preserve the current rules, including the existing `skipUserId` behavior, so that migration does not silently change audit data.
6. As a host application developer, I want to pass `skipUserId: true` on internal writes, so that system-generated changes are not attributed to a user.
7. As a host application developer, I want an option that rejects audited writes when no user context is set, so that the current `AppDbContext` guard is expressed as configuration rather than a subclass override.
8. As a host application developer, I want to set or replace the current user on an existing context, so that long-lived scoped contexts can switch identity.
9. As a host application developer, I want the context to expose its effective options, so that I can create a copy context for a different partition key.
10. As a host application developer, I want partition and container scoping fixed at context construction, so that no query can accidentally escape the partition.
11. As a host application developer, I want my `CosmosDbSet<T>` properties initialized automatically, so that existing context classes keep their declarative shape.
12. As a host application developer, I want to obtain a set explicitly by type when I do not want a property, so that small contexts do not need boilerplate.
13. As a host application developer, I want LINQ queries to be partition-scoped and discriminator-filtered by default, so that a set returns only its own entity type.
14. As a host application developer, I want to override how the discriminator is derived, so that a host with a custom naming convention is not forced to change stored data.
15. As a host application developer, I want a point-read `FindAsync` by string or numeric id, so that the common lookup stays a single RU operation.
16. As a host application developer, I want `FindAsync(null)` to return null without allocation or task tricks, so that callers can pass optional ids safely.
17. As a host application developer, I want point reads to populate the ETag even if my entity forgot the `_etag` attribute, so that subsequent updates are correct.
18. As a host application developer, I want an optional read cache that can be disabled per context, so that hot tenant reads can be cheap without forcing stale reads everywhere.
19. As a host application developer, I want the cache to avoid negative entries and to evict hard-deleted entities, so that a deleted document cannot be resurrected from memory.
20. As a host application developer, I want to clear all set caches from the context, so that lock acquisition or an external change can force fresh reads.
21. As a host application developer, I want adding a new entity to mark the partition key automatically, so that documents are always stored in the context's partition.
22. As a host application developer, I want choosing between upsert and replace for existing entities, so that both source behaviors remain available.
23. As a host application developer, I want upsert to remain the default, so that current write semantics are preserved.
24. As a host application developer, I want replace to be selectable per context, so that a missing document is a failure instead of a silent create.
25. As a host application developer, I want last-write-wins to remain the default, so that migration does not introduce new concurrency failures.
26. As a host application developer, I want optional ETag-conditional updates, deletes, and patches, so that I can opt into optimistic concurrency where it matters.
27. As a host application developer, I want one transactional batch per save, so that all pending changes in a partition commit or fail together.
28. As a host application developer, I want the batch operation limit enforced before any network call, so that a 101-operation save fails fast and predictably.
29. As a host application developer, I want soft delete implemented as a Cosmos patch for deleted-audit entities, so that the document and its audit fields remain queryable.
30. As a host application developer, I want soft delete to remain the default for deleted-audit entities, so that current deletion behavior is preserved.
31. As a host application developer, I want a per-context switch to keep hard delete, so that API-key deletion semantics do not change during migration.
32. As a host application developer, I want hard delete available explicitly, so that GDPR/retention jobs can remove documents.
33. As a host application developer, I want arbitrary patch operations to flow through the same batch, so that partial updates stay atomic with other changes.
34. As a host application developer, I want ETags written back after a successful batch, so that the in-memory objects are immediately reusable for a conditional update.
35. As a host application developer, I want a typed batch exception with per-operation status codes and messages, so that failures can be logged, classified, and tested without parsing strings.
36. As a host application developer, I want the batch exception to carry diagnostics, activity id, and request charge, so that support can correlate a failure with Cosmos telemetry.
37. As a host application developer, I want hooks that run before the batch is built, after it is prepared, after it commits, and after the save attempt finishes, so that cross-cutting behavior does not require rewriting the save pipeline.
38. As a host application developer, I want a disposal guard hook, so that using a disposed context fails clearly instead of issuing requests.
39. As a host application developer, I want post-commit hook failures not to turn a committed save into an apparent failure, so that callers do not retry and duplicate writes.
40. As a host application developer, I want every async operation to accept a cancellation token, so that request cancellation propagates to Cosmos.
41. As a host application developer, I want `ClearAsync` to discard pending changes and release resources, so that a failed unit of work can be abandoned safely.
42. As a host application developer, I want synchronous `Clear` and synchronous disposal for legacy call sites, so that migration does not require rewriting every caller at once.
43. As a host application developer, I want contexts to be disposable without owning injected clients, so that DI scope disposal does not tear down a shared client.
44. As a host application developer, I want Session consistency by default, so that the lock algorithm's read-after-write requirements hold.
45. As a host application developer, I want a Newtonsoft.Json serializer that plugs into the Cosmos SDK, so that existing documents and converters continue to work.
46. As a host application developer, I want a default serializer-settings factory, so that a new consumer gets correct behavior without copying settings from a host.
47. As a host application developer, I want to add or replace converters and serializer settings, so that host-specific polymorphism, enums, dates, and user ids serialize exactly as before.
48. As a host application developer, I want to inject a fully custom `CosmosSerializer`, so that a consumer can use System.Text.Json or a legacy serializer without forking the library.
49. As a host application developer, I want loopback endpoints detected from the connection string, so that emulator-only TLS relaxation can never affect a real Azure account.
50. As a host application developer, I want a ready-made emulator connection string, so that local development and tests do not duplicate the well-known key.
51. As a host application developer, I want emulator gateway mode and certificate relaxation applied automatically, so that the vNext Linux emulator works without per-app hacks.
52. As a host application developer, I want a helper that creates the emulator database only when the endpoint is loopback, so that production provisioning remains infrastructure-owned.
53. As a host application developer, I want feed-iterator extensions that turn query results into lists and async streams, so that I do not need `System.Linq.Async` or an async-LINQ package.
54. As a host application developer, I want in-memory `IQueryable` providers to keep working, so that unit tests and client-side projections behave as they do today.
55. As a host application developer, I want a cached client provider keyed by an explicit client key, so that multiple databases and hosts share clients deliberately rather than accidentally.
56. As a test author, I want to reset the shared client provider, so that each test run starts with a fresh Cosmos client and no stale name cache.
57. As a test author, I want to inject a client or a custom provider, so that integration tests can control client construction without mocking SDK types.
58. As a host application developer, I want a partition lease that can be acquired, waited on, cancelled, and released, so that read-modify-write workflows can be serialized per resource.
59. As a host application developer, I want the lease to expire and be taken over conditionally, so that a crashed owner cannot block a partition forever.
60. As a host application developer, I want a stale owner's business batch to fail when its lease was taken over, so that stale writes cannot commit.
61. As a host application developer, I want the lease release to be part of the business transaction, so that the lock and the protected writes commit atomically.
62. As a host application developer, I want abandoning a lock handle to discard pending changes, so that a failed workflow cannot accidentally save half-read state.
63. As a host application developer, I want lock ids to be deterministic and Cosmos-safe, so that different resource keys never collide and the same key always addresses the same lease.
64. As a host application developer, I want configurable lease duration, acquisition timeout, cleanup timeout, and retry bounds, so that tests run in seconds and production keeps safe defaults.
65. As a host application developer, I want lock timings driven by `TimeProvider`, so that expiry logic is testable and hosts can use a shared clock abstraction.
66. As a host application developer, I want locks counted against the 100-operation batch limit, so that a save cannot silently exceed Cosmos limits.
67. As a host application developer, I want a locking context base that wires locks into save, clear, abandonment, and disposal, so that I do not reimplement the lifecycle.
68. As a host application developer, I want the same resource key in different partitions to produce independent locks, so that tenant isolation is preserved.
69. As a host application developer, I want a documented, mechanical migration path to the shared context and serializer, so that I can remove the duplicated base class without changing entity storage.
70. As a host application developer, I want a documented migration path that preserves replace, hard delete, and lock behavior, so that existing API keys and counters keep working.
71. As a maintainer, I want all Cosmos behavior tested against a real emulator, so that the tests exercise serialization, batching, ETags, and locks as the SDK actually implements them.
72. As a maintainer, I want SDK `Container`, `TransactionalBatch`, and response mocks to be forbidden, so that the suite cannot pass while the real integration is broken.
73. As a test author, I want one unique database per test run and one container per test class, so that parallel test classes do not interfere and cleanup is a single delete.
74. As a test author, I want pure functions such as serializer settings, endpoint detection, query formatting, and option validation to have ordinary unit tests, so that fast feedback does not require Docker.
75. As a CI engineer, I want the emulator to run as a service container with a documented connection string, so that integration tests run on Linux without a Windows emulator.
76. As a CI engineer, I want integration tests to skip with a clear message when the emulator is not configured, so that a plain `dotnet test` does not fail on a developer machine without Docker.
77. As a release engineer, I want deterministic builds with SourceLink and embedded untracked sources, so that symbols can be debugged and builds are reproducible.
78. As a package consumer, I want XML documentation, nullable annotations, and warnings-as-errors in the build, so that the public API is self-describing and does not decay.
79. As a package consumer, I want central package management in the repository, so that dependency versions are consistent across library and tests.
80. As a package consumer, I want both `net8.0` and `net10.0` assets, so that I can adopt the library without retargeting.
81. As a release engineer, I want tag-based versioning with MinVer, so that the git tag is the single source of truth for the package version.
82. As a release engineer, I want NuGet Trusted Publishing through GitHub OIDC, so that no long-lived NuGet API key is stored in the repository.
83. As a documentation reader, I want API documentation generated with DocFX and published to GitHub Pages, so that the public surface is browsable without reading source.
84. As a security reviewer, I want TLS relaxation restricted to loopback endpoints and never enabled for Azure endpoints, so that the emulator workaround cannot weaken production traffic.
85. As a security reviewer, I want documented guidance on Newtonsoft `TypeNameHandling`, so that the compatibility default is not mistaken for a safe parser for untrusted input.
86. As a security reviewer, I want connection strings and document bodies excluded from logs and exception messages, so that secrets and personal data do not leak into telemetry.
87. As a new contributor, I want a single library project with a flat namespace and explicit contracts, so that the codebase is approachable and the public surface is easy to review.
88. As a new contributor, I want the migration documents to map old type and method names to new ones, so that I can update a host without reverse-engineering the library.

## Implementation Decisions

### Scope and repository shape

- One library project, `Mietex.CosmosDb`, producing a single NuGet package. No satellite packages for
  locking, serializer, or emulator support; they are small and share the same contracts.
- One test project containing unit, integration, and packaging tests, separated by xUnit traits.
- Build infrastructure: `Directory.Build.props`, `Directory.Packages.props`, `global.json` (pin the
  .NET SDK feature band), `LICENSE` (MIT), `README.md`, and `docs/`.
- Documentation deliverables: `README.md`, this spec, DocFX articles, and generic
  storage-compatibility guidance. No host source is changed.
- Root namespace `Mietex.CosmosDb`, flat. The only public namespace is `Mietex.CosmosDb`; no
  `Locking` or `Serialization` sub-namespaces.
- The library accepts and returns real Cosmos SDK types (`Container`, `TransactionalBatch`,
  `PatchOperation`, `QueryDefinition`, `FeedIterator<T>`, `CosmosClient`). It deliberately does not
  introduce an `IContainer`/`ITransactionalBatch` abstraction. This keeps the API honest and makes
  the emulator the test seam.

### Public API surface (decided type shapes)

The following block encodes the API decision. It is intentionally a type-shape sketch, not an
implementation.

```csharp
namespace Mietex.CosmosDb;

public interface ICosmosEntity
{
    string Id { get; set; }
    string PartitionKey { get; set; }
    string Discriminator { get; set; }
    string? Etag { get; set; }
    bool IsNew();
}

public interface ICreatedAuditEntity<TUser> where TUser : class
{
    DateTimeOffset CreatedDate { get; set; }
    TUser? CreatedById { get; set; }
}

public interface IUpdatedAuditEntity<TUser> where TUser : class
{
    DateTimeOffset UpdatedDate { get; set; }
    TUser? UpdatedById { get; set; }
}

public interface IDeletedAuditEntity<TUser> where TUser : class
{
    DateTimeOffset? DeletedDate { get; set; }
    TUser? DeletedById { get; set; }
}

public interface IAuditEntity<TUser>
    : ICreatedAuditEntity<TUser>, IUpdatedAuditEntity<TUser>
    where TUser : class { }

public interface ICosmosDbSet
{
    Type EntityType { get; }
    void ClearCache();
}

public sealed class CosmosDbSet<T> : ICosmosDbSet
    where T : class, ICosmosEntity
{
    public IQueryable<T> Query { get; }
    public Task<T?> FindAsync(string? id, CancellationToken cancellationToken = default);
    public Task<T?> FindAsync(long? id, CancellationToken cancellationToken = default);
    public void ClearCache();
}

public enum CosmosDbUpdateMode { Upsert, Replace }
public enum CosmosDbConcurrencyMode { LastWriteWins, MatchEtag }

public sealed record CosmosDbOptions
{
    public required string DatabaseName { get; init; }
    public required string ContainerName { get; init; }
    public required string PartitionKey { get; init; }

    public string? ConnectionString { get; init; }
    public CosmosClient? Client { get; init; }
    public ICosmosClientProvider? ClientProvider { get; init; }
    public string? ClientKey { get; init; }

    public ConsistencyLevel ConsistencyLevel { get; init; } = ConsistencyLevel.Session;
    public CosmosSerializer? Serializer { get; init; }
    public Newtonsoft.Json.JsonSerializerSettings? SerializerSettings { get; init; }
    public Action<Newtonsoft.Json.JsonSerializerSettings>? ConfigureSerializerSettings { get; init; }
    public Action<CosmosClientOptions>? ConfigureClientOptions { get; init; }

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public Microsoft.Extensions.Logging.ILoggerFactory? LoggerFactory { get; init; }
    public Microsoft.Extensions.Logging.ILogger? Logger { get; init; }

    public CosmosDbUpdateMode UpdateMode { get; init; } = CosmosDbUpdateMode.Upsert;
    public CosmosDbConcurrencyMode ConcurrencyMode { get; init; } = CosmosDbConcurrencyMode.LastWriteWins;
    public int BatchOperationLimit { get; init; } = 100;
    public bool PopulateIndexMetrics { get; init; }
    public bool EnableCache { get; init; } = true;
    public bool EnableSoftDelete { get; init; } = true;
    public bool RequireUserForAuditedWrites { get; init; }
    public Func<Type, string>? DiscriminatorResolver { get; init; }
    public string ApplicationName { get; init; } = "Mietex.CosmosDb";

    public TimeSpan LockAcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan LockLeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan LockCleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

public abstract class CosmosDbContextBase<TEntity, TUser>
    : IAsyncDisposable, IDisposable
    where TEntity : class, ICosmosEntity
    where TUser : class
{
    protected CosmosDbContextBase(CosmosDbOptions options, TUser? currentUser = null);

    public CosmosDbOptions Options { get; }
    public TUser? CurrentUser { get; protected set; }
    public string PartitionKey { get; }
    public Container Container { get; }
    public IReadOnlyList<ICosmosDbSet> DbSets { get; }
    protected Microsoft.Extensions.Logging.ILogger Logger { get; }

    public void SetUserContext(TUser? user);

    public void Add<T>(T entity, bool skipUserId = false) where T : TEntity;
    public void Delete(TEntity entity, bool useSoftDeleteIfPossible = true);
    public void Patch(TEntity entity, IReadOnlyList<PatchOperation> patchOperations);
    public void Patch(string entityId, IReadOnlyList<PatchOperation> patchOperations);

    public virtual void Clear();
    public virtual Task ClearAsync(CancellationToken cancellationToken = default);
    public Task SaveChangesAsync(CancellationToken cancellationToken = default);
    public void ClearCache();

    public FeedIterator<T> GetItemQueryIterator<T>(QueryDefinition queryDefinition);
    public IOrderedQueryable<T> GetItemLinqQueryable<T>();

    protected CosmosDbSet<T> Set<T>() where T : class, TEntity, ICosmosEntity;

    protected virtual void EnsureUsable();
    protected virtual void ValidateBatchOperationCount(int pendingOperationCount);
    protected virtual void PrepareBatch(TransactionalBatch batch);
    protected virtual void OnBatchCommitted();
    protected virtual Task OnSaveFinishedAsync(bool succeeded);

    public void Dispose();
    public virtual ValueTask DisposeAsync();
}

public abstract class CosmosLockingDbContextBase<TEntity, TUser>
    : CosmosDbContextBase<TEntity, TUser>
    where TEntity : class, ICosmosEntity
    where TUser : class
{
    protected CosmosLockingDbContextBase(CosmosDbOptions options, TUser? currentUser = null);

    public Task<CosmosPartitionLock> PerformLockUntilSaveChangesAsync(
        string resourceKey, CancellationToken cancellationToken = default);
    public CosmosPartitionLock PerformLockUntilSaveChanges(string resourceKey);

    public override void Clear();
    public override Task ClearAsync(CancellationToken cancellationToken = default);
    public override ValueTask DisposeAsync();
}

public sealed record CosmosPartitionLockOptions
{
    public TimeSpan AcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RetryDelayMin { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan RetryDelayMax { get; init; } = TimeSpan.FromMilliseconds(150);
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

public sealed class CosmosPartitionLock : IDisposable, IAsyncDisposable
{
    public static Task<CosmosPartitionLock> AcquireAsync(
        Container container,
        string partitionKey,
        string resourceKey,
        CosmosPartitionLockOptions? options = null,
        Microsoft.Extensions.Logging.ILogger? logger = null,
        CancellationToken cancellationToken = default);

    public string LockId { get; }
    public string ResourceKey { get; }
    public DateTimeOffset ExpiresAt { get; }

    public void AddReleaseToBatch(TransactionalBatch batch);
    public void Dispose();
    public ValueTask DisposeAsync();
}

public sealed class CosmosLockLease
{
    public string Id { get; set; }
    public string PartitionKey { get; set; }
    public string Discriminator { get; set; }
    public string Owner { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class CosmosJsonDotNetSerializer : CosmosLinqSerializer
{
    public CosmosJsonDotNetSerializer(Newtonsoft.Json.JsonSerializerSettings settings);
    public Newtonsoft.Json.JsonSerializerSettings Settings { get; }
    public override T FromStream<T>(Stream stream);
    public override Stream ToStream<T>(T input);
    public override string SerializeMemberName(System.Reflection.MemberInfo memberInfo);
}

public static class CosmosSerializerSettingsFactory
{
    public static Newtonsoft.Json.JsonSerializerSettings Create(
        Action<Newtonsoft.Json.JsonSerializerSettings>? configure = null,
        IEnumerable<Newtonsoft.Json.JsonConverter>? converters = null);
}

public static class CosmosLocalEndpointSupport
{
    public const string EmulatorAccountKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    public static string BuildEmulatorConnectionString(int port = 8281);
    public static bool IsLocalEndpoint(string? connectionString);
    public static CosmosClientOptions ApplyIfLocalEndpoint(
        CosmosClientOptions options, string? connectionString);
    public static Task EnsureLocalDatabaseExistsAsync(
        string? connectionString, string? databaseName, CancellationToken cancellationToken = default);
}

public static class CosmosFeedExtensions
{
    public static IAsyncEnumerable<T> ToAsyncEnumerableFromFeedIterator<T>(
        this FeedIterator<T> iterator,
        CancellationToken cancellationToken = default);

    public static Task<List<T>> ToListWithFeedSupportAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default);

    public static Task<List<T>> ToListWithFeedSupportAsync<T>(
        this IQueryable<T> query,
        Func<IAsyncEnumerable<T>, IAsyncEnumerable<T>> transform,
        CancellationToken cancellationToken = default);
}

public static class CosmosQueryLogFormatter
{
    public static string Format(QueryDefinition queryDefinition);
}

public sealed class CosmosBatchException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public int OperationCount { get; }
    public IReadOnlyList<CosmosBatchOperationError> Failures { get; }
    public string? Diagnostics { get; }
    public string? ActivityId { get; }
    public double RequestCharge { get; }
}

public sealed record CosmosBatchOperationError(
    int Index,
    System.Net.HttpStatusCode StatusCode,
    string? Message);

public sealed record CosmosClientDescriptor(
    string ClientKey,
    string ConnectionString,
    CosmosClientOptions ClientOptions);

public interface ICosmosClientProvider
{
    CosmosClient GetClient(CosmosClientDescriptor descriptor);
}

public sealed class CosmosClientProvider : ICosmosClientProvider, IDisposable
{
    public CosmosClientProvider(Func<CosmosClientDescriptor, CosmosClient>? factory = null);
    public static CosmosClientProvider Shared { get; }
    public static void ResetShared();
    public CosmosClient GetClient(CosmosClientDescriptor descriptor);
    public void Reset();
    public void Dispose();
}
```

API rules that follow from the shapes above:

- `ICosmosEntity.IsNew` is a method, not a property, because both host `Entity` classes already
  expose `bool IsNew()`. Declaring the interface must not require changing entity bodies.
- `TUser` is constrained to `class` because `TUser?` must mean a nullable reference type; both host
  `TUser` types are classes.
- `CosmosDbSet<T>` is constructed by the context only. Its constructor is not public, so cache and
  discriminator settings cannot be bypassed.
- `CosmosDbSet<T>.Query` returns `IQueryable<T>` backed by
  `GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true, ...)`, matching source behavior.
- `GetItemQueryIterator<T>` and `GetItemLinqQueryable<T>` remain public escape hatches for
  projections and raw queries. They are partition-scoped but do not apply the discriminator filter.
- `Container` is a get-only property. Tests must not replace it; this is deliberate (see Testing
  Decisions).

### Context construction and options

- A context validates its options in the constructor. `DatabaseName`, `ContainerName`, and
  `PartitionKey` are required non-empty; `ConnectionString` is required unless `Client` is supplied;
  `BatchOperationLimit` must be in `1..100`; `UpdateMode.Replace` with
  `ConcurrencyMode.MatchEtag` is valid; `UpdateMode.Upsert` with `MatchEtag` is rejected because
  Cosmos upsert cannot honour an ETag precondition reliably.
- `Serializer` and `SerializerSettings` are mutually exclusive. Resolution order is: explicit
  `Serializer`; else explicit `SerializerSettings` (then `ConfigureSerializerSettings`); else
  factory defaults plus `ConfigureSerializerSettings`.
- `Client` wins over `ConnectionString` and bypasses the provider. `Client` and `ClientProvider`
  together are rejected. The library never disposes a client supplied through `Client`.
- Client construction order mirrors the source: build `CosmosClientOptions`, set serializer,
  consistency, and application name; invoke `ConfigureClientOptions`; then apply loopback TLS
  relaxation. The host hook therefore runs first and may replace the `HttpClientFactory`; local
  relaxation only fills it when still unset.
- Default `ClientKey` is `"client-" + uppercase hex SHA-256(connection string, application name,
  consistency level, database name)`, not a concatenation of the raw connection string. This keeps
  account keys out of logs and `ToString()` output. Two contexts that intentionally share a client
  use the same key; contexts with different custom serializers set an explicit `ClientKey`.
- `CosmosDbOptions.ToString()` and `CosmosClientDescriptor.ToString()` redact the connection string.
  `ToString()` is a diagnostic surface, not a secret store; never log `ConnectionString` directly.
- Logger resolution: explicit `Logger`, else `LoggerFactory.CreateLogger(context type)`, else
  `NullLogger.Instance`.
- Time: `Options.TimeProvider.GetUtcNow()` replaces the host `ICalendar.Now`. Hosts that need tenant
  local time provide a `TimeProvider` adapter in their own code; the library never references
  `ICalendar`.
- Set discovery: in the base constructor, public instance properties whose type is exactly
  `CosmosDbSet<>` are instantiated and assigned. C# runs derived property initializers (including
  `= null!`) before the base constructor body, so the assignment survives. `DbSets` exposes the
  discovered sets read-only. `Set<T>()` returns an already-discovered set or creates and registers
  one for hosts that prefer explicit access. Supported property shapes are
  `public CosmosDbSet<T> Set { get; set; } = null!;` (discovered eagerly) and
  `public CosmosDbSet<T> Set => Set<T>();` (lazy; appears in `DbSets` after first access). A
  get-only auto-property is not discovered. Do not write `= Set<T>()` in an initializer: derived
  initializers run before base field initialization and will fail.
- The context is not thread-safe for concurrent mutation. `CosmosDbSet` reads are thread-safe; the
  cache is a `ConcurrentDictionary`.
- `Dispose`/`DisposeAsync` are idempotent. Disposal discards pending changes and caches. The base
  class does not dispose clients. The locking subclass releases leases.

### Set query, point read, and cache semantics

- `Query` applies one filter: `t.Discriminator == discriminator`, where `discriminator` is a
  captured string from `Options.DiscriminatorResolver ?? (t => t.Name)` invoked with `typeof(T)`.
  Capturing the value (rather than embedding `typeof(T).Name` in the expression tree) keeps the
  predicate translatable and parameterized.
- `FindAsync(null)` returns `null` immediately.
- `FindAsync` performs `ReadItemAsync<T>(id, new PartitionKey(partitionKey))`; it returns `null` on
  `404`, rethrows all other `CosmosException`s, and sets `Etag` from the response ETag even if the
  entity's `_etag` mapping is missing.
- The cache stores only positive results. It is enabled by `EnableCache` (default `true`). Cache
  keys are the string form of the id (`long` formatted invariantly). `FindAsync` consults the cache
  when enabled; `ClearCache` empties it. Hard delete evicts the id. Soft delete keeps the entry,
  applies `DeletedDate`/`DeletedById` to the cached object after a successful commit, and refreshes
  its ETag. Arbitrary patches and `Patch(string id, ...)` evict the id because their effects cannot
  be replayed reliably in memory.
- The cache is best-effort and is not a consistency mechanism. Lock acquisition clears it.
  A cached value can outlive another context's external write; call `ClearCache` after external
  changes.

### Save pipeline

`SaveChangesAsync` executes in this order:

1. `EnsureUsable()`.
2. If the pending queue is empty, skip batch creation and `PrepareBatch`; `succeeded` remains `true`
   and the `finally` below still runs, so the locking subclass can release leases.
3. `ValidateBatchOperationCount(pending.Count)`. The default implementation rejects counts above
   `BatchOperationLimit` with `InvalidOperationException`. The locking subclass adds one operation
   per held lease before calling the same check.
4. Create one `TransactionalBatch` for `Options.PartitionKey` in insertion order. For each pending
   operation:
   - Hard delete: `DeleteItem(id, options)`.
   - Patch: `PatchItem(id, patchOperations, options)`.
   - New entity (`IsNew()`): set `PartitionKey`, `CreateItem(entity, options)`.
   - Existing entity: `UpsertItem` or `ReplaceItem` according to `UpdateMode`.
   - All item options set `EnableContentResponseOnWrite = true`. With
     `ConcurrencyMode.MatchEtag`, non-empty `Etag` values become `IfMatchEtag` for replace, delete,
     and patch.
5. `PrepareBatch(batch)`, which the locking subclass uses to append ETag-conditional lease deletes.
6. `ExecuteAsync(cancellationToken)`.
7. On success, write ETags back from `TransactionalBatchResponse[index].ETag` for the queued
   entity operations, then call `OnBatchCommitted()`. Only the first N indexes map to queued
   entities; lease-release operations appended in `PrepareBatch` are ignored for write-back. The
   non-generic batch indexer is used; there is no `dynamic` or `GetOperationResultAtIndex<T>`
   reflection.
8. Clear the pending queue.
9. In `finally`, call `OnSaveFinishedAsync(succeeded)`.

Failure semantics:

- A failed batch throws `CosmosBatchException`; pending operations remain queued in the base class so
  the caller can inspect or retry.
- Cancellation propagates as `OperationCanceledException` and is not wrapped.
- Validation, `EnsureUsable`, and `PrepareBatch` failures occur before any write and propagate.
- Exceptions thrown by `OnBatchCommitted` and `OnSaveFinishedAsync` are logged and suppressed so a
  committed save is never reported as failed. `OnSaveFinishedAsync(true)` still runs on the no-op
  path.
- The locking subclass discards pending changes on failure because the lease is no longer reliable.

### Audit stamping

- `Add` stamps in the same order and with the same conditions as legacy implementations:
  - New entity + `ICreatedAuditEntity<TUser>`: always set `CreatedDate = now`; set `CreatedById`
    only when `skipUserId` is `false`.
  - New entity + `IUpdatedAuditEntity<TUser>`: set both `UpdatedDate` and `UpdatedById` only when
    `skipUserId` is `false`.
  - Existing entity + `IUpdatedAuditEntity<TUser>`: set both `UpdatedDate` and `UpdatedById` only
    when `skipUserId` is `false`.
- The asymmetric treatment of `CreatedDate` versus `UpdatedDate` under `skipUserId` is intentional
  compatibility, not an oversight. It is covered by tests.
- `RequireUserForAuditedWrites` (default `false`): when `true`, `Add` throws
  `InvalidOperationException` if `CurrentUser` is `null` and the entity implements
  `ICreatedAuditEntity<TUser>` or `IUpdatedAuditEntity<TUser>`, unless `skipUserId` is `true`.
  This replaces the legacy host `AppDbContext` override; `IDeletedAuditEntity<TUser>` alone does not
  trigger the guard, matching the source.
- Audit timestamps come from `Options.TimeProvider`.

### Delete, patch, and soft delete

- `Delete(entity, useSoftDeleteIfPossible: true)`:
  - If `Options.EnableSoftDelete` and the entity implements `IDeletedAuditEntity<TUser>` and the
    flag is `true`, queue one patch operation containing
    `PatchOperation.Set("/DeletedDate", now)` and
    `PatchOperation.Set("/DeletedById", CurrentUser)`. `Set` is used instead of `Replace` so the
    operation succeeds whether or not the JSON property already exists.
  - Otherwise queue a hard delete.
- legacy host keeps soft delete by default. legacy host sets `EnableSoftDelete = false` for contexts where
  hard delete is the current contract (notably API keys), preserving behavior.
- `Patch` queues one `PatchItem`. The string overload allows patching without a tracked entity.
- All three operations call `EnsureUsable` and count as one batch operation each.

### Batch exception model

- `CosmosBatchException` is thrown for a non-success batch response. It contains the batch status
  code, operation count, per-operation failures (index, status, message), diagnostics, activity id,
  and request charge.
- Error messages are read from the operation response stream when available, then the outer batch
  `ErrorMessage`, then the status code. Reads are bounded to 4 KiB per operation to avoid leaking
  document bodies and to avoid unbounded allocation.
- `404` on `Replace` is a normal batch failure and is reported as such; it is not converted to a
  silent create.
- Batch status `424 Failed Dependency` is a common outer status; per-operation codes carry the real
  cause. `424` entries are not counted in the root-failure summary.
- Request-level `CosmosException` failures from `ExecuteAsync` (for example 408/429/5xx or transport
  errors) are wrapped in `CosmosBatchException` with status, diagnostics, activity id, request
  charge, and the original exception as `InnerException`. Caller cancellation remains
  `OperationCanceledException`.

### Locking

`CosmosPartitionLock` preserves the proven source algorithm with these refinements:

- Lease id is `"distributed-lock-" + uppercase hex SHA-256(resourceKey)`; resource keys are
  therefore deterministic and Cosmos-safe.
- Owner is a fresh GUID (`N` format) per acquisition.
- Acquisition first attempts an unconditional create with a deterministic id. On `409`, it reads
  the existing lease and replaces it only when `ExpiresAt <= now`, using `IfMatchEtag`. On `404` or
  `412` it retries. Retry delay is random between `RetryDelayMin` and `RetryDelayMax`.
- Takeover verifies that the existing document really is a lease before replacing it: matching id,
  `Discriminator == "DistributedLock"`, non-empty `Owner`, and non-default `ExpiresAt`. A business
  document whose id happens to equal the deterministic lock id is never overwritten; acquisition
  throws `InvalidOperationException` instead.
- Acquisition is bounded by a linked `CancellationTokenSource` with `AcquireTimeout`, then throws
  `TimeoutException` (not `OperationCanceledException`) when the caller's own token was not
  cancelled. The timeout bound and retry delays are real-time; lease expiry comparisons use
  `Options.TimeProvider.GetUtcNow()`.
- `AddReleaseToBatch` is single-use and throws `InvalidOperationException` if the handle was
  released/committed/already appended or is locally expired at batch preparation. Otherwise it adds
  `DeleteItem(lockId, IfMatchEtag)`.
- Two fencing failure modes must be handled by hosts. If the local handle is already expired, the
  save fails fast with `InvalidOperationException` and no request is sent. If the handle is locally
  valid but another owner has taken over the lease, the ETag-conditional delete fails the whole
  batch with `CosmosBatchException` and a `412`; the protected writes roll back.
- `DisposeAsync` is idempotent, does not inherit a cancelled request token, uses
  `CleanupTimeout`, and ignores `404`/`412`. Other cleanup failures are logged and swallowed;
  expiry is the recovery path.
- The `OnAbandoned` callback is internal. The locking context uses it to discard pending changes,
  clear caches, and remove the handle from its set.
- The lease document is public as `CosmosLockLease` (replacing the nested `LeaseDocument`) so tests
  and diagnostics can read leases through a real container. Its `Id` keeps
  `[JsonProperty("id")]`; `Discriminator` defaults to `"DistributedLock"`. Without the explicit
  `id` mapping Cosmos would reject the lease document.

`CosmosLockingDbContextBase` behavior:

- `PerformLockUntilSaveChangesAsync` rejects duplicate resource keys in the same context, acquires,
  stores the handle, wires `OnAbandoned`, and clears caches so subsequent reads see the protected
  state. It builds `CosmosPartitionLockOptions` from `Options.LockAcquireTimeout`,
  `Options.LockLeaseDuration`, `Options.LockCleanupTimeout`, and `Options.TimeProvider`. The
  synchronous wrapper blocks on the async call.
- `ValidateBatchOperationCount` adds held-lease count to pending count and rejects above the limit.
- `PrepareBatch` appends one release per held lease.
- `OnBatchCommitted` marks each lease committed, so disposal does not issue a second delete.
- `OnSaveFinishedAsync`: on failure, discard pending changes and caches; always release leases.
- `ClearAsync` releases leases. `Clear` releases leases synchronously and discards changes.
- `DisposeAsync` is idempotent, releases leases, marks the context disposed.
- Locks are not renewed. Work that outlives `LeaseDuration` must discard and retry from a fresh
  read. This is documented on the public API.

### Serializer

- `CosmosJsonDotNetSerializer` is a straight port of the source serializer: `CosmosLinqSerializer`,
  UTF-8 without BOM, no formatting, a fresh `JsonSerializer` per call (avoids the known
  settings/race issue), `FromStream` disposes the input stream and passes `Stream` through
  unchanged, and `SerializeMemberName` honours `JsonProperty`, `DataMember`, and
  `JsonExtensionData`.
- `CosmosSerializerSettingsFactory.Create` defaults to: `TypeNameHandling.Auto`,
  `ReferenceLoopHandling.Error`, `PreserveReferencesHandling.None`,
  `ConstructorHandling.AllowNonPublicDefaultConstructor`, and a `StringEnumConverter`. It accepts
  extra converters and a configuration callback. It intentionally includes no host-specific
  converters; legacy host adds its hierarchy, date, and user-id converters; legacy host adds its date
  converter.
- `TypeNameHandling.Auto` is retained because existing documents may contain `$type` for polymorphic
  entities. This is a compatibility requirement, not a recommendation for parsing untrusted JSON.
- Hosts must keep `[JsonProperty("id")]` on `Id` and `[JsonProperty("_etag")]` on `Etag`. The
  library cannot add attributes to host entities. Point reads compensate for a missing `_etag`
  mapping; query results cannot.

### Client provider

- `CosmosClientProvider` caches one client per `ClientKey`, is thread-safe, and disposes clients it
  created on `Reset`/`Dispose`.
- `Shared` is the default provider for contexts that do not inject one; `ResetShared()` exists for
  test isolation and replaces the source's `DbClient = null`.
- The provider accepts a factory delegate for tests. An injected `CosmosClient` bypasses the
  provider entirely.
- `Reset` is safe to call between tests after all contexts are disposed; disposing a client with
  in-flight requests is not supported.
- The default key derivation and collision rules are documented on `ClientKey`.

### Local endpoint and feed extensions

- `CosmosLocalEndpointSupport` keeps the source's public shape and adds a cancellation token to
  database bootstrap. `IsLocalEndpoint` is a pure string/URI function. `ApplyIfLocalEndpoint` is a
  pure options mutation: gateway mode plus an accepting certificate handler only when no factory is
  already set. `EnsureLocalDatabaseExistsAsync` no-ops for non-loopback endpoints and for null or
  whitespace database names, and disposes its bootstrap client.
- `CosmosFeedExtensions` keeps the source method names. `ToListWithFeedSupportAsync` detects
  `EnumerableQuery` and falls back to `ToList()`; otherwise it calls `ToFeedIterator()` and
  accumulates into a `List<T>` without `System.Linq.Async`. The async-enumerable method passes the
  cancellation token to `ReadNextAsync` and uses `[EnumeratorCancellation]`.
- The library does not publish a `ToListAsync` extension for `IAsyncEnumerable<T>`, to avoid
  ambiguity with `System.Linq.Async` in hosts that already reference it.

### Configurable behavior and storage compatibility

Each divergence is an explicit per-context option, never a heuristic or a compile-time symbol:

1. `UpdateMode` — upsert versus replace.
2. `EnableSoftDelete` — soft versus hard delete.
3. `ConcurrencyMode` — last-write-wins versus ETag-conditional.
4. `EnableCache` — cached versus read-through point reads.
5. `PopulateIndexMetrics` — development diagnostics versus production quiet.
6. `RequireUserForAuditedWrites` — guard versus permissive.
7. Context base class — `CosmosDbContextBase` versus `CosmosLockingDbContextBase`.
8. `ClientKey`/`ClientProvider` — shared client versus isolated/injected client.

The C# interface property is named `PartitionKey`, but the JSON member name and the container
partition key path do not have to change. Keep the existing JSON attribute and container path when
migrating. Renaming only the property without the attribute changes storage shape and breaks point
reads and writes.

Likewise, keep `[JsonProperty("_etag")]` on `Etag`. Point reads compensate for a missing attribute,
but query results do not, and a missing attribute can persist a separate `"Etag"` member.

### Packaging, versioning, and documentation

- `PackageId` = `Mietex.CosmosDb`; `PackageLicenseExpression` = `MIT`; include `README.md` and
  `LICENSE`; `RepositoryUrl` = `https://github.com/pmieten/MietexCosmosDb`; `PackageTags` include
  `cosmosdb`, `azure`, `persistence`, `transactional-batch`, `distributed-lock`.
- Multi-target `net8.0;net10.0`. Central package versions: Cosmos SDK 3.63.x (both hosts already use
  3.57+), Newtonsoft.Json 13.0.4, `Microsoft.Extensions.Logging.Abstractions` for `net8.0` and
  `net10.0` matching the TFM, xUnit v3, `Microsoft.Extensions.TimeProvider.Testing`, MinVer,
  SourceLink, DocFX.
- Versioning: MinVer with tag prefix `v`. Tags are the only version source; local builds produce a
  prerelease. Release job runs only for `v*` tags.
- Publishing: NuGet Trusted Publishing with GitHub OIDC. The publish job requests
  `permissions: id-token: write`, uses `NuGet/login@v1` with the NuGet.org account name, consumes
  the emitted `NUGET_API_KEY`, and pushes with `--skip-duplicate`. No long-lived NuGet key is
  stored. The trusted-publishing policy is bound to this repository and the release workflow file.
- Docs: DocFX builds API metadata from the library project and the Markdown guides, treats warnings
  as errors, and publishes to GitHub Pages. The site is built from `main` and from release tags.
- No workflow files are produced by this spec; the decisions above are the contract for the
  follow-up implementation.

### Non-functional requirements

- `Nullable` enabled; `ImplicitUsings` enabled; `LangVersion` latest for each TFM.
- `TreatWarningsAsErrors` enabled for the library and tests. `AnalysisLevel` = `latest-recommended`.
- `GenerateDocumentationFile` enabled and `CS1591` not suppressed: every public type and member has
  XML documentation.
- `Deterministic` build enabled; `ContinuousIntegrationBuild` enabled in CI; `EmbedUntrackedSources`
  enabled.
- SourceLink to GitHub (`Microsoft.SourceLink.GitHub`), `PublishRepositoryUrl`,
  `IncludeSymbols`/`snupkg`.
- Central package management: all `PackageReference` entries are versionless; versions live in
  `Directory.Packages.props`. Do not pin SDK-style implicit versions in project files.
- No `Console.WriteLine` in the library. No `#if DEBUG` behavior. No static mutable state except the
  provider's resettable `Shared` instance.
- All public async methods accept and forward `CancellationToken`. Library awaits use
  `ConfigureAwait(false)`.
- Logging: trace-level operation summaries (index, type, id, partition, status) and expanded query
  text. Never log connection strings, account keys, full document bodies, or serialized audit users.
- Performance: one client per key; partition-scoped queries; point reads for id lookups; one batch
  per save; ETag extraction from the non-generic batch response; set discovery once per context.
- Security: loopback-only TLS relaxation; `TypeNameHandling` risk documented; no secrets in
  exceptions; lock is a coordination primitive, not an authorization boundary.

## Testing Decisions

### What makes a good test here

Tests assert observable behavior through public APIs: the document that ends up in Cosmos, the
exception a caller catches, the ETag on the object, the lease that exists or does not exist, the
bytes the serializer produces. Tests do not assert SDK call counts, private fields, or the order of
internal method invocations.

The library deliberately exposes real SDK types. Therefore the primary seam is the public context
and lock API against a real Cosmos DB emulator. Pure functions are tested with ordinary unit tests.

### Forbidden test doubles

The following are explicitly forbidden, in all test projects:

- Mocking or faking `Container`, `TransactionalBatch`, `TransactionalBatchResponse`,
  `ItemResponse<T>`, `FeedIterator<T>`, `CosmosClient`, or `CosmosException`.
- Substituting a fake `CosmosClient`/`Container` through `CosmosDbOptions.Client` to avoid the
  emulator for integration-scenario tests.
- `InternalsVisibleTo` hacks whose purpose is to make an SDK interaction testable.

Allowed test doubles: `TimeProvider` (including `FakeTimeProvider`), `ILogger`/`ILoggerFactory`
capture, test entity types, and a `CosmosClientProvider` factory delegate that constructs a real
client. If a scenario seems to require an SDK mock, the design is wrong: extract the decision into a
pure function or test it against the emulator.

The two existing mock-based suites (`legacy lock tests`, `legacy lock tests` in legacy host) are
the prior art for the scenarios, not for the technique. Every scenario they cover is ported to the
emulator, plus the cross-process contention and atomic rollback cases they could not cover.

### Seams and test ownership

| Seam | Test kind | Emulator | What it proves |
| --- | --- | --- | --- |
| `CosmosJsonDotNetSerializer` | Unit | No | Stream round-trip, member-name mapping, polymorphism, stream ownership, enum formatting |
| `CosmosSerializerSettingsFactory` | Unit | No | Defaults, converter composition, callback order |
| `CosmosLocalEndpointSupport` | Unit | No | Loopback parsing, connection-string builder, TLS/gateway mutation, non-local no-ops |
| `CosmosDbOptions` validation | Unit | No | Required values, mutual exclusion, limits, mode combinations |
| `CosmosQueryLogFormatter` | Unit | No | Parameter expansion and sanitization for logs only |
| `CosmosDbContextBase` / `CosmosDbSet` | Integration | Yes | CRUD, batching, ETags, audit, soft delete, queries, cache, hooks, errors |
| `CosmosPartitionLock` | Integration | Yes | Acquisition, takeover, fencing, release, abandonment, cleanup |
| `CosmosLockingDbContextBase` | Integration | Yes | Lock lifecycle wired into save/clear/dispose |
| `CosmosClientProvider` | Integration | Yes | Keyed reuse, reset, injection, ownership |
| `CosmosFeedExtensions` | Integration | Yes | Paged results, cancellation, `EnumerableQuery` fallback |
| Packaging | Packaging | No | `dotnet pack` output, TFMs, license, README, XML docs, symbols |
| CI/release contract | CI | No | Build matrix, emulator service, tag versioning, publish gate |

### Emulator harness

- Environment variable: `MIETEX_COSMOSDB_EMULATOR_CONNECTION_STRING`. If unset, fall back to
  `MIETEX_COSMOSDB_EMULATOR_PORT` (default `8281`) and
  `CosmosLocalEndpointSupport.BuildEmulatorConnectionString(port)`. If neither is usable, integration
  tests skip with an explicit message (xUnit v3 `Assert.Skip`); unit/packaging tests still run.
- Image: `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest`, started with
  `--protocol https --port 8281`; gateway port `8281`, health port `8280` mapped to container `8080`
  (`/ready`, `/alive`, `/status`), Data Explorer `1234` optional.
- Readiness: the fixture retries `CreateDatabaseIfNotExistsAsync` for up to 180 seconds (health probe
  plus gateway), then fails with the container log hint. CI may also wait on `http://localhost:8280/ready`.
- Isolation: one database per test run, named
  `mietex-tests-{yyyyMMddHHmmss}-{8 hex}` (or `MIETEX_TEST_RUN_ID` when supplied). One container per
  test class, created with `CreateContainerIfNotExistsAsync` and partition key path `/PartitionKey`.
  Each test uses a fresh partition key value (GUID) so tests within a class do not share documents;
  lock tests deliberately reuse a known key within their own partition. Tests that need a different
  partition key path create their own container.
- Cleanup: delete the run database in fixture teardown (best effort, 30 s timeout). On failure, log
  the database name and leave it for manual inspection. Do not delete/recreate the shared container
  between tests; that triggers stale name caches in the SDK and is the behavior the source worked
  around with `DbClient = null`.
- Client lifecycle: use the library's provider; call `CosmosClientProvider.ResetShared()` only after
  all contexts are disposed. Prefer a fixture-owned provider.
- Parallelism: integration tests run in one xUnit collection with parallelization disabled for
  determinism and to avoid emulator RU contention. Concurrency inside a lock test uses
  `Task.WhenAll`, not xUnit parallelism.
- Time: audit assertions use `FakeTimeProvider`. Lock expiry tests use short real durations (for
  example 1–2 s lease, 500 ms acquisition timeout) because retry delays are real; the expiry
  comparison still goes through `TimeProvider`.
- Target frameworks: the integration suite runs on both `net8.0` and `net10.0` in CI. Each matrix
  job uses its own run id/database name, so jobs can run concurrently.
- CI service container: GitHub Actions declares the emulator under `services` with the vNext image,
  port mappings `8281:8281` and `8280:8080`, and `--protocol https --port 8281`; the job exports
  `MIETEX_COSMOSDB_EMULATOR_CONNECTION_STRING` and waits for `/ready` before running the integration
  filter. The same container can be used locally with `docker run`.

### Required integration scenarios

Context and set:

1. Add a new entity, save, point-read it: document exists in the right partition, `PartitionKey` is
   set, `Etag` is populated, `IsNew()` is false.
2. Add an existing entity with `UpdateMode.Upsert`: a missing document is created, an existing
   document is updated.
3. Add an existing entity with `UpdateMode.Replace`: an existing document is updated; a missing
   document fails the batch with `404` and no partial write.
4. Mixed batch (create + replace + delete + patch) commits atomically; every affected ETag is
   written back at the correct index.
5. Batch failure (duplicate create, replace-missing, or an invalid patch path) throws
   `CosmosBatchException` with the failing index and status; no other operation in the batch is
   visible.
6. A save above `BatchOperationLimit` fails before any request; `100` operations succeed.
7. Audit stamping for new and existing entities, with and without `skipUserId`, including the
   `CreatedDate`/`UpdatedDate` asymmetry; `RequireUserForAuditedWrites` throws when applicable.
8. Soft delete writes `DeletedDate`/`DeletedById` via patch, keeps the document, updates the ETag,
   and leaves it findable; hard delete removes it and evicts the cache.
9. Custom patch operations update only the requested properties.
10. Discriminator filtering: two entity types sharing a partition, each set returns only its own;
    partition isolation: same id in another partition is not returned. A discriminator mismatch on
    a point read is tested once Open Question 2 is resolved; the default proposal is `null`.
11. `FindAsync` hit/miss/null; cache hit avoids a second read (assert via behavior, not call count),
    `EnableCache = false` reads through, `ClearCache` forces a fresh read, hard delete does not
    resurrect.
12. `GetItemQueryIterator` plus feed extensions enumerate all pages; cancellation stops enumeration;
    `EnumerableQuery` fallback executes in memory.
13. Hooks: `EnsureUsable` rejects writes after dispose; `ValidateBatchOperationCount` is called with
    the pending count; `PrepareBatch` can append an operation; `OnBatchCommitted` runs only on
    success; `OnSaveFinishedAsync` runs on success, failure, and the no-op path; a throwing
    post-commit hook does not turn a committed save into a failure.
14. `ClearAsync` discards pending changes; `Clear` does the same; repeated disposal is safe.
15. `CosmosClientProvider`: same key returns the same instance; different keys differ; `Reset`
    creates a new instance; an injected `CosmosClient` is used and not disposed; `ResetShared`
    isolates tests.
16. Serializer + emulator: an entity with enum and polymorphic payload round-trips through a real
    write/read, including `id` and `_etag` mapping.

Locking:

17. Acquire creates a lease with a `distributed-lock-{64 uppercase hex}` id, a unique owner, and
    `ExpiresAt = now + LeaseDuration`.
18. A second context acquiring the same resource while the lease is active times out with
    `TimeoutException`; no replacement occurs; caller cancellation yields `OperationCanceledException`.
19. After the lease expires, takeover succeeds conditionally; exactly one of N concurrent acquirers
    wins; the losers time out or retry, never both win.
20. `AddReleaseToBatch` appends an ETag-conditional delete; a successful save removes the lease in
    the same transaction and does not issue a second cleanup delete.
21. A failed save releases the lease through cleanup and discards pending changes; the next
    acquirer succeeds.
22. Abandoning a lock handle discards pending changes and caches and permits reacquisition.
23. Stale-owner fencing: acquire, let the lease expire, take over from a second context, then save
    the first context; the first save fails with `CosmosBatchException` (release precondition) and
    none of its business writes are visible.
24. Empty save while holding a lease still releases it and executes no batch.
25. Batch limit includes lease releases: 100 business operations plus one lease fails before the
    request.
26. `Clear`/`ClearAsync` release leases; disposal is idempotent; writes after disposal throw
    `ObjectDisposedException`.
27. Handle cleanup ignores `404`/`412`; a disposed handle never deletes a new owner's lease.
28. Same resource key in different partitions yields independent leases.

### Unit test detail

- Serializer: `ToStream` returns a positioned-at-zero, seekable, UTF-8-no-BOM stream; `FromStream`
  disposes the input; `Stream` passthrough; `SerializeMemberName` for `JsonProperty`, `DataMember`,
  `JsonExtensionData`; enum-as-string; `$type` polymorphism; reference loop throws;
  `ReferenceLoopHandling` setting is honoured.
- Settings factory: exact defaults; extra converters appended after defaults; configure callback runs
  last; no host-specific converter is present by default.
- Local endpoint: `AccountEndpoint` case-insensitivity, whitespace, missing key, malformed URI,
  `localhost`, `127.0.0.1`, `[::1]`, non-loopback host, relative URI; builder port; `ApplyIfLocalEndpoint`
  sets Gateway and an accepting handler for loopback, preserves a pre-set factory, and does nothing
  for remote endpoints.
- Options validation: empty names, `BatchOperationLimit` `0`/`101`, `MatchEtag` + `Upsert`,
  `Serializer` + `SerializerSettings`, `Client` + `ClientProvider`, negative/zero lock durations.
- Query formatter: string, `DateTime`, `DateTimeOffset`, `null`, numeric; replacement of multiple
  parameters; output is for logs only (the executed `QueryDefinition` is unchanged).

### Packaging and CI tests

- Run `dotnet pack -c Release` into a temp output, open the `.nupkg`, and assert: expected package
  id/version, `lib/net8.0/Mietex.CosmosDb.dll`, `lib/net10.0/Mietex.CosmosDb.dll`, XML docs,
  `README.md`, `LICENSE`, MIT license expression, repository URL, and a `.snupkg` with symbols.
- Build both TFMs with warnings as errors. Build twice in Release and compare the produced managed
  assemblies to catch accidental nondeterminism; tolerate toolchain-specific PDB differences.
- CI contract: unit/packaging job on Linux without Docker; integration job with the emulator service
  container; `dotnet test -f net8.0` and `-f net10.0`; integration filtered by
  `Category=Integration`, unit by `Category!=Integration`; publish job gated on tags and on both
  test matrices. These are decisions; workflow files are out of scope for this spec.

### Test traits and skip behavior

- `[Trait("Category", "Unit")]` for serializer, endpoint, formatter, and options tests.
- `[Trait("Category", "Integration")]` for context and lock tests.
- `[Trait("Category", "Packaging")]` for package inspection.
- Integration fixtures skip, not fail, when the emulator connection string is absent. CI sets it, so
  a skipped integration test in CI is treated as a failure by the pipeline check (for example by
  failing if the test summary reports zero integration tests executed).

## Out of Scope

- Database or container provisioning for production. `EnsureLocalDatabaseExistsAsync` is
  emulator-only; containers are created by infrastructure or by tests.
- An `IContainer`/`ITransactionalBatch` abstraction, repository pattern, unit-of-work abstraction
  beyond the context, or SDK mocking surface.
- A `CosmosEntity` base class for new consumers. Hosts own their entity bases; the contract is
  deliberately minimal. Revisit only if a new consumer has no base class.
- Dependency-injection extension methods (`AddCosmosDb`) and ASP.NET Core integration. Consumers
  construct contexts directly or register them in their own DI.
- Multi-region write accounts, lease renewal/heartbeat, and cross-partition locks. The lock algorithm
  requires a single write region with at least Session consistency.
- Stored procedures, triggers, UDFs, change feed, bulk executor, vector search, full-text search, and
  hierarchical partition keys.
- Batch splitting. A save above the limit fails; it is never divided into multiple transactions.
- Automatic query filters for soft-deleted entities. Hosts continue to apply their own
  `WhereNotDeleted`-style predicates.
- A System.Text.Json serializer or an AOT/trimming guarantee. A custom `CosmosSerializer` can be
  injected, but the default path is Newtonsoft.
- Retry policies beyond the Cosmos SDK defaults (for example custom 429 handling).
- Benchmarks, load tests, and RU cost optimisation reports.
- GitHub Actions workflow files, NuGet account configuration, and GitHub Pages settings. The
  implementation decisions describe the target; the files are a follow-up.
- NuGet package signing (Authenticode/strong naming) beyond SourceLink/symbols.
- Localization, telemetry exporters, and an Application Insights integration.
- Migration of stored data or documents. The storage shape is unchanged by the contracts.

## Further Notes

### Risks and constraints

- The lock algorithm is correct only for a single-write-region account with at least Session
  consistency and a shared client/container for reads and lease operations. Multi-write accounts are
  unsupported. Expiry is takeover eligibility, not a server-side commit deadline; fencing comes from
  the conditional lease delete in the same batch. Lease timestamps come from the configured
  `TimeProvider`; all contenders must use the same clock domain (system UTC in production).
- The vNext Linux emulator is a preview. It supports gateway mode only and not every Azure feature
  (no stored procedures/triggers/UDFs, limited indexing, no RU reporting). Integration tests must
  avoid those features.
- The emulator does not enforce authentication and must never be exposed outside the machine/CI job.
- `TypeNameHandling.Auto` is required to read existing polymorphic documents. It is dangerous for
  untrusted input. The serializer is a Cosmos persistence component, not a general-purpose parser;
  document this in the README.
- The 100-operation limit is a hard Cosmos constraint. Locks consume one operation each. The library
  fails fast rather than splitting batches, because splitting breaks atomicity.
- ETag write-back makes the in-memory object accurate without changing last-write-wins behavior.
  `MatchEtag` is opt-in.
- Soft delete changes query semantics only if a host enables it for entities that previously
  hard-deleted. A host that needs hard deletes sets `EnableSoftDelete = false`.
- The `ICalendar` to `TimeProvider` mapping is host-owned. A wrong adapter changes audit timestamps
  but not document structure.
- Query results deserialize `_etag` only when the host entity annotates it. Point reads compensate;
  hosts should still keep the attribute.
- C# property initializers on contexts run before the base constructor body, so `= null!` on
  `CosmosDbSet<>` properties does not wipe the discovered instances. Use `{ get; set; } = null!;`
  or an expression-bodied `=> Set<T>()` property. Do not use a get-only auto-property (it is not
  discovered) and do not use `= Set<T>()` in an initializer (base fields are not initialized yet).
- Synchronous `Dispose`, `Clear`, and `PerformLockUntilSaveChanges` block on async work. The library
  uses `ConfigureAwait(false)` throughout, but callers in synchronization-context-sensitive
  environments should prefer the async overloads.

### Compatibility checklist for a host migration

- Entity implements `ICosmosEntity`; `Etag` is public.
- Audit interfaces inherit the generic contracts with the host `TUser`.
- Context options reproduce the host's previous consistency, update mode, soft-delete mode, cache,
  index metrics, converters, and client options.
- Namespace collisions with the old `CosmosDb` types are removed.
- `ILogger` is supplied; `Console` logging expectations are gone.
- Exception handlers catch `CosmosBatchException` where they previously caught `Exception`.
- Test fixtures reset the client provider instead of nulling a static field.
- Mock-based SDK tests are replaced by emulator tests.
- Batch-size assumptions are checked against the 100-operation limit.

### Resolved decisions

1. **Default cache:** `EnableCache = true`. Legacy implementations cache unconditionally; preserving that
   behavior wins over a new-consumer preference. Invalidation is documented.
2. **Discriminator on point read:** no check. Legacy implementations return whatever the point read finds.
   Adding a mismatch check would be new behavior and is deferred. The set's LINQ query still filters
   by discriminator; `FindAsync` remains a raw point read.
3. **Soft-delete default:** the library defaults to `EnableSoftDelete = true`; a host that needs
   hard deletes sets `false` in its options. There is no library-side special case.
4. **Public API baseline:** deferred. `Microsoft.CodeAnalysis.PublicApiAnalyzers` and
   `PublicAPI.Shipped.txt` are a v1.1 hardening item, not a v0.1 blocker.
5. **Target framework matrix:** build, unit, and pack run for `net8.0` and `net10.0`. The emulator
   integration suite runs on `net10.0` only; `net8.0` consumers are covered by the identical code
   path and the unit/pack matrix.
6. **Cosmos SDK pin:** `Microsoft.Azure.Cosmos` 3.63.2 pinned centrally. Consumers are free to use a
   newer 3.x; the package dependency is a minimum, not an exact pin.
7. **Provider key:** a deterministic default key is derived as specified. Hosts that inject custom
   serializers and share a process should set an explicit `ClientKey`; this is documented, not
   enforced.
8. **Package signing:** unsigned package for v0.1. SourceLink, symbols, and build provenance
   attestations provide supply-chain traceability. Strong naming is out of scope.
9. **DocFX hosting:** GitHub Pages, deployed by GitHub Actions, API metadata generated from source.
10. **NuGet trusted publishing identity:** nuget.org profile `pmieten`, GitHub environment `release`,
    workflow file `publish.yml`. These values are documented as configuration variables so a fork can
    change them.
11. **Test framework:** xUnit v3 (`Assert.Skip`, fixture lifetime, VSTest runner).
12. **Cache and locks:** acquiring a lock clears all set caches, matching legacy implementations. Selective
    invalidation is not worth the complexity for v0.1.
