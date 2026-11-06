using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Mietex.CosmosDb;

/// <summary>
/// Base unit of work over one Cosmos DB container and partition. A context batches pending writes and
/// commits them with one transactional batch.
/// </summary>
/// <typeparam name="TEntity">Host entity base type implementing <see cref="ICosmosEntity"/>.</typeparam>
/// <typeparam name="TUser">Host user identity type.</typeparam>
public abstract class CosmosDbContextBase<TEntity, TUser> : IDisposable, IAsyncDisposable
    where TEntity : class, ICosmosEntity
    where TUser : class
{
    private static readonly MethodInfo SetMethod =
        typeof(CosmosDbContextBase<TEntity, TUser>).GetMethod(
            nameof(Set),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly Action<ILogger, string, Exception?> QueryExecuting =
        LoggerMessage.Define<string>(
            LogLevel.Trace,
            new EventId(1, "QueryExecuting"),
            "Executing query: {Query}");

    private static readonly Action<ILogger, Exception?> PostCommitBookkeepingFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, "PostCommitBookkeepingFailed"),
            "Failed to apply post-commit bookkeeping.");

    private static readonly Action<ILogger, Exception?> OnBatchCommittedFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(3, "OnBatchCommittedFailed"),
            "OnBatchCommitted threw after a successful commit.");

    private static readonly Action<ILogger, Exception?> OnSaveFinishedFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4, "OnSaveFinishedFailed"),
            "OnSaveFinishedAsync threw after a save attempt.");

    private static readonly Action<ILogger, Exception?> QueryFormattingFailed =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(5, "QueryFormattingFailed"),
            "Could not format a Cosmos query for trace logging; the original query will be used.");

    private readonly CosmosDbOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Container _container;
    private readonly List<PendingOperation> _pending = [];
    private readonly List<ICosmosDbSet> _dbSets = [];
    private readonly Dictionary<Type, ICosmosDbSet> _setsByType = [];
    private readonly object _setsGate = new();
    private bool _disposed;

    /// <summary>Initializes a context from validated options.</summary>
    /// <param name="options">Context options.</param>
    /// <param name="currentUser">Optional current user for audit stamping.</param>
    protected CosmosDbContextBase(CosmosDbOptions options, TUser? currentUser = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _timeProvider = options.TimeProvider;
        _logger = options.Logger ??
            options.LoggerFactory?.CreateLogger(GetType()) ??
            NullLogger.Instance;
        CurrentUser = currentUser;
        _container = CreateContainer(options);

        DiscoverSets();
    }

    /// <summary>Gets the effective options.</summary>
    public CosmosDbOptions Options => _options;

    /// <summary>Gets or sets the current user used for audit stamping.</summary>
    public TUser? CurrentUser { get; protected set; }

    /// <summary>Gets the partition key value this context operates on.</summary>
    public string PartitionKey => _options.PartitionKey;

    /// <summary>Gets the Cosmos container.</summary>
    public Container Container => _container;

    /// <summary>Gets the automatically discovered sets.</summary>
    public IReadOnlyList<ICosmosDbSet> DbSets => _dbSets;

    /// <summary>Gets the context logger.</summary>
    protected ILogger Logger => _logger;

    /// <summary>Gets the number of operations a subclass appends in <see cref="PrepareBatch"/>.</summary>
    protected virtual int AdditionalBatchOperationCount => 0;

    /// <summary>Sets or replaces the current user.</summary>
    /// <param name="user">New user, or <see langword="null"/> to clear it.</param>
    public void SetUserContext(TUser? user)
    {
        EnsureUsable();
        CurrentUser = user;
    }

    /// <summary>Queues a create or update for an entity and stamps audit fields.</summary>
    /// <typeparam name="T">Concrete entity type.</typeparam>
    /// <param name="entity">Entity to write.</param>
    /// <param name="skipUserId">When <see langword="true"/>, user ids are not stamped.</param>
    public virtual void Add<T>(T entity, bool skipUserId = false)
        where T : TEntity
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(entity);

        if (!skipUserId &&
            _options.RequireUserForAuditedWrites &&
            CurrentUser is null &&
            (entity is ICreatedAuditEntity<TUser> || entity is IUpdatedAuditEntity<TUser>))
        {
            throw new InvalidOperationException(
                "The current user is required for audited writes. Set CurrentUser or pass skipUserId: true.");
        }

        var isNew = entity.IsNew();
        var now = _timeProvider.GetUtcNow();

        entity.PartitionKey = PartitionKey;
        if (_options.DiscriminatorResolver is not null ||
            string.IsNullOrWhiteSpace(entity.Discriminator))
        {
            entity.Discriminator = ResolveDiscriminator(entity.GetType());
        }

        if (isNew)
        {
            if (entity is ICreatedAuditEntity<TUser> created)
            {
                created.CreatedDate = now;
                if (!skipUserId)
                {
                    created.CreatedById = CurrentUser;
                }
            }

            if (!skipUserId && entity is IUpdatedAuditEntity<TUser> updated)
            {
                updated.UpdatedDate = now;
                updated.UpdatedById = CurrentUser;
            }
        }
        else if (!skipUserId && entity is IUpdatedAuditEntity<TUser> updated)
        {
            updated.UpdatedDate = now;
            updated.UpdatedById = CurrentUser;
        }

        var kind = isNew
            ? PendingKind.Create
            : _options.UpdateMode == CosmosDbUpdateMode.Upsert
                ? PendingKind.Upsert
                : PendingKind.Replace;

        _pending.Add(new PendingOperation(kind, entity, entity.Id, PatchOperations: null));
    }

    /// <summary>Queues a soft or hard delete.</summary>
    /// <param name="entity">Entity to delete.</param>
    /// <param name="useSoftDeleteIfPossible">When <see langword="true"/>, deleted-audit entities are patched.</param>
    public void Delete(TEntity entity, bool useSoftDeleteIfPossible = true)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(entity);

        if (_options.EnableSoftDelete &&
            useSoftDeleteIfPossible &&
            entity is IDeletedAuditEntity<TUser>)
        {
            var now = _timeProvider.GetUtcNow();
            IReadOnlyList<PatchOperation> operations =
            [
                PatchOperation.Set("/DeletedDate", now),
                PatchOperation.Set("/DeletedById", CurrentUser),
            ];
            _pending.Add(new PendingOperation(
                PendingKind.Patch,
                entity,
                entity.Id,
                operations,
                SoftDeletedAt: now,
                SoftDeletedBy: CurrentUser));
            return;
        }

        _pending.Add(new PendingOperation(PendingKind.Delete, entity, entity.Id, PatchOperations: null));
    }

    /// <summary>Queues patch operations for an entity.</summary>
    /// <param name="entity">Entity being patched.</param>
    /// <param name="patchOperations">Patch operations to apply.</param>
    public void Patch(TEntity entity, IReadOnlyList<PatchOperation> patchOperations)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(patchOperations);

        _pending.Add(new PendingOperation(PendingKind.Patch, entity, entity.Id, patchOperations));
    }

    /// <summary>Queues patch operations by id.</summary>
    /// <param name="entityId">Document id.</param>
    /// <param name="patchOperations">Patch operations to apply.</param>
    public void Patch(string entityId, IReadOnlyList<PatchOperation> patchOperations)
    {
        EnsureUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentNullException.ThrowIfNull(patchOperations);

        _pending.Add(new PendingOperation(PendingKind.Patch, Entity: null, entityId, patchOperations));
    }

    /// <summary>Queues a batch query.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="queryDefinition">Query definition.</param>
    /// <returns>A feed iterator scoped to the context partition.</returns>
    public FeedIterator<T> GetItemQueryIterator<T>(QueryDefinition queryDefinition)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(queryDefinition);

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            string formatted;
            try
            {
                formatted = CosmosQueryLogFormatter.Format(queryDefinition);
            }
            catch (Exception exception)
            {
                QueryFormattingFailed(_logger, exception);
                formatted = queryDefinition.QueryText;
            }

            QueryExecuting(_logger, formatted, null);
        }

        return _container.GetItemQueryIterator<T>(
            queryDefinition,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(PartitionKey),
                PopulateIndexMetrics = _options.PopulateIndexMetrics,
            });
    }

    /// <summary>Creates a partition-scoped LINQ query.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <returns>An ordered queryable.</returns>
    public IOrderedQueryable<T> GetItemLinqQueryable<T>()
    {
        EnsureUsable();
        return _container.GetItemLinqQueryable<T>(
            allowSynchronousQueryExecution: true,
            continuationToken: null,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(PartitionKey),
                PopulateIndexMetrics = _options.PopulateIndexMetrics,
            });
    }

    /// <summary>Discards pending changes and clears caches.</summary>
    public virtual void Clear()
    {
        EnsureUsable();
        DiscardPendingAndCaches();
    }

    /// <summary>Discards pending changes and clears caches.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the context is cleared.</returns>
    public virtual Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clear();
        return Task.CompletedTask;
    }

    /// <summary>Commits all pending operations in one transactional batch.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the batch is committed.</returns>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();

        var succeeded = true;
        var operationCount = _pending.Count + AdditionalBatchOperationCount;
        try
        {
            if (_pending.Count == 0)
            {
                return;
            }

            ValidateBatchOperationCount(_pending.Count);

            var batch = _container.CreateTransactionalBatch(new PartitionKey(PartitionKey));
            foreach (var operation in _pending)
            {
                AppendOperation(batch, operation);
            }

            PrepareBatch(batch);

            using var response = await batch
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateBatchException(response, operationCount);
            }

            try
            {
                WriteBackEtags(response);
                ApplyPostCommitCacheEffects();
            }
            catch (Exception exception)
            {
                PostCommitBookkeepingFailed(_logger, exception);
            }

            try
            {
                OnBatchCommitted();
            }
            catch (Exception exception)
            {
                OnBatchCommittedFailed(_logger, exception);
            }

            _pending.Clear();
        }
        catch (OperationCanceledException)
        {
            succeeded = false;
            throw;
        }
        catch (CosmosException exception)
        {
            succeeded = false;
            throw new CosmosBatchException(
                $"The Cosmos transactional batch request failed with status {exception.StatusCode}.",
                exception.StatusCode,
                operationCount,
                Array.Empty<CosmosBatchOperationError>(),
                exception.Diagnostics?.ToString(),
                exception.ActivityId,
                exception.RequestCharge,
                exception);
        }
        catch
        {
            succeeded = false;
            throw;
        }
        finally
        {
            try
            {
                await OnSaveFinishedAsync(succeeded).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                OnSaveFinishedFailed(_logger, exception);
            }
        }
    }

    /// <summary>Clears every discovered set cache.</summary>
    public void ClearCache()
    {
        foreach (var set in _dbSets)
        {
            set.ClearCache();
        }
    }

    /// <summary>Gets an already discovered set or creates and registers one.</summary>
    /// <typeparam name="T">Concrete entity type.</typeparam>
    /// <returns>The set for <typeparamref name="T"/>.</returns>
    protected CosmosDbSet<T> Set<T>()
        where T : class, TEntity, ICosmosEntity
    {
        EnsureUsable();

        lock (_setsGate)
        {
            if (_setsByType.TryGetValue(typeof(T), out var existing))
            {
                return (CosmosDbSet<T>)existing;
            }

            var set = new CosmosDbSet<T>(
                _container,
                PartitionKey,
                ResolveDiscriminator(typeof(T)),
                _options.EnableCache,
                _options.PopulateIndexMetrics);

            _setsByType[typeof(T)] = set;
            _dbSets.Add(set);
            return set;
        }
    }

    /// <summary>Hook that rejects operations on an unusable context.</summary>
    protected virtual void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>Hook that validates the number of pending operations.</summary>
    /// <param name="pendingOperationCount">Pending business operation count.</param>
    protected virtual void ValidateBatchOperationCount(int pendingOperationCount)
    {
        var operationCount = pendingOperationCount + AdditionalBatchOperationCount;
        if (operationCount > _options.BatchOperationLimit)
        {
            throw new InvalidOperationException(
                $"The save contains {operationCount} operations, which exceeds the configured batch limit of {_options.BatchOperationLimit}.");
        }
    }

    /// <summary>Hook that appends operations to a prepared batch.</summary>
    /// <param name="batch">Batch being prepared.</param>
    protected virtual void PrepareBatch(TransactionalBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
    }

    /// <summary>Hook invoked after a successful commit.</summary>
    protected virtual void OnBatchCommitted()
    {
    }

    /// <summary>Hook invoked after every save attempt.</summary>
    /// <param name="succeeded">Whether the save succeeded.</param>
    /// <returns>A task that completes when post-save work is done.</returns>
    protected virtual Task OnSaveFinishedAsync(bool succeeded) => Task.CompletedTask;

    /// <summary>Discards the pending queue and clears all set caches.</summary>
    protected void DiscardPendingAndCaches()
    {
        _pending.Clear();
        ClearCache();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Disposes the context.</summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DiscardPendingAndCaches();
    }

    private static Container CreateContainer(CosmosDbOptions options)
    {
        if (options.Client is not null)
        {
            return options.Client.GetContainer(options.DatabaseName, options.ContainerName);
        }

        var clientOptions = BuildClientOptions(options);
        var connectionString = options.ConnectionString!;
        var provider = options.ClientProvider ?? CosmosClientProvider.Shared;
        var clientKey = options.ClientKey ?? BuildDefaultClientKey(options, connectionString);
        var client = provider.GetClient(
            new CosmosClientDescriptor(clientKey, connectionString, clientOptions));

        return client.GetContainer(options.DatabaseName, options.ContainerName);
    }

    private static CosmosClientOptions BuildClientOptions(CosmosDbOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = options.ApplicationName,
            ConsistencyLevel = options.ConsistencyLevel,
        };

        if (options.Serializer is not null)
        {
            clientOptions.Serializer = options.Serializer;
        }
        else
        {
            var settings = options.SerializerSettings is null
                ? CosmosSerializerSettingsFactory.Create()
                : CloneSettings(options.SerializerSettings);
            options.ConfigureSerializerSettings?.Invoke(settings);
            clientOptions.Serializer = new CosmosJsonDotNetSerializer(settings);
        }

        options.ConfigureClientOptions?.Invoke(clientOptions);
        CosmosLocalEndpointSupport.ApplyIfLocalEndpoint(clientOptions, options.ConnectionString);
        return clientOptions;
    }

    private static JsonSerializerSettings CloneSettings(JsonSerializerSettings source)
    {
        var clone = new JsonSerializerSettings
        {
            CheckAdditionalContent = source.CheckAdditionalContent,
            ConstructorHandling = source.ConstructorHandling,
            ContractResolver = source.ContractResolver,
            Culture = source.Culture,
            DateFormatHandling = source.DateFormatHandling,
            DateFormatString = source.DateFormatString,
            DateParseHandling = source.DateParseHandling,
            DateTimeZoneHandling = source.DateTimeZoneHandling,
            DefaultValueHandling = source.DefaultValueHandling,
            Error = source.Error,
            FloatFormatHandling = source.FloatFormatHandling,
            FloatParseHandling = source.FloatParseHandling,
            Formatting = source.Formatting,
            MaxDepth = source.MaxDepth,
            MetadataPropertyHandling = source.MetadataPropertyHandling,
            MissingMemberHandling = source.MissingMemberHandling,
            NullValueHandling = source.NullValueHandling,
            ObjectCreationHandling = source.ObjectCreationHandling,
            PreserveReferencesHandling = source.PreserveReferencesHandling,
            ReferenceLoopHandling = source.ReferenceLoopHandling,
            SerializationBinder = source.SerializationBinder,
            StringEscapeHandling = source.StringEscapeHandling,
            TraceWriter = source.TraceWriter,
            TypeNameAssemblyFormatHandling = source.TypeNameAssemblyFormatHandling,
            TypeNameHandling = source.TypeNameHandling,
        };

        foreach (var converter in source.Converters)
        {
            clone.Converters.Add(converter);
        }

        return clone;
    }

    private static string BuildDefaultClientKey(
        CosmosDbOptions options,
        string connectionString)
    {
        var payload = string.Join(
            '\0',
            connectionString,
            options.ApplicationName,
            options.ConsistencyLevel.ToString(),
            options.DatabaseName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return "client-" + Convert.ToHexString(hash);
    }

    private void DiscoverSets()
    {
        foreach (var property in GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanWrite ||
                property.GetIndexParameters().Length != 0 ||
                !property.PropertyType.IsGenericType ||
                property.PropertyType.GetGenericTypeDefinition() != typeof(CosmosDbSet<>))
            {
                continue;
            }

            var entityType = property.PropertyType.GetGenericArguments()[0];
            if (!typeof(TEntity).IsAssignableFrom(entityType))
            {
                continue;
            }

            var existing = property.GetValue(this);
            if (existing is ICosmosDbSet existingSet)
            {
                RegisterSet(existingSet);
                continue;
            }

            var set = (ICosmosDbSet)SetMethod.MakeGenericMethod(entityType).Invoke(this, null)!;
            property.SetValue(this, set);
        }
    }

    private void RegisterSet(ICosmosDbSet set)
    {
        lock (_setsGate)
        {
            if (_setsByType.ContainsKey(set.EntityType))
            {
                return;
            }

            _setsByType[set.EntityType] = set;
            _dbSets.Add(set);
        }
    }

    private string ResolveDiscriminator(Type entityType) =>
        _options.DiscriminatorResolver?.Invoke(entityType) ?? entityType.Name;

    private void AppendOperation(TransactionalBatch batch, PendingOperation operation)
    {
        var etag = _options.ConcurrencyMode == CosmosDbConcurrencyMode.MatchEtag &&
            operation.Entity is not null &&
            !string.IsNullOrEmpty(operation.Entity.Etag)
                ? operation.Entity.Etag
                : null;

        var itemOptions = new TransactionalBatchItemRequestOptions
        {
            EnableContentResponseOnWrite = true,
            IfMatchEtag = etag,
        };

        switch (operation.Kind)
        {
            case PendingKind.Create:
                batch.CreateItem(operation.Entity!, itemOptions);
                break;
            case PendingKind.Upsert:
                batch.UpsertItem(operation.Entity!, itemOptions);
                break;
            case PendingKind.Replace:
                batch.ReplaceItem(operation.Id, operation.Entity!, itemOptions);
                break;
            case PendingKind.Delete:
                batch.DeleteItem(operation.Id, itemOptions);
                break;
            case PendingKind.Patch:
                batch.PatchItem(
                    operation.Id,
                    operation.PatchOperations!,
                    new TransactionalBatchPatchItemRequestOptions
                    {
                        EnableContentResponseOnWrite = true,
                        IfMatchEtag = etag,
                    });
                break;
            default:
                throw new InvalidOperationException($"Unsupported operation kind: {operation.Kind}.");
        }
    }

    private void WriteBackEtags(TransactionalBatchResponse response)
    {
        for (var index = 0; index < _pending.Count && index < response.Count; index++)
        {
            var operation = _pending[index];
            if (operation.Entity is null || operation.Kind == PendingKind.Delete)
            {
                continue;
            }

            var etag = response[index].ETag;
            if (!string.IsNullOrEmpty(etag))
            {
                operation.Entity.Etag = etag;
            }
        }
    }

    private void ApplyPostCommitCacheEffects()
    {
        foreach (var operation in _pending)
        {
            if (operation.Kind == PendingKind.Delete)
            {
                EvictFromAllSets(operation.Id);
                continue;
            }

            if (operation.Kind != PendingKind.Patch)
            {
                continue;
            }

            if (operation.SoftDeletedAt is { } deletedAt &&
                operation.Entity is IDeletedAuditEntity<TUser> deleted)
            {
                deleted.DeletedDate = deletedAt;
                deleted.DeletedById = operation.SoftDeletedBy;
                continue;
            }

            // Arbitrary patches and patch-by-id may have changed fields we cannot replay locally.
            EvictFromAllSets(operation.Id);
        }
    }

    private void EvictFromAllSets(string id)
    {
        foreach (var set in _dbSets)
        {
            set.Evict(id);
        }
    }

    private static CosmosBatchException CreateBatchException(
        TransactionalBatchResponse response,
        int operationCount)
    {
        var failures = new List<CosmosBatchOperationError>();
        for (var index = 0; index < operationCount && index < response.Count; index++)
        {
            var result = response[index];
            if (result.IsSuccessStatusCode)
            {
                continue;
            }

            failures.Add(new CosmosBatchOperationError(
                index,
                result.StatusCode,
                ReadErrorMessage(result) ??
                    response.ErrorMessage ??
                    result.StatusCode.ToString()));
        }

        var rootFailureCount = failures.Count(
            failure => failure.StatusCode != HttpStatusCode.FailedDependency);
        var message =
            $"The Cosmos transactional batch failed with status {response.StatusCode}. " +
            $"{rootFailureCount} of {operationCount} operations failed.";

        return new CosmosBatchException(
            message,
            response.StatusCode,
            operationCount,
            failures,
            response.Diagnostics?.ToString(),
            response.ActivityId,
            response.RequestCharge);
    }

    private static string? ReadErrorMessage(TransactionalBatchOperationResult result)
    {
        if (result.ResourceStream is null)
        {
            return null;
        }

        try
        {
            using var reader = new StreamReader(
                result.ResourceStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            var buffer = new char[4096];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            return read == 0 ? null : new string(buffer, 0, read);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return null;
        }
    }

    private enum PendingKind
    {
        Create,
        Upsert,
        Replace,
        Delete,
        Patch,
    }

    private sealed record PendingOperation(
        PendingKind Kind,
        TEntity? Entity,
        string Id,
        IReadOnlyList<PatchOperation>? PatchOperations,
        DateTimeOffset? SoftDeletedAt = null,
        TUser? SoftDeletedBy = null);
}
