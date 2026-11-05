# Contributing

Thanks for helping improve Mietex.CosmosDb.

## Prerequisites

- .NET SDK 10.0.102 or newer (see `global.json`).
- Docker, for the Cosmos DB vNext emulator used by integration tests.
- Git.

## Build and test

```shell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

The integration suite expects a Cosmos DB emulator. The default connection string is
`https://localhost:8281` with the well-known emulator key. Start one with:

```shell
docker run --detach --name mietex-cosmos-emulator \
  --publish 8281:8281 --publish 8280:8080 \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest \
  --protocol https --port 8281
```

Override the connection with `MIETEX_COSMOSDB_EMULATOR_CONNECTION_STRING`.

If the emulator is unavailable, integration tests skip with a clear message.

## Rules

- No mocks of Cosmos SDK `Container`, `TransactionalBatch`, or response types. Integration behavior is
  tested against the emulator; pure decisions are extracted into unit-testable functions.
- Public API changes need XML documentation, tests, and a changelog entry.
- Keep warnings at zero; the build treats warnings as errors.
- Follow the existing code style and the decisions in `docs/specs/0001-mietex-cosmosdb.md`.
- Commits should be focused. Use conventional prefixes such as `feat:`, `fix:`, `test:`, `docs:`,
  `chore:`.

## Pull requests

1. Open a focused branch.
2. Add tests first where practical (the project follows TDD).
3. Run `dotnet test` on both target frameworks.
4. Update `README.md`, docs, or `CHANGELOG.md` when behavior changes.
5. Describe the problem, the change, and any trade-offs.
