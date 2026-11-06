using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Time.Testing;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextDeletePatchTests : IntegrationContextTestBase
{
    private static readonly DateTimeOffset DeletedAt = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);

    public ContextDeletePatchTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-delete";

    [Fact]
    public async Task SoftDeletePatchesAuditFieldsKeepsDocumentAndUpdatesEtag()
    {
        var partitionKey = NewPartition();
        var clock = new FakeTimeProvider(DeletedAt);
        var options = Options(partitionKey, o => o with { TimeProvider = clock });
        var user = TestUser("deleter");
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "soft" });
        var etagBeforeDelete = entity.Etag;

        TestEntity? tracked;
        await using (var context = new TestDbContext(options, user))
        {
            tracked = await context.Entities.FindAsync(entity.Id, Cancellation);
            Assert.NotNull(tracked);

            context.Delete(tracked);
            await context.SaveChangesAsync(Cancellation);
        }

        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal(DeletedAt, response.Resource.DeletedDate.GetValueOrDefault());
        Assert.Equal(user, response.Resource.DeletedById);
        Assert.NotEqual(etagBeforeDelete, response.ETag);
        Assert.NotNull(tracked);
        Assert.Equal(response.ETag, tracked.Etag);
        Assert.Equal(1, await CountPartitionAsync(partitionKey));

        await using var verify = new TestDbContext(options);
        var found = await verify.Entities.FindAsync(entity.Id, Cancellation);
        Assert.NotNull(found);
        Assert.NotNull(found.DeletedDate);
    }

    [Fact]
    public async Task EnableSoftDeleteFalseHardDeletesByDefault()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey, o => o with { EnableSoftDelete = false });
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "hard-by-option" });

        await using (var context = new TestDbContext(options, TestUser()))
        {
            var tracked = await context.Entities.FindAsync(entity.Id, Cancellation);
            Assert.NotNull(tracked);

            context.Delete(tracked);
            await context.SaveChangesAsync(Cancellation);

            Assert.Null(await context.Entities.FindAsync(entity.Id, Cancellation));
        }

        var exception = await Assert.ThrowsAsync<CosmosException>(
            () => ReadRawAsync(entity.Id, partitionKey));
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task SoftDeleteKeepsTheCachedInstanceAndRefreshesItsEtag()
    {
        var partitionKey = NewPartition();
        var clock = new FakeTimeProvider(DeletedAt);
        var options = Options(partitionKey, o => o with { TimeProvider = clock });
        var user = TestUser("cache-deleter");
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "cached-soft" });

        await using var context = new TestDbContext(options, user);
        var tracked = await context.Entities.FindAsync(entity.Id, Cancellation);
        Assert.NotNull(tracked);
        var etagBeforeDelete = tracked.Etag;

        context.Delete(tracked);
        await context.SaveChangesAsync(Cancellation);

        var cached = await context.Entities.FindAsync(entity.Id, Cancellation);
        Assert.Same(tracked, cached);
        Assert.NotEqual(etagBeforeDelete, cached!.Etag);

        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal(DeletedAt, response.Resource.DeletedDate.GetValueOrDefault());
        Assert.Equal(user, response.Resource.DeletedById);
    }

    [Fact]
    public async Task HardDeleteRemovesDocumentAndEvictsCache()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "hard" });

        await using (var context = new TestDbContext(options, TestUser()))
        {
            var tracked = await context.Entities.FindAsync(entity.Id, Cancellation);
            Assert.NotNull(tracked);
            Assert.NotNull(await context.Entities.FindAsync(entity.Id, Cancellation));

            context.Delete(tracked, useSoftDeleteIfPossible: false);
            await context.SaveChangesAsync(Cancellation);

            Assert.Null(await context.Entities.FindAsync(entity.Id, Cancellation));
        }

        var exception = await Assert.ThrowsAsync<CosmosException>(
            () => ReadRawAsync(entity.Id, partitionKey));
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task PatchChangesOnlyRequestedProperties()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var entity = await CreateSeedAsync(
            options,
            new TestEntity { Name = "original", Value = 5, Kind = TestKind.Standard });

        await using (var context = new TestDbContext(options, TestUser()))
        {
            var tracked = await context.Entities.FindAsync(entity.Id, Cancellation);
            Assert.NotNull(tracked);

            context.Patch(tracked, [PatchOperation.Set("/Name", "patched")]);
            await context.SaveChangesAsync(Cancellation);
        }

        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal("patched", response.Resource.Name);
        Assert.Equal(5, response.Resource.Value);
        Assert.Equal(TestKind.Standard, response.Resource.Kind);
        Assert.Null(response.Resource.DeletedDate);
        Assert.Null(response.Resource.DeletedById);
    }

    [Fact]
    public async Task PatchByIdChangesOnlyRequestedProperties()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var entity = await CreateSeedAsync(
            options,
            new TestEntity { Name = "original", Value = 5 });

        await using (var context = new TestDbContext(options, TestUser()))
        {
            context.Patch(entity.Id, [PatchOperation.Set("/Value", 42)]);
            await context.SaveChangesAsync(Cancellation);
        }

        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal("original", response.Resource.Name);
        Assert.Equal(42, response.Resource.Value);
    }
}
