# Mietex.CosmosDb — implementation plan and live status

> Status file for resuming work after a break. Update the checkboxes and the "Current state"
> section at the end of every phase. The engineering contract is
> [docs/specs/0001-mietex-cosmosdb.md](docs/specs/0001-mietex-cosmosdb.md).

## Current state

| Item | Value |
| --- | --- |
| Phase | 1 — analysis and spec |
| Branch | `main` |
| Package | `Mietex.CosmosDb` (MIT, `net8.0;net10.0`) |
| Source projects | READ-ONLY: `legacy host root\legacy host`, `legacy host root\legacy host` |
| Test strategy | Real Cosmos DB vNext emulator in Docker; SDK `Container` mocks forbidden |
| Emulator used locally | `cosmos-legacy host-Dev`, https://localhost:8281 |
| Next action | Scaffold repository and solution, then write failing tests |

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
- [ ] `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`.
- [ ] `Mietex.CosmosDb.sln` with `src/Mietex.CosmosDb` and `tests/Mietex.CosmosDb.Tests`.
- [ ] `LICENSE` (MIT), `README.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`.
- [ ] Public API stubs that compile but throw `NotImplementedException` (red TDD baseline).

### 3. TDD — write tests first
- [ ] Emulator fixture: connection string env var, unique database, container helper, readiness retry.
- [ ] Unit tests: serializer, settings factory, local endpoint, options validation, query formatter.
- [ ] Integration tests: context CRUD/batch/audit/soft delete/query/cache/hooks.
- [ ] Integration tests: partition lock acquisition/takeover/fencing/cleanup.
- [ ] Integration tests: locking context lifecycle.
- [ ] Packaging test: inspect `.nupkg` TFMs, docs, licence, symbols.
- [ ] Run the suite: all new tests fail for the expected reason (red).

### 4. Test review #1
- [ ] Independent review of test quality, seam usage, determinism, forbidden mocks.
- [ ] Apply fixes.

### 5. Implementation
- [ ] Contracts, options validation, client provider.
- [ ] `CosmosDbContextBase` save pipeline, audit, delete, patch, hooks.
- [ ] `CosmosDbSet`, feed extensions, serializer, local endpoint support.
- [ ] `CosmosPartitionLock` and `CosmosLockingDbContextBase`.
- [ ] Run the suite until green on the emulator.

### 6. Test review #2
- [ ] Independent review after implementation (coverage gaps, edge cases, flakiness).
- [ ] Apply fixes and re-run.

### 7. Human in the loop
- [ ] Present the public API, test results, and remaining risks for approval.

### 8. Documentation
- [ ] README quick start and concepts.
- [ ] DocFX site: index, articles, API metadata, toc.
- [ ] Migration guides for legacy host and legacy host.
- [ ] Release/versioning/NuGet trusted-publishing runbook.

### 9. CI/CD and publishing
- [ ] CI workflow: build + unit tests on net8/net10 + pack.
- [ ] Integration workflow: Cosmos emulator service container + net10 suite.
- [ ] Docs workflow: DocFX build + GitHub Pages deploy.
- [ ] Publish workflow: tag, MinVer, `NuGet/login@v1` OIDC, push + provenance.
- [ ] Dependabot, CodeQL, issue/PR templates.
- [ ] NuGet trusted-publishing policy instructions (manual account step).
- [ ] Dry-run pack locally and validate the `.nupkg`.
- [ ] Commit and push repository; verify workflows on GitHub.

## Phase log

| Date | Phase | Result |
| --- | --- | --- |
| 2026-11-05 | 0 | Skills installed; spec decisions confirmed; emulator verified on :8281. |
| 2026-11-05 | 1 | Spec written and open questions resolved. |

## Risks / watch list

- Cosmos vNext emulator is preview; no stored procedures, triggers, or UDFs. Tests stay on plain
  documents, batches, patches, and locks.
- Lock tests must use short lease/acquisition durations; the production defaults are 2 min / 30 s.
- `TypeNameHandling.Auto` is a compatibility requirement for existing polymorphic documents and is a
  security risk for untrusted input; documented in README/SECURITY.
- legacy host `Entity.Etag` is currently private; migration requires making it public. Documented only, no
  host edits in this repository.
- Integration suite requires Docker locally; CI provides the service container.
