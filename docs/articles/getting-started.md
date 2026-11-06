# Getting started

## Install

```shell
dotnet add package Mietex.CosmosDb
```

The package targets `net8.0` and `net10.0`.

## Define an entity

The library needs `id`, partition key, discriminator, ETag, and `IsNew()`. Keep the JSON names your
existing containers use.

```csharp
using Mietex.CosmosDb;
using Newtonsoft.Json;

public sealed class Order : ICosmosEntity, IAuditEntity<string>
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("PartitionKey")]
    public string PartitionKey { get; set; } = string.Empty;

    public string Discriminator { get; set; } = nameof(Order);

    [JsonProperty("_etag")]
    public string? Etag { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public DateTimeOffset CreatedDate { get; set; }
    public string? CreatedById { get; set; }
    public DateTimeOffset UpdatedDate { get; set; }
    public string? UpdatedById { get; set; }

    public bool IsNew() => string.IsNullOrWhiteSpace(Etag);
}
```

## Define a context

```csharp
public sealed class AppDbContext : CosmosDbContextBase<Order, string>
{
    public AppDbContext(CosmosDbOptions options, string? currentUser = null)
        : base(options, currentUser)
    {
    }

    public CosmosDbSet<Order> Orders { get; set; } = null!;
}
```

Use `= null!` (discovered eagerly) or `=> Set<Order>()` (lazy). Do not use a get-only
auto-property, and do not call `Set<T>()` from a property initializer.

## Configure and save

```csharp
var db = new AppDbContext(new CosmosDbOptions
{
    ConnectionString = configuration["Cosmos:ConnectionString"],
    DatabaseName = "orders",
    ContainerName = "tenants",
    PartitionKey = "tenant-a",
    UpdateMode = CosmosDbUpdateMode.Upsert,
    EnableSoftDelete = true,
});

db.Add(new Order { CustomerId = "c-1" });
await db.SaveChangesAsync();
```

`Add` stamps audit fields from `CurrentUser` and `Options.TimeProvider`. A save is one transactional
batch. All operations for one partition commit or fail together.

## Read

```csharp
var order = await db.Orders.FindAsync(orderId);
var recent = await db.Orders.Query
    .Where(order => order.CreatedDate >= cutoff)
    .ToListWithFeedSupportAsync();
```

`FindAsync` uses the point-read cache when `EnableCache` is `true`. The set query filters by
discriminator and the context partition key.

## Next

- [Auditing, delete, and patch](auditing-delete.md)
- [Partition locks](partition-locks.md)
- [Serialization](serialization.md)
