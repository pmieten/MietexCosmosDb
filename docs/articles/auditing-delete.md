# Auditing, delete, and patch

## Audit stamping

`Add` stamps audit fields from `Options.TimeProvider.GetUtcNow()` and `CurrentUser`.

| Case | Created | Updated |
| --- | --- | --- |
| New entity, `skipUserId: false` | `CreatedDate` and `CreatedById` | `UpdatedDate` and `UpdatedById` |
| New entity, `skipUserId: true` | `CreatedDate` only | untouched |
| Existing entity, `skipUserId: false` | untouched | `UpdatedDate` and `UpdatedById` |
| Existing entity, `skipUserId: true` | untouched | untouched |

This asymmetric behavior matches the two source implementations and is covered by tests.

Set `RequireUserForAuditedWrites = true` to reject audited writes when `CurrentUser` is null and
`skipUserId` is false. A `IDeletedAuditEntity<TUser>` alone does not trigger the guard.

## Soft delete

```csharp
db.Delete(order); // soft when EnableSoftDelete and IDeletedAuditEntity<TUser>
db.Delete(order, useSoftDeleteIfPossible: false); // always hard
```

Soft delete queues one patch with `Set("/DeletedDate", now)` and `Set("/DeletedById", user)`. `Set`
works whether or not the JSON property exists. The document remains, the cached instance keeps its
identity, its audit fields are updated after a successful commit, and its ETag is refreshed.

## Hard delete

Hard delete queues `DeleteItem`. On commit, the cached id is evicted from every discovered set. A
different context may still have its own positive cache entry; call `ClearCache` after external
changes if that matters.

## Patch

```csharp
db.Patch(order, [PatchOperation.Set("/Status", "shipped")]);
db.Patch(orderId, [PatchOperation.Set("/Status", "shipped")]);
```

Use the entity overload when you have an ETag and `ConcurrencyMode.MatchEtag`. Arbitrary patches
and `Patch(string id, ...)` evict the cached id because their effects cannot be replayed locally.

## ETag concurrency

```csharp
var options = new CosmosDbOptions
{
    // ...
    UpdateMode = CosmosDbUpdateMode.Replace,
    ConcurrencyMode = CosmosDbConcurrencyMode.MatchEtag,
};
```

Point reads populate `Etag` from the response even when the entity lacks `[JsonProperty("_etag")]`.
Stale replace, delete, and patch operations fail the whole batch with `CosmosBatchException` and a
`PreconditionFailed` per-operation code. The winner's document is unchanged.
