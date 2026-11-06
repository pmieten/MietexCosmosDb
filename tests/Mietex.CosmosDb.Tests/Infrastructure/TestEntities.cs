using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Mietex.CosmosDb.Tests.Infrastructure;

public sealed record TestUser(string Name);

public enum TestKind
{
    Standard = 0,
    Priority = 1,
}

public class TestEntity : ICosmosEntity, ICreatedAuditEntity<TestUser>, IUpdatedAuditEntity<TestUser>, IDeletedAuditEntity<TestUser>
{
    public TestEntity()
    {
        Discriminator = GetType().Name;
    }

    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty(nameof(PartitionKey))]
    public string PartitionKey { get; set; } = string.Empty;

    public string Discriminator { get; set; }

    [JsonProperty("_etag")]
    public string? Etag { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Value { get; set; }

    public TestKind Kind { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public TestUser? CreatedById { get; set; }

    public DateTimeOffset UpdatedDate { get; set; }

    public TestUser? UpdatedById { get; set; }

    public DateTimeOffset? DeletedDate { get; set; }

    public TestUser? DeletedById { get; set; }

    public bool IsNew() => string.IsNullOrWhiteSpace(Etag);
}

public sealed class OtherTestEntity : TestEntity
{
    public string Extra { get; set; } = string.Empty;
}

public sealed class TestDbContext : CosmosDbContextBase<TestEntity, TestUser>
{
    public TestDbContext(CosmosDbOptions options, TestUser? currentUser = null)
        : base(options, currentUser)
    {
    }

    public CosmosDbSet<TestEntity> Entities => Set<TestEntity>();

    public CosmosDbSet<OtherTestEntity> Others => Set<OtherTestEntity>();
}

public sealed class AutoSetDbContext : CosmosDbContextBase<TestEntity, TestUser>
{
    public AutoSetDbContext(CosmosDbOptions options, TestUser? currentUser = null)
        : base(options, currentUser)
    {
    }

    public CosmosDbSet<TestEntity> Entities { get; set; } = null!;
}

public sealed class UnmappedEtagEntity : ICosmosEntity
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty(nameof(PartitionKey))]
    public string PartitionKey { get; set; } = string.Empty;

    public string Discriminator { get; set; } = nameof(UnmappedEtagEntity);

    public string? Etag { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsNew() => string.IsNullOrWhiteSpace(Etag);
}

public sealed class UnmappedEtagDbContext : CosmosDbContextBase<UnmappedEtagEntity, TestUser>
{
    public UnmappedEtagDbContext(CosmosDbOptions options, TestUser? currentUser = null)
        : base(options, currentUser)
    {
    }

    public CosmosDbSet<UnmappedEtagEntity> Entities => Set<UnmappedEtagEntity>();
}

public sealed class LockingTestDbContext : CosmosLockingDbContextBase<TestEntity, TestUser>
{
    public LockingTestDbContext(CosmosDbOptions options, TestUser? currentUser = null)
        : base(options, currentUser)
    {
    }

    public CosmosDbSet<TestEntity> Entities => Set<TestEntity>();
}

public sealed class HookTestDbContext : CosmosDbContextBase<TestEntity, TestUser>
{
    private readonly List<int> _validatedCounts = [];
    private readonly List<bool> _saveFinished = [];

    public HookTestDbContext(CosmosDbOptions options, TestUser? currentUser = null)
        : base(options, currentUser)
    {
    }

    public CosmosDbSet<TestEntity> Entities => Set<TestEntity>();

    public IReadOnlyList<int> ValidatedCounts => _validatedCounts;

    public IReadOnlyList<bool> SaveFinished => _saveFinished;

    public int PrepareBatchCalls { get; private set; }

    public int CommittedCalls { get; private set; }

    public bool ThrowOnCommitted { get; init; }

    public bool ThrowOnSaveFinished { get; init; }

    public Action<TransactionalBatch>? OnPrepareBatch { get; init; }

    public int AppendedOperationCount { get; init; }

    protected override int AdditionalBatchOperationCount => AppendedOperationCount;

    protected override void ValidateBatchOperationCount(int pendingOperationCount)
    {
        _validatedCounts.Add(pendingOperationCount);
        base.ValidateBatchOperationCount(pendingOperationCount);
    }

    protected override void PrepareBatch(Microsoft.Azure.Cosmos.TransactionalBatch batch)
    {
        PrepareBatchCalls++;
        OnPrepareBatch?.Invoke(batch);
        base.PrepareBatch(batch);
    }

    protected override void OnBatchCommitted()
    {
        CommittedCalls++;
        if (ThrowOnCommitted)
        {
            throw new InvalidOperationException("post-commit hook");
        }

        base.OnBatchCommitted();
    }

    protected override Task OnSaveFinishedAsync(bool succeeded)
    {
        _saveFinished.Add(succeeded);
        if (ThrowOnSaveFinished)
        {
            throw new InvalidOperationException("post-save hook");
        }

        return base.OnSaveFinishedAsync(succeeded);
    }
}
