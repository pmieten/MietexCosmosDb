# Mietex.CosmosDb

Opinionated Cosmos DB persistence primitives for .NET: partition-scoped contexts, transactional
batches, audit stamping, soft delete, point-read sets, a Newtonsoft.Json serializer, local-emulator
support, and ETag-fenced partition locks.

```shell
dotnet add package Mietex.CosmosDb
```

The package targets `net8.0` and `net10.0`.

## Start here

- [Getting started](articles/getting-started.md)
- [Contexts and sets](articles/contexts-and-sets.md)
- [Auditing, delete, and patch](articles/auditing-delete.md)
- [Partition locks](articles/partition-locks.md)
- [Serialization](articles/serialization.md)
- [Local emulator](articles/emulator.md)
- [Migrate from legacy host](migration/eorderflow.md)
- [Migrate from legacy host](migration/ms-dc.md)
- [API reference](api/index.md)

## Design principles

- One context operates on one container and one partition key value.
- Every save is one transactional batch in insertion order.
- Soft delete, ETag concurrency, upsert/replace, cache, and locking are explicit options.
- The library never owns a host-provided `CosmosClient`.
- The distributed lock is a coordination primitive, not an authorization mechanism.

## Repository

Source, issues, and releases: [github.com/pmieten/MietexCosmosDb](https://github.com/pmieten/MietexCosmosDb).
