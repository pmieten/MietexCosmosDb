# Partition locks

`CosmosLockingDbContextBase<TEntity, TUser>` combines a unit of work with a partition lease. The
lease is stored in the same container as business documents and is released in the same
transactional batch as the protected writes.

## Acquire and save

```csharp
public sealed class AppDbContext : CosmosLockingDbContextBase<Order, string>
{
    public AppDbContext(CosmosDbOptions options, string? user = null) : base(options, user) { }
    public CosmosDbSet<Order> Orders { get; set; } = null!;
}

await using var db = new AppDbContext(options);
var lease = await db.PerformLockUntilSaveChangesAsync("tenant-a/orders");

var order = await db.Orders.FindAsync(orderId);
order!.Status = "shipped";
db.Add(order);
await db.SaveChangesAsync();
```

Acquiring a lease clears all set caches so the protected reads see fresh state. A successful save
appends an ETag-conditional delete to the business batch. If the batch succeeds, the lease is
released atomically and disposal does not issue a second delete.

## Takeover and fencing

A lease has an expiry and is eligible for takeover after it passes. The new owner replaces the lease
with `If-Match` on the old ETag. The old owner is fenced by one of two paths:

- If the old handle is locally expired at batch preparation, `AddReleaseToBatch` throws
  `InvalidOperationException` before sending any request.
- If the old handle still looks locally valid but another owner has taken over, the ETag-conditional
  delete fails the batch with `CosmosBatchException` and a `412`; all business writes roll back.

Catch both paths in host code. Do not retry a fenced batch without a fresh read and a new lease.

## Timing

| Option | Default | Meaning |
| --- | --- | --- |
| `LockAcquireTimeout` | 30 s | Maximum wait for takeover. |
| `LockLeaseDuration` | 2 min | Lease validity. |
| `LockCleanupTimeout` | 5 s | Best-effort release on disposal. |

Locks are not renewed. Work that outlives `LockLeaseDuration` must be discarded and retried from a
fresh read.

## Cleanup

Disposal is idempotent. It does not inherit a cancelled request token, ignores `404`/`412`, and logs
other cleanup failures. Expiry is the recovery path.

The lock is a coordination primitive, not an authorization or security boundary.

## Non-lease documents

Lease ids are `distributed-lock-` plus the uppercase SHA-256 of the resource key. If a business
document happens to use that id, acquisition verifies `Discriminator`, `Owner`, and `ExpiresAt`
before replacing it. A non-lease document is never overwritten; acquisition throws
`InvalidOperationException`.
