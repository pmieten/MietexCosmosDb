# Mietex.CosmosDb — implementation plan and live status

> Status file for resuming work after a break. Update the checkboxes and the "Current state"
> section at the end of every phase. The engineering contract is
> [docs/specs/0001-mietex-cosmosdb.md](docs/specs/0001-mietex-cosmosdb.md).

## Current state

| Item | Value |
| --- | --- |
| Phase | 10 — v0.1.0 published to NuGet; indexing in progress |
| Branch | `main` |
| Package | `Mietex.CosmosDb` (MIT, `net8.0;net10.0`) |
| Scope | This repository only; host repositories are out of scope. |
| Test strategy | Real Cosmos DB vNext emulator in Docker; SDK `Container` mocks forbidden |
| Emulator used locally | local vNext emulator, https://localhost:8281 |
| Test count | 165 passing (`net10.0`): 84 unit + 80 integration + 1 smoke |
| Last green run | `net10.0` full suite green (167); `net8.0` unit suite green (84) |
| Emulator | Real vNext emulator, real containers, no SDK mocks |
| Next action | Wait for NuGet indexing to complete; verify `dotnet add package Mietex.CosmosDb --version 0.1.0`; consider protecting the `release` environment with required reviewers |

## TODOs

### 0. Research and setup
- [x] Analyse both source implementations and their host dependencies.
- [x] Install missing skills: `cosmosdb-best-practices`, `dotnet-best-practices`, `csharp-async`.
- [x] Research 2026 packaging/docs approach (NuGet Trusted Publishing OIDC, DocFX + GitHub Pages).
- [x] Confirm package name, licence, TFMs, migration scope with the user.
- [x] Confirm test seams with the user (real emulator, no SDK mocks).
- [x] Verify a real Cosmos vNext emulator is running locally on port 8281.

### 1. Spec
- [x] Write `docs/specs/0001-mietex-cosmosdb.md` (problem, solution, 88 stories, decisions).
- [x] Resolve open questions and record the resolutions.
- [ ] Publish the spec as a GitHub issue with `ready-for-agent` label.

### 2. Repository scaffold
- [x] `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`.
- [x] `Mietex.CosmosDb.slnx` with `src/Mietex.CosmosDb` and `tests/Mietex.CosmosDb.Tests`.
- [x] `LICENSE` (MIT), `README.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`.
- [x] Public API stubs that compile but throw `NotImplementedException` (red TDD baseline).

### 3. TDD — write tests first
- [x] Emulator fixture: connection string env var, unique database, container helper, readiness retry.
- [x] Unit tests: serializer, settings factory, local endpoint, options validation, query formatter.
- [x] Integration tests: context CRUD/batch/audit/soft delete/query/cache/hooks/concurrency.
- [x] Integration tests: partition lock acquisition/takeover/fencing/cleanup.
- [x] Integration tests: locking context lifecycle.
- [ ] Packaging test: inspect `.nupkg` TFMs, docs, licence, symbols.
- [x] Run the suite: all new tests fail for the expected reason (red).

### 4. Test review #1
- [x] Independent review of test quality, seam usage, determinism, forbidden mocks.
- [x] Apply fixes (blockers B1–B3 and high-value should-fix items; report in `the independent test review`).

### 5. Implementation
- [x] Contracts, options validation, client provider.
- [x] `CosmosDbContextBase` save pipeline, audit, delete, patch, hooks.
- [x] `CosmosDbSet`, feed extensions, serializer, local endpoint support.
- [x] `CosmosPartitionLock` and `CosmosLockingDbContextBase`.
- [x] Run the suite until green on the emulator.

### 6. Test review #2
- [x] Independent review after implementation (coverage gaps, edge cases, flakiness).
- [x] Apply fixes and re-run.

### 7. Human in the loop
- [ ] Present the public API, test results, and remaining risks for approval.

### 8. Documentation
- [x] README quick start and concepts.
- [x] DocFX site: index, articles, API metadata, toc.
- [x] Generic storage-compatibility and migration guidance.
- [x] Release/versioning/NuGet trusted-publishing runbook.

### 9. CI/CD and publishing
- [x] CI workflow: build + unit tests on net8/net10 + pack.
- [x] Integration workflow: Cosmos emulator service container + net10 suite.
- [x] Docs workflow: DocFX build + GitHub Pages deploy.
- [x] Publish workflow: tag, MinVer, `NuGet/login@v1` OIDC, push + provenance.
- [x] Dependabot, CodeQL, issue/PR templates.
- [x] NuGet trusted-publishing policy instructions (manual account step).
- [x] Dry-run pack locally and validate the `.nupkg`.
- [x] Commit and push repository; verify workflows on GitHub (CI, Integration, Docs, CodeQL green; Pages live).

## Review #2 remediation TODO

Source: [the independent implementation review](the independent implementation review). Commit after each stage.

### Stage A — security blockers
- [x] B1: require every `AccountEndpoint` in a connection string to be loopback; reject duplicates that mix local and remote.
- [x] B2: verify an existing lock document is really a `DistributedLock` lease before takeover; never overwrite a business document.
- [x] B3: derive the default client key from a hash, not the connection string; redact `ConnectionString` in `CosmosDbOptions.ToString()` and `CosmosClientDescriptor.ToString()`.
- [x] Add regression tests for B1/B2/B3.

### Stage B — correctness cluster
- [x] S1: make `DiscriminatorResolver` authoritative for writes; test custom resolver end-to-end.
- [x] S2: count `AdditionalBatchOperationCount` in the base batch-limit validation; add hook test.
- [x] S3: preserve root batch error messages, add fallback, bound the error read; test non-null 409 message.
- [x] S4: guard query formatting by `IsEnabled(Trace)`, catch formatter failures, sanitize control characters, single-pass replacement; add tests.
- [x] S5: evict or refresh cache after patch-by-id and soft delete; test cached soft-delete state.

### Stage C — locking and test infrastructure
- [x] S6: make locking `ClearAsync` unconditional after entry and test a canceled token.
- [x] S7: guard acquire-vs-dispose, recompute takeover time, retry transient reads, make release single-use, reject empty acquisition ETag.
- [x] S8: fixture catches malformed connection strings and skips; database name gets 8+ random hex chars; sanitize skip reason.
- [x] S9: add a discovery test for `= null!` auto-properties; document supported set-property shapes.
- [x] S10: document and test both fencing failure modes (local expiry vs server 412).

### Stage D — client, migration, test quality
- [x] S11: test effective client construction with a unique provider/key; clone serializer settings before callback; document injected-client ownership.
- [x] S12: migration docs for JSON property names/container path and `_etag`; add storage-shape tests.
- [x] S13: define request-level Cosmos exception contract; wrap or document.
- [x] S14: strengthen weak tests (hook invocation, N-way timing, serializer factory list, smoke trait).
- [x] S15: packaging test, CI workflows, DocFX, migration guides.

## Phase log

| Date | Phase | Result |
| --- | --- | --- |
| 2026-11-05 | 0 | Skills installed; spec decisions confirmed; emulator verified on :8281. |
| 2026-11-05 | 1 | Spec written and open questions resolved. |
| 2026-11-06 | 2-4 | Scaffold, 167-test red suite, independent test review, fixes. |
| 2026-11-06 | 5-6 | Implemented library; full suite green; independent implementation review; blockers and should-fix items fixed. |
| 2026-11-06 | 8-9 | DocFX site, release runbook, CI/Integration/Docs/CodeQL workflows pushed and green. Public repo recreated with clean history. Tag `v0.1.0` pushed; publish run failed only at NuGet trusted login because no matching trust policy exists yet. |
| 2026-11-06 | 10 | NuGet trusted-publishing policy created and active for `pmieten/MietexCosmosDb`, workflow `publish.yml`, environment `release`. Rerun of the publish workflow succeeded: `Mietex.CosmosDb.0.1.0.nupkg` and `.snupkg` pushed. Package page live at https://www.nuget.org/packages/Mietex.CosmosDb/0.1.0; flat-container indexing still in progress. |

## Risks / watch list

- Cosmos vNext emulator is preview; no stored procedures, triggers, or UDFs. Tests stay on plain
  documents, batches, patches, and locks.
- Lock tests must use short lease/acquisition durations; the production defaults are 2 min / 30 s.
- `TypeNameHandling.Auto` is a compatibility requirement for existing polymorphic documents and is a
  security risk for untrusted input; documented in README/SECURITY.
- Legacy host entities may need a public, nullable ETag property with `[JsonProperty("_etag")]`;
  this repository documents the requirement and never edits host code.
- Integration suite requires Docker locally; CI provides the service container.
