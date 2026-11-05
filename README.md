# Mietex.CosmosDb

Opinionated Cosmos DB persistence primitives for .NET: partition-scoped contexts, transactional
batches, audit stamping, soft delete, point-read sets, a Newtonsoft.Json serializer, local-emulator
support, and ETag-fenced partition locks.

[![NuGet](https://img.shields.io/nuget/v/Mietex.CosmosDb.svg)](https://www.nuget.org/packages/Mietex.CosmosDb)
[![CI](https://github.com/pmieten/MietexCosmosDb/actions/workflows/ci.yml/badge.svg)](https://github.com/pmieten/MietexCosmosDb/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-blue)](https://pmieten.github.io/MietexCosmosDb/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

> Status: pre-release. The API is being stabilised; see [PLAN.md](PLAN.md) and the
> [engineering spec](docs/specs/0001-mietex-cosmosdb.md).

## Why

Two production systems grew near-identical Cosmos DB layers. This library captures the shared
behavior once, makes every divergence an explicit option, and validates it against a real Cosmos DB
emulator instead of SDK mocks.

## Install

```shell
dotnet add package Mietex.CosmosDb
```

Targets `net8.0` and `net10.0`.

## Quick start

```csharp
using Mietex.CosmosDb;

public sealed class Order : ICosmosEntity, IAuditEntity<string>
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PartitionKey { get; set; } = "";
    public string Discriminator { get; set; } = nameof(Order);
    public string? Etag { get; set; }
    public bool IsNew() => string.IsNullOrWhiteSpace(Etag);

    public DateTimeOffset CreatedDate { get; set; }
    public string? CreatedById { get; set; }
    public DateTimeOffset UpdatedDate { get; set; }
    public string? UpdatedById { get; set; }
}

public sealed class AppDbContext : CosmosDbContextBase<Order, string>
{
    public AppDbContext(CosmosDbOptions options) : base(options) { }

    public CosmosDbSet<Order> Orders { get; set; } = null!;
}

var db = new AppDbContext(new CosmosDbOptions
{
    ConnectionString = configuration["Cosmos:ConnectionString"],
    DatabaseName = "orders",
    ContainerName = "tenants",
    PartitionKey = "tenant-a",
});

db.Add(new Order());
await db.SaveChangesAsync();

var order = await db.Orders.FindAsync("id");
```

See the [documentation](https://pmieten.github.io/MietexCosmosDb/) for auditing, batching, soft
delete, and distributed locks.

## Security note

The default serializer uses Newtonsoft.Json `TypeNameHandling.Auto` for backward compatibility with
existing polymorphic documents. Do not use this serializer to parse untrusted input. See
[SECURITY.md](SECURITY.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Issues and pull requests are welcome.

## License

MIT. See [LICENSE](LICENSE).
