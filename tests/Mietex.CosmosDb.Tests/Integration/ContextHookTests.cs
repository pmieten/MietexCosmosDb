using System.Net;
using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextHookTests : IntegrationContextTestBase
{
    public ContextHookTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-hooks";

    [Fact]
    public async Task DisposedContextRejectsAddAndSave()
    {
        var context = new HookTestDbContext(Options(NewPartition()), TestUser());
        await context.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => context.Add(new TestEntity()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => context.SaveChangesAsync(Cancellation));
    }

    [Fact]
    public async Task ValidateBatchOperationCountReceivesPendingCountAndSkipsNoOpSave()
    {
        await using var context = new HookTestDbContext(Options(NewPartition()), TestUser());
        context.Add(new TestEntity { Name = "one" });
        context.Add(new TestEntity { Name = "two" });
        context.Add(new TestEntity { Name = "three" });

        await context.SaveChangesAsync(Cancellation);

        Assert.Equal(new[] { 3 }, context.ValidatedCounts);

        await context.SaveChangesAsync(Cancellation);

        Assert.Single(context.ValidatedCounts);
        Assert.Equal(new[] { true, true }, context.SaveFinished);
    }

    [Fact]
    public async Task PrepareBatchCanAppendAnExtraOperationToTheSameTransaction()
    {
        var partitionKey = NewPartition();
        var options = Options(partitionKey);
        var toDelete = await CreateSeedAsync(options, new TestEntity { Name = "appended-delete" });
        var primary = new TestEntity { Name = "primary" };

        await using var context = new HookTestDbContext(options, TestUser())
        {
            OnPrepareBatch = batch => batch.DeleteItem(toDelete.Id),
        };

        context.Add(primary);
        await context.SaveChangesAsync(Cancellation);

        Assert.Equal(1, context.PrepareBatchCalls);
        var primaryDocument = await ReadRawAsync(primary.Id, partitionKey);
        Assert.Equal("primary", primaryDocument.Resource.Name);

        var deletedException = await Assert.ThrowsAsync<CosmosException>(
            () => ReadRawAsync(toDelete.Id, partitionKey));
        Assert.Equal(HttpStatusCode.NotFound, deletedException.StatusCode);
    }

    [Fact]
    public async Task OnBatchCommittedRunsOnlyOnSuccessfulBatch()
    {
        await using var context = new HookTestDbContext(Options(NewPartition()), TestUser());

        context.Add(new TestEntity { Name = "success" });
        await context.SaveChangesAsync(Cancellation);
        Assert.Equal(1, context.CommittedCalls);

        await context.SaveChangesAsync(Cancellation);
        Assert.Equal(1, context.CommittedCalls);

        var duplicateId = Guid.NewGuid().ToString("N");
        context.Add(new TestEntity { Id = duplicateId });
        context.Add(new TestEntity { Id = duplicateId });
        await Assert.ThrowsAsync<CosmosBatchException>(() => context.SaveChangesAsync(Cancellation));

        Assert.Equal(1, context.CommittedCalls);
    }

    [Fact]
    public async Task OnSaveFinishedRunsForSuccessFailureAndNoOp()
    {
        await using var context = new HookTestDbContext(Options(NewPartition()), TestUser());

        await context.SaveChangesAsync(Cancellation);

        context.Add(new TestEntity { Name = "success" });
        await context.SaveChangesAsync(Cancellation);

        var duplicateId = Guid.NewGuid().ToString("N");
        context.Add(new TestEntity { Id = duplicateId });
        context.Add(new TestEntity { Id = duplicateId });
        await Assert.ThrowsAsync<CosmosBatchException>(() => context.SaveChangesAsync(Cancellation));

        Assert.Equal(new[] { true, true, false }, context.SaveFinished);
    }

    [Fact]
    public async Task ThrowingOnBatchCommittedDoesNotTurnACommittedSaveIntoFailure()
    {
        var partitionKey = NewPartition();
        var entity = new TestEntity { Name = "committed" };

        await using var context = new HookTestDbContext(Options(partitionKey), TestUser())
        {
            ThrowOnCommitted = true,
        };

        context.Add(entity);
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(Cancellation));

        Assert.Null(exception);
        Assert.Equal(1, context.CommittedCalls);
        Assert.Equal(new[] { true }, context.SaveFinished);

        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal("committed", response.Resource.Name);
    }

    [Fact]
    public async Task ThrowingOnSaveFinishedDoesNotTurnASuccessfulSaveIntoFailure()
    {
        var partitionKey = NewPartition();
        var entity = new TestEntity { Name = "saved" };

        await using var context = new HookTestDbContext(Options(partitionKey), TestUser())
        {
            ThrowOnSaveFinished = true,
        };

        context.Add(entity);
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(Cancellation));

        Assert.Null(exception);
        var response = await ReadRawAsync(entity.Id, partitionKey);
        Assert.Equal("saved", response.Resource.Name);
    }
}
