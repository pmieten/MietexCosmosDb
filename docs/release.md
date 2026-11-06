# Release and NuGet publishing

## Versioning

Versioning uses [MinVer](https://github.com/adamralph/minver) with tag prefix `v`. A tag such as
`v0.1.0` produces package version `0.1.0`. Tags must be created from `main` after CI is green.

## One-time NuGet Trusted Publishing setup

No long-lived NuGet API key is stored in GitHub.

1. Sign in to nuget.org as the package owner (`pmieten`).
2. Open **Account settings → Trusted Publishing**.
3. Create a policy with:
   - repository owner: `pmieten`
   - repository: `MietexCosmosDb`
   - workflow file: `publish.yml`
   - environment: `release`
4. Create a protected GitHub environment named `release` with the required reviewers.

The first publish leaves the policy partially active until the workflow successfully exchanges an
OIDC token; after that it is fully active.

## Publish a release

1. Ensure `main` is green: CI, Integration, Docs, and CodeQL.
2. Update `CHANGELOG.md`.
3. Create and push a tag:

   ```shell
   git tag v0.1.0
   git push origin v0.1.0
   ```

4. `publish.yml` runs on the tag:
   - restores and runs unit tests;
   - packs with MinVer;
   - calls `NuGet/login@v1` to exchange a GitHub OIDC token for a short-lived API key;
   - pushes `.nupkg` and `.snupkg` to nuget.org.

5. Verify the package on nuget.org, the symbol package, SourceLink navigation, and the GitHub
   release notes.

## Manual dry run

```shell
dotnet pack src/Mietex.CosmosDb/Mietex.CosmosDb.csproj -c Release -o artifacts
dotnet test -f net10.0 --filter Category=Packaging
```

Do not push manually from a workstation; trusted publishing is the supported path.

## Rollback

NuGet does not support deleting a published version safely. If a release is broken:

1. Unlist the version on nuget.org.
2. Publish a new patch version.
3. Document the issue in `CHANGELOG.md` and the GitHub release.
