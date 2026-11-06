# Serialization

The library ships `CosmosJsonDotNetSerializer`, a Newtonsoft.Json serializer for the Cosmos SDK. The
SDK does not provide a default serializer with custom settings.

## Defaults

`CosmosSerializerSettingsFactory.Create()` returns:

- `TypeNameHandling.Auto`
- `ReferenceLoopHandling.Error`
- `PreserveReferencesHandling.None`
- `ConstructorHandling.AllowNonPublicDefaultConstructor`
- one `StringEnumConverter`

These defaults preserve existing polymorphic documents. `TypeNameHandling.Auto` is a compatibility
requirement, not safe parsing for untrusted input. See `SECURITY.md` in the repository.

## Customize

```csharp
var settings = CosmosSerializerSettingsFactory.Create(
    converters: [new IsoDateTimeConverter()],
    configure: configured =>
    {
        configured.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
    });

var options = new CosmosDbOptions
{
    // ...
    SerializerSettings = settings,
};
```

`Serializer` and `SerializerSettings` are mutually exclusive. Resolution order:

1. explicit `Serializer`;
2. explicit `SerializerSettings`, cloned before `ConfigureSerializerSettings` is applied;
3. factory defaults plus `ConfigureSerializerSettings`.

An injected `Client` owns its serializer. Serializer options on `CosmosDbOptions` are ignored when
`Client` is set.

## Storage shape

Keep `[JsonProperty("id")]` on `Id` and `[JsonProperty("_etag")]` on `Etag`. Point reads compensate
for a missing `_etag` attribute, but query results do not. A missing attribute can also persist a
separate `"Etag"` member.

Keep the existing JSON name for the partition key. The interface property is named `PartitionKey`,
but the container partition key path is a separate concern. If a host used
`[JsonProperty("PartitionKeyValue")]` with a `/PartitionKeyValue` path, keep both until a deliberate
data migration.
