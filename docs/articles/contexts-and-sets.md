# Contexts and sets

A context is a unit of work over one Cosmos DB container and one partition key value. It owns:

- a pending queue of business operations;
- one `TransactionalBatch` per save;
- one point-read cache per discovered set;
- optional partition leases (locking context only).

## Options

| Option | Default | Purpose |
| --- | --- | --- |
| `UpdateMode` | `Upsert` | Create-or-overwrite versus replace-only. |
| `ConcurrencyMode` | `LastWriteWins` | Optional `If-Match` on replace/delete/patch. |
| `EnableCache` | `true` | Positive point-read cache per set. |
| `EnableSoftDelete` | `true` | Patch `DeletedDate`/`DeletedById` when possible. |
| `BatchOperationLimit` | `100` | Fail before sending an oversized batch. |
| `RequireUserForAuditedWrites` | `false` | Reject audited writes without a current user. |
| `DiscriminatorResolver` | type name | One discriminator per entity type; authoritative for writes and queries. |
| `TimeProvider` | system | Audit timestamps and lock expiry. |

`UpdateMode.Upsert` cannot be combined with `ConcurrencyMode.MatchEtag`; use `Replace`.

## Set discovery

Two property shapes are supported:

```csharp
public CosmosDbSet<Order> Orders { get; set; } = null!; // eager discovery
public CosmosDbSet<Order> Orders => Set<Order>();      // lazy registration
```

A get-only auto-property is not discovered. Do not initialize a set with `= Set<Order>()`: derived
initializers run before base field initialization. `DbSets` contains eagerly discovered sets
immediately; lazy sets appear after first access.

## Save pipeline

1. Reject a disposed context.
2. No-op when the pending queue is empty (locking contexts still release leases).
3. Validate the batch limit, including subclass-appended operations.
4. Append business operations in insertion order.
5. Let `PrepareBatch` append extra operations.
6. Execute one batch.
7. Write ETags back by index, apply cache effects, call `OnBatchCommitted`.
8. Clear pending operations.
9. Call `OnSaveFinishedAsync` even for no-op, success, and failure.

A failed batch leaves the pending queue in the base context for inspection. The locking context
discards it because the lease is no longer trustworthy.

## Hooks

Subclass `CosmosDbContextBase<TEntity, TUser>` and override:

- `EnsureUsable`
- `ValidateBatchOperationCount`
- `PrepareBatch`
- `OnBatchCommitted`
- `OnSaveFinishedAsync`

Exceptions from post-commit hooks are logged and suppressed; a committed save is never reported as
failed.
