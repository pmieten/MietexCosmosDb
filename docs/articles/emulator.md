# Local emulator

The integration tests run against the real Cosmos DB vNext emulator in Docker. No Cosmos SDK types
are mocked.

## Start the emulator

```shell
docker run --detach --name cosmos-mietex \
  --publish 8281:8281 \
  --publish 8280:8080 \
  --publish 1234:1234 \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest
```

The library's test fixture accepts:

- `MIETEX_COSMOSDB_EMULATOR_CONNECTION_STRING` — full connection string;
- `MIETEX_COSMOSDB_EMULATOR_PORT` — gateway port, default `8281`;
- `MIETEX_TEST_RUN_ID` — optional database suffix for parallel runs.

The fallback connection string uses the well-known emulator key:

```csharp
var connectionString =
    CosmosLocalEndpointSupport.BuildEmulatorConnectionString(8281);
```

TLS relaxation and gateway mode are applied only when **every** `AccountEndpoint` in the connection
string is loopback. A mixed or remote connection string is never relaxed.

## Run the suites

```shell
dotnet test -f net10.0 --filter Category=Unit
dotnet test -f net10.0 --filter Category=Integration
dotnet test -f net10.0 --filter Category=Packaging
```

Integration tests skip when the emulator is not reachable. CI treats a skipped integration run as a
failure, so a broken service container cannot silently disable the suite.

## Fixtures

Each run creates one unique database. Each test class owns one container. Each test uses a fresh
partition key. Cleanup is best effort with a 30-second timeout; a failed cleanup is reported but does
not hide test results.
