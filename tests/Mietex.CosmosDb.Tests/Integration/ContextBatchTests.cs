using System.Net;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextBatchTests : IntegrationContextTestBase
{
    public ContextBatchTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-batch";

    [Fact]
    public async Task MixedBatchCommitsAtomicallyAndWritesBackEtagsAtTheCorrectIndexes()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey, o => o with { UpdateMode = CosmosDbUpdateMode.Replace });
        var replacedSeed = await CreateSeedAsync(options, new TestEntity { Name = "replace-me" });
        var patchedSeed = await CreateSeedAsync(options, new TestEntity { Name = "patch-me" });
        var deletedSeed = await CreateSeedAsync(options, new TestEntity { Name = "delete-me" });

        TestEntity replaced = null!;
        TestEntity created = null!;
        TestEntity patched = null!;

        await using (var context = new TestDbContext(options, TestUser()))
        {
            var existing = await context.Entities.FindAsync(replacedSeed.Id, Cancellation);
            Assert.NotNull(existing);
            var toPatch = await context.Entities.FindAsync(patchedSeed.Id, Cancellation);
            Assert.NotNull(toPatch);
            var toDelete = await context.Entities.FindAsync(deletedSeed.Id, Cancellation);
            Assert.NotNull(toDelete);

            replaced = existing;
            replaced.Name = "replaced";
            created = new TestEntity { Name = "created" };
            patched = toPatch;

            context.Add(replaced);
            context.Add(created);
            context.Delete(toDelete, useSoftDeleteIfPossible: false);
            context.Patch(toPatch, [PatchOperation.Set("/Name", "patched")]);

            await context.SaveChangesAsync(Cancellation);

            Assert.NotNull(replaced.Etag);
            Assert.NotNull(created.Etag);
            Assert.NotNull(patched.Etag);
        }

        var replacedDocument = await ReadRawAsync(replaced.Id, partitionKey);
        var createdDocument = await ReadRawAsync(created.Id, partitionKey);
        var patchedDocument = await ReadRawAsync(patchedSeed.Id, partitionKey);

        Assert.Equal("replaced", replacedDocument.Resource.Name);
        Assert.Equal("created", createdDocument.Resource.Name);
        Assert.Equal("patched", patchedDocument.Resource.Name);
        Assert.Equal(replacedDocument.ETag, replaced.Etag);
        Assert.Equal(createdDocument.ETag, created.Etag);
        Assert.Equal(patchedDocument.ETag, patched.Etag);
        Assert.Distinct(new[] { replaced.Etag, created.Etag, patched.Etag });
        Assert.Equal(3, await CountPartitionAsync(partitionKey));

        var deletedException = await Assert.ThrowsAsync<CosmosException>(
            () => ReadRawAsync(deletedSeed.Id, partitionKey));
        Assert.Equal(HttpStatusCode.NotFound, deletedException.StatusCode);
    }

    [Fact]
    public async Task InvalidPatchPathFailsWithTypedExceptionAndNoPartialWrite()
    {
        var partitionKey = NewPartition();

        await using (var context = new TestDbContext(Options(partitionKey), TestUser()))
        {
            var entity = new TestEntity { Name = "valid" };
            context.Add(entity);
            context.Patch(entity.Id, [PatchOperation.Remove("/DoesNotExist")]);

            var exception = await Assert.ThrowsAsync<CosmosBatchException>(
                () => context.SaveChangesAsync(Cancellation));

            Assert.Equal(2, exception.OperationCount);
            Assert.Contains(
                exception.Failures,
                failure => failure.Index == 1 && failure.StatusCode == HttpStatusCode.BadRequest);
        }

        Assert.Equal(0, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task DuplicateCreateFailsWithTypedExceptionAndNoPartialWrite()
    {
        var partitionKey = NewPartition();
        var id = Guid.NewGuid().ToString("N");

        await using (var context = new TestDbContext(Options(partitionKey), TestUser()))
        {
            context.Add(new TestEntity { Id = id, Name = "first" });
            context.Add(new TestEntity { Id = id, Name = "duplicate" });

            var exception = await Assert.ThrowsAsync<CosmosBatchException>(
                () => context.SaveChangesAsync(Cancellation));

            Assert.Equal(2, exception.OperationCount);
            Assert.Contains(
                exception.Failures,
                failure => failure.Index == 1 && failure.StatusCode == HttpStatusCode.Conflict);
            Assert.False(string.IsNullOrWhiteSpace(exception.Diagnostics));
            Assert.False(string.IsNullOrWhiteSpace(exception.ActivityId));
            Assert.True(exception.RequestCharge > 0);
        }

        Assert.Equal(0, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task HundredOperationsInOneBatchSucceed()
    {
        var partitionKey = NewPartition();

        await using (var context = new TestDbContext(Options(partitionKey), TestUser()))
        {
            for (var index = 0; index < 100; index++)
            {
                context.Add(new TestEntity { Name = $"entity-{index}" });
            }

            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(100, await CountPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task AboveBatchLimitFailsBeforeAnyRequest()
    {
        var partitionKey = NewPartition();

        await using (var context = new TestDbContext(Options(partitionKey), TestUser()))
        {
            for (var index = 0; index < 101; index++)
            {
                context.Add(new TestEntity { Name = $"entity-{index}" });
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Cancellation));
        }

        Assert.Equal(0, await CountPartitionAsync(partitionKey));
    }
}
